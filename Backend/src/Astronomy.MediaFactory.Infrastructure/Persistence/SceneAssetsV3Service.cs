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
        await WriteJsonAsync(timelinePath, new SceneAssetsV3Timeline(Version, format, beats), ct); files.Add(timelinePath);

        var manifestScenes = new List<SceneAssetsV3ManifestScene>();
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

        var manifestPath = Path.Combine(dir, "scene-manifest-v3.json");
        var manifest = new SceneAssetsV3Manifest(Version, format, manifestScenes.Count, manifestScenes);
        await WriteJsonAsync(manifestPath, manifest, ct); files.Add(manifestPath);

        var duplicate = manifestScenes.GroupBy(s => s.Hash, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        var repeated = duplicate;
        var review = new SceneAssetsV3Review(manifestScenes.Count, manifestScenes.Any(s => s.RenderMode == "AccurateSkyGuideScene"), manifestScenes.Count(s => s.RenderMode is "CinematicStoryScene" or "FinalReminderScene"), manifestScenes.Count(s => s.RenderMode == "ExplainerScene"), manifestScenes.Count(s => s.RenderMode == "ViewingTipsScene"), duplicate, repeated, manifestScenes.All(s => !string.IsNullOrWhiteSpace(s.NarrationBeat)), "Failed");
        review = review with { Status = ReviewPassed(review, expectedCount) ? "Passed" : "Failed" };
        var reviewPath = Path.Combine(dir, "scene-review-v3.json");
        await WriteJsonAsync(reviewPath, review, ct); files.Add(reviewPath);

        var errors = BuildValidationErrors(timelinePath, manifestPath, review, expectedCount);
        var validation = new SceneAssetsV3Validation(Version, format, errors.Count == 0 ? "Passed" : "Failed", File.Exists(timelinePath), File.Exists(manifestPath), manifestScenes.Count == expectedCount, review.AccurateSkyGuidePresent, duplicate, repeated, review.AllScenesHaveNarrationBeat, errors);
        var validationPath = Path.Combine(dir, "scene-v3-validation.json");
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
            var font = SystemFonts.CreateFont("DejaVu Sans", 34, FontStyle.Bold);
            ctx.DrawText(beat.NarrationBeat, font, Color.FromRgb(235, 240, 248), new PointF(90, 900));
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), ct);
    }

    private static void DrawStars(IImageProcessingContext ctx, int seed) { for (var i = 0; i < 90; i++) ctx.Fill(Color.FromRgba(255, 255, 255, (byte)(64 + (i % 5) * 28)), new EllipsePolygon((i * 137 + seed * 61) % Width, (i * 73 + seed * 89) % 760, 1 + i % 3)); }
    private static void DrawCinematicForeground(IImageProcessingContext ctx, SceneAssetsV3Beat beat) { for (var i = 0; i < 5 + beat.BeatNo; i++) ctx.DrawLine(Color.FromRgb(190, 230, 255), 3, new PointF(250 + i * 210, 80 + i * 35), new PointF(80 + i * 210, 270 + i * 26)); ctx.Fill(Color.FromRgb(6, 8, 12), new RectangularPolygon(0, 830, Width, 250)); }
    private static void DrawSkyGuide(IImageProcessingContext ctx) { var font = SystemFonts.CreateFont("DejaVu Sans", 30, FontStyle.Regular); ctx.DrawLine(Color.FromRgb(120, 150, 170), 4, new PointF(180, 780), new PointF(1740, 780)); ctx.DrawText("UDAIPUR • DEC 13–14, 2026 • EAST → OVERHEAD AFTER 10 PM", font, Color.White, new PointF(250, 120)); ctx.DrawText("Best window: midnight to pre-dawn • Gemini radiant • meteors can appear anywhere • no telescope", font, Color.FromRgb(190, 220, 255), new PointF(250, 180)); ctx.DrawLine(Color.FromRgb(90, 180, 255), 5, new PointF(520, 760), new PointF(1160, 280)); ctx.DrawText("E horizon", font, Color.White, new PointF(430, 800)); ctx.DrawText("overhead", font, Color.White, new PointF(1120, 230)); ctx.DrawText("Gemini radiant / look direction", font, Color.FromRgb(255, 220, 120), new PointF(900, 420)); }

    private string ResolveRoot(SceneAssetsV3Request request) => !string.IsNullOrWhiteSpace(request.WorkingDirectoryRoot) ? request.WorkingDirectoryRoot! : string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string StyleFor(string mode) => mode == "ExplainerScene" ? "cinematic educational astronomy, realistic space documentary" : "Netflix science documentary, National Geographic astronomy, NASA campaign, realistic cinematic sky, minimal overlay";
    private static bool ReviewPassed(SceneAssetsV3Review r, int expected) => r.SceneCount == expected && r.AccurateSkyGuidePresent && !r.DuplicateHashDetected && !r.RepeatedBackgroundDetected && r.AllScenesHaveNarrationBeat;
    private static List<string> BuildValidationErrors(string timeline, string manifest, SceneAssetsV3Review r, int expected) { var e = new List<string>(); if (!File.Exists(timeline)) e.Add("visual-timeline-v3.json is missing."); if (!File.Exists(manifest)) e.Add("scene-manifest-v3.json is missing."); if (r.SceneCount != expected) e.Add($"Expected {expected} scenes but found {r.SceneCount}."); if (!r.AccurateSkyGuidePresent) e.Add("AccurateSkyGuideScene is missing."); if (r.DuplicateHashDetected) e.Add("Duplicate image hashes detected."); if (r.RepeatedBackgroundDetected) e.Add("Repeated generic infographic background detected."); if (!r.AllScenesHaveNarrationBeat) e.Add("At least one scene is missing narrationBeat."); return e; }
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
