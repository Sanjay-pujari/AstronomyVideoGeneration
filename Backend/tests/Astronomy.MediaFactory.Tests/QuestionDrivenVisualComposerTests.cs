using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Tests;

public sealed class QuestionDrivenVisualComposerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateQuestionDrivenVisualsAsync_DryRunReturnsCompletePreviewPlanWithoutWritingFiles()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        var result = await composer.GenerateQuestionDrivenVisualsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.Equal(0, result.FinalImageCount);
        Assert.Equal(0, result.SrtCount);
        Assert.Equal(6, result.PlannedImageCount);
        Assert.Equal(6, result.PlannedSrtCount);
        Assert.Equal(6, result.PlannedReviewCount);
        Assert.Equal(0, result.ApprovedSceneCount);
        Assert.Equal(0, result.FailedSceneCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.PlannedScenes);
        Assert.Equal(6, result.PlannedScenes!.Count);
        Assert.False(Directory.Exists(BuildSceneApprovalPath(workingDirectory)));

        Assert.All(result.PlannedScenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.NarrationText));
            Assert.False(string.IsNullOrWhiteSpace(scene.CaptionText));
            Assert.False(string.IsNullOrWhiteSpace(scene.AiBackgroundPrompt));
            Assert.False(string.IsNullOrWhiteSpace(scene.PlannedOutputs.FinalImagePath));
            Assert.False(string.IsNullOrWhiteSpace(scene.PlannedOutputs.SrtPath));
            Assert.True(scene.ValidationPreview.ImageSceneSpecific);
            Assert.True(scene.ValidationPreview.NarrationAligned);
            Assert.True(scene.ValidationPreview.SrtReady);
            Assert.True(scene.ValidationPreview.AccessibilityReady);
            Assert.Empty(scene.ValidationPreview.Issues);
        });

        var what = result.PlannedScenes[0];
        Assert.Equal(AstronomyQuestionTypes.What, what.QuestionType);
        Assert.Contains("professional astronomy magazine cover", what.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("golden-orange horizon", what.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Venus & Jupiter", what.ProgrammaticOverlayPlan.Title);
        Assert.Contains("Venus", what.ProgrammaticOverlayPlan.LocalAssetObjects);
        Assert.Contains("Jupiter", what.ProgrammaticOverlayPlan.LocalAssetObjects);
        Assert.Empty(what.ProgrammaticOverlayPlan.TimingMarkers);
        Assert.Empty(what.ProgrammaticOverlayPlan.Steps);

        var where = result.PlannedScenes[1];
        Assert.Equal(AstronomyQuestionTypes.Where, where.QuestionType);
        Assert.Contains("observation chart", where.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("West", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Venus", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Jupiter", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Horizon", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("West", where.ProgrammaticOverlayPlan.DirectionMarkers);

        var when = result.PlannedScenes[2];
        Assert.Equal(AstronomyQuestionTypes.When, when.QuestionType);
        Assert.Contains("real twilight transition", when.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7:23 PM IST", when.ProgrammaticOverlayPlan.TimingMarkers);
        Assert.Contains("Time", when.ProgrammaticOverlayPlan.Title);

        var how = result.PlannedScenes[3];
        Assert.Equal(AstronomyQuestionTypes.How, how.QuestionType);
        Assert.Equal(new[] { "Find Venus", "Look nearby for Jupiter", "Face west" }, how.ProgrammaticOverlayPlan.Steps);
        Assert.NotEmpty(how.ProgrammaticOverlayPlan.Arrows);

        var why = result.PlannedScenes[4];
        Assert.Equal(AstronomyQuestionTypes.Why, why.QuestionType);
        Assert.Contains("two of the brightest worlds sharing the evening sky", why.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Why It Matters", why.ProgrammaticOverlayPlan.Title);
        Assert.Contains("Venus", why.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Jupiter", why.ProgrammaticOverlayPlan.Labels);

        var action = result.PlannedScenes[5];
        Assert.Equal(AstronomyQuestionTypes.Action, action.QuestionType);
        Assert.Contains("poster-quality twilight", action.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Step Outside Tonight", action.ProgrammaticOverlayPlan.Title);
        Assert.Empty(action.ProgrammaticOverlayPlan.Steps);
    }


    [Fact]
    public async Task GenerateEditorialAstronomyInfographicsAsync_DryRunPlansLongAndShortSceneApprovalVariants()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        var result = await composer.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.Equal(12, result.PlannedInfographicCount);
        Assert.Equal(0, result.FinalImageCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.NotNull(result.SceneVariantFinalImages);
        Assert.Equal("LongForm", result.SceneVariantFinalImages!.LongForm.Profile);
        Assert.Equal(1920, result.SceneVariantFinalImages.LongForm.Width);
        Assert.Equal(1080, result.SceneVariantFinalImages.LongForm.Height);
        Assert.Equal("ShortForm", result.SceneVariantFinalImages.ShortForm.Profile);
        Assert.Equal(1080, result.SceneVariantFinalImages.ShortForm.Width);
        Assert.Equal(1920, result.SceneVariantFinalImages.ShortForm.Height);
        Assert.Equal(6, result.SceneVariantFinalImages.LongForm.Images.Count);
        Assert.Equal(6, result.SceneVariantFinalImages.ShortForm.Images.Count);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics!.SceneVariantGenerationEnabled);
        Assert.NotNull(result.ShortFormValidation);
        Assert.True(result.ShortFormValidation!.NativeShortFormComposerUsed);
        Assert.False(result.ShortFormValidation.EmbeddedLongFormImageDetected);
        Assert.False(result.ShortFormValidation.InnerFrameDetected);
        Assert.Equal(1080, result.ShortFormValidation.ShortFormWidth);
        Assert.Equal(1920, result.ShortFormValidation.ShortFormHeight);

        foreach (var sceneNumber in Enumerable.Range(1, 6))
        {
            var key = $"scene-{sceneNumber:000}";
            Assert.True(result.SceneVariantFinalImages.LongForm.Images.ContainsKey(key));
            Assert.True(result.SceneVariantFinalImages.ShortForm.Images.ContainsKey(key));
            Assert.Contains($"scene-approval-v3/long/{key}-final.png", result.SceneVariantFinalImages.LongForm.Images[key]);
            Assert.Contains($"scene-approval-v3/short/{key}-final.png", result.SceneVariantFinalImages.ShortForm.Images[key]);
        }

        Assert.All(result.PlannedScenes, scene =>
        {
            Assert.Contains("scene-approval-v3/", scene.PlannedOutputs.FinalImagePath);
            Assert.DoesNotContain("scene-approval-v3/long/", scene.PlannedOutputs.FinalImagePath);
            Assert.NotNull(scene.PlannedOutputs.PresentationVariants);
            Assert.Contains("scene-approval-v3/long/", scene.PlannedOutputs.PresentationVariants!.LongFormFinalImagePath);
            Assert.Contains("scene-approval-v3/short/", scene.PlannedOutputs.PresentationVariants.ShortFormFinalImagePath);
            Assert.False(string.IsNullOrWhiteSpace(scene.NarrationText));
            Assert.False(string.IsNullOrWhiteSpace(scene.CaptionText));
            Assert.True(scene.ValidationPreview.ImageSceneSpecific);
            Assert.True(scene.ValidationPreview.NarrationAligned);
            Assert.Empty(scene.ValidationPreview.Issues);
        });
    }



    [Fact]
    public async Task GenerateEditorialAstronomyInfographicsAsync_WritesLongAndShortVariantImages()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        var result = await composer.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(12, result.FinalImageCount);
        Assert.NotNull(result.SceneVariantFinalImages);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics!.LongFormGenerated);
        Assert.True(result.Diagnostics.ShortFormGenerated);
        Assert.Equal(6, result.Diagnostics.LongFormImageCount);
        Assert.Equal(6, result.Diagnostics.ShortFormImageCount);
        Assert.NotNull(result.ShortFormValidation);
        Assert.True(result.ShortFormValidation!.NativeShortFormComposerUsed);
        Assert.False(result.ShortFormValidation.EmbeddedLongFormImageDetected);
        Assert.False(result.ShortFormValidation.InnerFrameDetected);
        Assert.Equal(6, result.ShortFormValidation.ShortFormImageCount);
        Assert.Equal(1080, result.ShortFormValidation.ShortFormWidth);
        Assert.Equal(1920, result.ShortFormValidation.ShortFormHeight);
        Assert.InRange(result.ShortFormValidation.ShortFormReadabilityScore, 90, 100);
        Assert.InRange(result.ShortFormValidation.ShortFormReelSuitabilityScore, 90, 100);

        var longRoot = Path.Combine(BuildSceneApprovalPath(workingDirectory), "long");
        var shortRoot = Path.Combine(BuildSceneApprovalPath(workingDirectory), "short");
        Assert.True(Directory.Exists(longRoot));
        Assert.True(Directory.Exists(shortRoot));
        Assert.Equal(6, Directory.EnumerateFiles(longRoot, "scene-*-final.png").Count());
        Assert.Equal(6, Directory.EnumerateFiles(shortRoot, "scene-*-final.png").Count());

        var polishValidationPath = Path.Combine(shortRoot, "shortform-polish-validation.json");
        Assert.True(File.Exists(polishValidationPath));
        Assert.Contains(Normalize(polishValidationPath), result.GeneratedFiles);
        using (var polishValidationDocument = JsonDocument.Parse(await File.ReadAllTextAsync(polishValidationPath)))
        {
            var root = polishValidationDocument.RootElement;
            Assert.True(root.GetProperty("shortFormPolishApplied").GetBoolean());
            Assert.False(root.GetProperty("decorativeEllipseOverlayDetected").GetBoolean());
            Assert.True(root.GetProperty("scene2GuideComplexityReduced").GetBoolean());
            Assert.True(root.GetProperty("scene3TimelineSimplified").GetBoolean());
            Assert.True(root.GetProperty("scene5PlanetProximityEnhanced").GetBoolean());
            Assert.True(root.GetProperty("scene6CtaEnhanced").GetBoolean());
            Assert.True(root.GetProperty("captionDensityReduced").GetBoolean());
            Assert.True(root.GetProperty("shortFormPolishScore").GetInt32() >= 95);
        }

        foreach (var sceneNumber in Enumerable.Range(1, 6))
        {
            var key = $"scene-{sceneNumber:000}";
            var longPath = Path.Combine(longRoot, $"{key}-final.png");
            var shortPath = Path.Combine(shortRoot, $"{key}-final.png");
            Assert.True(File.Exists(longPath));
            Assert.True(File.Exists(shortPath));
            Assert.Contains(Normalize(longPath), result.GeneratedFiles);
            Assert.Contains(Normalize(shortPath), result.GeneratedFiles);
            using var longImage = Image.Load(longPath);
            using var shortImage = Image.Load(shortPath);
            Assert.Equal(1920, longImage.Width);
            Assert.Equal(1080, longImage.Height);
            Assert.Equal(1080, shortImage.Width);
            Assert.Equal(1920, shortImage.Height);
            Assert.Contains($"scene-approval-v3/long/{key}-final.png", result.SceneVariantFinalImages!.LongForm.Images[key]);
            Assert.Contains($"scene-approval-v3/short/{key}-final.png", result.SceneVariantFinalImages.ShortForm.Images[key]);
        }
    }

    [Fact]
    public async Task GenerateQuestionDrivenVisualsAsync_PlanetGroupingPropagatesInfographicMetadata()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WritePlanetGroupingInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        await composer.GenerateQuestionDrivenVisualsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            ProductionContext: new ProductionPipelineExecutionContext(
                UseProductionPipeline: true,
                ContentGenerationPlanId: null,
                AstronomyEventIntelligenceId: null,
                SourceExternalEventId: "PLANET_GROUPING_2026",
                IsDbApprovedPlanExecution: false,
                EventType: "PLANET_GROUPING",
                ProductionEventIntelligence: BuildPlanetGroupingIntelligence())), CancellationToken.None);

        var specPath = Path.Combine(BuildSceneApprovalPath(workingDirectory), "scene-002-infographic-spec.json");
        Assert.True(File.Exists(specPath));
        using var specDocument = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
        var spec = specDocument.RootElement;

        Assert.Equal("PlanetGrouping", spec.GetProperty("strategyId").GetString());
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(spec.GetProperty("requiredVisualObjects")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(spec.GetProperty("requiredCelestialObjects")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(spec.GetProperty("resolvedObjectNames")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(spec.GetProperty("visibleObjects")));
        Assert.Equal(new[] { "planet grouping", "guided scan path", "grouping arc" }, ReadStringArray(spec.GetProperty("visualMotifs")));
        Assert.DoesNotContain("planet grouping", spec.GetProperty("visualSourceResolution").GetProperty("validationRequiredTerms").EnumerateArray().Select(item => item.GetString()), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("guided scan path", spec.GetProperty("visualSourceResolution").GetProperty("validationRequiredTerms").EnumerateArray().Select(item => item.GetString()), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("grouping arc", spec.GetProperty("visualSourceResolution").GetProperty("validationRequiredTerms").EnumerateArray().Select(item => item.GetString()), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, spec.GetProperty("drawableVisualObjects").EnumerateArray().Select(item => item.GetProperty("objectType").GetString()).ToArray());
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, spec.GetProperty("visualSourceResolution").GetProperty("requiredDrawableObjects").EnumerateArray().Select(item => item.GetString()).ToArray());

        var diagnosticsPath = Path.Combine(BuildSceneApprovalPath(workingDirectory), "phase8-visual-source-diagnostics.json");
        Assert.True(File.Exists(diagnosticsPath));
        using var diagnosticsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
        var diagnostics = diagnosticsDocument.RootElement;
        var summary = diagnostics.GetProperty("phase8VisualSourceDiagnostics");
        Assert.Equal("question-driven-scene-plan.enriched.json", summary.GetProperty("expectedSource").GetString());
        Assert.Equal("question-driven-scene-plan.enriched.json", summary.GetProperty("actualSourceUsed").GetString());
        Assert.True(summary.GetProperty("usedEnrichedScenePlan").GetBoolean());
        Assert.False(summary.GetProperty("usedFallbackTemplate").GetBoolean());
        Assert.False(summary.GetProperty("gapDetected").GetBoolean());

        var sceneDiagnostics = diagnostics.GetProperty("scenes").EnumerateArray().Single(scene => scene.GetProperty("sceneNumber").GetInt32() == 2);
        Assert.Equal("question-driven-scene-plan.enriched.json", sceneDiagnostics.GetProperty("selectedVisualSourceType").GetString());
        Assert.Contains("question-driven-scene-plan.enriched.json", sceneDiagnostics.GetProperty("selectedVisualSourceFile").GetString());
        Assert.Contains("Visual intent", sceneDiagnostics.GetProperty("selectedVisualIntent").GetString());
        Assert.Contains("Image prompt intent", sceneDiagnostics.GetProperty("selectedImagePromptIntent").GetString());
        Assert.Contains("Overlay intent", sceneDiagnostics.GetProperty("selectedOverlayIntent").GetString());
        Assert.Equal("Follow the scan path.", sceneDiagnostics.GetProperty("selectedCaptionText").GetString());
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(sceneDiagnostics.GetProperty("selectedRequiredVisualObjects")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(sceneDiagnostics.GetProperty("selectedResolvedObjectNames")));
        Assert.Equal("PlanetGrouping", sceneDiagnostics.GetProperty("selectedStrategyId").GetString());
        Assert.True(sceneDiagnostics.GetProperty("usedEnrichedScenePlan").GetBoolean());
        Assert.False(sceneDiagnostics.GetProperty("usedFallbackVisualTemplate").GetBoolean());
        Assert.Equal(string.Empty, sceneDiagnostics.GetProperty("fallbackReason").GetString());
        var rendererPrompt = sceneDiagnostics.GetProperty("rendererPromptBeforeRendering").GetString();
        Assert.False(string.IsNullOrWhiteSpace(rendererPrompt));
        Assert.Contains("Saturn", rendererPrompt);
        Assert.Contains("Mars", rendererPrompt);
        Assert.Contains("Jupiter", rendererPrompt);
        Assert.Contains("Venus", rendererPrompt);
        Assert.Contains("scene-002-infographic-spec.json", sceneDiagnostics.GetProperty("infographicSpecPath").GetString());
        Assert.True(sceneDiagnostics.GetProperty("infographicSpecContainsPlanetGroupingMetadata").GetBoolean());
        Assert.True(sceneDiagnostics.GetProperty("infographicSpecContainsResolvedObjects").GetBoolean());

        var mappingDiagnostics = sceneDiagnostics.GetProperty("buildSpecMappingDiagnostics");
        Assert.Contains("Visual intent", mappingDiagnostics.GetProperty("visualIntent").GetProperty("sourceValue").GetString());
        Assert.False(string.IsNullOrWhiteSpace(mappingDiagnostics.GetProperty("visualIntent").GetProperty("mappedValue").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(mappingDiagnostics.GetProperty("visualIntent").GetProperty("finalSerializedValue").GetString()));
        Assert.Contains("Image prompt intent", mappingDiagnostics.GetProperty("imagePromptIntent").GetProperty("sourceValue").GetString());
        Assert.Equal(new[] { "Saturn, Mars, Jupiter, Venus", "sky direction", "local horizon", "guided scan path" }, ReadStringArray(mappingDiagnostics.GetProperty("overlayIntent").GetProperty("mappedValue")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(mappingDiagnostics.GetProperty("requiredVisualObjects").GetProperty("mappedValue")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(mappingDiagnostics.GetProperty("requiredVisualObjects").GetProperty("finalSerializedValue")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(mappingDiagnostics.GetProperty("resolvedObjectNames").GetProperty("mappedValue")));
        Assert.Equal(new[] { "Saturn", "Mars", "Jupiter", "Venus" }, ReadStringArray(mappingDiagnostics.GetProperty("resolvedObjectNames").GetProperty("finalSerializedValue")));
        Assert.Equal("PlanetGrouping", mappingDiagnostics.GetProperty("strategyId").GetProperty("sourceValue").GetString());
        Assert.Equal("PlanetGrouping", mappingDiagnostics.GetProperty("strategyId").GetProperty("mappedValue").GetString());
        Assert.Equal("PlanetGrouping", mappingDiagnostics.GetProperty("strategyId").GetProperty("finalSerializedValue").GetString());
    }


    [Fact]
    public async Task GenerateEditorialAstronomyInfographicsAsync_MeteorShowerDisablesLocalPlanetAssetsInReviews()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteMeteorInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        var result = await composer.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            ProductionContext: new ProductionPipelineExecutionContext(
                UseProductionPipeline: true,
                ContentGenerationPlanId: null,
                AstronomyEventIntelligenceId: null,
                SourceExternalEventId: "GEMINIDS_2026",
                IsDbApprovedPlanExecution: false,
                EventType: "MeteorShower")), CancellationToken.None);

        Assert.Equal(12, result.FinalImageCount);
        Assert.All(result.PlannedScenes, scene =>
        {
            Assert.Contains("meteor", scene.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Venus", scene.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Jupiter", scene.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(scene.ProgrammaticOverlayPlan.LocalAssetObjects);
        });

        foreach (var sceneNumber in Enumerable.Range(1, 6))
        {
            var reviewPath = Path.Combine(BuildSceneApprovalPath(workingDirectory), $"scene-{sceneNumber:000}-review.json");
            Assert.True(File.Exists(reviewPath));
            using var reviewDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reviewPath));
            var review = reviewDocument.RootElement;
            Assert.False(review.GetProperty("usesLocalPlanetAssets").GetBoolean());
            Assert.False(review.GetProperty("planetAssetsIntegratedIntoSky").GetBoolean());
        }
    }

    private static QuestionDrivenVisualComposer CreateComposer(string workingDirectory)
        => new(
            Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
            new QuestionDrivenImagePromptGenerator(),
            new AstronomyInfographicRenderer(
                new AstronomyBackgroundLayerRenderer(),
                new CelestialObjectLayerRenderer(),
                new SkyGuidanceLayerRenderer(),
                new EducationalLayerRenderer(),
                new AnnotationLayerRenderer()),
            new DefaultVisualSourceResolver(),
            NullLogger<QuestionDrivenVisualComposer>.Instance);

    private static async Task WritePlanetGroupingInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildPlanetGroupingEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildPlanetGroupingNarration(), JsonOptions));
    }

    private static EnrichedQuestionScenePlanDto BuildPlanetGroupingEnrichedPlan() => BuildEnrichedPlan() with
    {
        Diagnostics = new QuestionSceneEnrichmentDiagnostics(
            "PlanetGrouping",
            ["Saturn", "Mars", "Jupiter", "Venus", "planet grouping", "guided scan path"],
            [],
            [],
            [],
            "production-event-intelligence.json",
            PrimaryObjects: ["Saturn", "Mars"],
            SecondaryObjects: ["Jupiter", "Venus"])
    };

    private static QuestionDrivenNarrationDto BuildPlanetGroupingNarration() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Saturn, Mars, Jupiter, and Venus form a planet grouping.", "Saturn, Mars, Jupiter, and Venus share the sky in a guided planet grouping.", "Four-planet grouping."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Follow the guided scan path from Saturn through Mars, Jupiter, and Venus.", "Use the guided scan path to move from Saturn to Mars, Jupiter, and Venus.", "Follow the scan path."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "Use the local viewing window for the grouping.", "The grouping is best seen during the local viewing window.", "Use the viewing window."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Start at Saturn and scan toward Venus.", "Start at Saturn, scan through Mars and Jupiter, and finish at Venus.", "Scan Saturn to Venus."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "A multi-planet grouping puts four bright planets in one sweep.", "The grouping matters because four planets fit into one guided sweep.", "Four planets in one sweep."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Check weather and follow the guided scan path.", "If skies are clear, follow the guided scan path across the grouping.", "Check weather and scan.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static ProductionEventIntelligence BuildPlanetGroupingIntelligence() => new(
        Domain: "Astronomy",
        EventType: "PLANET_GROUPING",
        Title: "Saturn, Mars, Jupiter, and Venus planet grouping",
        ShortTitle: "Planet grouping",
        EventDate: null,
        PeakUtc: null,
        LocalPeakTime: null,
        BestViewingWindowLocal: "pre-dawn",
        SkyDirectionHint: "eastern sky",
        VisibilityRegion: RegionId,
        PrimaryObjects: ["Saturn", "Mars"],
        SecondaryObjects: ["Jupiter", "Venus"],
        ViewingQuality: null,
        MoonInterference: null,
        MoonIlluminationPercent: null,
        ScientificContext: null,
        ViewerInstructions: [],
        VisualMotifs: ["multi-planet grouping", "guided scan path", "realistic planet textures", "grouping arc"],
        SceneStrategy: [],
        QualityWarnings: [],
        ForbiddenTerms: [],
        StrategyId: "PlanetGrouping",
        ResolvedObjectNames: ["Saturn", "Mars", "Jupiter", "Venus"],
        RequiredVisualObjects: ["Saturn", "Mars", "Jupiter", "Venus", "planet grouping", "guided scan path"]);

    private static string[] ReadStringArray(JsonElement element)
        => element.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static async Task WriteMeteorInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildMeteorEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildMeteorNarration(), JsonOptions));
    }

    private static EnrichedQuestionScenePlanDto BuildMeteorEnrichedPlan() => new(
        EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "The Geminids meteor shower peaks with many meteor streaks in a dark sky."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Use a dark open sky from east to overhead and notice the shower radiant hint."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best meteor viewing window is 2026-12-14 00:00–05:00 IST under a dark sky."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I watch it?", "No telescope is needed; avoid city lights and let your eyes adapt for meteor streaks."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "The Geminids are a strong annual meteor shower with low moon interference."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Set a reminder, check weather, and pick a dark landscape for the Geminids meteor shower.")
        ],
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static QuestionDrivenNarrationDto BuildMeteorNarration() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "The Geminids meteor shower peaks in a dark sky.", "The Geminids meteor shower brings bright meteor streaks across the dark sky.", "Geminids meteor shower peak."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Watch east to overhead from a dark open sky.", "Face a dark open sky from east to overhead and use the radiant as only a subtle hint.", "Look east to overhead."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best meteor window is 2026-12-14 00:00–05:00 IST.", "The best window is 2026-12-14 00:00–05:00 IST, from midnight to pre-dawn.", "Midnight to pre-dawn."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I watch it?", "No telescope; avoid city lights and let your eyes adapt.", "You need no telescope; avoid city lights, let your eyes adapt, and watch the open sky.", "No telescope needed."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "A strong annual meteor shower with low moon interference.", "The Geminids are a strong annual meteor shower, and low moon interference helps faint streaks stand out.", "Strong shower, low Moon."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Set a reminder and check weather for a dark location.", "Set a reminder, check weather, and choose a dark location with a clear open sky.", "Set a reminder.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static async Task WriteInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildNarration(), JsonOptions));
    }

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan() => new(
        EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter appear close together tonight."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look west above the horizon for Venus and Jupiter."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best time is around 7:23 PM IST after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find Venus first, then look nearby for Jupiter while facing west."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter form a close bright planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Step outside tonight and look west for Venus and Jupiter.")
        ],
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static EnrichedQuestionSceneDto BuildScene(int sceneNumber, string questionType, string scenePurpose, string viewerQuestion, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            sourceAnswer,
            "CasualSkyWatcher",
            "Beginner",
            sourceAnswer,
            $"Narration intent for {questionType}.",
            $"Visual intent for {questionType}.",
            $"Image prompt intent for {questionType}.",
            $"Overlay intent for {questionType}.",
            $"Accessibility intent for {questionType}.",
            true);

    private static QuestionDrivenNarrationDto BuildNarration() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter appear close together tonight.", "Tonight, Venus and Jupiter appear close in Udaipur’s western sky.", "Venus and Jupiter shine close tonight."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look west above the horizon for Venus and Jupiter.", "Face west and scan above the horizon to spot Venus and Jupiter.", "Face west above the horizon."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best time is around 7:23 PM IST after sunset.", "The best viewing time is around 7:23 PM IST, shortly after sunset.", "Best around 7:23 PM IST."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find Venus first, then look nearby for Jupiter while facing west.", "Find Venus first, then look nearby for Jupiter while you face west.", "Find Venus, then nearby Jupiter."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter form a close bright planetary pairing.", "It matters because two bright planets make a close, beautiful pairing.", "A close bright planetary pairing."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Step outside tonight and look west for Venus and Jupiter.", "If skies are clear, step outside tonight and look west for Venus and Jupiter.", "Step outside and look west.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static QuestionDrivenNarrationSceneDto BuildNarrationScene(
        int sceneNumber,
        string questionType,
        string scenePurpose,
        string viewerQuestion,
        string viewerTakeaway,
        string narrationText,
        string captionText)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            viewerTakeaway,
            viewerTakeaway,
            $"Narration intent for {questionType}.",
            narrationText,
            9,
            "Warm and clear.",
            captionText);

    private static string BuildQuestionEnginePath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine");

    private static string BuildSceneApprovalPath(string workingDirectory)
        => Path.Combine(BuildQuestionEnginePath(workingDirectory), "scene-approval-v3");

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-driven-visual-composer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
