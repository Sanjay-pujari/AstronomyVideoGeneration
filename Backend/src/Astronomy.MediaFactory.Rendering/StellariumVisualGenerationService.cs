using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumVisualGenerationService : IVisualAssetProvider
{
    private const int PlaceholderWidth = 1280;
    private const int PlaceholderHeight = 720;
    private const uint CrcPolynomial = 0xEDB88320;
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static readonly byte[] PlaceholderPngBytes = CreatePlaceholderPngBytes(PlaceholderWidth, PlaceholderHeight);

    private readonly StellariumOptions _options;
    private readonly StellariumScriptBuilder _scriptBuilder;
    private readonly ILogger<StellariumVisualGenerationService> _logger;

    public StellariumVisualGenerationService(
        IOptions<StellariumOptions> options,
        StellariumScriptBuilder scriptBuilder,
        ILogger<StellariumVisualGenerationService> logger)
    {
        _options = options.Value;
        _scriptBuilder = scriptBuilder;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        var visualsDirectory = Path.Combine(outputDirectory, "visuals");
        var scriptsBaseDirectory = string.IsNullOrWhiteSpace(_options.ScriptsDirectory)
            ? Path.Combine(visualsDirectory, "scripts")
            : _options.ScriptsDirectory;
        var capturesBaseDirectory = string.IsNullOrWhiteSpace(_options.CaptureDirectory)
            ? Path.Combine(visualsDirectory, "screenshots")
            : _options.CaptureDirectory;

        // Always isolate scripts/captures per pipeline run. Stellarium may not overwrite existing files reliably,
        // and reusing the same folder across runs makes troubleshooting hard.
        var runIdSegment = TryExtractRunIdSegment(outputDirectory) ?? Guid.NewGuid().ToString("N");
        var runSegment = Path.Combine(context.Date.ToString("yyyy-MM-dd"), runIdSegment);
        var scriptsDirectory = Path.Combine(scriptsBaseDirectory, runSegment);
        var capturesDirectory = Path.Combine(capturesBaseDirectory, runSegment);

        Directory.CreateDirectory(visualsDirectory);
        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(capturesDirectory);

        var scenes = ComposeScenes(context, scriptsDirectory, capturesDirectory);

        foreach (var scene in scenes)
        {
            var script = _scriptBuilder.BuildSceneScript(scene);
            await File.WriteAllTextAsync(scene.ScriptPath, script, cancellationToken);

            var sceneMetadata = JsonSerializer.Serialize(scene, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(scene.MetadataPath, sceneMetadata, cancellationToken);
        }

        var manifest = new StellariumCaptureManifest
        {
            Date = context.Date,
            LocationName = context.LocationName,
            ScriptsDirectory = scriptsDirectory,
            CaptureDirectory = capturesDirectory,
            Scenes = scenes
        };

        var manifestPath = Path.Combine(visualsDirectory, "capture-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var canTryExecution = !string.IsNullOrWhiteSpace(_options.ExecutablePath) && File.Exists(_options.ExecutablePath);
        if (canTryExecution)
        {
            await TryInvokeStellariumAsync(scenes, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Stellarium executable is not configured or not found at '{ExecutablePath}'. Creating placeholder screenshots.",
                _options.ExecutablePath);
        }

        foreach (var scene in scenes)
        {
            if (!File.Exists(scene.OutputImagePath))
            {
                var placeholderInfoPath = Path.ChangeExtension(scene.OutputImagePath, ".placeholder.txt");
                await File.WriteAllBytesAsync(scene.OutputImagePath, PlaceholderPngBytes, cancellationToken);
                await File.WriteAllTextAsync(
                    placeholderInfoPath,
                    $"Placeholder generated for scene '{scene.SceneId}' ({scene.Title}) because Stellarium output was unavailable.",
                    cancellationToken);
            }
        }

        return scenes.Select(s => s.OutputImagePath).ToList();
    }

    private static string? TryExtractRunIdSegment(string outputDirectory)
    {
        // PipelineOrchestrator creates: <WorkingDirectory>/<ContentType>/<yyyy-MM-dd>/<runIdN>
        // We want to reuse that <runIdN> to name Stellarium run folders for easy correlation.
        var last = Path.GetFileName(outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(last))
            return null;

        // run.Id is formatted as "N" (32 hex chars). Accept that and standard GUID string formats.
        if (Guid.TryParseExact(last, "N", out _))
            return last;
        if (Guid.TryParse(last, out var parsed))
            return parsed.ToString("N");

        return null;
    }

    private async Task TryInvokeStellariumAsync(IReadOnlyCollection<StellariumScene> scenes, CancellationToken cancellationToken)
    {
        foreach (var scene in scenes)
        {
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = _options.ExecutablePath,
                    Arguments = $"--startup-script \"{scene.ScriptPath}\"",
                    // Stellarium is a GUI/OpenGL app. Redirecting stdout/stderr and using CreateNoWindow can
                    // result in black/empty screenshots on some Windows setups.
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                if (process is null)
                    continue;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(75));
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout waiting for Stellarium to exit. We'll forcefully terminate it below.
                _logger.LogWarning("Stellarium did not exit within the timeout for scene {SceneId}. Terminating the process.", scene.SceneId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stellarium invocation failed for scene {SceneId}. Falling back to placeholders.", scene.SceneId);
            }
            finally
            {
                if (process is not null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // Stellarium is a GUI app; CreateNoWindow doesn't guarantee it will close.
                            // Ensure we never leak multiple instances when running many scenes.
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to terminate Stellarium process for scene {SceneId}.", scene.SceneId);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }

    private static List<StellariumScene> ComposeScenes(AstronomyContext context, string scriptsDirectory, string capturesDirectory)
    {
        // Interpret the scene time as "local evening time" at the user's configured timezone, then convert to UTC.
        // This avoids accidentally treating local time as UTC (which can shift the scene by many hours).
        var baseSceneTimeUtc = BuildLocalSceneTimeUtc(context.Date, new TimeOnly(20, 10), context.TimeZone);
        var moonEvent = context.Events.FirstOrDefault(e => e.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var brightPlanet = context.Events.FirstOrDefault(e => e.Category.Contains("planet", StringComparison.OrdinalIgnoreCase));
        var deepSky = context.Events.FirstOrDefault(e =>
            e.Category.Contains("deep", StringComparison.OrdinalIgnoreCase)
            || e.Category.Contains("constellation", StringComparison.OrdinalIgnoreCase));

        var sceneDefinitions = new[]
        {
            new SceneDefinition("sky-overview", "Sky overview", $"Tonight's sky overview for {context.LocationName}.", "Polaris"),
            new SceneDefinition("moon", "Moon focus", moonEvent is null ? "Moon scene generated from fallback ephemeris." : moonEvent.Details, NormalizeStellariumObjectName(moonEvent?.ObjectName ?? "Moon")),
            new SceneDefinition(NormalizeSlug(brightPlanet?.ObjectName ?? "jupiter"), "Bright planet", brightPlanet?.Details ?? "A bright planet visible this evening.", brightPlanet?.ObjectName ?? "Jupiter"),
            new SceneDefinition(NormalizeSlug(deepSky?.ObjectName ?? "orion"), "Deep sky target", deepSky?.Details ?? "A deep-sky object or constellation to observe.", deepSky?.ObjectName ?? "Orion"),
            new SceneDefinition("wide-sky-close", "Closing wide sky", "Final wide view of the visible night sky.", "Polaris")
        };

        return sceneDefinitions.Select((def, index) =>
        {
            var order = index + 1;
            var prefix = $"{order:000}-{def.Slug}";
            return new StellariumScene
            {
                SceneId = prefix,
                Title = def.Title,
                Caption = def.Caption,
                LocationName = context.LocationName,
                TargetObject = def.TargetObject,
                Latitude = context.Latitude,
                Longitude = context.Longitude,
                SceneTimeUtc = baseSceneTimeUtc.AddMinutes(order * 10),
                ScriptPath = Path.Combine(scriptsDirectory, $"{prefix}.ssc"),
                MetadataPath = Path.Combine(scriptsDirectory, $"{prefix}.json"),
                OutputImagePath = Path.Combine(capturesDirectory, $"{prefix}.png")
            };
        }).ToList();
    }

    private static string NormalizeStellariumObjectName(string name)
    {
        // Stellarium object IDs are typically simple names ("Moon", "Jupiter", etc.).
        // Ephemeris/event providers may return descriptive strings ("Waxing Gibbous Moon") that Stellarium won't resolve.
        if (name.Contains("moon", StringComparison.OrdinalIgnoreCase))
            return "Moon";
        return name;
    }

    private static DateTimeOffset BuildLocalSceneTimeUtc(DateOnly date, TimeOnly localTime, string timeZone)
    {
        // Prefer OS timezones when possible. If an IANA timezone is provided on Windows, it may not resolve.
        // We special-case Asia/Kolkata because it's a common default in this project.
        var localUnspecified = DateTime.SpecifyKind(date.ToDateTime(localTime), DateTimeKind.Unspecified);

        TimeZoneInfo? tz = null;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch
        {
            // ignore
        }

        if (tz is null && timeZone.Equals("Asia/Kolkata", StringComparison.OrdinalIgnoreCase))
        {
            // IST (no DST): UTC+05:30
            return new DateTimeOffset(localUnspecified, TimeSpan.FromHours(5.5)).ToUniversalTime();
        }

        if (tz is not null)
        {
            var offset = tz.GetUtcOffset(localUnspecified);
            return new DateTimeOffset(localUnspecified, offset).ToUniversalTime();
        }

        // Last resort: treat provided time as UTC to avoid exceptions (still better than failing the pipeline).
        return new DateTimeOffset(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Utc));
    }

    private static byte[] CreatePlaceholderPngBytes(int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (width * 4 + 1);
            raw[rowStart] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixelStart = rowStart + 1 + x * 4;
                raw[pixelStart] = 12;
                raw[pixelStart + 1] = 16;
                raw[pixelStart + 2] = 26;
                raw[pixelStart + 3] = 255;
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", BuildIHdr(width, height));
        WriteChunk(ms, "IDAT", Compress(raw));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] BuildIHdr(int width, int height)
    {
        var data = new byte[13];
        WriteBigEndianInt(width, data.AsSpan(0, 4));
        WriteBigEndianInt(height, data.AsSpan(4, 4));
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlibStream = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlibStream.Write(raw);
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream destination, string chunkType, byte[] data)
    {
        WriteBigEndianInt(data.Length, destination);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(chunkType);
        destination.Write(typeBytes);
        destination.Write(data);

        WriteBigEndianInt(unchecked((int)ComputeCrc(typeBytes, data)), destination);
    }

    private static uint ComputeCrc(byte[] typeBytes, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        crc = AppendCrc(crc, typeBytes);
        crc = AppendCrc(crc, data);
        return ~crc;
    }

    private static uint AppendCrc(uint seed, byte[] data)
    {
        var crc = seed;
        foreach (var b in data)
            crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var c = i;
            for (var bit = 0; bit < 8; bit++)
                c = (c & 1) == 1 ? (c >> 1) ^ CrcPolynomial : c >> 1;

            table[i] = c;
        }

        return table;
    }

    private static void WriteBigEndianInt(int value, Stream destination)
    {
        Span<byte> buffer = stackalloc byte[4];
        WriteBigEndianInt(value, buffer);
        destination.Write(buffer);
    }

    private static void WriteBigEndianInt(int value, Span<byte> destination)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private static string NormalizeSlug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "target" : slug;
    }

    private sealed record SceneDefinition(string Slug, string Title, string Caption, string TargetObject);
}
