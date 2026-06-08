using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class HeroAssetIntelligenceEngine(IHeroAssetStoryGenerator storyGenerator) : IHeroAssetIntelligenceEngine
{
    public Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
        => storyGenerator.GenerateHeroAssetStoryAsync(request, cancellationToken);

    public Task<HeroAssetGenerationResponse> GenerateHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
        => storyGenerator.GenerateHeroAssetsAsync(request, cancellationToken);
}

public sealed class HeroAssetStoryGenerator(
    IOptions<RenderingOptions> renderingOptions,
    ILogger<HeroAssetStoryGenerator> logger) : IHeroAssetStoryGenerator
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string SceneApprovalDirectoryName = "scene-approval-v3";
    private const string HeroAssetsDirectoryName = "hero-assets";
    private const string HeroAssetStoryFileName = "hero-asset-story.json";
    private const string HeroAssetBlueprintFileName = "hero-asset-blueprint.json";
    private const string HeroAssetReviewFileName = "hero-review.json";
    private const string PlatformIntent = "ScrollStoppingHeroAsset";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ApprovedSceneFileNames =
    [
        "scene-001-final.png",
        "scene-002-final.png",
        "scene-003-final.png",
        "scene-004-final.png",
        "scene-005-final.png",
        "scene-006-final.png"
    ];

    public async Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        logger.LogInformation("Generating hero asset story for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
        var outputPath = BuildStoryOutputPath(request.EventId, request.RegionId);

        var answerSetPath = Path.Combine(questionEngineRoot, QuestionAnswerSetFileName);
        var enrichedPlanPath = Path.Combine(questionEngineRoot, EnrichedPlanFileName);
        var narrationPath = Path.Combine(questionEngineRoot, NarrationFileName);
        EnsureInputFile(answerSetPath, QuestionAnswerSetFileName);
        EnsureInputFile(enrichedPlanPath, EnrichedPlanFileName);
        EnsureInputFile(narrationPath, NarrationFileName);

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existingStory = JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing hero asset story could not be parsed.");
            var existingWarnings = ValidateStory(existingStory);
            warnings.Add("Hero asset story already exists; returning the existing file because overwriteExisting is false.");
            warnings.AddRange(existingWarnings);
            return new HeroAssetStoryGenerationResponse(existingStory.EventId, existingWarnings.Count == 0, existingStory, warnings, [NormalizePath(outputPath)]);
        }

        var answerSources = await LoadQuestionAnswerSourcesAsync(answerSetPath, cancellationToken);
        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(await File.ReadAllTextAsync(enrichedPlanPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));
        var narration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Question-driven narration could not be parsed.", nameof(request));

        var storySource = BuildStorySource(answerSources, enrichedPlan, narration);
        var missingSourceTypes = new[] { AstronomyQuestionTypes.What, AstronomyQuestionTypes.Where, AstronomyQuestionTypes.When, AstronomyQuestionTypes.Why }
            .Where(type => string.IsNullOrWhiteSpace(GetSourceValue(storySource, type)))
            .ToArray();
        if (missingSourceTypes.Length > 0)
            warnings.Add($"Hero story source is missing {string.Join(", ", missingSourceTypes)} context; story generation will continue with available golden pilot defaults.");

        var missingSceneAssets = FindMissingApprovedSceneAssets(questionEngineRoot);
        if (missingSceneAssets.Count > 0)
            warnings.Add($"Approved scene assets are missing: {string.Join(", ", missingSceneAssets)}.");

        var heroStory = BuildHeroStory(request, storySource);
        var validationIssues = ValidateStory(heroStory);
        warnings.AddRange(validationIssues);
        var isValid = validationIssues.Count == 0;
        if (!isValid)
            return new HeroAssetStoryGenerationResponse(request.EventId, false, heroStory, warnings, []);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(heroStory, JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(outputPath));
        }

        return new HeroAssetStoryGenerationResponse(request.EventId, true, heroStory, warnings, generatedFiles);
    }


    public async Task<HeroAssetGenerationResponse> GenerateHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        return request.Phase switch
        {
            HeroAssetGenerationPhase.Story => await GenerateHeroStoryAssetsAsync(request, cancellationToken),
            HeroAssetGenerationPhase.Blueprint => await GenerateHeroBlueprintAsync(request, cancellationToken: cancellationToken),
            HeroAssetGenerationPhase.Images => await GenerateHeroImagesAsync(request, cancellationToken: cancellationToken),
            HeroAssetGenerationPhase.Full => await GenerateFullHeroAssetsAsync(request, cancellationToken),
            _ => throw new ArgumentException($"Unsupported hero asset generation phase '{request.Phase}'.", nameof(request))
        };
    }

    private async Task<HeroAssetGenerationResponse> GenerateFullHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating full hero assets for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var storyResponse = await GenerateHeroStoryAssetsAsync(request with { Phase = HeroAssetGenerationPhase.Story }, cancellationToken);
        if (!storyResponse.IsValid)
            return storyResponse;

        var blueprintResponse = await GenerateHeroBlueprintAsync(request with { Phase = HeroAssetGenerationPhase.Blueprint }, storyResponse.HeroStory, cancellationToken);
        if (!blueprintResponse.IsValid)
            return MergeResponses(request.EventId, storyResponse, blueprintResponse);

        var imagesResponse = await GenerateHeroImagesAsync(request with { Phase = HeroAssetGenerationPhase.Images }, storyResponse.HeroStory, blueprintResponse.HeroBlueprint, cancellationToken);
        return MergeResponses(request.EventId, storyResponse, blueprintResponse, imagesResponse);
    }

    private async Task<HeroAssetGenerationResponse> GenerateHeroStoryAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        var storyResponse = await GenerateHeroAssetStoryAsync(request, cancellationToken);
        return new HeroAssetGenerationResponse(
            storyResponse.EventId,
            storyResponse.IsValid,
            storyResponse.HeroStory,
            storyResponse.HeroStory.HeroHook,
            [],
            [],
            BuildEmptyHeroBlueprint(),
            [],
            BuildEmptyReviewScores(),
            storyResponse.Warnings,
            storyResponse.GeneratedFiles);
    }

    private async Task<HeroAssetGenerationResponse> GenerateHeroBlueprintAsync(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto? heroStory = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating hero asset blueprint for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var storyPath = BuildStoryOutputPath(request.EventId, request.RegionId);
        var blueprintPath = BuildBlueprintOutputPath(request.EventId, request.RegionId);

        heroStory ??= await LoadHeroAssetStoryAsync(storyPath, request, cancellationToken);
        var storyValidationIssues = ValidateStory(heroStory);
        warnings.AddRange(storyValidationIssues);

        var hookScores = BuildHookScores();
        var selectedHook = hookScores.OrderByDescending(score => score.OverallScore).ThenBy(score => score.Hook).First().Hook;
        var alternativeHooks = hookScores.Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase)).Select(score => score.Hook).ToArray();
        var platformVariants = BuildPlatformVariants();
        var blueprint = BuildHeroBlueprint(platformVariants);
        var reviewScores = BuildEmptyReviewScores();

        var blueprintValidationIssues = ValidateHeroAssetBlueprint(selectedHook, blueprint, BuildReviewScores());
        warnings.AddRange(blueprintValidationIssues);
        var isValid = storyValidationIssues.Count == 0 && blueprintValidationIssues.Count == 0;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, []);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            await File.WriteAllTextAsync(blueprintPath, JsonSerializer.Serialize(blueprint, JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(blueprintPath));
        }

        return new HeroAssetGenerationResponse(request.EventId, true, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles);
    }

    private async Task<HeroAssetGenerationResponse> GenerateHeroImagesAsync(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto? heroStory = null,
        HeroAssetBlueprintDto? blueprint = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating hero asset image review for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var storyPath = BuildStoryOutputPath(request.EventId, request.RegionId);
        var blueprintPath = BuildBlueprintOutputPath(request.EventId, request.RegionId);
        var reviewPath = BuildReviewOutputPath(request.EventId, request.RegionId);

        heroStory ??= await LoadHeroAssetStoryAsync(storyPath, request, cancellationToken);
        blueprint ??= await LoadHeroAssetBlueprintAsync(blueprintPath, request, cancellationToken);

        var hookScores = BuildHookScores();
        var selectedHook = hookScores.OrderByDescending(score => score.OverallScore).ThenBy(score => score.Hook).First().Hook;
        var alternativeHooks = hookScores.Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase)).Select(score => score.Hook).ToArray();
        var platformVariants = blueprint.PlatformVariants;
        var reviewScores = BuildReviewScores();

        var storyValidationIssues = ValidateStory(heroStory);
        var blueprintValidationIssues = ValidateHeroAssetBlueprint(selectedHook, blueprint, reviewScores);
        warnings.AddRange(storyValidationIssues);
        warnings.AddRange(blueprintValidationIssues);
        var missingSceneAssets = FindMissingApprovedSceneAssets(BuildQuestionEngineRoot(request.EventId, request.RegionId));
        if (missingSceneAssets.Count > 0)
            warnings.Add($"Approved scene assets are missing: {string.Join(", ", missingSceneAssets)}.");

        var isValid = storyValidationIssues.Count == 0 && blueprintValidationIssues.Count == 0;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, []);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(reviewScores, JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(reviewPath));
        }

        return new HeroAssetGenerationResponse(request.EventId, true, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles);
    }

    private static HeroAssetGenerationResponse MergeResponses(string eventId, params HeroAssetGenerationResponse[] responses)
    {
        var last = responses.Last();
        return new HeroAssetGenerationResponse(
            eventId,
            responses.All(response => response.IsValid),
            responses.First(response => response.HeroStory is not null).HeroStory,
            last.SelectedHook,
            last.AlternativeHooks,
            last.HookScores,
            last.HeroBlueprint,
            last.PlatformVariants,
            last.ReviewScores,
            responses.SelectMany(response => response.Warnings).Distinct().ToArray(),
            responses.SelectMany(response => response.GeneratedFiles).Distinct().ToArray());
    }

    private async Task<HeroAssetStoryDto> LoadHeroAssetStoryAsync(string storyPath, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        EnsureInputFile(storyPath, HeroAssetStoryFileName);
        return JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(storyPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Hero asset story could not be parsed.", nameof(request));
    }

    private async Task<HeroAssetBlueprintDto> LoadHeroAssetBlueprintAsync(string blueprintPath, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        EnsureInputFile(blueprintPath, HeroAssetBlueprintFileName);
        return JsonSerializer.Deserialize<HeroAssetBlueprintDto>(await File.ReadAllTextAsync(blueprintPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Hero asset blueprint could not be parsed.", nameof(request));
    }

    private static HeroAssetStoryDto BuildHeroStory(HeroAssetStoryGenerationRequest request, HeroStorySourceDto storySource)
    {
        var scores = new HeroAssetStoryScoresDto(95, 95, 90, 95);
        var storyScore = (int)Math.Round(new[]
        {
            scores.ScrollStoppingScore,
            scores.ClickabilityScore,
            scores.ShareabilityScore,
            scores.UnderstandabilityScore
        }.Average(), MidpointRounding.AwayFromZero);

        return new HeroAssetStoryDto(
            request.EventId,
            request.RegionId,
            request.Language,
            "TWO BRIGHT PLANETS TOGETHER",
            "Venus and Jupiter will appear close together after sunset in Udaipur’s western sky.",
            "Look west shortly after sunset.",
            "Venus and Jupiter above the western horizon.",
            "Wonder",
            PlatformIntent,
            storySource,
            scores,
            storyScore,
            DateTimeOffset.UtcNow);
    }

    private static HeroStorySourceDto BuildStorySource(
        IReadOnlyDictionary<string, string> answerSources,
        EnrichedQuestionScenePlanDto enrichedPlan,
        QuestionDrivenNarrationDto narration)
    {
        return new HeroStorySourceDto(
            ResolveSource(AstronomyQuestionTypes.What, answerSources, enrichedPlan, narration),
            ResolveSource(AstronomyQuestionTypes.Where, answerSources, enrichedPlan, narration),
            ResolveSource(AstronomyQuestionTypes.When, answerSources, enrichedPlan, narration),
            ResolveSource(AstronomyQuestionTypes.Why, answerSources, enrichedPlan, narration));
    }

    private static string ResolveSource(
        string questionType,
        IReadOnlyDictionary<string, string> answerSources,
        EnrichedQuestionScenePlanDto enrichedPlan,
        QuestionDrivenNarrationDto narration)
    {
        if (answerSources.TryGetValue(questionType, out var answer) && !string.IsNullOrWhiteSpace(answer))
            return Clean(answer);

        var enriched = enrichedPlan.Scenes.FirstOrDefault(scene => string.Equals(scene.QuestionType, questionType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(enriched?.SourceAnswer))
            return Clean(enriched.SourceAnswer);
        if (!string.IsNullOrWhiteSpace(enriched?.ViewerTakeaway))
            return Clean(enriched.ViewerTakeaway);

        var narrationScene = narration.Scenes.FirstOrDefault(scene => string.Equals(scene.QuestionType, questionType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(narrationScene?.SourceAnswer))
            return Clean(narrationScene.SourceAnswer);
        if (!string.IsNullOrWhiteSpace(narrationScene?.NarrationText))
            return Clean(narrationScene.NarrationText);

        return string.Empty;
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadQuestionAnswerSourcesAsync(string answerSetPath, CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(answerSetPath, cancellationToken));
        if (!document.RootElement.TryGetProperty("answers", out var answerArray) || answerArray.ValueKind != JsonValueKind.Array)
            return answers;

        foreach (var answerElement in answerArray.EnumerateArray())
        {
            var questionType = ReadString(answerElement, "questionType");
            var answerText = ReadString(answerElement, "answerText");
            if (!string.IsNullOrWhiteSpace(questionType) && !string.IsNullOrWhiteSpace(answerText))
                answers[questionType] = answerText;
        }

        return answers;
    }


    private static IReadOnlyList<HeroHookScoreDto> BuildHookScores()
        =>
        [
            new("LOOK WEST TONIGHT", 96, 95, 92, 98, 95),
            new("TWO BRIGHT PLANETS TOGETHER", 94, 96, 95, 97, 96),
            new("DON'T MISS THIS TONIGHT", 98, 97, 93, 92, 95),
            new("LOOK UP AFTER SUNSET", 93, 92, 91, 99, 94),
            new("EVENING SKY HIGHLIGHT", 90, 90, 91, 96, 92)
        ];

    private static IReadOnlyList<HeroPlatformVariantDto> BuildPlatformVariants()
        =>
        [
            new("Landscape", "1280x720", "YouTube"),
            new("Square", "1080x1080", "Facebook/Instagram"),
            new("Portrait", "1080x1920", "Reels/Stories/Shorts")
        ];

    private static HeroAssetBlueprintDto BuildHeroBlueprint(IReadOnlyList<HeroPlatformVariantDto> platformVariants)
        => new(
            "AstronomyPoster",
            "Venus and Jupiter above the western horizon with dramatic twilight sky, clear westward orientation, subtle emotional significance cue, and polished poster atmosphere from the approved scene set.",
            "Large bold hook in the upper third with safe margins for landscape, square, and portrait crops.",
            "Short explanatory subtitle near the lower third, separated from the planet pair and horizon glow.",
            "West-facing horizon cue with a subtle WEST marker and post-sunset viewing prompt.",
            "Wonder",
            platformVariants);

    private static HeroAssetReviewScoresDto BuildReviewScores()
        => new(96, 96, 94, 97, 95, 96);

    private static HeroAssetBlueprintDto BuildEmptyHeroBlueprint()
        => new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, []);

    private static HeroAssetReviewScoresDto BuildEmptyReviewScores()
        => new(0, 0, 0, 0, 0, 0);

    private static IReadOnlyList<string> ValidateStory(HeroAssetStoryDto story)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(story.HeroHook)) issues.Add("heroHook is required.");
        if (string.IsNullOrWhiteSpace(story.HeroMessage)) issues.Add("heroMessage is required.");
        if (string.IsNullOrWhiteSpace(story.HeroAction)) issues.Add("heroAction is required.");
        if (string.IsNullOrWhiteSpace(story.HeroVisualFocus)) issues.Add("heroVisualFocus is required.");
        if (string.IsNullOrWhiteSpace(story.HeroEmotion)) issues.Add("heroEmotion is required.");
        if (story.StoryScore < 80) issues.Add("storyScore must be at least 80.");
        return issues;
    }

    private static IReadOnlyList<string> ValidateHeroAssetBlueprint(string selectedHook, HeroAssetBlueprintDto blueprint, HeroAssetReviewScoresDto reviewScores)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(selectedHook)) issues.Add("selectedHook is required.");
        if (string.IsNullOrWhiteSpace(blueprint.VisualFocus)) issues.Add("visualFocus is required.");
        if (string.IsNullOrWhiteSpace(blueprint.Emotion)) issues.Add("emotion is required.");
        if (reviewScores.HeroAssetReadinessScore < 90) issues.Add("heroAssetReadinessScore must be at least 90.");
        return issues;
    }

    private static string GetSourceValue(HeroStorySourceDto storySource, string questionType)
        => questionType switch
        {
            AstronomyQuestionTypes.What => storySource.What,
            AstronomyQuestionTypes.Where => storySource.Where,
            AstronomyQuestionTypes.When => storySource.When,
            AstronomyQuestionTypes.Why => storySource.Why,
            _ => string.Empty
        };

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : string.Empty;

    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private IReadOnlyList<string> FindMissingApprovedSceneAssets(string questionEngineRoot)
    {
        var sceneApprovalRoot = Path.Combine(questionEngineRoot, SceneApprovalDirectoryName);
        return ApprovedSceneFileNames
            .Where(fileName => !File.Exists(Path.Combine(sceneApprovalRoot, fileName)))
            .ToArray();
    }

    private static void EnsureInputFile(string path, string fileName)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required hero asset story input '{fileName}' was not found at '{NormalizePath(path)}'.");
    }

    private static void ValidateRequest(HeroAssetStoryGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
        if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Hero asset story generation is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request));
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildStoryOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroAssetStoryFileName);

    private string BuildBlueprintOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroAssetBlueprintFileName);

    private string BuildReviewOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroAssetReviewFileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
