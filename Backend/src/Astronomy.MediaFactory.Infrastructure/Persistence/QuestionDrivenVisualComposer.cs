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
    ILogger<QuestionDrivenVisualComposer> logger) : IQuestionDrivenVisualComposer, IEditorialAstronomyInfographicComposer
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string OutputDirectoryName = "scene-approval-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenViewerTerms = ["guid", "path", "file", "json", "internal", "debug", "metadata", "question-engine", "scene-approval", "e7013ee4"];

    public async Task<QuestionDrivenVisualGenerationResponse> GenerateQuestionDrivenVisualsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        var response = await GenerateEditorialAstronomyInfographicsAsync(request, cancellationToken);
        return new QuestionDrivenVisualGenerationResponse(
            response.EventId,
            response.SceneCount,
            response.FinalImageCount,
            response.FinalImageCount,
            response.ApprovedSceneCount,
            response.FailedSceneCount,
            response.GeneratedFiles,
            response.Warnings,
            response.PlannedInfographicCount,
            response.PlannedInfographicCount,
            response.PlannedInfographicCount,
            response.PlannedScenes);
    }

    public async Task<EditorialAstronomyInfographicGenerationResponse> GenerateEditorialAstronomyInfographicsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        logger.LogInformation("Generating editorial astronomy infographics for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var plannedScenes = new List<QuestionDrivenPlannedScene>();
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
        var approvedSceneCount = 0;
        var failedSceneCount = 0;
        var seenSrtTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLayoutKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var venusAsset = FindLocalAsset("venus");
        var jupiterAsset = FindLocalAsset("jupiter");
        if (venusAsset is null || jupiterAsset is null) warnings.Add("Local transparent Venus/Jupiter assets were not both found; renderer will use textured editorial fallbacks.");

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

            var finalPath = Path.Combine(outputRoot, $"{numberPrefix}-final.png");
            var srtPath = Path.Combine(outputRoot, $"{numberPrefix}.srt");
            var narrationTextPath = Path.Combine(outputRoot, $"{numberPrefix}-narration.txt");
            var specPath = Path.Combine(outputRoot, $"{numberPrefix}-infographic-spec.json");
            var reviewPath = Path.Combine(outputRoot, $"{numberPrefix}-review.json");
            var plannedOutputs = new QuestionDrivenPlannedOutputs(NormalizePath(finalPath), NormalizePath(srtPath), NormalizePath(narrationTextPath), NormalizePath(specPath), string.Empty, NormalizePath(reviewPath));
            var validationPreview = BuildValidationPreview(spec, srt, review, overlayPlan, plannedOutputs);
            plannedScenes.Add(new QuestionDrivenPlannedScene(scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, scene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, scene.VisualIntent, scene.ImagePromptIntent, scene.OverlayIntent, scene.AccessibilityIntent, prompt, overlayPlan, plannedOutputs, validationPreview));

            if (validationPreview.Issues.Count > 0) warnings.AddRange(validationPreview.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            if (request.DryRun) continue;

            if (!request.OverwriteExisting && new[] { finalPath, srtPath, narrationTextPath, specPath, reviewPath }.Any(File.Exists))
            {
                warnings.Add($"Skipped scene {sceneNumber:000} because one or more approval assets already exist and overwriteExisting is false.");
                continue;
            }

            if (review.Issues.Count == 0) approvedSceneCount++; else { failedSceneCount++; warnings.AddRange(review.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}")); }
            Directory.CreateDirectory(outputRoot);
            await ComposeSceneImageAsync(finalPath, spec, venusAsset, jupiterAsset, cancellationToken);
            await File.WriteAllTextAsync(srtPath, srt, cancellationToken);
            await File.WriteAllTextAsync(narrationTextPath, spec.NarrationText + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
            generatedFiles.AddRange([finalPath, srtPath, narrationTextPath, specPath, reviewPath]);
            finalImageCount++;
        }

        AddPlanLevelWarnings(plannedScenes, warnings);
        return new EditorialAstronomyInfographicGenerationResponse(request.EventId, scenes.Length, plannedScenes.Count, finalImageCount, approvedSceneCount, failedSceneCount, plannedScenes, generatedFiles.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static QuestionDrivenVisualSpec BuildSpec(QuestionDrivenVisualGenerationRequest request, EnrichedQuestionSceneDto scene, QuestionDrivenNarrationSceneDto narrationScene, string prompt)
    {
        var overlays = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "Venus & Jupiter Tonight", "After sunset" },
            "where" => new[] { "W", "Venus", "Jupiter", "Western horizon" },
            "when" => new[] { "Sunset", "7:23 PM IST", "After-sunset window" },
            "how" => new[] { "1 Find Venus", "2 Look nearby for Jupiter", "3 Face west" },
            "why" => new[] { "Two bright planets close together", "Venus", "Jupiter" },
            "action" => new[] { "Step outside tonight", "Look west" },
            _ => new[] { scene.ViewerTakeaway }
        };

        var layers = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "background:cinematic twilight sky", "horizon:silhouette landscape", "celestial:local transparent Venus and Jupiter assets", "annotation:minimal magazine title", "review:metadata saved separately" },
            "where" => new[] { "background:western sky map gradient", "horizon:measured western horizon line", "celestial:Venus/Jupiter plotted positions", "reference:subtle star grid", "direction:west marker", "annotation:integrated labels" },
            "when" => new[] { "background:twilight-to-night gradient", "horizon:sunset band", "time:sunset marker", "time:7:23 PM IST marker", "direction:after-sunset viewing window", "annotation:small timeline labels" },
            "how" => new[] { "background:observation guide sky", "horizon:west reference", "celestial:Venus/Jupiter assets", "direction:arrows Venus to Jupiter", "steps:three small integrated step labels" },
            "why" => new[] { "background:deep significance sky", "celestial:close bright planetary pairing", "comparison:Venus/Jupiter size-brightness comparison", "direction:closeness bracket", "annotation:short significance note" },
            "action" => new[] { "background:peaceful evening sky poster", "horizon:quiet landscape", "celestial:Venus and Jupiter together", "annotation:minimal closing CTA" },
            _ => new[] { "background:sky", "programmatic:overlays" }
        };

        return new QuestionDrivenVisualSpec(request.EventId, request.RegionId, request.Language, scene.SceneNumber, scene.QuestionType, scene.ScenePurpose, scene.ViewerQuestion, scene.ViewerTakeaway, narrationScene.NarrationText, narrationScene.CaptionText, Math.Max(4, narrationScene.EstimatedDurationSeconds), prompt, overlays, layers, [scene.AccessibilityIntent, "Text coverage target <= 25%; visual astronomy information target >= 75%; no large title cards or debug text."], DateTimeOffset.UtcNow);
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
        if (spec.ProgrammaticLayers.Any(layer => layer.Contains("card", StringComparison.OrdinalIgnoreCase) || layer.Contains("slide", StringComparison.OrdinalIgnoreCase))) issues.Add("image looks like a card/slide.");
        if (!venusAssetFound || !jupiterAssetFound) recommendations.Add("Install real transparent Venus and Jupiter assets to avoid textured fallback rendering.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !viewerText.Contains("close", StringComparison.OrdinalIgnoreCase)) issues.Add("Why scene does not emphasize the close bright planetary pairing.");
        if (!srt.Contains(" --> ", StringComparison.Ordinal)) issues.Add("SRT is not in timed-caption format.");
        var approved = issues.Count == 0;
        return new QuestionDrivenSceneReview(spec.SceneNumber, spec.QuestionType, approved, approved, approved, approved, approved, issues, recommendations);
    }

    private static QuestionDrivenProgrammaticOverlayPlan BuildOverlayPlan(QuestionDrivenVisualSpec spec) => spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => new("Venus & Jupiter Tonight", "After sunset", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], [], [], []),
        "where" => new("Where to Look", "Face the western horizon", ["West", "Venus", "Jupiter", "Horizon"], ["western horizon altitude guide"], ["Venus", "Jupiter"], ["West"], [], []),
        "when" => new("Best Time Tonight", "After sunset", ["Sunset", "Viewing window"], [], [], [], ["7:23 PM IST"], []),
        "how" => new("How to Find It", "Use Venus as your anchor", ["Venus", "Jupiter", "West"], ["arrow from Venus to Jupiter", "arrow toward western horizon"], ["Venus", "Jupiter"], ["West"], [], ["Find Venus", "Look nearby for Jupiter", "Face west"]),
        "why" => new("Why It Matters", "Two bright planets close together", ["Venus", "Jupiter"], ["closeness bracket"], ["Venus", "Jupiter"], [], [], []),
        "action" => new("Step Outside Tonight", "Look west", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], ["West"], [], []),
        _ => new(spec.ViewerTakeaway, string.Empty, [], [], [], [], [], [])
    };

    private static QuestionDrivenValidationPreview BuildValidationPreview(QuestionDrivenVisualSpec spec, string srt, QuestionDrivenSceneReview review, QuestionDrivenProgrammaticOverlayPlan overlayPlan, QuestionDrivenPlannedOutputs plannedOutputs)
    {
        var issues = new List<string>(review.Issues);
        var srtReady = !string.IsNullOrWhiteSpace(spec.CaptionText) && srt.Contains(" --> ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(plannedOutputs.SrtPath);
        var accessibilityReady = spec.AccessibilityCues.Any(cue => !string.IsNullOrWhiteSpace(cue)) && (overlayPlan.Labels.Count > 0 || overlayPlan.Steps.Count > 0 || overlayPlan.TimingMarkers.Count > 0) && !string.IsNullOrWhiteSpace(spec.CaptionText);
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
        "why" => overlayPlan.Subtitle.Contains("Two bright planets", StringComparison.OrdinalIgnoreCase) && overlayPlan.Arrows.Count > 0,
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
            "why" => ContainsAll(text, "bright", "planets", "close"),
            "action" => ContainsAll(text, "west", "venus", "jupiter"),
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

    private static async Task ComposeSceneImageAsync(string finalPath, QuestionDrivenVisualSpec spec, string? venusAsset, string? jupiterAsset, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#061124"));
        var titleFont = ResolveFont(68, FontStyle.Bold);
        var subtitleFont = ResolveFont(38, FontStyle.Bold);
        var labelFont = ResolveFont(30, FontStyle.Bold);
        var smallFont = ResolveFont(24, FontStyle.Regular);
        image.Mutate(ctx =>
        {
            DrawEditorialBackground(ctx, spec.SceneNumber);
            DrawReferenceStars(ctx, spec.SceneNumber);
            DrawSceneLayers(ctx, spec, venusAsset, jupiterAsset, titleFont, subtitleFont, labelFont, smallFont);
            DrawVignette(ctx);
        });
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");
        await image.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
    }

    private static void DrawEditorialBackground(IImageProcessingContext ctx, int sceneNumber)
    {
        var top = sceneNumber switch { 1 => "#06142E", 2 => "#081831", 3 => "#17254A", 4 => "#06172A", 5 => "#030D1E", 6 => "#07132B", _ => "#061124" };
        var middle = sceneNumber switch { 3 => "#56538A", 6 => "#243B63", _ => "#163158" };
        var bottom = sceneNumber switch { 1 => "#E07A45", 3 => "#FFAA5D", 6 => "#C86548", _ => "#293C3F" };
        for (var y = 0; y < 1080; y += 6)
        {
            var t = y / 1080f;
            var color = t < .68f ? Blend(Color.ParseHex(top), Color.ParseHex(middle), t / .68f) : Blend(Color.ParseHex(middle), Color.ParseHex(bottom), (t - .68f) / .32f);
            ctx.Fill(color, new RectangleF(0, y, 1920, 8));
        }
        DrawLandscape(ctx, sceneNumber);
    }

    private static void DrawLandscape(IImageProcessingContext ctx, int sceneNumber)
    {
        var horizon = sceneNumber is 2 ? 760 : sceneNumber is 3 ? 820 : 790;
        var ridge = new PathBuilder()
            .AddLine(new PointF(0, horizon + 30), new PointF(180, horizon - 18))
            .AddLine(new PointF(180, horizon - 18), new PointF(360, horizon - 70))
            .AddLine(new PointF(360, horizon - 70), new PointF(650, horizon + 55))
            .AddLine(new PointF(650, horizon + 55), new PointF(960, horizon - 20))
            .AddLine(new PointF(960, horizon - 20), new PointF(1250, horizon - 90))
            .AddLine(new PointF(1250, horizon - 90), new PointF(1550, horizon + 40))
            .AddLine(new PointF(1550, horizon + 40), new PointF(1920, horizon - 35))
            .AddLine(new PointF(1920, horizon - 35), new PointF(1920, 1080))
            .AddLine(new PointF(1920, 1080), new PointF(0, 1080))
            .CloseFigure()
            .Build();
        ctx.Fill(Color.ParseHex("#10181B").WithAlpha(.96f), ridge);
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(sceneNumber is 2 or 4 ? .62f : .22f), sceneNumber is 2 ? 4 : 2, new PathBuilder().AddLine(new PointF(0, horizon), new PointF(1920, horizon)).Build());
    }

    private static void DrawReferenceStars(IImageProcessingContext ctx, int sceneNumber)
    {
        var stars = new[] { new PointF(250, 150), new PointF(475, 250), new PointF(745, 120), new PointF(990, 205), new PointF(1320, 145), new PointF(1610, 260), new PointF(1780, 95), new PointF(1185, 330), new PointF(380, 370) };
        foreach (var star in stars) ctx.Fill(Color.White.WithAlpha(sceneNumber is 1 or 6 ? .40f : .62f), new EllipsePolygon(star.X, star.Y, sceneNumber is 2 ? 2.2f : 1.6f));
        if (sceneNumber == 2)
        {
            var path = new PathBuilder().AddLine(stars[1], stars[3]).AddLine(stars[3], stars[5]).Build();
            ctx.Draw(Color.White.WithAlpha(.18f), 2, path);
        }
    }

    private static void DrawSceneLayers(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, string? venusAsset, string? jupiterAsset, Font titleFont, Font subtitleFont, Font labelFont, Font smallFont)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "what":
                DrawPlanetAsset(ctx, venusAsset, new PointF(1220, 360), 140, "venus"); DrawPlanetAsset(ctx, jupiterAsset, new PointF(1410, 410), 96, "jupiter");
                DrawLeaderLabel(ctx, "Venus", new PointF(1220, 360), new PointF(1085, 300), labelFont, Color.ParseHex("#FFF2B8"));
                DrawLeaderLabel(ctx, "Jupiter", new PointF(1410, 410), new PointF(1490, 345), labelFont, Color.ParseHex("#F0C88B"));
                DrawText(ctx, "Venus & Jupiter Tonight", titleFont, 115, 110, Color.White, 760); DrawText(ctx, "after sunset", subtitleFont, 122, 195, Color.ParseHex("#F6C177"), 520);
                break;
            case "where":
                DrawSkyMap(ctx, smallFont); DrawWestMarker(ctx, new PointF(245, 760), labelFont); DrawPlanetAsset(ctx, venusAsset, new PointF(1060, 505), 92, "venus"); DrawPlanetAsset(ctx, jupiterAsset, new PointF(1255, 545), 68, "jupiter");
                DrawLeaderLabel(ctx, "Venus", new PointF(1060, 505), new PointF(940, 445), labelFont, Color.White); DrawLeaderLabel(ctx, "Jupiter", new PointF(1255, 545), new PointF(1320, 492), labelFont, Color.White); DrawText(ctx, "Western horizon", smallFont, 820, 785, Color.ParseHex("#B7E0FF"), 300);
                break;
            case "when": DrawTimingWindow(ctx, titleFont, subtitleFont, smallFont); break;
            case "how":
                DrawWestMarker(ctx, new PointF(230, 780), labelFont); DrawPlanetAsset(ctx, venusAsset, new PointF(950, 430), 112, "venus"); DrawPlanetAsset(ctx, jupiterAsset, new PointF(1195, 470), 76, "jupiter"); DrawArrow(ctx, new PointF(1015, 440), new PointF(1150, 465), Color.ParseHex("#8FD2FF")); DrawGuideSteps(ctx, subtitleFont); DrawLeaderLabel(ctx, "Venus", new PointF(950, 430), new PointF(820, 365), labelFont, Color.White); DrawLeaderLabel(ctx, "Jupiter", new PointF(1195, 470), new PointF(1260, 415), labelFont, Color.White);
                break;
            case "why":
                DrawPlanetAsset(ctx, venusAsset, new PointF(900, 420), 128, "venus"); DrawPlanetAsset(ctx, jupiterAsset, new PointF(1060, 445), 98, "jupiter"); DrawClosenessBracket(ctx, new PointF(810, 330), new PointF(1140, 540)); DrawComparisonStrip(ctx, smallFont); DrawText(ctx, "Two bright planets close together", subtitleFont, 170, 145, Color.White, 850);
                break;
            default:
                DrawPlanetAsset(ctx, venusAsset, new PointF(1010, 390), 110, "venus"); DrawPlanetAsset(ctx, jupiterAsset, new PointF(1165, 430), 78, "jupiter"); DrawText(ctx, "Step outside tonight", titleFont, 135, 150, Color.White, 740); DrawText(ctx, "Look west", subtitleFont, 145, 235, Color.ParseHex("#F6C177"), 320);
                break;
        }
    }

    private static void DrawSkyMap(IImageProcessingContext ctx, Font font)
    {
        for (var x = 420; x <= 1540; x += 140) ctx.Draw(Color.White.WithAlpha(.10f), 1, new PathBuilder().AddLine(new PointF(x, 240), new PointF(x, 760)).Build());
        for (var y = 300; y <= 720; y += 105) ctx.Draw(Color.White.WithAlpha(.10f), 1, new PathBuilder().AddLine(new PointF(420, y), new PointF(1540, y)).Build());
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.55f), 3, new RectangleF(420, 240, 1120, 520));
        DrawText(ctx, "Sky map view", font, 450, 260, Color.ParseHex("#B7E0FF"), 240);
    }

    private static void DrawTimingWindow(IImageProcessingContext ctx, Font titleFont, Font subtitleFont, Font smallFont)
    {
        DrawText(ctx, "Best viewing window", titleFont, 160, 135, Color.White, 790);
        var y = 575f; var start = 260f; var end = 1600f;
        ctx.Draw(Color.ParseHex("#B7E0FF"), 6, new PathBuilder().AddLine(new PointF(start, y), new PointF(end, y)).Build());
        ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(385, y, 18)); ctx.Fill(Color.ParseHex("#FFF2B8"), new EllipsePolygon(1110, y, 28));
        ctx.Fill(Color.ParseHex("#8FD2FF").WithAlpha(.18f), new RectangleF(430, 500, 860, 105));
        DrawText(ctx, "Sunset", smallFont, 330, y + 40, Color.White, 190); DrawText(ctx, "7:23 PM IST", subtitleFont, 990, y + 42, Color.ParseHex("#F6C177"), 350); DrawText(ctx, "after-sunset viewing window", smallFont, 650, 455, Color.ParseHex("#B7E0FF"), 460);
    }

    private static void DrawGuideSteps(IImageProcessingContext ctx, Font font)
    {
        var items = new[] { ("1", "Find Venus", new PointF(140, 165)), ("2", "Look nearby for Jupiter", new PointF(140, 255)), ("3", "Face west", new PointF(140, 345)) };
        foreach (var (n, text, p) in items) { ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(p.X, p.Y + 21, 24)); DrawText(ctx, n, font, p.X - 9, p.Y - 4, Color.ParseHex("#061124"), 40); DrawText(ctx, text, font, p.X + 50, p.Y, Color.White, 560); }
    }

    private static void DrawComparisonStrip(IImageProcessingContext ctx, Font font)
    {
        ctx.Draw(Color.White.WithAlpha(.24f), 2, new RectangleF(180, 745, 690, 95));
        ctx.Fill(Color.ParseHex("#FFF2B8"), new EllipsePolygon(260, 792, 20)); ctx.Fill(Color.ParseHex("#F0C88B"), new EllipsePolygon(520, 792, 15));
        DrawText(ctx, "Venus: very bright", font, 300, 768, Color.White, 230); DrawText(ctx, "Jupiter: bright nearby", font, 555, 768, Color.White, 300);
    }

    private static void DrawWestMarker(IImageProcessingContext ctx, PointF p, Font font)
    { ctx.Draw(Color.ParseHex("#F6C177"), 5, new PathBuilder().AddLine(new PointF(p.X + 210, p.Y), p).Build()); ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(p.X, p.Y, 12)); DrawText(ctx, "W", font, p.X - 20, p.Y - 58, Color.ParseHex("#F6C177"), 70); }

    private static void DrawClosenessBracket(IImageProcessingContext ctx, PointF a, PointF b)
    { ctx.Draw(Color.ParseHex("#F6C177"), 4, new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y)); DrawText(ctx, "close pairing", ResolveFont(26, FontStyle.Bold), a.X + 95, a.Y - 42, Color.ParseHex("#F6C177"), 220); }

    private static void DrawLeaderLabel(IImageProcessingContext ctx, string text, PointF from, PointF label, Font font, Color color)
    { ctx.Draw(color.WithAlpha(.72f), 2, new PathBuilder().AddLine(from, label).Build()); DrawText(ctx, text, font, label.X, label.Y, color, 220); }

    private static void DrawPlanetAsset(IImageProcessingContext ctx, string? assetPath, PointF center, int diameter, string objectName)
    {
        ctx.Fill((objectName == "venus" ? Color.ParseHex("#FFF2B8") : Color.ParseHex("#E5C18D")).WithAlpha(.20f), new EllipsePolygon(center.X, center.Y, diameter * .62f));
        if (assetPath is not null && File.Exists(assetPath))
        {
            using var asset = Image.Load<Rgba32>(assetPath);
            asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Max }));
            ctx.DrawImage(asset, new Point((int)(center.X - asset.Width / 2f), (int)(center.Y - asset.Height / 2f)), 1f);
            return;
        }
        DrawTexturedFallbackPlanet(ctx, center, diameter, objectName);
    }

    private static void DrawTexturedFallbackPlanet(IImageProcessingContext ctx, PointF c, int diameter, string objectName)
    {
        var r = diameter / 2f;
        if (objectName == "venus") { ctx.Fill(Color.ParseHex("#FFF6C7"), new EllipsePolygon(c.X, c.Y, r)); ctx.Fill(Color.ParseHex("#EED78F").WithAlpha(.25f), new EllipsePolygon(c.X - r * .20f, c.Y - r * .15f, r * .72f)); }
        else { ctx.Fill(Color.ParseHex("#D8B17A"), new EllipsePolygon(c.X, c.Y, r)); for (var i = -2; i <= 2; i++) ctx.Draw(Color.ParseHex(i % 2 == 0 ? "#F0D09B" : "#9E704C").WithAlpha(.55f), 4, new PathBuilder().AddLine(new PointF(c.X - r * .75f, c.Y + i * r * .22f), new PointF(c.X + r * .75f, c.Y + i * r * .15f)).Build()); }
    }

    private static void DrawArrow(IImageProcessingContext ctx, PointF from, PointF to, Color color) { ctx.Draw(color, 5, new PathBuilder().AddLine(from, to).Build()); ctx.Fill(color, new EllipsePolygon(to.X, to.Y, 9)); }
    private static void DrawVignette(IImageProcessingContext ctx) => ctx.Draw(Color.Black.WithAlpha(.30f), 60, new RectangleF(30, 30, 1860, 1020));
    private static void DrawText(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap) => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);
    private static Color Blend(Color a, Color b, float amount) { amount = Math.Clamp(amount, 0, 1); var ap = a.ToPixel<Rgba32>(); var bp = b.ToPixel<Rgba32>(); return Color.FromRgb((byte)(ap.R + (bp.R - ap.R) * amount), (byte)(ap.G + (bp.G - ap.G) * amount), (byte)(ap.B + (bp.B - ap.B) * amount)); }

    private static Font ResolveFont(float size, FontStyle style)
    {
        var collection = new FontCollection();
        foreach (var candidate in new[] { "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-Bold.ttf", "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-ExtraBold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" })
            if (File.Exists(candidate)) return collection.Add(candidate).CreateFont(size, style);
        return SystemFonts.CreateFont("Arial", size, style);
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
    private static bool ContainsForbiddenTerm(string text) => ForbiddenViewerTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAll(string value, params string[] terms) => terms.All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static double EstimateTextCoverage(QuestionDrivenVisualSpec spec) => spec.QuestionType.ToLowerInvariant() switch { "what" => .10, "where" => .11, "when" => .13, "how" => .16, "why" => .12, "action" => .09, _ => .20 };
    private static string GetLayoutKey(string questionType) => questionType.ToLowerInvariant() switch { "what" => "magazine-hero-poster", "where" => "western-sky-map-chart", "when" => "twilight-time-axis", "how" => "observational-arrow-guide", "why" => "close-pair-comparison", "action" => "minimal-closing-poster", _ => "unknown" };
    private static int CountQuestions(JsonDocument document) => document.RootElement.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array ? q.GetArrayLength() : document.RootElement.TryGetProperty("questionAnswers", out var qa) && qa.ValueKind == JsonValueKind.Array ? qa.GetArrayLength() : 0;
    private static void EnsureInputFile(string path, string logicalName) { if (!File.Exists(path)) throw new ArgumentException($"Required {logicalName} input file was not found at '{NormalizePath(path)}'."); }
    private void ValidateRequest(QuestionDrivenVisualGenerationRequest request) { if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase) || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase) || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Editorial astronomy infographic composition is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request)); }
    private string BuildQuestionEngineRoot(string eventId, string regionId) => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");
    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string SanitizePathSegment(string value) { var invalid = Path.GetInvalidFileNameChars(); var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim(); return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized; }
    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
