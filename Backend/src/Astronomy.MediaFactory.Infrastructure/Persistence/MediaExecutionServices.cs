using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using System.Diagnostics;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ExternalProcessRunner(ILogger<ExternalProcessRunner> logger) : IExternalProcessRunner
{
    public async Task<ExternalProcessExecutionResult> RunAsync(string executablePath, string arguments, string workingDirectory, string? outputPath, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var psi = new ProcessStartInfo(executablePath, arguments) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Unable to start process: {executablePath}");
        var stdOutBuilder = new System.Text.StringBuilder();
        var stdErrBuilder = new System.Text.StringBuilder();
        var stdOutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdErrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) stdOutClosed.TrySetResult();
            else stdOutBuilder.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) stdErrClosed.TrySetResult();
            else stdErrBuilder.AppendLine(e.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdOutClosed.Task, stdErrClosed.Task).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
        var completed = DateTime.UtcNow;
        var result = new ExternalProcessExecutionResult(executablePath, arguments, workingDirectory, started, completed, (long)(completed - started).TotalMilliseconds, p.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString(), outputPath, outputPath is not null && File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0);
        logger.LogInformation("External process executed {@Result}", result);
        return result;
    }
}

public sealed class FFmpegService(IOptions<RenderingOptions> options, IExternalProcessRunner runner) : IFFmpegService
{
    public Task<ExternalProcessExecutionResult> ExecuteAsync(string arguments, string workingDirectory, string? outputPath, CancellationToken cancellationToken)
    {
        var ffmpegPath = options.Value.FfmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath)) throw new InvalidOperationException("FFmpeg executable not configured or not found.");
        return runner.RunAsync(ffmpegPath, arguments, workingDirectory, outputPath, cancellationToken);
    }
}

public sealed class FFprobeService(IOptions<RenderingOptions> options, IExternalProcessRunner runner) : IFFprobeService
{
    public async Task<FfprobeMediaInfo?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var ffprobePath = options.Value.FfprobePath;
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath)) throw new InvalidOperationException("FFmpeg executable not configured or not found.");
        var args = $"-v error -print_format json -show_streams -show_format \"{path}\"";
        var res = await runner.RunAsync(ffprobePath, args, Directory.GetCurrentDirectory(), path, cancellationToken);
        if (res.ExitCode != 0) return null;
        using var doc = JsonDocument.Parse(res.StdOut);
        var fmt = doc.RootElement.GetProperty("format");
        var duration = fmt.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), out var parsed) ? parsed : 0d;
        int width = 0, height = 0; string? codec = null; bool hasAudio = false; bool hasVideo = false;
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "video") { hasVideo = true; width = s.TryGetProperty("width", out var w) ? w.GetInt32() : 0; height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0; codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() : null; }
            if (type == "audio") hasAudio = true;
        }
        return new FfprobeMediaInfo(duration, width, height, codec, hasAudio, hasVideo);
    }
}

public sealed class MediaValidationService(IFFprobeService ffprobeService) : IMediaValidationService
{
    public async Task<MediaValidationResult> ValidateMp4Async(string path, long minBytes, CancellationToken cancellationToken)
    {
        var issues = Basic(path, minBytes);
        var info = issues.Count == 0 ? await ffprobeService.ProbeAsync(path, cancellationToken) : null;
        if (info is null || info.DurationSeconds <= 0 || info.Width <= 0 || info.Height <= 0 || string.IsNullOrWhiteSpace(info.VideoCodec)) issues.Add($"Invalid MP4 media info for {path}");
        return new MediaValidationResult(issues.Count == 0, path, "mp4", issues, info);
    }

    public async Task<MediaValidationResult> ValidateWavAsync(string path, CancellationToken cancellationToken)
    {
        var issues = Basic(path, 10 * 1024);
        var info = issues.Count == 0 ? await ffprobeService.ProbeAsync(path, cancellationToken) : null;
        if (info is null || info.DurationSeconds <= 0) issues.Add($"Invalid WAV media info for {path}");
        return new MediaValidationResult(issues.Count == 0, path, "wav", issues, info);
    }

    public MediaValidationResult ValidateImage(string path, long minBytes, string mediaType)
    {
        var issues = Basic(path, minBytes);
        try { var i = Image.Identify(path); if (i is null || i.Width <= 0 || i.Height <= 0) issues.Add($"Invalid image dimensions for {path}"); }
        catch (Exception ex) { issues.Add($"Image decode failed for {path}: {ex.Message}"); }
        return new MediaValidationResult(issues.Count == 0, path, mediaType, issues);
    }

    private static List<string> Basic(string path, long minBytes)
    {
        var issues = new List<string>();
        if (!File.Exists(path)) issues.Add($"Missing file: {path}");
        else if (new FileInfo(path).Length <= minBytes) issues.Add($"File too small: {path}");
        return issues;
    }
}
