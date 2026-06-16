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

        var eventObjectContext = BuildEventObjectContext(context);
        var selection = SelectBaseScene(context.SceneObservationContexts, eventObjectContext);
        var selectedImage = ResolveSelectedImage(selection.scene, context.SceneObservationContexts, candidates) ?? candidates[0];
        var variants = BuildTextVariants(context, eventObjectContext);

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
        var request = new AstronomyVisualCompositionRequest(
            _options.Width,
            _options.Height,
            text,
            string.Empty,
            string.Empty,
            ResolvePlanetAssetsFromImage(sourcePath),
            mood: "WarmTwilightThumbnail",
            starDensity: 520,
            showReferenceOverlays: false,
            backgroundImagePath: sourcePath,
            compositionMode: AstronomyVisualCompositionMode.Thumbnail);

        await AstronomyVisualCompositionEngine.ComposePngAsync(request, outputPath, cancellationToken);
    }

    private static IReadOnlyList<AstronomyVisualPlanetAsset> ResolvePlanetAssetsFromImage(string sourcePath)
    {
        var label = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(label)) label = "Featured planet";
        return [new AstronomyVisualPlanetAsset(ToSafeWords(label), sourcePath)];
    }

    private static EventObjectContext BuildEventObjectContext(AstronomyContext context)
    {
        var eventType = context.SpecialEvent?.EventType ?? "AstronomyEvent";
        var title = context.SpecialEvent?.EventTitle;
        var sceneObjects = context.SceneObservationContexts
            .Where(s => !IsGuideOnlyScene(s))
            .Select(s => s.ObjectName);
        var eventObjects = context.Events.Select(e => e.ObjectName);
        return EventObjectContextBuilder.FromJsonValues(eventType, title, [], sceneObjects, eventObjects, []);
    }

    private static (SceneObservationContext? scene, string objectName) SelectBaseScene(IReadOnlyCollection<SceneObservationContext> scenes, EventObjectContext eventObjectContext)
    {
        var approvedObjects = eventObjectContext.ObjectNames;
        var eventScene = scenes
            .Where(s => !IsGuideOnlyScene(s))
            .FirstOrDefault(s => approvedObjects.Contains(s.ObjectName, StringComparer.OrdinalIgnoreCase));
        if (eventScene is not null) return (eventScene, eventScene.ObjectName);

        var highestAltitude = scenes
            .Where(s => !IsGuideOnlyScene(s))
            .OrderByDescending(s => s.AltitudeDegrees ?? double.MinValue)
            .FirstOrDefault();
        if (highestAltitude is not null) return (highestAltitude, highestAltitude.ObjectName);

        return (scenes.FirstOrDefault(), eventObjectContext.PrimaryObjectName);
    }

    private static bool IsGuideOnlyScene(SceneObservationContext scene)
        => string.Equals(scene.ObjectType, "Overview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.SceneType, "Overview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.SceneType, "Tips", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.SceneType, "Closing", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveSelectedImage(SceneObservationContext? selectedScene, List<SceneObservationContext> orderedScenes, List<string> images)
    {
        if (selectedScene is null)
            return null;

        var index = orderedScenes.FindIndex(s => s.SceneId.Equals(selectedScene.SceneId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < images.Count ? images[index] : null;
    }

    private static List<string> BuildTextVariants(AstronomyContext context, EventObjectContext eventObjectContext)
    {
        var headline = ToThumbnailCopy(FirstNonEmpty(eventObjectContext.ObjectHeadlineText, context.SpecialEvent?.EventTitle, eventObjectContext.ObjectListText, "SKY EVENT"));
        var eventType = ToThumbnailCopy((context.SpecialEvent?.EventType ?? "").Replace("_", " ", StringComparison.OrdinalIgnoreCase));
        var eventCopy = string.IsNullOrWhiteSpace(eventType) || eventType.Equals(headline, StringComparison.OrdinalIgnoreCase)
            ? "RARE EVENT"
            : eventType;
        var objectCopy = ToThumbnailCopy(FirstNonEmpty(eventObjectContext.ObjectPairText, eventObjectContext.PrimaryObjectName, headline));

        return [headline, eventCopy, objectCopy]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(ToThumbnailCopy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .DefaultIfEmpty("SKY EVENT")
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ToThumbnailCopy(string text)
        => LimitWords(ToSafeWords(text), 6);

    private static string LimitWords(string text, int maxWords)
        => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(maxWords));

    private static string ToSafeWords(string text)
    {
        var words = text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6);
        return string.Join(' ', words);
    }
}
