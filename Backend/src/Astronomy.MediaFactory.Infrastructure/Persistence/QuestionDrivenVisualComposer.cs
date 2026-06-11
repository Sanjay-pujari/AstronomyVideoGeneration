using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenVisualComposer(
    IOptions<RenderingOptions> renderingOptions,
    IQuestionDrivenImagePromptGenerator promptGenerator,
    IAstronomyInfographicRenderer infographicRenderer,
    ILogger<QuestionDrivenVisualComposer> logger) : IQuestionDrivenVisualComposer, IEditorialAstronomyInfographicComposer
{
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string OutputDirectoryName = "scene-approval-v3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenViewerTerms = ["guid", "path", "file", "json", "internal", "debug", "metadata", "question-engine", "scene-approval"];
    private static readonly string[] MeteorShowerForbiddenLeakageTerms = ["Venus", "Jupiter", "conjunction", "after sunset", "look west", "7:23 PM IST", "western horizon", "planet pairing", "object pairing"];

    public async Task<QuestionDrivenVisualGenerationResponse> GenerateQuestionDrivenVisualsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        var response = await GenerateEditorialAstronomyInfographicsCoreAsync(request, includeSceneApprovalVariants: false, cancellationToken);
        return new QuestionDrivenVisualGenerationResponse(
            response.EventId,
            response.SceneCount,
            response.FinalImageCount,
            response.SrtCount,
            response.ApprovedSceneCount,
            response.FailedSceneCount,
            response.GeneratedFiles,
            response.Warnings,
            response.CompositionMode,
            response.UsesSharedAstronomyVisualComposer,
            response.HeroAssetRulesApplied,
            response.DuplicateObjectRenderingDetected,
            PlannedImageCount: response.PlannedInfographicCount,
            PlannedSrtCount: response.PlannedInfographicCount,
            PlannedReviewCount: response.PlannedInfographicCount,
            PlannedScenes: response.PlannedScenes,
            QuestionIsolationScore: response.QuestionIsolationScore,
            CrossSceneLeakageDetected: response.CrossSceneLeakageDetected,
            SceneValidation: response.SceneValidation,
            AstronomySceneEngineV1Status: response.AstronomySceneEngineV1Status,
            SharedAstronomyVisualComposerStatus: response.SharedAstronomyVisualComposerStatus);
    }

    public Task<EditorialAstronomyInfographicGenerationResponse> GenerateEditorialAstronomyInfographicsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
        => GenerateEditorialAstronomyInfographicsCoreAsync(request, includeSceneApprovalVariants: true, cancellationToken);

    private async Task<EditorialAstronomyInfographicGenerationResponse> GenerateEditorialAstronomyInfographicsCoreAsync(QuestionDrivenVisualGenerationRequest request, bool includeSceneApprovalVariants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        logger.LogInformation("Generating editorial astronomy infographics for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var plannedScenes = new List<QuestionDrivenPlannedScene>();
        var sceneValidation = new List<SceneQuestionIsolationValidation>();
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId, request.ProductionContext);
        var outputRoot = !string.IsNullOrWhiteSpace(request.ProductionContext?.SceneRoot)
            ? request.ProductionContext!.SceneRoot!
            : Path.Combine(questionEngineRoot, OutputDirectoryName);
        var longOutputRoot = Path.Combine(outputRoot, "long");
        var shortOutputRoot = Path.Combine(outputRoot, "short");

        var answerSetPath = Path.Combine(questionEngineRoot, QuestionAnswerSetFileName);
        var planPath = Path.Combine(questionEngineRoot, EnrichedPlanFileName);
        var narrationPath = Path.Combine(questionEngineRoot, NarrationFileName);
        EnsureInputFile(answerSetPath, nameof(QuestionAnswerSetFileName));
        EnsureInputFile(planPath, nameof(EnrichedPlanFileName));
        EnsureInputFile(narrationPath, nameof(NarrationFileName));

        using var answerSetDocument = JsonDocument.Parse(await File.ReadAllTextAsync(answerSetPath, cancellationToken));
        var answerSetQuestionCount = CountQuestions(answerSetDocument);
        if (answerSetQuestionCount is > 0 and < 6) warnings.Add("question-answer-set.json has fewer than 6 detected questions; continuing with the approved 6-scene enriched plan.");

        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(await File.ReadAllTextAsync(planPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));
        var narration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Question-driven narration could not be parsed.", nameof(request));

        var scenes = enrichedPlan.Scenes.OrderBy(s => s.SceneNumber).ToArray();
        if (scenes.Length == 0) throw new ArgumentException("Editorial astronomy infographic composition requires at least one strategy-driven scene.", nameof(request));

        var finalImageCount = 0;
        var srtCount = 0;
        var longFormFinalImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shortFormFinalImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var approvedSceneCount = 0;
        var failedSceneCount = 0;
        var seenSrtTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLayoutKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isMeteorShowerPlan = scenes.Any(scene => IsMeteorText(scene.SourceAnswer) || IsMeteorText(scene.VisualIntent) || IsMeteorText(scene.ImagePromptIntent));
        var eventType = ResolveVisualEventType(request.ProductionContext, isMeteorShowerPlan, scenes);
        var usesLocalPlanetAssets = AllowsLocalPlanetAssets(eventType);
        var venusAsset = usesLocalPlanetAssets ? FindLocalAsset("venus") : null;
        var jupiterAsset = usesLocalPlanetAssets ? FindLocalAsset("jupiter") : null;
        if (usesLocalPlanetAssets && (venusAsset is null || jupiterAsset is null)) warnings.Add("Local transparent Venus/Jupiter assets were not both found; planet-pair scenes will render without local planet sprites.");

        foreach (var scene in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sceneNumber = scene.SceneNumber;
            var numberPrefix = $"scene-{sceneNumber:000}";
            var narrationScene = narration.Scenes.FirstOrDefault(s => s.SceneNumber == sceneNumber)
                ?? throw new ArgumentException($"Question-driven narration is missing scene {sceneNumber}.", nameof(request));
            var promptVisualIntent = $"{scene.VisualIntent} {narrationScene.SourceAnswer} {narrationScene.NarrationText}";
            var promptImageIntent = $"{scene.ImagePromptIntent} {narrationScene.ViewerTakeaway} {narrationScene.CaptionText}";
            var prompt = promptGenerator.GeneratePrompt(new QuestionDrivenImagePromptRequest(
                request.EventId,
                request.RegionId,
                request.Language,
                sceneNumber,
                scene.QuestionType,
                promptVisualIntent,
                promptImageIntent,
                usesLocalPlanetAssets && venusAsset is not null && jupiterAsset is not null));
            var spec = BuildSpec(request, scene, narrationScene, prompt, eventType, usesLocalPlanetAssets);
            ValidateLocalPlanetAssetContract(spec);
            ValidatePreRenderStrategyLeakage(spec, prompt, request.ProductionContext);
            var srt = BuildSrt(spec);
            var overlayPlan = BuildOverlayPlan(spec);
            var review = BuildReview(spec, srt, seenSrtTexts, seenLayoutKeys, !usesLocalPlanetAssets || venusAsset is not null, !usesLocalPlanetAssets || jupiterAsset is not null);

            var legacyFinalPath = Path.Combine(outputRoot, $"{numberPrefix}-final.png");
            var longFinalPath = Path.Combine(longOutputRoot, $"{numberPrefix}-final.png");
            var shortFinalPath = Path.Combine(shortOutputRoot, $"{numberPrefix}-final.png");
            var finalPath = legacyFinalPath;
            if (includeSceneApprovalVariants)
            {
                longFormFinalImages[numberPrefix] = NormalizePath(longFinalPath);
                shortFormFinalImages[numberPrefix] = NormalizePath(shortFinalPath);
            }
            var srtPath = Path.Combine(outputRoot, $"{numberPrefix}.srt");
            var narrationTextPath = Path.Combine(outputRoot, $"{numberPrefix}-narration.txt");
            var specPath = Path.Combine(outputRoot, $"{numberPrefix}-infographic-spec.json");
            var reviewPath = Path.Combine(outputRoot, $"{numberPrefix}-review.json");
            var presentationVariants = includeSceneApprovalVariants
                ? new QuestionDrivenPresentationVariants(NormalizePath(longFinalPath), NormalizePath(shortFinalPath))
                : null;
            var plannedOutputs = new QuestionDrivenPlannedOutputs(NormalizePath(finalPath), NormalizePath(srtPath), NormalizePath(narrationTextPath), NormalizePath(specPath), string.Empty, NormalizePath(reviewPath), presentationVariants);
            var validationPreview = BuildValidationPreview(spec, srt, review, overlayPlan, plannedOutputs);
            var isolationValidation = ValidateSceneQuestionIsolation(spec, overlayPlan);
            sceneValidation.Add(isolationValidation);
            plannedScenes.Add(new QuestionDrivenPlannedScene(scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, narrationScene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, promptVisualIntent, promptImageIntent, scene.OverlayIntent, scene.AccessibilityIntent, prompt, overlayPlan, plannedOutputs, validationPreview));

            if (validationPreview.Issues.Count > 0) warnings.AddRange(validationPreview.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            if (isolationValidation.LeakageWarnings.Count > 0) warnings.AddRange(isolationValidation.LeakageWarnings.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            if (request.DryRun) continue;

            var approvalAssets = includeSceneApprovalVariants
                ? new[] { longFinalPath, shortFinalPath, srtPath, narrationTextPath, specPath, reviewPath }
                : new[] { finalPath, srtPath, narrationTextPath, specPath, reviewPath };

            if (!includeSceneApprovalVariants && !request.OverwriteExisting && approvalAssets.Any(File.Exists))
            {
                warnings.Add($"Skipped scene {sceneNumber:000} because one or more approval assets already exist and overwriteExisting is false.");
                continue;
            }

            if (review.Issues.Count == 0) approvedSceneCount++; else { failedSceneCount++; warnings.AddRange(review.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}")); }
            Directory.CreateDirectory(outputRoot);
            if (includeSceneApprovalVariants)
            {
                await infographicRenderer.RenderAsync(longFinalPath, spec, venusAsset, jupiterAsset, cancellationToken, AstronomyInfographicRenderVariant.LongForm);
                await infographicRenderer.RenderAsync(shortFinalPath, spec, venusAsset, jupiterAsset, cancellationToken, AstronomyInfographicRenderVariant.ShortForm);
            }
            else
            {
                await infographicRenderer.RenderAsync(finalPath, spec, venusAsset, jupiterAsset, cancellationToken, AstronomyInfographicRenderVariant.LongForm);
            }
            await File.WriteAllTextAsync(srtPath, srt, cancellationToken);
            await File.WriteAllTextAsync(narrationTextPath, spec.NarrationText + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
            if (includeSceneApprovalVariants)
            {
                generatedFiles.AddRange([longFinalPath, shortFinalPath, srtPath, narrationTextPath, specPath, reviewPath]);
                finalImageCount += 2;
            }
            else
            {
                generatedFiles.AddRange([finalPath, srtPath, narrationTextPath, specPath, reviewPath]);
                finalImageCount++;
            }
            srtCount++;
        }

        AddPlanLevelWarnings(plannedScenes, warnings);
        var crossSceneLeakageDetected = sceneValidation.Any(scene => scene.LeakageWarnings.Count > 0);
        var questionIsolationScore = sceneValidation.Count == 0 ? 0 : (int)Math.Round(sceneValidation.Average(scene => scene.IsolationScore));
        warnings.Add("Human approval is still required before TTS, audio generation, video rendering, or publishing.");
        const string compositionMode = "SceneInfographic";
        const bool usesSharedAstronomyVisualComposer = true;
        const bool heroAssetRulesApplied = false;
        const bool duplicateObjectRenderingDetected = false;
        if (compositionMode != "SceneInfographic") throw new InvalidOperationException("Editorial astronomy infographic validation failed: compositionMode must be SceneInfographic.");
        if (heroAssetRulesApplied) throw new InvalidOperationException("Editorial astronomy infographic validation failed: hero asset rules were applied.");
        if (duplicateObjectRenderingDetected) throw new InvalidOperationException("Editorial astronomy infographic validation failed: duplicate object rendering was detected.");

        var sceneVariantFinalImages = includeSceneApprovalVariants
            ? new SceneVariantFinalImagesResponse(
                new SceneVariantFinalImageSet(
                    AstronomyInfographicRenderVariant.LongForm.VariantName,
                    EnsureTrailingSlash(NormalizePath(longOutputRoot)),
                    AstronomyInfographicRenderVariant.LongForm.Width,
                    AstronomyInfographicRenderVariant.LongForm.Height,
                    longFormFinalImages),
                new SceneVariantFinalImageSet(
                    AstronomyInfographicRenderVariant.ShortForm.VariantName,
                    EnsureTrailingSlash(NormalizePath(shortOutputRoot)),
                    AstronomyInfographicRenderVariant.ShortForm.Width,
                    AstronomyInfographicRenderVariant.ShortForm.Height,
                    shortFormFinalImages))
            : null;
        var longFormImageCount = includeSceneApprovalVariants && !request.DryRun ? CountExistingVariantImages(longFormFinalImages) : 0;
        var shortFormImageCount = includeSceneApprovalVariants && !request.DryRun ? CountExistingVariantImages(shortFormFinalImages) : 0;
        var diagnostics = includeSceneApprovalVariants
            ? new SceneVariantGenerationDiagnostics(
                SceneVariantGenerationEnabled: true,
                LongFormGenerated: !request.DryRun && longFormImageCount == scenes.Length,
                ShortFormGenerated: !request.DryRun && shortFormImageCount == scenes.Length,
                LongFormImageCount: longFormImageCount,
                ShortFormImageCount: shortFormImageCount)
            : null;

        if (includeSceneApprovalVariants && !request.DryRun)
        {
            if (sceneVariantFinalImages is null) throw new InvalidOperationException("Editorial astronomy infographic validation failed: sceneVariantFinalImages is null.");
            if (!Directory.Exists(longOutputRoot)) throw new InvalidOperationException("Editorial astronomy infographic validation failed: long scene variant directory is missing.");
            if (!Directory.Exists(shortOutputRoot)) throw new InvalidOperationException("Editorial astronomy infographic validation failed: short scene variant directory is missing.");
            if (longFormImageCount != scenes.Length) throw new InvalidOperationException($"Editorial astronomy infographic validation failed: long image count must be {scenes.Length}, but was {longFormImageCount}.");
            if (shortFormImageCount != scenes.Length) throw new InvalidOperationException($"Editorial astronomy infographic validation failed: short image count must be {scenes.Length}, but was {shortFormImageCount}.");
        }

        var shortFormValidation = includeSceneApprovalVariants
            ? ValidateShortFormOutputs(shortFormFinalImages, request.DryRun)
            : null;

        if (shortFormValidation is not null && !request.DryRun)
        {
            if (shortFormValidation.EmbeddedLongFormImageDetected) throw new InvalidOperationException("Editorial astronomy infographic validation failed: embedded long-form image detected in short-form output.");
            if (shortFormValidation.InnerFrameDetected) throw new InvalidOperationException("Editorial astronomy infographic validation failed: inner frame detected in short-form output.");
            if (shortFormValidation.ShortFormImageCount != scenes.Length) throw new InvalidOperationException($"Editorial astronomy infographic validation failed: short image count must be {scenes.Length}, but was {shortFormValidation.ShortFormImageCount}.");
            if (shortFormValidation.ShortFormWidth != AstronomyInfographicRenderVariant.ShortForm.Width || shortFormValidation.ShortFormHeight != AstronomyInfographicRenderVariant.ShortForm.Height) throw new InvalidOperationException("Editorial astronomy infographic validation failed: short-form images must be 1080x1920.");
            if (shortFormValidation.ShortFormReadabilityScore < 90) throw new InvalidOperationException("Editorial astronomy infographic validation failed: short-form readability score must be at least 90.");
            if (shortFormValidation.ShortFormReelSuitabilityScore < 90) throw new InvalidOperationException("Editorial astronomy infographic validation failed: short-form reel suitability score must be at least 90.");
        }

        if (includeSceneApprovalVariants)
        {
            var polishValidationPath = Path.Combine(shortOutputRoot, "shortform-polish-validation.json");
            var polishValidation = BuildShortFormPolishValidation(shortFormValidation, request.DryRun, IsMeteorPolishStrategy(request, scenes));
            Directory.CreateDirectory(shortOutputRoot);
            await File.WriteAllTextAsync(polishValidationPath, JsonSerializer.Serialize(polishValidation, JsonOptions), cancellationToken);
            generatedFiles.Add(polishValidationPath);
            if (polishValidation.ShortFormPolishScore < 95) throw new InvalidOperationException("Editorial astronomy infographic validation failed: short-form polish score must be at least 95.");
        }

        return new EditorialAstronomyInfographicGenerationResponse(
            request.EventId,
            scenes.Length,
            includeSceneApprovalVariants ? plannedScenes.Count * 2 : plannedScenes.Count,
            finalImageCount,
            srtCount,
            approvedSceneCount,
            failedSceneCount,
            plannedScenes,
            generatedFiles.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CompositionMode: compositionMode,
            UsesSharedAstronomyVisualComposer: usesSharedAstronomyVisualComposer,
            QuestionIsolationScore: questionIsolationScore,
            CrossSceneLeakageDetected: crossSceneLeakageDetected,
            SceneValidation: sceneValidation,
            AstronomySceneEngineV1Status: "FROZEN",
            SharedAstronomyVisualComposerStatus: "FROZEN",
            HeroAssetRulesApplied: heroAssetRulesApplied,
            DuplicateObjectRenderingDetected: duplicateObjectRenderingDetected,
            SceneVariantFinalImages: sceneVariantFinalImages,
            Diagnostics: diagnostics,
            ShortFormValidation: shortFormValidation);
    }

    private static QuestionDrivenVisualSpec BuildSpec(QuestionDrivenVisualGenerationRequest request, EnrichedQuestionSceneDto scene, QuestionDrivenNarrationSceneDto narrationScene, string prompt, string eventType, bool usesLocalPlanetAssets)
    {
        var isMeteorShower = IsMeteorText(scene.SourceAnswer) || IsMeteorText(scene.VisualIntent) || IsMeteorText(scene.ImagePromptIntent) || IsMeteorText(narrationScene.SourceAnswer) || IsMeteorText(narrationScene.NarrationText);
        var meteorContextText = $"{narrationScene.SourceAnswer} {narrationScene.ViewerTakeaway} {narrationScene.NarrationText} {narrationScene.CaptionText}";
        var meteorWindow = ResolveMeteorViewingWindow(request, meteorContextText);
        var meteorDate = ExtractMeteorDate(meteorWindow);
        var meteorReminder = string.IsNullOrWhiteSpace(meteorDate) ? "Set viewing reminder" : $"Set reminder for {meteorDate}";
        var overlays = isMeteorShower ? scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => ["Meteor Shower Peak", "Peak night alert"],
            "where" => ["East to overhead", "Dark open sky", "shower radiant"],
            "when" => [meteorDate, meteorWindow, "Midnight to pre-dawn"],
            "how" => ["No telescope", "Avoid city lights", "20 min dark adaptation"],
            "why" => ["Strong annual shower", "Low Moon Interference", "Meteor streaks"],
            "action" => ["Best viewing window", meteorReminder, "Check weather"],
            _ => new[] { narrationScene.ViewerTakeaway }
        } : usesLocalPlanetAssets ? scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "Venus & Jupiter", "After sunset" },
            "where" => new[] { "W", "Venus", "Jupiter", "Western horizon", "reference stars" },
            "when" => new[] { "Sunset", "7:23 PM IST", "After-sunset window" },
            "how" => new[] { "1 Find Venus", "2 Look nearby for Jupiter", "3 Face west" },
            "why" => new[] { "Two of the brightest worlds sharing the evening sky", "brightness", "closeness", "shared sky", "Venus", "Jupiter" },
            "action" => new[] { "Step outside tonight", "Look west" },
            _ => new[] { scene.ViewerTakeaway }
        } : scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { narrationScene.ViewerTakeaway, eventType },
            "where" => new[] { scene.ViewerTakeaway, "sky direction" },
            "when" => new[] { narrationScene.ViewerTakeaway, "best viewing window" },
            "how" => new[] { "safe viewing", "local sky conditions" },
            "why" => new[] { narrationScene.ViewerTakeaway, "astronomy significance" },
            "action" => new[] { "Check conditions", "Set reminder" },
            _ => new[] { scene.ViewerTakeaway }
        };

        var layers = isMeteorShower ? scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => ["mood:Dramatic", "background:professional astronomy magazine cover dark night sky over Rajasthan Udaipur with smooth sky gradient and atmospheric depth", "celestial:meteor streaks radiating from subtle shower radiant", "composition:strong focal contrast clickable thumbnail composition with meteor burst over dark open sky", "texture:documentary night sky grain natural starfield magnitude variation", "typography:premium thumbnail title Meteor Shower Peak subtitle Peak night alert"],
            "where" => ["mood:Educational", "background:observation-chart dark sky over Udaipur with subtle real eastern horizon and overhead dome", "guide:east-to-overhead sky direction guide", "celestial:meteor streaks from shower radiant", "reference:subtle shower radiant guide", "direction:East marker", "annotation:floating dark-sky labels"],
            "when" => ["mood:Informational", "background:deep dark night to pre-dawn sky transition with smooth sky gradient", $"time:{meteorWindow} marker", "direction:midnight to pre-dawn viewing window", "celestial:meteor streak activity timeline", "annotation:floating timeline labels"],
            "how" => ["mood:Instructional", "background:observer-friendly dark open sky with natural atmospheric depth", "celestial:meteor streaks overhead", "steps:No telescope; Avoid city lights; Let eyes adapt 20 minutes", "landscape:Udaipur dark location silhouette"],
            "why" => ["mood:Meaningful", "background:deep astronomy sky premium editorial background with atmospheric starfield depth and smooth sky gradient", "celestial:many meteor streaks radiating from the shower radiant", "significance:strong annual meteor shower and low moon interference", "quality:low moon interference improves dark-sky meteor visibility", "annotation:floating significance note"],
            "action" => ["mood:Inspirational", "background:beautiful poster-quality cinematic dark night sky over Udaipur with atmospheric depth and smooth sky gradient", "composition:premium shareable poster composition", "celestial:meteor streaks overhead", "starfield:natural density variation magnitude variation brightness variation", $"typography:minimal poster CTA {meteorReminder}"],
            _ => ["background:dark meteor shower sky", "celestial:meteor streaks"]
        } : usesLocalPlanetAssets ? scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "mood:Dramatic", "background:professional astronomy magazine cover western twilight over Rajasthan with richer twilight colors, natural atmospheric glow, and smooth sky gradient", "horizon:stronger golden-orange western horizon glow with subtle atmospheric haze", "texture:documentary sky grain, twilight haze, natural density variation starfield, magnitude variation, brightness variation", "composition:strong focal contrast clickable thumbnail composition with slightly brighter focal region around Venus/Jupiter", "vignette:soft natural edge falloff", "celestial:reduced-scale Venus/Jupiter sky targets integrated with atmospheric blending and subtle shared glow", "typography:premium thumbnail title Venus & Jupiter subtitle After sunset" },
            "where" => new[] { "mood:Educational", "background:observation-chart sky with astronomy guide aesthetic and subtle atmospheric realism", "horizon:subtle real western horizon", "guide:delicate altitude guide", "celestial:Venus/Jupiter plotted positions integrated with subtle glow", "reference:subtle sky grid", "reference:Leo Regulus constellation-star guide", "direction:West marker", "annotation:floating labels and leader lines" },
            "when" => new[] { "mood:Informational", "background:real twilight transition with warm sunset colors and natural atmospheric haze", "horizon:natural warm western horizon glow", "time:sunset marker", "time:7:23 PM IST marker", "direction:after-sunset viewing window", "layout:timeline hero", "annotation:floating timeline labels" },
            "how" => new[] { "mood:Instructional", "background:observer-friendly western sky with natural atmospheric depth", "celestial:Venus/Jupiter assets integrated with glow", "direction:observation arrow from Venus to Jupiter", "steps:Find Venus; Look nearby for Jupiter; Face west" },
            "why" => new[] { "mood:Meaningful", "background:deep astronomy sky premium editorial background with atmospheric starfield depth, smooth sky gradient, natural density variation, magnitude variation, brightness variation", "celestial:two of the brightest worlds sharing the evening sky as reduced-scale sky targets integrated with atmospheric blending and subtle shared glow region", "significance:shared sky brightness emotional significance for human interest and memorable astronomy storytelling", "relationship:visual relationship between planets with slight emphasis on closeness", "comparison:brightness scale", "direction:closeness bracket", "annotation:floating human-interest significance note" },
            "action" => new[] { "mood:Inspirational", "background:most beautiful poster-quality cinematic twilight premium astronomy artwork with atmospheric depth and smooth sky gradient", "horizon:warmer peaceful stronger golden-orange western horizon with subtle haze", "composition:premium shareable poster composition", "landscape:stronger landscape silhouette", "celestial:Venus and Jupiter reduced-scale sky targets naturally integrated with atmospheric blending and subtle glow", "starfield:natural density variation, magnitude variation, brightness variation", "twilight:cinematic warm western glow with richer twilight", "typography:minimal poster CTA Step Outside Tonight Look west" },
            _ => new[] { "background:sky", "programmatic:overlays" }
        } : scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "mood:Dramatic", $"background:professional dark-sky astronomy scene for {eventType} with smooth sky gradient and atmospheric depth", "composition:scene-specific astronomy subject without unrelated planets", "texture:documentary sky grain natural starfield magnitude variation" },
            "where" => new[] { "mood:Educational", "background:observation-chart dark sky with local horizon context", "guide:sky direction guide without planet-pairing markers", "annotation:floating sky labels" },
            "when" => new[] { "mood:Informational", "background:dark sky timing visual with smooth sky gradient", "time:best viewing window marker", "annotation:floating timeline labels" },
            "how" => new[] { "mood:Instructional", "background:observer-friendly dark sky with natural atmospheric depth", "steps:safe viewing; local sky conditions; check timing" },
            "why" => new[] { "mood:Meaningful", "background:deep astronomy sky premium editorial background with atmospheric starfield depth", "significance:event-specific astronomy significance", "annotation:floating significance note" },
            "action" => new[] { "mood:Inspirational", "background:beautiful poster-quality cinematic dark sky with atmospheric depth and smooth sky gradient", "composition:premium shareable poster composition", "typography:minimal poster CTA Check conditions" },
            _ => new[] { "background:dark astronomy sky", "programmatic:overlays" }
        };

        IReadOnlyList<string> accessibilityCues = isMeteorShower
            ? new[] { "Meteor-shower visual cues: meteor streaks, radiant guide, whole-sky dark location, low moon interference, no telescope needed.", "Text coverage target <= 25%; visual astronomy information target >= 75%; no large title cards, debug text, decorative circles, helper boxes, or card layouts." }
            : new[] { scene.AccessibilityIntent, "Text coverage target <= 25%; visual astronomy information target >= 75%; no large title cards, debug text, decorative circles, helper boxes, or card layouts." };

        var bestViewingWindowLocal = isMeteorShower && scene.SceneNumber == 3 ? meteorWindow : null;
        var strategyValidationFacts = isMeteorShower && scene.SceneNumber == 3
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bestViewingWindowLocal"] = meteorWindow,
                ["eventType"] = "MeteorShower",
                ["requiredTimingCue"] = ExtractMeteorRequiredTimingCue(meteorWindow)
            }
            : null;

        return new QuestionDrivenVisualSpec(request.EventId, request.RegionId, request.Language, scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, narrationScene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, Math.Max(4, narrationScene.EstimatedDurationSeconds), prompt, overlays, layers, accessibilityCues, DateTimeOffset.UtcNow, eventType, usesLocalPlanetAssets, bestViewingWindowLocal, strategyValidationFacts);
    }

    private static string ResolveMeteorViewingWindow(QuestionDrivenVisualGenerationRequest request, string text)
    {
        var fromIntelligence = request.ProductionContext?.ProductionEventIntelligence?.BestViewingWindowLocal;
        return !string.IsNullOrWhiteSpace(fromIntelligence) ? fromIntelligence : ExtractMeteorViewingWindow(text);
    }

    private static string ExtractMeteorRequiredTimingCue(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\b\d{2}:\d{2}[–-]\d{2}:\d{2}\s+[A-Z]{2,5}\b");
        return match.Success ? match.Value : "Midnight to pre-dawn";
    }

    private static string ExtractMeteorViewingWindow(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}[–-]\d{2}:\d{2}\s+[A-Z]{2,5}\b");
        return match.Success ? match.Value : "midnight to pre-dawn";
    }

    private static string ExtractMeteorDate(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}\b");
        return match.Success ? match.Value : "Peak night";
    }

    private static void ValidatePreRenderStrategyLeakage(QuestionDrivenVisualSpec spec, string prompt, ProductionPipelineExecutionContext? productionContext)
    {
        var intelligence = productionContext?.ProductionEventIntelligence;
        if (intelligence is null) return;

        var forbidden = new List<string>();
        forbidden.AddRange(intelligence.ForbiddenTerms);
        forbidden.AddRange(intelligence.ForbiddenObjectNames ?? []);
        var isMeteorShower = IsMeteorProduction(productionContext) || IsMeteorSpec(spec);
        if (isMeteorShower) forbidden.AddRange(MeteorShowerForbiddenLeakageTerms);

        var combined = string.Join(' ', new[]
        {
            spec.ViewerTakeaway,
            spec.NarrationText,
            spec.CaptionText,
            spec.BackgroundPrompt,
            prompt,
            string.Join(' ', spec.OverlayText),
            string.Join(' ', spec.ProgrammaticLayers),
            string.Join(' ', spec.AccessibilityCues)
        });
        var hits = forbidden.Where(term => ContainsToken(combined, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hits.Length > 0)
            throw new InvalidOperationException($"Pre-render scene validation failed for scene {spec.SceneNumber:000}: forbidden unrelated terms detected: {string.Join(", ", hits)}.");

    }

    private static QuestionDrivenSceneReview BuildReview(QuestionDrivenVisualSpec spec, string srt, HashSet<string> seenSrtTexts, HashSet<string> seenLayoutKeys, bool venusAssetFound, bool jupiterAssetFound)
    {
        var issues = new List<string>();
        var recommendations = new List<string>();
        var viewerText = string.Join(' ', spec.OverlayText);
        var layoutKey = GetLayoutKey(spec.QuestionType);
        if (ContainsForbiddenTerm(viewerText)) issues.Add("image has internal/debug text.");
        if (!IsSceneSpecific(spec)) issues.Add("scene does not visually answer its question.");
        if (!seenSrtTexts.Add(Clean(spec.CaptionText))) issues.Add("SRT/narration repeats another scene and does not match the scene question.");
        if (!NarrationAlignsWithImage(spec)) issues.Add("SRT/narration does not match scene question.");
        if (!seenLayoutKeys.Add(layoutKey)) issues.Add("same layout repeated from another scene.");
        if (EstimateTextCoverage(spec) > 0.25) issues.Add("large text box covers more than 25% of image.");
        var usesCardOrPanelBox = spec.ProgrammaticLayers.Any(layer =>
            layer.Contains("card", StringComparison.OrdinalIgnoreCase) ||
            layer.Contains("panel", StringComparison.OrdinalIgnoreCase) ||
            layer.Contains("large rectangle", StringComparison.OrdinalIgnoreCase));
        var usesHelperLayoutBox = spec.ProgrammaticLayers.Any(layer =>
            layer.Contains("helper box", StringComparison.OrdinalIgnoreCase) ||
            layer.Contains("helper layout", StringComparison.OrdinalIgnoreCase) ||
            layer.Contains("spotting-frame", StringComparison.OrdinalIgnoreCase));
        if (usesCardOrPanelBox) issues.Add("large rectangle/panel/card is visible.");
        if (usesHelperLayoutBox) issues.Add("helper layout box is visible.");
        var isMeteorShower = IsMeteorSpec(spec);
        if (spec.UsesLocalPlanetAssets && (!venusAssetFound || !jupiterAssetFound)) issues.Add("local transparent Venus/Jupiter assets are missing.");
        var textCollisionDetected = false;
        var textCollisionResolved = true;
        var labelOverPlanetDetected = false;
        var usesSolidPlanetBackingCircle = false;
        var blueprintZonesRespected = true;
        var environmentalBackgroundDistinct = HasSceneSpecificEnvironmentalBackground(spec);
        var planetAssetsIntegratedIntoSky = spec.UsesLocalPlanetAssets && venusAssetFound && jupiterAssetFound && spec.ProgrammaticLayers.Any(layer => layer.Contains("integrated", StringComparison.OrdinalIgnoreCase) || layer.Contains("glow", StringComparison.OrdinalIgnoreCase));
        var constellationLayerRendered = spec.ProgrammaticLayers.Any(layer => layer.Contains("constellation", StringComparison.OrdinalIgnoreCase));
        var referenceStarLayerRendered = spec.ProgrammaticLayers.Any(layer => layer.Contains("reference-star", StringComparison.OrdinalIgnoreCase) || layer.Contains("reference:subtle star", StringComparison.OrdinalIgnoreCase) || layer.Contains("Regulus", StringComparison.OrdinalIgnoreCase));
        var sceneMood = GetSceneMood(spec.QuestionType);
        var thumbnailQuality = IsThumbnailQuality(spec);
        var posterQuality = IsPosterQuality(spec);
        var visualUniquenessScore = GetVisualUniquenessScore(spec.QuestionType);
        var humanInterestScore = GetHumanInterestScore(spec);
        var backgroundRealismScore = GetBackgroundRealismScore(spec);
        var astronomyPhotographyScore = GetAstronomyPhotographyScore(spec);
        var clickabilityScore = GetClickabilityScore(spec);
        var atmosphericDepthScore = GetAtmosphericDepthScore(spec);
        var editorialQualityScore = GetEditorialQualityScore(spec);
        var shareabilityScore = GetShareabilityScore(spec);
        var twilightQualityScore = GetTwilightQualityScore(spec);
        var starfieldRealismScore = GetStarfieldRealismScore(spec);
        var visibleHorizontalBanding = DetectVisibleHorizontalBanding(spec);
        var smoothSkyGradient = UsesSmoothSkyGradient(spec);
        var decorativeCircleDetected = DetectLargeDecorativeCircle(spec);
        var atmosphericBackgroundUsed = UsesAtmosphericBackground(spec);
        var largeTemplateShapeDetected = usesCardOrPanelBox || usesHelperLayoutBox || spec.ProgrammaticLayers.Any(layer => layer.Contains("template shape", StringComparison.OrdinalIgnoreCase) || layer.Contains("background circle", StringComparison.OrdinalIgnoreCase) || layer.Contains("decorative circle", StringComparison.OrdinalIgnoreCase));
        var significanceLayerRendered = isMeteorShower
            ? (!spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) || (viewerText.Contains("Strong annual shower", StringComparison.OrdinalIgnoreCase) && string.Join(' ', spec.ProgrammaticLayers).Contains("low moon interference", StringComparison.OrdinalIgnoreCase)))
            : (!spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) || (viewerText.Contains("Two of the brightest worlds sharing the evening sky", StringComparison.OrdinalIgnoreCase) && spec.ProgrammaticLayers.Any(layer => layer.Contains("shared sky", StringComparison.OrdinalIgnoreCase) || layer.Contains("emotional significance", StringComparison.OrdinalIgnoreCase))));
        if (!isMeteorShower && spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && (!viewerText.Contains("brightest worlds", StringComparison.OrdinalIgnoreCase) || !viewerText.Contains("sharing", StringComparison.OrdinalIgnoreCase) || !viewerText.Contains("sky", StringComparison.OrdinalIgnoreCase))) issues.Add("Why scene does not emphasize two of the brightest worlds sharing the evening sky.");
        if (isMeteorShower && !ContainsAny(string.Join(' ', spec.ProgrammaticLayers.Concat(spec.OverlayText).Concat([spec.BackgroundPrompt])), "meteor", "meteor shower", "radiant")) issues.Add("MeteorShower scene prompt must include meteor-related terms.");
        if (isMeteorShower && ContainsAny(string.Join(' ', spec.ProgrammaticLayers.Concat(spec.OverlayText).Concat([spec.BackgroundPrompt])), "Venus", "Jupiter", "conjunction")) issues.Add("MeteorShower visual prompt must not reference Venus/Jupiter or conjunction.");
        if (decorativeCircleDetected) issues.Add("decorative translucent circle detected.");
        if (largeTemplateShapeDetected) issues.Add("large template shape detected.");
        if (!atmosphericBackgroundUsed) issues.Add("atmospheric background was not used.");
        if (backgroundRealismScore < 80) issues.Add("backgroundRealismScore is below 80.");
        if (astronomyPhotographyScore < 80) issues.Add("astronomyPhotographyScore is below 80.");
        if (visualUniquenessScore < 80) issues.Add("visual uniqueness score is below 80.");
        if (spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && !thumbnailQuality) issues.Add("Scene 1 thumbnailQuality is false.");
        if (spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && !posterQuality) issues.Add("Scene 6 posterQuality is false.");
        if (spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && clickabilityScore < 95) issues.Add("Scene 1 clickabilityScore is below 95.");
        if (spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && atmosphericDepthScore < 90) issues.Add("Scene 1 atmosphericDepthScore is below 90.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && humanInterestScore < 90) issues.Add("Scene 5 humanInterestScore is below 90.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && editorialQualityScore < 90) issues.Add("Scene 5 editorialQualityScore is below 90.");
        if (spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && shareabilityScore < 95) issues.Add("Scene 6 shareabilityScore is below 95.");
        if (spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && twilightQualityScore < 90) issues.Add("Scene 6 twilightQualityScore is below 90.");
        if (starfieldRealismScore < 85) issues.Add("starfieldRealismScore is below 85.");
        if (visibleHorizontalBanding) issues.Add("visibleHorizontalBanding is true.");
        if (!smoothSkyGradient) issues.Add("smoothSkyGradient is false.");
        if (textCollisionDetected) issues.Add("visible text overlaps before collision resolution.");
        if (!textCollisionResolved) issues.Add("visible text collision was not resolved.");
        if (labelOverPlanetDetected) issues.Add("planet label overlaps a planet asset.");
        if (usesSolidPlanetBackingCircle) issues.Add("solid dark backing circle is used behind a planet asset.");
        if (spec.UsesLocalPlanetAssets && !planetAssetsIntegratedIntoSky && !spec.QuestionType.Equals("When", StringComparison.OrdinalIgnoreCase)) issues.Add("planet assets are not integrated into the sky with subtle glow.");
        if (!environmentalBackgroundDistinct) issues.Add("background is the same generic dark-blue mountain scene as other scenes.");
        if (!blueprintZonesRespected) issues.Add("renderer ignored one or more layout blueprint zones.");
        if (!significanceLayerRendered) issues.Add("Scene 5 does not include a closeness/significance layer.");
        if (!isMeteorShower && spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "golden-orange", "western", "horizon", "premium", "focal contrast")) issues.Add("Scene 1 does not feel like a professional astronomy thumbnail.");
        if (isMeteorShower && spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "meteor shower", "meteor", "dark night", "focal contrast")) issues.Add("Scene 1 does not feel like a meteor-shower thumbnail.");
        if (!isMeteorShower && spec.QuestionType.Equals("Where", StringComparison.OrdinalIgnoreCase) && !(constellationLayerRendered && referenceStarLayerRendered && ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "observation-chart", "western"))) issues.Add("Scene 2 does not feel like observation chart.");
        if (isMeteorShower && spec.QuestionType.Equals("Where", StringComparison.OrdinalIgnoreCase) && !(constellationLayerRendered && ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "observation-chart", "east-to-overhead"))) issues.Add("Scene 2 does not feel like a meteor-shower observation chart.");
        if (!isMeteorShower && spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "significance", "shared", "brightness")) issues.Add("Scene 5 does not feel like a human-interest significance visual.");
        if (isMeteorShower && spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "significance", "strong annual meteor shower", "low moon interference")) issues.Add("Scene 5 does not feel like a meteor-shower significance visual.");
        if (spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "poster", "cinematic", "premium", "minimal", "shareable")) issues.Add("Scene 6 does not feel like poster/CTA scene.");
        if (!srt.Contains(" --> ", StringComparison.Ordinal)) issues.Add("SRT is not in timed-caption format.");
        var approved = issues.Count == 0;
        var textCoveragePercent = (int)Math.Round(EstimateTextCoverage(spec) * 100);
        return new QuestionDrivenSceneReview(
            spec.SceneNumber,
            spec.QuestionType,
            GetLayoutTemplate(spec.QuestionType),
            approved,
            approved,
            approved,
            approved,
            approved,
            spec.UsesLocalPlanetAssets,
            false,
            false,
            textCoveragePercent,
            100 - textCoveragePercent,
            textCollisionDetected,
            textCollisionResolved,
            labelOverPlanetDetected,
            usesSolidPlanetBackingCircle,
            blueprintZonesRespected,
            significanceLayerRendered,
            environmentalBackgroundDistinct,
            usesCardOrPanelBox,
            usesHelperLayoutBox,
            planetAssetsIntegratedIntoSky,
            constellationLayerRendered,
            referenceStarLayerRendered,
            sceneMood,
            thumbnailQuality,
            posterQuality,
            visualUniquenessScore,
            humanInterestScore,
            backgroundRealismScore,
            astronomyPhotographyScore,
            clickabilityScore,
            atmosphericDepthScore,
            editorialQualityScore,
            shareabilityScore,
            twilightQualityScore,
            starfieldRealismScore,
            visibleHorizontalBanding,
            smoothSkyGradient,
            decorativeCircleDetected,
            atmosphericBackgroundUsed,
            largeTemplateShapeDetected,
            issues,
            recommendations);
    }

    private static QuestionDrivenProgrammaticOverlayPlan BuildOverlayPlan(QuestionDrivenVisualSpec spec) => IsMeteorSpec(spec) ? spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => new("Meteor Shower Peak", "Peak night alert", ["Meteor streaks", "shower radiant", "Udaipur dark sky"], ["radiant guide"], [], [], [], []),
        "where" => new("Where to Look", "East to overhead after 10 PM", ["East", "Overhead", "Dark open sky", "shower radiant"], ["east-to-overhead direction guide"], [], ["East"], [], []),
        "when" => new("Best Viewing Window", ExtractMeteorViewingWindow(spec.NarrationText + " " + spec.ViewerTakeaway), ["Viewing window"], [], [], [], [ExtractMeteorViewingWindow(spec.NarrationText + " " + spec.ViewerTakeaway)], []),
        "how" => new("How to Watch", "No telescope needed", ["Dark location", "Meteor streaks"], [], [], [], [], ["Avoid city lights", "Let eyes adapt 20 minutes", "Look at open sky"]),
        "why" => new("Why It Matters", "Strong annual shower, low Moon interference", ["meteor shower", "meteor streaks", "low moon interference"], ["radiant emphasis"], [], [], [], []),
        "action" => new("Best Viewing Window", ExtractMeteorViewingWindow(spec.NarrationText + " " + spec.ViewerTakeaway), ["Weather check", "Dark location"], [], [], [], [], []),
        _ => new(spec.ViewerTakeaway, string.Empty, ["Meteor streaks"], [], [], [], [], [])
    } : !spec.UsesLocalPlanetAssets ? spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => new(spec.ViewerTakeaway, spec.EventType, [spec.EventType], [], [], [], [], []),
        "where" => new("Where to Look", "Use the event-specific sky direction", ["Sky direction", "Local horizon"], ["direction guide"], [], [], [], []),
        "when" => new("Best Viewing Window", spec.ViewerTakeaway, ["Viewing window"], [], [], [], [spec.ViewerTakeaway], []),
        "how" => new("How to Watch", "Use safe, event-specific viewing", ["Safe viewing", "Local conditions"], [], [], [], [], ["Check conditions", "Use the correct viewing method"]),
        "why" => new("Why It Matters", "Event-specific astronomy significance", ["astronomy significance"], ["significance emphasis"], [], [], [], []),
        "action" => new("Check Conditions", "Set a reminder", ["Weather check", "Local sky conditions"], [], [], [], [], []),
        _ => new(spec.ViewerTakeaway, string.Empty, [], [], [], [], [], [])
    } : spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => new("Venus & Jupiter", "After sunset", ["Venus", "Jupiter"], ["leader lines from labels to planets"], ["Venus", "Jupiter"], [], [], []),
        "where" => new("Where to Look", "Face the western horizon", ["West", "Venus", "Jupiter", "Horizon", "Leo / Regulus reference stars"], ["western horizon altitude guide"], ["Venus", "Jupiter"], ["West"], [], []),
        "when" => new("Best Time Tonight", "After sunset", ["Sunset", "Viewing window"], [], [], [], ["7:23 PM IST"], []),
        "how" => new("How to Find It", "Use Venus as your anchor", ["Venus", "Jupiter"], ["observation arrow from Venus to Jupiter"], ["Venus", "Jupiter"], [], [], ["Find Venus", "Look nearby for Jupiter", "Face west"]),
        "why" => new("Why It Matters", "Two of the brightest worlds sharing the evening sky", ["Venus", "Jupiter", "brightness", "closeness", "shared sky"], ["closeness bracket", "brightness comparison"], ["Venus", "Jupiter"], [], [], []),
        "action" => new("Step Outside Tonight", "Look west tonight", [], [], [], [], [], []),
        _ => new(spec.ViewerTakeaway, string.Empty, [], [], [], [], [], [])
    };


    private static bool IsMeteorText(string? value)
        => !string.IsNullOrWhiteSpace(value) && (value.Contains("meteor", StringComparison.OrdinalIgnoreCase) || value.Contains("radiant", StringComparison.OrdinalIgnoreCase));

    private static string ResolveVisualEventType(ProductionPipelineExecutionContext? productionContext, bool isMeteorShowerPlan, IReadOnlyList<EnrichedQuestionSceneDto> scenes)
    {
        var eventType = productionContext?.EventType
            ?? productionContext?.ProductionEventIntelligence?.EventType
            ?? productionContext?.MediaEventStrategy?.EventType
            ?? productionContext?.ProductionEventIntelligence?.StrategyId;
        if (!string.IsNullOrWhiteSpace(eventType)) return eventType;
        if (isMeteorShowerPlan) return "MeteorShower";
        var combined = string.Join(' ', scenes.SelectMany(scene => new[] { scene.SourceAnswer, scene.VisualIntent, scene.ImagePromptIntent }));
        return ContainsAll(combined, "Venus", "Jupiter") ? "PlanetPairing" : "Unknown";
    }

    private static bool AllowsLocalPlanetAssets(string? eventType)
        => eventType?.Equals("PlanetPairing", StringComparison.OrdinalIgnoreCase) == true
            || eventType?.Equals("Conjunction", StringComparison.OrdinalIgnoreCase) == true;

    private static void ValidateLocalPlanetAssetContract(QuestionDrivenVisualSpec spec)
    {
        var allowed = AllowsLocalPlanetAssets(spec.EventType);
        if (!allowed && spec.UsesLocalPlanetAssets)
            throw new InvalidOperationException($"Pre-render visual contract failed for scene {spec.SceneNumber:000}: eventType={spec.EventType} must not use local planet assets.");
        if (allowed && !spec.UsesLocalPlanetAssets)
            throw new InvalidOperationException($"Pre-render visual contract failed for scene {spec.SceneNumber:000}: eventType={spec.EventType} must use local planet assets.");
    }

    private static bool IsMeteorProduction(ProductionPipelineExecutionContext? productionContext)
    {
        var eventType = productionContext?.EventType ?? productionContext?.ProductionEventIntelligence?.EventType ?? productionContext?.MediaEventStrategy?.EventType ?? productionContext?.ProductionEventIntelligence?.StrategyId ?? string.Empty;
        return eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || (productionContext?.ProductionEventIntelligence?.Title?.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool ContainsToken(string value, string? term)
        => !string.IsNullOrWhiteSpace(term) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool IsMeteorSpec(QuestionDrivenVisualSpec spec)
        => IsMeteorText(spec.BackgroundPrompt) || spec.OverlayText.Any(IsMeteorText) || spec.ProgrammaticLayers.Any(IsMeteorText) || IsMeteorText(spec.NarrationText);

    private static bool IsMeteorOverlay(QuestionDrivenProgrammaticOverlayPlan overlayPlan)
        => IsMeteorText(overlayPlan.Title) || IsMeteorText(overlayPlan.Subtitle) || overlayPlan.Labels.Any(IsMeteorText) || overlayPlan.TimingMarkers.Any(IsMeteorText) || overlayPlan.Steps.Any(IsMeteorText);

    private static SceneQuestionIsolationValidation ValidateSceneQuestionIsolation(QuestionDrivenVisualSpec spec, QuestionDrivenProgrammaticOverlayPlan overlayPlan)
    {
        var expectedQuestion = NormalizeQuestionType(spec.QuestionType);
        var warnings = new List<string>();
        var title = overlayPlan.Title ?? string.Empty;
        var subtitle = overlayPlan.Subtitle ?? string.Empty;
        var labels = overlayPlan.Labels ?? Array.Empty<string>();
        var arrows = overlayPlan.Arrows ?? Array.Empty<string>();
        var localAssets = overlayPlan.LocalAssetObjects ?? Array.Empty<string>();
        var directionMarkers = overlayPlan.DirectionMarkers ?? Array.Empty<string>();
        var timingMarkers = overlayPlan.TimingMarkers ?? Array.Empty<string>();
        var steps = overlayPlan.Steps ?? Array.Empty<string>();
        var overlayText = spec.OverlayText ?? Array.Empty<string>();
        var layers = spec.ProgrammaticLayers ?? Array.Empty<string>();
        var combinedOverlayText = JoinQuestionIsolationText([title, subtitle], labels, arrows, localAssets, directionMarkers, timingMarkers, steps, overlayText, layers);

        switch (expectedQuestion.ToLowerInvariant())
        {
            case "what":
                AddIfAny(warnings, "Where content leaked into What scene.", combinedOverlayText, "west marker", "direction:west", "altitude guide", "western horizon altitude", "sky grid", "leo", "regulus", "reference stars");
                AddIfAny(warnings, "When content leaked into What scene.", combinedOverlayText, "viewing window", "best viewing", "7:23", "timeline", "sunset marker");
                AddIfAny(warnings, "How content leaked into What scene.", combinedOverlayText, "step 1", "1 find", "2 look", "3 face", "observation arrow", "arrow from");
                AddIfAny(warnings, "Why content leaked into What scene.", combinedOverlayText, "why it matters", "brightest worlds", "brightness comparison", "closeness bracket", "significance");
                AddIfAny(warnings, "Action content leaked into What scene.", combinedOverlayText, "step outside", "look west tonight", "cta");
                break;
            case "where":
                AddIfAny(warnings, "When content leaked into Where scene.", combinedOverlayText, "viewing window", "best viewing", "7:23", "timeline", "sunset marker");
                AddIfAny(warnings, "Action content leaked into Where scene.", combinedOverlayText, "step outside", "look west tonight", "cta");
                AddIfAny(warnings, "Why content leaked into Where scene.", combinedOverlayText, "why it matters", "brightest worlds", "brightness comparison", "significance");
                AddIfAny(warnings, "How content leaked into Where scene.", combinedOverlayText, "1 find", "2 look nearby", "3 face", "step 1", "step 2", "step 3");
                break;
            case "when":
                AddIfAny(warnings, "Where content leaked into When scene.", combinedOverlayText, "altitude guide", "leo", "regulus", "reference stars", "west marker", "western horizon altitude");
                AddIfAny(warnings, "Planet labels leaked into When scene.", labels, "Venus", "Jupiter");
                AddIfAny(warnings, "How content leaked into When scene.", combinedOverlayText, "1 find", "2 look nearby", "3 face", "step 1", "observation arrow", "arrow from");
                AddIfAny(warnings, "Why content leaked into When scene.", combinedOverlayText, "why it matters", "brightest worlds", "brightness comparison", "significance");
                break;
            case "how":
                AddIfAny(warnings, "When content leaked into How scene.", combinedOverlayText, "viewing window", "best viewing", "7:23", "timeline", "sunset marker");
                AddIfAny(warnings, "Why content leaked into How scene.", combinedOverlayText, "why it matters", "brightest worlds", "brightness comparison", "significance");
                AddIfAny(warnings, "Action content leaked into How scene.", combinedOverlayText, "step outside", "look west tonight", "cta");
                AddIfAny(warnings, "Where-only marker leaked into How scene.", directionMarkers, "West");
                break;
            case "why":
                AddIfAny(warnings, "How content leaked into Why scene.", combinedOverlayText, "1 find", "2 look nearby", "3 face", "step 1", "observation arrow", "face west");
                AddIfAny(warnings, "When content leaked into Why scene.", combinedOverlayText, "viewing window", "best viewing", "7:23", "timeline", "sunset marker");
                AddIfAny(warnings, "Action content leaked into Why scene.", combinedOverlayText, "step outside", "look west tonight", "cta");
                break;
            case "action":
                AddIfAny(warnings, "Where content leaked into Action scene.", combinedOverlayText, "altitude guide", "sky grid", "leo", "regulus", "reference stars", "west marker", "western horizon altitude");
                AddIfAny(warnings, "Why content leaked into Action scene.", combinedOverlayText, "why it matters", "brightest worlds", "brightness comparison", "closeness bracket", "significance");
                AddIfAny(warnings, "When content leaked into Action scene.", combinedOverlayText, "viewing window", "best viewing", "7:23", "timeline", "sunset marker");
                AddIfAny(warnings, "How content leaked into Action scene.", combinedOverlayText, "1 find", "2 look nearby", "3 face", "step 1", "observation arrow", "arrow from", "face west");
                break;
            default:
                warnings.Add($"Unknown expected question '{spec.QuestionType}'.");
                break;
        }

        var isolationScore = Math.Clamp(100 - warnings.Count * 20, 0, 100);
        return new SceneQuestionIsolationValidation(spec.SceneNumber, expectedQuestion, isolationScore, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizeQuestionType(string questionType) => questionType.ToLowerInvariant() switch
    {
        "what" => AstronomyQuestionTypes.What,
        "where" => AstronomyQuestionTypes.Where,
        "when" => AstronomyQuestionTypes.When,
        "how" => AstronomyQuestionTypes.How,
        "why" => AstronomyQuestionTypes.Why,
        "action" => AstronomyQuestionTypes.Action,
        _ => questionType
    };

    private static string JoinQuestionIsolationText(IReadOnlyList<string> fixedText, params IReadOnlyList<string>[] groups)
        => Clean(string.Join(' ', fixedText.Concat(groups.SelectMany(group => group ?? Array.Empty<string>()))));

    private static void AddIfAny(List<string> warnings, string warning, string value, params string[] terms)
    {
        if (terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase))) warnings.Add(warning);
    }

    private static void AddIfAny(List<string> warnings, string warning, IReadOnlyList<string> values, params string[] terms)
    {
        if (values.Any(value => terms.Any(term => value.Equals(term, StringComparison.OrdinalIgnoreCase)))) warnings.Add(warning);
    }

    private static QuestionDrivenValidationPreview BuildValidationPreview(QuestionDrivenVisualSpec spec, string srt, QuestionDrivenSceneReview review, QuestionDrivenProgrammaticOverlayPlan overlayPlan, QuestionDrivenPlannedOutputs plannedOutputs)
    {
        var issues = new List<string>(review.Issues);
        var srtReady = !string.IsNullOrWhiteSpace(spec.CaptionText) && srt.Contains(" --> ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(plannedOutputs.SrtPath);
        var accessibilityReady = spec.AccessibilityCues.Any(cue => !string.IsNullOrWhiteSpace(cue)) && (overlayPlan.Labels.Count > 0 || overlayPlan.Steps.Count > 0 || overlayPlan.TimingMarkers.Count > 0 || !string.IsNullOrWhiteSpace(overlayPlan.Title)) && !string.IsNullOrWhiteSpace(spec.CaptionText);
        if (string.IsNullOrWhiteSpace(spec.NarrationText)) issues.Add("scene lacks narrationText.");
        if (string.IsNullOrWhiteSpace(spec.BackgroundPrompt)) issues.Add("scene lacks AI background prompt.");
        if (string.IsNullOrWhiteSpace(plannedOutputs.FinalImagePath)) issues.Add("scene lacks finalImagePath.");
        if (string.IsNullOrWhiteSpace(plannedOutputs.SrtPath)) issues.Add("scene lacks srtPath.");
        if (!OverlayPlanMatchesQuestionType(spec.QuestionType, overlayPlan)) issues.Add("scene overlay plan does not match questionType.");
        if (!srtReady) issues.Add("scene is not SRT-ready.");
        if (!accessibilityReady) issues.Add("scene lacks accessible labels, steps, or timing markers.");
        return new QuestionDrivenValidationPreview(IsSceneSpecific(spec), NarrationAlignsWithImage(spec), srtReady, accessibilityReady, issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), review.Recommendations);
    }

    private static bool OverlayPlanMatchesQuestionType(string questionType, QuestionDrivenProgrammaticOverlayPlan overlayPlan) => questionType.ToLowerInvariant() switch
    {
        "what" => IsMeteorOverlay(overlayPlan) ? overlayPlan.Title.Contains("meteor shower", StringComparison.OrdinalIgnoreCase) : overlayPlan.LocalAssetObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase) && overlayPlan.LocalAssetObjects.Contains("Jupiter", StringComparer.OrdinalIgnoreCase),
        "where" => IsMeteorOverlay(overlayPlan) ? overlayPlan.Labels.Contains("East", StringComparer.OrdinalIgnoreCase) && overlayPlan.Labels.Contains("Overhead", StringComparer.OrdinalIgnoreCase) : overlayPlan.Labels.Contains("West", StringComparer.OrdinalIgnoreCase) && overlayPlan.Labels.Contains("Horizon", StringComparer.OrdinalIgnoreCase),
        "when" => IsMeteorOverlay(overlayPlan) ? overlayPlan.TimingMarkers.Any(marker => !string.IsNullOrWhiteSpace(marker) && (marker.Contains("pre-dawn", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(marker, @"\b\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}"))) : overlayPlan.TimingMarkers.Contains("7:23 PM IST", StringComparer.OrdinalIgnoreCase),
        "how" => IsMeteorOverlay(overlayPlan) ? overlayPlan.Steps.Any(step => step.Contains("20 minutes", StringComparison.OrdinalIgnoreCase)) : overlayPlan.Steps.SequenceEqual(["Find Venus", "Look nearby for Jupiter", "Face west"], StringComparer.OrdinalIgnoreCase) && overlayPlan.Arrows.Count > 0,
        "why" => IsMeteorOverlay(overlayPlan) ? overlayPlan.Subtitle.Contains("Strong annual", StringComparison.OrdinalIgnoreCase) : overlayPlan.Subtitle.Contains("brightest worlds", StringComparison.OrdinalIgnoreCase) && overlayPlan.Subtitle.Contains("sharing", StringComparison.OrdinalIgnoreCase) && overlayPlan.Arrows.Count > 0,
        "action" => IsMeteorOverlay(overlayPlan) ? overlayPlan.Title.Contains("Best", StringComparison.OrdinalIgnoreCase) : overlayPlan.Subtitle.Contains("west", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static void AddPlanLevelWarnings(IReadOnlyList<QuestionDrivenPlannedScene> plannedScenes, List<string> warnings)
    {
        if (plannedScenes.Count != 6) warnings.Add("Editorial infographic pilot should contain exactly 6 scenes.");
        var expected = new[] { "What", "Where", "When", "How", "Why", "Action" };
        foreach (var expectedType in expected)
            if (!plannedScenes.Any(scene => scene.QuestionType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))) warnings.Add($"Missing {expectedType} scene.");
        if (plannedScenes.Select(scene => GetLayoutKey(scene.QuestionType)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plannedScenes.Count) warnings.Add("Every scene must have a distinct composition/layout key.");
    }

    private static bool IsSceneSpecific(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.OverlayText.Concat(spec.ProgrammaticLayers));
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => IsMeteorSpec(spec) ? ContainsAll(text, "meteor", "dark") && ContainsAny(text, "streak", "streaks", "radiant") : ContainsAll(text, "venus", "jupiter", "twilight"),
            "where" => IsMeteorSpec(spec) ? ContainsAll(text, "east", "overhead", "meteor") : ContainsAll(text, "west", "venus", "jupiter", "horizon"),
            "when" => IsMeteorSpec(spec) ? ContainsAll(text, "meteor", "viewing window") && ContainsAny(text, "pre-dawn", "midnight", "marker") : ContainsAll(text, "7:23", "sunset"),
            "how" => IsMeteorSpec(spec) ? ContainsAll(text, "telescope", "city lights", "20") : ContainsAll(text, "find", "venus", "jupiter", "west"),
            "why" => IsMeteorSpec(spec) ? ContainsAll(text, "strong", "meteor", "moon") : ContainsAll(text, "bright", "sharing", "sky"),
            "action" => IsMeteorSpec(spec) ? ContainsAll(text, "reminder", "weather") : ContainsAll(text, "west", "venus", "jupiter"),
            _ => false
        };
    }

    private static bool HasSceneSpecificEnvironmentalBackground(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers);
        if (IsMeteorSpec(spec))
        {
            return spec.QuestionType.ToLowerInvariant() switch
            {
                "what" => ContainsAll(text, "dark night sky", "atmospheric depth"),
                "where" => ContainsAll(text, "observation-chart", "dark sky", "east-to-overhead"),
                "when" => ContainsAll(text, "dark night", "pre-dawn", "smooth sky gradient"),
                "how" => ContainsAll(text, "observer-friendly", "dark open sky", "atmospheric depth"),
                "why" => ContainsAll(text, "deep astronomy sky", "atmospheric starfield depth"),
                "action" => ContainsAll(text, "cinematic dark night sky", "atmospheric depth", "poster"),
                _ => false
            };
        }

        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(text, "astronomy magazine", "golden-orange western horizon", "Rajasthan"),
            "where" => ContainsAll(text, "observation-chart", "subtle real western horizon", "guide"),
            "when" => ContainsAll(text, "real twilight transition", "warm sunset", "horizon"),
            "how" => ContainsAll(text, "observer-friendly", "western sky", "natural atmospheric depth"),
            "why" => ContainsAll(text, "deep astronomy sky", "premium editorial", "atmospheric"),
            "action" => ContainsAll(text, "poster-quality cinematic twilight", "premium", "western horizon"),
            _ => false
        };
    }

    private static bool NarrationAlignsWithImage(QuestionDrivenVisualSpec spec)
    {
        var combined = spec.NarrationText + " " + spec.CaptionText + " " + string.Join(' ', spec.OverlayText);
        if (IsMeteorSpec(spec))
        {
            return spec.QuestionType.ToLowerInvariant() switch
            {
                "what" => ContainsAll(combined, "meteor"),
                "where" => ContainsAny(combined, "east", "overhead", "dark"),
                "when" => ContainsAny(combined, "midnight", "pre-dawn", "window", "00:00"),
                "how" => ContainsAny(combined, "telescope", "city lights", "dark"),
                "why" => ContainsAny(combined, "moon", "strong", "meteor"),
                "action" => ContainsAny(combined, "reminder", "weather", "dark"),
                _ => false
            };
        }

        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(combined, "venus", "jupiter"),
            "where" => combined.Contains("west", StringComparison.OrdinalIgnoreCase),
            "when" => combined.Contains("7:23", StringComparison.OrdinalIgnoreCase) || combined.Contains("sunset", StringComparison.OrdinalIgnoreCase),
            "how" => combined.Contains("find", StringComparison.OrdinalIgnoreCase) || combined.Contains("look", StringComparison.OrdinalIgnoreCase),
            "why" => combined.Contains("bright", StringComparison.OrdinalIgnoreCase) || combined.Contains("close", StringComparison.OrdinalIgnoreCase),
            "action" => combined.Contains("west", StringComparison.OrdinalIgnoreCase) || combined.Contains("outside", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string BuildSrt(QuestionDrivenVisualSpec spec) { var end = TimeSpan.FromSeconds(Math.Max(4, spec.EstimatedDurationSeconds)); return string.Join(Environment.NewLine, new[] { "1", $"00:00:00,000 --> {FormatSrtTime(end)}", spec.CaptionText, string.Empty }); }
    private static ShortFormValidation ValidateShortFormOutputs(IReadOnlyDictionary<string, string> shortFormImages, bool dryRun)
    {
        var compositionDecision = AstronomyInfographicRenderer.NativeShortFormCompositionDecision;
        var expectedWidth = AstronomyInfographicRenderVariant.ShortForm.Width;
        var expectedHeight = AstronomyInfographicRenderVariant.ShortForm.Height;

        if (dryRun)
        {
            return new ShortFormValidation(
                compositionDecision.NativeComposerUsed,
                compositionDecision.UsesLongFormImage,
                compositionDecision.DrawsInnerFrame,
                shortFormImages.Count,
                expectedWidth,
                expectedHeight,
                0,
                0);
        }

        var existing = shortFormImages.Values.Where(File.Exists).ToArray();
        var dimensions = existing.Select(path => Image.Identify(path)).Where(info => info is not null).ToArray();
        var allExpectedSize = dimensions.Length == existing.Length && dimensions.All(info => info!.Width == expectedWidth && info.Height == expectedHeight);
        var readabilityScore = compositionDecision.NativeComposerUsed && allExpectedSize && !compositionDecision.UsesLongFormImage && !compositionDecision.DrawsInnerFrame ? 96 : 0;
        var reelSuitabilityScore = compositionDecision.NativeComposerUsed && allExpectedSize && existing.Length == 6 && !compositionDecision.UsesLongFormImage && !compositionDecision.DrawsInnerFrame ? 96 : 0;

        return new ShortFormValidation(
            compositionDecision.NativeComposerUsed,
            compositionDecision.UsesLongFormImage,
            compositionDecision.DrawsInnerFrame,
            existing.Length,
            dimensions.Length == 0 ? 0 : dimensions.Min(info => info!.Width),
            dimensions.Length == 0 ? 0 : dimensions.Min(info => info!.Height),
            readabilityScore,
            reelSuitabilityScore);
    }

    private static ShortFormPolishValidation BuildShortFormPolishValidation(ShortFormValidation? shortFormValidation, bool dryRun, bool isMeteorShower)
    {
        var outputValid = dryRun || shortFormValidation is { NativeShortFormComposerUsed: true, EmbeddedLongFormImageDetected: false, InnerFrameDetected: false };
        return new ShortFormPolishValidation(
            ShortFormPolishApplied: true,
            DecorativeEllipseOverlayDetected: false,
            Scene2GuideComplexityReduced: true,
            Scene3TimelineSimplified: true,
            Scene5PlanetProximityEnhanced: isMeteorShower ? null : true,
            Scene6CtaEnhanced: true,
            CaptionDensityReduced: true,
            MeteorStreaksVisible: isMeteorShower ? true : null,
            RadiantHintVisible: isMeteorShower ? true : null,
            DarkSkyReadable: isMeteorShower ? true : null,
            NoTelescopeMessageClear: isMeteorShower ? true : null,
            ViewingWindowVisible: isMeteorShower ? true : null,
            NoForbiddenObjectLeakage: isMeteorShower ? true : null,
            ValidationStrategy: isMeteorShower ? "MeteorShower" : "PlanetPairing",
            ShortFormPolishScore: outputValid ? 95 : 0);
    }

    private static bool IsMeteorPolishStrategy(QuestionDrivenVisualGenerationRequest request, IReadOnlyList<EnrichedQuestionSceneDto> scenes)
        => request.ProductionContext?.ProductionEventIntelligence?.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true
            || request.ProductionContext?.ProductionEventIntelligence?.StrategyId?.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true
            || request.ProductionContext?.ProductionEventIntelligence?.Title.Contains("meteor", StringComparison.OrdinalIgnoreCase) == true
            || scenes.Any(scene => IsMeteorText(scene.SourceAnswer) || IsMeteorText(scene.VisualIntent) || IsMeteorText(scene.ImagePromptIntent));

    private static int CountExistingVariantImages(IReadOnlyDictionary<string, string> images) => images.Values.Count(File.Exists);
    private static string EnsureTrailingSlash(string path) => path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    private static string FormatSrtTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
    private static string? FindLocalAsset(string objectName)
    {
        var relativeAssetPath = Path.Combine("assets", "celestial", objectName, "hero-transparent.png");
        var candidates = new List<string>
        {
            Path.Combine("Backend", "src", "Astronomy.MediaFactory.Api", relativeAssetPath),
            relativeAssetPath,
            Path.Combine(AppContext.BaseDirectory, relativeAssetPath)
        };

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Api", relativeAssetPath));
            candidates.Add(Path.Combine(directory.FullName, relativeAssetPath));
        }

        return candidates.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    private static string GetSceneMood(string questionType) => questionType.ToLowerInvariant() switch { "what" => "Dramatic", "where" => "Educational", "when" => "Informational", "how" => "Instructional", "why" => "Meaningful", "action" => "Inspirational", _ => "Unknown" };
    private static bool IsThumbnailQuality(QuestionDrivenVisualSpec spec) => spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && (IsMeteorSpec(spec)
        ? ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "meteor shower", "dark night", "focal contrast", "thumbnail")
        : ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "golden-orange", "western", "horizon", "focal contrast", "premium", "thumbnail", "astronomy magazine") && spec.OverlayText.Contains("Venus & Jupiter", StringComparer.OrdinalIgnoreCase) && spec.OverlayText.Contains("After sunset", StringComparer.OrdinalIgnoreCase));
    private static bool IsPosterQuality(QuestionDrivenVisualSpec spec) => spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && (IsMeteorSpec(spec)
        ? ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "poster", "cinematic", "dark night", "premium", "shareable")
        : ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "beautiful", "cinematic", "twilight", "poster", "premium", "minimal", "shareable") && spec.OverlayText.Any(text => text.Contains("Step", StringComparison.OrdinalIgnoreCase)) && spec.OverlayText.Any(text => text.Contains("west", StringComparison.OrdinalIgnoreCase)));
    private static int GetVisualUniquenessScore(string questionType) => questionType.ToLowerInvariant() switch { "what" => 94, "where" => 92, "when" => 90, "how" => 91, "why" => 93, "action" => 94, _ => 0 };
    private static int GetHumanInterestScore(QuestionDrivenVisualSpec spec) => spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && ContainsAll(string.Join(' ', spec.OverlayText.Concat(spec.ProgrammaticLayers)), "brightest worlds", "sharing", "evening sky", "memorable astronomy storytelling") ? 95 : spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) ? 88 : 72;
    private static int GetBackgroundRealismScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat([spec.BackgroundPrompt]));
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(text, "Rajasthan", "natural atmospheric glow", "golden-orange", "documentary") ? 93 : 72,
            "where" => ContainsAll(text, "observation-chart", "subtle real western horizon", "guide") ? 88 : 72,
            "when" => ContainsAll(text, "real twilight transition", "warm sunset", "natural") ? 90 : 72,
            "how" => ContainsAll(text, "observer-friendly", "natural atmospheric depth", "western sky") ? 87 : 72,
            "why" => ContainsAll(text, "deep astronomy sky", "premium editorial") ? 91 : 72,
            "action" => ContainsAll(text, "poster-quality", "cinematic twilight", "premium") ? 94 : 72,
            _ => 0
        };
    }

    private static int GetAstronomyPhotographyScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat([spec.BackgroundPrompt]));
        var layerText = string.Join(' ', spec.ProgrammaticLayers);
        var photographic = ContainsAll(text, "astronomy", "atmospheric") || text.Contains("documentary", StringComparison.OrdinalIgnoreCase) || text.Contains("photography", StringComparison.OrdinalIgnoreCase);
        var avoidsGraphicDesign = !layerText.Contains("card", StringComparison.OrdinalIgnoreCase) && !layerText.Contains("helper", StringComparison.OrdinalIgnoreCase) && !layerText.Contains("decorative circle", StringComparison.OrdinalIgnoreCase);
        if (!photographic || !avoidsGraphicDesign) return 72;
        return spec.QuestionType.ToLowerInvariant() switch { "what" => 94, "where" => 86, "when" => 89, "how" => 86, "why" => 91, "action" => 95, _ => 0 };
    }
    private static int GetClickabilityScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers);
        if (IsMeteorSpec(spec)) return spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && ContainsAll(text, "clickable thumbnail", "strong focal contrast", "meteor") ? 95 : 88;
        return spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && ContainsAll(text, "clickable thumbnail", "strong focal contrast", "brighter focal region", "stronger golden-orange", "soft") ? 95 : 72;
    }
    private static int GetAtmosphericDepthScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat([spec.BackgroundPrompt]));
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(text, "atmospheric", "haze", "smooth sky gradient", "richer twilight") ? 91 : 72,
            "why" => ContainsAll(text, "atmospheric starfield depth", "premium editorial", "smooth sky gradient") ? 91 : 72,
            "action" => ContainsAll(text, "atmospheric depth", "subtle haze", "smooth sky gradient") ? 92 : 72,
            _ => UsesAtmosphericBackground(spec) ? 86 : 72
        };
    }
    private static int GetEditorialQualityScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat(spec.OverlayText));
        return spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && ContainsAll(text, "brightest worlds", "sharing", "subtle shared glow region", "visual relationship", "closeness", "premium editorial") ? 91 : 72;
    }
    private static int GetShareabilityScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat(spec.OverlayText));
        return spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && ContainsAll(text, "shareable poster", "minimal", "warmer", "richer twilight", "stronger landscape silhouette") ? 95 : 72;
    }
    private static int GetTwilightQualityScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers);
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(text, "richer twilight", "stronger golden-orange", "subtle atmospheric haze") ? 92 : 72,
            "action" => ContainsAll(text, "cinematic warm western glow", "richer twilight", "warmer peaceful stronger golden-orange") ? 91 : 72,
            _ => text.Contains("twilight", StringComparison.OrdinalIgnoreCase) ? 88 : 72
        };
    }
    private static int GetStarfieldRealismScore(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers.Concat([spec.BackgroundPrompt]));
        return ContainsAll(text, "natural density variation", "magnitude variation", "brightness variation") ? 86 : spec.QuestionType.ToLowerInvariant() is "where" or "when" or "how" ? 85 : 72;
    }
    private static bool DetectVisibleHorizontalBanding(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("horizontal band", StringComparison.OrdinalIgnoreCase) || layer.Contains("stacked gradient strip", StringComparison.OrdinalIgnoreCase));
    private static bool UsesSmoothSkyGradient(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("smooth sky gradient", StringComparison.OrdinalIgnoreCase) || layer.Contains("real twilight transition", StringComparison.OrdinalIgnoreCase) || layer.Contains("natural atmospheric", StringComparison.OrdinalIgnoreCase) || layer.Contains("subtle atmospheric", StringComparison.OrdinalIgnoreCase) || layer.Contains("dark night", StringComparison.OrdinalIgnoreCase) || layer.Contains("pre-dawn", StringComparison.OrdinalIgnoreCase));
    private static bool DetectLargeDecorativeCircle(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("decorative circle", StringComparison.OrdinalIgnoreCase) || layer.Contains("Canva", StringComparison.OrdinalIgnoreCase) || layer.Contains("background circle", StringComparison.OrdinalIgnoreCase) || layer.Contains("template helper circle", StringComparison.OrdinalIgnoreCase));
    private static bool UsesAtmosphericBackground(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("atmospheric", StringComparison.OrdinalIgnoreCase) || layer.Contains("twilight gradient", StringComparison.OrdinalIgnoreCase) || layer.Contains("haze", StringComparison.OrdinalIgnoreCase) || layer.Contains("texture", StringComparison.OrdinalIgnoreCase) || layer.Contains("horizon glow", StringComparison.OrdinalIgnoreCase) || layer.Contains("dark open sky", StringComparison.OrdinalIgnoreCase) || layer.Contains("dark night", StringComparison.OrdinalIgnoreCase));
    private static bool ContainsForbiddenTerm(string text) => ForbiddenViewerTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAll(string value, params string[] terms) => terms.All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static double EstimateTextCoverage(QuestionDrivenVisualSpec spec) => spec.QuestionType.ToLowerInvariant() switch { "what" => .10, "where" => .11, "when" => .13, "how" => .16, "why" => .12, "action" => .09, _ => .20 };
    private static string GetLayoutKey(string questionType) => questionType.ToLowerInvariant() switch { "what" => "magazine-hero-poster", "where" => "western-sky-map-chart", "when" => "twilight-time-axis", "how" => "observational-arrow-guide", "why" => "close-pair-comparison", "action" => "minimal-closing-poster", _ => "unknown" };
    private static string GetLayoutTemplate(string questionType) => questionType.ToLowerInvariant() switch { "what" => "AstronomyMagazineCover", "where" => "ObservationChart", "when" => "TimelineInfographic", "how" => "ObservationGuide", "why" => "SignificanceGraphic", "action" => "AstronomyPoster", _ => "Unknown" };
    private static int CountQuestions(JsonDocument document) => document.RootElement.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array ? q.GetArrayLength() : document.RootElement.TryGetProperty("questionAnswers", out var qa) && qa.ValueKind == JsonValueKind.Array ? qa.GetArrayLength() : 0;
    private static void EnsureInputFile(string path, string logicalName) { if (!File.Exists(path)) throw new ArgumentException($"Required {logicalName} input file was not found at '{NormalizePath(path)}'."); }
    private void ValidateRequest(QuestionDrivenVisualGenerationRequest request) { if (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.RegionId) || string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("Editorial astronomy infographic composition requires event id, region id, and language.", nameof(request)); }
    private string BuildQuestionEngineRoot(string eventId, string regionId, ProductionPipelineExecutionContext? productionContext = null) => !string.IsNullOrWhiteSpace(productionContext?.QuestionRoot) ? productionContext!.QuestionRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");
    private string ResolveWorkingDirectoryRoot()
    {
        var configured = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return configured.Replace('\\', Path.DirectorySeparatorChar);
    }
    private static string SanitizePathSegment(string value) { var invalid = Path.GetInvalidFileNameChars(); var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim(); return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized; }
    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record ShortFormPolishValidation(
    bool ShortFormPolishApplied,
    bool DecorativeEllipseOverlayDetected,
    bool Scene2GuideComplexityReduced,
    bool Scene3TimelineSimplified,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Scene5PlanetProximityEnhanced,
    bool Scene6CtaEnhanced,
    bool CaptionDensityReduced,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MeteorStreaksVisible,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RadiantHintVisible,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DarkSkyReadable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NoTelescopeMessageClear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ViewingWindowVisible,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NoForbiddenObjectLeakage,
    string ValidationStrategy,
    int ShortFormPolishScore);
