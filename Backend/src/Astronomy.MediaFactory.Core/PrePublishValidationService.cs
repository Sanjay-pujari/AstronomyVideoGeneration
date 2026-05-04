using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class PrePublishValidationService : IPrePublishValidationService
{
    private readonly RenderingOptions _renderingOptions;
    private readonly PublishingValidationOptions _options;
    private readonly ILogger<PrePublishValidationService> _logger;

    public PrePublishValidationService(IOptions<RenderingOptions> renderingOptions, IOptions<PublishingValidationOptions> options, ILogger<PrePublishValidationService> logger)
    { _renderingOptions = renderingOptions.Value; _options = options.Value; _logger = logger; }

    public async Task<PrePublishValidationReport> ValidateAsync(PrePublishValidationRequest request, CancellationToken cancellationToken)
    {
        var report = new PrePublishValidationReport { PipelineRunId = request.PipelineRunId, ContentType = request.ContentType, IsShort = request.IsShort, FinalVideoPath = request.FinalVideoPath, CheckedAtUtc = DateTimeOffset.UtcNow };
        if (!File.Exists(request.FinalVideoPath)) report.Errors.Add("Final video file is missing.");
        else if (new FileInfo(request.FinalVideoPath).Length <= 0) report.Errors.Add("Final video file size is zero.");

        var minDuration = request.IsShort ? _options.MinimumShortVideoDurationSeconds : _options.MinimumLongVideoDurationSeconds;
        var (duration, hasVideo, hasAudio) = await ProbeAsync(request.FinalVideoPath, cancellationToken);
        if (duration < minDuration) report.Errors.Add($"Video duration {duration:F2}s is shorter than minimum {minDuration}s.");
        if (!hasVideo) report.Errors.Add("Video stream is missing.");
        if (!hasAudio) report.Errors.Add("Audio stream is missing.");

        if (request.VisualPaths.Any(p => p.EndsWith(".placeholder.txt", StringComparison.OrdinalIgnoreCase))) report.Errors.Add("Placeholder visuals were used.");
        var missing = request.VisualPaths.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0) report.Errors.Add($"Missing screenshot files: {string.Join(", ", missing)}");

        var expected = request.Context.SceneObservationContexts.Select(x => x.ObjectName).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        var actual = request.Script.SceneScriptSections?.SectionsBySceneId.Keys.Select(id => request.Context.SceneObservationContexts.FirstOrDefault(s => s.SceneId.Equals(id, StringComparison.OrdinalIgnoreCase))?.ObjectName ?? id).Distinct(StringComparer.OrdinalIgnoreCase) ?? [];
        if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase)) report.Errors.Add("Narration objects do not match visual objects.");

        var observationTimes = request.Context.SceneObservationContexts
            .Select(s => s.UtcObservationTime)
            .OrderBy(t => t)
            .ToList();
        if (observationTimes.Count > 1)
        {
            var totalWindow = observationTimes[^1] - observationTimes[0];
            if (totalWindow > TimeSpan.FromHours(24))
            {
                report.Errors.Add("SceneObservationContext UTC times span more than 24 hours.");
            }
        }

        if (request.IsShort)
        {
            var mapPath = Path.Combine(request.OutputDirectory, "short-sequence-map.json");
            if (!File.Exists(mapPath)) report.Errors.Add("short-sequence-map.json is missing for short video.");
            else
            {
                var text = await File.ReadAllTextAsync(mapPath, cancellationToken);
                if (!text.Contains("sceneId", StringComparison.OrdinalIgnoreCase)) report.Errors.Add("short-sequence-map.json is invalid.");
            }
        }

        var ffmpegLog = Path.Combine(request.OutputDirectory, "ffmpeg.log");
        if (File.Exists(ffmpegLog))
        {
            var t = await File.ReadAllTextAsync(ffmpegLog, cancellationToken);
            if (t.Contains("fatal", StringComparison.OrdinalIgnoreCase)) report.Errors.Add("ffmpeg.log contains fatal errors.");
        }
        else report.Warnings.Add("ffmpeg.log was not found.");

        report.Passed = report.Errors.Count == 0;
        var reportPath = Path.Combine(request.OutputDirectory, "pre-publish-validation-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        _logger.LogInformation("Pre-publish validation complete for run {PipelineRunId}. Passed={Passed}", request.PipelineRunId, report.Passed);
        return report;
    }

    private async Task<(double DurationSeconds, bool HasVideo, bool HasAudio)> ProbeAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return (0, false, false);
        var ffprobe = string.IsNullOrWhiteSpace(_renderingOptions.FfprobePath) ? "ffprobe" : _renderingOptions.FfprobePath;
        var args = $"-v quiet -print_format json -show_streams -show_format \"{path}\"";
        var psi = new ProcessStartInfo(ffprobe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) return (0, false, false);
        using var doc = JsonDocument.Parse(o);
        var streams = doc.RootElement.GetProperty("streams");
        var hasVideo = streams.EnumerateArray().Any(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
        var hasAudio = streams.EnumerateArray().Any(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
        var dur = doc.RootElement.GetProperty("format").GetProperty("duration").GetString();
        _ = double.TryParse(dur, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d);
        return (d, hasVideo, hasAudio);
    }
}
