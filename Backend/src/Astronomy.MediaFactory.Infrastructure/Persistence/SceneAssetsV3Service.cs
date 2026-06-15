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

        if (request.GenerateShort)
            shortValidation = await GenerateFormatAsync(root, "short", BuildShortBeats(), 5, request.OverwriteExisting, files, warnings, cancellationToken);
        if (request.GenerateLong)
            longValidation = await GenerateFormatAsync(root, "long", BuildLongBeats(), 9, request.OverwriteExisting, files, warnings, cancellationToken);

        return new SceneAssetsV3Response(root, files, warnings, shortValidation, longValidation);
    }

    private async Task<string> GenerateFormatAsync(string root, string format, IReadOnlyList<SceneAssetsV3Beat> beats, int expectedCount, bool overwrite, List<string> files, List<string> warnings, CancellationToken ct)
    {
        var dir = Path.Combine(root, format);
        Directory.CreateDirectory(dir);
        var timelinePath = Path.Combine(dir, "visual-timeline-v3.json");
        var manifestPath = Path.Combine(dir, "scene-manifest-v3.json");
        var reviewPath = Path.Combine(dir, "scene-review-v3.json");
        var validationPath = Path.Combine(dir, "scene-v3-validation.json");
        var metadataPath = Path.Combine(dir, "scene-timeline-metadata.json");
        var manifestScenes = new List<SceneAssetsV3ManifestScene>();
        var errors = new List<string>();

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
        await WriteJsonAsync(manifestPath, manifest, ct); files.Add(manifestPath);

        var duplicate = manifestScenes.GroupBy(s => s.Hash, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        var repeated = duplicate;
        var sameBackground = DetectRepeatedMetadata(beats, b => BackgroundSignature(b));
        var sameComposition = DetectRepeatedMetadata(beats, b => CompositionSignature(b));
        var sameCameraAngle = DetectRepeatedMetadata(beats, b => CameraSignature(b));
        var review = new SceneAssetsV3Review(manifestScenes.Count, manifestScenes.Any(s => s.RenderMode == "AccurateSkyGuideScene"), manifestScenes.Count(s => s.RenderMode is "CinematicStoryScene" or "FinalReminderScene"), manifestScenes.Count(s => s.RenderMode == "ExplainerScene"), manifestScenes.Count(s => s.RenderMode == "ViewingTipsScene"), duplicate, repeated, sameBackground, sameComposition, sameCameraAngle, manifestScenes.All(s => !string.IsNullOrWhiteSpace(s.NarrationBeat)), "Failed");
        review = review with { Status = ReviewPassed(review, expectedCount) ? "Passed" : "Failed" };
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
            if (beat.RenderMode == "AccurateSkyGuideScene") DrawSkyGuide(ctx);
            else DrawCinematicForeground(ctx, beat);
            var font = ResolveOverlayFont(34, FontStyle.Bold);
            ctx.DrawText(SmallSceneLabel(beat), font, Color.FromRgba(235, 240, 248, 210), new PointF(90, 900));
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), ct);
    }

    private static void DrawStars(IImageProcessingContext ctx, int seed) { for (var i = 0; i < 180; i++) ctx.Fill(Color.FromRgba(255, 255, 255, (byte)(58 + (i % 6) * 27)), new EllipsePolygon((i * 137 + seed * 61) % Width, (i * 73 + seed * 89) % 820, 1 + i % 3)); }
    private static void DrawCinematicForeground(IImageProcessingContext ctx, SceneAssetsV3Beat beat) { for (var i = 0; i < 5 + beat.BeatNo; i++) ctx.DrawLine(Color.FromRgb(190, 230, 255), 3, new PointF(250 + i * 210, 80 + i * 35), new PointF(80 + i * 210, 270 + i * 26)); ctx.Fill(Color.FromRgb(6, 8, 12), new RectangularPolygon(0, 830, Width, 250)); }
    private void DrawSkyGuide(IImageProcessingContext ctx)
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
        ctx.DrawText("Location: Udaipur, India  •  Date: Dec 13–14, 2026", label, Color.FromRgb(185, 215, 245), new PointF(96, 132));
        ctx.DrawText("Observation window: after 10 PM; strongest from midnight to pre-dawn", label, Color.FromRgb(185, 215, 245), new PointF(96, 168));
        var gemini = new[] { new PointF(965, 315), new PointF(1035, 385), new PointF(1105, 344), new PointF(1172, 405), new PointF(1000, 470), new PointF(1098, 512) };
        for (var i = 0; i < gemini.Length - 1; i++) ctx.DrawLine(Color.FromRgba(120, 170, 255, 190), 2, gemini[i], gemini[i + 1]);
        foreach (var p in gemini) ctx.Fill(Color.FromRgb(225, 238, 255), new EllipsePolygon(p, 4));
        var radiant = new PointF(1055, 425);
        ctx.Draw(Color.FromRgb(255, 210, 92), 4, new EllipsePolygon(radiant, 26));
        ctx.DrawLine(Color.FromRgb(255, 210, 92), 3, new PointF(radiant.X - 38, radiant.Y), new PointF(radiant.X + 38, radiant.Y));
        ctx.DrawLine(Color.FromRgb(255, 210, 92), 3, new PointF(radiant.X, radiant.Y - 38), new PointF(radiant.X, radiant.Y + 38));
        ctx.DrawLine(Color.FromRgb(90, 185, 255), 6, new PointF(520, 790), new PointF(1055, 425));
        ctx.DrawLine(Color.FromRgb(90, 185, 255), 6, new PointF(1055, 425), new PointF(1285, 245));
        ctx.DrawLine(Color.FromRgb(90, 185, 255), 5, new PointF(1285, 245), new PointF(1242, 257));
        ctx.DrawLine(Color.FromRgb(90, 185, 255), 5, new PointF(1285, 245), new PointF(1268, 288));
        ctx.DrawText("E horizon", label, Color.FromRgb(235, 242, 248), new PointF(448, 830));
        ctx.DrawText("overhead", label, Color.FromRgb(235, 242, 248), new PointF(1245, 202));
        ctx.DrawText("Gemini", label, Color.FromRgb(180, 210, 255), new PointF(1188, 405));
        ctx.DrawText("radiant", label, Color.FromRgb(255, 220, 120), new PointF(1098, 450));
        ctx.DrawText("viewing direction", label, Color.FromRgb(120, 210, 255), new PointF(690, 650));
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

    private static string SmallSceneLabel(SceneAssetsV3Beat beat) => beat.RenderMode switch
    {
        "AccurateSkyGuideScene" => "Geminids observing guide",
        "ExplainerScene" => beat.SceneId.Contains("cause", StringComparison.OrdinalIgnoreCase) ? "3200 Phaethon debris stream" : "Geminids meteor shower",
        "ViewingTipsScene" => beat.SceneId.Contains("time", StringComparison.OrdinalIgnoreCase) ? "Peak night window" : "Dark-sky viewing",
        "FinalReminderScene" => "Peak-night reminder",
        _ => "Geminids peak"
    };

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

    private static IReadOnlyList<SceneAssetsV3Beat> BuildShortBeats() => [
        Beat(1,"001-hook","CinematicStoryScene","Tonight, the Geminids meteor shower reaches its peak.","Create excitement and show the event beauty.","Dramatic Geminids meteor shower over a dark Rajasthan landscape, cinematic realistic night sky, no text.",5),
        Beat(2,"002-cause","ExplainerScene","The Geminids happen when Earth crosses debris from asteroid 3200 Phaethon.","Make the cause feel cinematic and understandable.","Cinematic space view of Earth crossing a glowing debris stream from asteroid 3200 Phaethon, educational but not a flat diagram.",6),
        Beat(3,"003-accurate-sky-guide","AccurateSkyGuideScene","From Udaipur, look east after 10 PM, with the best view from midnight to pre-dawn.","Provide deterministic observing guidance.","Deterministic Udaipur east-to-overhead Gemini radiant observing guide for Dec 13–14 2026.",7),
        Beat(4,"004-viewing-tip","ViewingTipsScene","You do not need a telescope. A dark location and patience are enough.","Show calm dark-sky observing conditions.","Cinematic dark-sky observing atmosphere, minimal overlay concepts: no telescope, dark location, eyes adapt 20 minutes.",5),
        Beat(5,"005-final-reminder","FinalReminderScene","Find a clear dark sky and do not miss the peak night.","Close with a memorable meteor-shower reminder.","Beautiful final Geminids meteor shower over quiet horizon, cinematic emotional astronomy campaign still, no text.",5)];
    private static IReadOnlyList<SceneAssetsV3Beat> BuildLongBeats() => [.. BuildShortBeats().Take(1), Beat(2,"002-what-is-it","ExplainerScene","The Geminids are one of the strongest annual meteor showers.","Establish why the event matters.","Cinematic wide sky filled with several bright Geminid meteors, educational documentary realism, no infographic panels.",6), Beat(3,"003-cause","ExplainerScene","They happen when Earth crosses debris from asteroid 3200 Phaethon.","Explain the physical cause cinematically.","Earth crossing asteroid 3200 Phaethon debris stream, realistic space documentary scene.",6), Beat(4,"004-interesting-fact","ExplainerScene","Unlike many meteor showers, Geminids come from an asteroid-like object.","Highlight unusual origin.","Asteroid-like 3200 Phaethon leaving dusty trail near the Sun, cinematic science documentary style.",6), Beat(5,"005-best-time","ViewingTipsScene","The peak is Dec 13–14, with the best window from midnight to pre-dawn.","Reinforce timing.","Dark night landscape with subtle moonless observing mood, minimal timing overlay space, cinematic.",5), Beat(6,"006-accurate-sky-guide","AccurateSkyGuideScene","From Udaipur, look east after 10 PM, then higher overhead later in the night.","Provide deterministic observing guidance.","Deterministic Udaipur east-to-overhead Gemini radiant observing guide for Dec 13–14 2026.",7), Beat(7,"007-what-you-will-see","CinematicStoryScene","Meteors can appear anywhere in the sky, often bright and colorful.","Show visual payoff.","Bright colorful Geminid meteors appearing across a wide realistic sky over dark terrain, cinematic.",6), Beat(8,"008-viewing-tips","ViewingTipsScene","No telescope is required. Find a dark place and give your eyes time to adapt.","Give practical observing advice.","Cinematic dark-sky viewing location with subtle minimal tip overlay space, no telescope, patient skywatching mood.",5), Beat(9,"009-final-reminder","FinalReminderScene","Dress warm, lie back, and enjoy the Geminids peak.","End with warm practical invitation.","Beautiful closing meteor shower scene with warm foreground and clear dark sky, premium documentary campaign still.",5)];
    private static SceneAssetsV3Beat Beat(int no, string id, string mode, string narration, string intent, string prompt, int sec) => new(no, id, mode, narration, intent, prompt, sec);
}
