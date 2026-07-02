using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public class ThumbnailV7CinematicOverlayRenderer
{
    public const string RendererName = "ThumbnailV7CinematicOverlayRenderer";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ThumbnailV7ProfileResolver _profileResolver = new();
    private readonly ThumbnailV7ObservationModelBuilder _observationBuilder = new();
    private readonly ThumbnailV7TemplatePlanner _templatePlanner = new();
    private readonly ThumbnailV7BackgroundPromptBuilder _promptBuilder = new();
    private readonly ThumbnailV7CinematicOverlayComposer _composer = new();
    private readonly ThumbnailV7VariantRenderer _renderer = new();
    private readonly ThumbnailV7Validator _validator = new();
    private readonly Func<ThumbnailV7AzureImage2Request, CancellationToken, Task<ThumbnailV7AzureImage2GenerationResult>>? _azureImage2Generator;

    public ThumbnailV7CinematicOverlayRenderer(string celestialAssetsRoot = "assets/celestial", Func<ThumbnailV7AzureImage2Request, CancellationToken, Task<ThumbnailV7AzureImage2GenerationResult>>? azureImage2Generator = null)
    {
        _azureImage2Generator = azureImage2Generator;
    }

    public async Task<ThumbnailV7Result> RenderAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, bool overwriteExisting, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailRoot);
        CleanFinalFiles(thumbnailRoot);
        var profile = _profileResolver.Resolve(request);
        var observation = _observationBuilder.Build(request, profile);
        var plan = _templatePlanner.Plan(profile, observation);
        var visualIntelligence = EventVisualIntelligence.From(request.ProductionContext?.ProductionEventIntelligence, observation);
        var heroComposition = HeroCompositionModel.From(request.ProductionContext?.ProductionEventIntelligence, observation);
        var galleryComposition = GalleryCompositionModel.From(request.ProductionContext?.ProductionEventIntelligence, observation);
        var backgroundPrompts = _promptBuilder.BuildVariants(visualIntelligence, heroComposition, galleryComposition);
        var composition = _composer.Compose(plan);
        var writes = new List<ThumbnailV7OutputWrite>();
        var sceneKey = "ThumbnailV7PerVariantBackground";
        var backgroundResults = new Dictionary<string, ThumbnailV7VariantBackground>(StringComparer.OrdinalIgnoreCase);
        var providerCalls = new List<ThumbnailV7ProviderCallLog>();
        var processingLogs = new List<ThumbnailV7ProcessingLog>();
        foreach (var variant in ThumbnailV7VariantRenderer.Variants)
        {
            var variantBackgroundPath = Path.Combine(thumbnailRoot, $"v7-background-{variant.Name}.png");
            var rawPath = Path.Combine(thumbnailRoot, $"thumbnail-{variant.Name}-raw.png");
            var normalizedVariantBackgroundPath = NormalizePath(variantBackgroundPath);
            var variantPrompt = backgroundPrompts[variant.Name];
            var promptHash = ComputeSha256Hex(Encoding.UTF8.GetBytes(variantPrompt));
            ThumbnailV7AzureImage2GenerationResult azureResult = new(false, false, 0, 0, null, null, null, null, null, null, "Azure Image2 generator was not provided to Thumbnail V7 renderer.");
            LogThumbnailV7BackgroundTrace(RendererName, variantPrompt, "Pending", normalizedVariantBackgroundPath, File.Exists(variantBackgroundPath), GetFileSize(variantBackgroundPath), sceneKey + ":" + variant.Name);
            if (_azureImage2Generator is not null)
            {
                var generationStartedUtc = DateTimeOffset.UtcNow;
                azureResult = await _azureImage2Generator(new ThumbnailV7AzureImage2Request(variant.Name, variantPrompt, variant.Width, variant.Height, rawPath), cancellationToken);
                var generationEndedUtc = DateTimeOffset.UtcNow;
                if (File.Exists(rawPath)) File.Copy(rawPath, variantBackgroundPath, overwrite: true);
                azureResult = azureResult with
                {
                    GenerationStartedUtc = azureResult.GenerationStartedUtc ?? generationStartedUtc,
                    GenerationEndedUtc = azureResult.GenerationEndedUtc ?? generationEndedUtc,
                    DurationMs = azureResult.DurationMs ?? Math.Max(0, (long)(generationEndedUtc - generationStartedUtc).TotalMilliseconds)
                };
            }
            var exists = File.Exists(variantBackgroundPath);
            var generated = azureResult.ProviderSucceeded && exists;
            LogThumbnailV7BackgroundTrace(RendererName, variantPrompt, azureResult.ProviderCalled ? (azureResult.ProviderSucceeded ? "Succeeded" : $"Failed: {azureResult.FailureReason}") : "NotCalled", generated ? normalizedVariantBackgroundPath : "procedural-fallback", exists, GetFileSize(variantBackgroundPath), sceneKey + ":" + variant.Name);
            backgroundResults[variant.Name] = new ThumbnailV7VariantBackground(variant.Name, variantBackgroundPath, rawPath, variantPrompt, promptHash, azureResult, generated, variant.Width, variant.Height);
            providerCalls.Add(ThumbnailV7ProviderCallLog.From(variant, variantPrompt, promptHash, azureResult));
            processingLogs.Add(new ThumbnailV7ProcessingLog(variant.Name, NormalizePath(rawPath), ["No post-processing before raw save"], []));

            var path = Path.Combine(thumbnailRoot, variant.FileName);
            await _renderer.RenderAsync(path, variant.Width, variant.Height, profile, observation, plan, composition, generated ? variantBackgroundPath : null, cancellationToken);
            processingLogs.Add(new ThumbnailV7ProcessingLog(variant.Name, NormalizePath(path), [], ["copy raw provider bytes to V7 background path", "resize/stretch background to final canvas", "canvas overlay composition", "save final PNG"]));
            writes.Add(new ThumbnailV7OutputWrite(path, RendererName));
        }

        File.Copy(Path.Combine(thumbnailRoot, "thumbnail-landscape.png"), Path.Combine(thumbnailRoot, "thumbnail-final.png"), overwrite: true);
        writes.Insert(0, new ThumbnailV7OutputWrite(Path.Combine(thumbnailRoot, "thumbnail-final.png"), RendererName));
        var verification = await WriteThumbnailVerificationArtifactsAsync(thumbnailRoot, backgroundResults, writes, providerCalls, processingLogs, cancellationToken);
        var validation = _validator.Validate(thumbnailRoot, plan, composition, writes, observation, backgroundResults);
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-v7-diagnostics.json");
        var promptPath = Path.Combine(thumbnailRoot, "thumbnail-prompt.json");
        await File.WriteAllTextAsync(promptPath, JsonSerializer.Serialize(new { thumbnailVersion = "V7", selectedRenderer = RendererName, renderer = RendererName, sceneKey, backgroundMode = "PerVariantAzureImage2", backgroundPrompts, azureImage2BackgroundOnly = true, backgroundPromptSource = "HeroGalleryEventVisualLogic", cropFromLandscape = false, vectorIconsUsed = true, emojiIconsUsed = false, forbiddenBackgroundContent = new[] { "text", "labels", "ui", "infographic elements", "dashboard cards", "widget panels", "extra celestial objects" }, backgrounds = backgroundResults.ToDictionary(kvp => kvp.Key, kvp => new { prompt = kvp.Value.Prompt, azureImage2Call = kvp.Value.AzureResult.ProviderCalled ? (kvp.Value.AzureResult.ProviderSucceeded ? "Succeeded" : "Failed") : "NotCalled", azureImage2OutputPath = NormalizePath(kvp.Value.Path), backgroundImagePath = kvp.Value.Generated ? NormalizePath(kvp.Value.Path) : "procedural-fallback", fileExists = File.Exists(kvp.Value.Path), fileSize = GetFileSize(kvp.Value.Path) }), layers = ThumbnailV7Plan.LayerNames, visualIntelligence, heroComposition, galleryComposition, profile, observation, plan }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        var outputFiles = writes.Select(w => NormalizePath(w.Path)).ToList();
        outputFiles.AddRange(backgroundResults.Values.Where(b => b.Generated).Select(b => NormalizePath(b.Path)));
        outputFiles.Add(NormalizePath(promptPath));
        outputFiles.Add(NormalizePath(diagnosticsPath));
        outputFiles.AddRange(verification.Select(NormalizePath));
        return new ThumbnailV7Result(outputFiles, diagnosticsPath, validation);
    }


    private static async Task<IReadOnlyList<string>> WriteThumbnailVerificationArtifactsAsync(string thumbnailRoot, IReadOnlyDictionary<string, ThumbnailV7VariantBackground> backgrounds, IReadOnlyList<ThumbnailV7OutputWrite> writes, IReadOnlyList<ThumbnailV7ProviderCallLog> providerCalls, IReadOnlyList<ThumbnailV7ProcessingLog> processingLogs, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(thumbnailRoot, "thumbnail-generation-metadata.json");
        var processingPath = Path.Combine(thumbnailRoot, "thumbnail-processing-log.json");
        var diffPath = Path.Combine(thumbnailRoot, "thumbnail-prompt-diff.md");
        var validationReasons = new List<string>();
        var expected = ThumbnailV7VariantRenderer.Variants.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var hashes = backgrounds.Values.Select(b => b.PromptHash).ToArray();
        if (hashes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hashes.Length) validationReasons.Add("any two aspect prompt hashes are identical");
        foreach (var bg in backgrounds.Values)
        {
            if (!expected.TryGetValue(bg.VariantName, out var variant) || bg.RequestedWidth != variant.Width || bg.RequestedHeight != variant.Height) validationReasons.Add($"{bg.VariantName} requested size is wrong");
            if (!File.Exists(bg.RawPath))
            {
                validationReasons.Add($"{bg.VariantName} raw provider output is missing");
            }
            else
            {
                using var image = Image.Load(bg.RawPath);
                if (image.Width != bg.RequestedWidth || image.Height != bg.RequestedHeight) validationReasons.Add($"{bg.VariantName} raw image dimensions do not match requested dimensions; expected {bg.RequestedWidth}x{bg.RequestedHeight}, got {image.Width}x{image.Height}");
            }
        }
        if (SameBytes(backgrounds.GetValueOrDefault("portrait")?.RawPath, backgrounds.GetValueOrDefault("landscape")?.RawPath)) validationReasons.Add("portrait is generated from landscape bytes");
        if (SameBytes(backgrounds.GetValueOrDefault("square")?.RawPath, backgrounds.GetValueOrDefault("landscape")?.RawPath)) validationReasons.Add("square is generated from landscape bytes");
        if (processingLogs.Any(l => l.RawSaveOperations.Any(op => !op.Equals("No post-processing before raw save", StringComparison.Ordinal)))) validationReasons.Add("hidden resize/crop happens before raw save");
        var metadata = backgrounds.Values.Select(bg => new { aspect = bg.VariantName, requestedWidth = bg.RequestedWidth, requestedHeight = bg.RequestedHeight, returnedWidth = ReadWidth(bg.RawPath), returnedHeight = ReadHeight(bg.RawPath), promptHash = bg.PromptHash, promptFilePath = NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-prompt.json")), rawImagePath = NormalizePath(bg.RawPath), finalImagePath = NormalizePath(Path.Combine(thumbnailRoot, $"thumbnail-{bg.VariantName}.png")), providerRequestId = bg.AzureResult.ProviderRequestId, generationDurationMs = bg.AzureResult.DurationMs ?? bg.AzureResult.AzureRequestMs + bg.AzureResult.ImageDownloadMs, generatedIndependently = !SameBytes(bg.RawPath, backgrounds.Values.FirstOrDefault(x => x.VariantName != bg.VariantName)?.RawPath) }).ToArray();
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(new { providerCalls, metadata, validation = new { status = validationReasons.Count == 0 ? "PASS" : "FAIL", reasons = validationReasons } }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(processingPath, JsonSerializer.Serialize(processingLogs, JsonOptions), cancellationToken);
        var unique = hashes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == hashes.Length;
        var lines = new List<string> { "# Thumbnail Prompt Diff", "", $"Validation: {(validationReasons.Count == 0 ? "PASS" : "FAIL")}", "", $"Hashes unique: {unique.ToString().ToLowerInvariant()}", "" };
        foreach (var bg in backgrounds.Values.OrderBy(b => b.VariantName)) lines.AddRange([ $"## {bg.VariantName}", $"- prompt hash: `{bg.PromptHash}`", $"- requested size: {bg.RequestedWidth}x{bg.RequestedHeight}", $"- key creative-directing phrases detected: {string.Join(", ", DetectCreativePhrases(bg.Prompt))}", "" ]);
        if (validationReasons.Count > 0) lines.AddRange(["## Failure Reasons", ..validationReasons.Select(r => $"- {r}")]);
        await File.WriteAllTextAsync(diffPath, string.Join(Environment.NewLine, lines), cancellationToken);
        return [metadataPath, processingPath, diffPath];
    }

    private static int? ReadWidth(string path) { if (!File.Exists(path)) return null; using var image = Image.Load(path); return image.Width; }
    private static int? ReadHeight(string path) { if (!File.Exists(path)) return null; using var image = Image.Load(path); return image.Height; }
    private static bool SameBytes(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && File.Exists(a) && File.Exists(b) && ComputeFileHash(a) == ComputeFileHash(b);
    private static string ComputeFileHash(string path) => ComputeSha256Hex(File.ReadAllBytes(path));
    private static string ComputeSha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string[] DetectCreativePhrases(string prompt) => new[] { "background-only", "cinematic", "twilight", "reserved", "overlay-safe", "National Geographic quality", "No text", "No labels", "No UI" }.Where(p => prompt.Contains(p, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static void LogThumbnailV7BackgroundTrace(string renderer, string backgroundPrompt, string azureImage2Call, string backgroundImagePath, bool fileExists, long fileSize, string sceneKey)
    {
        Console.WriteLine("[ThumbnailV7BackgroundTrace]");
        Console.WriteLine($"Renderer={renderer}");
        Console.WriteLine($"BackgroundPrompt={backgroundPrompt}");
        Console.WriteLine($"AzureImage2Call={azureImage2Call}");
        Console.WriteLine($"BackgroundImagePath={backgroundImagePath}");
        Console.WriteLine($"FileExists={fileExists.ToString().ToLowerInvariant()}");
        Console.WriteLine($"FileSize={fileSize}");
        Console.WriteLine($"SceneKey={sceneKey}");
    }

    private static long GetFileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void CleanFinalFiles(string root)
    {
        foreach (var file in new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png", "v7-background.png", "v7-background-landscape.png", "v7-background-portrait.png", "v7-background-square.png" })
        {
            var path = Path.Combine(root, file);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class ThumbnailV7InfographicRenderer : ThumbnailV7CinematicOverlayRenderer
{
    public ThumbnailV7InfographicRenderer(string celestialAssetsRoot = "assets/celestial") : base(celestialAssetsRoot) { }
}

public sealed class ThumbnailV7Engine : ThumbnailV7CinematicOverlayRenderer
{
    public ThumbnailV7Engine(string celestialAssetsRoot = "assets/celestial") : base(celestialAssetsRoot) { }
}

public sealed class ThumbnailV7ProfileResolver
{
    public ThumbnailV7Profile Resolve(ThumbnailAssetGenerationRequest request)
    {
        var raw = string.Join(' ', request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, request.ProductionContext?.ProductionEventIntelligence?.Title, request.ThumbnailStyle, request.EventId);
        var family = raw.Contains("meteor", StringComparison.OrdinalIgnoreCase) || raw.Contains("geminid", StringComparison.OrdinalIgnoreCase) ? "MeteorShower" :
            raw.Contains("eclipse", StringComparison.OrdinalIgnoreCase) ? "SolarEclipse" :
            raw.Contains("moon", StringComparison.OrdinalIgnoreCase) || raw.Contains("wolf", StringComparison.OrdinalIgnoreCase) ? "NamedFullMoon" :
            raw.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || raw.Contains("planet", StringComparison.OrdinalIgnoreCase) ? "PlanetConjunction" : "PlanetConjunction";
        return new ThumbnailV7Profile(family, family switch { "MeteorShower" => "METEOR SHOWER PEAK", "NamedFullMoon" => "FULL MOON OBSERVATION", "SolarEclipse" => "SOLAR ECLIPSE", _ => "PLANET CONJUNCTION" });
    }
}

public sealed class ThumbnailV7ObservationModelBuilder
{
    public ThumbnailV7Observation Build(ThumbnailAssetGenerationRequest request, ThumbnailV7Profile profile)
    {
        var intel = request.ProductionContext?.ProductionEventIntelligence;
        var title = CleanTitle(intel?.ShortTitle ?? intel?.Title ?? request.EventId.Replace('-', ' '));
        var direction = CleanDirection(intel?.SkyDirectionHint ?? (profile.Family == "PlanetConjunction" ? "near western horizon" : "open sky"));
        var objects = ResolveObjects(profile, intel).ToArray();
        var equipment = profile.Family == "SolarEclipse" ? "Certified solar filter" : profile.Family == "NamedFullMoon" ? "Eyes / binoculars" : "No telescope required";
        var date = intel?.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "Tonight";
        var best = FirstNonEmpty(intel?.BestViewingWindowLocal, intel?.PreferredViewingWindow, intel?.LocalPeakTime, "After sunset");
        var moon = intel?.MoonIlluminationPercent is { } illumination ? $"{illumination:0}% illuminated" : FirstNonEmpty(intel?.MoonInterference, "Low interference");
        var calloutMetric = intel?.AngularSeparationDegrees is { } sep ? $"{sep:0.#}° separation" : intel?.AltitudeDegrees is { } alt ? $"{alt:0.#}° altitude" : string.Empty;
        return new ThumbnailV7Observation(title, profile.Subtitle, direction, DirectionMarker(direction), objects, objects.Select(ToAssetKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), best, date, equipment, moon, calloutMetric);
    }

    private static IReadOnlyList<string> ResolveObjects(ThumbnailV7Profile profile, ProductionEventIntelligence? intel) => profile.Family switch
    {
        "PlanetConjunction" => EnsurePlanetConjunctionObjects(intel),
        "NamedFullMoon" => ["Moon"],
        "SolarEclipse" => ["Sun", "Moon", "Corona"],
        _ => ["Radiant"]
    };
    private static IReadOnlyList<string> EnsurePlanetConjunctionObjects(ProductionEventIntelligence? intel)
    {
        var objects = (intel?.ResolvedObjectNames ?? intel?.PrimaryObjects ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var jupiterVenusEvent = objects.Any(o => o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && objects.Any(o => o.Equals("Venus", StringComparison.OrdinalIgnoreCase));
        if (jupiterVenusEvent)
        {
            return objects.Where(o => o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || o.Equals("Venus", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
        }

        return objects
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }
    private static string CleanTitle(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').Trim().ToLowerInvariant());
    private static string CleanDirection(string value) => value.Trim();
    private static string DirectionMarker(string direction) => new[] { "WEST", "EAST", "SOUTH", "NORTH" }.FirstOrDefault(d => direction.Contains(d, StringComparison.OrdinalIgnoreCase)) ?? "SKY";
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static string ToAssetKey(string value) => value.ToLowerInvariant().Replace(" ", "-");
}

public sealed class ThumbnailV7TemplatePlanner
{
    public ThumbnailV7Plan Plan(ThumbnailV7Profile profile, ThumbnailV7Observation obs)
    {
        var cards = profile.Family switch
        {
            "MeteorShower" => new[] { new ThumbnailV7InfoCard("Date", obs.DateLabel), new("Best Time", obs.TimeLabel), new("Where To Look", obs.DirectionCue), new("Equipment", obs.Equipment), new("Moon Conditions", obs.MoonCondition) },
            "NamedFullMoon" => new[] { new ThumbnailV7InfoCard("Date", obs.DateLabel), new("Best Time", obs.TimeLabel), new("Direction", obs.DirectionCue), new("Moon Phase", "Full Moon"), new("Equipment", obs.Equipment) },
            "SolarEclipse" => new[] { new ThumbnailV7InfoCard("Date", obs.DateLabel), new("Best Time", obs.TimeLabel), new("Direction", obs.DirectionCue), new("Safety", "Certified solar filter"), new("Equipment", obs.Equipment) },
            _ => new[] { new ThumbnailV7InfoCard("Event Type", obs.Subtitle), new("Date", obs.DateLabel), new("Best Viewing Time", obs.TimeLabel), new("Direction", obs.DirectionCue), new("Objects Visible", string.Join(" + ", obs.ObjectNames)), new("Equipment", obs.Equipment) }
        };
        var template = profile.Family switch { "MeteorShower" => "MeteorShowerV7Template", "NamedFullMoon" => "NamedFullMoonV7Template", "SolarEclipse" => "SolarEclipseV7Template", _ => "PlanetConjunctionV7Template" };
        string[] footer = profile.Family switch
        {
            "MeteorShower" => ["Find dark skies", "Face the radiant", "Let eyes adapt"],
            "NamedFullMoon" => ["Find open horizon", "Watch near moonrise", "Binoculars optional"],
            "SolarEclipse" => ["Use certified solar filter", "Never look unfiltered", "Check local visibility"],
            _ => ["Find clear horizon", "Observe after sunset", "No telescope required"]
        };
        return new ThumbnailV7Plan(template, 32, 68, 5, cards, footer);
    }
}

public sealed class ThumbnailV7BackgroundPromptBuilder
{
    public IReadOnlyDictionary<string, string> BuildVariants(EventVisualIntelligence visualIntelligence, HeroCompositionModel heroComposition, GalleryCompositionModel galleryComposition)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["landscape"] = Build(visualIntelligence, heroComposition, galleryComposition, "landscape", "1280x720", "cinematic twilight astronomy scene; main celestial objects on the right or upper-right; left side reserved for a premium dark glass information panel; bottom reserved for footer text blocks"),
            ["portrait"] = Build(visualIntelligence, heroComposition, galleryComposition, "portrait", "1080x1920", "cinematic vertical astronomy scene; celestial objects in upper-middle or upper-right visual area; title safe at top; lower third reserved for info card; footer safe at bottom; no cropping from landscape"),
            ["square"] = Build(visualIntelligence, heroComposition, galleryComposition, "square", "1080x1080", "cinematic square astronomy scene; object focus center-right; lower-left or left reserved for info card; footer safe at bottom")
        };
    }

    public string Build(EventVisualIntelligence visualIntelligence, HeroCompositionModel heroComposition, GalleryCompositionModel galleryComposition)
        => Build(visualIntelligence, heroComposition, galleryComposition, "landscape", "1280x720", "cinematic twilight astronomy scene; main celestial objects on the right or upper-right; left side reserved for info panel; bottom reserved for footer");

    private string Build(EventVisualIntelligence visualIntelligence, HeroCompositionModel heroComposition, GalleryCompositionModel galleryComposition, string variantName, string dimensions, string layoutInstruction)
    {
        var objects = visualIntelligence.ObjectNames.Count == 0 ? "the event objects" : string.Join(" and ", visualIntelligence.ObjectNames);
        var skyDirection = FirstNonEmpty(visualIntelligence.SkyDirectionHint, heroComposition.DirectionCue, galleryComposition.HorizonCue, "event horizon");
        var mood = FirstNonEmpty(heroComposition.VisualMood, galleryComposition.VisualMood, visualIntelligence.VisualTheme, "premium astronomy photography");
        var title = FirstNonEmpty(visualIntelligence.ShortTitle, visualIntelligence.Title, "astronomy event");

        return string.Join(" ", new[]
        {
            $"Create a {dimensions} {variantName} background-only image: {DescribeSky(visualIntelligence.EventType, skyDirection)} for {title}.",
            layoutInstruction + ".",
            $"{objects} visible naturally near each other when part of the event story.",
            "For planet events, render Jupiter, Venus, Moon, or named planets as beautiful bright planet-like objects, not tiny star dots, and keep them only in the open visual area away from reserved overlay zones.",
            "Beautiful horizon and atmospheric depth.",
            mood + ".",
            "National Geographic quality.",
            "No text.",
            "No labels.",
            "No UI.",
            "Respect all reserved overlay-safe zones for this exact aspect ratio.",
            "Do not include UI, infographic panels, dashboard cards, widget panels, star-map graphics, or extra celestial objects."
        });
    }

    private static string DescribeSky(string eventType, string skyDirection)
    {
        if (eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || eventType.Contains("planet", StringComparison.OrdinalIgnoreCase))
            return skyDirection.Contains("west", StringComparison.OrdinalIgnoreCase) ? "twilight western sky" : $"twilight sky toward the {skyDirection}";
        if (eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "dark-sky meteor shower radiant";
        if (eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "eclipse-safe dramatic sky";
        if (eventType.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "moonrise horizon sky";
        return $"event-specific astronomy sky toward the {skyDirection}";
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class ThumbnailV7CinematicOverlayComposer
{
    public ThumbnailV7Composition Compose(ThumbnailV7Plan plan) => new(false, plan.InformationAreaPercent, plan.VisualAreaPercent);
}

public sealed class ThumbnailV7VariantRenderer
{
    public static readonly IReadOnlyList<ThumbnailV7Variant> Variants = [new("landscape", "thumbnail-landscape.png", 1280, 720), new("portrait", "thumbnail-portrait.png", 1080, 1920), new("square", "thumbnail-square.png", 1080, 1080)];
    public async Task RenderAsync(string path, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs, ThumbnailV7Plan plan, ThumbnailV7Composition composition, string? backgroundImagePath, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(width, height, Color.FromRgb(5, 9, 27));
        image.Mutate(ctx =>
        {
            if (!string.IsNullOrWhiteSpace(backgroundImagePath) && File.Exists(backgroundImagePath))
                DrawAzureBackgroundLayer(ctx, backgroundImagePath, width, height);
            else
                DrawBackgroundLayer(ctx, width, height, profile);
            DrawObservationCardLayer(ctx, width, height, profile, obs, plan);
            DrawFooterTipsLayer(ctx, width, height, plan);
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), cancellationToken);
    }
    private static void DrawAzureBackgroundLayer(IImageProcessingContext ctx, string backgroundImagePath, int width, int height)
    {
        using var background = Image.Load<Rgba32>(backgroundImagePath);
        background.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Stretch }));
        ctx.DrawImage(background, 1f);
        ctx.Fill(Color.Black.WithAlpha(.18f), new RectangularPolygon(0, 0, width, height));
    }
    private static void DrawBackgroundLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None, [new ColorStop(0, Color.FromRgb(5, 9, 30)), new ColorStop(.55f, Color.FromRgb(18, 45, 86)), new ColorStop(1, Color.FromRgb(232, 123, 74))]));
        for (var i = 0; i < 125; i++) ctx.Fill(Color.White.WithAlpha(i % 5 == 0 ? .9f : .45f), new EllipsePolygon((i * 89) % width, (i * 47) % (height * 58 / 100), 1 + i % 2));
        ctx.Fill(Color.FromRgb(16, 25, 34).WithAlpha(.92f), new RectangularPolygon(0, height * .76f, width, height * .24f));
        ctx.Fill(Color.FromRgb(2, 9, 17).WithAlpha(.88f), new RectangularPolygon(0, height * .82f, width, height * .18f));
        if (profile.Family == "MeteorShower") for (var i = 0; i < 9; i++) ctx.DrawLine(Color.White.WithAlpha(.72f), Math.Max(2, width / 360), new PointF(width * (.45f + i * .04f), height * (.16f + i % 3 * .07f)), new PointF(width * (.38f + i * .035f), height * (.24f + i % 3 * .07f)));
    }
    private static void DrawObservationCardLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs, ThumbnailV7Plan plan)
    {
        var margin = width * plan.SafeMarginPercent / 100f;
        var font = SystemFonts.Collection.Families.First();
        var titleFont = font.CreateFont(Math.Max(34, width / 25), FontStyle.Bold);
        var subtitleFont = font.CreateFont(Math.Max(16, width / 58), FontStyle.Bold);
        var labelFont = font.CreateFont(Math.Max(13, width / 82), FontStyle.Regular);
        var valueFont = font.CreateFont(Math.Max(16, width / 68), FontStyle.Bold);
        ctx.DrawText(obs.Title.ToUpperInvariant(), titleFont, Color.White, new PointF(margin, margin * .95f));
        ctx.DrawText(obs.Subtitle, subtitleFont, Color.FromRgb(134, 211, 255), new PointF(margin, margin * 2.05f));
        if (profile.Family == "PlanetConjunction") ctx.DrawText(obs.DirectionCue, labelFont, Color.FromRgb(255, 220, 150), new PointF(margin, margin * 2.58f));
        var cardW = width * .30f; var rowH = height * .063f; var cardH = rowH * plan.Cards.Count + margin * .9f; var y = height * .29f;
        DrawGlassPanel(ctx, margin, y, cardW, cardH);
        for (var i = 0; i < plan.Cards.Count; i++)
        {
            var rowY = y + margin * .42f + i * rowH;
            DrawInfoIcon(ctx, i, new PointF(margin * 1.38f, rowY + rowH * .28f), Math.Max(10, width / 92));
            ctx.DrawText(plan.Cards[i].Label.ToUpperInvariant(), labelFont, Color.FromRgb(79, 221, 255), new PointF(margin * 1.82f, rowY));
            ctx.DrawText(Trim(plan.Cards[i].Value, 26), valueFont, Color.White, new PointF(margin * 1.82f, rowY + rowH * .36f));
        }
        ctx.DrawText(obs.DirectionMarker, subtitleFont, Color.FromRgb(255, 214, 118), new PointF(width * .76f, height * .72f));
    }
    private static void DrawFooterTipsLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Plan plan)
    {
        var margin = width * plan.SafeMarginPercent / 100f;
        var font = SystemFonts.Collection.Families.First().CreateFont(Math.Max(15, width / 76), FontStyle.Bold);
        var y = height - margin * 1.36f;
        var panelY = height - margin * 1.95f;
        var panelH = margin * 1.05f;
        DrawGlassPanel(ctx, margin, panelY, width - margin * 2, panelH);
        var blockW = (width - margin * 2) / Math.Max(1, plan.FooterTips.Count);
        for (var i = 0; i < plan.FooterTips.Count; i++)
        {
            var x = margin + i * blockW + margin * .35f;
            DrawFooterIcon(ctx, i, new PointF(x, panelY + panelH * .52f), Math.Max(12, width / 78));
            ctx.DrawText(plan.FooterTips[i], font, Color.FromRgb(255, 220, 100), new PointF(x + margin * .55f, y));
        }
    }

    private static void DrawGlassPanel(IImageProcessingContext ctx, float x, float y, float w, float h)
    {
        var radius = Math.Min(w, h) * .12f;
        var fill = Color.FromRgb(2, 10, 24).WithAlpha(.74f);
        var stroke = Color.FromRgb(68, 219, 255).WithAlpha(.38f);
        ctx.Fill(fill, new RectangularPolygon(x + radius, y, w - radius * 2, h));
        ctx.Fill(fill, new RectangularPolygon(x, y + radius, w, h - radius * 2));
        ctx.Fill(fill, new EllipsePolygon(new PointF(x + radius, y + radius), radius));
        ctx.Fill(fill, new EllipsePolygon(new PointF(x + w - radius, y + radius), radius));
        ctx.Fill(fill, new EllipsePolygon(new PointF(x + radius, y + h - radius), radius));
        ctx.Fill(fill, new EllipsePolygon(new PointF(x + w - radius, y + h - radius), radius));
        ctx.Draw(stroke, Math.Max(2, w / 240), new RectangularPolygon(x + radius, y, w - radius * 2, h));
        ctx.Draw(stroke, Math.Max(2, w / 240), new RectangularPolygon(x, y + radius, w, h - radius * 2));
        ctx.DrawLine(Color.White.WithAlpha(.20f), Math.Max(1, w / 360), new PointF(x + radius, y + 2), new PointF(x + w - radius, y + 2));
    }
    private static void DrawInfoIcon(IImageProcessingContext ctx, int index, PointF center, float r)
    {
        var cyan = Color.FromRgb(68, 219, 255);
        var yellow = Color.FromRgb(255, 213, 93);
        ctx.Fill(Color.FromRgb(6, 23, 44).WithAlpha(.88f), new EllipsePolygon(center, r * 1.25f));
        ctx.Draw(cyan.WithAlpha(.82f), Math.Max(2, r / 4), new EllipsePolygon(center, r * 1.25f));
        if (index % 3 == 0)
            ctx.DrawLine(yellow, Math.Max(2, r / 3), new PointF(center.X, center.Y - r * .65f), new PointF(center.X, center.Y + r * .65f));
        else if (index % 3 == 1)
        {
            ctx.Draw(cyan, Math.Max(2, r / 4), new EllipsePolygon(center, r * .62f));
            ctx.DrawLine(yellow, Math.Max(2, r / 4), center, new PointF(center.X + r * .42f, center.Y - r * .34f));
        }
        else
            ctx.Fill(yellow, new EllipsePolygon(center, r * .45f));
    }
    private static void DrawFooterIcon(IImageProcessingContext ctx, int index, PointF center, float r)
    {
        var cyan = Color.FromRgb(68, 219, 255);
        var yellow = Color.FromRgb(255, 213, 93);
        ctx.Draw(cyan, Math.Max(2, r / 5), new EllipsePolygon(center, r));
        if (index % 3 == 0)
            ctx.DrawLine(yellow, Math.Max(2, r / 4), new PointF(center.X - r * .65f, center.Y), new PointF(center.X + r * .65f, center.Y));
        else if (index % 3 == 1)
            ctx.DrawLine(yellow, Math.Max(2, r / 4), center, new PointF(center.X + r * .45f, center.Y - r * .55f));
        else
            ctx.Fill(yellow, new EllipsePolygon(center, r * .34f));
    }
    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

public sealed class ThumbnailV7Validator
{
    public ThumbnailV7Diagnostics Validate(string root, ThumbnailV7Plan plan, ThumbnailV7Composition composition, IReadOnlyList<ThumbnailV7OutputWrite> writes, ThumbnailV7Observation observation, IReadOnlyDictionary<string, ThumbnailV7VariantBackground> backgrounds)
    {
        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" };
        var outputFiles = required.Select(file => Path.Combine(root, file).Replace('\\', '/')).ToArray();
        var missing = required.Where(file => !File.Exists(Path.Combine(root, file))).ToArray();
        var oldWriterDetected = writes.Any(w => !string.Equals(w.WriterComponent, ThumbnailV7CinematicOverlayRenderer.RendererName, StringComparison.Ordinal));
        var jupiterVenusEvent = observation.ObjectNames.Any(o => o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && observation.ObjectNames.Any(o => o.Equals("Venus", StringComparison.OrdinalIgnoreCase));
        var mercuryLeak = jupiterVenusEvent && observation.ObjectNames.Any(o => o.Equals("Mercury", StringComparison.OrdinalIgnoreCase));
        var backgroundGenerated = backgrounds.Values.Any(b => b.Generated);
        var backgroundFallbackUsed = backgrounds.Values.Any(b => !b.Generated);
        var azureImage2Error = string.Join("; ", backgrounds.Values.Select(b => b.AzureResult.FailureReason).Where(e => !string.IsNullOrWhiteSpace(e)));
        var valid = missing.Length == 0 && !oldWriterDetected && !mercuryLeak && !composition.OverlapDetected && composition.InformationAreaPercent <= 35 && composition.VisualAreaPercent >= 65 && !string.IsNullOrWhiteSpace(plan.SelectedTemplate) && !plan.DashboardCardsDetected && !plan.PreviewWidgetsDetected;
        if (!valid) throw new InvalidOperationException($"Thumbnail V7 validation failed: missing={string.Join(',', missing)}, oldWriterDetected={oldWriterDetected}, overlap={composition.OverlapDetected}, info={composition.InformationAreaPercent}, visual={composition.VisualAreaPercent}, template={plan.SelectedTemplate}");
        return new ThumbnailV7Diagnostics("V7", ThumbnailV7CinematicOverlayRenderer.RendererName, "ThumbnailV7Validator", "HeroGalleryEventVisualLogic", plan.SelectedTemplate, backgroundGenerated, backgroundFallbackUsed, string.IsNullOrWhiteSpace(azureImage2Error) ? null : azureImage2Error, backgrounds.GetValueOrDefault("landscape")?.Path.Replace('\\', '/'), backgrounds.GetValueOrDefault("landscape")?.Path.Replace('\\', '/'), backgrounds.GetValueOrDefault("portrait")?.Path.Replace('\\', '/'), backgrounds.GetValueOrDefault("square")?.Path.Replace('\\', '/'), "PerVariantAzureImage2", false, true, false, true, true, true, false, false, false, false, false, false, false, mercuryLeak, outputFiles, composition.InformationAreaPercent, composition.VisualAreaPercent, composition.OverlapDetected);
    }
}


public sealed record EventVisualIntelligence(string EventType, string Title, string ShortTitle, string SkyDirectionHint, IReadOnlyList<string> ObjectNames, string VisualTheme)
{
    public static EventVisualIntelligence From(ProductionEventIntelligence? intelligence, ThumbnailV7Observation observation)
        => new(
            intelligence?.EventType ?? observation.Subtitle,
            intelligence?.Title ?? observation.Title,
            intelligence?.ShortTitle ?? observation.Title,
            intelligence?.SkyDirectionHint ?? observation.DirectionCue,
            observation.ObjectNames.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            intelligence?.VisualTheme ?? intelligence?.StoryTheme ?? "Premium astronomy photography");
}

public sealed record HeroCompositionModel(string BackgroundPrompt, string DirectionCue, string VisualMood)
{
    public static HeroCompositionModel From(ProductionEventIntelligence? intelligence, ThumbnailV7Observation observation)
        => new(
            $"Cinematic astronomy hero visual for {intelligence?.Title ?? observation.Title}",
            intelligence?.SkyDirectionHint ?? observation.DirectionCue,
            intelligence?.VisualTheme ?? "Premium astronomy photography");
}

public sealed record GalleryCompositionModel(string HorizonCue, string VisualMood, IReadOnlyList<string> VisualMotifs)
{
    public static GalleryCompositionModel From(ProductionEventIntelligence? intelligence, ThumbnailV7Observation observation)
        => new(
            intelligence?.SkyDirectionHint ?? observation.DirectionCue,
            intelligence?.SkyGuideTheme ?? intelligence?.VisualTheme ?? "National Geographic quality",
            intelligence?.VisualMotifs ?? []);
}

public sealed record ThumbnailV7Result(IReadOnlyList<string> OutputFiles, string DiagnosticsPath, ThumbnailV7Diagnostics Diagnostics);
public sealed record ThumbnailV7Profile(string Family, string Subtitle);
public sealed record ThumbnailV7Observation(string Title, string Subtitle, string DirectionCue, string DirectionMarker, IReadOnlyList<string> ObjectNames, IReadOnlyList<string> AssetObjectKeys, string TimeLabel, string DateLabel, string Equipment, string MoonCondition, string CalloutMetric);
public sealed record ThumbnailV7Plan(string SelectedTemplate, int InformationAreaPercent, int VisualAreaPercent, int SafeMarginPercent, IReadOnlyList<ThumbnailV7InfoCard> Cards, IReadOnlyList<string> FooterTips)
{
    public static readonly IReadOnlyList<string> LayerNames = ["AzureImage2BackgroundLayer", "ObservationCardV7Layer", "FooterTipsLayer"];
    public bool DashboardCardsDetected => false;
    public bool PreviewWidgetsDetected => false;
}
public sealed record ThumbnailV7InfoCard(string Label, string Value);
public sealed record ThumbnailV7Composition(bool OverlapDetected, int InformationAreaPercent, int VisualAreaPercent);
public sealed record ThumbnailV7Variant(string Name, string FileName, int Width, int Height);
public sealed record ThumbnailV7OutputWrite(string Path, string WriterComponent);
public sealed record ThumbnailV7VariantBackground(string VariantName, string Path, string RawPath, string Prompt, string PromptHash, ThumbnailV7AzureImage2GenerationResult AzureResult, bool Generated, int RequestedWidth, int RequestedHeight);
public sealed record ThumbnailV7AzureImage2Request(string Aspect, string Prompt, int RequestedWidth, int RequestedHeight, string RawOutputPath);
public sealed record ThumbnailV7AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? ProviderName, string? ProviderModelName, string? ProviderRequestId, DateTimeOffset? GenerationStartedUtc, DateTimeOffset? GenerationEndedUtc, long? DurationMs, string? FailureReason);
public sealed record ThumbnailV7ProviderCallLog(string Aspect, string PromptHash, int PromptLength, string RequestedSize, string? ProviderName, string? ProviderModelName, string? ProviderRequestId, DateTimeOffset? GenerationStartUtc, DateTimeOffset? GenerationEndUtc, long? DurationMs)
{
    public static ThumbnailV7ProviderCallLog From(ThumbnailV7Variant variant, string prompt, string promptHash, ThumbnailV7AzureImage2GenerationResult result)
        => new(variant.Name, promptHash, prompt.Length, $"{variant.Width}x{variant.Height}", result.ProviderName, result.ProviderModelName, result.ProviderRequestId, result.GenerationStartedUtc, result.GenerationEndedUtc, result.DurationMs ?? result.AzureRequestMs + result.ImageDownloadMs);
}
public sealed record ThumbnailV7ProcessingLog(string Aspect, string Path, IReadOnlyList<string> RawSaveOperations, IReadOnlyList<string> PostProviderOperations);
public sealed record ThumbnailV7Diagnostics(string ThumbnailVersion, string SelectedRenderer, string Validator, string BackgroundPromptSource, string SelectedTemplate, bool BackgroundGenerated, bool BackgroundFallbackUsed, string? AzureImage2Error, string? BackgroundImagePath, string? LandscapeBackgroundPath, string? PortraitBackgroundPath, string? SquareBackgroundPath, string BackgroundMode, bool CropFromLandscape, bool VectorIconsUsed, bool EmojiIconsUsed, bool ObservationCardRendered, bool FooterRendered, bool OldValidationBlocked, bool ThumbnailReviewJsonRequired, bool ManualCelestialAssetPlacement, bool V6RendererExecuted, bool V6ValidatorExecuted, bool DashboardCardsAppear, bool ExtraObjectsDetected, bool V5RendererExecuted, bool MercuryAppears, IReadOnlyList<string> OutputFiles, int InformationAreaPercent, int VisualAreaPercent, bool OverlapDetected);
