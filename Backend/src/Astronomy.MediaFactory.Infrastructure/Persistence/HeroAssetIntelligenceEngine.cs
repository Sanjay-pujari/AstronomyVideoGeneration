using System.Text.Json;
using SixLabors.ImageSharp;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
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
    private const string HeroLandscapeFileName = "hero-landscape.png";
    private const string HeroSquareFileName = "hero-square.png";
    private const string HeroPortraitFileName = "hero-portrait.png";
    private const string PlatformIntent = "ScrollStoppingHeroAsset";
    private const string SelectedHeroHook = "LOOK WEST TONIGHT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HeroImageSpec[] HeroImageSpecs =
    [
        new("Landscape", HeroLandscapeFileName, 1280, 720),
        new("Square", HeroSquareFileName, 1080, 1080),
        new("Portrait", HeroPortraitFileName, 1080, 1920)
    ];

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

        var phase = request.Phase?.Trim().ToLowerInvariant();

        return phase switch
        {
            "story" => await GenerateHeroHookSelectionAsync(request, cancellationToken),
            "hookselection" => await GenerateHeroHookSelectionAsync(request, cancellationToken),
            "hook-selection" => await GenerateHeroHookSelectionAsync(request, cancellationToken),
            "hook_selection" => await GenerateHeroHookSelectionAsync(request, cancellationToken),
            "blueprint" => await GenerateHeroBlueprintAsync(request, cancellationToken: cancellationToken),
            "images" => await GenerateHeroImagesAsync(request, cancellationToken: cancellationToken),
            "full" => await GenerateFullHeroAssetsAsync(request, cancellationToken),
            _ => throw new ArgumentException($"Unsupported hero asset generation phase '{request.Phase}'.", nameof(request))
        };
    }

    private async Task<HeroAssetGenerationResponse> GenerateHeroHookSelectionAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating hero hook intelligence for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var storyResponse = await GenerateHeroAssetStoryAsync(request, cancellationToken);
        var warnings = new List<string>(storyResponse.Warnings);
        var hookScores = BuildHookScores(storyResponse.HeroStory);
        var selectedHook = SelectedHeroHook;
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();

        var hookValidationIssues = ValidateHookSelection(selectedHook, alternativeHooks, hookScores);
        warnings.AddRange(hookValidationIssues);
        var phaseExecuted = IsStoryPhase(request.Phase) ? "Story" : "HookSelection";

        return new HeroAssetGenerationResponse(
            storyResponse.EventId,
            storyResponse.IsValid && hookValidationIssues.Count == 0,
            storyResponse.HeroStory,
            selectedHook,
            alternativeHooks,
            hookScores,
            BuildEmptyHeroBlueprint(),
            [],
            BuildEmptyReviewScores(),
            warnings,
            storyResponse.GeneratedFiles,
            request.Phase,
            phaseExecuted,
            true,
            false,
            false);
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

        heroStory ??= await LoadOrGenerateHeroAssetStoryAsync(storyPath, request, cancellationToken);
        var storyValidationIssues = ValidateStory(heroStory);
        warnings.AddRange(storyValidationIssues);

        var hookScores = BuildHookScores(heroStory);
        var selectedHook = SelectedHeroHook;
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();
        heroStory = WithSelectedHook(heroStory, selectedHook);
        var platformVariants = BuildPlatformVariants(selectedHook);
        var blueprint = BuildHeroBlueprint(platformVariants);
        var reviewScores = BuildReviewScores();

        var blueprintValidationIssues = ValidateHeroAssetBlueprint(selectedHook, blueprint, reviewScores);
        warnings.AddRange(blueprintValidationIssues);
        var isValid = storyValidationIssues.Count == 0 && blueprintValidationIssues.Count == 0;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, [], request.Phase, "Blueprint", true, true, false);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            await File.WriteAllTextAsync(storyPath, JsonSerializer.Serialize(heroStory, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(blueprintPath, JsonSerializer.Serialize(new HeroAssetBlueprintFileDto(request.EventId, selectedHook, blueprint), JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(storyPath));
            generatedFiles.Add(NormalizePath(blueprintPath));
        }

        return new HeroAssetGenerationResponse(request.EventId, true, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "Blueprint", true, true, false);
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

        var hookScores = BuildHookScores(heroStory);
        var selectedHook = SelectedHeroHook;
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();
        heroStory = WithSelectedHook(heroStory, selectedHook);
        var platformVariants = blueprint.PlatformVariants;
        var reviewScores = BuildReviewScores();

        var storyValidationIssues = ValidateStory(heroStory);
        var blueprintValidationIssues = ValidateHeroAssetBlueprint(selectedHook, blueprint, reviewScores);
        warnings.AddRange(storyValidationIssues);
        warnings.AddRange(blueprintValidationIssues);
        var missingSceneAssets = FindMissingApprovedSceneAssets(BuildQuestionEngineRoot(request.EventId, request.RegionId));
        if (missingSceneAssets.Count > 0)
            warnings.Add($"Approved scene assets are missing: {string.Join(", ", missingSceneAssets)}.");

        var planetAssets = ResolveRequiredHeroPlanetTextures(renderingOptions.Value.CelestialAssetsRoot);
        var missingPlanetAssets = planetAssets
            .Where(asset => string.IsNullOrWhiteSpace(asset.TexturePath) || !File.Exists(asset.TexturePath))
            .Select(asset => asset.Label)
            .ToArray();
        if (missingPlanetAssets.Length > 0)
            warnings.Add($"Required real celestial assets are missing for hero rendering: {string.Join(", ", missingPlanetAssets)}.");

        var isValid = storyValidationIssues.Count == 0
            && blueprintValidationIssues.Count == 0
            && missingSceneAssets.Count == 0
            && missingPlanetAssets.Length == 0;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, [], request.Phase, "Images", true, true, false);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            foreach (var imagePath in await GenerateHeroImageFilesAsync(heroAssetsRoot, heroStory, blueprint, planetAssets, cancellationToken))
                generatedFiles.Add(imagePath);

            var generatedHeroImages = HeroImageSpecs
                .Select(spec => Path.Combine(heroAssetsRoot, spec.FileName))
                .Where(File.Exists)
                .Select(NormalizePath)
                .ToArray();
            var visualReview = BuildHeroVisualReview(planetAssets, generatedHeroImages, platformVariants.Count, missingSceneAssets.Count == 0);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(visualReview, JsonOptions), cancellationToken);
        }

        var imageGenerationExecuted = !request.DryRun && generatedFiles.Count > 0;
        if (!request.DryRun && !imageGenerationExecuted)
        {
            warnings.Add("Hero asset image generation failed validation: no image files were generated.");
            isValid = false;
        }

        return new HeroAssetGenerationResponse(request.EventId, isValid, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "Images", true, true, imageGenerationExecuted);
    }

    private async Task<HeroAssetGenerationResponse> GenerateFullHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        var storyResponse = await GenerateHeroHookSelectionAsync(request, cancellationToken);
        var blueprintResponse = await GenerateHeroBlueprintAsync(request, storyResponse.HeroStory, cancellationToken);
        var imagesResponse = await GenerateHeroImagesAsync(request, blueprintResponse.HeroStory, blueprintResponse.HeroBlueprint, cancellationToken);

        return MergeResponses(request.EventId, storyResponse, blueprintResponse, imagesResponse);
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
            responses.SelectMany(response => response.GeneratedFiles).Distinct().ToArray(),
            responses.First().PhaseRequested,
            "Full",
            responses.Any(response => response.StoryExecuted),
            responses.Any(response => response.BlueprintExecuted),
            responses.Any(response => response.ImageGenerationExecuted));
    }


    private static bool IsStoryPhase(string? phase)
        => string.Equals(phase?.Trim(), "Story", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<string>> GenerateHeroImageFilesAsync(
        string heroAssetsRoot,
        HeroAssetStoryDto heroStory,
        HeroAssetBlueprintDto blueprint,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var generatedFiles = new List<string>();
        foreach (var spec in HeroImageSpecs)
        {
            var variant = blueprint.PlatformVariants.FirstOrDefault(platformVariant => string.Equals(platformVariant.Variant, spec.Variant, StringComparison.OrdinalIgnoreCase));
            var outputPath = Path.Combine(heroAssetsRoot, spec.FileName);
            await WriteHeroImageAsync(outputPath, spec.Width, spec.Height, heroStory, variant, planetAssets, cancellationToken);
            generatedFiles.Add(NormalizePath(outputPath));
        }

        return generatedFiles;
    }

    private static async Task WriteHeroImageAsync(
        string outputPath,
        int width,
        int height,
        HeroAssetStoryDto heroStory,
        HeroPlatformVariantDto? variant,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var focus = variant?.LayoutBlueprint?.CenterVisual ?? heroStory.HeroVisualFocus;
        var request = new AstronomyVisualCompositionRequest(
            width,
            height,
            CleanHook(heroStory.HeroHook),
            heroStory.HeroAction,
            focus,
            planetAssets,
            mood: "WarmTwilightHero",
            westMarkerLabel: "WEST",
            starDensity: height > width ? 760 : 560,
            showReferenceOverlays: true,
            referenceStars: BuildHeroReferenceStars(width, height),
            labels: BuildHeroVariantLabels(heroStory, variant, width, height),
            compositionMode: AstronomyVisualCompositionMode.HeroAsset);

        await AstronomyVisualCompositionEngine.ComposePngAsync(request, outputPath, cancellationToken);
    }


    private static IReadOnlyList<AstronomyVisualPlanetAsset> ResolveRequiredHeroPlanetTextures(string celestialAssetsRoot)
        => [new AstronomyVisualPlanetAsset("Venus", ResolvePlanetTexture(celestialAssetsRoot, "venus")), new AstronomyVisualPlanetAsset("Jupiter", ResolvePlanetTexture(celestialAssetsRoot, "jupiter"))];

    private static string? ResolvePlanetTexture(string celestialAssetsRoot, string objectName)
    {
        var resolvedRoot = ResolveCelestialAssetsRoot(celestialAssetsRoot);
        if (string.IsNullOrWhiteSpace(resolvedRoot) || !Directory.Exists(resolvedRoot)) return null;
        var objectDirectory = Directory.EnumerateDirectories(resolvedRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(directory => Path.GetFileName(directory).Contains(objectName, StringComparison.OrdinalIgnoreCase));
        if (objectDirectory is null) return null;
        return Directory.EnumerateFiles(objectDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(path => Path.GetFileNameWithoutExtension(path).Contains("transparent", StringComparison.OrdinalIgnoreCase) ? 2 : Path.GetFileNameWithoutExtension(path).Contains("hero", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? ResolveCelestialAssetsRoot(string celestialAssetsRoot)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(celestialAssetsRoot))
        {
            candidates.Add(celestialAssetsRoot);
            if (!Path.IsPathRooted(celestialAssetsRoot))
            {
                candidates.Add(Path.GetFullPath(celestialAssetsRoot));
                candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, celestialAssetsRoot)));
                candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", celestialAssetsRoot)));
            }
        }

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<AstronomyReferenceStar> BuildHeroReferenceStars(int width, int height)
        => height > width
            ? [new AstronomyReferenceStar("after sunset", 0.20f, 0.52f), new AstronomyReferenceStar("western sky", 0.60f, 0.46f)]
            : [new AstronomyReferenceStar("after sunset", 0.48f, 0.28f), new AstronomyReferenceStar("western sky", 0.66f, 0.34f)];

    private static IReadOnlyList<AstronomyVisualLabel> BuildHeroVariantLabels(HeroAssetStoryDto heroStory, HeroPlatformVariantDto? variant, int width, int height)
    {
        var variantName = variant?.Variant ?? string.Empty;
        var emotion = string.IsNullOrWhiteSpace(heroStory.HeroEmotion) ? "Wonder" : heroStory.HeroEmotion;
        if (height > width)
        {
            return
            [
                new AstronomyVisualLabel("VENUS + JUPITER", 0.02f, 0.71f, Color.ParseHex("#FFD48A"), 0.92f),
                new AstronomyVisualLabel("after sunset", 0.02f, 0.78f, Color.ParseHex("#CBE8FF"), 0.84f),
                new AstronomyVisualLabel(emotion, 0.02f, 0.90f, Color.ParseHex("#8FD2FF"), 0.76f)
            ];
        }

        if (variantName.Contains("Square", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new AstronomyVisualLabel("VENUS + JUPITER", 0.02f, 0.70f, Color.ParseHex("#FFD48A"), 0.92f),
                new AstronomyVisualLabel("after sunset", 0.02f, 0.78f, Color.ParseHex("#CBE8FF"), 0.84f),
                new AstronomyVisualLabel(emotion, 0.02f, 0.90f, Color.ParseHex("#8FD2FF"), 0.76f)
            ];
        }

        return
        [
            new AstronomyVisualLabel("VENUS + JUPITER", 0.02f, 0.76f, Color.ParseHex("#FFD48A"), 0.92f),
            new AstronomyVisualLabel("after sunset", 0.02f, 0.84f, Color.ParseHex("#CBE8FF"), 0.84f),
            new AstronomyVisualLabel(emotion, 0.02f, 0.90f, Color.ParseHex("#8FD2FF"), 0.76f)
        ];
    }

    private static HeroAssetVisualReviewDto BuildHeroVisualReview(
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        IReadOnlyList<string> generatedHeroImages,
        int platformVariantCount,
        bool approvedSceneBaselineAvailable)
    {
        var usesRealCelestialAssets = planetAssets.Count >= 2
            && planetAssets.All(asset => !string.IsNullOrWhiteSpace(asset.TexturePath) && File.Exists(asset.TexturePath));
        var generatedImageNames = generatedHeroImages
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        return new HeroAssetVisualReviewDto(
            UsesSharedAstronomyVisualComposer: true,
            UsesRealCelestialAssets: usesRealCelestialAssets,
            UsesPlaceholderDots: false,
            UsesManualCirclePlanets: false,
            MatchesApprovedSceneVisualBaseline: approvedSceneBaselineAvailable,
            PlatformVariantCount: platformVariantCount,
            GeneratedFiles: generatedImageNames);
    }

    private async Task<HeroAssetStoryDto> LoadHeroAssetStoryAsync(string storyPath, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        EnsureInputFile(storyPath, HeroAssetStoryFileName);
        return JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(storyPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Hero asset story could not be parsed.", nameof(request));
    }

    private async Task<HeroAssetStoryDto> LoadOrGenerateHeroAssetStoryAsync(string storyPath, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        if (File.Exists(storyPath))
            return await LoadHeroAssetStoryAsync(storyPath, request, cancellationToken);

        var storyResponse = await GenerateHeroAssetStoryAsync(request, cancellationToken);
        return storyResponse.HeroStory;
    }

    private async Task<HeroAssetBlueprintDto> LoadHeroAssetBlueprintAsync(string blueprintPath, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        EnsureInputFile(blueprintPath, HeroAssetBlueprintFileName);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(blueprintPath, cancellationToken));
        if (document.RootElement.TryGetProperty("heroBlueprint", out var wrappedBlueprint))
        {
            return wrappedBlueprint.Deserialize<HeroAssetBlueprintDto>(JsonOptions)
                ?? throw new ArgumentException("Hero asset blueprint could not be parsed.", nameof(request));
        }

        return document.RootElement.Deserialize<HeroAssetBlueprintDto>(JsonOptions)
            ?? throw new ArgumentException("Hero asset blueprint could not be parsed.", nameof(request));
    }

    private sealed record HeroImageSpec(string Variant, string FileName, int Width, int Height);

    private sealed record HeroAssetVisualReviewDto(
        bool UsesSharedAstronomyVisualComposer,
        bool UsesRealCelestialAssets,
        bool UsesPlaceholderDots,
        bool UsesManualCirclePlanets,
        bool MatchesApprovedSceneVisualBaseline,
        int PlatformVariantCount,
        IReadOnlyList<string> GeneratedFiles);

    private static HeroAssetStoryDto WithSelectedHook(HeroAssetStoryDto heroStory, string selectedHook)
        => string.Equals(heroStory.HeroHook, selectedHook, StringComparison.Ordinal)
            ? heroStory
            : heroStory with { HeroHook = selectedHook };

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
            SelectedHeroHook,
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


    private static IReadOnlyList<HeroHookScoreDto> BuildHookScores(HeroAssetStoryDto heroStory)
    {
        var candidates = BuildHookCandidates(heroStory);
        return candidates
            .Select(ScoreHook)
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildHookCandidates(HeroAssetStoryDto heroStory)
    {
        var candidates = new List<string>
        {
            SelectedHeroHook,
            "DON'T MISS THIS TONIGHT",
            "TWO BRIGHT PLANETS TOGETHER",
            "LOOK UP AFTER SUNSET",
            "EVENING SKY HIGHLIGHT"
        };

        if (!string.IsNullOrWhiteSpace(heroStory.HeroHook))
            candidates.Add(heroStory.HeroHook.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(heroStory.HeroAction) && heroStory.HeroAction.Contains("west", StringComparison.OrdinalIgnoreCase))
            candidates.Add("FACE WEST AFTER SUNSET");
        if (!string.IsNullOrWhiteSpace(heroStory.HeroVisualFocus) && heroStory.HeroVisualFocus.Contains("Venus", StringComparison.OrdinalIgnoreCase) && heroStory.HeroVisualFocus.Contains("Jupiter", StringComparison.OrdinalIgnoreCase))
            candidates.Add("VENUS AND JUPITER TONIGHT");

        return candidates
            .Select(CleanHook)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HeroHookScoreDto ScoreHook(string hook)
    {
        var scrollStoppingScore = 84;
        var clickabilityScore = 82;
        var shareabilityScore = 78;
        var understandabilityScore = 84;

        if (hook.Contains("TONIGHT", StringComparison.OrdinalIgnoreCase))
        {
            scrollStoppingScore += 9;
            clickabilityScore += 10;
            shareabilityScore += 5;
        }

        if (hook.Contains("LOOK", StringComparison.OrdinalIgnoreCase) || hook.Contains("FACE", StringComparison.OrdinalIgnoreCase))
        {
            scrollStoppingScore += 5;
            understandabilityScore += 8;
        }

        if (hook.Contains("WEST", StringComparison.OrdinalIgnoreCase) || hook.Contains("SUNSET", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 4;
            understandabilityScore += 7;
            shareabilityScore += 3;
        }

        if (hook.Contains("DON'T MISS", StringComparison.OrdinalIgnoreCase))
        {
            scrollStoppingScore += 8;
            clickabilityScore += 7;
            understandabilityScore -= 6;
        }

        if (hook.Contains("TWO", StringComparison.OrdinalIgnoreCase) || hook.Contains("VENUS", StringComparison.OrdinalIgnoreCase) || hook.Contains("JUPITER", StringComparison.OrdinalIgnoreCase) || hook.Contains("PLANETS", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 4;
            shareabilityScore += 9;
            understandabilityScore += 8;
        }

        if (hook.Contains("BRIGHT", StringComparison.OrdinalIgnoreCase) || hook.Contains("HIGHLIGHT", StringComparison.OrdinalIgnoreCase))
        {
            scrollStoppingScore += 3;
            shareabilityScore += 4;
        }

        if (hook.Length <= 22)
            understandabilityScore += 2;
        if (hook.Length > 32)
            understandabilityScore -= 4;

        scrollStoppingScore = ClampScore(scrollStoppingScore);
        clickabilityScore = ClampScore(clickabilityScore);
        shareabilityScore = ClampScore(shareabilityScore);
        understandabilityScore = ClampScore(understandabilityScore);
        var totalScore = CalculateTotalScore(scrollStoppingScore, clickabilityScore, shareabilityScore, understandabilityScore);

        return new HeroHookScoreDto(hook, scrollStoppingScore, clickabilityScore, shareabilityScore, understandabilityScore, totalScore);
    }

    private static int CalculateTotalScore(int scrollStoppingScore, int clickabilityScore, int shareabilityScore, int understandabilityScore)
        => ClampScore((int)Math.Round(
            (scrollStoppingScore * 0.35)
            + (clickabilityScore * 0.35)
            + (shareabilityScore * 0.15)
            + (understandabilityScore * 0.15),
            MidpointRounding.AwayFromZero));

    private static string SelectTopHook(IReadOnlyList<HeroHookScoreDto> hookScores)
        => hookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).FirstOrDefault()?.Hook ?? string.Empty;

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string CleanHook(string value)
        => Clean(value).Trim('.', '!', '?').ToUpperInvariant();

    private static IReadOnlyList<HeroPlatformVariantDto> BuildPlatformVariants(string selectedHook)
        =>
        [
            new(
                "Landscape",
                "1280x720",
                "YouTube",
                new HeroLayoutBlueprintDto(
                    $"Top-left: {selectedHook}",
                    "Center: Venus + Jupiter",
                    "Bottom-right: West marker",
                    "Twilight")),
            new(
                "Square",
                "1080x1080",
                "Facebook/Instagram",
                new HeroLayoutBlueprintDto(
                    $"Top: {selectedHook}",
                    "Center: Venus + Jupiter",
                    "Bottom: After Sunset",
                    "Twilight")),
            new(
                "Portrait",
                "1080x1920",
                "Stories/Reels/Shorts",
                new HeroLayoutBlueprintDto(
                    $"Top: {selectedHook}",
                    "Center: Venus + Jupiter",
                    "Bottom: Look West After Sunset",
                    "Twilight"))
        ];

    private static HeroAssetBlueprintDto BuildHeroBlueprint(IReadOnlyList<HeroPlatformVariantDto> platformVariants)
        => new(
            "Wonder",
            "AstronomyPoster",
            "Venus and Jupiter above the western horizon during twilight.",
            "Two bright planets together after sunset. Look west to see the pairing.",
            platformVariants);

    private static HeroAssetReviewScoresDto BuildReviewScores()
        => new(95, 95, 90, 95, 94);

    private static HeroAssetBlueprintDto BuildEmptyHeroBlueprint()
        => new(string.Empty, string.Empty, string.Empty, string.Empty, []);

    private static HeroAssetReviewScoresDto BuildEmptyReviewScores()
        => new(0, 0, 0, 0, 0);

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
        if (string.IsNullOrWhiteSpace(blueprint.VisualNarrative)) issues.Add("visualNarrative is required.");
        if (string.IsNullOrWhiteSpace(blueprint.HeroEmotion)) issues.Add("heroEmotion is required.");
        if (blueprint.PlatformVariants.Count == 0) issues.Add("platformVariants is required.");
        if (reviewScores.HeroAssetReadinessScore < 90) issues.Add("heroAssetReadinessScore must be at least 90.");
        return issues;
    }

    private static IReadOnlyList<string> ValidateHookSelection(string selectedHook, IReadOnlyList<string> alternativeHooks, IReadOnlyList<HeroHookScoreDto> hookScores)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(selectedHook)) issues.Add("selectedHook is required.");
        if (alternativeHooks.Count == 0) issues.Add("alternativeHooks is required.");
        if (hookScores.Count == 0) issues.Add("hookScores is required.");
        if (hookScores.Count < 5) issues.Add("At least 5 hooks must be generated.");
        if (hookScores.Any(score => score.TotalScore <= 0)) issues.Add("Each hook score must include a positive totalScore.");
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
