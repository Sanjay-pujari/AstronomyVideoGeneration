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
        var runScopeDirectory = BuildRunScopeDirectory(outputDirectory, context.Date);
        var scriptsDirectory = string.IsNullOrWhiteSpace(_options.ScriptsDirectory)
            ? Path.Combine(visualsDirectory, "scripts")
            : Path.Combine(_options.ScriptsDirectory, runScopeDirectory);
        var capturesDirectory = string.IsNullOrWhiteSpace(_options.CaptureDirectory)
            ? Path.Combine(visualsDirectory, "screenshots")
            : Path.Combine(_options.CaptureDirectory, runScopeDirectory);

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
                TryResolveCapturedImage(scene);
            }

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

    private static string BuildRunScopeDirectory(string outputDirectory, DateOnly date)
    {
        var runFolder = Path.GetFileName(Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (Guid.TryParse(runFolder, out _))
            return Path.Combine(date.ToString("yyyy-MM-dd"), runFolder);

        return date.ToString("yyyy-MM-dd");
    }

    private void TryResolveCapturedImage(StellariumScene scene)
    {
        var captureDirectory = Path.GetDirectoryName(scene.OutputImagePath);
        if (string.IsNullOrWhiteSpace(captureDirectory) || !Directory.Exists(captureDirectory))
            return;

        var scenePrefix = Path.GetFileNameWithoutExtension(scene.OutputImagePath);
        var discoveredCapture = Directory
            .EnumerateFiles(captureDirectory, $"{scenePrefix}*.png", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(discoveredCapture))
            return;

        try
        {
            File.Move(discoveredCapture, scene.OutputImagePath, overwrite: true);
            _logger.LogInformation(
                "Mapped Stellarium screenshot '{DiscoveredCapture}' to expected output '{ExpectedCapture}' for scene {SceneId}.",
                discoveredCapture,
                scene.OutputImagePath,
                scene.SceneId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to map Stellarium screenshot '{DiscoveredCapture}' to expected output '{ExpectedCapture}' for scene {SceneId}.",
                discoveredCapture,
                scene.OutputImagePath,
                scene.SceneId);
        }
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
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    // Stellarium is an OpenGL GUI app. Running it with CreateNoWindow can lead to
                    // a headless/invalid rendering surface and black screenshots on Windows.
                    // Keep the windowed process so the framebuffer is actually rendered.
                    CreateNoWindow = false
                });

                if (process is null)
                    continue;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
                await process.WaitForExitAsync(timeoutCts.Token);
                await WaitForCaptureWriteAsync(scene, cancellationToken);
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

                // Give the OS + GPU driver a short cooldown between GUI launches.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private static async Task WaitForCaptureWriteAsync(StellariumScene scene, CancellationToken cancellationToken)
    {
        var captureDirectory = Path.GetDirectoryName(scene.OutputImagePath);
        if (string.IsNullOrWhiteSpace(captureDirectory) || !Directory.Exists(captureDirectory))
            return;

        var scenePrefix = Path.GetFileNameWithoutExtension(scene.OutputImagePath);
        const int maxAttempts = 30;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = Directory
                .EnumerateFiles(captureDirectory, $"{scenePrefix}*.png", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                var fileInfo = new FileInfo(candidate);
                if (fileInfo.Length > 0)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static List<StellariumScene> ComposeScenes(AstronomyContext context, string scriptsDirectory, string capturesDirectory)
    {
        var sceneTime = BuildInitialSceneTimeUtc(context);
        var moonEvent = context.Events.FirstOrDefault(e => e.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var brightPlanet = context.Events.FirstOrDefault(e => e.Category.Contains("planet", StringComparison.OrdinalIgnoreCase));
        var deepSky = context.Events.FirstOrDefault(e =>
            e.Category.Contains("deep", StringComparison.OrdinalIgnoreCase)
            || e.Category.Contains("constellation", StringComparison.OrdinalIgnoreCase));

        var sceneDefinitions = new[]
        {
            new SceneDefinition("sky-overview", "Sky overview", $"Tonight's sky overview for {context.LocationName}.", "Polaris"),
            new SceneDefinition("moon", "Moon focus", moonEvent is null ? "Moon scene generated from fallback ephemeris." : moonEvent.Details, moonEvent?.ObjectName ?? "Moon"),
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
                TargetObject = def.TargetObject,
                Latitude = context.Latitude,
                Longitude = context.Longitude,
                SceneTimeUtc = sceneTime.AddMinutes(order * 10),
                ScriptPath = Path.Combine(scriptsDirectory, $"{prefix}.ssc"),
                MetadataPath = Path.Combine(scriptsDirectory, $"{prefix}.json"),
                OutputImagePath = Path.Combine(capturesDirectory, $"{prefix}.png")
            };
        }).ToList();
    }

    private static DateTimeOffset BuildInitialSceneTimeUtc(AstronomyContext context)
    {
        var localEvening = DateTime.SpecifyKind(context.Date.ToDateTime(new TimeOnly(20, 0)), DateTimeKind.Unspecified);

        try
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZone);
            return TimeZoneInfo.ConvertTimeToUtc(localEvening, timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(localEvening, TimeSpan.Zero);
        }
        catch (InvalidTimeZoneException)
        {
            return new DateTimeOffset(localEvening, TimeSpan.Zero);
        }
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
