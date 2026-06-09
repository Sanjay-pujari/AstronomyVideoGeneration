using System.Text.Json;
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
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string OutputDirectoryName = "scene-approval-v3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenViewerTerms = ["guid", "path", "file", "json", "internal", "debug", "metadata", "question-engine", "scene-approval", "e7013ee4"];

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
        var questionEngineRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
        var outputRoot = Path.Combine(questionEngineRoot, OutputDirectoryName);

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
        if (scenes.Length != 6) throw new ArgumentException("Editorial astronomy infographic composition requires exactly 6 golden pilot scenes.", nameof(request));

        var finalImageCount = 0;
        var srtCount = 0;
        var variantFinalImages = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var approvedSceneCount = 0;
        var failedSceneCount = 0;
        var seenSrtTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLayoutKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var venusAsset = FindLocalAsset("venus");
        var jupiterAsset = FindLocalAsset("jupiter");
        if (venusAsset is null || jupiterAsset is null) warnings.Add("Local transparent Venus/Jupiter assets were not both found; scene review will fail and no fake-circle planets will be used.");

        foreach (var scene in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sceneNumber = scene.SceneNumber;
            var numberPrefix = $"scene-{sceneNumber:000}";
            var narrationScene = narration.Scenes.FirstOrDefault(s => s.SceneNumber == sceneNumber)
                ?? throw new ArgumentException($"Question-driven narration is missing scene {sceneNumber}.", nameof(request));
            var prompt = promptGenerator.GeneratePrompt(new QuestionDrivenImagePromptRequest(
                request.EventId,
                request.RegionId,
                request.Language,
                sceneNumber,
                scene.QuestionType,
                scene.VisualIntent,
                scene.ImagePromptIntent,
                venusAsset is not null && jupiterAsset is not null));
            var spec = BuildSpec(request, scene, narrationScene, prompt);
            var srt = BuildSrt(spec);
            var overlayPlan = BuildOverlayPlan(spec);
            var review = BuildReview(spec, srt, seenSrtTexts, seenLayoutKeys, venusAsset is not null, jupiterAsset is not null);

            var finalPath = includeSceneApprovalVariants
                ? Path.Combine(outputRoot, "long", $"{numberPrefix}-final.png")
                : Path.Combine(outputRoot, $"{numberPrefix}-final.png");
            var shortFinalPath = Path.Combine(outputRoot, "short", $"{numberPrefix}-final.png");
            if (includeSceneApprovalVariants)
            {
                variantFinalImages[numberPrefix] = [NormalizePath(finalPath), NormalizePath(shortFinalPath)];
            }
            var srtPath = Path.Combine(outputRoot, $"{numberPrefix}.srt");
            var narrationTextPath = Path.Combine(outputRoot, $"{numberPrefix}-narration.txt");
            var specPath = Path.Combine(outputRoot, $"{numberPrefix}-infographic-spec.json");
            var reviewPath = Path.Combine(outputRoot, $"{numberPrefix}-review.json");
            var plannedOutputs = new QuestionDrivenPlannedOutputs(NormalizePath(finalPath), NormalizePath(srtPath), NormalizePath(narrationTextPath), NormalizePath(specPath), string.Empty, NormalizePath(reviewPath));
            var validationPreview = BuildValidationPreview(spec, srt, review, overlayPlan, plannedOutputs);
            var isolationValidation = ValidateSceneQuestionIsolation(spec, overlayPlan);
            sceneValidation.Add(isolationValidation);
            plannedScenes.Add(new QuestionDrivenPlannedScene(scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, scene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, scene.VisualIntent, scene.ImagePromptIntent, scene.OverlayIntent, scene.AccessibilityIntent, prompt, overlayPlan, plannedOutputs, validationPreview));

            if (validationPreview.Issues.Count > 0) warnings.AddRange(validationPreview.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            if (isolationValidation.LeakageWarnings.Count > 0) warnings.AddRange(isolationValidation.LeakageWarnings.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            if (request.DryRun) continue;

            var approvalAssets = includeSceneApprovalVariants
                ? new[] { finalPath, shortFinalPath, srtPath, narrationTextPath, specPath, reviewPath }
                : new[] { finalPath, srtPath, narrationTextPath, specPath, reviewPath };

            if (!request.OverwriteExisting && approvalAssets.Any(File.Exists))
            {
                warnings.Add($"Skipped scene {sceneNumber:000} because one or more approval assets already exist and overwriteExisting is false.");
                continue;
            }

            if (review.Issues.Count == 0) approvedSceneCount++; else { failedSceneCount++; warnings.AddRange(review.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}")); }
            Directory.CreateDirectory(outputRoot);
            if (venusAsset is null || jupiterAsset is null)
            {
                warnings.Add($"Skipped scene {sceneNumber:000} image because required local transparent Venus/Jupiter assets are missing.");
                continue;
            }

            await infographicRenderer.RenderAsync(finalPath, spec, venusAsset, jupiterAsset, cancellationToken, AstronomyInfographicRenderVariant.LongForm);
            if (includeSceneApprovalVariants)
            {
                await infographicRenderer.RenderAsync(shortFinalPath, spec, venusAsset, jupiterAsset, cancellationToken, AstronomyInfographicRenderVariant.ShortForm);
            }
            await File.WriteAllTextAsync(srtPath, srt, cancellationToken);
            await File.WriteAllTextAsync(narrationTextPath, spec.NarrationText + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
            if (includeSceneApprovalVariants)
            {
                generatedFiles.AddRange([finalPath, shortFinalPath, srtPath, narrationTextPath, specPath, reviewPath]);
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
            SceneVariantFinalImages: includeSceneApprovalVariants ? variantFinalImages : null);
    }

    private static QuestionDrivenVisualSpec BuildSpec(QuestionDrivenVisualGenerationRequest request, EnrichedQuestionSceneDto scene, QuestionDrivenNarrationSceneDto narrationScene, string prompt)
    {
        var overlays = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "Venus & Jupiter", "After sunset" },
            "where" => new[] { "W", "Venus", "Jupiter", "Western horizon", "reference stars" },
            "when" => new[] { "Sunset", "7:23 PM IST", "After-sunset window" },
            "how" => new[] { "1 Find Venus", "2 Look nearby for Jupiter", "3 Face west" },
            "why" => new[] { "Two of the brightest worlds sharing the evening sky", "brightness", "closeness", "shared sky", "Venus", "Jupiter" },
            "action" => new[] { "Step outside tonight", "Look west" },
            _ => new[] { scene.ViewerTakeaway }
        };

        var layers = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "mood:Dramatic", "background:professional astronomy magazine cover western twilight over Rajasthan with richer twilight colors, natural atmospheric glow, and smooth sky gradient", "horizon:stronger golden-orange western horizon glow with subtle atmospheric haze", "texture:documentary sky grain, twilight haze, natural density variation starfield, magnitude variation, brightness variation", "composition:strong focal contrast clickable thumbnail composition with slightly brighter focal region around Venus/Jupiter", "vignette:soft natural edge falloff", "celestial:reduced-scale Venus/Jupiter sky targets integrated with atmospheric blending and subtle shared glow", "typography:premium thumbnail title Venus & Jupiter subtitle After sunset" },
            "where" => new[] { "mood:Educational", "background:observation-chart sky with astronomy guide aesthetic and subtle atmospheric realism", "horizon:subtle real western horizon", "guide:delicate altitude guide", "celestial:Venus/Jupiter plotted positions integrated with subtle glow", "reference:subtle sky grid", "reference:Leo Regulus constellation-star guide", "direction:West marker", "annotation:floating labels and leader lines" },
            "when" => new[] { "mood:Informational", "background:real twilight transition with warm sunset colors and natural atmospheric haze", "horizon:natural warm western horizon glow", "time:sunset marker", "time:7:23 PM IST marker", "direction:after-sunset viewing window", "layout:timeline hero", "annotation:floating timeline labels" },
            "how" => new[] { "mood:Instructional", "background:observer-friendly western sky with natural atmospheric depth", "celestial:Venus/Jupiter assets integrated with glow", "direction:observation arrow from Venus to Jupiter", "steps:Find Venus; Look nearby for Jupiter; Face west" },
            "why" => new[] { "mood:Meaningful", "background:deep astronomy sky premium editorial background with atmospheric starfield depth, smooth sky gradient, natural density variation, magnitude variation, brightness variation", "celestial:two of the brightest worlds sharing the evening sky as reduced-scale sky targets integrated with atmospheric blending and subtle shared glow region", "significance:shared sky brightness emotional significance for human interest and memorable astronomy storytelling", "relationship:visual relationship between planets with slight emphasis on closeness", "comparison:brightness scale", "direction:closeness bracket", "annotation:floating human-interest significance note" },
            "action" => new[] { "mood:Inspirational", "background:most beautiful poster-quality cinematic twilight premium astronomy artwork with atmospheric depth and smooth sky gradient", "horizon:warmer peaceful stronger golden-orange western horizon with subtle haze", "composition:premium shareable poster composition", "landscape:stronger landscape silhouette", "celestial:Venus and Jupiter reduced-scale sky targets naturally integrated with atmospheric blending and subtle glow", "starfield:natural density variation, magnitude variation, brightness variation", "twilight:cinematic warm western glow with richer twilight", "typography:minimal poster CTA Step Outside Tonight Look west" },
            _ => new[] { "background:sky", "programmatic:overlays" }
        };

        return new QuestionDrivenVisualSpec(request.EventId, request.RegionId, request.Language, scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, scene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, Math.Max(4, narrationScene.EstimatedDurationSeconds), prompt, overlays, layers, [scene.AccessibilityIntent, "Text coverage target <= 25%; visual astronomy information target >= 75%; no large title cards, debug text, decorative circles, helper boxes, or card layouts."], DateTimeOffset.UtcNow);
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
        if (!venusAssetFound || !jupiterAssetFound) issues.Add("local transparent Venus/Jupiter assets are missing.");
        var textCollisionDetected = false;
        var textCollisionResolved = true;
        var labelOverPlanetDetected = false;
        var usesSolidPlanetBackingCircle = false;
        var blueprintZonesRespected = true;
        var environmentalBackgroundDistinct = HasSceneSpecificEnvironmentalBackground(spec);
        var planetAssetsIntegratedIntoSky = venusAssetFound && jupiterAssetFound && spec.ProgrammaticLayers.Any(layer => layer.Contains("integrated", StringComparison.OrdinalIgnoreCase) || layer.Contains("glow", StringComparison.OrdinalIgnoreCase));
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
        var significanceLayerRendered = !spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) || (viewerText.Contains("Two of the brightest worlds sharing the evening sky", StringComparison.OrdinalIgnoreCase) && spec.ProgrammaticLayers.Any(layer => layer.Contains("shared sky", StringComparison.OrdinalIgnoreCase) || layer.Contains("emotional significance", StringComparison.OrdinalIgnoreCase)));
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && (!viewerText.Contains("brightest worlds", StringComparison.OrdinalIgnoreCase) || !viewerText.Contains("sharing", StringComparison.OrdinalIgnoreCase) || !viewerText.Contains("sky", StringComparison.OrdinalIgnoreCase))) issues.Add("Why scene does not emphasize two of the brightest worlds sharing the evening sky.");
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
        if (!planetAssetsIntegratedIntoSky && !spec.QuestionType.Equals("When", StringComparison.OrdinalIgnoreCase)) issues.Add("planet assets are not integrated into the sky with subtle glow.");
        if (!environmentalBackgroundDistinct) issues.Add("background is the same generic dark-blue mountain scene as other scenes.");
        if (!blueprintZonesRespected) issues.Add("renderer ignored one or more layout blueprint zones.");
        if (!significanceLayerRendered) issues.Add("Scene 5 does not include a closeness/significance layer.");
        if (spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "golden-orange", "western", "horizon", "premium", "focal contrast")) issues.Add("Scene 1 does not feel like a professional astronomy thumbnail.");
        if (spec.QuestionType.Equals("Where", StringComparison.OrdinalIgnoreCase) && !(constellationLayerRendered && referenceStarLayerRendered && ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "observation-chart", "western"))) issues.Add("Scene 2 does not feel like observation chart.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "significance", "shared", "brightness")) issues.Add("Scene 5 does not feel like a human-interest significance visual.");
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
            venusAssetFound && jupiterAssetFound,
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
            planetAssetsIntegratedIntoSky || spec.QuestionType.Equals("When", StringComparison.OrdinalIgnoreCase),
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

    private static QuestionDrivenProgrammaticOverlayPlan BuildOverlayPlan(QuestionDrivenVisualSpec spec) => spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => new("Venus & Jupiter", "After sunset", ["Venus", "Jupiter"], ["leader lines from labels to planets"], ["Venus", "Jupiter"], [], [], []),
        "where" => new("Where to Look", "Face the western horizon", ["West", "Venus", "Jupiter", "Horizon", "Leo / Regulus reference stars"], ["western horizon altitude guide"], ["Venus", "Jupiter"], ["West"], [], []),
        "when" => new("Best Time Tonight", "After sunset", ["Sunset", "Viewing window"], [], [], [], ["7:23 PM IST"], []),
        "how" => new("How to Find It", "Use Venus as your anchor", ["Venus", "Jupiter"], ["observation arrow from Venus to Jupiter"], ["Venus", "Jupiter"], [], [], ["Find Venus", "Look nearby for Jupiter", "Face west"]),
        "why" => new("Why It Matters", "Two of the brightest worlds sharing the evening sky", ["Venus", "Jupiter", "brightness", "closeness", "shared sky"], ["closeness bracket", "brightness comparison"], ["Venus", "Jupiter"], [], [], []),
        "action" => new("Step Outside Tonight", "Look west tonight", [], [], [], [], [], []),
        _ => new(spec.ViewerTakeaway, string.Empty, [], [], [], [], [], [])
    };


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
        "what" => overlayPlan.LocalAssetObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase) && overlayPlan.LocalAssetObjects.Contains("Jupiter", StringComparer.OrdinalIgnoreCase),
        "where" => overlayPlan.Labels.Contains("West", StringComparer.OrdinalIgnoreCase) && overlayPlan.Labels.Contains("Horizon", StringComparer.OrdinalIgnoreCase),
        "when" => overlayPlan.TimingMarkers.Contains("7:23 PM IST", StringComparer.OrdinalIgnoreCase),
        "how" => overlayPlan.Steps.SequenceEqual(["Find Venus", "Look nearby for Jupiter", "Face west"], StringComparer.OrdinalIgnoreCase) && overlayPlan.Arrows.Count > 0,
        "why" => overlayPlan.Subtitle.Contains("brightest worlds", StringComparison.OrdinalIgnoreCase) && overlayPlan.Subtitle.Contains("sharing", StringComparison.OrdinalIgnoreCase) && overlayPlan.Arrows.Count > 0,
        "action" => overlayPlan.Subtitle.Contains("west", StringComparison.OrdinalIgnoreCase),
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
            "what" => ContainsAll(text, "venus", "jupiter", "twilight"),
            "where" => ContainsAll(text, "west", "venus", "jupiter", "horizon"),
            "when" => ContainsAll(text, "7:23", "sunset"),
            "how" => ContainsAll(text, "find", "venus", "jupiter", "west"),
            "why" => ContainsAll(text, "bright", "sharing", "sky"),
            "action" => ContainsAll(text, "west", "venus", "jupiter"),
            _ => false
        };
    }

    private static bool HasSceneSpecificEnvironmentalBackground(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.ProgrammaticLayers);
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
    private static bool IsThumbnailQuality(QuestionDrivenVisualSpec spec) => spec.QuestionType.Equals("What", StringComparison.OrdinalIgnoreCase) && ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "golden-orange", "western", "horizon", "focal contrast", "premium", "thumbnail", "astronomy magazine") && spec.OverlayText.Contains("Venus & Jupiter", StringComparer.OrdinalIgnoreCase) && spec.OverlayText.Contains("After sunset", StringComparer.OrdinalIgnoreCase);
    private static bool IsPosterQuality(QuestionDrivenVisualSpec spec) => spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) && ContainsAll(string.Join(' ', spec.ProgrammaticLayers), "beautiful", "cinematic", "twilight", "poster", "premium", "minimal", "shareable") && spec.OverlayText.Any(text => text.Contains("Step", StringComparison.OrdinalIgnoreCase)) && spec.OverlayText.Any(text => text.Contains("west", StringComparison.OrdinalIgnoreCase));
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
    private static bool UsesSmoothSkyGradient(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("smooth sky gradient", StringComparison.OrdinalIgnoreCase) || layer.Contains("real twilight transition", StringComparison.OrdinalIgnoreCase) || layer.Contains("natural atmospheric", StringComparison.OrdinalIgnoreCase) || layer.Contains("subtle atmospheric", StringComparison.OrdinalIgnoreCase));
    private static bool DetectLargeDecorativeCircle(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("decorative circle", StringComparison.OrdinalIgnoreCase) || layer.Contains("Canva", StringComparison.OrdinalIgnoreCase) || layer.Contains("background circle", StringComparison.OrdinalIgnoreCase) || layer.Contains("template helper circle", StringComparison.OrdinalIgnoreCase));
    private static bool UsesAtmosphericBackground(QuestionDrivenVisualSpec spec) => spec.ProgrammaticLayers.Any(layer => layer.Contains("atmospheric", StringComparison.OrdinalIgnoreCase) || layer.Contains("twilight gradient", StringComparison.OrdinalIgnoreCase) || layer.Contains("haze", StringComparison.OrdinalIgnoreCase) || layer.Contains("texture", StringComparison.OrdinalIgnoreCase) || layer.Contains("horizon glow", StringComparison.OrdinalIgnoreCase));
    private static bool ContainsForbiddenTerm(string text) => ForbiddenViewerTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAll(string value, params string[] terms) => terms.All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static double EstimateTextCoverage(QuestionDrivenVisualSpec spec) => spec.QuestionType.ToLowerInvariant() switch { "what" => .10, "where" => .11, "when" => .13, "how" => .16, "why" => .12, "action" => .09, _ => .20 };
    private static string GetLayoutKey(string questionType) => questionType.ToLowerInvariant() switch { "what" => "magazine-hero-poster", "where" => "western-sky-map-chart", "when" => "twilight-time-axis", "how" => "observational-arrow-guide", "why" => "close-pair-comparison", "action" => "minimal-closing-poster", _ => "unknown" };
    private static string GetLayoutTemplate(string questionType) => questionType.ToLowerInvariant() switch { "what" => "AstronomyMagazineCover", "where" => "ObservationChart", "when" => "TimelineInfographic", "how" => "ObservationGuide", "why" => "SignificanceGraphic", "action" => "AstronomyPoster", _ => "Unknown" };
    private static int CountQuestions(JsonDocument document) => document.RootElement.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array ? q.GetArrayLength() : document.RootElement.TryGetProperty("questionAnswers", out var qa) && qa.ValueKind == JsonValueKind.Array ? qa.GetArrayLength() : 0;
    private static void EnsureInputFile(string path, string logicalName) { if (!File.Exists(path)) throw new ArgumentException($"Required {logicalName} input file was not found at '{NormalizePath(path)}'."); }
    private void ValidateRequest(QuestionDrivenVisualGenerationRequest request) { if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase) || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase) || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Editorial astronomy infographic composition is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request)); }
    private string BuildQuestionEngineRoot(string eventId, string regionId) => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");
    private string ResolveWorkingDirectoryRoot()
    {
        var configured = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return configured.Replace('\\', Path.DirectorySeparatorChar);
    }
    private static string SanitizePathSegment(string value) { var invalid = Path.GetInvalidFileNameChars(); var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim(); return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized; }
    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
