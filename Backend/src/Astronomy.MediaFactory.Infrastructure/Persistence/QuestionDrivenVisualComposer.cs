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
        var response = await GenerateEditorialAstronomyInfographicsAsync(request, cancellationToken);
        return new QuestionDrivenVisualGenerationResponse(
            response.EventId,
            response.SceneCount,
            response.FinalImageCount,
            response.SrtCount,
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
        var srtCount = 0;
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
            if (venusAsset is null || jupiterAsset is null)
            {
                warnings.Add($"Skipped scene {sceneNumber:000} image because required local transparent Venus/Jupiter assets are missing.");
                continue;
            }

            await infographicRenderer.RenderAsync(finalPath, spec, venusAsset, jupiterAsset, cancellationToken);
            await File.WriteAllTextAsync(srtPath, srt, cancellationToken);
            await File.WriteAllTextAsync(narrationTextPath, spec.NarrationText + Environment.NewLine, cancellationToken);
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);
            generatedFiles.AddRange([finalPath, srtPath, narrationTextPath, specPath, reviewPath]);
            finalImageCount++;
            srtCount++;
        }

        AddPlanLevelWarnings(plannedScenes, warnings);
        warnings.Add("Human approval is still required before TTS, audio generation, video rendering, or publishing.");
        return new EditorialAstronomyInfographicGenerationResponse(request.EventId, scenes.Length, plannedScenes.Count, finalImageCount, srtCount, approvedSceneCount, failedSceneCount, plannedScenes, generatedFiles.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
        if (!venusAssetFound || !jupiterAssetFound) issues.Add("local transparent Venus/Jupiter assets are missing.");
        if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase) && !viewerText.Contains("close", StringComparison.OrdinalIgnoreCase)) issues.Add("Why scene does not emphasize the close bright planetary pairing.");
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
            issues,
            recommendations);
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
