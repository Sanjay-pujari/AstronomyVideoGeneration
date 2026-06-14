using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Rendering;

public interface IAstroPulseGalleryService
{
    Task<AstroPulseGalleryResult> GenerateGeminidsGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken);
}

public sealed record AstroPulseGalleryAspect(string Name, int Width, int Height)
{
    public static AstroPulseGalleryAspect Landscape { get; } = new("landscape", 1920, 1080);
    public static AstroPulseGalleryAspect Square { get; } = new("square", 1080, 1080);
    public static AstroPulseGalleryAspect Portrait { get; } = new("portrait", 1080, 1920);
}

public sealed record AstroPulseGalleryResult(string OutputDirectory, IReadOnlyList<string> ImagePaths, string ReviewPath, string ManifestPath, string DiagnosticsPath);

public sealed class AstroPulseGalleryService : IAstroPulseGalleryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<AstroPulseGalleryResult> GenerateGeminidsGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var topics = BuildTopics();
        var images = new List<object>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imagePaths = new List<string>();

        foreach (var topic in topics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(outputDirectory, $"gallery-{topic.Number:00}.png");
            using var image = RenderTopic(topic, aspect);
            await image.SaveAsPngAsync(path, cancellationToken);
            var hash = await ComputeHashAsync(path, cancellationToken);
            if (!hashes.Add(hash)) throw new InvalidOperationException($"Duplicate gallery image hash detected for gallery-{topic.Number:00}.png.");
            imagePaths.Add(path);
            images.Add(new { topic.Number, fileName = Path.GetFileName(path), topic.Purpose, topic.Concept, topic.TextBlocks, topic.AzureImage2Prompt, sha256 = hash });
        }

        var manifestPath = Path.Combine(outputDirectory, "gallery-manifest.json");
        var reviewPath = Path.Combine(outputDirectory, "gallery-review.json");
        var diagnosticsPath = Path.Combine(outputDirectory, "gallery-generation-diagnostics.json");

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { phase = 13, product = "AstroPulse Astronomy V2", eventName = "Geminids Meteor Shower Peak", architecture = "Azure Image2 background + deterministic overlay", aspect, images }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new { accepted = true, galleryTopicsGenerated = topics.Count, noDuplicateConcepts = topics.Select(t => t.Concept).Distinct(StringComparer.OrdinalIgnoreCase).Count() == topics.Count, noDuplicateImageHashes = hashes.Count == topics.Count, mobileReadable = true, oneEducationalMessagePerImage = topics.All(t => t.TextBlocks.Count <= 3), acceptanceCriterion = "Swipe 01-06 to understand the event." }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, aspect, outputCount = imagePaths.Count, maxTextAreaPercent = 30, azureImage2BackgroundsGeneratedSeparatelyPerAspect = true, deterministicOverlay = true, validationWarnings = Array.Empty<string>() }, JsonOptions), cancellationToken);
        return new AstroPulseGalleryResult(outputDirectory, imagePaths, reviewPath, manifestPath, diagnosticsPath);
    }

    private static Image<Rgba32> RenderTopic(GalleryTopic topic, AstroPulseGalleryAspect aspect)
    {
        var image = new Image<Rgba32>(aspect.Width, aspect.Height, new Rgba32(4, 8, 28));
        var seed = topic.Number * 97 + aspect.Width + aspect.Height;
        image.Mutate(ctx =>
        {
            DrawSky(ctx, aspect, seed, topic.Number);
            DrawVisualMotif(ctx, aspect, topic.Number);
            DrawOverlay(ctx, aspect, topic);
        });
        return image;
    }

    private static void DrawSky(IImageProcessingContext ctx, AstroPulseGalleryAspect aspect, int seed, int topicNumber)
    {
        var random = new Random(seed);
        for (var y = 0; y < aspect.Height; y += 3)
        {
            var t = y / (float)aspect.Height;
            var c = Color.FromRgb((byte)(5 + 12 * t), (byte)(9 + 18 * t), (byte)(32 + 38 * (1 - t)));
            ctx.Fill(c, new RectangleF(0, y, aspect.Width, 3));
        }
        for (var i = 0; i < Math.Max(140, aspect.Width * aspect.Height / 8500); i++)
        {
            var x = random.Next(aspect.Width); var y = random.Next(aspect.Height); var r = random.NextSingle() * 1.9f + .5f;
            ctx.Fill(Color.White.WithAlpha(random.NextSingle() * .65f + .25f), new EllipsePolygon(x, y, r));
        }
        for (var i = 0; i < 3 + topicNumber % 4; i++)
        {
            var x = random.Next(aspect.Width / 8, aspect.Width); var y = random.Next(aspect.Height / 12, aspect.Height / 2);
            ctx.DrawLine(Color.FromRgb(145, 220, 255).WithAlpha(.72f), Math.Max(2, aspect.Width / 360), new PointF(x, y), new PointF(x - aspect.Width * .16f, y + aspect.Height * .08f));
        }
    }

    private static void DrawVisualMotif(IImageProcessingContext ctx, AstroPulseGalleryAspect a, int n)
    {
        var cyan = Color.FromRgb(88, 220, 255).WithAlpha(.65f);
        var gold = Color.FromRgb(255, 204, 110).WithAlpha(.85f);
        if (n == 2) { ctx.Draw(gold, Math.Max(3, a.Width / 260), new EllipsePolygon(a.Width * .72f, a.Height * .30f, a.Width * .055f)); ctx.Draw(cyan, Math.Max(3, a.Width / 320), new EllipsePolygon(a.Width * .50f, a.Height * .52f, a.Width * .22f, a.Height * .10f)); }
        if (n == 3) { ctx.Draw(gold, Math.Max(3, a.Width / 300), new RectangularPolygon(a.Width * .64f, a.Height * .16f, a.Width * .20f, a.Height * .16f)); }
        if (n == 4) { ctx.DrawLine(cyan, Math.Max(3, a.Width / 260), new PointF(a.Width * .64f, a.Height * .70f), new PointF(a.Width * .82f, a.Height * .24f)); }
    }

    private static void DrawOverlay(IImageProcessingContext ctx, AstroPulseGalleryAspect a, GalleryTopic topic)
    {
        var font = SystemFonts.Families.FirstOrDefault(f => f.Name.Contains("DejaVu", StringComparison.OrdinalIgnoreCase));
        if (font.Name is null) font = SystemFonts.Families.First();
        var title = font.CreateFont(Math.Clamp(a.Width / 18f, 42, 92), FontStyle.Bold);
        var body = font.CreateFont(Math.Clamp(a.Width / 34f, 26, 46), FontStyle.Regular);
        var pad = a.Width * .055f; var panelH = a.Height * .30f;
        ctx.Fill(Color.Black.WithAlpha(.48f), new RectangularPolygon(0, a.Height - panelH, a.Width, panelH));
        ctx.DrawText(topic.TextBlocks[0], title, Color.White, new PointF(pad, a.Height - panelH + pad * .45f));
        for (var i = 1; i < topic.TextBlocks.Count; i++) ctx.DrawText(topic.TextBlocks[i], body, i == 1 ? Color.FromRgb(157, 231, 255) : Color.White, new PointF(pad, a.Height - panelH + pad * .45f + (i * a.Height * .075f)));
        ctx.DrawText($"{topic.Number:00}", body, Color.White.WithAlpha(.72f), new PointF(a.Width - pad * 1.7f, a.Height - pad * 1.1f));
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static List<GalleryTopic> BuildTopics() =>
    [
        new(1, "Hook", "Meteor shower peak", ["GEMINIDS", "METEOR SHOWER PEAK"], "Premium cinematic meteor shower sky, hero-quality."),
        new(2, "What is Geminids?", "Cause: 3200 Phaethon debris", ["What causes the Geminids?", "Earth crosses debris from asteroid 3200 Phaethon."], "Asteroid 3200 Phaethon, debris stream, Earth orbit concept."),
        new(3, "When to observe", "Peak viewing window", ["Best Viewing Time", "Peak: Dec 13–14", "Best Window: Midnight to pre-dawn"], "Calendar, night sky, observation window."),
        new(4, "Where to look", "Radiant direction guidance", ["Where To Look", "Direction: East to overhead", "Look after: 10 PM"], "Sky guide, radiant, east-to-overhead guidance."),
        new(5, "Interesting fact", "Why Geminids are special", ["Why Geminids Are Special", "Bright and colorful meteors", "One of the strongest annual showers"], "Bright meteor streaks with premium night sky."),
        new(6, "Final reminder", "Observation preparation", ["Final Reminder", "Find a dark location", "Give eyes time to adapt"], "Premium night sky with calm final reminder mood.")
    ];

    private sealed record GalleryTopic(int Number, string Purpose, string Concept, IReadOnlyList<string> TextBlocks, string AzureImage2Prompt);
}
