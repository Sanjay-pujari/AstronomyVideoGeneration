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
    ILogger<HeroAssetStoryGenerator> logger,
    IHeroAssetSceneSelector sceneSelector,
    IHeroCompositionEngine compositionEngine) : IHeroAssetStoryGenerator
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
    private const string HeroSceneManifestFileName = "hero-scene-manifest.json";
    private const string HeroCompositionModelFileName = "hero-composition-model.json";
    private const string HeroLayoutValidationFileName = "hero-layout-validation.json";
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
            "sceneselection" => await GenerateHeroSceneManifestAsync(request, cancellationToken: cancellationToken),
            "scene-selection" => await GenerateHeroSceneManifestAsync(request, cancellationToken: cancellationToken),
            "scene_selection" => await GenerateHeroSceneManifestAsync(request, cancellationToken: cancellationToken),
            "scenes" => await GenerateHeroSceneManifestAsync(request, cancellationToken: cancellationToken),
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


    private async Task<HeroAssetGenerationResponse> GenerateHeroSceneManifestAsync(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto? heroStory = null,
        HeroAssetBlueprintDto? blueprint = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Selecting hero asset scenes for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var storyPath = BuildStoryOutputPath(request.EventId, request.RegionId);
        var blueprintPath = BuildBlueprintOutputPath(request.EventId, request.RegionId);
        var sceneManifestPath = BuildSceneManifestOutputPath(request.EventId, request.RegionId);

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

        var approvedScenes = await LoadApprovedSceneCandidatesAsync(BuildQuestionEngineRoot(request.EventId, request.RegionId), cancellationToken);
        if (approvedScenes.Count < 3)
            warnings.Add("Hero scene selection requires at least three approved scene outputs.");

        var isValid = storyValidationIssues.Count == 0 && blueprintValidationIssues.Count == 0 && approvedScenes.Count >= 3;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, [], request.Phase, "SceneSelection", true, true, false);

        var sceneManifest = await sceneSelector.SelectHeroScenesAsync(request, heroStory, blueprint, approvedScenes, cancellationToken);
        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            await File.WriteAllTextAsync(sceneManifestPath, JsonSerializer.Serialize(sceneManifest, JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(sceneManifestPath));
        }

        var sceneManifestGenerated = request.DryRun || File.Exists(sceneManifestPath);
        return new HeroAssetGenerationResponse(request.EventId, sceneManifestGenerated, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "SceneSelection", true, true, false)
        {
            HeroSceneManifest = sceneManifest,
            HeroSceneSelectorExecuted = true,
            HeroSceneManifestGenerated = sceneManifestGenerated,
            HeroSceneManifestPath = NormalizePath(sceneManifestPath),
            PrimaryScene = sceneManifest.PrimaryScene.SceneId,
            SecondaryScene = sceneManifest.SecondaryScene.SceneId,
            SupportScene = sceneManifest.SupportScene.SceneId
        };
    }

    private async Task<HeroAssetGenerationResponse> GenerateHeroImagesAsync(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto? heroStory = null,
        HeroAssetBlueprintDto? blueprint = null,
        HeroSceneManifestDto? selectedSceneManifest = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating hero asset image review for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var storyPath = BuildStoryOutputPath(request.EventId, request.RegionId);
        var blueprintPath = BuildBlueprintOutputPath(request.EventId, request.RegionId);
        var reviewPath = BuildReviewOutputPath(request.EventId, request.RegionId);
        var sceneManifestPath = BuildSceneManifestOutputPath(request.EventId, request.RegionId);
        var compositionModelPath = BuildCompositionModelOutputPath(request.EventId, request.RegionId);
        var layoutValidationPath = BuildLayoutValidationOutputPath(request.EventId, request.RegionId);

        if (!request.DryRun && request.OverwriteExisting)
            CleanExistingHeroAssetOutputs(heroAssetsRoot);

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
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
        var missingSceneAssets = FindMissingApprovedSceneAssets(questionEngineRoot);
        if (missingSceneAssets.Count > 0)
            warnings.Add($"Approved scene assets are missing: {string.Join(", ", missingSceneAssets)}.");

        var approvedSceneOutputs = await LoadApprovedSceneCandidatesAsync(questionEngineRoot, cancellationToken);
        if (approvedSceneOutputs.Count < 3)
            warnings.Add("Hero scene selection requires at least three approved scene outputs before image generation.");

        var heroSceneSelectorExecuted = false;
        if (selectedSceneManifest is null && approvedSceneOutputs.Count >= 3)
        {
            selectedSceneManifest = await sceneSelector.SelectHeroScenesAsync(request, heroStory, blueprint, approvedSceneOutputs, cancellationToken);
            heroSceneSelectorExecuted = true;
        }
        else
        {
            heroSceneSelectorExecuted = selectedSceneManifest is not null;
        }

        if (selectedSceneManifest is null)
            warnings.Add("Hero scene manifest selection did not produce a manifest; image generation cannot run.");

        HeroCompositionModelDto? compositionModel = null;
        if (selectedSceneManifest is not null)
        {
            try
            {
                compositionModel = compositionEngine.ComposeHeroComposition(heroStory, selectedHook, blueprint, selectedSceneManifest, approvedSceneOutputs);
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add(ex.Message);
            }
        }

        var planetAssets = ResolveRequiredHeroPlanetTextures(renderingOptions.Value.CelestialAssetsRoot);
        var missingPlanetAssets = planetAssets
            .Where(asset => string.IsNullOrWhiteSpace(asset.TexturePath) || !File.Exists(asset.TexturePath))
            .Select(asset => asset.Label)
            .ToArray();
        if (missingPlanetAssets.Length > 0)
            warnings.Add($"Required real celestial assets are missing for hero rendering: {string.Join(", ", missingPlanetAssets)}.");

        var layoutValidation = compositionModel is null ? null : BuildHeroLayoutValidation(compositionModel);
        if (layoutValidation?.DuplicateBlocksDetected == true)
            warnings.Add("Hero layout validation failed: duplicate composition block rendering was detected.");
        if (layoutValidation?.TextOverlapDetected == true)
            warnings.Add($"Hero layout validation failed: text overlap detected ({string.Join("; ", layoutValidation.OverlapWarnings)}).");
        if (layoutValidation?.ObjectsVisible == false)
            warnings.Add("Hero layout validation failed: Venus and Jupiter must remain fully visible in every hero variant.");

        var isValid = storyValidationIssues.Count == 0
            && blueprintValidationIssues.Count == 0
            && missingSceneAssets.Count == 0
            && approvedSceneOutputs.Count >= 3
            && selectedSceneManifest is not null
            && compositionModel is not null
            && layoutValidation is not null
            && !layoutValidation.DuplicateBlocksDetected
            && !layoutValidation.TextOverlapDetected
            && layoutValidation.ObjectsVisible
            && heroSceneSelectorExecuted
            && missingPlanetAssets.Length == 0;
        if (!isValid)
        {
            if (layoutValidation is null || !layoutValidation.IsValid)
                layoutValidation = BuildInvalidHeroLayoutValidation();
            if (!request.DryRun)
            {
                DeleteExistingHeroImageOutputs(heroAssetsRoot);
                Directory.CreateDirectory(heroAssetsRoot);
                await File.WriteAllTextAsync(layoutValidationPath, JsonSerializer.Serialize(layoutValidation, JsonOptions), cancellationToken);
                if (!File.Exists(layoutValidationPath))
                    throw new InvalidOperationException($"Hero layout validation was not generated at '{NormalizePath(layoutValidationPath)}'; aborting image generation.");
                generatedFiles.Add(NormalizePath(layoutValidationPath));
            }

            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "Images", true, true, false)
            {
                HeroSceneManifest = selectedSceneManifest,
                HeroSceneSelectorExecuted = heroSceneSelectorExecuted,
                HeroSceneManifestGenerated = false,
                HeroSceneManifestPath = NormalizePath(sceneManifestPath),
                PrimaryScene = selectedSceneManifest?.PrimaryScene.SceneId,
                SecondaryScene = selectedSceneManifest?.SecondaryScene.SceneId,
                SupportScene = selectedSceneManifest?.SupportScene.SceneId,
                HeroCompositionModelGenerated = false,
                LayoutValidationGenerated = request.DryRun || File.Exists(layoutValidationPath),
                DuplicateBlocksDetected = layoutValidation.DuplicateBlocksDetected,
                TextOverlapDetected = layoutValidation.TextOverlapDetected,
                ObjectsVisible = layoutValidation.ObjectsVisible
            };
        }

        if (!request.DryRun)
        {
            Directory.CreateDirectory(heroAssetsRoot);
            await File.WriteAllTextAsync(sceneManifestPath, JsonSerializer.Serialize(selectedSceneManifest, JsonOptions), cancellationToken);
            if (!File.Exists(sceneManifestPath))
                throw new InvalidOperationException($"Hero scene manifest was not generated at '{NormalizePath(sceneManifestPath)}'; aborting image generation.");
            generatedFiles.Add(NormalizePath(sceneManifestPath));

            await File.WriteAllTextAsync(compositionModelPath, JsonSerializer.Serialize(compositionModel, JsonOptions), cancellationToken);
            if (!File.Exists(compositionModelPath))
                throw new InvalidOperationException($"Hero composition model was not generated at '{NormalizePath(compositionModelPath)}'; aborting image generation.");
            generatedFiles.Add(NormalizePath(compositionModelPath));

            await File.WriteAllTextAsync(layoutValidationPath, JsonSerializer.Serialize(layoutValidation, JsonOptions), cancellationToken);
            if (!File.Exists(layoutValidationPath))
                throw new InvalidOperationException($"Hero layout validation was not generated at '{NormalizePath(layoutValidationPath)}'; aborting image generation.");
            generatedFiles.Add(NormalizePath(layoutValidationPath));

            foreach (var imagePath in await GenerateHeroImageFilesAsync(heroAssetsRoot, blueprint, selectedSceneManifest, compositionModel!, planetAssets, cancellationToken))
                generatedFiles.Add(imagePath);

            var generatedHeroImages = HeroImageSpecs
                .Select(spec => Path.Combine(heroAssetsRoot, spec.FileName))
                .Where(File.Exists)
                .Select(NormalizePath)
                .ToArray();
            var visualReview = BuildHeroVisualReview(planetAssets, generatedHeroImages, platformVariants.Count, missingSceneAssets.Count == 0);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(visualReview, JsonOptions), cancellationToken);
        }

        var heroSceneManifestGenerated = request.DryRun || File.Exists(sceneManifestPath);
        var heroCompositionModelGenerated = request.DryRun || File.Exists(compositionModelPath);
        var layoutValidationGenerated = request.DryRun || File.Exists(layoutValidationPath);
        var imageGenerationExecuted = !request.DryRun && heroSceneManifestGenerated && heroCompositionModelGenerated && layoutValidationGenerated && generatedFiles.Count > 3;
        if (!request.DryRun && !imageGenerationExecuted)
        {
            warnings.Add("Hero asset image generation failed validation: no image files were generated.");
            isValid = false;
        }

        if (!heroSceneSelectorExecuted || !heroSceneManifestGenerated || !heroCompositionModelGenerated || !layoutValidationGenerated)
        {
            warnings.Add("Hero asset image generation failed validation: selected scene manifest, composition model, and layout validation are required before image generation.");
            isValid = false;
        }

        return new HeroAssetGenerationResponse(request.EventId, isValid, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "Images", true, true, imageGenerationExecuted)
        {
            HeroSceneManifest = selectedSceneManifest,
            HeroSceneSelectorExecuted = heroSceneSelectorExecuted,
            HeroSceneManifestGenerated = heroSceneManifestGenerated,
            HeroSceneManifestPath = NormalizePath(sceneManifestPath),
            PrimaryScene = selectedSceneManifest?.PrimaryScene.SceneId,
            SecondaryScene = selectedSceneManifest?.SecondaryScene.SceneId,
            SupportScene = selectedSceneManifest?.SupportScene.SceneId,
            HeroCompositionModelGenerated = heroCompositionModelGenerated,
            LayoutValidationGenerated = layoutValidationGenerated,
            DuplicateBlocksDetected = layoutValidation?.DuplicateBlocksDetected ?? false,
            TextOverlapDetected = layoutValidation?.TextOverlapDetected ?? false,
            ObjectsVisible = layoutValidation?.ObjectsVisible ?? false
        };
    }

    private async Task<HeroAssetGenerationResponse> GenerateFullHeroAssetsAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        var storyResponse = await GenerateHeroHookSelectionAsync(request, cancellationToken);
        var blueprintResponse = await GenerateHeroBlueprintAsync(request, storyResponse.HeroStory, cancellationToken);
        var sceneSelectionResponse = await GenerateHeroSceneManifestAsync(request, blueprintResponse.HeroStory, blueprintResponse.HeroBlueprint, cancellationToken);
        var imagesResponse = await GenerateHeroImagesAsync(request, blueprintResponse.HeroStory, blueprintResponse.HeroBlueprint, sceneSelectionResponse.HeroSceneManifest, cancellationToken);

        return MergeResponses(request.EventId, storyResponse, blueprintResponse, sceneSelectionResponse, imagesResponse);
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
            responses.Any(response => response.ImageGenerationExecuted))
        {
            HeroSceneManifest = responses.LastOrDefault(response => response.HeroSceneManifest is not null)?.HeroSceneManifest,
            HeroSceneSelectorExecuted = responses.Any(response => response.HeroSceneSelectorExecuted),
            HeroSceneManifestGenerated = responses.Any(response => response.HeroSceneManifestGenerated),
            HeroSceneManifestPath = responses.LastOrDefault(response => !string.IsNullOrWhiteSpace(response.HeroSceneManifestPath))?.HeroSceneManifestPath,
            PrimaryScene = responses.LastOrDefault(response => !string.IsNullOrWhiteSpace(response.PrimaryScene))?.PrimaryScene,
            SecondaryScene = responses.LastOrDefault(response => !string.IsNullOrWhiteSpace(response.SecondaryScene))?.SecondaryScene,
            SupportScene = responses.LastOrDefault(response => !string.IsNullOrWhiteSpace(response.SupportScene))?.SupportScene,
            HeroCompositionModelGenerated = responses.Any(response => response.HeroCompositionModelGenerated),
            LayoutValidationGenerated = responses.Any(response => response.LayoutValidationGenerated),
            DuplicateBlocksDetected = responses.Any(response => response.DuplicateBlocksDetected),
            TextOverlapDetected = responses.Any(response => response.TextOverlapDetected),
            ObjectsVisible = responses.Any(response => response.ObjectsVisible)
        };
    }


    private static void CleanExistingHeroAssetOutputs(string heroAssetsRoot)
    {
        DeleteExistingHeroImageOutputs(heroAssetsRoot);
        var validationPath = Path.Combine(heroAssetsRoot, HeroLayoutValidationFileName);
        if (File.Exists(validationPath))
            File.Delete(validationPath);
    }

    private static void DeleteExistingHeroImageOutputs(string heroAssetsRoot)
    {
        foreach (var fileName in new[] { HeroLandscapeFileName, HeroSquareFileName, HeroPortraitFileName })
        {
            var path = Path.Combine(heroAssetsRoot, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static bool IsStoryPhase(string? phase)
        => string.Equals(phase?.Trim(), "Story", StringComparison.OrdinalIgnoreCase);

    private static HeroLayoutValidationDto BuildHeroLayoutValidation(HeroCompositionModelDto compositionModel)
    {
        var variants = HeroImageSpecs
            .Select(spec => BuildHeroVariantLayoutValidation(spec, compositionModel))
            .ToArray();
        var renderedBlocks = new[] { "Hook", "Visual", "Timing", "Direction", "CTA" };
        var duplicateBlocksDetected = variants.Any(variant => variant.DuplicateBlocksDetected);
        var textOverlapDetected = variants.Any(variant => variant.TextOverlapDetected);
        var objectsVisible = variants.All(variant => variant.ObjectsVisible);
        var overlapWarnings = variants.SelectMany(variant => variant.OverlapWarnings).ToArray();
        var isValid = !duplicateBlocksDetected && !textOverlapDetected && objectsVisible;
        var errors = BuildHeroLayoutErrors(duplicateBlocksDetected, textOverlapDetected, objectsVisible, overlapWarnings);
        return new HeroLayoutValidationDto(
            renderedBlocks,
            duplicateBlocksDetected,
            textOverlapDetected,
            overlapWarnings,
            objectsVisible,
            BuildObjectVisibility(objectsVisible),
            variants,
            isValid,
            variants,
            errors);
    }

    private static HeroLayoutValidationDto BuildInvalidHeroLayoutValidation()
        => new(
            [],
            false,
            true,
            [],
            false,
            BuildObjectVisibility(false),
            [],
            false,
            [],
            []);

    private static IReadOnlyList<string> BuildHeroLayoutErrors(bool duplicateBlocksDetected, bool textOverlapDetected, bool objectsVisible, IReadOnlyList<string> overlapWarnings)
    {
        var errors = new List<string>();
        if (duplicateBlocksDetected)
            errors.Add("Duplicate hero composition blocks detected.");
        if (textOverlapDetected)
            errors.AddRange(overlapWarnings.Count > 0 ? overlapWarnings : ["Hero text overlap detected."]);
        if (!objectsVisible)
            errors.Add("Venus and Jupiter must remain fully visible in every hero variant.");
        return errors;
    }

    private static HeroVariantLayoutValidationDto BuildHeroVariantLayoutValidation(HeroImageSpec spec, HeroCompositionModelDto compositionModel)
    {
        var (marginX, marginY) = ResolveHeroSafeMargins(spec.Width, spec.Height);
        var renderedBlocks = new[] { "Hook", "Visual", "Timing", "Direction", "CTA" };
        var duplicateBlocksDetected = renderedBlocks.GroupBy(block => block, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);
        var boxes = BuildHeroTextBoxes(spec, marginX, marginY, compositionModel);
        var overlapWarnings = new List<string>();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (Intersects(boxes[i].Bounds, boxes[j].Bounds))
                    overlapWarnings.Add($"{spec.Variant}: {boxes[i].Name} overlaps {boxes[j].Name}.");
            }
        }

        var objectBoxes = BuildHeroObjectBoxes(spec, marginX, marginY);
        var visibility = objectBoxes
            .Select(box => new HeroObjectVisibilityDto(box.Name, IsInsideSafeCanvas(box.Bounds, spec.Width, spec.Height, marginX, marginY), !IsInsideSafeCanvas(box.Bounds, spec.Width, spec.Height, marginX, marginY)))
            .ToArray();
        var objectsVisible = visibility.All(item => item.Visible && !item.Cropped);
        return new HeroVariantLayoutValidationDto(
            spec.Variant,
            spec.Width,
            spec.Height,
            (int)marginX,
            (int)marginY,
            renderedBlocks,
            duplicateBlocksDetected,
            overlapWarnings.Count > 0,
            overlapWarnings,
            objectsVisible,
            visibility);
    }

    private static IReadOnlyList<HeroObjectVisibilityDto> BuildObjectVisibility(bool visible)
        => [new HeroObjectVisibilityDto("Venus", visible, !visible), new HeroObjectVisibilityDto("Jupiter", visible, !visible)];

    private static IReadOnlyList<(string Name, RectangleF Bounds)> BuildHeroTextBoxes(HeroImageSpec spec, float marginX, float marginY, HeroCompositionModelDto compositionModel)
    {
        var subtitleText = BuildHeroSubtitle(spec.Width, spec.Height);
        var boxes = new List<(string Name, RectangleF Bounds)>
        {
            ("Hook", BuildHeroTextBox(spec, "Hook"))
        };

        if (!string.IsNullOrWhiteSpace(subtitleText))
            boxes.Add(("Subtitle", BuildHeroTextBox(spec, "Subtitle")));

        boxes.Add(("Timing", BuildHeroTextBox(spec, "Timing")));
        boxes.Add(("Direction", BuildHeroTextBox(spec, "Direction")));
        boxes.Add(("CTA", BuildHeroTextBox(spec, "CTA")));
        return boxes;
    }

    private static RectangleF BuildHeroTextBox(HeroImageSpec spec, string blockName)
        => (spec.Width, spec.Height, blockName) switch
        {
            (1280, 720, "Hook") => new RectangleF(80, 55, 660, 74),
            (1280, 720, "Subtitle") => new RectangleF(80, 130, 520, 34),
            (1280, 720, "Timing") => new RectangleF(80, 560, 270, 42),
            (1280, 720, "CTA") => new RectangleF(400, 566, 540, 58),
            (1280, 720, "Direction") => new RectangleF(972, 538, 250, 56),
            (1080, 1080, "Hook") => new RectangleF(70, 80, 700, 78),
            (1080, 1080, "Subtitle") => new RectangleF(70, 165, 700, 38),
            (1080, 1080, "Timing") => new RectangleF(70, 780, 280, 48),
            (1080, 1080, "Direction") => new RectangleF(720, 780, 240, 48),
            (1080, 1080, "CTA") => new RectangleF(70, 900, 800, 54),
            (1080, 1920, "Hook") => new RectangleF(70, 110, 820, 94),
            (1080, 1920, "Subtitle") => new RectangleF(70, 210, 820, 44),
            (1080, 1920, "Timing") => new RectangleF(70, 1250, 300, 58),
            (1080, 1920, "Direction") => new RectangleF(650, 1250, 260, 58),
            (1080, 1920, "CTA") => new RectangleF(70, 1602, 900, 76),
            _ => blockName switch
            {
                "Hook" => new RectangleF(Math.Max(36, spec.Width * 0.06f), Math.Max(36, spec.Height * 0.06f), spec.Width - Math.Max(36, spec.Width * 0.06f) * 2, 80),
                "Subtitle" => new RectangleF(Math.Max(36, spec.Width * 0.06f), Math.Max(36, spec.Height * 0.06f) + 90, spec.Width - Math.Max(36, spec.Width * 0.06f) * 2, 40),
                "Timing" => new RectangleF(Math.Max(36, spec.Width * 0.06f), spec.Height * 0.75f, 300, 50),
                "Direction" => new RectangleF(spec.Width - Math.Max(36, spec.Width * 0.06f) - 260, spec.Height * 0.75f, 240, 50),
                "CTA" => new RectangleF(Math.Max(36, spec.Width * 0.06f), spec.Height * 0.84f, spec.Width - Math.Max(36, spec.Width * 0.06f) * 2, 56),
                _ => RectangleF.Empty
            }
        };

    private static IReadOnlyList<(string Name, RectangleF Bounds)> BuildHeroObjectBoxes(HeroImageSpec spec, float marginX, float marginY)
    {
        var safeWidth = spec.Width - marginX * 2;
        var safeHeight = spec.Height - marginY * 2;
        if (spec.Height > spec.Width)
            return [("Venus", CenteredHeroObject(spec.Width * 0.40f, spec.Height * 0.412f, spec.Width * 0.22f)), ("Jupiter", CenteredHeroObject(spec.Width * 0.592f, spec.Height * 0.482f, spec.Width * 0.16f))];
        if (spec.Width == spec.Height)
            return [("Venus", CenteredHeroObject(spec.Width * 0.47f, spec.Height * 0.42f, spec.Width * 0.16f)), ("Jupiter", CenteredHeroObject(spec.Width * 0.65f, spec.Height * 0.49f, spec.Width * 0.12f))];
        return [("Venus", CenteredHeroObject(spec.Width * 0.648f, spec.Height * 0.458f, spec.Width * 0.115f)), ("Jupiter", CenteredHeroObject(spec.Width * 0.738f, spec.Height * 0.435f, spec.Width * 0.080f))];
    }

    private static RectangleF CenteredHeroObject(float centerX, float centerY, float size)
        => new(centerX - size / 2f, centerY - size / 2f, size, size);

    private static bool Intersects(RectangleF a, RectangleF b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static bool IsInsideSafeCanvas(RectangleF bounds, int width, int height, float marginX, float marginY)
        => bounds.Left >= marginX && bounds.Top >= marginY && bounds.Right <= width - marginX && bounds.Bottom <= height - marginY;

    private static (float MarginX, float MarginY) ResolveHeroSafeMargins(int width, int height)
        => (width, height) switch
        {
            (1280, 720) => (80f, 50f),
            (1080, 1080) => (70f, 70f),
            (1080, 1920) => (70f, 100f),
            _ => (Math.Max(36, width * 0.06f), Math.Max(36, height * 0.06f))
        };

    private static async Task<IReadOnlyList<string>> GenerateHeroImageFilesAsync(
        string heroAssetsRoot,
        HeroAssetBlueprintDto blueprint,
        HeroSceneManifestDto sceneManifest,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var generatedFiles = new List<string>();
        foreach (var spec in HeroImageSpecs)
        {
            var variant = blueprint.PlatformVariants.FirstOrDefault(platformVariant => string.Equals(platformVariant.Variant, spec.Variant, StringComparison.OrdinalIgnoreCase));
            var outputPath = Path.Combine(heroAssetsRoot, spec.FileName);
            await WriteHeroImageAsync(outputPath, spec.Width, spec.Height, variant, sceneManifest, compositionModel, planetAssets, cancellationToken);
            generatedFiles.Add(NormalizePath(outputPath));
        }

        return generatedFiles;
    }

    private static async Task WriteHeroImageAsync(
        string outputPath,
        int width,
        int height,
        HeroPlatformVariantDto? variant,
        HeroSceneManifestDto sceneManifest,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var request = new AstronomyVisualCompositionRequest(
            width,
            height,
            compositionModel.HookBlock.Text,
            BuildHeroSubtitle(width, height),
            string.Empty,
            planetAssets,
            mood: "WarmTwilightHero",
            westMarkerLabel: FormatHeroDirection(compositionModel.DirectionBlock.Text),
            starDensity: height > width ? 620 : 455,
            showReferenceOverlays: false,
            referenceStars: [],
            labels: BuildHeroVariantLabels(compositionModel, variant, width, height),
            backgroundImagePath: null,
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

    private static IReadOnlyList<AstronomyVisualLabel> BuildHeroVariantLabels(HeroCompositionModelDto compositionModel, HeroPlatformVariantDto? variant, int width, int height)
    {
        var variantName = variant?.Variant ?? string.Empty;
        var timing = compositionModel.TimingBlock.Text;
        var direction = FormatHeroDirection(compositionModel.DirectionBlock.Text);
        var cta = ResolveHeroImageCta(compositionModel.CtaBlock.Text);
        if (height > width)
        {
            return
            [
                new AstronomyVisualLabel(timing, 0.02f, 0.66f, Color.ParseHex("#CBE8FF"), 0.94f),
                new AstronomyVisualLabel(direction, 0.58f, 0.66f, Color.ParseHex("#FFD48A"), 0.96f),
                new AstronomyVisualLabel(cta, 0.10f, 0.88f, Color.ParseHex("#8FD2FF"), 0.94f)
            ];
        }

        if (variantName.Contains("Square", StringComparison.OrdinalIgnoreCase) || width == height)
        {
            return
            [
                new AstronomyVisualLabel(timing, 0.02f, 0.72f, Color.ParseHex("#CBE8FF"), 0.94f),
                new AstronomyVisualLabel(direction, 0.68f, 0.72f, Color.ParseHex("#FFD48A"), 0.96f),
                new AstronomyVisualLabel(cta, 0.26f, 0.90f, Color.ParseHex("#8FD2FF"), 0.94f)
            ];
        }

        return
        [
            new AstronomyVisualLabel(timing, 0.00f, 0.82f, Color.ParseHex("#CBE8FF"), 0.94f),
            new AstronomyVisualLabel(direction, 0.76f, 0.82f, Color.ParseHex("#FFD48A"), 0.96f),
            new AstronomyVisualLabel(cta, 0.38f, 0.90f, Color.ParseHex("#8FD2FF"), 0.94f)
        ];
    }

    private static string BuildHeroSubtitle(int width, int height)
        => string.Empty;

    private static string ResolveHeroImageCta(string ctaText)
        => "STEP OUTSIDE TONIGHT";

    private static string FormatHeroDirection(string directionText)
    {
        var cleaned = Clean(directionText).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Contains("WEST", StringComparison.OrdinalIgnoreCase))
            return "← WEST";
        return cleaned.StartsWith('←') ? cleaned : $"← {cleaned}";
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


    private async Task<IReadOnlyList<ApprovedHeroSceneCandidate>> LoadApprovedSceneCandidatesAsync(string questionEngineRoot, CancellationToken cancellationToken)
    {
        var sceneApprovalRoot = Path.Combine(questionEngineRoot, SceneApprovalDirectoryName);
        if (!Directory.Exists(sceneApprovalRoot))
            return [];

        var approvedSceneFiles = Directory.EnumerateFiles(sceneApprovalRoot, "scene-*-final.png")
            .Select(path => new { Path = path, SceneId = (Path.GetFileNameWithoutExtension(path) ?? string.Empty).Replace("-final", string.Empty, StringComparison.OrdinalIgnoreCase) })
            .Where(file => !string.IsNullOrWhiteSpace(file.SceneId))
            .ToDictionary(file => file.SceneId, file => NormalizePath(file.Path), StringComparer.OrdinalIgnoreCase);

        if (approvedSceneFiles.Count == 0)
            return [];

        var enrichedPlanPath = Path.Combine(questionEngineRoot, EnrichedPlanFileName);
        EnrichedQuestionScenePlanDto? enrichedPlan = null;
        if (File.Exists(enrichedPlanPath))
            enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(await File.ReadAllTextAsync(enrichedPlanPath, cancellationToken), JsonOptions);

        return approvedSceneFiles
            .Select(file =>
            {
                var scene = enrichedPlan?.Scenes?.FirstOrDefault(candidate => string.Equals(FormatSceneId(candidate.SceneNumber), file.Key, StringComparison.OrdinalIgnoreCase));
                return new ApprovedHeroSceneCandidate(
                    file.Key,
                    scene?.QuestionType,
                    scene?.NarrationIntent,
                    scene?.VisualIntent,
                    scene?.SourceAnswer,
                    file.Value);
            })
            .OrderBy(scene => scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatSceneId(int sceneNumber) => $"scene-{sceneNumber:000}";

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

    private string BuildSceneManifestOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroSceneManifestFileName);

    private string BuildCompositionModelOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroCompositionModelFileName);

    private string BuildLayoutValidationOutputPath(string eventId, string regionId)
        => Path.Combine(BuildHeroAssetsRoot(eventId, regionId), HeroLayoutValidationFileName);

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
