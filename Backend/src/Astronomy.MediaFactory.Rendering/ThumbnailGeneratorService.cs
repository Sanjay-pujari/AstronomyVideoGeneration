using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailGeneratorService : IThumbnailGeneratorService
{
    private static readonly HashSet<string> PriorityPlanets = new(StringComparer.OrdinalIgnoreCase) { "Venus", "Jupiter", "Saturn" };
    private readonly ThumbnailOptions _options;
    private readonly ILogger<ThumbnailGeneratorService> _logger;

    public ThumbnailGeneratorService(IOptions<ThumbnailOptions> options, ILogger<ThumbnailGeneratorService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<string>> GenerateAsync(AstronomyContext context, IReadOnlyCollection<string> screenshots, string outputDirectory, string narrationContext, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return [];

        var candidates = screenshots.Where(File.Exists).ToList();
        if (candidates.Count == 0)
            return [];

        var thumbnailsDirectory = System.IO.Path.Combine(outputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);

        var selection = SelectBaseScene(context.SceneObservationContexts);
        var selectedImage = ResolveSelectedImage(selection.scene, context.SceneObservationContexts, candidates) ?? candidates[0];
        var variants = BuildTextVariants(context, selection.objectName, narrationContext);

        var outputs = new List<string>(3);
        for (var i = 0; i < variants.Count; i++)
        {
            var output = System.IO.Path.Combine(thumbnailsDirectory, $"thumbnail-{i + 1}.png");
            await RenderAsync(selectedImage, output, variants[i], cancellationToken);
            outputs.Add(output);
        }

        var diagnosticsPath = System.IO.Path.Combine(thumbnailsDirectory, "thumbnail-selection.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            selectedImage,
            @object = selection.objectName,
            variants
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        _logger.LogInformation("Generated {Count} thumbnails at {Path}", outputs.Count, thumbnailsDirectory);
        return outputs;
    }

    private async Task RenderAsync(string sourcePath, string outputPath, string text, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size(_options.Width, _options.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center });
            ctx.Fill(new LinearGradientBrush(new PointF(0, _options.Height * 0.5f), new PointF(0, _options.Height), GradientRepetitionMode.None,
                new ColorStop(0, Color.Transparent), new ColorStop(1, Color.Black.WithAlpha(0.75f))), new RectangleF(0, _options.Height * 0.45f, _options.Width, _options.Height * 0.55f));

            var font = SystemFonts.CreateFont("Arial", 86, FontStyle.Bold);
            var origin = new PointF(64, _options.Height - 160);
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(origin.X + 4, origin.Y + 4) }, text, Color.Black.WithAlpha(0.85f));
            ctx.DrawText(new RichTextOptions(font) { Origin = origin }, text, Color.White);
        });

        await image.SaveAsPngAsync(outputPath, cancellationToken);
    }

    private static (SceneObservationContext? scene, string objectName) SelectBaseScene(IReadOnlyCollection<SceneObservationContext> scenes)
    {
        var moon = scenes.FirstOrDefault(s => string.Equals(s.ObjectName, "Moon", StringComparison.OrdinalIgnoreCase));
        if (moon is not null) return (moon, "Moon");

        var brightPlanet = scenes.FirstOrDefault(s => PriorityPlanets.Contains(s.ObjectName));
        if (brightPlanet is not null) return (brightPlanet, brightPlanet.ObjectName);

        var highestAltitude = scenes.OrderByDescending(s => s.AltitudeDegrees ?? double.MinValue).FirstOrDefault();
        if (highestAltitude is not null) return (highestAltitude, highestAltitude.ObjectName);

        var overview = scenes.FirstOrDefault(s => string.Equals(s.ObjectType, "Overview", StringComparison.OrdinalIgnoreCase));
        return (overview, overview?.ObjectName ?? "Overview");
    }

    private static string? ResolveSelectedImage(SceneObservationContext? selectedScene, List<SceneObservationContext> orderedScenes, List<string> images)
    {
        if (selectedScene is null)
            return null;

        var index = orderedScenes.FindIndex(s => s.SceneId.Equals(selectedScene.SceneId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < images.Count ? images[index] : null;
    }

    private static List<string> BuildTextVariants(AstronomyContext context, string objectName, string narrationContext)
    {
        var planetCount = context.SceneObservationContexts.Count(s => PriorityPlanets.Contains(s.ObjectName));
        var v1 = "TONIGHT'S SKY";
        var v2 = planetCount > 0 ? $"{planetCount} PLANETS VISIBLE" : $"{ToSafeWords(objectName)} TONIGHT";
        var direction = context.SceneObservationContexts.OrderByDescending(x => x.AltitudeDegrees ?? double.MinValue).FirstOrDefault()?.DirectionLabel;
        var v3 = !string.IsNullOrWhiteSpace(direction) ? $"LOOK {direction.ToUpperInvariant()} TONIGHT" : "LOOK UP TONIGHT";

        return [ToSafeWords(v1), ToSafeWords(v2), ToSafeWords(v3)];
    }

    private static string ToSafeWords(string text)
    {
        var words = text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4);
        return string.Join(' ', words);
    }
}
