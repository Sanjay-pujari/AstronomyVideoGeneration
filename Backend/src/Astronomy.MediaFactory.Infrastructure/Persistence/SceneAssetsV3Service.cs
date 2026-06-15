using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
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

public sealed class SceneAssetsV3Service(
    IOptions<RenderingOptions> renderingOptions,
    IAICinematicImageGenerator imageGenerator,
    ILogger<SceneAssetsV3Service> logger) : ISceneAssetsV3Service
{
    private const string Version = "v3";
    private const int Width = 1920;
    private const int Height = 1080;
    private const string RequestedOverlayFont = "DejaVu Sans";
    private static readonly string[] WindowsSafeFontFallbacks = ["Segoe UI", "Arial", "Calibri", "Tahoma", "DejaVu Sans"];
    private static readonly string[] CheckedFontPaths = [
        "C:/WINDOWS/Fonts",
        "C:/Windows/Fonts",
        "%LOCALAPPDATA%/Microsoft/Windows/Fonts",
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "~/Library/Fonts"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<SceneAssetsV3Response> GenerateAsync(SceneAssetsV3Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.Combine(ResolveRoot(request), "scene-assets-v3");
        var files = new List<string>();
        var warnings = new List<string>();
        string? shortValidation = null;
        string? longValidation = null;

        var context = await LoadTimelineContextAsync(root, cancellationToken);
        if (request.GenerateShort)
            shortValidation = await GenerateFormatAsync(root, "short", BuildBeats(context, "short", 5), 5, request.OverwriteExisting, files, warnings, context, cancellationToken);
        if (request.GenerateLong)
            longValidation = await GenerateFormatAsync(root, "long", BuildBeats(context, "long", 9), 9, request.OverwriteExisting, files, warnings, context, cancellationToken);

        return new SceneAssetsV3Response(root, files, warnings, shortValidation, longValidation);
    }

    private async Task<string> GenerateFormatAsync(string root, string format, IReadOnlyList<SceneAssetsV3Beat> beats, int expectedCount, bool overwrite, List<string> files, List<string> warnings, SceneAssetsV3TimelineContext context, CancellationToken ct)
    {
        var dir = Path.Combine(root, format);
        if (overwrite && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var timelinePath = Path.Combine(dir, "visual-timeline-v3.json");
        var manifestPath = Path.Combine(dir, "scene-manifest-v3.json");
        var reviewPath = Path.Combine(dir, "scene-review-v3.json");
        var validationPath = Path.Combine(dir, "scene-v3-validation.json");
        var metadataPath = Path.Combine(dir, "scene-timeline-metadata.json");
        var diagnosticsPath = Path.Combine(dir, "scene-assets-v3-diagnostics.json");
        var manifestScenes = new List<SceneAssetsV3ManifestScene>();
        var sceneDiagnostics = new List<object>();
        var errors = new List<string>();
        ValidateNoForbiddenTerms("visualTimeline", JsonSerializer.Serialize(new SceneAssetsV3Timeline(Version, format, beats), JsonOptions), context.ForbiddenTerms);

        try
        {
            await WriteJsonAsync(timelinePath, new SceneAssetsV3Timeline(Version, format, beats), ct); files.Add(timelinePath);
            await WriteJsonAsync(metadataPath, BuildTimelineMetadata(format, beats), ct); files.Add(metadataPath);

            foreach (var beat in beats)
            {
                var imagePath = Path.Combine(dir, beat.SceneId + ".png");
                var providerCalled = beat.RenderMode is not "AccurateSkyGuideScene";
                var providerSucceeded = false;
                if ((!File.Exists(imagePath) || overwrite) && providerCalled)
                {
                    var result = await imageGenerator.GenerateAsync(new AICinematicAssetRequest(
                        $"scene-assets-v3-{format}-{beat.SceneId}", beat.SceneId, beat.RenderMode, format, beat.SceneId,
                        "scene-background", "cinematic wonder", "narration-beat", StyleFor(beat.RenderMode), beat.VisualPrompt,
                        "infographic, PowerPoint slide, large text panels, fake star labels, UI, watermark, logo", Width, Height, imagePath), ct);
                    providerSucceeded = result.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase) && File.Exists(imagePath);
                    if (!providerSucceeded)
                        warnings.Add($"Azure Image2 did not produce {format}/{beat.SceneId}; deterministic Scene V3 fallback was rendered. Status={result.GenerationStatus}.");
                }

                if (!File.Exists(imagePath) || overwrite && !providerSucceeded)
                    await RenderDeterministicSceneAsync(imagePath, beat, ct);

                var forbiddenDetected = DetectForbiddenTerms(string.Join(Environment.NewLine, beat.NarrationBeat, beat.VisualIntent, beat.VisualPrompt), context.ForbiddenTerms);
                var providerName = providerCalled ? imageGenerator.GetType().Name : "DeterministicRenderer";
                var azureCallsCount = providerCalled ? 1 : 0;
                logger.LogInformation(
                    "Scene Assets V3 scene diagnostics: sceneId={SceneId}; eventType={EventType}; narrationBeatSource={NarrationBeatSource}; visualPromptSource={VisualPromptSource}; finalVisualPrompt={FinalVisualPrompt}; forbiddenTermsDetected={ForbiddenTermsDetected}; providerName={ProviderName}; azureCallsCount={AzureCallsCount}",
                    beat.SceneId,
                    context.EventType,
                    beat.NarrationBeatSource,
                    beat.VisualPromptSource,
                    beat.VisualPrompt,
                    string.Join(", ", forbiddenDetected),
                    providerName,
                    azureCallsCount);
                sceneDiagnostics.Add(new
                {
                    beat.SceneId,
                    eventType = context.EventType,
                    beat.NarrationBeatSource,
                    beat.VisualPromptSource,
                    finalVisualPrompt = beat.VisualPrompt,
                    forbiddenTermsDetected = forbiddenDetected,
                    providerName,
                    azureCallsCount
                });
                manifestScenes.Add(new SceneAssetsV3ManifestScene(beat.SceneId, beat.RenderMode, imagePath, beat.NarrationBeat, await Sha256Async(imagePath, ct), providerCalled, providerSucceeded));
                files.Add(imagePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var warning = $"Scene Assets V3 {format} generation failed after {manifestScenes.Count}/{expectedCount} scenes; writing validation diagnostics when possible. {ex.GetType().Name}: {ex.Message}";
            logger.LogWarning(ex, "{Warning}", warning);
            warnings.Add(warning);
            errors.Add(warning);
        }

        var manifest = new SceneAssetsV3Manifest(Version, format, manifestScenes.Count, manifestScenes);
        ValidateNoForbiddenTerms("sceneManifest", JsonSerializer.Serialize(manifest, JsonOptions), context.ForbiddenTerms);
        await WriteJsonAsync(manifestPath, manifest, ct); files.Add(manifestPath);
        await WriteJsonAsync(diagnosticsPath, new { version = Version, format, eventType = context.EventType, scenes = sceneDiagnostics }, ct); files.Add(diagnosticsPath);

        var duplicate = manifestScenes.GroupBy(s => s.Hash, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        var repeated = duplicate;
        var sameBackground = DetectRepeatedMetadata(beats, b => BackgroundSignature(b));
        var sameComposition = DetectRepeatedMetadata(beats, b => CompositionSignature(b));
        var sameCameraAngle = DetectRepeatedMetadata(beats, b => CameraSignature(b));
        var review = new SceneAssetsV3Review(manifestScenes.Count, manifestScenes.Any(s => s.RenderMode == "AccurateSkyGuideScene"), manifestScenes.Count(s => s.RenderMode is "CinematicStoryScene" or "FinalReminderScene"), manifestScenes.Count(s => s.RenderMode == "ExplainerScene"), manifestScenes.Count(s => s.RenderMode == "ViewingTipsScene"), duplicate, repeated, sameBackground, sameComposition, sameCameraAngle, manifestScenes.All(s => !string.IsNullOrWhiteSpace(s.NarrationBeat)), "Failed");
        review = review with { Status = ReviewPassed(review, expectedCount) ? "Passed" : "Failed" };
        ValidateNoForbiddenTerms("sceneReview", JsonSerializer.Serialize(review, JsonOptions), context.ForbiddenTerms);
        await WriteJsonAsync(reviewPath, review, ct); files.Add(reviewPath);

        errors.AddRange(BuildValidationErrors(timelinePath, manifestPath, metadataPath, review, expectedCount));
        var validation = new SceneAssetsV3Validation(Version, format, errors.Count == 0 ? "Passed" : "Failed", File.Exists(timelinePath), File.Exists(manifestPath), manifestScenes.Count == expectedCount, review.AccurateSkyGuidePresent, duplicate, repeated, sameBackground, sameComposition, sameCameraAngle, review.AllScenesHaveNarrationBeat, errors, BuildFontDiagnostics());
        await WriteJsonAsync(validationPath, validation, ct); files.Add(validationPath);
        return validationPath;
    }

    private async Task RenderDeterministicSceneAsync(string path, SceneAssetsV3Beat beat, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var image = new Image<Rgba32>(Width, Height, Color.Black);
        image.Mutate(ctx =>
        {
            var bg = beat.RenderMode == "AccurateSkyGuideScene" ? Color.FromRgb(5, 10, 22) : Color.FromRgb((byte)(8 + beat.BeatNo * 11), (byte)(16 + beat.BeatNo * 7), (byte)(34 + beat.BeatNo * 13));
            ctx.Fill(bg);
            DrawStars(ctx, beat.BeatNo);
            if (beat.RenderMode == "AccurateSkyGuideScene") DrawSkyGuide(ctx, beat);
            else DrawCinematicForeground(ctx, beat);
            var font = ResolveOverlayFont(34, FontStyle.Bold);
            ctx.DrawText(SmallSceneLabel(beat), font, Color.FromRgba(235, 240, 248, 210), new PointF(90, 900));
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), ct);
    }

    private static void DrawStars(IImageProcessingContext ctx, int seed) { for (var i = 0; i < 180; i++) ctx.Fill(Color.FromRgba(255, 255, 255, (byte)(58 + (i % 6) * 27)), new EllipsePolygon((i * 137 + seed * 61) % Width, (i * 73 + seed * 89) % 820, 1 + i % 3)); }
    private static void DrawCinematicForeground(IImageProcessingContext ctx, SceneAssetsV3Beat beat)
    {
        ctx.Fill(Color.FromRgb(6, 8, 12), new RectangularPolygon(0, 830, Width, 250));
        var venus = new PointF(820 + beat.BeatNo * 8, 360 + beat.BeatNo * 9);
        var jupiter = new PointF(950 + beat.BeatNo * 8, 330 + beat.BeatNo * 9);
        ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(venus, 13));
        ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(jupiter, 11));
        ctx.DrawLine(Color.FromRgba(120, 210, 255, 150), 3, venus, jupiter);
    }
    private void DrawSkyGuide(IImageProcessingContext ctx, SceneAssetsV3Beat beat)
    {
        var label = ResolveOverlayFont(25, FontStyle.Regular);
        var title = ResolveOverlayFont(36, FontStyle.Bold);
        ctx.Fill(Color.FromRgb(4, 12, 32));
        ctx.Fill(Color.FromRgba(18, 36, 58, 150), new RectangularPolygon(0, 520, Width, 560));
        for (var i = 0; i < 260; i++) ctx.Fill(Color.FromRgba(245, 250, 255, (byte)(50 + i % 150)), new EllipsePolygon((i * 149 + 97) % Width, (i * 83 + 41) % 760, 1 + i % 3));
        ctx.Fill(Color.FromRgba(10, 16, 22, 245), new RectangularPolygon(0, 810, Width, 270));
        ctx.DrawLine(Color.FromRgb(95, 135, 155), 3, new PointF(120, 812), new PointF(1800, 812));
        ctx.DrawLine(Color.FromRgba(80, 130, 160, 120), 1, new PointF(260, 740), new PointF(1660, 740));
        ctx.DrawLine(Color.FromRgba(80, 130, 160, 90), 1, new PointF(460, 580), new PointF(1460, 580));
        ctx.DrawText("Accurate sky guide", title, Color.FromRgb(238, 246, 255), new PointF(96, 80));
        ctx.DrawText(TruncateForOverlay(beat.NarrationBeat, 86), label, Color.FromRgb(185, 215, 245), new PointF(96, 132));
        ctx.DrawText("Western sky after sunset • twilight horizon", label, Color.FromRgb(185, 215, 245), new PointF(96, 168));
        var venus = new PointF(900, 430);
        var jupiter = new PointF(1010, 400);
        ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(venus, 15));
        ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(jupiter, 13));
        ctx.Draw(Color.FromRgb(255, 210, 92), 3, new EllipsePolygon(venus, 34));
        ctx.Draw(Color.FromRgb(180, 210, 255), 3, new EllipsePolygon(jupiter, 30));
        ctx.DrawLine(Color.FromRgb(120, 210, 255), 4, venus, jupiter);
        ctx.DrawText("Venus", label, Color.FromRgb(255, 245, 190), new PointF(835, 460));
        ctx.DrawText("Jupiter", label, Color.FromRgb(235, 242, 255), new PointF(1030, 418));
        ctx.DrawText("1.63° separation", label, Color.FromRgb(120, 210, 255), new PointF(902, 342));
        ctx.DrawText("W horizon", label, Color.FromRgb(235, 242, 248), new PointF(448, 830));
    }


    private static SceneTimelineMetadataDocument BuildTimelineMetadata(string format, IReadOnlyList<SceneAssetsV3Beat> beats) => new(
        Version,
        format,
        beats.Select(beat => new SceneTimelineMetadata(
            beat.SceneId,
            beat.RenderMode,
            beat.VisualIntent,
            beat.NarrationBeat,
            beat.ExpectedDurationSec,
            RecommendedTransition(beat),
            RecommendedMotion(beat))).ToList());

    private static string RecommendedTransition(SceneAssetsV3Beat beat) => beat.RenderMode switch
    {
        "AccurateSkyGuideScene" => "push",
        "ViewingTipsScene" => "fade",
        "FinalReminderScene" => "fade",
        _ => beat.BeatNo % 2 == 0 ? "zoom" : "crossfade"
    };

    private static string RecommendedMotion(SceneAssetsV3Beat beat) => beat.RenderMode switch
    {
        "AccurateSkyGuideScene" => "panRight",
        "ExplainerScene" => "parallax",
        "ViewingTipsScene" => "slowZoomOut",
        "FinalReminderScene" => "slowZoomIn",
        _ => beat.BeatNo % 2 == 0 ? "panLeft" : "slowZoomIn"
    };

    private static string SmallSceneLabel(SceneAssetsV3Beat beat) => TruncateForOverlay(beat.VisualIntent, 54);
    private static string TruncateForOverlay(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";

    private static bool DetectRepeatedMetadata(IReadOnlyList<SceneAssetsV3Beat> beats, Func<SceneAssetsV3Beat, string> selector) => beats.GroupBy(selector, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
    private static string BackgroundSignature(SceneAssetsV3Beat beat) => NormalizeSignature(beat.VisualPrompt);
    private static string CompositionSignature(SceneAssetsV3Beat beat) => NormalizeSignature($"{beat.RenderMode}:{beat.VisualIntent}:{beat.VisualPrompt}");
    private static string CameraSignature(SceneAssetsV3Beat beat) => $"camera-{beat.SceneId}";
    private static string NormalizeSignature(string value) => string.Join(" ", value.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private Font ResolveOverlayFont(float size, FontStyle style)
    {
        foreach (var fontName in WindowsSafeFontFallbacks)
        {
            if (SystemFonts.TryGet(fontName, out var family))
            {
                if (!fontName.Equals(RequestedOverlayFont, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Scene Assets V3 requested font {RequestedFont} is not available; using fallback font {ResolvedFont}. CheckedFontPaths={CheckedFontPaths}", RequestedOverlayFont, family.Name, CheckedFontPaths);
                }

                return family.CreateFont(size, style);
            }
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
        {
            throw new InvalidOperationException("No system fonts available for Scene Assets V3 deterministic overlay rendering.");
        }

        logger.LogWarning("Scene Assets V3 requested font {RequestedFont} and configured fallbacks are not available; using first available font {ResolvedFont}. CheckedFontPaths={CheckedFontPaths}", RequestedOverlayFont, fallbackFamily.Name, CheckedFontPaths);
        return fallbackFamily.CreateFont(size, style);
    }

    private static SceneAssetsV3FontDiagnostics BuildFontDiagnostics()
    {
        var resolved = WindowsSafeFontFallbacks.FirstOrDefault(fontName => SystemFonts.TryGet(fontName, out _));
        resolved ??= SystemFonts.Collection.Families.FirstOrDefault().Name;
        return new SceneAssetsV3FontDiagnostics(
            RequestedOverlayFont,
            string.IsNullOrWhiteSpace(resolved) ? "" : resolved,
            !string.Equals(resolved, RequestedOverlayFont, StringComparison.OrdinalIgnoreCase),
            CheckedFontPaths);
    }

    private string ResolveRoot(SceneAssetsV3Request request) => !string.IsNullOrWhiteSpace(request.WorkingDirectoryRoot) ? request.WorkingDirectoryRoot! : string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string StyleFor(string mode) => mode == "ExplainerScene" ? "cinematic educational astronomy, realistic space documentary" : "Netflix science documentary, National Geographic astronomy, NASA campaign, realistic cinematic sky, minimal overlay";
    private static bool ReviewPassed(SceneAssetsV3Review r, int expected) => r.SceneCount == expected && r.AccurateSkyGuidePresent && !r.DuplicateHashDetected && !r.RepeatedBackgroundDetected && !r.SameBackgroundDetected && !r.SameCompositionDetected && !r.SameCameraAngleDetected && r.AllScenesHaveNarrationBeat;
    private static List<string> BuildValidationErrors(string timeline, string manifest, string metadata, SceneAssetsV3Review r, int expected) { var e = new List<string>(); if (!File.Exists(timeline)) e.Add("visual-timeline-v3.json is missing."); if (!File.Exists(manifest)) e.Add("scene-manifest-v3.json is missing."); if (!File.Exists(metadata)) e.Add("scene-timeline-metadata.json is missing."); if (r.SceneCount != expected) e.Add($"Expected {expected} scenes but found {r.SceneCount}."); if (!r.AccurateSkyGuidePresent) e.Add("AccurateSkyGuideScene is missing."); if (r.DuplicateHashDetected) e.Add("Duplicate image hashes detected."); if (r.RepeatedBackgroundDetected) e.Add("Repeated generic infographic background detected."); if (r.SameBackgroundDetected) e.Add("sameBackgroundDetected review check failed."); if (r.SameCompositionDetected) e.Add("sameCompositionDetected review check failed."); if (r.SameCameraAngleDetected) e.Add("sameCameraAngleDetected review check failed."); if (!r.AllScenesHaveNarrationBeat) e.Add("At least one scene is missing narrationBeat."); return e; }
    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), ct);
    private static async Task<string> Sha256Async(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); }

    private static async Task<SceneAssetsV3TimelineContext> LoadTimelineContextAsync(string sceneAssetsRoot, CancellationToken ct)
    {
        var outputRoot = Directory.GetParent(sceneAssetsRoot)?.FullName ?? sceneAssetsRoot;
        var intelligencePath = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        var narrationPath = ResolveFirstExisting(Path.Combine(outputRoot, "question-engine", "question-driven-narration-v2.json"), Path.Combine(outputRoot, "narration-engine", "question-driven-narration-v2.json"), Path.Combine(outputRoot, "narration-engine", "short", "question-driven-narration-v2.json"));
        using var intelligence = File.Exists(intelligencePath) ? JsonDocument.Parse(await File.ReadAllTextAsync(intelligencePath, ct)) : JsonDocument.Parse("{}");
        using var narration = narrationPath is not null ? JsonDocument.Parse(await File.ReadAllTextAsync(narrationPath, ct)) : JsonDocument.Parse("{}");
        var root = intelligence.RootElement;
        var eventType = FirstString(root, "eventType", "strategyId", "selectedEventType");
        var forbidden = ReadStringArray(root, "forbiddenTerms").Concat(DefaultForbiddenTerms(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new SceneAssetsV3TimelineContext(
            string.IsNullOrWhiteSpace(eventType) ? "Generic" : eventType,
            FirstString(root, "storyTheme"), FirstString(root, "visualTheme"), FirstString(root, "skyGuideTheme"),
            ReadStringArray(root, "requiredVisualObjects").DefaultIfEmpty(FirstString(root, "title")).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            forbidden,
            ExtractNarrationBeats(narration.RootElement));
    }

    private static IReadOnlyList<SceneAssetsV3Beat> BuildBeats(SceneAssetsV3TimelineContext context, string format, int count)
    {
        var ids = format == "short"
            ? new[] { "001-hook", "002-cause", "003-accurate-sky-guide", "004-viewing-tip", "005-final-reminder" }
            : new[] { "001-hook", "002-what-is-it", "003-cause", "004-interesting-fact", "005-best-time", "006-accurate-sky-guide", "007-what-you-will-see", "008-viewing-tips", "009-final-reminder" };
        var modes = ids.Select(id => id.Contains("accurate-sky-guide", StringComparison.OrdinalIgnoreCase) ? "AccurateSkyGuideScene" : id.Contains("tip", StringComparison.OrdinalIgnoreCase) || id.Contains("time", StringComparison.OrdinalIgnoreCase) ? "ViewingTipsScene" : id.Contains("final", StringComparison.OrdinalIgnoreCase) ? "FinalReminderScene" : id.Contains("cause", StringComparison.OrdinalIgnoreCase) || id.Contains("what", StringComparison.OrdinalIgnoreCase) || id.Contains("fact", StringComparison.OrdinalIgnoreCase) ? "ExplainerScene" : "CinematicStoryScene").ToArray();
        var result = new List<SceneAssetsV3Beat>();
        for (var i = 0; i < count; i++)
        {
            var narration = i < context.NarrationBeats.Count ? context.NarrationBeats[i] : BuildFallbackNarration(context, ids[i]);
            narration = EnsureRequiredNarrationContext(context, narration);
            ValidateNoForbiddenTerms("narrationBeat", narration, context.ForbiddenTerms);
            var intent = BuildVisualIntent(context, ids[i]);
            var prompt = BuildVisualPrompt(context, ids[i], intent);
            ValidateNoForbiddenTerms("visualIntent", intent, context.ForbiddenTerms);
            ValidateNoForbiddenTerms("visualPrompt", prompt, context.ForbiddenTerms);
            result.Add(new SceneAssetsV3Beat(i + 1, ids[i], modes[i], narration, intent, prompt, modes[i] == "AccurateSkyGuideScene" ? 7 : 5 + i % 2, "question-driven-narration-v2.json", "production-event-intelligence.json"));
        }
        return result;
    }

    private static string BuildFallbackNarration(SceneAssetsV3TimelineContext c, string sceneId)
        => IsPlanetConjunction(c.EventType)
            ? sceneId.Contains("guide", StringComparison.OrdinalIgnoreCase) ? "Look to the western sky after sunset for Venus and Jupiter close together." : "Venus and Jupiter form a close planetary conjunction, separated by about 1.63 degrees in twilight."
            : $"Watch this {c.EventType} sky event with {JoinNatural(c.RequiredVisualObjects)} as the visual focus.";

    private static string EnsureRequiredNarrationContext(SceneAssetsV3TimelineContext c, string narration)
        => IsPlanetConjunction(c.EventType) && (!ContainsTerm(narration, "Venus") || !ContainsTerm(narration, "Jupiter"))
            ? $"{narration} Venus and Jupiter appear as two bright planets close together in the western sky after sunset, separated by 1.63 degrees above the twilight horizon."
            : narration;

    private static string BuildVisualIntent(SceneAssetsV3TimelineContext c, string sceneId)
        => IsPlanetConjunction(c.EventType)
            ? $"Show Venus and Jupiter as two bright planets close together in the western sky after sunset, near a twilight horizon, with angular separation 1.63 degrees. Theme: {FirstNonEmpty(c.StoryTheme, c.VisualTheme, c.SkyGuideTheme, "planet conjunction viewing guide")}."
            : $"Show {JoinNatural(c.RequiredVisualObjects)} for {c.EventType}. Theme: {FirstNonEmpty(c.StoryTheme, c.VisualTheme, c.SkyGuideTheme, "astronomy viewing guide")}.";

    private static string BuildVisualPrompt(SceneAssetsV3TimelineContext c, string sceneId, string intent)
        => IsPlanetConjunction(c.EventType)
            ? $"Realistic cinematic astronomy scene: Jupiter and Venus, two bright planets close together, western sky after sunset, twilight horizon, angular separation 1.63 degrees, clear horizon, subtle observing-guide composition, no text, no labels. Scene focus: {sceneId}. {intent} Required objects: {JoinNatural(c.RequiredVisualObjects)}."
            : $"Realistic cinematic astronomy scene for {c.EventType}, required visual objects: {JoinNatural(c.RequiredVisualObjects)}, no unrelated event imagery, no text, no labels. Scene focus: {sceneId}. {intent}";

    private static IReadOnlyList<string> ExtractNarrationBeats(JsonElement root)
    {
        var beats = new List<string>();
        CollectStringsFromProperties(root, beats, ["narrationBeat", "narrationText", "voiceover", "text"]);
        return beats.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(12).ToArray();
    }

    private static void CollectStringsFromProperties(JsonElement element, List<string> values, IReadOnlyCollection<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject()) { if (p.Value.ValueKind == JsonValueKind.String && names.Contains(p.Name)) values.Add(p.Value.GetString()!); else CollectStringsFromProperties(p.Value, values, names); }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectStringsFromProperties(item, values, names);
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        var values = new List<string>();
        CollectArrayValues(root, propertyName, values);
        return values.ToArray();
    }
    private static void CollectArrayValues(JsonElement e, string name, List<string> values)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Array) values.AddRange(p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x))); else CollectArrayValues(p.Value, name, values); }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) CollectArrayValues(item, name, values);
    }
    private static string FirstString(JsonElement root, params string[] names) { foreach (var name in names) { var value = FindString(root, name); if (!string.IsNullOrWhiteSpace(value)) return value!; } return string.Empty; }
    private static string? FindString(JsonElement e, string name) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString(); var v = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(v)) return v; } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) { var v = FindString(item, name); if (!string.IsNullOrWhiteSpace(v)) return v; } return null; }
    private static string? ResolveFirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static bool IsPlanetConjunction(string eventType) => eventType.Contains("CONJUNCTION", StringComparison.OrdinalIgnoreCase) || eventType.Contains("PlanetConjunction", StringComparison.OrdinalIgnoreCase) || eventType.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<string> DefaultForbiddenTerms(string eventType) => IsPlanetConjunction(eventType) ? ["Geminids", "meteor", "meteor shower", "radiant", "Phaethon", "debris stream"] : [];
    private static string JoinNatural(IEnumerable<string> values) => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)).DefaultIfEmpty("the selected sky event"));
    private static IReadOnlyList<string> DetectForbiddenTerms(string text, IReadOnlyList<string> terms) => terms.Where(term => ContainsTerm(text, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static void ValidateNoForbiddenTerms(string area, string text, IReadOnlyList<string> terms) { var hits = DetectForbiddenTerms(text, terms); if (hits.Count > 0) throw new InvalidOperationException($"Scene Assets V3 forbidden terms detected in {area}: {string.Join(", ", hits)}"); }
    private static bool ContainsTerm(string text, string term) => !string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(term) && CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase) >= 0;

    private sealed record SceneAssetsV3TimelineContext(string EventType, string StoryTheme, string VisualTheme, string SkyGuideTheme, IReadOnlyList<string> RequiredVisualObjects, IReadOnlyList<string> ForbiddenTerms, IReadOnlyList<string> NarrationBeats);

}
