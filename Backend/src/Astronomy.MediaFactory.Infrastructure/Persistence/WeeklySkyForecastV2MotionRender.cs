using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyCameraPathEngine : IWeeklyCameraPathEngine
{
    public WeeklyCameraPathPlan Build(WeeklyCinematicShot shot, string sequencePurpose)
    {
        var movement = MapMovement(shot.ShotType, shot.MotionStyle);
        var cameraIntensity = sequencePurpose.Contains("Opening", StringComparison.OrdinalIgnoreCase) ? 0.65 : sequencePurpose.Contains("Closing", StringComparison.OrdinalIgnoreCase) ? 0.35 : 0.8;
        return new WeeklyCameraPathPlan(shot.ShotCode, shot.NarrationSync.EstimatedStartSecond, shot.NarrationSync.EstimatedEndSecond, shot.StartFovDegrees, shot.EndFovDegrees, 120, 130, 30, 34, "easeInOutSine", 0.12, 0, Math.Min(1.5, shot.DurationSeconds / 3d), cameraIntensity, movement);
    }
    private static string MapMovement(string shotType, string motionStyle)
    {
        var key = $"{shotType} {motionStyle}".ToLowerInvariant();
        if (key.Contains("hold")) return "cinematic_hold";
        if (key.Contains("track")) return "object_tracking";
        if (key.Contains("pan")) return "slow_pan";
        if (key.Contains("zoom") && key.Contains("out")) return "slow_zoom_out";
        if (key.Contains("focus")) return "focus_pull";
        if (key.Contains("path")) return "path_trace";
        if (key.Contains("atmos")) return "atmospheric_fade";
        return "slow_zoom_in";
    }
}

public sealed class WeeklyCinematicCompositionEngine : IWeeklyCinematicCompositionEngine
{
    public async Task<string> ComposeAsync(WeeklyCinematicShot shot, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var img = new Image<Rgba32>(1920, 1080, new Rgba32(5, 8, 22));
        img.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 800), new PointF(0, 1080), GradientRepetitionMode.None, [new ColorStop(0, new Rgba32(0, 0, 0, 0)), new ColorStop(1, new Rgba32(0, 0, 0, 190))]));
            ctx.Fill(Color.FromRgba(0, 0, 0, 160), new Polygon(new LinearLineSegment(new[]
            {
                new PointF(0, 900),
                new PointF(350, 840),
                new PointF(740, 910),
                new PointF(1200, 860),
                new PointF(1500, 900),
                new PointF(1920, 840),
                new PointF(1920, 1080),
                new PointF(0, 1080)
            })));
            var font = SystemFonts.CreateFont("Arial", 44, FontStyle.Bold);
            var subtitle = SystemFonts.CreateFont("Arial", 28, FontStyle.Regular);
            ctx.DrawText(shot.TitleText, font, Color.White, new PointF(70, 70));
            ctx.DrawText(shot.SubtitleText, subtitle, Color.LightGray, new PointF(70, 130));
            ctx.DrawText($"{shot.DateLocal:yyyy-MM-dd}  {shot.CameraDirection}", subtitle, Color.LightBlue, new PointF(70, 980));
        });
        await img.SaveAsync(outputPath, new PngEncoder(), cancellationToken);
        return outputPath;
    }
}

public sealed class WeeklyMotionClipRenderer(IFFmpegService ffmpeg, IFFprobeService ffprobe) : IWeeklyMotionClipRenderer
{
    public async Task<(string Command, WeeklyMotionRenderValidation Validation)> RenderAsync(WeeklyCinematicShot shot, WeeklyCameraPathPlan cameraPath, string composedFramePath, string clipOutputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(clipOutputPath)!);
        var expectedDurationSeconds = Math.Max(1, shot.DurationSeconds);
        var args = $"-y -loop 1 -i \"{composedFramePath}\" -t {expectedDurationSeconds} -r 30 -vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,format=yuv420p\" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p \"{clipOutputPath}\"";
        var timeoutSeconds = Math.Clamp(expectedDurationSeconds + 20, 30, 60);
        var startUtc = DateTime.UtcNow;
        var ffmpegExitCode = -1;
        Console.WriteLine($"Starting FFmpeg preview clip. Shot={shot.ShotCode}, ExpectedDuration={expectedDurationSeconds}s");
        try
        {
            using var ffmpegTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ffmpegTimeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var ffmpegResult = await ffmpeg.ExecuteAsync(args, Directory.GetCurrentDirectory(), clipOutputPath, ffmpegTimeoutCts.Token);
            ffmpegExitCode = ffmpegResult.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsedTimeout = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds;
            Console.WriteLine($"Completed FFmpeg preview clip. Shot={shot.ShotCode}, ElapsedMs={elapsedTimeout}, ExitCode={ffmpegExitCode}");
            var timeoutValidation = new WeeklyMotionRenderValidation(shot.ShotCode, false, ["ffmpeg timeout"], [], clipOutputPath, null, null, null);
            return (args, timeoutValidation);
        }
        var elapsedMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds;
        Console.WriteLine($"Completed FFmpeg preview clip. Shot={shot.ShotCode}, ElapsedMs={elapsedMs}, ExitCode={ffmpegExitCode}");
        var errors = new List<string>(); var warnings = new List<string>();
        if (!File.Exists(clipOutputPath)) errors.Add("clip missing");
        else if (new FileInfo(clipOutputPath).Length <= 50 * 1024) errors.Add("clip too small");
        var info = await ffprobe.ProbeAsync(clipOutputPath, cancellationToken);
        if (info is null) errors.Add("ffprobe unavailable");
        else
        {
            if (Math.Abs(info.DurationSeconds - expectedDurationSeconds) > 1) errors.Add("duration mismatch");
            if (info.DurationSeconds > expectedDurationSeconds + 1) errors.Add("duration exceeds expected tolerance");
            if (!((info.Width == 1920 && info.Height == 1080) || (info.Width == 1280 && info.Height == 720))) errors.Add("resolution mismatch");
            Console.WriteLine($"Validated preview clip. Shot={shot.ShotCode}, ActualDuration={info.DurationSeconds}s");
        }
        return (args, new WeeklyMotionRenderValidation(shot.ShotCode, errors.Count == 0, errors, warnings, clipOutputPath, info?.DurationSeconds, info?.Width, info?.Height));
    }
}

public sealed class WeeklyMotionRenderManifestBuilder(
    IWeeklyCameraPathEngine cameraPathEngine,
    IWeeklyCinematicCompositionEngine compositionEngine,
    IWeeklyMotionClipRenderer clipRenderer) : IWeeklyMotionRenderManifestBuilder
{
    public async Task<WeeklyMotionRenderManifest> BuildAsync(WeeklyCinematicShotPackage cinematicShotPackage, string rootPath, string pipelineRunId, bool renderPreviewClips, int previewClipCount, CancellationToken cancellationToken)
    {
        var allShots = cinematicShotPackage.SceneSequences.SelectMany(s => s.Shots.Select(sh => (shot: sh, s.SequencePurpose))).ToList();
        var shotPlans = new List<WeeklyMotionRenderShotPlan>(); var ffmpeg = new List<string>(); var validations = new List<WeeklyMotionRenderValidation>(); var failed = new List<string>(); var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(rootPath)) throw new InvalidOperationException("missing working root");
        var compositionRoot = Path.Combine(rootPath, "composition", "frames");
        var clipsRoot = Path.Combine(rootPath, "stellarium", "clips");
        var toRender = renderPreviewClips ? Math.Min(1, Math.Max(0, previewClipCount)) : 0;
        for (int i = 0; i < allShots.Count; i++)
        {
            var (shot, purpose) = allShots[i];
            var camera = cameraPathEngine.Build(shot, purpose);
            var emotion = BuildEmotion(purpose);
            var composed = Path.Combine(compositionRoot, $"{shot.ShotCode}.composed.png");
            var clip = Path.Combine(clipsRoot, $"{shot.ShotCode}.mp4");
            shotPlans.Add(new WeeklyMotionRenderShotPlan(shot.ShotCode, shot.DurationSeconds, composed, clip, camera, emotion, []));
            if (shot.DurationSeconds <= 0 || camera.StartFov <= 0 || camera.EndFov <= 0) failed.Add(shot.ShotCode);
            if (i < toRender)
            {
                await compositionEngine.ComposeAsync(shot, composed, cancellationToken);
                var render = await clipRenderer.RenderAsync(shot, camera, composed, clip, cancellationToken);
                ffmpeg.Add(render.Command); validations.Add(render.Validation);
            }
        }
        warnings.Add("Stellarium live capture unavailable; static preview render used.");
        warnings.Add("music sync placeholder only");
        var manifestPath = Path.Combine(rootPath, "debug", "weekly-motion-render-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var manifest = new WeeklyMotionRenderManifest(shotPlans, shotPlans.Select(x => x.CameraPath).ToList(), shotPlans.Select(x => x.ClipOutputPath).ToList(), shotPlans.Select(x => x.CompositionFramePath).ToList(), ffmpeg, validations, failed, warnings, manifestPath);
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return manifest;
    }

    private static WeeklyShotEmotionPlan BuildEmotion(string sequencePurpose)
    {
        var p = sequencePurpose.ToLowerInvariant();
        var emotion = p.Contains("opening") ? "wonder" : p.Contains("guide") ? "practical" : p.Contains("closing") ? "calm_close" : "climax";
        return new WeeklyShotEmotionPlan(emotion, "placeholder-beat", emotion == "climax" ? 0.85 : 0.45, emotion == "practical" ? 0.4 : 0.7, emotion == "climax" ? 0.8 : 0.35);
    }
}
