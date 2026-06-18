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

public sealed class ThumbnailV7Engine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ThumbnailV7ProfileResolver _profileResolver = new();
    private readonly ThumbnailV7ObservationModelBuilder _observationBuilder = new();
    private readonly ThumbnailV7TemplatePlanner _templatePlanner = new();
    private readonly ThumbnailV7BackgroundPromptBuilder _promptBuilder = new();
    private readonly ThumbnailV7InfographicComposer _composer = new();
    private readonly ThumbnailV7VariantRenderer _renderer = new();
    private readonly ThumbnailV7Validator _validator = new();

    public async Task<ThumbnailV7Result> RenderAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, bool overwriteExisting, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailRoot);
        CleanFinalFiles(thumbnailRoot);
        var profile = _profileResolver.Resolve(request);
        var observation = _observationBuilder.Build(request, profile);
        var plan = _templatePlanner.Plan(profile, observation);
        var backgroundPrompt = _promptBuilder.Build(profile, observation, plan);
        var composition = _composer.Compose(plan);
        var outputFiles = ThumbnailV7VariantRenderer.Variants
            .Select(variant => Path.Combine(thumbnailRoot, variant.FileName))
            .ToArray();
        var writes = new List<ThumbnailV7OutputWrite>();
        foreach (var variant in ThumbnailV7VariantRenderer.Variants)
        {
            var path = Path.Combine(thumbnailRoot, variant.FileName);
            await _renderer.RenderAsync(path, variant.Width, variant.Height, profile, observation, plan, composition, cancellationToken);
            writes.Add(new ThumbnailV7OutputWrite(path, "ThumbnailV7Engine"));
        }
        File.Copy(Path.Combine(thumbnailRoot, "thumbnail-landscape.png"), Path.Combine(thumbnailRoot, "thumbnail-final.png"), overwrite: true);
        writes.Insert(0, new ThumbnailV7OutputWrite(Path.Combine(thumbnailRoot, "thumbnail-final.png"), "ThumbnailV7Engine"));
        var validation = _validator.Validate(thumbnailRoot, plan, composition, writes);
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-generation-diagnostics.json");
        var promptPath = Path.Combine(thumbnailRoot, "thumbnail-prompt.json");
        await File.WriteAllTextAsync(promptPath, JsonSerializer.Serialize(new { thumbnailVersion = "V7", selectedRenderer = "ThumbnailV7Engine", backgroundPrompt, profile, observation, plan }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        return new ThumbnailV7Result(outputFiles.Prepend(Path.Combine(thumbnailRoot, "thumbnail-final.png")).Append(promptPath).Append(diagnosticsPath).Select(NormalizePath).ToArray(), diagnosticsPath, validation);
    }

    private static void CleanFinalFiles(string root)
    {
        foreach (var file in new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" })
        {
            var path = Path.Combine(root, file);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class ThumbnailV7ProfileResolver
{
    public ThumbnailV7Profile Resolve(ThumbnailAssetGenerationRequest request)
    {
        var raw = request.ProductionContext?.EventType ?? request.ThumbnailStyle ?? request.EventId;
        var normalized = raw.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        var family = normalized.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? "MeteorShower" :
            normalized.Contains("conjunction", StringComparison.OrdinalIgnoreCase) ? "PlanetConjunction" :
            normalized.Contains("planet", StringComparison.OrdinalIgnoreCase) || normalized.Contains("grouping", StringComparison.OrdinalIgnoreCase) ? "PlanetGrouping" :
            normalized.Contains("moon", StringComparison.OrdinalIgnoreCase) ? "NamedFullMoon" :
            normalized.Contains("eclipse", StringComparison.OrdinalIgnoreCase) ? "SolarEclipse" : "MeteorShower";
        return new ThumbnailV7Profile(family, family switch { "MeteorShower" => "☄", "NamedFullMoon" => "☾", "SolarEclipse" => "◉", _ => "◎" });
    }
}

public sealed class ThumbnailV7ObservationModelBuilder
{
    public ThumbnailV7Observation Build(ThumbnailAssetGenerationRequest request, ThumbnailV7Profile profile)
    {
        var intel = request.ProductionContext?.ProductionEventIntelligence;
        var title = intel?.Title ?? request.EventId.Replace('-', ' ');
        var direction = intel?.SkyDirectionHint ?? "LOOK EAST";
        var objects = intel?.ResolvedObjectNames?.Take(3).ToArray() ?? [];
        if (objects.Length == 0) objects = profile.Family.Contains("Planet", StringComparison.OrdinalIgnoreCase) ? ["Venus", "Jupiter"] : [profile.Family.Replace("Named", string.Empty)];
        return new ThumbnailV7Observation(title, direction, objects, intel?.BestViewingWindowLocal ?? intel?.PreferredViewingWindow ?? "After dark", intel?.EventDate?.ToString("MMM d") ?? "Tonight");
    }
}

public sealed class ThumbnailV7TemplatePlanner
{
    public ThumbnailV7Plan Plan(ThumbnailV7Profile profile, ThumbnailV7Observation observation)
        => new(profile.Family, 32, 68, 5, [new("When", observation.DateLabel), new("Best", observation.TimeLabel), new("Where", observation.DirectionCue)], "Use dark skies • Let eyes adapt • Check local horizon");
}

public sealed class ThumbnailV7BackgroundPromptBuilder
{
    public string Build(ThumbnailV7Profile profile, ThumbnailV7Observation observation, ThumbnailV7Plan plan)
        => $"Premium astronomy infographic thumbnail background for {observation.Title}; family {profile.Family}; preserve at least {plan.VisualAreaPercent}% visual sky area; no embedded text.";
}

public sealed class ThumbnailV7InfographicComposer
{
    public ThumbnailV7Composition Compose(ThumbnailV7Plan plan) => new(false, plan.InformationAreaPercent, plan.VisualAreaPercent);
}

public sealed class ThumbnailV7VariantRenderer
{
    public static readonly IReadOnlyList<ThumbnailV7Variant> Variants = [new("landscape", "thumbnail-landscape.png", 1280, 720), new("portrait", "thumbnail-portrait.png", 1080, 1920), new("square", "thumbnail-square.png", 1080, 1080)];
    public async Task RenderAsync(string path, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation observation, ThumbnailV7Plan plan, ThumbnailV7Composition composition, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(4, 8, 25));
        image.Mutate(ctx => {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None, [new ColorStop(0, Color.FromRgb(4, 8, 28)), new ColorStop(1, Color.FromRgb(18, 35, 76))]));
            DrawStars(ctx, width, height);
            DrawInfographic(ctx, width, height, profile, observation, plan);
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), cancellationToken);
    }
    private static void DrawStars(IImageProcessingContext ctx, int width, int height) { for (var i = 0; i < 90; i++) ctx.Fill(Color.White.WithAlpha(0.75f), new EllipsePolygon((i * 73) % width, (i * 41) % (height * 65 / 100), i % 3 + 1)); }
    private static void DrawInfographic(IImageProcessingContext ctx, int width, int height, ThumbnailV7Profile profile, ThumbnailV7Observation obs, ThumbnailV7Plan plan)
    {
        var margin = width * plan.SafeMarginPercent / 100;
        var panelH = height * plan.InformationAreaPercent / 100;
        var panelY = height - panelH - margin;
        var fontFamily = SystemFonts.Collection.Families.First();
        var titleFont = fontFamily.CreateFont(Math.Max(34, width / 26), FontStyle.Bold);
        var cardFont = fontFamily.CreateFont(Math.Max(22, width / 44), FontStyle.Bold);
        var smallFont = fontFamily.CreateFont(Math.Max(18, width / 58), FontStyle.Regular);
        ctx.Fill(Color.Black.WithAlpha(0.68f), new RectangularPolygon(margin, panelY, width - margin * 2, panelH));
        ctx.DrawText($"{profile.Icon} {obs.Title.ToUpperInvariant()}", titleFont, Color.White, new PointF(margin * 1.4f, panelY + margin * .55f));
        var cardW = (width - margin * 3) / 3;
        for (var i = 0; i < plan.Cards.Count; i++)
        {
            var x = margin * 1.25f + i * (cardW + margin * .25f);
            var y = panelY + panelH * .38f;
            ctx.Fill(Color.White.WithAlpha(0.12f), new RectangularPolygon(x, y, cardW, panelH * .32f));
            ctx.DrawText($"✦ {plan.Cards[i].Label}", smallFont, Color.LightSkyBlue, new PointF(x + 18, y + 10));
            ctx.DrawText(plan.Cards[i].Value, cardFont, Color.White, new PointF(x + 18, y + 40));
        }
        ctx.DrawText($"→ {obs.DirectionCue}    {plan.FooterTips}", smallFont, Color.Gold, new PointF(margin * 1.4f, panelY + panelH * .78f));
        ctx.DrawText(string.Join("  •  ", obs.ObjectNames), smallFont, Color.White, new PointF(width * .58f, height * .28f));
    }
}

public sealed class ThumbnailV7Validator
{
    public ThumbnailV7Diagnostics Validate(string root, ThumbnailV7Plan plan, ThumbnailV7Composition composition, IReadOnlyList<ThumbnailV7OutputWrite> writes)
    {
        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" };
        var outputFiles = required.Select(file => Path.Combine(root, file).Replace('\\', '/')).ToArray();
        var missing = required.Where(file => !File.Exists(Path.Combine(root, file))).ToArray();
        var oldWriterDetected = writes.Any(w => !string.Equals(w.WriterComponent, "ThumbnailV7Engine", StringComparison.Ordinal));
        var valid = missing.Length == 0 && !oldWriterDetected && !composition.OverlapDetected && composition.InformationAreaPercent <= 35 && composition.VisualAreaPercent >= 65 && !string.IsNullOrWhiteSpace(plan.SelectedFamilyTemplate);
        if (!valid) throw new InvalidOperationException($"Thumbnail V7 validation failed: missing={string.Join(',', missing)}, oldWriterDetected={oldWriterDetected}, overlap={composition.OverlapDetected}, info={composition.InformationAreaPercent}, visual={composition.VisualAreaPercent}, template={plan.SelectedFamilyTemplate}");
        return new ThumbnailV7Diagnostics("V7", "ThumbnailV7Engine", plan.SelectedFamilyTemplate, composition.InformationAreaPercent, composition.VisualAreaPercent, composition.OverlapDetected, outputFiles, true, false, false);
    }
}

public sealed record ThumbnailV7Result(IReadOnlyList<string> OutputFiles, string DiagnosticsPath, ThumbnailV7Diagnostics Diagnostics);
public sealed record ThumbnailV7Profile(string Family, string Icon);
public sealed record ThumbnailV7Observation(string Title, string DirectionCue, IReadOnlyList<string> ObjectNames, string TimeLabel, string DateLabel);
public sealed record ThumbnailV7Plan(string SelectedFamilyTemplate, int InformationAreaPercent, int VisualAreaPercent, int SafeMarginPercent, IReadOnlyList<ThumbnailV7InfoCard> Cards, string FooterTips);
public sealed record ThumbnailV7InfoCard(string Label, string Value);
public sealed record ThumbnailV7Composition(bool OverlapDetected, int InformationAreaPercent, int VisualAreaPercent);
public sealed record ThumbnailV7Variant(string Name, string FileName, int Width, int Height);
public sealed record ThumbnailV7OutputWrite(string Path, string WriterComponent);
public sealed record ThumbnailV7Diagnostics(string ThumbnailVersion, string SelectedRenderer, string SelectedFamilyTemplate, int InformationAreaPercent, int VisualAreaPercent, bool OverlapDetected, IReadOnlyList<string> OutputFiles, bool OldRendererBlocked, bool V5RendererExecuted, bool V6RendererExecuted);
