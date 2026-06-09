using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ThumbnailAssetIntelligenceService(IOptions<RenderingOptions> renderingOptions) : IThumbnailAssetIntelligenceService
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string HeroAssetsDirectoryName = "hero-assets";
    private const string ThumbnailAssetsDirectoryName = "thumbnail-assets";
    private const string QuestionEngineDirectoryName = "question-engine";
    private const string SceneApprovalDirectoryName = "scene-approval-v3";
    private const string HeroAssetStoryFileName = "hero-asset-story.json";
    private const string LegacyHeroStoryFileName = "hero-story.json";
    private const string HeroSceneManifestFileName = "hero-scene-manifest.json";
    private const string HeroCompositionModelFileName = "hero-composition-model.json";
    private const string ThumbnailIntelligenceFileName = "thumbnail-intelligence.json";
    private const string ThumbnailCompositionModelFileName = "thumbnail-composition-model.json";
    private const string ThumbnailSceneManifestFileName = "thumbnail-scene-manifest.json";
    private const string SelectedThumbnailHook = "DON'T MISS THIS TONIGHT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailAssetsAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (string.Equals(request.Phase, "Composition", StringComparison.OrdinalIgnoreCase))
            return await GenerateThumbnailCompositionModelAsync(request, cancellationToken);
        if (string.Equals(request.Phase, "SceneSelection", StringComparison.OrdinalIgnoreCase))
            return await GenerateThumbnailSceneManifestAsync(request, cancellationToken);

        return await GenerateThumbnailIntelligenceAsync(request, cancellationToken);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailCompositionModelAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailCompositionOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailCompositionModelDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail composition model could not be parsed.");
            return new ThumbnailAssetGenerationResponse(request.Phase, "Composition", true, NormalizePath(outputPath), existing.Validation.ThumbnailCompositionReadinessScore, []);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var thumbnailIntelligence = await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        var sceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
        EnsureApprovedSceneOutputs(request.EventId, request.RegionId, sceneManifest);

        var model = BuildThumbnailCompositionModel(request, thumbnailIntelligence);
        ValidateThumbnailCompositionModel(model);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(model, JsonOptions), cancellationToken);
        }

        return new ThumbnailAssetGenerationResponse(request.Phase, "Composition", true, NormalizePath(outputPath), model.Validation.ThumbnailCompositionReadinessScore, []);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailSceneManifestAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailSceneManifestDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail scene manifest could not be parsed.");
            ValidateThumbnailSceneManifest(existing, requireSavedManifest: false, outputPath: outputPath);
            return BuildSceneSelectionResponse(request, outputPath, existing);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var thumbnailRoot = Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot();
        await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);
        var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);

        var manifest = BuildThumbnailSceneManifest(request, heroSceneManifest);
        ValidateThumbnailSceneManifest(manifest, requireSavedManifest: false, outputPath: outputPath);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            ValidateThumbnailSceneManifest(manifest, requireSavedManifest: true, outputPath: outputPath);
        }

        return BuildSceneSelectionResponse(request, outputPath, manifest);
    }

    private static ThumbnailAssetGenerationResponse BuildSceneSelectionResponse(ThumbnailAssetGenerationRequest request, string outputPath, ThumbnailSceneManifestDto manifest)
        => new(
            request.Phase,
            "SceneSelection",
            false,
            string.Empty,
            0,
            [],
            ThumbnailSceneManifestGenerated: true,
            ThumbnailSceneManifestPath: NormalizePath(outputPath),
            PrimaryScene: manifest.PrimaryScene.SceneId,
            SecondaryScene: manifest.SecondaryScene.SceneId,
            SupportScene: manifest.SupportScene.SceneId);

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailIntelligenceAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailIntelligenceDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail intelligence could not be parsed.");
            return new ThumbnailAssetGenerationResponse(request.Phase, "Intelligence", false, string.Empty, 0, [], true, NormalizePath(outputPath), existing.SelectedThumbnailHook, existing.Scores.ThumbnailReadinessScore);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var heroStory = await LoadHeroStoryAsync(heroAssetsRoot, cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        var compositionModel = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);

        var warnings = new List<string>();
        var hookScores = BuildThumbnailHookScores(heroStory);
        var selectedHookScore = hookScores.First(score => string.Equals(score.Hook, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase));
        var selectedHook = selectedHookScore.ClarityScore >= 80 ? SelectedThumbnailHook : SelectTopHook(hookScores);
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();

        var recommendedSourceScene = ResolveRecommendedSourceScene(compositionModel);
        var thumbnailCopy = new ThumbnailCopyDto(selectedHook, "Venus + Jupiter", "After Sunset");
        var scores = BuildReadinessScores(hookScores.First(score => string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase)));
        var visualFocus = "Large Venus and Jupiter close together above twilight horizon.";
        var emotion = "Curiosity + Wonder";
        warnings.AddRange(ValidateReadiness(thumbnailCopy, visualFocus, emotion, scores));

        var intelligence = new ThumbnailIntelligenceDto(
            request.EventId,
            request.RegionId,
            request.Language,
            selectedHook,
            alternativeHooks,
            hookScores,
            emotion,
            "High",
            "A time-sensitive sky moment that feels easy to miss unless the viewer clicks now.",
            visualFocus,
            "Bold emotional astronomy thumbnail with minimal text and twilight contrast.",
            "HeroCompositionModel + PrimaryScene",
            recommendedSourceScene,
            ["too much explanation", "long sentences", "exact paragraph CTA", "small unreadable labels"],
            thumbnailCopy,
            [
                new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click"),
                new ThumbnailPlatformTargetDto("Facebook", "1200x630", "Share"),
                new ThumbnailPlatformTargetDto("Instagram", "1080x1080", "StopScroll")
            ],
            scores,
            warnings,
            DateTimeOffset.UtcNow);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        }

        return new ThumbnailAssetGenerationResponse(request.Phase, "Intelligence", false, string.Empty, 0, [], true, NormalizePath(outputPath), selectedHook, scores.ThumbnailReadinessScore);
    }

    private async Task<HeroAssetStoryDto> LoadHeroStoryAsync(string heroAssetsRoot, CancellationToken cancellationToken)
    {
        var heroAssetStoryPath = Path.Combine(heroAssetsRoot, HeroAssetStoryFileName);
        var legacyHeroStoryPath = Path.Combine(heroAssetsRoot, LegacyHeroStoryFileName);
        var storyPath = File.Exists(heroAssetStoryPath) ? heroAssetStoryPath : legacyHeroStoryPath;
        if (!File.Exists(storyPath))
            throw new ArgumentException($"Required thumbnail intelligence input '{HeroAssetStoryFileName}' was not found at '{NormalizePath(heroAssetStoryPath)}'.");

        return JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(storyPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Hero story input could not be parsed.");
    }

    private async Task<ThumbnailIntelligenceDto> LoadThumbnailIntelligenceAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail composition input '{ThumbnailIntelligenceFileName}' was not found at '{NormalizePath(path)}'.");

        return JsonSerializer.Deserialize<ThumbnailIntelligenceDto>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Thumbnail intelligence input could not be parsed.");
    }

    private static async Task<JsonDocument> EnsureJsonInputAsync(string path, string fileName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail intelligence input '{fileName}' was not found at '{NormalizePath(path)}'.");

        return JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    private ThumbnailCompositionModelDto BuildThumbnailCompositionModel(ThumbnailAssetGenerationRequest request, ThumbnailIntelligenceDto intelligence)
    {
        var primaryHook = SelectedThumbnailHook;
        var secondaryText = "Venus + Jupiter";
        var microText = "After Sunset";
        var visualFocus = CleanTextElement(intelligence.VisualFocus, "Large Venus and Jupiter close together above twilight horizon.");
        var textElementCount = new[] { primaryHook, secondaryText, microText }.Count(text => !string.IsNullOrWhiteSpace(text));
        var readinessScore = ClampScore(intelligence.Scores.ThumbnailReadinessScore);

        return new ThumbnailCompositionModelDto(
            request.EventId,
            request.RegionId,
            request.Language,
            primaryHook,
            secondaryText,
            microText,
            "Curiosity",
            "High",
            "ScrollStoppingAstronomyThumbnail",
            visualFocus,
            new ThumbnailCompositionBlocksDto(
                new ThumbnailCompositionTextBlockDto(primaryHook, 1),
                new ThumbnailCompositionVisualBlockDto("HeroCompositionModel + PrimaryScene", 2),
                new ThumbnailCompositionTextBlockDto(secondaryText, 3),
                new ThumbnailCompositionTextBlockDto(microText, 4)),
            [
                new ThumbnailCompositionPlatformVariantDto("Landscape", "1280x720", "YouTubeThumbnail"),
                new ThumbnailCompositionPlatformVariantDto("Square", "1080x1080", "InstagramFacebookPost"),
                new ThumbnailCompositionPlatformVariantDto("Portrait", "1080x1920", "ShortsReelsCover")
            ],
            new ThumbnailCompositionValidationDto(!string.IsNullOrWhiteSpace(primaryHook), !string.IsNullOrWhiteSpace(visualFocus), textElementCount, readinessScore),
            DateTimeOffset.UtcNow);
    }

    private void EnsureApprovedSceneOutputs(string eventId, string regionId, JsonDocument sceneManifest)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName);
        var sceneIds = ResolveManifestSceneIds(sceneManifest).DefaultIfEmpty("scene-001").ToArray();
        var missingSceneOutputs = sceneIds
            .Select(sceneId => Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png"))
            .Where(path => !File.Exists(path))
            .Select(NormalizePath)
            .ToArray();

        if (missingSceneOutputs.Length > 0)
            throw new ArgumentException($"Required thumbnail composition approved scene output(s) were not found: {string.Join(", ", missingSceneOutputs)}.");
    }

    private static IReadOnlyList<string> ResolveManifestSceneIds(JsonDocument sceneManifest)
    {
        var root = sceneManifest.RootElement;
        var sceneIds = new List<string>();
        AddManifestSceneId(root, "primaryScene", sceneIds);
        AddManifestSceneId(root, "secondaryScene", sceneIds);
        AddManifestSceneId(root, "supportScene", sceneIds);
        return sceneIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddManifestSceneId(JsonElement root, string propertyName, ICollection<string> sceneIds)
    {
        if (!root.TryGetProperty(propertyName, out var sceneElement))
            return;

        var sceneId = sceneElement.ValueKind switch
        {
            JsonValueKind.String => sceneElement.GetString(),
            JsonValueKind.Object when sceneElement.TryGetProperty("sceneNumber", out var sceneNumber) && sceneNumber.TryGetInt32(out var number) => $"scene-{number:000}",
            JsonValueKind.Object when sceneElement.TryGetProperty("sceneId", out var sceneIdElement) => sceneIdElement.GetString(),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(sceneId))
            sceneIds.Add(sceneId!);
    }

    private static void ValidateThumbnailCompositionModel(ThumbnailCompositionModelDto model)
    {
        if (string.IsNullOrWhiteSpace(model.PrimaryHook))
            throw new ArgumentException("Thumbnail composition validation failed: primaryHook is required.");
        if (string.IsNullOrWhiteSpace(model.VisualFocus))
            throw new ArgumentException("Thumbnail composition validation failed: visualFocus is required.");
        if (model.Validation.TextElementCount > 3)
            throw new ArgumentException("Thumbnail composition validation failed: textElementCount must be 3 or fewer.");
        if (model.Validation.ThumbnailCompositionReadinessScore < 90)
            throw new ArgumentException("Thumbnail composition validation failed: thumbnailCompositionReadinessScore must be at least 90.");
    }

    private static IReadOnlyList<ThumbnailHookScoreDto> BuildThumbnailHookScores(HeroAssetStoryDto heroStory)
    {
        var candidates = new List<string>
        {
            "DON'T MISS THIS TONIGHT",
            "TWO BRIGHT PLANETS TOGETHER",
            "VENUS AND JUPITER TONIGHT",
            "SEE THIS AFTER SUNSET",
            "LOOK WEST TONIGHT"
        };

        if (!string.IsNullOrWhiteSpace(heroStory.HeroHook))
            candidates.Add(heroStory.HeroHook.ToUpperInvariant());

        return candidates
            .Select(CleanHook)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ScoreThumbnailHook)
            .OrderByDescending(score => string.Equals(score.Hook, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .ToArray();
    }

    private static ThumbnailHookScoreDto ScoreThumbnailHook(string hook)
    {
        var clickabilityScore = 82;
        var curiosityScore = 80;
        var emotionalPullScore = 80;
        var clarityScore = 84;

        if (hook.Contains("DON'T MISS", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 13;
            curiosityScore += 10;
            emotionalPullScore += 11;
            clarityScore -= 4;
        }

        if (hook.Contains("TONIGHT", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 7;
            curiosityScore += 5;
            clarityScore += 4;
        }

        if (hook.Contains("VENUS", StringComparison.OrdinalIgnoreCase) || hook.Contains("JUPITER", StringComparison.OrdinalIgnoreCase) || hook.Contains("PLANETS", StringComparison.OrdinalIgnoreCase))
        {
            clarityScore += 10;
            emotionalPullScore += 4;
        }

        if (hook.Contains("SUNSET", StringComparison.OrdinalIgnoreCase) || hook.Contains("WEST", StringComparison.OrdinalIgnoreCase))
        {
            clarityScore += 8;
            clickabilityScore += 3;
        }

        if (hook.Length <= 30)
            clarityScore += 3;
        else
            clarityScore -= 6;

        clickabilityScore = ClampScore(clickabilityScore);
        curiosityScore = ClampScore(curiosityScore);
        emotionalPullScore = ClampScore(emotionalPullScore);
        clarityScore = ClampScore(clarityScore);
        var totalScore = ClampScore((int)Math.Round(
            (clickabilityScore * 0.35)
            + (curiosityScore * 0.25)
            + (emotionalPullScore * 0.20)
            + (clarityScore * 0.20),
            MidpointRounding.AwayFromZero));

        return new ThumbnailHookScoreDto(hook, clickabilityScore, curiosityScore, emotionalPullScore, clarityScore, totalScore);
    }

    private static string SelectTopHook(IReadOnlyList<ThumbnailHookScoreDto> hookScores)
        => hookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).FirstOrDefault()?.Hook ?? SelectedThumbnailHook;

    private static ThumbnailReadinessScoresDto BuildReadinessScores(ThumbnailHookScoreDto selectedHookScore)
    {
        var readiness = ClampScore((int)Math.Round(
            (selectedHookScore.ClickabilityScore * 0.35)
            + (selectedHookScore.CuriosityScore * 0.25)
            + (selectedHookScore.EmotionalPullScore * 0.20)
            + (selectedHookScore.ClarityScore * 0.20),
            MidpointRounding.AwayFromZero));

        return new ThumbnailReadinessScoresDto(
            selectedHookScore.ClickabilityScore,
            selectedHookScore.CuriosityScore,
            selectedHookScore.EmotionalPullScore,
            selectedHookScore.ClarityScore,
            readiness);
    }

    private static IReadOnlyList<string> ValidateReadiness(ThumbnailCopyDto thumbnailCopy, string visualFocus, string emotion, ThumbnailReadinessScoresDto scores)
    {
        var warnings = new List<string>();
        if (thumbnailCopy.PrimaryText.Length > 30)
            warnings.Add("Thumbnail primary text should be 30 characters or fewer.");
        if (new[] { thumbnailCopy.PrimaryText, thumbnailCopy.SecondaryText, thumbnailCopy.MicroText }.Count(text => !string.IsNullOrWhiteSpace(text)) > 3)
            warnings.Add("Thumbnail should use no more than 3 text elements.");
        if (string.IsNullOrWhiteSpace(visualFocus))
            warnings.Add("Thumbnail visual focus is required.");
        if (string.IsNullOrWhiteSpace(emotion))
            warnings.Add("Thumbnail emotional trigger is required.");
        if (scores.ClickabilityScore < 90)
            warnings.Add("Thumbnail approval requires clickability score >= 90.");

        return warnings;
    }

    private ThumbnailSceneManifestDto BuildThumbnailSceneManifest(ThumbnailAssetGenerationRequest request, JsonDocument heroSceneManifest)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(request.EventId, request.RegionId), SceneApprovalDirectoryName);
        var primaryImagePath = Path.Combine(sceneApprovalRoot, "scene-001-final.png");
        var secondaryImagePath = Path.Combine(sceneApprovalRoot, "scene-005-final.png");
        var supportImagePath = Path.Combine(sceneApprovalRoot, "scene-006-final.png");

        if (!HeroManifestContainsSuitablePrimaryScene(heroSceneManifest))
            throw new ArgumentException("Thumbnail scene selection validation failed: primary scene scene-001 / What is not visually suitable for thumbnail use.");

        return new ThumbnailSceneManifestDto(
            request.EventId,
            new ThumbnailSceneManifestEntryDto(1, "What", NormalizePath(primaryImagePath), "PrimaryVisual"),
            new ThumbnailSceneManifestEntryDto(5, "Why", NormalizePath(secondaryImagePath), "EmotionalSignificance"),
            new ThumbnailSceneManifestEntryDto(6, "Action", NormalizePath(supportImagePath), "UrgencyCue"),
            "Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.");
    }

    private static bool HeroManifestContainsSuitablePrimaryScene(JsonDocument heroSceneManifest)
    {
        var root = heroSceneManifest.RootElement;
        if (!root.TryGetProperty("primaryScene", out var primaryScene))
            return false;

        if (primaryScene.ValueKind != JsonValueKind.Object)
            return false;

        var sceneId = ResolveSceneId(primaryScene);
        var sceneKey = primaryScene.TryGetProperty("sceneKey", out var sceneKeyElement) ? sceneKeyElement.GetString() : null;
        var role = primaryScene.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;

        return string.Equals(sceneId, "scene-001", StringComparison.OrdinalIgnoreCase)
            && string.Equals(sceneKey, "What", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(role) || string.Equals(role, "PrimaryVisual", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveSceneId(JsonElement sceneElement)
    {
        if (sceneElement.TryGetProperty("sceneId", out var sceneIdElement) && !string.IsNullOrWhiteSpace(sceneIdElement.GetString()))
            return sceneIdElement.GetString()!;
        if (sceneElement.TryGetProperty("sceneNumber", out var sceneNumberElement) && sceneNumberElement.TryGetInt32(out var sceneNumber))
            return $"scene-{sceneNumber:000}";
        if (sceneElement.TryGetProperty("imagePath", out var imagePathElement))
        {
            var fileName = Path.GetFileNameWithoutExtension(imagePathElement.GetString() ?? string.Empty);
            if (fileName.Length >= "scene-000".Length)
                return fileName[.."scene-000".Length];
        }

        return string.Empty;
    }

    private static void ValidateThumbnailSceneManifest(ThumbnailSceneManifestDto manifest, bool requireSavedManifest, string outputPath)
    {
        if (manifest.PrimaryScene is null || string.IsNullOrWhiteSpace(manifest.PrimaryScene.ImagePath))
            throw new ArgumentException("Thumbnail scene selection validation failed: primaryScene is required.");
        if (!string.Equals(manifest.PrimaryScene.SceneId, "scene-001", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PrimaryScene.SceneKey, "What", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PrimaryScene.Role, "PrimaryVisual", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail scene selection validation failed: primary scene is not visually suitable.");

        var selectedImagePaths = new[]
        {
            manifest.PrimaryScene.ImagePath,
            manifest.SecondaryScene.ImagePath,
            manifest.SupportScene.ImagePath
        };
        var missingImages = selectedImagePaths.Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingImages.Length > 0)
            throw new ArgumentException($"Thumbnail scene selection validation failed: selected image file(s) missing: {string.Join(", ", missingImages)}.");
        if (requireSavedManifest && !File.Exists(outputPath))
            throw new ArgumentException($"Thumbnail scene selection validation failed: manifest was not saved at '{NormalizePath(outputPath)}'.");
    }

    private static string ResolveRecommendedSourceScene(JsonDocument compositionModel)
    {
        if (compositionModel.RootElement.TryGetProperty("visualBlock", out var visualBlock)
            && visualBlock.TryGetProperty("sourceScene", out var sourceScene)
            && !string.IsNullOrWhiteSpace(sourceScene.GetString()))
            return sourceScene.GetString()!;

        return "scene-001";
    }

    private static void ValidateRequest(ThumbnailAssetGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
        if (!string.Equals(request.Phase, "Intelligence", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Phase, "Composition", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Phase, "SceneSelection", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only thumbnail asset phases 'Intelligence', 'Composition', and 'SceneSelection' are supported in this endpoint version.", nameof(request));
        if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail intelligence generation is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request));
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailIntelligenceOutputPath(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName, ThumbnailIntelligenceFileName);

    private string BuildThumbnailCompositionOutputPath(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName, ThumbnailCompositionModelFileName);

    private string BuildThumbnailSceneManifestOutputPath(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName, ThumbnailSceneManifestFileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string CleanHook(string value)
        => (value ?? string.Empty).Trim().Trim('.', '!', '?').ToUpperInvariant();

    private static string CleanTextElement(string? value, string fallback)
        => string.Join(' ', (string.IsNullOrWhiteSpace(value) ? fallback : value).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
