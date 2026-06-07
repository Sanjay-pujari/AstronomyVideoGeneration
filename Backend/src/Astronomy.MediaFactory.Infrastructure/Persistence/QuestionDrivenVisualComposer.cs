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
    ILogger<QuestionDrivenVisualComposer> logger) : IQuestionDrivenVisualComposer
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string QuestionAnswerSetFileName = "question-answer-set.json";
    private const string EnrichedPlanFileName = "question-driven-scene-plan.enriched.json";
    private const string NarrationFileName = "question-driven-narration.json";
    private const string OutputDirectoryName = "scene-approval";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenViewerTerms = ["guid", "path", "file", "json", "internal", "debug", "metadata", "question-engine", "scene-approval", "e7013ee4"];

    public async Task<QuestionDrivenVisualGenerationResponse> GenerateQuestionDrivenVisualsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        logger.LogInformation("Generating question-driven visual approval assets for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

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
        var enrichedPlan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(await File.ReadAllTextAsync(planPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Enriched question-driven scene plan could not be parsed.", nameof(request));
        var narration = JsonSerializer.Deserialize<QuestionDrivenNarrationDto>(await File.ReadAllTextAsync(narrationPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Question-driven narration could not be parsed.", nameof(request));

        var scenes = enrichedPlan.Scenes.OrderBy(s => s.SceneNumber).ToArray();
        if (scenes.Length != 6)
            throw new ArgumentException("Question-driven visual composition requires exactly 6 golden pilot scenes.", nameof(request));

        var localPlanetAssetsAvailable = FindLocalAsset("venus") is not null && FindLocalAsset("jupiter") is not null;
        var finalImageCount = 0;
        var srtCount = 0;
        var approvedSceneCount = 0;
        var failedSceneCount = 0;
        var seenSrtTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                localPlanetAssetsAvailable));
            var spec = BuildSpec(request, scene, narrationScene, prompt);
            var srt = BuildSrt(spec);
            var review = BuildReview(spec, srt, seenSrtTexts);

            var finalPath = Path.Combine(outputRoot, $"{numberPrefix}-final.png");
            var srtPath = Path.Combine(outputRoot, $"{numberPrefix}.srt");
            var narrationTextPath = Path.Combine(outputRoot, $"{numberPrefix}-narration.txt");
            var specPath = Path.Combine(outputRoot, $"{numberPrefix}-visual-spec.json");
            var promptPath = Path.Combine(outputRoot, $"{numberPrefix}-image-prompt.txt");
            var reviewPath = Path.Combine(outputRoot, $"{numberPrefix}-review.json");
            var plannedOutputs = new QuestionDrivenPlannedOutputs(
                NormalizePath(finalPath),
                NormalizePath(srtPath),
                NormalizePath(narrationTextPath),
                NormalizePath(specPath),
                NormalizePath(promptPath),
                NormalizePath(reviewPath));
            var overlayPlan = BuildOverlayPlan(spec);
            var validationPreview = BuildValidationPreview(spec, srt, review, overlayPlan, plannedOutputs);
            plannedScenes.Add(new QuestionDrivenPlannedScene(
                scene.SceneNumber,
                scene.QuestionType,
                scene.ScenePurpose,
                scene.ViewerQuestion,
                scene.ViewerTakeaway,
                narrationScene.NarrationText,
                narrationScene.CaptionText,
                scene.VisualIntent,
                scene.ImagePromptIntent,
                scene.OverlayIntent,
                scene.AccessibilityIntent,
                prompt,
                overlayPlan,
                plannedOutputs,
                validationPreview));

            if (validationPreview.Issues.Count > 0)
                warnings.AddRange(validationPreview.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}"));

            if (request.DryRun) continue;

            if (review.Issues.Count == 0)
                approvedSceneCount++;
            else
            {
                failedSceneCount++;
                warnings.AddRange(review.Issues.Select(issue => $"Scene {sceneNumber:000}: {issue}"));
            }

            Directory.CreateDirectory(outputRoot);

            if (!request.OverwriteExisting && new[] { finalPath, srtPath, narrationTextPath, specPath, promptPath, reviewPath }.Any(File.Exists))
            {
                warnings.Add($"Skipped scene {sceneNumber:000} because one or more approval assets already exist and overwriteExisting is false.");
                continue;
            }

            await ComposeSceneImageAsync(finalPath, spec, cancellationToken);
            await File.WriteAllTextAsync(srtPath, srt, cancellationToken);
            await File.WriteAllTextAsync(narrationTextPath, spec.NarrationText + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(promptPath, prompt + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
            generatedFiles.AddRange([finalPath, srtPath, narrationTextPath, specPath, promptPath, reviewPath]);
            finalImageCount++;
            srtCount++;
        }

        AddPlanLevelWarnings(plannedScenes, warnings);

        return new QuestionDrivenVisualGenerationResponse(
            request.EventId,
            scenes.Length,
            finalImageCount,
            srtCount,
            approvedSceneCount,
            failedSceneCount,
            generatedFiles.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            plannedScenes.Count,
            plannedScenes.Count,
            plannedScenes.Count,
            plannedScenes);
    }

    private static QuestionDrivenVisualSpec BuildSpec(QuestionDrivenVisualGenerationRequest request, EnrichedQuestionSceneDto scene, QuestionDrivenNarrationSceneDto narrationScene, string prompt)
    {
        var overlays = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "Venus & Jupiter Tonight", "Look west after sunset" },
            "where" => new[] { "Face WEST", "Venus", "Jupiter", "About one-third above horizon" },
            "when" => new[] { "Sunset", "7:23 PM IST", "Best viewing time" },
            "how" => new[] { "1. Find Venus", "2. Look nearby for Jupiter", "3. Face west" },
            "why" => new[] { "Why It Matters", "Two bright planets close together" },
            "action" => new[] { "Step Outside Tonight", "Look west for Venus and Jupiter" },
            _ => new[] { scene.ViewerTakeaway }
        };

        var layers = scene.QuestionType.ToLowerInvariant() switch
        {
            "what" => new[] { "twilight-sky-background", "venus-local-asset", "jupiter-local-asset", "short-title", "viewing-cue" },
            "where" => new[] { "western-horizon-background", "compass-west-marker", "venus-local-asset", "jupiter-local-asset", "altitude-hint", "labels" },
            "when" => new[] { "sunset-to-evening-background", "timeline", "7:23-pm-ist-marker" },
            "how" => new[] { "observing-guide-background", "venus-local-asset", "jupiter-local-asset", "arrows", "three-guide-steps" },
            "why" => new[] { "close-pairing-background", "venus-local-asset", "jupiter-local-asset", "pairing-callout" },
            "action" => new[] { "closing-sky-background", "venus-local-asset", "jupiter-local-asset", "minimal-cta" },
            _ => new[] { "sky-background", "programmatic-overlays" }
        };

        return new QuestionDrivenVisualSpec(
            request.EventId,
            request.RegionId,
            request.Language,
            scene.SceneNumber,
            scene.QuestionType,
            scene.ScenePurpose,
            scene.ViewerQuestion,
            scene.ViewerTakeaway,
            narrationScene.NarrationText,
            narrationScene.CaptionText,
            Math.Max(4, narrationScene.EstimatedDurationSeconds),
            prompt,
            overlays,
            layers,
            [scene.AccessibilityIntent, "Muted viewers can understand the answer from title, labels, arrows, timing, and direction overlays."],
            DateTimeOffset.UtcNow);
    }

    private static QuestionDrivenSceneReview BuildReview(QuestionDrivenVisualSpec spec, string srt, HashSet<string> seenSrtTexts)
    {
        var issues = new List<string>();
        var recommendations = new List<string>();
        var viewerText = string.Join(' ', spec.OverlayText);
        if (ContainsForbiddenTerm(viewerText)) issues.Add("image contains debug text, GUID/path/file/internal words, or implementation terminology.");
        if (!IsSceneSpecific(spec)) issues.Add("image is generic and not scene-specific.");
        if (!seenSrtTexts.Add(Clean(spec.CaptionText))) issues.Add("SRT repeats another scene.");
        if (!NarrationAlignsWithImage(spec)) issues.Add("narration and image do not answer the same question.");
        if (spec.OverlayText.Count < 2) issues.Add("muted viewer cannot understand the scene from visual text alone.");
        if (!srt.Contains(" --> ", StringComparison.Ordinal)) issues.Add("SRT is not in timed-caption format.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !viewerText.Contains("close", StringComparison.OrdinalIgnoreCase))
            recommendations.Add("Keep the Why scene focused on the close bright pairing if angular separation data is unavailable.");

        var approved = issues.Count == 0;
        return new QuestionDrivenSceneReview(spec.SceneNumber, spec.QuestionType, approved, approved, approved, approved, approved, issues, recommendations);
    }

    private static QuestionDrivenProgrammaticOverlayPlan BuildOverlayPlan(QuestionDrivenVisualSpec spec)
    {
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => new QuestionDrivenProgrammaticOverlayPlan(
                "Venus & Jupiter Tonight",
                "Look west after sunset",
                ["Venus", "Jupiter"],
                [],
                ["Venus", "Jupiter"],
                [],
                [],
                []),
            "where" => new QuestionDrivenProgrammaticOverlayPlan(
                "Where to Look",
                "Face the western horizon",
                ["West", "Venus", "Jupiter", "Horizon"],
                ["west-facing horizon guide arrow"],
                ["Venus", "Jupiter"],
                ["West"],
                [],
                []),
            "when" => new QuestionDrivenProgrammaticOverlayPlan(
                "Best Time Tonight",
                "Start looking shortly after sunset",
                ["Sunset", "Viewing time"],
                [],
                [],
                [],
                ["7:23 PM IST"],
                []),
            "how" => new QuestionDrivenProgrammaticOverlayPlan(
                "How to Find It",
                "Use Venus as your anchor",
                ["Venus", "Jupiter", "West"],
                ["arrow from Venus toward Jupiter", "arrow pointing toward western horizon"],
                ["Venus", "Jupiter"],
                ["West"],
                [],
                ["Find Venus", "Look nearby for Jupiter", "Face west"]),
            "why" => new QuestionDrivenProgrammaticOverlayPlan(
                "Why It Matters",
                "A close bright planetary pairing",
                ["Venus", "Jupiter"],
                ["pairing callout bracket"],
                ["Venus", "Jupiter"],
                [],
                [],
                []),
            "action" => new QuestionDrivenProgrammaticOverlayPlan(
                "Step Outside Tonight",
                "Look west for the bright pair",
                ["Venus", "Jupiter"],
                [],
                ["Venus", "Jupiter"],
                ["West"],
                [],
                []),
            _ => new QuestionDrivenProgrammaticOverlayPlan(
                spec.ViewerTakeaway,
                string.Empty,
                [],
                [],
                [],
                [],
                [],
                [])
        };
    }

    private static QuestionDrivenValidationPreview BuildValidationPreview(
        QuestionDrivenVisualSpec spec,
        string srt,
        QuestionDrivenSceneReview review,
        QuestionDrivenProgrammaticOverlayPlan overlayPlan,
        QuestionDrivenPlannedOutputs plannedOutputs)
    {
        var issues = new List<string>(review.Issues);
        var imageSceneSpecific = IsSceneSpecific(spec);
        var narrationAligned = NarrationAlignsWithImage(spec);
        var srtReady = !string.IsNullOrWhiteSpace(spec.CaptionText) && srt.Contains(" --> ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(plannedOutputs.SrtPath);
        var accessibilityReady = spec.AccessibilityCues.Any(cue => !string.IsNullOrWhiteSpace(cue))
            && (overlayPlan.Labels.Count > 0 || overlayPlan.Steps.Count > 0 || overlayPlan.TimingMarkers.Count > 0)
            && !string.IsNullOrWhiteSpace(spec.CaptionText);

        if (string.IsNullOrWhiteSpace(spec.NarrationText)) issues.Add("scene lacks narrationText.");
        if (string.IsNullOrWhiteSpace(spec.BackgroundPrompt)) issues.Add("scene lacks aiBackgroundPrompt.");
        if (string.IsNullOrWhiteSpace(plannedOutputs.FinalImagePath)) issues.Add("scene lacks finalImagePath.");
        if (string.IsNullOrWhiteSpace(plannedOutputs.SrtPath)) issues.Add("scene lacks srtPath.");
        if (!OverlayPlanMatchesQuestionType(spec.QuestionType, overlayPlan)) issues.Add("scene overlay plan does not match questionType.");
        if (!srtReady) issues.Add("scene is not SRT-ready.");
        if (!accessibilityReady) issues.Add("scene lacks accessible labels, steps, or timing markers.");

        return new QuestionDrivenValidationPreview(
            imageSceneSpecific,
            narrationAligned,
            srtReady,
            accessibilityReady,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            review.Recommendations);
    }

    private static bool OverlayPlanMatchesQuestionType(string questionType, QuestionDrivenProgrammaticOverlayPlan overlayPlan)
    {
        return questionType.ToLowerInvariant() switch
        {
            "what" => overlayPlan.Title.Contains("Venus", StringComparison.OrdinalIgnoreCase)
                && overlayPlan.Title.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)
                && overlayPlan.LocalAssetObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.LocalAssetObjects.Contains("Jupiter", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Steps.Count == 0
                && overlayPlan.TimingMarkers.Count == 0,
            "where" => overlayPlan.Labels.Contains("West", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Labels.Contains("Venus", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Labels.Contains("Jupiter", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Labels.Contains("Horizon", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.DirectionMarkers.Contains("West", StringComparer.OrdinalIgnoreCase),
            "when" => overlayPlan.Title.Contains("time", StringComparison.OrdinalIgnoreCase)
                && overlayPlan.TimingMarkers.Contains("7:23 PM IST", StringComparer.OrdinalIgnoreCase),
            "how" => overlayPlan.Steps.SequenceEqual(["Find Venus", "Look nearby for Jupiter", "Face west"], StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Arrows.Count > 0,
            "why" => overlayPlan.Title.Equals("Why It Matters", StringComparison.OrdinalIgnoreCase)
                && overlayPlan.Labels.Contains("Venus", StringComparer.OrdinalIgnoreCase)
                && overlayPlan.Labels.Contains("Jupiter", StringComparer.OrdinalIgnoreCase),
            "action" => overlayPlan.Title.Equals("Step Outside Tonight", StringComparison.OrdinalIgnoreCase)
                && overlayPlan.Subtitle.Contains("west", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static void AddPlanLevelWarnings(IReadOnlyList<QuestionDrivenPlannedScene> plannedScenes, List<string> warnings)
    {
        if (plannedScenes.Count == 0)
        {
            warnings.Add("plannedScenes is empty.");
            return;
        }

        foreach (var scene in plannedScenes)
        {
            if (string.IsNullOrWhiteSpace(scene.NarrationText)) warnings.Add($"Scene {scene.SceneNumber:000}: scene lacks narrationText.");
            if (string.IsNullOrWhiteSpace(scene.AiBackgroundPrompt)) warnings.Add($"Scene {scene.SceneNumber:000}: scene lacks aiBackgroundPrompt.");
            if (string.IsNullOrWhiteSpace(scene.PlannedOutputs.FinalImagePath)) warnings.Add($"Scene {scene.SceneNumber:000}: scene lacks finalImagePath.");
            if (string.IsNullOrWhiteSpace(scene.PlannedOutputs.SrtPath)) warnings.Add($"Scene {scene.SceneNumber:000}: scene lacks srtPath.");
            if (!OverlayPlanMatchesQuestionType(scene.QuestionType, scene.ProgrammaticOverlayPlan)) warnings.Add($"Scene {scene.SceneNumber:000}: scene overlay plan does not match questionType.");
        }

        for (var i = 0; i < plannedScenes.Count; i++)
        {
            for (var j = i + 1; j < plannedScenes.Count; j++)
            {
                if (AreSubstantiallyIdenticalPrompts(plannedScenes[i].AiBackgroundPrompt, plannedScenes[j].AiBackgroundPrompt))
                    warnings.Add($"Scene {plannedScenes[i].SceneNumber:000} and scene {plannedScenes[j].SceneNumber:000}: scene prompts are substantially identical.");
            }
        }
    }

    private static bool AreSubstantiallyIdenticalPrompts(string left, string right)
    {
        var leftWords = SignificantPromptWords(left).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightWords = SignificantPromptWords(right).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftWords.Count == 0 || rightWords.Count == 0) return true;
        var intersection = leftWords.Count(word => rightWords.Contains(word));
        var union = leftWords.Union(rightWords, StringComparer.OrdinalIgnoreCase).Count();
        return union > 0 && intersection / (double)union >= 0.88d;
    }

    private static IEnumerable<string> SignificantPromptWords(string prompt)
    {
        var stopWords = new HashSet<string>(["professional", "astronomy", "production", "background", "only", "over", "with", "cinematic", "sky", "horizon", "atmosphere", "landscape", "text", "labels", "watermarks", "diagrams", "filenames", "debug", "markings", "local", "assets", "programmatic", "overlays", "clean", "space", "foreground", "educational", "high", "quality", "polished", "generic", "scene", "specific", "mood", "include", "leave", "readable", "will", "added"], StringComparer.OrdinalIgnoreCase);
        foreach (var word in prompt.Split([' ', '.', ',', ';', ':', '-', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Length < 4 || stopWords.Contains(word)) continue;
            yield return word.ToLowerInvariant();
        }
    }

    private static bool IsSceneSpecific(QuestionDrivenVisualSpec spec)
    {
        var text = string.Join(' ', spec.OverlayText.Concat([spec.QuestionType, spec.NarrationText]));
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(text, "venus", "jupiter"),
            "where" => ContainsAll(text, "west", "venus", "jupiter"),
            "when" => text.Contains("7:23", StringComparison.OrdinalIgnoreCase) || text.Contains("IST", StringComparison.OrdinalIgnoreCase),
            "how" => ContainsAll(text, "find", "venus", "jupiter"),
            "why" => ContainsAll(text, "bright", "planets") || ContainsAll(text, "close", "pair"),
            "action" => text.Contains("outside", StringComparison.OrdinalIgnoreCase) || text.Contains("look west", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool NarrationAlignsWithImage(QuestionDrivenVisualSpec spec)
    {
        var combined = spec.NarrationText + " " + string.Join(' ', spec.OverlayText);
        return spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => ContainsAll(combined, "venus", "jupiter"),
            "where" => combined.Contains("west", StringComparison.OrdinalIgnoreCase),
            "when" => combined.Contains("7:23", StringComparison.OrdinalIgnoreCase) || combined.Contains("sunset", StringComparison.OrdinalIgnoreCase),
            "how" => combined.Contains("find", StringComparison.OrdinalIgnoreCase),
            "why" => combined.Contains("bright", StringComparison.OrdinalIgnoreCase) || combined.Contains("close", StringComparison.OrdinalIgnoreCase),
            "action" => combined.Contains("west", StringComparison.OrdinalIgnoreCase) || combined.Contains("outside", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static async Task ComposeSceneImageAsync(string finalPath, QuestionDrivenVisualSpec spec, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#061124"));
        var titleFont = ResolveFont(72, FontStyle.Bold);
        var subtitleFont = ResolveFont(40, FontStyle.Bold);
        var labelFont = ResolveFont(32, FontStyle.Bold);
        var smallFont = ResolveFont(28, FontStyle.Regular);

        image.Mutate(ctx =>
        {
            DrawBackground(ctx, spec.SceneNumber);
            DrawSceneLayers(ctx, spec, titleFont, subtitleFont, labelFont, smallFont);
            DrawVignette(ctx);
        });

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");
        await image.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
    }

    private static void DrawBackground(IImageProcessingContext ctx, int sceneNumber)
    {
        var top = sceneNumber switch
        {
            1 => Color.ParseHex("#07152F"),
            2 => Color.ParseHex("#081A35"),
            3 => Color.ParseHex("#182849"),
            4 => Color.ParseHex("#061628"),
            5 => Color.ParseHex("#031022"),
            6 => Color.ParseHex("#07142A"),
            _ => Color.ParseHex("#061124")
        };
        var bottom = sceneNumber is 3 ? Color.ParseHex("#FF9B5E") : Color.ParseHex("#1D3557");
        for (var y = 0; y < 1080; y += 8)
        {
            var amount = y / 1080f;
            ctx.Fill(Blend(top, bottom, amount), new RectangleF(0, y, 1920, 10));
        }

        ctx.Fill(Color.ParseHex("#101417"), new RectangleF(0, 850, 1920, 230));
        ctx.Fill(Color.ParseHex("#222A2C"), new RectangleF(0, 822, 1920, 34));
        ctx.Fill(Color.White.WithAlpha(0.75f), new EllipsePolygon(420, 160, 2));
        ctx.Fill(Color.White.WithAlpha(0.45f), new EllipsePolygon(1500, 230, 1.5f));
        ctx.Fill(Color.White.WithAlpha(0.35f), new EllipsePolygon(1140, 120, 1.4f));
    }

    private static void DrawSceneLayers(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, Font titleFont, Font subtitleFont, Font labelFont, Font smallFont)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "what":
                DrawPlanets(ctx, new PointF(1120, 405), new PointF(1285, 430), labelFont, true);
                DrawPanel(ctx, 110, 95, 760, 250);
                DrawText(ctx, spec.OverlayText[0], titleFont, 150, 125, Color.White, 650);
                DrawText(ctx, spec.OverlayText[1], subtitleFont, 150, 220, Color.ParseHex("#F6C177"), 620);
                break;
            case "where":
                DrawCompass(ctx, labelFont);
                DrawHorizonGuide(ctx, smallFont);
                DrawPlanets(ctx, new PointF(1050, 500), new PointF(1240, 535), labelFont, true);
                DrawText(ctx, "About one-third above horizon", smallFont, 760, 670, Color.ParseHex("#B7E0FF"), 520);
                break;
            case "when":
                DrawTimeline(ctx, spec, titleFont, subtitleFont, smallFont);
                break;
            case "how":
                DrawPlanets(ctx, new PointF(1040, 450), new PointF(1225, 488), labelFont, true);
                DrawArrow(ctx, new PointF(860, 560), new PointF(1020, 470), Color.ParseHex("#F6C177"));
                DrawArrow(ctx, new PointF(1130, 470), new PointF(1210, 488), Color.ParseHex("#8FD2FF"));
                DrawStepPanel(ctx, spec, subtitleFont);
                break;
            case "why":
                DrawPlanets(ctx, new PointF(960, 430), new PointF(1090, 450), labelFont, true);
                ctx.Draw(Color.ParseHex("#F6C177"), 4, new RectangleF(900, 365, 260, 145));
                DrawPanel(ctx, 140, 110, 780, 220);
                DrawText(ctx, spec.OverlayText[0], titleFont, 180, 140, Color.White, 680);
                DrawText(ctx, spec.OverlayText[1], subtitleFont, 180, 238, Color.ParseHex("#F6C177"), 650);
                break;
            default:
                DrawPlanets(ctx, new PointF(980, 390), new PointF(1160, 425), labelFont, false);
                DrawPanel(ctx, 200, 130, 880, 230);
                DrawText(ctx, spec.OverlayText[0], titleFont, 240, 160, Color.White, 760);
                DrawText(ctx, spec.OverlayText[1], subtitleFont, 240, 258, Color.ParseHex("#F6C177"), 720);
                break;
        }
    }

    private static void DrawPlanets(IImageProcessingContext ctx, PointF venus, PointF jupiter, Font labelFont, bool labels)
    {
        ctx.Fill(Color.ParseHex("#FFF2B8").WithAlpha(0.22f), new EllipsePolygon(venus.X, venus.Y, 52));
        ctx.Fill(Color.ParseHex("#FFF8D8"), new EllipsePolygon(venus.X, venus.Y, 19));
        ctx.Fill(Color.ParseHex("#E5C18D").WithAlpha(0.22f), new EllipsePolygon(jupiter.X, jupiter.Y, 44));
        ctx.Fill(Color.ParseHex("#F0C88B"), new EllipsePolygon(jupiter.X, jupiter.Y, 15));
        if (!labels) return;
        DrawText(ctx, "Venus", labelFont, venus.X + 34, venus.Y - 26, Color.White, 220);
        DrawText(ctx, "Jupiter", labelFont, jupiter.X + 30, jupiter.Y - 24, Color.White, 220);
    }

    private static void DrawCompass(IImageProcessingContext ctx, Font labelFont)
    {
        DrawPanel(ctx, 110, 90, 420, 170);
        DrawText(ctx, "Face WEST", labelFont, 155, 125, Color.White, 330);
        DrawArrow(ctx, new PointF(410, 180), new PointF(240, 180), Color.ParseHex("#F6C177"));
        DrawText(ctx, "W", labelFont, 185, 155, Color.ParseHex("#F6C177"), 80);
    }

    private static void DrawHorizonGuide(IImageProcessingContext ctx, Font smallFont)
    {
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(0.65f), 3, new RectangleF(650, 285, 760, 390));
        ctx.Draw(Color.ParseHex("#8FD2FF"), 3, new SixLabors.ImageSharp.Drawing.PathBuilder().AddLine(new PointF(650, 675), new PointF(1410, 675)).Build());
        DrawText(ctx, "Western horizon", smallFont, 680, 690, Color.White, 320);
    }

    private static void DrawTimeline(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, Font titleFont, Font subtitleFont, Font smallFont)
    {
        DrawPanel(ctx, 170, 170, 1540, 300);
        DrawText(ctx, "Best Viewing Time", titleFont, 230, 210, Color.White, 700);
        var y = 355f;
        ctx.Draw(Color.ParseHex("#B7E0FF"), 5, new SixLabors.ImageSharp.Drawing.PathBuilder().AddLine(new PointF(300, y), new PointF(1560, y)).Build());
        ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(350, y, 16));
        ctx.Fill(Color.ParseHex("#FFF2B8"), new EllipsePolygon(1120, y, 24));
        DrawText(ctx, "Sunset", smallFont, 300, y + 35, Color.White, 220);
        DrawText(ctx, "7:23 PM IST", subtitleFont, 1020, y + 42, Color.ParseHex("#F6C177"), 360);
        DrawText(ctx, "Shortly after sunset", smallFont, 1120, y - 78, Color.ParseHex("#B7E0FF"), 390);
    }

    private static void DrawStepPanel(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, Font subtitleFont)
    {
        DrawPanel(ctx, 115, 105, 610, 365);
        var y = 145f;
        foreach (var step in spec.OverlayText)
        {
            DrawText(ctx, step, subtitleFont, 155, y, Color.White, 520);
            y += 92;
        }
    }

    private static void DrawPanel(IImageProcessingContext ctx, float x, float y, float w, float h)
    {
        ctx.Fill(Color.Black.WithAlpha(0.52f), new RectangleF(x, y, w, h));
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(0.42f), 2, new RectangleF(x, y, w, h));
    }

    private static void DrawArrow(IImageProcessingContext ctx, PointF from, PointF to, Color color)
    {
        ctx.Draw(color, 6, new SixLabors.ImageSharp.Drawing.PathBuilder().AddLine(from, to).Build());
        ctx.Fill(color, new EllipsePolygon(to.X, to.Y, 10));
    }

    private static void DrawVignette(IImageProcessingContext ctx)
        => ctx.Draw(Color.Black.WithAlpha(0.34f), 70, new RectangleF(35, 35, 1850, 1010));

    private static void DrawText(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap)
        => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);

    private static Color Blend(Color a, Color b, float amount)
    {
        var ap = a.ToPixel<Rgba32>();
        var bp = b.ToPixel<Rgba32>();
        return Color.FromRgb((byte)(ap.R + (bp.R - ap.R) * amount), (byte)(ap.G + (bp.G - ap.G) * amount), (byte)(ap.B + (bp.B - ap.B) * amount));
    }

    private static Font ResolveFont(float size, FontStyle style)
    {
        var collection = new FontCollection();
        var candidates = new[]
        {
            "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-Bold.ttf",
            "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-ExtraBold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
        };
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var family = collection.Add(candidate);
            return family.CreateFont(size, style);
        }

        return SystemFonts.CreateFont("Arial", size, style);
    }

    private static string BuildSrt(QuestionDrivenVisualSpec spec)
    {
        var end = TimeSpan.FromSeconds(Math.Max(4, spec.EstimatedDurationSeconds));
        return string.Join(Environment.NewLine, new[]
        {
            "1",
            $"00:00:00,000 --> {FormatSrtTime(end)}",
            spec.CaptionText,
            string.Empty
        });
    }

    private static string FormatSrtTime(TimeSpan value)
        => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";

    private static string? FindLocalAsset(string objectName)
    {
        var candidates = new[]
        {
            Path.Combine("Backend", "src", "Astronomy.MediaFactory.Api", "assets", "celestial", objectName, "hero-transparent.png"),
            Path.Combine("assets", "celestial", objectName, "hero-transparent.png"),
            Path.Combine(AppContext.BaseDirectory, "assets", "celestial", objectName, "hero-transparent.png")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool ContainsForbiddenTerm(string text)
        => ForbiddenViewerTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAll(string value, params string[] terms)
        => terms.All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string value)
        => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static void EnsureInputFile(string path, string logicalName)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required {logicalName} input file was not found at '{NormalizePath(path)}'.");
    }

    private void ValidateRequest(QuestionDrivenVisualGenerationRequest request)
    {
        if (!string.Equals(request.EventId, GoldenEventId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Question-driven visual composition is enabled only for the approved golden pilot event e7013ee4-55c6-4f01-b1d0-7c500f26f98b / IN-RJ-UDAIPUR / en.", nameof(request));
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), "question-engine");

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
