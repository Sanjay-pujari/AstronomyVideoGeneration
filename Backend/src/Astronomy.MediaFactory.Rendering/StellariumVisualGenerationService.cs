using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumVisualGenerationService : IVisualAssetProvider
{
    private static readonly byte[] PlaceholderPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

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
        var scriptsDirectory = string.IsNullOrWhiteSpace(_options.ScriptsDirectory) ? visualsDirectory : _options.ScriptsDirectory;
        var capturesDirectory = string.IsNullOrWhiteSpace(_options.CaptureDirectory)
            ? Path.Combine(visualsDirectory, "screenshots")
            : _options.CaptureDirectory;

        Directory.CreateDirectory(visualsDirectory);
        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(capturesDirectory);

        var scenes = BuildScenes(context, scriptsDirectory, capturesDirectory);

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
            _logger.LogWarning("Stellarium executable is not configured or not found. Creating placeholder screenshots.");
        }

        foreach (var scene in scenes)
        {
            if (!File.Exists(scene.OutputImagePath))
                await File.WriteAllBytesAsync(scene.OutputImagePath, PlaceholderPngBytes, cancellationToken);
        }

        return scenes.Select(s => s.OutputImagePath).ToList();
    }

    private async Task TryInvokeStellariumAsync(IReadOnlyCollection<StellariumScene> scenes, CancellationToken cancellationToken)
    {
        foreach (var scene in scenes)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = _options.ExecutablePath,
                    Arguments = $"--startup-script \"{scene.ScriptPath}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                    continue;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stellarium invocation failed for scene {SceneId}. Falling back to placeholders.", scene.SceneId);
            }
        }
    }

    private static List<StellariumScene> BuildScenes(AstronomyContext context, string scriptsDirectory, string capturesDirectory)
    {
        var sceneTime = new DateTimeOffset(context.Date.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc));
        var moonEvent = context.Events.FirstOrDefault(e => e.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var brightPlanet = context.Events.FirstOrDefault(e => e.Category.Contains("planet", StringComparison.OrdinalIgnoreCase));
        var deepSky = context.Events.FirstOrDefault(e =>
            e.Category.Contains("deep", StringComparison.OrdinalIgnoreCase)
            || e.Category.Contains("constellation", StringComparison.OrdinalIgnoreCase));

        var sceneDefinitions = new List<(string slug, string title, string caption, string target)>
        {
            ("sky-overview", "Sky overview", $"Tonight's sky overview for {context.LocationName}.", "Sun"),
            ("moon", "Moon focus", moonEvent is null ? "Moon scene generated from fallback ephemeris." : moonEvent.Details, moonEvent?.ObjectName ?? "Moon"),
            (NormalizeSlug(brightPlanet?.ObjectName ?? "jupiter"), "Bright planet", brightPlanet?.Details ?? "A bright planet visible this evening.", brightPlanet?.ObjectName ?? "Jupiter"),
            (NormalizeSlug(deepSky?.ObjectName ?? "orion"), "Deep sky target", deepSky?.Details ?? "A deep-sky object or constellation to observe.", deepSky?.ObjectName ?? "Orion"),
            ("wide-sky-close", "Closing wide sky", "Final wide view of the visible night sky.", "Polaris")
        };

        return sceneDefinitions.Select((def, index) =>
        {
            var order = index + 1;
            var prefix = $"{order:000}-{def.slug}";
            return new StellariumScene
            {
                SceneId = prefix,
                Title = def.title,
                Caption = def.caption,
                TargetObject = def.target,
                SceneTimeUtc = sceneTime.AddMinutes(order * 10),
                ScriptPath = Path.Combine(scriptsDirectory, $"{prefix}.ssc"),
                MetadataPath = Path.Combine(scriptsDirectory, $"{prefix}.json"),
                OutputImagePath = Path.Combine(capturesDirectory, $"{prefix}.png")
            };
        }).ToList();
    }

    private static string NormalizeSlug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "target" : slug;
    }
}
