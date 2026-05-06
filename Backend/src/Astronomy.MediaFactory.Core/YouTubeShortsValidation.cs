using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed class YouTubeShortDiagnostics
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string AspectRatio { get; init; } = "unknown";
    public double DurationSeconds { get; init; }
    public string VideoCodec { get; init; } = "unknown";
    public string AudioCodec { get; init; } = "unknown";
    public double Fps { get; init; }
    public long? Bitrate { get; init; }
    public bool IsValidYouTubeShort { get; init; }
    public string Container { get; init; } = "unknown";
}

public sealed class YouTubeShortValidationReport
{
    public bool YouTubeShortEligible { get; init; }
    public bool IsValidYouTubeShort { get; init; }
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
    public YouTubeShortDiagnostics Diagnostics { get; init; } = new();
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class YouTubeShortsValidation
{
    public const int RequiredWidth = 1080;
    public const int RequiredHeight = 1920;
    public const double MaximumDurationSeconds = 60d;
    public const string DiagnosticsFileName = "short-video-diagnostics.json";
    public const string UploadValidationFileName = "youtube-short-validation.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool ContainsShortsMarker(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains("#Shorts", StringComparison.OrdinalIgnoreCase);

    public static string EnsureShortsMarkerInDescription(string? title, string? description)
    {
        var normalizedDescription = description?.TrimEnd() ?? string.Empty;
        if (ContainsShortsMarker(title) || ContainsShortsMarker(normalizedDescription))
        {
            return normalizedDescription;
        }

        return string.IsNullOrWhiteSpace(normalizedDescription)
            ? "#Shorts"
            : $"{normalizedDescription}{Environment.NewLine}{Environment.NewLine}#Shorts";
    }

    public static YouTubeShortDiagnostics Evaluate(
        int width,
        int height,
        double durationSeconds,
        string? videoCodec,
        string? audioCodec,
        double fps,
        long? bitrate,
        string? container)
    {
        var normalizedVideoCodec = NormalizeCodec(videoCodec);
        var normalizedAudioCodec = NormalizeCodec(audioCodec);
        var normalizedContainer = NormalizeContainer(container);
        var isPortrait = height > width;
        var isMp4 = string.Equals(normalizedContainer, "mp4", StringComparison.OrdinalIgnoreCase);
        var isH264 = string.Equals(normalizedVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        var isAac = string.Equals(normalizedAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);
        var isDurationEligible = durationSeconds > 0d && durationSeconds <= MaximumDurationSeconds;

        return new YouTubeShortDiagnostics
        {
            Width = width,
            Height = height,
            AspectRatio = BuildAspectRatio(width, height),
            DurationSeconds = durationSeconds,
            VideoCodec = normalizedVideoCodec,
            AudioCodec = normalizedAudioCodec,
            Fps = fps,
            Bitrate = bitrate,
            Container = normalizedContainer,
            IsValidYouTubeShort = isPortrait && isDurationEligible && isMp4 && isH264 && isAac
        };
    }

    public static YouTubeShortValidationReport BuildValidationReport(YouTubeShortDiagnostics diagnostics)
    {
        var warnings = new List<string>();
        if (diagnostics.Width != RequiredWidth || diagnostics.Height != RequiredHeight)
        {
            warnings.Add($"Short video dimensions are {diagnostics.Width}x{diagnostics.Height}; expected {RequiredWidth}x{RequiredHeight}.");
        }

        if (diagnostics.Height <= diagnostics.Width)
        {
            warnings.Add("Short video is not portrait.");
        }

        if (diagnostics.DurationSeconds <= 0d || diagnostics.DurationSeconds > MaximumDurationSeconds)
        {
            warnings.Add($"Short video duration is {diagnostics.DurationSeconds:F2}s; expected <= {MaximumDurationSeconds:F0}s.");
        }

        if (!string.Equals(diagnostics.Container, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Short video container is {diagnostics.Container}; expected mp4.");
        }

        if (!string.Equals(diagnostics.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Short video codec is {diagnostics.VideoCodec}; expected h264/libx264 output.");
        }

        if (!string.Equals(diagnostics.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Short audio codec is {diagnostics.AudioCodec}; expected aac.");
        }

        if (diagnostics.Fps > 0d && Math.Abs(diagnostics.Fps - 30d) > 0.2d)
        {
            warnings.Add($"Short video fps is {diagnostics.Fps:F2}; 30 fps is preferred.");
        }

        return new YouTubeShortValidationReport
        {
            YouTubeShortEligible = diagnostics.IsValidYouTubeShort,
            IsValidYouTubeShort = diagnostics.IsValidYouTubeShort,
            Warnings = warnings,
            Diagnostics = diagnostics,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static async Task<YouTubeShortDiagnostics> ProbeAndWriteDiagnosticsAsync(string videoPath, string outputDirectory, string? ffprobePath, CancellationToken cancellationToken)
    {
        var diagnostics = await ProbeAsync(videoPath, ffprobePath, cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, DiagnosticsFileName), JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        return diagnostics;
    }

    public static async Task<YouTubeShortValidationReport> ValidateBeforeUploadAsync(string videoPath, string? ffprobePath, CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(videoPath) ?? Directory.GetCurrentDirectory();
        YouTubeShortDiagnostics diagnostics;
        var diagnosticsPath = Path.Combine(outputDirectory, DiagnosticsFileName);
        if (File.Exists(diagnosticsPath))
        {
            var json = await File.ReadAllTextAsync(diagnosticsPath, cancellationToken);
            diagnostics = JsonSerializer.Deserialize<YouTubeShortDiagnostics>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? await ProbeAsync(videoPath, ffprobePath, cancellationToken);
        }
        else
        {
            diagnostics = await ProbeAndWriteDiagnosticsAsync(videoPath, outputDirectory, ffprobePath, cancellationToken);
        }

        var report = BuildValidationReport(diagnostics);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, UploadValidationFileName), JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        return report;
    }

    private static async Task<YouTubeShortDiagnostics> ProbeAsync(string videoPath, string? ffprobePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(videoPath))
        {
            return Evaluate(0, 0, 0d, null, null, 0d, null, Path.GetExtension(videoPath).TrimStart('.'));
        }

        var executable = string.IsNullOrWhiteSpace(ffprobePath) ? "ffprobe" : ffprobePath;
        var arguments = $"-v quiet -print_format json -show_streams -show_format \"{videoPath}\"";
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Evaluate(0, 0, 0d, null, null, 0d, null, Path.GetExtension(videoPath).TrimStart('.'));
        }

        if (process is null)
        {
            return Evaluate(0, 0, 0d, null, null, 0d, null, Path.GetExtension(videoPath).TrimStart('.'));
        }

        using (process)
        {
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Evaluate(0, 0, 0d, null, null, 0d, null, Path.GetExtension(videoPath).TrimStart('.'));
            }

            return ParseProbeOutput(output, videoPath);
        }
    }

    private static YouTubeShortDiagnostics ParseProbeOutput(string output, string videoPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var streams = root.TryGetProperty("streams", out var streamsElement) ? streamsElement.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
            var videoStream = streams.FirstOrDefault(IsVideoStream);
            var audioStream = streams.FirstOrDefault(IsAudioStream);
            var format = root.TryGetProperty("format", out var formatElement) ? formatElement : default;

            return Evaluate(
                GetInt(videoStream, "width"),
                GetInt(videoStream, "height"),
                GetDouble(format, "duration"),
                GetString(videoStream, "codec_name"),
                GetString(audioStream, "codec_name"),
                ParseFps(GetString(videoStream, "avg_frame_rate") ?? GetString(videoStream, "r_frame_rate")),
                GetLong(format, "bit_rate"),
                GetString(format, "format_name") ?? Path.GetExtension(videoPath).TrimStart('.'));
        }
        catch (JsonException)
        {
            return Evaluate(0, 0, 0d, null, null, 0d, null, Path.GetExtension(videoPath).TrimStart('.'));
        }
    }

    private static bool IsVideoStream(JsonElement stream)
        => string.Equals(GetString(stream, "codec_type"), "video", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudioStream(JsonElement stream)
        => string.Equals(GetString(stream, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (int.TryParse(GetString(element, propertyName), out var value))
        {
            return value;
        }

        return element.ValueKind != JsonValueKind.Undefined
               && element.TryGetProperty(propertyName, out var property)
               && property.TryGetInt32(out value)
            ? value
            : 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
        => double.TryParse(GetString(element, propertyName), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? Math.Max(0d, value) : 0d;

    private static long? GetLong(JsonElement element, string propertyName)
        => long.TryParse(GetString(element, propertyName), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double ParseFps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0d;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0d)
        {
            return numerator / denominator;
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps) ? fps : 0d;
    }

    private static string NormalizeCodec(string? codec)
        => string.IsNullOrWhiteSpace(codec) ? "unknown" : codec.Trim().ToLowerInvariant();

    private static string NormalizeContainer(string? container)
    {
        if (string.IsNullOrWhiteSpace(container)) return "unknown";
        var normalized = container.Trim().ToLowerInvariant();
        return normalized.Split(',').Contains("mp4", StringComparer.OrdinalIgnoreCase) ? "mp4" : normalized;
    }

    private static string BuildAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "unknown";
        var gcd = GreatestCommonDivisor(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            var next = a % b;
            a = b;
            b = next;
        }

        return Math.Abs(a);
    }
}
