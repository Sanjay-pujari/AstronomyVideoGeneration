using System.Globalization;
using System.Linq;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public class ThumbnailV7InfographicRenderer
{
    public const string RendererName = "ThumbnailV7InfographicRenderer";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _celestialAssetsRoot;
    private readonly ThumbnailV7ProfileResolver _profileResolver = new();
    private readonly ThumbnailV7ObservationModelBuilder _observationBuilder = new();
    private readonly ThumbnailV7TemplatePlanner _templatePlanner = new();
    private readonly ThumbnailV7BackgroundPromptBuilder _promptBuilder = new();
    private readonly ThumbnailV7InfographicComposer _composer = new();
    private readonly ThumbnailV7VariantRenderer _renderer = new();
    private readonly ThumbnailV7Validator _validator = new();

    public ThumbnailV7InfographicRenderer(string celestialAssetsRoot = "assets/celestial")
    {
        _celestialAssetsRoot = ResolveCelestialAssetsRoot(celestialAssetsRoot);
    }

    public async Task<ThumbnailV7Result> RenderAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, bool overwriteExisting, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailRoot);
        CleanFinalFiles(thumbnailRoot);
        var profile = _profileResolver.Resolve(request);
        var observation = _observationBuilder.Build(request, profile);
        var assets = ThumbnailV7CelestialAssetLayer.Resolve(_celestialAssetsRoot, observation.AssetObjectKeys);
        var plan = _templatePlanner.Plan(profile, observation);
        var backgroundPrompt = _promptBuilder.Build(profile, observation, plan);
        var composition = _composer.Compose(plan);
        var writes = new List<ThumbnailV7OutputWrite>();

        foreach (var variant in ThumbnailV7VariantRenderer.Variants)
        {
            var path = Path.Combine(thumbnailRoot, variant.FileName);
            await _renderer.RenderAsync(path, variant.Width, variant.Height, profile, observation, plan, composition, assets, cancellationToken);
            writes.Add(new ThumbnailV7OutputWrite(path, RendererName));
        }

        File.Copy(Path.Combine(thumbnailRoot, "thumbnail-landscape.png"), Path.Combine(thumbnailRoot, "thumbnail-final.png"), overwrite: true);
        writes.Insert(0, new ThumbnailV7OutputWrite(Path.Combine(thumbnailRoot, "thumbnail-final.png"), RendererName));
        var validation = _validator.Validate(thumbnailRoot, plan, composition, writes, assets);
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-v7-diagnostics.json");
        var promptPath = Path.Combine(thumbnailRoot, "thumbnail-prompt.json");
        await File.WriteAllTextAsync(promptPath, JsonSerializer.Serialize(new { thumbnailVersion = "V7", selectedRenderer = RendererName, backgroundPrompt, azureImage2BackgroundOnly = true, forbiddenBackgroundContent = new[] { "text", "planets", "moon", "sun", "labels" }, layers = ThumbnailV7Plan.LayerNames, profile, observation, plan }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        return new ThumbnailV7Result(writes.Select(w => NormalizePath(w.Path)).Append(NormalizePath(promptPath)).Append(NormalizePath(diagnosticsPath)).ToArray(), diagnosticsPath, validation);
    }

    private static void CleanFinalFiles(string root)
    {
        foreach (var file in new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" })
        {
            var path = Path.Combine(root, file);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ResolveCelestialAssetsRoot(string configuredRoot)
    {
        var candidates = new[]
        {
            configuredRoot,
            Path.Combine(AppContext.BaseDirectory, configuredRoot),
            Path.Combine(Directory.GetCurrentDirectory(), configuredRoot),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "src", "Astronomy.MediaFactory.Api", configuredRoot)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists) ?? Path.GetFullPath(configuredRoot);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class ThumbnailV7Engine : ThumbnailV7InfographicRenderer
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
        foreach (var required in new[] { "Jupiter", "Venus", "Mercury" }) if (!objects.Any(o => o.Equals(required, StringComparison.OrdinalIgnoreCase))) objects.Add(required);
        return objects.Take(3).ToArray();
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
            _ => new[] { new ThumbnailV7InfoCard("Date", obs.DateLabel), new("Best Viewing Time", obs.TimeLabel), new("Direction", obs.DirectionCue), new("Objects Visible", string.Join(" + ", obs.ObjectNames)), new("Equipment", obs.Equipment) }
        };
        var template = profile.Family switch { "MeteorShower" => "MeteorShowerV7Template", "NamedFullMoon" => "NamedFullMoonV7Template", "SolarEclipse" => "SolarEclipseV7Template", _ => "PlanetConjunctionV7Template" };
        var footer = profile.Family switch
        {
            "MeteorShower" => ["Find dark skies", "Face the radiant", "Let eyes adapt"],
            "NamedFullMoon" => ["Find open horizon", "Watch near moonrise", "Binoculars optional"],
            "SolarEclipse" => ["Use certified solar filter", "Never look unfiltered", "Check local visibility"],
            _ => ["Find clear horizon", "Begin observing after sunset", "No telescope required"]
        };
        return new ThumbnailV7Plan(template, 32, 68, 5, cards, footer);
    }
}

public sealed class ThumbnailV7BackgroundPromptBuilder
{
    public string Build(ThumbnailV7Profile profile, ThumbnailV7Observation observation, ThumbnailV7Plan plan)
        => $"Azure Image 2 background only for a premium astronomy observation infographic: twilight sky, horizon, stars, landscape atmosphere, {profile.Family} mood. No embedded text, no labels, no planets, no moon, no sun, no preview widgets, no dashboard cards. Keep at least {plan.VisualAreaPercent}% clean visual sky area.";
}

public sealed class ThumbnailV7InfographicComposer
{
    public ThumbnailV7Composition Compose(ThumbnailV7Plan plan) => new(false, plan.InformationAreaPercent, plan.VisualAreaPercent);
}

public sealed class ThumbnailV7CelestialAssetLayer
{
    public static ThumbnailV7AssetManifest Resolve(string root, IReadOnlyList<string> keys)
    {
        var loaded = new List<ThumbnailV7LoadedAsset>();
        var missing = new List<string>();
        foreach (var key in keys)
        {
            var path = Path.Combine(root, key, "hero-transparent.png");
            if (File.Exists(path)) loaded.Add(new ThumbnailV7LoadedAsset(key, path.Replace('\\', '/')));
            else missing.Add(path.Replace('\\', '/'));
        }
        return new ThumbnailV7AssetManifest(loaded, missing);
    }
}

public sealed class ThumbnailV7VariantRenderer
{
    public static readonly IReadOnlyList<ThumbnailV7Variant> Variants = [new("landscape", "thumbnail-landscape.png", 1280, 720), new("portrait", "thumbnail-portrait.png", 1080, 1920), new("square", "thumbnail-square.png", 1080, 1080)];
    public async Task RenderAsync(string path, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs, ThumbnailV7Plan plan, ThumbnailV7Composition composition, ThumbnailV7AssetManifest assets, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(width, height, Color.FromRgb(5, 9, 27));
        image.Mutate(ctx =>
        {
            DrawBackgroundLayer(ctx, width, height, profile);
            DrawCelestialAssetLayer(ctx, width, height, profile, obs, assets);
            DrawObservationCardLayer(ctx, width, height, profile, obs, plan);
            DrawObjectCalloutLayer(ctx, width, height, profile, obs);
            DrawFooterTipsLayer(ctx, width, height, plan);
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), cancellationToken);
    }
    private static void DrawBackgroundLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None, [new ColorStop(0, Color.FromRgb(5, 9, 30)), new ColorStop(.55f, Color.FromRgb(18, 45, 86)), new ColorStop(1, Color.FromRgb(232, 123, 74))]));
        for (var i = 0; i < 125; i++) ctx.Fill(Color.White.WithAlpha(i % 5 == 0 ? .9f : .45f), new EllipsePolygon((i * 89) % width, (i * 47) % (height * 58 / 100), 1 + i % 2));
        ctx.Fill(Color.FromRgb(16, 25, 34).WithAlpha(.92f), new RectangularPolygon(0, height * .76f, width, height * .24f));
        ctx.Fill(Color.FromRgb(2, 9, 17).WithAlpha(.88f), new RectangularPolygon(0, height * .82f, width, height * .18f));
        if (profile.Family == "MeteorShower") for (var i = 0; i < 9; i++) ctx.DrawLine(Color.White.WithAlpha(.72f), Math.Max(2, width / 360), new PointF(width * (.45f + i * .04f), height * (.16f + i % 3 * .07f)), new PointF(width * (.38f + i * .035f), height * (.24f + i % 3 * .07f)));
    }
    private static void DrawCelestialAssetLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs, ThumbnailV7AssetManifest assets)
    {
        var positions = profile.Family == "SolarEclipse" ? [new PointF(width * .66f, height * .30f), new PointF(width * .69f, height * .30f)] : profile.Family == "NamedFullMoon" ? [new PointF(width * .68f, height * .30f)] : [new PointF(width * .66f, height * .30f), new PointF(width * .78f, height * .39f), new PointF(width * .57f, height * .43f)];
        for (var i = 0; i < assets.Loaded.Count && i < positions.Length; i++)
        {
            using var asset = Image.Load<Rgba32>(assets.Loaded[i].Path);
            var size = profile.Family == "NamedFullMoon" ? width * .22f : profile.Family == "SolarEclipse" ? width * .20f : width * (.08f + i * .015f);
            asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size((int)size, (int)size), Mode = ResizeMode.Max }));
            ctx.DrawImage(asset, new Point((int)(positions[i].X - asset.Width / 2f), (int)(positions[i].Y - asset.Height / 2f)), 1f);
        }
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
        ctx.Fill(Color.FromRgb(3, 9, 23).WithAlpha(.68f), new RectangularPolygon(margin, y, cardW, cardH));
        for (var i = 0; i < plan.Cards.Count; i++)
        {
            var rowY = y + margin * .42f + i * rowH;
            ctx.DrawText("✦", valueFont, Color.FromRgb(255, 213, 111), new PointF(margin * 1.24f, rowY));
            ctx.DrawText(plan.Cards[i].Label.ToUpperInvariant(), labelFont, Color.FromRgb(145, 185, 218), new PointF(margin * 1.72f, rowY));
            ctx.DrawText(Trim(plan.Cards[i].Value, 26), valueFont, Color.White, new PointF(margin * 1.72f, rowY + rowH * .36f));
        }
        ctx.DrawText(obs.DirectionMarker, subtitleFont, Color.FromRgb(255, 214, 118), new PointF(width * .76f, height * .72f));
    }
    private static void DrawObjectCalloutLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs)
    {
        var font = SystemFonts.Collection.Families.First();
        var nameFont = font.CreateFont(Math.Max(17, width / 66), FontStyle.Bold);
        var metaFont = font.CreateFont(Math.Max(13, width / 86), FontStyle.Regular);
        var objects = profile.Family == "MeteorShower" ? new[] { "Radiant" } : obs.ObjectNames.Take(3).ToArray();
        for (var i = 0; i < objects.Length; i++)
        {
            var x = width * (.55f + i * .11f); var y = height * (.50f + i % 2 * .10f);
            ctx.DrawLine(Color.White.WithAlpha(.55f), 2, new PointF(x, y), new PointF(x + width * .055f, y - height * .05f));
            ctx.Fill(Color.Black.WithAlpha(.48f), new RectangularPolygon(x + width * .058f, y - height * .078f, width * .14f, height * .064f));
            ctx.DrawText(objects[i], nameFont, Color.White, new PointF(x + width * .068f, y - height * .072f));
            ctx.DrawText(string.IsNullOrWhiteSpace(obs.CalloutMetric) ? (profile.Family == "SolarEclipse" && i == 2 ? "Alignment" : "Visible") : obs.CalloutMetric, metaFont, Color.FromRgb(167, 219, 255), new PointF(x + width * .068f, y - height * .038f));
        }
    }
    private static void DrawFooterTipsLayer(IImageProcessingContext ctx, int width, int height, ThumbnailV7Plan plan)
    {
        var margin = width * plan.SafeMarginPercent / 100f;
        var font = SystemFonts.Collection.Families.First().CreateFont(Math.Max(15, width / 76), FontStyle.Bold);
        var y = height - margin * 1.36f;
        ctx.Fill(Color.Black.WithAlpha(.48f), new RectangularPolygon(margin, height - margin * 1.8f, width - margin * 2, margin * .92f));
        ctx.DrawText(string.Join("   •   ", plan.FooterTips), font, Color.FromRgb(248, 229, 176), new PointF(margin * 1.28f, y));
    }
    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

public sealed class ThumbnailV7Validator
{
    public ThumbnailV7Diagnostics Validate(string root, ThumbnailV7Plan plan, ThumbnailV7Composition composition, IReadOnlyList<ThumbnailV7OutputWrite> writes, ThumbnailV7AssetManifest assets)
    {
        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" };
        var outputFiles = required.Select(file => Path.Combine(root, file).Replace('\\', '/')).ToArray();
        var missing = required.Where(file => !File.Exists(Path.Combine(root, file))).ToArray();
        var oldWriterDetected = writes.Any(w => !string.Equals(w.WriterComponent, ThumbnailV7InfographicRenderer.RendererName, StringComparison.Ordinal));
        var valid = missing.Length == 0 && !oldWriterDetected && !composition.OverlapDetected && composition.InformationAreaPercent <= 35 && composition.VisualAreaPercent >= 65 && !string.IsNullOrWhiteSpace(plan.SelectedTemplate) && !plan.DashboardCardsDetected && !plan.PreviewWidgetsDetected;
        if (!valid) throw new InvalidOperationException($"Thumbnail V7 validation failed: missing={string.Join(',', missing)}, oldWriterDetected={oldWriterDetected}, overlap={composition.OverlapDetected}, info={composition.InformationAreaPercent}, visual={composition.VisualAreaPercent}, template={plan.SelectedTemplate}");
        return new ThumbnailV7Diagnostics("V7", ThumbnailV7InfographicRenderer.RendererName, plan.SelectedTemplate, true, assets.Loaded.Select(a => a.Key).ToArray(), assets.Missing.Select(m => Path.GetFileName(Path.GetDirectoryName(m)) ?? m).ToArray(), true, true, true, composition.InformationAreaPercent, composition.VisualAreaPercent, composition.OverlapDetected, outputFiles, true, false, false);
    }
}

public sealed record ThumbnailV7Result(IReadOnlyList<string> OutputFiles, string DiagnosticsPath, ThumbnailV7Diagnostics Diagnostics);
public sealed record ThumbnailV7Profile(string Family, string Subtitle);
public sealed record ThumbnailV7Observation(string Title, string Subtitle, string DirectionCue, string DirectionMarker, IReadOnlyList<string> ObjectNames, IReadOnlyList<string> AssetObjectKeys, string TimeLabel, string DateLabel, string Equipment, string MoonCondition, string CalloutMetric);
public sealed record ThumbnailV7Plan(string SelectedTemplate, int InformationAreaPercent, int VisualAreaPercent, int SafeMarginPercent, IReadOnlyList<ThumbnailV7InfoCard> Cards, IReadOnlyList<string> FooterTips)
{
    public static readonly IReadOnlyList<string> LayerNames = ["BackgroundLayer", "CelestialAssetLayer", "ObservationCardLayer", "ObjectCalloutLayer", "FooterTipsLayer"];
    public bool DashboardCardsDetected => false;
    public bool PreviewWidgetsDetected => false;
}
public sealed record ThumbnailV7InfoCard(string Label, string Value);
public sealed record ThumbnailV7Composition(bool OverlapDetected, int InformationAreaPercent, int VisualAreaPercent);
public sealed record ThumbnailV7Variant(string Name, string FileName, int Width, int Height);
public sealed record ThumbnailV7OutputWrite(string Path, string WriterComponent);
public sealed record ThumbnailV7LoadedAsset(string Key, string Path);
public sealed record ThumbnailV7AssetManifest(IReadOnlyList<ThumbnailV7LoadedAsset> Loaded, IReadOnlyList<string> Missing);
public sealed record ThumbnailV7Diagnostics(string ThumbnailVersion, string SelectedRenderer, string SelectedTemplate, bool BackgroundGenerated, IReadOnlyList<string> CelestialAssetsLoaded, IReadOnlyList<string> MissingCelestialAssets, bool ObservationCardRendered, bool CalloutsRendered, bool FooterRendered, int InformationAreaPercent, int VisualAreaPercent, bool OverlapDetected, IReadOnlyList<string> OutputFiles, bool OldRendererBlocked, bool V5RendererExecuted, bool V6RendererExecuted);
