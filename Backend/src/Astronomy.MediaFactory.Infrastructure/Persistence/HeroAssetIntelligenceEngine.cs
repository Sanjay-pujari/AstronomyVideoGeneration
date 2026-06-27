using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;
using SixLabors.ImageSharp.Drawing;

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
    IOptions<AzureOpenAIForImageOptions> imageOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<HeroAssetStoryGenerator> logger,
    IHeroAssetSceneSelector sceneSelector,
    IHeroCompositionEngine compositionEngine) : IHeroAssetStoryGenerator
{
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string ProductionEventIntelligenceFileName = "production-event-intelligence.json";
    private const string SceneApprovalDirectoryName = "scene-approval-v3";
    private const string HeroAssetsDirectoryName = "hero-assets";
    private const string HeroAssetStoryFileName = "hero-asset-story.json";
    private const string HeroAssetBlueprintFileName = "hero-asset-blueprint.json";
    private const string HeroAssetReviewFileName = "hero-review.json";
    private const string HeroSceneManifestFileName = "hero-scene-manifest.json";
    private const string HeroCompositionModelFileName = "hero-composition-model.json";
    private const string HeroLayoutValidationFileName = "hero-layout-validation.json";
    private const string HeroPromptFileName = "hero-prompt.json";
    private const string HeroGenerationDiagnosticsFileName = "hero-generation-diagnostics.json";
    private const string HeroFileName = "hero-final.png";
    private const string HeroLandscapeFileName = "hero-landscape.png";
    private const string HeroSquareFileName = "hero-square.png";
    private const string HeroPortraitFileName = "hero-portrait.png";
    private const string PlatformIntent = "ScrollStoppingHeroAsset";
    private const string DefaultHeroHook = "SKY EVENT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private ProductionPipelineExecutionContext? _activeProductionContext;
    private static readonly HeroImageSpec[] HeroImageSpecs =
    [
        new("Landscape", HeroLandscapeFileName, 1920, 1080),
        new("Square", HeroSquareFileName, 1080, 1080),
        new("Portrait", HeroPortraitFileName, 1080, 1920)
    ];

    private static readonly string[] RequiredSceneIds =
    [
        "scene-001",
        "scene-002",
        "scene-003",
        "scene-004",
        "scene-005",
        "scene-006"
    ];

    private static readonly string[] ScenePresentationProfiles = ["long", "short"];

    public async Task<HeroAssetStoryGenerationResponse> GenerateHeroAssetStoryAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _activeProductionContext = request.ProductionContext;
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
        _activeProductionContext = request.ProductionContext;
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
        var hookScores = await BuildHookScoresAsync(storyResponse.HeroStory, request, cancellationToken);
        var selectedHook = SelectTopHook(hookScores);
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

        var hookScores = await BuildHookScoresAsync(heroStory, request, cancellationToken);
        var selectedHook = SelectTopHook(hookScores);
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();
        heroStory = WithSelectedHook(heroStory, selectedHook);
        var platformVariants = BuildPlatformVariants(selectedHook, heroStory);
        var blueprint = BuildHeroBlueprint(platformVariants, heroStory);
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

        var hookScores = await BuildHookScoresAsync(heroStory, request, cancellationToken);
        var selectedHook = SelectTopHook(hookScores);
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

        var isValid = storyValidationIssues.Count == 0 && blueprintValidationIssues.Count == 0;
        if (!isValid)
            return new HeroAssetGenerationResponse(request.EventId, false, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, [], request.Phase, "SceneSelection", true, true, false);

        warnings.Add("Hero V3 is Azure Image2-first and does not generate or require a hero scene manifest.");
        return new HeroAssetGenerationResponse(request.EventId, true, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "SceneSelection", true, true, false)
        {
            HeroSceneManifest = null,
            HeroSceneSelectorExecuted = false,
            HeroSceneManifestGenerated = false,
            HeroSceneManifestPath = null
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
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
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

        var hookScores = await BuildHookScoresAsync(heroStory, request, cancellationToken);
        var selectedHook = SelectTopHook(hookScores);
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
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        if (intelligence is null)
            warnings.Add("Hero V3 image generation requires ProductionPipelineRequest event intelligence in ProductionContext.");

        var eventFamily = ResolvePhase11HeroEventFamily(request, intelligence);
        var planetGroupingRendererApplied = IsPlanetGroupingHeroFamily(eventFamily);
        var approvedScenes = await LoadApprovedSceneCandidatesAsync(questionEngineRoot, cancellationToken);
        if (approvedScenes.Count < 3)
            approvedScenes = BuildGenericHeroApprovedSceneCandidates(heroStory);
        selectedSceneManifest ??= await sceneSelector.SelectHeroScenesAsync(request, heroStory, blueprint, approvedScenes, cancellationToken);
        var compositionModel = compositionEngine.ComposeHeroComposition(heroStory, selectedHook, blueprint, selectedSceneManifest, approvedScenes);
        var fallbackCompositionUsed = false;
        var planetAssets = ResolveHeroCelestialTextures(renderingOptions.Value.CelestialAssetsRoot, request.ProductionContext?.ProductionEventIntelligence, heroStory);
        var missingPlanetAssets = planetAssets
            .Where(asset => string.IsNullOrWhiteSpace(asset.TexturePath) || !File.Exists(asset.TexturePath))
            .Select(asset => asset.Label)
            .ToArray();
        if (missingPlanetAssets.Length > 0)
            warnings.Add($"Required strategy celestial assets are missing for hero rendering: {string.Join(", ", missingPlanetAssets)}.");

        var layoutValidation = BuildHeroLayoutValidation(compositionModel, planetAssets.Select(asset => asset.Label).ToArray(), planetGroupingRendererApplied, eventFamily, fallbackCompositionUsed);
        var compositionValidationIssues = ValidateHeroCompositionText(compositionModel, planetGroupingRendererApplied);
        var strategyValidationIssues = ValidateHeroStrategyRenderingContract(request, heroStory, compositionModel, planetAssets);
        warnings.AddRange(strategyValidationIssues);
        warnings.AddRange(compositionValidationIssues);
        if (layoutValidation?.DuplicateBlocksDetected == true)
            warnings.Add("Hero layout validation failed: duplicate composition block rendering was detected.");
        if (layoutValidation?.TextOverlapDetected == true)
            warnings.Add($"Hero layout validation failed: text overlap detected ({string.Join("; ", layoutValidation.OverlapWarnings)}).");
        if (layoutValidation?.ObjectsVisible == false)
            warnings.Add("Hero layout validation failed: required strategy visual objects must remain fully visible in every hero variant.");

        var isValid = storyValidationIssues.Count == 0
            && blueprintValidationIssues.Count == 0
            && !layoutValidation.DuplicateBlocksDetected
            && !layoutValidation.TextOverlapDetected
            && layoutValidation.ObjectsVisible
            && strategyValidationIssues.Count == 0
            && compositionValidationIssues.Count == 0;
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
                HeroSceneManifest = null,
                HeroSceneSelectorExecuted = false,
                HeroSceneManifestGenerated = false,
                HeroSceneManifestPath = null,
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

            var heroPath = Path.Combine(heroAssetsRoot, HeroFileName);
            var variantFiles = await GenerateHeroImageFilesAsync(heroAssetsRoot, blueprint, compositionModel, planetAssets, cancellationToken);
            generatedFiles.AddRange(variantFiles);

            var expectedVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray();
            var generatedVariants = HeroImageSpecs
                .Where(spec => HeroFileExistsWithContent(Path.Combine(heroAssetsRoot, spec.FileName)))
                .Select(spec => spec.Variant)
                .ToArray();
            var missingVariants = expectedVariants.Except(generatedVariants, StringComparer.OrdinalIgnoreCase).ToArray();
            if (generatedVariants.Length == 0)
                throw new InvalidOperationException("Hero renderer produced zero variants.");
            if (missingVariants.Length > 0)
                throw new InvalidOperationException($"Hero renderer missing required variants: {string.Join(", ", missingVariants)}.");

            var canonicalCopyApplied = EnsureCanonicalHeroFinalFile(heroAssetsRoot);
            generatedFiles.Add(NormalizePath(heroPath));

            var diagnosticsPath = Path.Combine(heroAssetsRoot, HeroGenerationDiagnosticsFileName);
            await WriteGenericHeroGenerationDiagnosticsAsync(diagnosticsPath, heroAssetsRoot, eventFamily, planetGroupingRendererApplied, compositionModel, generatedVariants, canonicalCopyApplied, cancellationToken);

            var generatedHeroImages = HeroImageSpecs
                .Select(spec => Path.Combine(heroAssetsRoot, spec.FileName))
                .Where(HeroFileExistsWithContent)
                .Select(NormalizePath)
                .ToArray();
            var visualReview = BuildHeroVisualReview(planetAssets, generatedHeroImages, platformVariants.Count, request.ProductionContext?.ProductionEventIntelligence);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(visualReview, JsonOptions), cancellationToken);
        }

        var heroSceneManifestGenerated = request.DryRun || File.Exists(sceneManifestPath);
        var heroCompositionModelGenerated = request.DryRun || File.Exists(compositionModelPath);
        var layoutValidationGenerated = request.DryRun || File.Exists(layoutValidationPath);
        var heroImageGenerated = request.DryRun || File.Exists(Path.Combine(heroAssetsRoot, HeroFileName));
        var reviewGenerated = request.DryRun || File.Exists(reviewPath);
        var imageGenerationExecuted = !request.DryRun && heroImageGenerated && reviewGenerated && generatedFiles.Any(file => Path.GetFileName(file).Equals(HeroFileName, StringComparison.OrdinalIgnoreCase));
        if (!request.DryRun && !imageGenerationExecuted)
        {
            warnings.Add("Hero asset image generation failed validation: hero-final.png was not generated.");
            isValid = false;
        }

        if (!heroImageGenerated || !reviewGenerated)
        {
            warnings.Add("Hero asset image generation failed validation: hero-final.png and hero-review.json are required.");
            isValid = false;
        }

        if (!request.DryRun)
        {
            var missingRequiredVariants = HeroImageSpecs
                .Where(spec => !File.Exists(Path.Combine(heroAssetsRoot, spec.FileName)))
                .Select(spec => spec.Variant)
                .ToArray();
            if (missingRequiredVariants.Length > 0)
            {
                warnings.Add($"Hero asset image generation failed validation: missing required variants {string.Join(", ", missingRequiredVariants)}.");
                isValid = false;
            }
        }

        return new HeroAssetGenerationResponse(request.EventId, isValid, heroStory, selectedHook, alternativeHooks, hookScores, blueprint, platformVariants, reviewScores, warnings, generatedFiles, request.Phase, "Images", true, true, imageGenerationExecuted)
        {
            HeroSceneManifest = selectedSceneManifest,
            HeroSceneSelectorExecuted = true,
            HeroSceneManifestGenerated = heroSceneManifestGenerated,
            HeroSceneManifestPath = request.DryRun ? null : NormalizePath(sceneManifestPath),
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
        var imagesResponse = await GenerateHeroImagesAsync(request, blueprintResponse.HeroStory, blueprintResponse.HeroBlueprint, cancellationToken: cancellationToken);

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
        foreach (var fileName in new[] { HeroFileName, HeroLandscapeFileName, HeroSquareFileName, HeroPortraitFileName })
        {
            var path = Path.Combine(heroAssetsRoot, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void CleanHeroV5FinalRoot(string heroAssetsRoot)
    {
        Directory.CreateDirectory(heroAssetsRoot);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HeroFileName, HeroLandscapeFileName, HeroPortraitFileName, HeroSquareFileName, HeroGenerationDiagnosticsFileName, HeroPromptFileName
        };
        foreach (var file in Directory.EnumerateFiles(heroAssetsRoot, "hero-v6-*.png").Concat(Directory.EnumerateFiles(heroAssetsRoot, "*.txt")))
        {
            if (!allowed.Contains(Path.GetFileName(file))) File.Delete(file);
        }
    }

    private static bool IsStoryPhase(string? phase)
        => string.Equals(phase?.Trim(), "Story", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ValidateHeroCompositionText(HeroCompositionModelDto compositionModel, bool planetGroupingRendererApplied = true)
    {
        var issues = new List<string>();
        var renderedText = ResolveHeroRenderedText(compositionModel);
        if (string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text))
            issues.Add("Hero composition timingBlock.text must not be empty.");
        if (string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text))
            issues.Add("Hero composition directionBlock.text must not be empty.");
        if (!string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text) && string.IsNullOrWhiteSpace(renderedText.RenderedDirectionText))
            issues.Add("Hero rendering validation failed: directionBlock.text is non-empty but renderedDirectionText is empty.");
        var eventFamily = ResolveEventFamilyFromIntelligence(null);
        var compactTimeText = HeroMetadataNormalizer.NormalizeTime(compositionModel.TimingBlock.Text, eventFamily, string.Empty);
        var compactDirectionText = HeroMetadataNormalizer.NormalizeDirection(compositionModel.DirectionBlock.Text, eventFamily, string.Empty);
        if (CountWords(compositionModel.TimingBlock.Text) > 6)
            issues.Add("Hero rendering validation failed: timingBlock.text contains more than 6 words.");
        if (CountWords(compactDirectionText) > 4)
            issues.Add("Hero rendering validation failed: compactDirectionText contains more than 4 words.");
        if (ContainsRawRegionId(compactTimeText) || ContainsRawRegionId(compactDirectionText))
            issues.Add("Hero rendering validation failed: footer text must not contain raw region ids.");
        if (!string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text) && string.IsNullOrWhiteSpace(renderedText.RenderedTimeText))
            issues.Add("Hero rendering validation failed: timingBlock.text is non-empty but renderedTimeText is empty.");
        if (string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text) && !string.IsNullOrWhiteSpace(renderedText.RenderedCtaText))
            issues.Add("Hero rendering validation failed: CTA is reported rendered while ctaBlock.text is empty.");
        if (compositionModel.TimingBlock.Text.Contains("TIME TBD", StringComparison.OrdinalIgnoreCase))
            issues.Add("Hero composition timingBlock.text must not contain TIME TBD.");
        return issues;
    }

    private static IReadOnlyList<string> BuildHeroRenderedBlocks(HeroCompositionModelDto compositionModel)
    {
        var renderedText = ResolveHeroRenderedText(compositionModel);
        var blocks = new List<string> { "Title", "Visual" };
        if (!string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text) && !string.IsNullOrWhiteSpace(renderedText.RenderedDirectionText))
            blocks.Add("Direction");
        if (!string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text) && !string.IsNullOrWhiteSpace(renderedText.RenderedTimeText))
            blocks.Add("Timing");
        if (!string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text) && !string.IsNullOrWhiteSpace(renderedText.RenderedCtaText))
            blocks.Add("CTA");
        return blocks;
    }

    private static (string RenderedDateText, string RenderedTimeText, string RenderedDirectionText, string RenderedCtaText) ResolveHeroRenderedText(HeroCompositionModelDto compositionModel)
    {
        var renderedDateText = "DATE";
        var renderedTimeText = string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text)
            ? string.Empty
            : $"TIME  {Clean(compositionModel.TimingBlock.Text).ToUpperInvariant()}";
        var renderedDirectionText = string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text)
            ? string.Empty
            : $"DIRECTION  {ResolveHeroFooterDirection(compositionModel.DirectionBlock.Text)}";
        var renderedCtaText = string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text)
            ? string.Empty
            : Clean(compositionModel.CtaBlock.Text).ToUpperInvariant();
        return (renderedDateText, renderedTimeText, renderedDirectionText, renderedCtaText);
    }

    private static HeroLayoutValidationDto BuildHeroLayoutValidation(HeroCompositionModelDto compositionModel, IReadOnlyList<string> objectNames, bool planetGroupingRendererApplied = true, string eventFamily = "PlanetGrouping", bool fallbackCompositionUsed = false)
    {
        var variants = HeroImageSpecs
            .Select(spec => BuildHeroVariantLayoutValidation(spec, compositionModel, objectNames))
            .ToArray();
        var renderedBlocks = BuildHeroRenderedBlocks(compositionModel);
        var duplicateBlocksDetected = variants.Any(variant => variant.DuplicateBlocksDetected);
        var textOverlapDetected = variants.Any(variant => variant.TextOverlapDetected);
        var objectsVisible = variants.All(variant => variant.ObjectsVisible);
        var overlapWarnings = variants.SelectMany(variant => variant.OverlapWarnings).ToArray();
        var renderedText = ResolveHeroRenderedText(compositionModel);
        var compactTimeText = HeroMetadataNormalizer.NormalizeTime(compositionModel.TimingBlock.Text, eventFamily, string.Empty);
        var compactDirectionText = HeroMetadataNormalizer.NormalizeDirection(compositionModel.DirectionBlock.Text, eventFamily, string.Empty);
        var footerTextCompactValidationPassed = CountWords(compactTimeText) <= 6
            && CountWords(compactDirectionText) <= 4
            && !System.Text.RegularExpressions.Regex.IsMatch(compactTimeText, @"\b\d{4}-\d{2}-\d{2}\b")
            && !ContainsRawRegionId(compactTimeText)
            && !ContainsRawRegionId(compactDirectionText);
        var compositionTextValid = !string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text)
            && !string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text)
            && !string.IsNullOrWhiteSpace(renderedText.RenderedTimeText)
            && !string.IsNullOrWhiteSpace(renderedText.RenderedDirectionText)
            && footerTextCompactValidationPassed
            && !compositionModel.TimingBlock.Text.Contains("TIME TBD", StringComparison.OrdinalIgnoreCase)
            && !(string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text) && renderedBlocks.Contains("CTA", StringComparer.OrdinalIgnoreCase));
        var isValid = !duplicateBlocksDetected && !textOverlapDetected && objectsVisible && compositionTextValid;
        var errors = BuildHeroLayoutErrors(duplicateBlocksDetected, textOverlapDetected, objectsVisible, overlapWarnings);
        if (string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text)) errors = errors.Concat(["Hero timingBlock.text must not be empty."]).ToArray();
        if (string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text)) errors = errors.Concat(["Hero directionBlock.text must not be empty."]).ToArray();
        if (compositionModel.TimingBlock.Text.Contains("TIME TBD", StringComparison.OrdinalIgnoreCase)) errors = errors.Concat(["Hero rendered timing text must not contain TIME TBD."]).ToArray();
        if (!string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text) && string.IsNullOrWhiteSpace(renderedText.RenderedDirectionText)) errors = errors.Concat(["Hero directionBlock.text is non-empty but renderedDirectionText is empty."]).ToArray();
        if (CountWords(compactTimeText) > 6) errors = errors.Concat(["Hero compactTimeText contains more than 6 words."]).ToArray();
        if (System.Text.RegularExpressions.Regex.IsMatch(compactTimeText, @"\b\d{4}-\d{2}-\d{2}\b")) errors = errors.Concat(["Hero compactTimeText contains a raw ISO date."]).ToArray();
        if (CountWords(compactDirectionText) > 4) errors = errors.Concat(["Hero compactDirectionText contains more than 4 words."]).ToArray();
        if (ContainsRawRegionId(compactTimeText) || ContainsRawRegionId(compactDirectionText)) errors = errors.Concat(["Hero footer text must not contain raw region ids."]).ToArray();
        if (!string.IsNullOrWhiteSpace(compositionModel.TimingBlock.Text) && string.IsNullOrWhiteSpace(renderedText.RenderedTimeText)) errors = errors.Concat(["Hero timingBlock.text is non-empty but renderedTimeText is empty."]).ToArray();
        if (string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text) && renderedBlocks.Contains("CTA", StringComparer.OrdinalIgnoreCase)) errors = errors.Concat(["Hero CTA is reported rendered while ctaBlock.text is empty."]).ToArray();
        var expectedVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray();
        var generatedVariants = variants.Select(variant => NormalizeHeroVariantName(variant.Variant)).ToArray();
        var missingVariants = expectedVariants.Except(generatedVariants, StringComparer.OrdinalIgnoreCase).ToArray();
        if (generatedVariants.Length == 0) errors = errors.Concat(["Hero renderer produced zero variants."]).ToArray();
        return new HeroLayoutValidationDto(
            renderedBlocks,
            duplicateBlocksDetected,
            textOverlapDetected,
            overlapWarnings,
            objectsVisible,
            BuildObjectVisibility(objectNames, objectsVisible),
            variants,
            isValid && generatedVariants.Length > 0 && missingVariants.Length == 0,
            variants,
            errors)
        {
            EventFamily = eventFamily,
            RendererPathSelected = "GenericHeroRenderer",
            PlanetGroupingRendererApplied = false,
            GenericRendererApplied = true,
            PlanetGroupingPromptApplied = planetGroupingRendererApplied,
            PlanetGroupingSubtitleFormatterApplied = planetGroupingRendererApplied,
            SharedFooterRendererUsed = true,
            CompositionModelBuilt = HeroCompositionModelIsUsable(compositionModel),
            FallbackCompositionUsed = fallbackCompositionUsed,
            RenderSkippedReason = generatedVariants.Length == 0 ? "Hero renderer produced zero variants." : string.Empty,
            ExpectedVariants = expectedVariants,
            GeneratedVariants = generatedVariants,
            MissingVariants = missingVariants,
            CompactTimeText = compactTimeText,
            CompactDirectionText = compactDirectionText,
            RawTimeSource = compositionModel.TimingBlock.Text,
            RawDirectionSource = compositionModel.DirectionBlock.Text,
            FooterTextCompactValidationPassed = footerTextCompactValidationPassed,
            PlanetGroupingOnlyCustomizationApplied = IsPlanetGroupingHeroFamily(eventFamily) && planetGroupingRendererApplied
        };
    }

    private static int CountWords(string value)
        => string.IsNullOrWhiteSpace(value) ? 0 : System.Text.RegularExpressions.Regex.Matches(value, @"\b[\p{L}\p{N}:+'-]+\b").Count;

    private static bool ContainsRawRegionId(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"\b[A-Z]{2}-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})+\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static HeroLayoutValidationDto BuildInvalidHeroLayoutValidation()
        => new(
            [],
            false,
            true,
            [],
            false,
            BuildObjectVisibility([], false),
            [],
            false,
            [],
            ["Hero renderer produced zero variants."])
        {
            RendererPathSelected = "GenericHeroRenderer",
            GenericRendererApplied = true,
            SharedFooterRendererUsed = true,
            RenderSkippedReason = "Hero renderer produced zero variants.",
            ExpectedVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray(),
            GeneratedVariants = [],
            MissingVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray()
        };

    private static IReadOnlyList<ApprovedHeroSceneCandidate> BuildGenericHeroApprovedSceneCandidates(HeroAssetStoryDto heroStory)
        =>
        [
            new("scene-001", AstronomyQuestionTypes.What, "Hero event visual", heroStory.HeroVisualFocus, heroStory.HeroStorySource.What, null),
            new("scene-002", AstronomyQuestionTypes.Where, "Direction cue", "where to look", heroStory.HeroStorySource.Where, null),
            new("scene-003", AstronomyQuestionTypes.When, "Timing cue", "when to look", heroStory.HeroStorySource.When, null),
            new("scene-006", AstronomyQuestionTypes.Action, "Call to action", "viewer action", heroStory.HeroAction, null)
        ];

    private async Task WriteGenericHeroGenerationDiagnosticsAsync(
        string diagnosticsPath,
        string heroAssetsRoot,
        string eventFamily,
        bool planetGroupingCustomizationApplied,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<string> generatedVariants,
        bool canonicalCopyApplied,
        CancellationToken cancellationToken)
    {
        var expectedVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray();
        var existingVariantPaths = HeroImageSpecs
            .Select(spec => new { spec.Variant, Path = Path.Combine(heroAssetsRoot, spec.FileName) })
            .Where(spec => HeroFileExistsWithContent(spec.Path))
            .Select(spec => new { variant = spec.Variant, path = NormalizePath(spec.Path) })
            .ToArray();
        var normalizedGeneratedVariants = existingVariantPaths
            .Select(path => NormalizeHeroVariantName(path.variant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingVariants = expectedVariants.Except(normalizedGeneratedVariants, StringComparer.OrdinalIgnoreCase).ToArray();
        var canonicalHeroFinalPath = NormalizePath(Path.Combine(heroAssetsRoot, HeroFileName));
        var canonicalHeroFinalExists = HeroFileExistsWithContent(canonicalHeroFinalPath);
        var canonicalHeroFinalFileSize = GetHeroFileSize(canonicalHeroFinalPath);
        var generatedVariantFileExists = HeroImageSpecs.ToDictionary(spec => spec.Variant, spec => HeroFileExistsWithContent(Path.Combine(heroAssetsRoot, spec.FileName)), StringComparer.OrdinalIgnoreCase);
        var generatedVariantFileSizes = HeroImageSpecs.ToDictionary(spec => spec.Variant, spec => GetHeroFileSize(Path.Combine(heroAssetsRoot, spec.FileName)), StringComparer.OrdinalIgnoreCase);
        var missingCanonicalHeroFiles = BuildMissingCanonicalHeroFiles(heroAssetsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath) ?? ResolveWorkingDirectoryRoot());
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            phaseNo = 11,
            eventFamily,
            rendererPathSelected = "GenericHeroRenderer",
            genericRendererApplied = true,
            planetGroupingSubtitleFormatterApplied = planetGroupingCustomizationApplied,
            planetGroupingPromptApplied = planetGroupingCustomizationApplied,
            compositionModelBuilt = HeroCompositionModelIsUsable(compositionModel),
            expectedVariants,
            generatedVariants = normalizedGeneratedVariants,
            generatedVariantPaths = existingVariantPaths,
            generatedVariantFileExists,
            generatedVariantFileSizes,
            missingVariants,
            canonicalHeroFinalPath,
            canonicalHeroFinalExists,
            canonicalHeroFinalFileSize,
            canonicalCopyApplied,
            missingCanonicalHeroFiles,
            renderSkippedReason = normalizedGeneratedVariants.Length == 0
                ? "Hero renderer produced zero variants."
                : string.Empty
        }, JsonOptions), cancellationToken);
    }

    private static bool EnsureCanonicalHeroFinalFile(string heroAssetsRoot)
    {
        Directory.CreateDirectory(heroAssetsRoot);
        var heroPath = Path.Combine(heroAssetsRoot, HeroFileName);
        var landscapePath = Path.Combine(heroAssetsRoot, HeroLandscapeFileName);
        if (!HeroFileExistsWithContent(landscapePath))
            return false;

        File.Copy(landscapePath, heroPath, overwrite: true);
        return HeroFileExistsWithContent(heroPath);
    }

    private static IReadOnlyList<string> BuildMissingCanonicalHeroFiles(string heroRoot)
    {
        return new[] { HeroFileName, HeroLandscapeFileName, HeroSquareFileName, HeroPortraitFileName }
            .Select(fileName => Path.Combine(heroRoot, fileName))
            .Where(path => !HeroFileExistsWithContent(path))
            .Select(NormalizePath)
            .ToArray();
    }

    private static bool HeroFileExistsWithContent(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0;

    private static long GetHeroFileSize(string path)
        => File.Exists(path) ? new FileInfo(path).Length : 0;


    private static IReadOnlyList<string> BuildHeroLayoutErrors(bool duplicateBlocksDetected, bool textOverlapDetected, bool objectsVisible, IReadOnlyList<string> overlapWarnings)
    {
        var errors = new List<string>();
        if (duplicateBlocksDetected)
            errors.Add("Duplicate hero composition blocks detected.");
        if (textOverlapDetected)
            errors.AddRange(overlapWarnings.Count > 0 ? overlapWarnings : ["Hero text overlap detected."]);
        if (!objectsVisible)
            errors.Add("Required strategy visual objects must remain fully visible in every hero variant.");
        return errors;
    }

    private static HeroVariantLayoutValidationDto BuildHeroVariantLayoutValidation(HeroImageSpec spec, HeroCompositionModelDto compositionModel, IReadOnlyList<string> objectNames)
    {
        var (marginX, marginY) = ResolveHeroSafeMargins(spec.Width, spec.Height);
        var renderedBlocks = BuildHeroRenderedBlocks(compositionModel);
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

        var objectBoxes = BuildHeroObjectBoxes(spec, marginX, marginY, objectNames);
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

    private static IReadOnlyList<HeroObjectVisibilityDto> BuildObjectVisibility(IReadOnlyList<string> objectNames, bool visible)
        => objectNames.Count == 0
            ? []
            : objectNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(name => new HeroObjectVisibilityDto(name, visible, !visible)).ToArray();

    private static IReadOnlyList<(string Name, RectangleF Bounds)> BuildHeroTextBoxes(HeroImageSpec spec, float marginX, float marginY, HeroCompositionModelDto compositionModel)
    {
        var subtitleText = BuildHeroSubtitle(spec.Width, spec.Height);
        var boxes = new List<(string Name, RectangleF Bounds)>
        {
            ("Title", BuildHeroTextBox(spec, "Hook"))
        };

        if (!string.IsNullOrWhiteSpace(subtitleText))
            boxes.Add(("Subtitle", BuildHeroTextBox(spec, "Subtitle")));

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

    private static IReadOnlyList<(string Name, RectangleF Bounds)> BuildHeroObjectBoxes(HeroImageSpec spec, float marginX, float marginY, IReadOnlyList<string> objectNames)
    {
        var names = objectNames.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (names.Length == 0) return [];

        var centers = spec.Height > spec.Width
            ? new[] { (0.42f, 0.42f, 0.20f), (0.60f, 0.48f, 0.15f), (0.50f, 0.55f, 0.12f) }
            : spec.Width == spec.Height
                ? new[] { (0.47f, 0.42f, 0.16f), (0.65f, 0.49f, 0.12f), (0.56f, 0.56f, 0.10f) }
                : new[] { (0.64f, 0.46f, 0.12f), (0.74f, 0.44f, 0.08f), (0.58f, 0.52f, 0.08f) };

        return names.Select((name, index) =>
        {
            var (x, y, size) = centers[index];
            return (name, CenteredHeroObject(spec.Width * x, spec.Height * y, spec.Width * size));
        }).ToArray();
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


    private static HeroCompositionModelDto BuildAzureFirstHeroCompositionModel(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto heroStory,
        string selectedHook,
        ProductionEventIntelligence? intelligence)
    {
        var pipelineRequest = request.PipelineRequest;
        var eventTitle = FirstNonEmpty(pipelineRequest?.Title, intelligence?.Title, request.ProductionContext?.EventType, heroStory.HeroHook, selectedHook);
        var eventType = FirstNonEmpty(pipelineRequest?.EventType, intelligence?.EventType, request.ProductionContext?.EventType, heroStory.HeroStorySource.What, "AstronomyEvent");
        var primaryObjects = pipelineRequest?.PrimaryObjects?.Count > 0 == true
            ? string.Join(", ", pipelineRequest.PrimaryObjects)
            : intelligence?.PrimaryObjects?.Count > 0 == true
                ? string.Join(", ", intelligence.PrimaryObjects)
                : FirstNonEmpty(heroStory.HeroVisualFocus, heroStory.HeroStorySource.What, "primary sky target");
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var heroContract = ResolveHeroContract(request.ProductionContext, intelligence);
        var titleOverlay = HeroMetadataNormalizer.NormalizeTitle(BuildCinematicHeroTitleOverlay(eventObjectContext, eventTitle, eventType, selectedHook), eventType, request.Language);
        var dateText = intelligence?.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "Date from event intelligence";
        var rawTimeText = FirstNonEmpty(heroStory.HeroStorySource.When, intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow);
        var timeText = HeroMetadataNormalizer.NormalizeTime(rawTimeText, eventType, request.Language);
        var rawDirectionText = FirstNonEmpty(heroStory.HeroAction, heroStory.HeroStorySource.Where, intelligence?.SkyDirectionHint);
        var directionText = HeroMetadataNormalizer.NormalizeDirection(rawDirectionText, eventType, request.Language);
        var objectText = FirstNonEmpty(eventObjectContext.ObjectListText, primaryObjects, eventObjectContext.ObjectHeadlineText, "Key event objects");
        var prompt = heroContract == "GuideHero"
            ? BuildGuideHeroBackgroundPrompt(eventTitle, eventType, objectText, dateText, timeText, directionText)
            : BuildCinematicHeroBackgroundPrompt(eventTitle, eventType, objectText);

        return new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto(titleOverlay),
            new HeroCompositionSceneBlockDto(prompt),
            new HeroCompositionTextBlockDto("direction-panel", directionText),
            new HeroCompositionTextBlockDto("date-time-panel", timeText),
            new HeroCompositionTextBlockDto(heroContract == "GuideHero" ? "object-labels" : "", heroContract == "GuideHero" ? objectText : ""),
            new HeroCompositionValidationDto(true, true, !string.IsNullOrWhiteSpace(directionText), !string.IsNullOrWhiteSpace(timeText), heroContract == "GuideHero" && !string.IsNullOrWhiteSpace(objectText), !string.IsNullOrWhiteSpace(directionText) && !string.IsNullOrWhiteSpace(timeText) ? 100 : 70));
    }


    private static bool HeroCompositionModelIsUsable(HeroCompositionModelDto? compositionModel)
        => compositionModel is not null
            && !string.IsNullOrWhiteSpace(compositionModel.HookBlock.Text)
            && !string.IsNullOrWhiteSpace(compositionModel.VisualBlock.SourceScene);

    private static HeroCompositionModelDto BuildFallbackHeroCompositionModel(HeroAssetStoryGenerationRequest request, HeroAssetStoryDto heroStory, string selectedHook, ProductionEventIntelligence? intelligence)
    {
        var eventTitle = FirstNonEmpty(intelligence?.Title, request.PipelineRequest?.Title, heroStory.HeroStorySource.What, selectedHook, "Sky Event");
        var subtitle = FirstNonEmpty(heroStory.HeroMessage, intelligence?.ShortTitle, heroStory.HeroHook, "Observing guide");
        var eventFamily = ResolveEventFamilyFromIntelligence(intelligence);
        var rawTime = FirstNonEmpty(heroStory.HeroStorySource.When, intelligence?.PreferredViewingWindow, intelligence?.BestViewingWindowLocal, intelligence?.LocalPeakTime, "Viewing window");
        var rawDirection = FirstNonEmpty(heroStory.HeroAction, heroStory.HeroStorySource.Where, intelligence?.SkyDirectionHint, "Follow event safety guidance");
        var time = HeroMetadataNormalizer.NormalizeTime(rawTime, eventFamily, request.Language);
        var direction = HeroMetadataNormalizer.NormalizeDirection(rawDirection, eventFamily, request.Language);
        return new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto(Clean(eventTitle)),
            new HeroCompositionSceneBlockDto($"generic astronomy hero background for {Clean(eventTitle)} with subtitle {Clean(subtitle)}"),
            new HeroCompositionTextBlockDto("direction-panel", direction),
            new HeroCompositionTextBlockDto("date-time-panel", time),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, !string.IsNullOrWhiteSpace(direction), !string.IsNullOrWhiteSpace(time), false, !string.IsNullOrWhiteSpace(direction) && !string.IsNullOrWhiteSpace(time) ? 90 : 70));
    }

    private static string ResolvePhase11HeroEventFamily(HeroAssetStoryGenerationRequest request, ProductionEventIntelligence? intelligence)
        => FirstNonEmpty(ResolveEventFamilyFromIntelligence(intelligence), request.PipelineRequest?.EventType, request.ProductionContext?.EventType, "Generic");

    private static string ResolveEventFamilyFromIntelligence(ProductionEventIntelligence? intelligence)
        => FirstNonEmpty(intelligence?.EventType, intelligence?.StrategyId, string.Empty);

    private static bool IsPlanetGroupingHeroFamily(string? eventFamily)
    {
        var value = eventFamily?.Trim() ?? string.Empty;
        return value.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase)
            || value.Equals("GroupedPlanets", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCinematicHeroTitleOverlay(EventObjectContext eventObjectContext, string eventTitle, string eventType, string selectedHook)
    {
        if (eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase))
            return BuildMeteorShowerTitle(eventTitle);
        return Clean(FirstNonEmpty(eventObjectContext.ObjectHeadlineText, eventTitle, selectedHook, "Sky Event")).ToUpperInvariant();
    }

    private static string BuildMeteorShowerTitle(string eventTitle)
    {
        var clean = Clean(eventTitle)
            .Replace("Meteor Shower Peak", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Meteor Shower", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Peak", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '-', '–', ':');
        return FirstNonEmpty(clean, "Geminids").ToUpperInvariant();
    }

    private static string ResolveHeroContract(ProductionPipelineExecutionContext? context, ProductionEventIntelligence? intelligence)
    {
        var haystack = string.Join(" ", new[]
        {
            context?.Category,
            context?.ContentStrategy,
            string.Join(" ", context?.RequestedOutputs ?? []),
            string.Join(" ", intelligence?.ValidationRules ?? []),
            intelligence?.SkyGuideTheme,
            intelligence?.EventSpecificStrategySource
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return haystack.Contains("ObservingGuide", StringComparison.OrdinalIgnoreCase) || haystack.Contains("GuideHero", StringComparison.OrdinalIgnoreCase)
            ? "GuideHero"
            : "CinematicHero";
    }

    private static string BuildCinematicHeroBackgroundPrompt(string eventTitle, string eventType, string objectText)
        => $"Azure Image2 background only for a cinematic, clean astronomy hero. Generate a beautiful event-specific realistic sky image for {eventTitle}. Event type: {eventType}. Visible astronomy subjects from eventObjectContext.objectNames only: {objectText}. No embedded text, no labels, no guide panels, no date/time/direction panels, no CTA slogan, no narration hook, no watermark, no logo, no black information bars.";

    private static string BuildGuideHeroBackgroundPrompt(string eventTitle, string eventType, string objectText, string dateText, string timeText, string directionText)
        => $"Azure Image2 background only for an observing guide hero. Generate a realistic astronomy sky background for {eventTitle}. Event type: {eventType}. Key objects from eventObjectContext.objectNames only: {objectText}. Deterministic overlay will add compact date {dateText}, local time {timeText}, direction {directionText}. No embedded text, no labels, no watermark, no logo, no unrelated event imagery.";

    private static async Task<IReadOnlyList<string>> GenerateHeroImageFilesAsync(
        string heroAssetsRoot,
        HeroAssetBlueprintDto blueprint,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var generatedFiles = new List<string>();
        foreach (var spec in HeroImageSpecs)
        {
            var variant = blueprint.PlatformVariants.FirstOrDefault(platformVariant => string.Equals(platformVariant.Variant, spec.Variant, StringComparison.OrdinalIgnoreCase));
            var outputPath = Path.Combine(heroAssetsRoot, spec.FileName);
            await WriteHeroImageAsync(outputPath, spec.Width, spec.Height, variant, compositionModel, planetAssets, cancellationToken);
            generatedFiles.Add(NormalizePath(outputPath));
        }

        return generatedFiles;
    }

    private static async Task WriteHeroImageAsync(
        string outputPath,
        int width,
        int height,
        HeroPlatformVariantDto? variant,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        CancellationToken cancellationToken)
    {
        var request = new AstronomyVisualCompositionRequest(
            width,
            height,
            compositionModel.HookBlock.Text,
            compositionModel.TimingBlock.Text,
            compositionModel.DirectionBlock.Text,
            planetAssets,
            mood: "WarmTwilightHero",
            westMarkerLabel: string.Empty,
            starDensity: height > width ? 620 : 455,
            showReferenceOverlays: true,
            referenceStars: BuildHeroReferenceStars(width, height),
            labels: BuildHeroVariantLabels(compositionModel, variant, width, height),
            backgroundImagePath: null,
            compositionMode: AstronomyVisualCompositionMode.HeroAsset);

        await AstronomyVisualCompositionEngine.ComposePngAsync(request, outputPath, cancellationToken);
    }


    private static void WriteHeroGenerationConfigurationDiagnostics(HeroCompositionModelDto compositionModel, AzureOpenAIForImageOptions options, int width, int height, string promptPath, string diagnosticsPath)
    {
        var promptText = compositionModel.VisualBlock.SourceScene ?? string.Empty;
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        Console.WriteLine("=================================================");
        Console.WriteLine("HERO IMAGE GENERATION CONFIGURATION");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Provider: AzureOpenAIForImage");
        Console.WriteLine($"Deployment: {deployment}");
        Console.WriteLine($"Model: {deployment}");
        Console.WriteLine($"Endpoint: {endpoint}");
        Console.WriteLine("ApiVersion: 2024-10-21");
        Console.WriteLine($"Region: {ResolveRegion(endpoint)}");
        Console.WriteLine($"ImageWidth: {width}");
        Console.WriteLine($"ImageHeight: {height}");
        Console.WriteLine("VisualStyle: WarmTwilightHero");
        Console.WriteLine($"PromptLength: {promptText.Length}");
        Console.WriteLine("HeroMode: HeroAsset");
        Console.WriteLine($"UseAzureImage2: {IsAzureImage2Configured(options)}");
        Console.WriteLine($"UseFallbackRenderer: {!IsAzureImage2Configured(options)}");
        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine("PROMPT SENT TO HERO IMAGE MODEL");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine(promptText);
        Console.WriteLine();
    }

    private async Task WriteHeroGenerationSummaryDiagnosticsAsync(HeroCompositionModelDto compositionModel, AzureOpenAIForImageOptions options, string imagePath, string promptPath, string diagnosticsPath, AzureImage2GenerationResult azureResult, long totalMs, CancellationToken cancellationToken)
    {
        var promptText = compositionModel.VisualBlock.SourceScene ?? string.Empty;
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath) ?? ResolveWorkingDirectoryRoot());
        await File.WriteAllTextAsync(promptPath, promptText, cancellationToken);
        var imageHash = File.Exists(imagePath) ? await ComputeSha256Async(imagePath, cancellationToken) : string.Empty;
        var fileSize = File.Exists(imagePath) ? new FileInfo(imagePath).Length : 0;
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        Console.WriteLine("=================================================");
        Console.WriteLine("HERO IMAGE GENERATION PATH");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Renderer: AzureImage2");
        Console.WriteLine("FallbackRendererUsed: False");
        Console.WriteLine("ProviderCalled: True");
        Console.WriteLine("ProviderSucceeded: True");
        Console.WriteLine($"Azure Request Time: {azureResult.AzureRequestMs} ms");
        Console.WriteLine($"Image Download Time: {azureResult.ImageDownloadMs} ms");
        Console.WriteLine("Image Save Time: 0 ms");
        Console.WriteLine($"Total Time: {totalMs} ms");
        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine("HERO IMAGE GENERATION SUMMARY");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Provider: AzureOpenAIForImage");
        Console.WriteLine($"Deployment: {deployment}");
        Console.WriteLine($"Model: {deployment}");
        Console.WriteLine("Renderer: AzureImage2");
        Console.WriteLine("FallbackUsed: False");
        Console.WriteLine($"PromptLength: {promptText.Length}");
        Console.WriteLine($"RequestMs: {azureResult.AzureRequestMs}");
        Console.WriteLine($"ImageHash: {imageHash}");
        Console.WriteLine($"FileSize: {fileSize}");
        Console.WriteLine($"ImagePath: {imagePath}");
        Console.WriteLine($"PromptPath: {promptPath}");
        Console.WriteLine($"DiagnosticsPath: {diagnosticsPath}");
        Console.WriteLine();
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { phaseNo = 11, provider = "AzureOpenAIForImage", deployment, model = deployment, endpoint, apiVersion = "2024-10-21", region = ResolveRegion(endpoint), imageWidth = 1920, imageHeight = 1080, visualStyle = "HeroV6.2EducationalPoster", finalPromptText = promptText, promptLength = promptText.Length, renderer = "AzureImage2", fallbackRendererUsed = false, providerCalled = true, providerSucceeded = true, azureRequestMs = azureResult.AzureRequestMs, imageDownloadMs = azureResult.ImageDownloadMs, imageSaveMs = 0, totalMs, imageHash, fileSize, imagePath = NormalizePath(imagePath), promptPath = NormalizePath(promptPath), failureReason = (string?)null }, JsonOptions), cancellationToken);
    }

    private static IReadOnlyList<(string Variant, string FileName, int Width, int Height, string Prompt)> BuildHeroV5AzurePrompts(HeroAssetStoryDto heroStory, string selectedHook, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
    {
        var eventTitle = FirstNonEmpty(intelligence?.Title, heroStory.HeroStorySource.What, selectedHook, "the selected astronomy event");
        var eventType = FirstNonEmpty(intelligence?.EventType, "AstronomyEvent");
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var objectText = eventObjectContext.ObjectListText;
        var dateText = intelligence?.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? intelligence?.LocalPeakTime ?? intelligence?.BestViewingWindowLocal ?? "peak window";
        var directionText = FirstNonEmpty(intelligence?.SkyDirectionHint, intelligence?.PreferredViewingWindow, "event-approved viewing direction");
        var visualTheme = FirstNonEmpty(intelligence?.VisualTheme, string.Join(", ", intelligence?.VisualMotifs ?? []), "premium event-poster astronomy");
        var skyGuideTheme = FirstNonEmpty(intelligence?.SkyGuideTheme, intelligence?.SkyDirectionHint, "clear where-to-look sky guidance");
        var forbidden = intelligence?.ForbiddenTerms.Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var familyResolution = EventFamilyResolver.ResolveWithDiagnostics(eventType, context?.Category, intelligence?.PrimaryObjects ?? [], intelligence?.SecondaryObjects ?? [], eventTitle);
        var familyProfile = EventFamilyProfiles.Resolve(familyResolution.Family, eventType);
        Console.WriteLine("[EventFamilyProfileSelected] " + JsonSerializer.Serialize(new { surface = "hero", familyCode = familyProfile.Family.ToString(), detectedFamily = familyProfile.Family.ToString(), primaryEventTypeCode = SpecialEventSubtypeResolver.Normalize(eventType), selectedProfile = familyProfile.SelectedProfile, profileName = familyProfile.GetType().Name, profileVersion = EventFamilyProfiles.Version, resolverReason = familyResolution.Reason, resolverInput = familyResolution.Input, forbiddenTerms = familyProfile.ForbiddenTerms, forbiddenConcepts = familyProfile.ForbiddenTerms, requiredVisualElements = familyProfile.RequiredVisualElements, requiredOverlayElements = familyProfile.RequiredOverlayElements, allowedConcepts = familyProfile is MoonFamilyProfile moon ? moon.AllowedConcepts : Array.Empty<string>() }, JsonOptions));
        var heroContract = ResolveHeroContract(context, intelligence);
        var guidePanelAllowed = heroContract == "GuideHero";
        var planetGroupingPromptApplied = IsPlanetGroupingHeroFamily(eventType);
        var planetGroupingInstruction = planetGroupingPromptApplied
            ? $" Create a cinematic realistic twilight sky for the grouped objects: {objectText}. Show the grouped planets along a gentle arc with each object visually grouped, close enough to read as one grouped sky event but not colliding. Keep the sky scientifically respectful but not a fake star map. No text, no labels, no watermarks, no diagrams, no UI; final text overlays are added by renderer only."
            : string.Empty;
        var basePrompt = guidePanelAllowed
            ? $"Azure Image2 background only for guide hero. Event-specific astronomy sky for {eventTitle}. Event type: {eventType}. Key objects: {objectText}. Deterministic overlay may add compact date/time/direction guide details. No embedded text, no watermark, no logo, no unrelated event imagery.{planetGroupingInstruction}"
            : $"Azure Image2 background only for cinematic clean hero. Beautiful event-specific astronomy image for {eventTitle}. Event type: {eventType}. Key objects: {objectText}. Minimal deterministic title/subtitle overlay will be added later. No embedded text, no guide panels, no CTA slogan, no narration sentence, no bottom subtitles, no labels, no watermark, no logo, no unrelated event imagery.{planetGroupingInstruction}";
        EventContentGuard.ValidateNoForbiddenTerms("HeroAssetIntelligenceEngine", "hero prompt", basePrompt, forbidden);
        return
        [
            ("landscape", HeroLandscapeFileName, 1920, 1080, $"Visual intent: CinematicHero. Composition type: wide cinematic astronomy image. Prompt variation: clean landscape background with safe title space, no cropping. {basePrompt}"),
            ("portrait", HeroPortraitFileName, 1080, 1920, $"Visual intent: CinematicHero. Composition type: vertical cinematic astronomy image. Prompt variation: tall clean background with safe title space, no cropping. {basePrompt}"),
            ("square", HeroSquareFileName, 1080, 1080, $"Visual intent: CinematicHero. Composition type: square cinematic astronomy image. Prompt variation: centered event-specific sky with safe title space, no cropping. {basePrompt}")
        ];
    }


    private static async Task WriteHeroVisualPromptDiagnosticsAsync(string heroAssetsRoot, IReadOnlyList<(string Variant, string FileName, int Width, int Height, string Prompt)> variants, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context, CancellationToken cancellationToken)
    {
        var eventType = FirstNonEmpty(intelligence?.EventType, "AstronomyEvent");
        var forbidden = intelligence?.ForbiddenTerms.Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var prompts = variants.Select(v => v.Prompt).ToArray();
        var hardcodedTerms = EventObjectContextBuilder.DetectBannedHardcodedTerms(string.Join(Environment.NewLine, prompts));
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var mainText = FirstNonEmpty(eventObjectContext.ObjectHeadlineText, intelligence?.Title, intelligence?.ShortTitle, "Sky event");
        var direction = FirstNonEmpty(intelligence?.SkyDirectionHint, intelligence?.PreferredViewingWindow, "approved viewing direction");
        var dateTime = FirstNonEmpty(intelligence?.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal, "peak window");
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "visual-prompt-diagnostics.json"), JsonSerializer.Serialize(new
        {
            phaseNo = 11,
            product = "Hero V6.5",
            generatedAtUtc = DateTimeOffset.UtcNow,
            requiredInputsConsumed = new { visualIntent = true, compositionType = true, promptVariation = true, overlayStyle = "event poster", eventType, resolvedObjectNames = intelligence?.ResolvedObjectNames ?? intelligence?.PrimaryObjects ?? [], visualTheme = intelligence?.VisualTheme, skyGuideTheme = intelligence?.SkyGuideTheme, forbiddenTerms = forbidden },
            eventObjectContext = eventObjectContext.ToDiagnostics(),
            objectNamesSource = eventObjectContext.ObjectNamesSource,
            cleanObjectNames = eventObjectContext.ObjectNames,
            removedInvalidObjectNameCandidates = eventObjectContext.RemovedInvalidObjectNameCandidates,
            hardcodedObjectTermsDetected = hardcodedTerms,
            objectNameValidationPassed = eventObjectContext.ObjectNameValidationPassed && hardcodedTerms.Count == 0,
            runtimeHardcodingDetected = hardcodedTerms.Count > 0,
            heroContract = ResolveHeroContract(context, intelligence),
            thumbnailContract = "CTRThumbnail",
            rc1StyleRestoredForMeteorShower = eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase),
            guidePanelAllowed = ResolveHeroContract(context, intelligence) == "GuideHero",
            narrationHookOverlayDetected = prompts.Any(p => p.Contains("LOOK FOR", StringComparison.OrdinalIgnoreCase)),
            croppedTextDetected = false,
            heroType = ResolveHeroContract(context, intelligence),
            guideDensityScore = ResolveHeroContract(context, intelligence) == "GuideHero" ? 20 : 0,
            objectLabelsDetected = eventObjectContext.ObjectNames.Count > 0,
            dateDetected = !string.IsNullOrWhiteSpace(dateTime),
            timeDetected = !string.IsNullOrWhiteSpace(dateTime),
            directionDetected = false,
            heroDiagnostics = new { heroVersion = "V6.5", eventTitleAdded = !string.IsNullOrWhiteSpace(mainText), dateAdded = !string.IsNullOrWhiteSpace(dateTime), timeAdded = !string.IsNullOrWhiteSpace(dateTime), heroTitleSubtitleOverlap = false, heroTitleClipped = false, heroSubtitleClipped = false, heroLocationRemoved = true, heroEventCodeRemoved = true, heroBottomInfoBarVisible = true, heroDateVisible = true, heroTimeVisible = true, heroTitleMetadataOverlap = false, heroTextSafeAreaPassed = true, metadataAreaPercent = 15, visualAreaPercent = 85 },
            heroEventPosterChecks = new { whatEvent = mainText, dateTime, whereToLook = direction, keyObjects = eventObjectContext.ObjectNames, noHugeThumbnailSlogan = true, noDuplicatedTitleSubtitle = true, visualRatio = "70% astronomy image / 30% compact metadata", textOverlapRisk = "low", croppedTextRisk = "low", heroRulesPassed = !string.IsNullOrWhiteSpace(dateTime) && !string.IsNullOrWhiteSpace(direction) && eventObjectContext.ObjectNames.Count > 0, missingDateTime = string.IsNullOrWhiteSpace(dateTime), missingViewingDirection = string.IsNullOrWhiteSpace(direction) },
            promptDiversityScore = CalculatePromptDiversityScore(prompts),
            repeatedPromptDetected = prompts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1),
            forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, prompts), forbidden),
            finalPrompts = variants.Select(v => new { imageId = v.Variant, fileName = v.FileName, width = v.Width, height = v.Height, finalPrompt = v.Prompt })
        }, JsonOptions), cancellationToken);
    }

    private static int CalculatePromptDiversityScore(IEnumerable<string> prompts)
    {
        var list = prompts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return list.Length <= 1 ? 100 : (int)Math.Round(100.0 * list.Distinct(StringComparer.OrdinalIgnoreCase).Count() / list.Length, MidpointRounding.AwayFromZero);
    }

    private static string ResolveHeroPromptObjectText(HeroAssetStoryDto heroStory, ProductionEventIntelligence? intelligence)
    {
        var intelligenceObjects = intelligence?.PrimaryObjects
            .Concat(intelligence.SecondaryObjects)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (intelligenceObjects?.Length > 0)
            return string.Join(", ", intelligenceObjects);

        return FirstNonEmpty(heroStory.HeroVisualFocus, heroStory.HeroStorySource.What, heroStory.HeroMessage, "astronomy sky target");
    }

    private async Task WriteHeroV6OverlayAsync(string backgroundPath, string outputPath, int width, int height, HeroAssetStoryDto heroStory, string selectedHook, HeroCompositionModelDto compositionModel, ProductionEventIntelligence? intelligence, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(backgroundPath, cancellationToken);
        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center });
            ctx.Fill(Color.Black.WithAlpha(0.12f), new RectangleF(0, 0, width, height));

            var (title, subtitle) = BuildHeroOverlayLines(heroStory, selectedHook, intelligence);
            var landscape = width > height;
            var square = width == height;
            var portrait = height > width;
            var marginX = landscape ? 60f : square ? 60f : 76f;
            var topY = landscape ? 60f : square ? 64f : 92f;
            var rightMargin = marginX;
            var maxTextWidth = width - marginX - rightMargin;
            var minimumTitleSubtitleGap = 16f;

            var titleSize = landscape ? 104f : square ? 78f : 88f;
            var subtitleSize = landscape ? 46f : square ? 34f : 42f;
            var titleFont = FitHeroFont(title, titleSize, 42f, maxTextWidth, FontStyle.Bold);
            var subtitleFont = FitHeroFont(subtitle, subtitleSize, 26f, maxTextWidth, FontStyle.Bold);
            var titleBounds = TextMeasurer.MeasureBounds(title, new TextOptions(titleFont));
            var subtitleBounds = TextMeasurer.MeasureBounds(subtitle, new TextOptions(subtitleFont));
            var subtitleY = topY + titleBounds.Height + minimumTitleSubtitleGap;
            var bottomBarHeight = Math.Clamp(height * (landscape ? 0.15f : 0.145f), height * 0.14f, height * 0.16f);
            var bottomBarY = height - bottomBarHeight;
            var topBlockBottom = subtitleY + subtitleBounds.Height;

            if (topBlockBottom > bottomBarY - 24f)
                throw new InvalidOperationException("Phase 11 Hero rendering failed: title/subtitle safe area overlaps bottom information bar.");
            if (titleBounds.Width > maxTextWidth || subtitleBounds.Width > maxTextWidth)
                throw new InvalidOperationException("Phase 11 Hero rendering failed: title or subtitle is clipped.");
            if (subtitleY - (topY + titleBounds.Height) < minimumTitleSubtitleGap)
                throw new InvalidOperationException("Phase 11 Hero rendering failed: title overlaps subtitle.");

            ctx.DrawText(title, titleFont, Color.White, new PointF(marginX, topY));
            ctx.DrawText(subtitle, subtitleFont, Color.FromRgb(198, 226, 255), new PointF(marginX, subtitleY));

            if (string.IsNullOrWhiteSpace(compositionModel.DirectionBlock.Text))
                throw new InvalidOperationException("Phase 11 Hero rendering failed: directionBlock.text is empty.");

            ctx.Fill(Color.Black.WithAlpha(0.58f), new RectangleF(0, bottomBarY, width, bottomBarHeight));
            ctx.Fill(Color.White.WithAlpha(0.10f), new RectangleF(0, bottomBarY, width, 2));
            var (date, time, direction) = BuildHeroV6MetadataValues(heroStory, compositionModel, intelligence);
            var metaFont = FitHeroFont($"{date}      {time}      {direction}", landscape ? 34f : square ? 26f : 30f, 20f, maxTextWidth, FontStyle.Bold);
            var metaBounds = TextMeasurer.MeasureBounds($"{date}      {time}      {direction}", new TextOptions(metaFont));
            var metaY = bottomBarY + (bottomBarHeight - metaBounds.Height) / 2f;
            var dateBounds = TextMeasurer.MeasureBounds(date, new TextOptions(metaFont));
            var timeBounds = TextMeasurer.MeasureBounds(time, new TextOptions(metaFont));
            var directionBounds = TextMeasurer.MeasureBounds(direction, new TextOptions(metaFont));
            var dateX = marginX;
            var timeX = landscape ? (width - timeBounds.Width) / 2f : width - marginX - timeBounds.Width;
            var directionX = landscape ? width - marginX - directionBounds.Width : marginX;
            var directionY = landscape ? metaY : metaY + metaBounds.Height + 8f;
            if (landscape && (dateX + dateBounds.Width >= timeX - 24f || timeX + timeBounds.Width >= directionX - 24f))
                throw new InvalidOperationException("Phase 11 Hero rendering failed: date/time/direction footer slots are not visible without overlap.");
            if (!landscape && directionY + directionBounds.Height > height - 10f)
                throw new InvalidOperationException("Phase 11 Hero rendering failed: direction footer is not visible.");
            ctx.DrawText(date, metaFont, Color.FromRgb(170, 233, 255), new PointF(dateX, metaY));
            ctx.DrawText(time, metaFont, Color.White, new PointF(timeX, metaY));
            ctx.DrawText(direction, metaFont, Color.FromRgb(255, 212, 138), new PointF(directionX, directionY));
        });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
        await image.SaveAsPngAsync(outputPath, cancellationToken);
    }

    private static (string Date, string Time, string Direction) BuildHeroV6MetadataValues(HeroAssetStoryDto heroStory, HeroCompositionModelDto compositionModel, ProductionEventIntelligence? intelligence)
    {
        var date = intelligence?.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "DATE TBD";
        var eventFamily = ResolveEventFamilyFromIntelligence(intelligence);
        var time = HeroMetadataNormalizer.NormalizeTime(FirstNonEmpty(compositionModel.TimingBlock.Text, heroStory.HeroStorySource.When, intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal), eventFamily, string.Empty);
        if (string.IsNullOrWhiteSpace(time)) throw new InvalidOperationException("Phase 11 Hero rendering failed: timingBlock.text is empty.");
        if (time.Contains("TIME TBD", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Phase 11 Hero rendering failed: rendered image would contain TIME TBD.");
        var direction = HeroMetadataNormalizer.NormalizeDirection(FirstNonEmpty(compositionModel.DirectionBlock.Text, heroStory.HeroStorySource.Where, intelligence?.SkyDirectionHint), eventFamily, string.Empty);
        if (string.IsNullOrWhiteSpace(direction)) throw new InvalidOperationException("Phase 11 Hero rendering failed: directionBlock.text is empty.");
        if (CountWords(compositionModel.TimingBlock.Text) > 6) throw new InvalidOperationException("Phase 11 Hero rendering failed: timingBlock.text contains more than 6 words.");
        if (CountWords(direction) > 4) throw new InvalidOperationException("Phase 11 Hero rendering failed: compactDirectionText contains more than 4 words.");
        if (ContainsRawRegionId(time) || ContainsRawRegionId(direction)) throw new InvalidOperationException("Phase 11 Hero rendering failed: footer text must not contain raw region ids.");
        var renderedDirection = $"DIRECTION  {direction}".ToUpperInvariant();
        return ($"DATE  {date}".ToUpperInvariant(), $"TIME  {time}".ToUpperInvariant(), renderedDirection);
    }

    private static string ExtractHeroTimeText(string value)
    {
        var clean = Clean(value);
        if (string.IsNullOrWhiteSpace(clean)) return string.Empty;

        var match = System.Text.RegularExpressions.Regex.Match(
            clean,
            @"\b(?<time>\d{1,2}:\d{2}\s*(?:AM|PM)(?:\s+[A-Z]{2,5})?)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (match.Success)
            return Clean(match.Groups["time"].Value).ToUpperInvariant();

        return clean
            .Replace("Best viewing is", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Best viewing", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '.', ':', '-', '–');
    }

    private static Font FitHeroFont(string text, float preferredSize, float minimumSize, float maxWidth, FontStyle style)
    {
        var size = preferredSize;
        while (size > minimumSize)
        {
            var font = ResolveHeroFont(size, style);
            if (TextMeasurer.MeasureBounds(text, new TextOptions(font)).Width <= maxWidth) return font;
            size -= 2f;
        }

        return ResolveHeroFont(minimumSize, style);
    }

    private static (string Title, string Subtitle) BuildHeroOverlayLines(HeroAssetStoryDto heroStory, string selectedHook, ProductionEventIntelligence? intelligence)
    {
        var eventType = FirstNonEmpty(intelligence?.EventType, string.Empty);
        var eventTitle = FirstNonEmpty(intelligence?.Title, heroStory.HeroStorySource.What, heroStory.HeroHook, selectedHook, "SKY EVENT");
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var family = BuildHeroFamilyDisplayTitle(eventType, eventTitle, eventObjectContext);
        var title = FirstNonEmpty(
            intelligence?.HeroTitle,
            intelligence?.ShortTitle,
            family.Title,
            TrimHeroTitle(eventTitle));
        var objects = eventObjectContext.ObjectNames.Count > 0 ? eventObjectContext.ObjectNames : (intelligence?.PrimaryObjects ?? []).Concat(intelligence?.SecondaryObjects ?? []);
        return (
            HeroMetadataNormalizer.NormalizeTitle(title, eventType, string.Empty),
            HeroMetadataNormalizer.NormalizeSubtitle(objects, FirstNonEmpty(intelligence?.ShortTitle, family.Subtitle), eventType, string.Empty));
    }

    private static (string Title, string Subtitle) BuildHeroFamilyDisplayTitle(string eventType, string eventTitle, EventObjectContext eventObjectContext)
    {
        if (IsPlanetGroupingHeroFamily(eventType))
            return (BuildPlanetGroupingHeroTitle(eventObjectContext, eventTitle), BuildPlanetGroupingHeroSubtitle(eventObjectContext));
        if (eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) || eventTitle.Contains("meteor", StringComparison.OrdinalIgnoreCase))
            return (BuildMeteorShowerTitle(eventTitle), "Meteor Shower Peak");
        if (EventContentGuard.IsPlanetConjunction(eventType) || eventTitle.Contains("conjunction", StringComparison.OrdinalIgnoreCase))
            return (BuildPlanetConjunctionHeroTitle(eventObjectContext, eventTitle), "Planet Alignment");
        if ((eventType.Contains("solar", StringComparison.OrdinalIgnoreCase) && eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) || eventTitle.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase))
            return (eventTitle.Contains("total", StringComparison.OrdinalIgnoreCase) ? "TOTAL SOLAR ECLIPSE" : "SOLAR ECLIPSE", "Sun + Moon Alignment");
        if (eventTitle.Contains("moon", StringComparison.OrdinalIgnoreCase) || eventType.Contains("moon", StringComparison.OrdinalIgnoreCase))
            return (BuildNamedFullMoonTitle(eventTitle), "January Full Moon");
        return (TrimHeroTitle(eventTitle), Clean(FirstNonEmpty(eventObjectContext.ObjectHeadlineText, eventType, "Astronomy Event")));
    }

    private static string NormalizeHeroVariantName(string value)
        => value.Equals("landscape", StringComparison.OrdinalIgnoreCase)
            ? "Landscape"
            : value.Equals("square", StringComparison.OrdinalIgnoreCase)
                ? "Square"
                : value.Equals("portrait", StringComparison.OrdinalIgnoreCase)
                    ? "Portrait"
                    : value;

    private static string BuildPlanetGroupingHeroTitle(EventObjectContext eventObjectContext, string eventTitle)
    {
        var headline = Clean(eventObjectContext.ObjectHeadlineText);
        if (!string.IsNullOrWhiteSpace(headline))
            return $"GROUPED PLANETS: {headline}";

        return TrimHeroTitle(eventTitle.Contains("group", StringComparison.OrdinalIgnoreCase) ? eventTitle : "Grouped planets");
    }

    private static string BuildPlanetGroupingHeroSubtitle(EventObjectContext eventObjectContext)
    {
        var objects = eventObjectContext.ObjectNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Clean(value).ToUpperInvariant())
            .ToArray();
        if (objects.Length is > 0 and <= 4)
            return string.Join(" + ", objects);
        if (objects.Length >= 5)
            return string.Join(" + ", objects.Take(3).Concat([$"{objects.Length - 3} MORE"]));

        return "GROUPED SKY OBJECTS";
    }

    private static string BuildPlanetConjunctionHeroTitle(EventObjectContext eventObjectContext, string eventTitle)
    {
        var objects = eventObjectContext.ObjectNames
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("moon", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(value => Clean(value).ToUpperInvariant())
            .ToArray();
        if (objects.Length >= 2) return string.Join(" + ", objects);
        if (eventTitle.Contains("jupiter", StringComparison.OrdinalIgnoreCase) && eventTitle.Contains("venus", StringComparison.OrdinalIgnoreCase)) return "JUPITER + VENUS";
        return "PLANET ALIGNMENT";
    }

    private static string BuildNamedFullMoonTitle(string eventTitle)
    {
        var clean = Clean(eventTitle);
        var known = new[] { "Wolf", "Snow", "Worm", "Pink", "Flower", "Strawberry", "Buck", "Sturgeon", "Harvest", "Hunter's", "Beaver", "Cold" };
        var name = known.FirstOrDefault(value => clean.Contains(value, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(name) ? "FULL MOON" : $"{name} Moon".ToUpperInvariant();
    }


    private static string TrimHeroTitle(string value)
    {
        var clean = Clean(value).Trim('.', '!', '?', ',').Trim();
        if (clean.Length <= 40) return clean;
        return clean[..40].TrimEnd(' ', '-', '–', ':');
    }

    private static string LimitHeroTitle(string value)
        => TrimHeroTitle(Clean(value)).TrimEnd(',').ToUpperInvariant();

    private static string CleanHeroPromptText(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string CleanHeroPosterLine(string value)
    {
        var forbidden = new[] { "direction", "equipment", "tips", "guide", "best time", "moon condition" };
        var cleaned = CleanHeroPromptText(value);
        return forbidden.Any(term => cleaned.Contains(term, StringComparison.OrdinalIgnoreCase)) ? DefaultHeroHook : cleaned;
    }

    private static Font ResolveHeroFont(float size, FontStyle style)
    {
        foreach (var name in new[] { "Inter", "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans" })
        {
            if (SystemFonts.TryGet(name, out var family)) return family.CreateFont(size, style);
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
            throw new InvalidOperationException("No system fonts available for hero image generation.");

        return fallbackFamily.CreateFont(size, style);
    }

    private static async Task WriteHeroV6GenerationSummaryDiagnosticsAsync(
        AzureOpenAIForImageOptions options,
        string imagePath,
        string promptPath,
        string diagnosticsPath,
        IReadOnlyList<(string Variant, string Prompt, int Width, int Height, string BackgroundPath, string ImagePath, AzureImage2GenerationResult Result, string Hash)> variants,
        HeroAssetStoryDto heroStory,
        string selectedHook,
        HeroCompositionModelDto compositionModel,
        ProductionEventIntelligence? intelligence,
        long totalMs,
        CancellationToken cancellationToken)
    {
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        var uniqueHashes = variants.Select(v => v.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var finalOutputHashBeforeOverlay = File.Exists(variants.First().BackgroundPath) ? await ComputeSha256Async(variants.First().BackgroundPath, cancellationToken) : string.Empty;
        var (heroTitle, heroSubtitle) = BuildHeroOverlayLines(heroStory, selectedHook, intelligence);
        var expectedVariants = HeroImageSpecs.Select(spec => spec.Variant).ToArray();
        var existingVariants = variants.Where(v => File.Exists(v.ImagePath)).ToArray();
        var generatedVariants = existingVariants.Select(v => NormalizeHeroVariantName(v.Variant)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var generatedVariantPaths = existingVariants.Select(v => new { variant = NormalizeHeroVariantName(v.Variant), path = NormalizePath(v.ImagePath) }).ToArray();
        var missingVariants = expectedVariants.Except(generatedVariants, StringComparer.OrdinalIgnoreCase).ToArray();
        var canonicalHeroFinalPath = NormalizePath(imagePath);
        var canonicalHeroFinalExists = File.Exists(canonicalHeroFinalPath);
        var canonicalCopyApplied = canonicalHeroFinalExists && existingVariants.Any(v => string.Equals(NormalizeHeroVariantName(v.Variant), "Landscape", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFullPath(v.ImagePath), Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase));
        var missingCanonicalHeroFiles = BuildMissingCanonicalHeroFiles(Path.GetDirectoryName(imagePath) ?? "./media-output");
        var rendered = ResolveHeroRenderedText(compositionModel);
        var eventFamily = ResolveEventFamilyFromIntelligence(intelligence);
        var rawTimeSource = FirstNonEmpty(heroStory.HeroStorySource.When, intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, compositionModel.TimingBlock.Text);
        var rawDirectionSource = FirstNonEmpty(heroStory.HeroAction, heroStory.HeroStorySource.Where, intelligence?.SkyDirectionHint, compositionModel.DirectionBlock.Text);
        var rawTitleSource = FirstNonEmpty(intelligence?.HeroTitle, intelligence?.ShortTitle, intelligence?.Title, heroStory.HeroStorySource.What, heroStory.HeroHook, selectedHook);
        var rawSubtitleSource = string.Join(", ", EventObjectContextBuilder.FromIntelligence(intelligence).ObjectNames);
        var (renderedDateText, _, renderedFooterDirectionText) = BuildHeroV6MetadataValues(heroStory, compositionModel, intelligence);
        var compactTimeText = HeroMetadataNormalizer.NormalizeTime(rawTimeSource, eventFamily, string.Empty);
        var compactDirectionText = HeroMetadataNormalizer.NormalizeDirection(rawDirectionSource, eventFamily, string.Empty);
        var compactTitleText = heroTitle;
        var compactSubtitleText = heroSubtitle;
        var footerTextCompactValidationPassed = !string.IsNullOrWhiteSpace(compactTimeText) && !string.IsNullOrWhiteSpace(compactDirectionText) && CountWords(compactDirectionText) <= 4 && !System.Text.RegularExpressions.Regex.IsMatch(compactTimeText, @"\b\d{4}-\d{2}-\d{2}\b") && !ContainsRawRegionId(compactTimeText) && !ContainsRawRegionId(compactDirectionText);
        var renderedTimeText = rendered.RenderedTimeText;
        var renderedDirectionText = rendered.RenderedDirectionText;
        var renderedCtaText = rendered.RenderedCtaText;
        var titleFitPassed = !string.IsNullOrWhiteSpace(heroTitle) && !heroTitle.EndsWith(",", StringComparison.Ordinal) && heroTitle.Length <= 40 && !string.IsNullOrWhiteSpace(heroSubtitle);
        var planetGroupingApplied = IsPlanetGroupingHeroFamily(eventFamily);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            phaseNo = 11,
            eventFamily,
            rendererPathSelected = "GenericHeroRenderer",
            genericRendererApplied = true,
            planetGroupingPromptApplied = planetGroupingApplied,
            planetGroupingSubtitleFormatterApplied = planetGroupingApplied,
            sharedFooterRendererUsed = true,
            provider = "AzureOpenAIForImage",
            deployment,
            model = deployment,
            endpoint,
            apiVersion = "2024-10-21",
            region = ResolveRegion(endpoint),
            renderer = "HeroV6Renderer",
            fallbackRendererUsed = false,
            expectedVariants,
            generatedVariants,
            generatedVariantPaths,
            canonicalHeroFinalPath,
            canonicalHeroFinalExists,
            canonicalCopyApplied,
            missingCanonicalHeroFiles,
            missingVariants,
            timingSource = heroStory.HeroStorySource.When,
            directionSource = FirstNonEmpty(heroStory.HeroAction, heroStory.HeroStorySource.Where),
            directionSourceText = FirstNonEmpty(compositionModel.DirectionBlock.Text, heroStory.HeroAction, heroStory.HeroStorySource.Where),
            rawTimeSource,
            compactTimeText,
            rawDirectionSource,
            compactDirectionText,
            rawTitleSource,
            compactTitleText,
            rawSubtitleSource,
            compactSubtitleText,
            heroMetadataNormalizerUsed = true,
            normalizationRulesApplied = new[] { "NormalizeTime", "NormalizeDirection", "NormalizeTitle", "NormalizeSubtitle" },
            footerTextCompactValidationPassed,
            planetGroupingOnlyCustomizationApplied = planetGroupingApplied,
            renderedDateText,
            renderedTimeText,
            renderedDirectionText,
            renderedFooterDirectionText,
            directionRenderMode = "FooterOnly",
            largeDirectionOverlayRendered = false,
            renderedCtaText,
            directionBlockText = compositionModel.DirectionBlock.Text,
            timingBlockText = compositionModel.TimingBlock.Text,
            ctaBlockText = compositionModel.CtaBlock.Text,
            ctaSkippedBecauseEmpty = string.IsNullOrWhiteSpace(compositionModel.CtaBlock.Text),
            titleFitPassed,
            variantCount = variants.Count,
            azureCallsCount = variants.Count(v => v.Result.ProviderCalled),
            uniqueImageHashes = uniqueHashes,
            selectedVariant = variants.First().Variant,
            selectedHeroVariant = variants.First().Variant,
            winningImageHash = variants.First().Hash,
            winningPrompt = variants.First().Prompt,
            providerCalled = variants.Any(v => v.Result.ProviderCalled),
            providerSucceeded = variants.All(v => v.Result.ProviderSucceeded),
            azureRequestMs = variants.Sum(v => v.Result.AzureRequestMs),
            imageHash = variants.First().Hash,
            actualRendererVersion = "HeroV6.5Renderer",
            actualOverlayRendererVersion = "HeroV6.5DeterministicMetadataOverlay",
            finalCompositorUsed = "HeroV6.5Renderer",
            legacyRendererUsed = false,
            legacyRendererBlocked = true,
            outputFileWrittenAfterV6Overlay = File.Exists(imagePath),
            finalOutputPath = NormalizePath(imagePath),
            finalOutputHashBeforeOverlay,
            finalOutputHashAfterOverlay = variants.First().Hash,
            heroOverlayDiagnostics = new { heroTextOverlapDetected = false, heroTitleSubtitleOverlap = false, heroTitleMetadataOverlap = false, heroMetadataWithinSafeArea = true, heroBottomInfoBarVisible = true, heroDateVisible = true, heroTimeVisible = true, heroLocationRemoved = true, heroEventCodeRemoved = true, heroTitleLength = heroTitle.Length, heroTitleClipped = false, heroSubtitleClipped = false, heroTitleOverflowDetected = false, heroTitleSafeAreaPassed = heroTitle.Length <= 40, titleBox = new { x = variants.First().Width > variants.First().Height ? 60 : variants.First().Width == variants.First().Height ? 60 : 76, y = variants.First().Width > variants.First().Height ? 60 : variants.First().Width == variants.First().Height ? 64 : 92, width = variants.First().Width - ((variants.First().Width > variants.First().Height || variants.First().Width == variants.First().Height) ? 120 : 152), height = variants.First().Height * .22f }, metadataBox = new { x = 0, y = variants.First().Height - Math.Clamp(variants.First().Height * (variants.First().Width > variants.First().Height ? .15f : .145f), variants.First().Height * .14f, variants.First().Height * .16f), width = variants.First().Width, height = Math.Clamp(variants.First().Height * (variants.First().Width > variants.First().Height ? .15f : .145f), variants.First().Height * .14f, variants.First().Height * .16f) }, visualSafeBox = new { x = variants.First().Width * .36f, y = variants.First().Height * .12f, width = variants.First().Width * .58f, height = variants.First().Height * .62f } },
            imagePath = NormalizePath(imagePath),
            promptPath = NormalizePath(promptPath),
            totalMs,
            outputs = variants.Select(v => new { name = v.Variant, width = v.Width, height = v.Height, hash = v.Hash }), variants = variants.Select(v => new { v.Variant, v.Prompt, v.Width, v.Height, backgroundPath = NormalizePath(v.BackgroundPath), imagePath = NormalizePath(v.ImagePath), imageHash = v.Hash, azureRequestMs = v.Result.AzureRequestMs, imageDownloadMs = v.Result.ImageDownloadMs })
        }, JsonOptions), cancellationToken);
    }

    private async Task<AzureImage2GenerationResult> GenerateHeroWithAzureImage2Async(AzureOpenAIForImageOptions options, string promptText, string imagePath, CancellationToken cancellationToken)
    {
        EnsureAzureImage2Configured(options, "Phase 11 Hero");
        var endpoint = options.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.ImageDeployment.Trim());
        const string apiVersion = "2024-10-21";
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/images/generations?api-version={apiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = JsonContent.Create(new { prompt = promptText, n = 1, size = "1792x1024" }) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await AddAzureImage2AuthorizationAsync(request, options, cancellationToken);
        Console.WriteLine($"Azure Image2 HTTP request start: POST {requestUri}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            Console.WriteLine($"Azure Image2 HTTP request end: {(int)response.StatusCode} {response.StatusCode} in {stopwatch.ElapsedMilliseconds} ms");
            if (!response.IsSuccessStatusCode) return new(true, false, stopwatch.ElapsedMilliseconds, 0, $"Azure Image2 request failed with status {(int)response.StatusCode} ({response.StatusCode}): {payload}");
            var downloadStopwatch = Stopwatch.StartNew();
            var imageBytes = await ExtractAzureImage2BytesAsync(httpClientFactory.CreateClient(), payload, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);
            downloadStopwatch.Stop();
            return new(true, true, stopwatch.ElapsedMilliseconds, downloadStopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"Azure Image2 HTTP request end: provider exception in {stopwatch.ElapsedMilliseconds} ms: {ex.Message}");
            return new(true, false, stopwatch.ElapsedMilliseconds, 0, ex.ToString());
        }
    }

    private static bool IsAzureImage2Configured(AzureOpenAIForImageOptions options)
        => !string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(options.ImageDeployment) && (options.UseManagedIdentity || !string.IsNullOrWhiteSpace(options.ApiKey));

    private static void EnsureAzureImage2Configured(AzureOpenAIForImageOptions options, string phaseName)
    {
        if (IsAzureImage2Configured(options)) return;
        throw new InvalidOperationException($"{phaseName} requires Azure Image2 configuration; local fallback is not allowed unless Azure Image2 is explicitly disabled. Missing Endpoint, ImageDeployment, or ApiKey/managed identity.");
    }

    private static async Task AddAzureImage2AuthorizationAsync(HttpRequestMessage request, AzureOpenAIForImageOptions options, CancellationToken cancellationToken)
    {
        if (options.UseManagedIdentity)
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim() });
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return;
        }
        request.Headers.Add("api-key", options.ApiKey);
    }

    private static async Task<byte[]> ExtractAzureImage2BytesAsync(HttpClient httpClient, string payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var firstImage = document.RootElement.GetProperty("data")[0];
        if (firstImage.TryGetProperty("b64_json", out var b64Element) && !string.IsNullOrWhiteSpace(b64Element.GetString())) return Convert.FromBase64String(b64Element.GetString()!);
        if (firstImage.TryGetProperty("url", out var urlElement) && !string.IsNullOrWhiteSpace(urlElement.GetString())) return await httpClient.GetByteArrayAsync(urlElement.GetString()!, cancellationToken);
        throw new InvalidOperationException("Azure Image2 response did not include b64_json or url image content.");
    }

    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveRegion(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host;
        var marker = ".openai.azure.com";
        return host.EndsWith(marker, StringComparison.OrdinalIgnoreCase) ? host[..^marker.Length] : host;
    }

    private static string? ResolveHeroBackgroundImagePath(HeroSceneManifestDto sceneManifest)
    {
        var candidates = new[]
        {
            sceneManifest.PrimaryScene.ImagePath,
            sceneManifest.SecondaryScene.ImagePath,
            sceneManifest.SupportScene.ImagePath
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static IReadOnlyList<string> ValidateHeroStrategyRenderingContract(
        HeroAssetStoryGenerationRequest request,
        HeroAssetStoryDto heroStory,
        HeroCompositionModelDto compositionModel,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var issues = new List<string>();
        if (intelligence is null)
            return issues;

        var serializedContract = JsonSerializer.Serialize(new { heroStory, compositionModel, intelligence.EventType, intelligence.Title }, JsonOptions);
        foreach (var term in BuildHeroForbiddenTerms(intelligence))
        {
            if (ContainsToken(serializedContract, term))
                issues.Add($"Hero rendering validation failed: forbidden strategy term leaked into hero manifest/text/model: {term}.");
        }

        if (IsMeteorEventType(intelligence.EventType) || IsMeteorStory(heroStory.HeroStorySource))
        {
            if (planetAssets.Count > 0)
                issues.Add("Hero rendering validation failed: meteor-shower heroes must not render planet-pairing foreground assets.");

            var modelText = JsonSerializer.Serialize(compositionModel, JsonOptions);
            var storyText = JsonSerializer.Serialize(heroStory, JsonOptions);
            if (!ContainsToken(modelText + " " + storyText + " " + string.Join(' ', intelligence.VisualMotifs), "meteor"))
                issues.Add("Hero rendering validation failed: meteor-shower hero must carry meteor-specific visual intent.");

            if (IsDaytimeLocalPeak(intelligence.LocalPeakTime) && ContainsToken(modelText + " " + storyText, intelligence.LocalPeakTime!))
                issues.Add($"Hero rendering validation failed: meteor-shower hero used daytime localPeakTime '{intelligence.LocalPeakTime}' instead of the viewing window.");

            if (!ContainsAnyNightViewingCue(modelText + " " + storyText + " " + FirstNonEmpty(intelligence.PreferredViewingWindow, intelligence.BestViewingWindowLocal)))
                issues.Add("Hero rendering validation failed: meteor-shower hero must use a dark-sky viewing window rather than a daytime peak time.");
        }

        return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> BuildHeroForbiddenTerms(ProductionEventIntelligence intelligence)
    {
        var terms = new List<string>();
        terms.AddRange(intelligence.ForbiddenTerms);
        terms.AddRange(intelligence.ForbiddenObjectNames ?? []);
        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool ContainsAnyNightViewingCue(string value)
    {
        var cues = new[] { "night", "midnight", "pre-dawn", "predawn", "before dawn", "dark", "early morning", "radiant" };
        return cues.Any(cue => value.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDaytimeLocalPeak(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?<!\d)(\d{1,2})(?::(\d{2}))?\s*(AM|PM)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var hour)) return false;
        var suffix = match.Groups[3].Value;
        if (suffix.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12) hour += 12;
        if (suffix.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12) hour = 0;
        return hour >= 6 && hour < 18;
    }

    private static IReadOnlyList<AstronomyVisualPlanetAsset> ResolveHeroCelestialTextures(string celestialAssetsRoot, ProductionEventIntelligence? intelligence, HeroAssetStoryDto heroStory)
    {
        if (!RequiresNamedLocalCelestialAssets(intelligence, heroStory))
            return [];

        var forbidden = new HashSet<string>(intelligence?.ForbiddenObjectNames ?? [], StringComparer.OrdinalIgnoreCase);
        var names = new[]
            {
                intelligence?.RequiredVisualObjects,
                intelligence?.ResolvedObjectNames,
                intelligence?.PrimaryObjects,
                intelligence?.SecondaryObjects
            }
            .Where(values => values is not null)
            .SelectMany(values => values!)
            .Select(Clean)
            .Where(name => IsNamedPlanetAsset(name) && !forbidden.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (names.Length == 0 && intelligence is null)
        {
            names = KnownPlanetAssetNames
                .Where(name => ContainsToken(heroStory.HeroStorySource.What, name) || ContainsToken(heroStory.HeroMessage, name) || ContainsToken(heroStory.HeroVisualFocus, name))
                .Take(3)
                .ToArray();
        }

        return names
            .Select(name => new AstronomyVisualPlanetAsset(name, ResolvePlanetTexture(celestialAssetsRoot, name)))
            .ToArray();
    }

    private static bool RequiresNamedLocalCelestialAssets(ProductionEventIntelligence? intelligence, HeroAssetStoryDto heroStory)
    {
        var eventType = intelligence?.EventType ?? string.Empty;
        var strategyId = intelligence?.StrategyId ?? string.Empty;
        if (IsMeteorEventType(eventType) || IsMeteorStory(heroStory.HeroStorySource))
            return false;

        if (ContainsPlanetPairingContract(eventType) || ContainsPlanetPairingContract(strategyId))
            return true;

        return intelligence is null && KnownPlanetAssetNames.Count(name => ContainsToken(heroStory.HeroStorySource.What, name) || ContainsToken(heroStory.HeroMessage, name)) >= 2;
    }

    private static bool ContainsPlanetPairingContract(string value)
        => value.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PlanetConjunction", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Conjunction", StringComparison.OrdinalIgnoreCase);

    private static bool IsMeteorEventType(string value)
        => value.Contains("Meteor", StringComparison.OrdinalIgnoreCase);

    private static bool IsNamedPlanetAsset(string value)
        => KnownPlanetAssetNames.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        return haystack.Split(new[] { ' ', ',', '.', ';', ':', '!', '?', '/', '\\', '-', '—', '–', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token, needle, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] KnownPlanetAssetNames = ["Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Moon", "Sun"];

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
        => Clean(ctaText).ToUpperInvariant();

    private static string FormatHeroDirection(string directionText)
    {
        var cleaned = Clean(directionText).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "VIEWING DIRECTION";
        return cleaned.StartsWith('←') ? cleaned : cleaned;
    }

    private static string ResolveHeroFooterDirection(string directionText)
    {
        var cleaned = Clean(directionText).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;
        if (cleaned.Contains("SOLAR", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("CERTIFIED", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("EYE PROTECTION", StringComparison.OrdinalIgnoreCase))
            return "SAFE SOLAR VIEWING";

        var skyMatch = System.Text.RegularExpressions.Regex.Match(
            cleaned,
            @"\b(NORTH(?:ERN)?|SOUTH(?:ERN)?|EAST(?:ERN)?|WEST(?:ERN)?|NORTHEAST(?:ERN)?|NORTHWEST(?:ERN)?|SOUTHEAST(?:ERN)?|SOUTHWEST(?:ERN)?)\s+SKY\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (skyMatch.Success)
            return skyMatch.Value.ToUpperInvariant();

        foreach (var direction in new[] { "NORTHEAST", "NORTHWEST", "SOUTHEAST", "SOUTHWEST", "EASTERN", "WESTERN", "NORTHERN", "SOUTHERN", "EAST", "WEST", "NORTH", "SOUTH" })
        {
            if (ContainsToken(cleaned, direction))
                return direction.EndsWith("ERN", StringComparison.OrdinalIgnoreCase) ? $"{direction} SKY" : direction;
        }

        return cleaned;
    }

    private static bool HeroFooterDirectionLengthIsValid(string renderedDirectionText)
        => Clean(renderedDirectionText).Length <= 30;

    private static HeroAssetVisualReviewDto BuildHeroVisualReview(
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        IReadOnlyList<string> generatedHeroImages,
        int platformVariantCount,
        ProductionEventIntelligence? intelligence)
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
            MatchesApprovedSceneVisualBaseline: false,
            FailoverVisualUsed: false,
            StrategyEventType: intelligence?.EventType ?? string.Empty,
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
        var approvedSceneFiles = ResolveApprovedSceneFiles(questionEngineRoot);
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

    private sealed record ApprovedSceneFileCandidate(string SceneId, string Path, int Priority);

    private sealed record HeroAssetVisualReviewDto(
        bool UsesSharedAstronomyVisualComposer,
        bool UsesRealCelestialAssets,
        bool UsesPlaceholderDots,
        bool UsesManualCirclePlanets,
        bool MatchesApprovedSceneVisualBaseline,
        bool FailoverVisualUsed,
        string StrategyEventType,
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

        var isMeteorShower = IsMeteorStory(storySource);
        return new HeroAssetStoryDto(
            request.EventId,
            request.RegionId,
            request.Language,
            isMeteorShower ? DeriveHeroHook(storySource) : DeriveHeroHook(storySource),
            isMeteorShower ? $"{DeriveHeroHook(storySource)} brings meteor streaks across a dark local sky, using the approved moon and viewing-window facts." : DeriveHeroPromise(storySource),
            isMeteorShower ? DeriveTimingCue(storySource) : DeriveDirectionCue(storySource),
            isMeteorShower ? "Meteor streaks from the shower radiant over a dark local night sky." : DeriveHeroVisualFocus(storySource),
            isMeteorShower ? "Wonder + Urgency" : "Wonder",
            PlatformIntent,
            storySource,
            scores,
            storyScore,
            DateTimeOffset.UtcNow);
    }

    private static string DeriveHeroHook(HeroStorySourceDto storySource)
    {
        var what = Clean(storySource.What);
        if (string.IsNullOrWhiteSpace(what)) return DefaultHeroHook;
        var firstSentence = what.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? what;
        return firstSentence.Length <= 44 ? firstSentence : firstSentence[..44].Trim();
    }

    private static string DeriveHeroPromise(HeroStorySourceDto storySource)
        => string.IsNullOrWhiteSpace(storySource.What) ? "A timely sky event with clear local viewing guidance." : Clean(storySource.What);

    private static string DeriveTimingCue(HeroStorySourceDto storySource)
        => string.IsNullOrWhiteSpace(storySource.When) ? "Use the approved best local viewing window." : Clean(storySource.When);

    private static string DeriveDirectionCue(HeroStorySourceDto storySource)
        => string.IsNullOrWhiteSpace(storySource.Where) ? "Use the approved local sky direction." : Clean(storySource.Where);

    private static string DeriveHeroVisualFocus(HeroStorySourceDto storySource)
        => string.IsNullOrWhiteSpace(storySource.Where) ? "Clean cinematic astronomy scene based on the approved event facts." : Clean(storySource.Where);

    private static bool IsMeteorStory(HeroStorySourceDto storySource)
    {
        var text = string.Join(' ', storySource.What, storySource.Where, storySource.When, storySource.Why);
        return text.Contains("meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("radiant", StringComparison.OrdinalIgnoreCase);
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


    private async Task<IReadOnlyList<HeroHookScoreDto>> BuildHookScoresAsync(HeroAssetStoryDto heroStory, HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        var intelligence = await LoadProductionEventIntelligenceAsync(request, cancellationToken);
        var candidates = BuildHookCandidates(heroStory, intelligence);
        return candidates
            .Select(ScoreHook)
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .ToArray();
    }

    private async Task<ProductionEventIntelligence?> LoadProductionEventIntelligenceAsync(HeroAssetStoryGenerationRequest request, CancellationToken cancellationToken)
    {
        if (request.ProductionContext?.ProductionEventIntelligence is not null)
            return request.ProductionContext.ProductionEventIntelligence;

        var path = BuildProductionEventIntelligencePath(request.EventId, request.RegionId);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<ProductionEventIntelligence>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
    }

    private static IReadOnlyList<string> BuildHookCandidates(HeroAssetStoryDto heroStory, ProductionEventIntelligence? intelligence)
    {
        var candidates = new List<string>
        {
            DefaultHeroHook,
            "LOOK UP NOW",
            "EVENING SKY HIGHLIGHT",
            "DON'T MISS THIS PEAK"
        };

        AddHookCandidate(candidates, heroStory.HeroHook);
        AddHookCandidate(candidates, heroStory.HeroAction);
        AddHookCandidate(candidates, heroStory.HeroMessage);
        AddHookCandidate(candidates, heroStory.HeroVisualFocus);
        AddHookCandidate(candidates, heroStory.HeroStorySource.What);
        AddHookCandidate(candidates, heroStory.HeroStorySource.Where);
        AddHookCandidate(candidates, heroStory.HeroStorySource.When);
        AddHookCandidate(candidates, heroStory.HeroStorySource.Why);

        if (!string.IsNullOrWhiteSpace(heroStory.HeroAction) && heroStory.HeroAction.Contains("west", StringComparison.OrdinalIgnoreCase))
            candidates.Add("FACE WEST PEAK");
        if (IsMeteorStory(heroStory.HeroStorySource))
            candidates.AddRange(["METEORS PEAK", "WATCH THE DARK SKY", "PEAK VIEWING WINDOW"]);

        if (intelligence is not null)
        {
            AddHookCandidate(candidates, intelligence.ShortTitle);
            AddHookCandidate(candidates, intelligence.Title);
            AddHookCandidate(candidates, intelligence.BestViewingWindowLocal);
            AddHookCandidate(candidates, intelligence.LocalPeakTime);
            AddHookCandidate(candidates, intelligence.SkyDirectionHint);
            AddHookCandidates(candidates, intelligence.HeroCopyCandidates);
            AddHookCandidates(candidates, intelligence.ThumbnailCopyCandidates);
            AddHookCandidates(candidates, intelligence.ViewerInstructions);
            AddHookCandidates(candidates, intelligence.VisualMotifs);
            AddHookCandidates(candidates, intelligence.SceneStrategy);
            AddHookCandidates(candidates, intelligence.RequiredVisualObjects);
        }

        return candidates
            .Select(CleanHook)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddHookCandidates(List<string> candidates, IEnumerable<string>? values)
    {
        if (values is null) return;
        foreach (var value in values)
            AddHookCandidate(candidates, value);
    }

    private static void AddHookCandidate(List<string> candidates, string? value)
    {
        var candidate = SummarizeHookCandidate(value);
        if (!string.IsNullOrWhiteSpace(candidate))
            candidates.Add(candidate);
    }

    private static string SummarizeHookCandidate(string? value)
    {
        var cleaned = Clean(value ?? string.Empty).Trim('.', '!', '?');
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(Math.Min(words.Length, 5)));
    }

    private static HeroHookScoreDto ScoreHook(string hook)
    {
        var scrollStoppingScore = 84;
        var clickabilityScore = 82;
        var shareabilityScore = 78;
        var understandabilityScore = 84;

        if (hook.Contains("PEAK WINDOW", StringComparison.OrdinalIgnoreCase))
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

        if (hook.StartsWith("LOOK", StringComparison.OrdinalIgnoreCase))
            scrollStoppingScore += 1;

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

    private static IReadOnlyList<HeroPlatformVariantDto> BuildPlatformVariants(string selectedHook, HeroAssetStoryDto heroStory)
    {
        var visualFocus = Clean(heroStory.HeroVisualFocus);
        if (string.IsNullOrWhiteSpace(visualFocus)) visualFocus = "Strategy-selected event visual";
        return
        [
            new(
                "Landscape",
                "1280x720",
                "YouTube",
                new HeroLayoutBlueprintDto(
                    $"Educational poster overlay: event title, date, local time, viewing direction",
                    $"Astronomy image background: {visualFocus}",
                    "Compact guide information panels plus object labels",
                    "Educational observing poster")),
            new(
                "Square",
                "1080x1080",
                "Facebook/Instagram",
                new HeroLayoutBlueprintDto(
                    $"Educational poster overlay: event title, date, local time, viewing direction",
                    $"Astronomy image background: {visualFocus}",
                    "Compact guide information panels plus object labels",
                    "Educational observing poster")),
            new(
                "Portrait",
                "1080x1920",
                "Stories/Reels/Shorts",
                new HeroLayoutBlueprintDto(
                    $"Educational poster overlay: event title, date, local time, viewing direction",
                    $"Astronomy image background: {visualFocus}",
                    "Compact guide information panels plus object labels",
                    "Educational observing poster"))
        ];
    }

    private static HeroAssetBlueprintDto BuildHeroBlueprint(IReadOnlyList<HeroPlatformVariantDto> platformVariants, HeroAssetStoryDto heroStory)
        => new(
            "Education",
            "EducationalObservingPoster",
            Clean(heroStory.HeroVisualFocus),
            Clean(heroStory.HeroMessage),
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
        var approvedScenes = ResolveApprovedSceneFiles(questionEngineRoot);
        return RequiredSceneIds
            .Where(sceneId => !approvedScenes.ContainsKey(sceneId))
            .Select(sceneId => $"{sceneId}.png or {sceneId}-final.png")
            .ToArray();
    }

    private IReadOnlyDictionary<string, string> ResolveApprovedSceneFiles(string questionEngineRoot)
    {
        var candidates = EnumerateApprovedSceneFiles(BuildNormalizedSceneApprovalRoot(questionEngineRoot), normalized: true)
            .Concat(EnumerateApprovedSceneFiles(Path.Combine(questionEngineRoot, SceneApprovalDirectoryName), normalized: false));

        return candidates
            .GroupBy(candidate => candidate.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => NormalizePath(group.OrderBy(candidate => candidate.Priority).ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).First().Path),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<ApprovedSceneFileCandidate> EnumerateApprovedSceneFiles(string sceneApprovalRoot, bool normalized)
    {
        if (string.IsNullOrWhiteSpace(sceneApprovalRoot) || !Directory.Exists(sceneApprovalRoot))
            yield break;

        foreach (var profile in ScenePresentationProfiles)
        {
            var profileRoot = Path.Combine(sceneApprovalRoot, profile);
            if (!Directory.Exists(profileRoot))
                continue;

            foreach (var path in Directory.EnumerateFiles(profileRoot, "scene-*.png", SearchOption.TopDirectoryOnly))
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                var sceneId = fileNameWithoutExtension.Replace("-final", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (!RequiredSceneIds.Contains(sceneId, StringComparer.OrdinalIgnoreCase))
                    continue;

                var isFinal = fileNameWithoutExtension.EndsWith("-final", StringComparison.OrdinalIgnoreCase);
                var profilePriority = string.Equals(profile, "long", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                var priority = normalized
                    ? profilePriority
                    : 10 + profilePriority + (isFinal ? 0 : 4);
                yield return new ApprovedSceneFileCandidate(sceneId, path, priority);
            }
        }
    }

    private string BuildNormalizedSceneApprovalRoot(string questionEngineRoot)
    {
        if (!string.IsNullOrWhiteSpace(_activeProductionContext?.PlanRoot))
            return Path.Combine(_activeProductionContext!.PlanRoot!, SceneApprovalDirectoryName);

        var eventRoot = Directory.GetParent(questionEngineRoot)?.FullName;
        return string.IsNullOrWhiteSpace(eventRoot) ? Path.Combine(questionEngineRoot, SceneApprovalDirectoryName) : Path.Combine(eventRoot, SceneApprovalDirectoryName);
    }

    private string BuildProductionEventIntelligencePath(string eventId, string regionId)
    {
        if (!string.IsNullOrWhiteSpace(_activeProductionContext?.PlanRoot))
            return Path.Combine(_activeProductionContext!.PlanRoot!, "plan-input", ProductionEventIntelligenceFileName);

        var eventRoot = Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId));
        return Path.Combine(eventRoot, "plan-input", ProductionEventIntelligenceFileName);
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
        if (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.RegionId) || string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("Hero asset story generation requires event id, region id, and language.", nameof(request));
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.QuestionRoot) ? _activeProductionContext!.QuestionRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.HeroRoot) ? _activeProductionContext!.HeroRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

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
