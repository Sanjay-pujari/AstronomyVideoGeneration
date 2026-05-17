using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class MetaPosterFrameFallbackService : IMetaPosterFrameFallbackService
{
    public const string Reason = "Custom cover unsupported/rejected by Meta endpoint";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly RenderingOptions _renderingOptions;
    private readonly ILogger<MetaPosterFrameFallbackService> _logger;

    public MetaPosterFrameFallbackService(IOptions<RenderingOptions> renderingOptions, ILogger<MetaPosterFrameFallbackService> logger)
    {
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
    }

    public async Task<MetaPosterFrameFallbackResult> ApplyAsync(
        string outputDirectory,
        string inputShortVideoPath,
        string posterFrameImagePath,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputShortVideoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(posterFrameImagePath);

        var clampedDuration = Math.Clamp(durationSeconds, 0.5d, 1.0d);
        var outputMetaVideoPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputShortVideoPath)) ?? outputDirectory, "short-video-meta.mp4");
        var result = new MetaPosterFrameFallbackResult(
            PosterFrameApplied: true,
            PosterFrameImagePath: posterFrameImagePath,
            PosterFrameDurationSeconds: clampedDuration,
            InputShortVideoPath: inputShortVideoPath,
            OutputMetaVideoPath: outputMetaVideoPath,
            Reason: Reason);

        Directory.CreateDirectory(outputDirectory);

        if (!File.Exists(inputShortVideoPath))
        {
            throw new FileNotFoundException("Input short video for Meta poster-frame fallback was not found.", inputShortVideoPath);
        }

        if (!File.Exists(posterFrameImagePath))
        {
            throw new FileNotFoundException("Poster-frame image for Meta fallback was not found.", posterFrameImagePath);
        }

        var preset = VideoEncodingPreset.YouTubeShortProduction();
        var durationText = clampedDuration.ToString("0.###", CultureInfo.InvariantCulture);
        var filter = string.Join(string.Empty,
            $"[0:v]scale={preset.Width}:{preset.Height}:force_original_aspect_ratio=increase,",
            $"crop={preset.Width}:{preset.Height},setsar=1,format={preset.PixelFormat},fps=30[poster];",
            $"[1:v]scale={preset.Width}:{preset.Height}:force_original_aspect_ratio=increase,",
            $"crop={preset.Width}:{preset.Height},setsar=1,format={preset.PixelFormat},fps=30[mainv];",
            "[poster][mainv]concat=n=2:v=1:a=0[v];",
            $"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={durationText}[silence];",
            "[silence][1:a]concat=n=2:v=0:a=1[a]");

        var arguments = string.Join(' ',
            "-y",
            "-loop 1",
            $"-t {durationText}",
            $"-i {Quote(posterFrameImagePath)}",
            $"-i {Quote(inputShortVideoPath)}",
            $"-filter_complex {Quote(filter)}",
            "-map \"[v]\"",
            "-map \"[a]\"",
            $"-c:v {preset.Codec}",
            $"-preset {preset.Preset}",
            $"-crf {preset.Crf}",
            $"-b:v {preset.VideoBitrate}",
            $"-maxrate {preset.MaxVideoBitrate}",
            $"-bufsize {preset.BufferSize}",
            $"-pix_fmt {preset.PixelFormat}",
            "-c:a aac",
            $"-b:a {preset.AudioBitrate}",
            "-movflags +faststart",
            Quote(outputMetaVideoPath));

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "meta-poster-frame-ffmpeg-command.txt"), $"{_renderingOptions.FfmpegPath} {arguments}", cancellationToken);
        var execution = await ExecuteAsync(_renderingOptions.FfmpegPath, arguments, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "meta-poster-frame-ffmpeg.log"), execution, cancellationToken);

        if (!File.Exists(outputMetaVideoPath) || new FileInfo(outputMetaVideoPath).Length == 0)
        {
            throw new InvalidOperationException($"Meta poster-frame fallback video was not generated at {outputMetaVideoPath}.");
        }

        await WriteReportAsync(outputDirectory, result, cancellationToken);
        return result;
    }

    public static async Task WriteReportAsync(string outputDirectory, MetaPosterFrameFallbackResult result, CancellationToken cancellationToken)
    {
        var payload = new
        {
            posterFrameApplied = result.PosterFrameApplied,
            posterFrameImagePath = result.PosterFrameImagePath,
            posterFrameDurationSeconds = result.PosterFrameDurationSeconds,
            inputShortVideoPath = result.InputShortVideoPath,
            outputMetaVideoPath = result.OutputMetaVideoPath,
            reason = result.Reason
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "meta-poster-frame-report.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private async Task<string> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            return "Process failed to start.";
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var end = DateTimeOffset.UtcNow;
        var diagnostics = $"Command={fileName} {arguments}{Environment.NewLine}Start={start:O}{Environment.NewLine}End={end:O}{Environment.NewLine}ExitCode={process.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{await stderr}";
        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Meta poster-frame FFmpeg command exited with {ExitCode}. See diagnostics for details.", process.ExitCode);
            throw new InvalidOperationException($"Meta poster-frame FFmpeg failed with exit code {process.ExitCode}. {diagnostics}");
        }

        return diagnostics;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
