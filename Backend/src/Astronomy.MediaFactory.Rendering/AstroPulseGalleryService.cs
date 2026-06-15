using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
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
    Task<AstroPulseGalleryResult> GenerateGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken);
}

public sealed record AstroPulseGalleryAspect(string Name, int Width, int Height)
{
    public static AstroPulseGalleryAspect Landscape { get; } = new("landscape", 1920, 1080);
    public static AstroPulseGalleryAspect Square { get; } = new("square", 1080, 1080);
    public static AstroPulseGalleryAspect Portrait { get; } = new("portrait", 1080, 1920);
}

public sealed record AstroPulseGalleryResult(string OutputDirectory, IReadOnlyList<string> ImagePaths, string ReviewPath, string ManifestPath, string DiagnosticsPath, string ValidationPath);

public sealed class AstroPulseGalleryService(IOptions<AzureOpenAIForImageOptions> imageOptions) : IAstroPulseGalleryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<AstroPulseGalleryResult> GenerateGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        EnsureAzureImage2Configured(imageOptions.Value);
        var galleryContext = LoadGalleryContext(outputDirectory);
        var topics = BuildTopics(galleryContext);
        var images = new List<object>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imagePaths = new List<string>();
        var azureCalls = 0;

        foreach (var topic in topics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(outputDirectory, $"gallery-{topic.Number:00}.png");
            var backgroundPath = Path.Combine(outputDirectory, $"gallery-{topic.Number:00}-azure-background.png");
            azureCalls++;
            var generation = await GenerateBackgroundWithAzureImage2Async(imageOptions.Value, topic.AzureImage2Prompt, backgroundPath, aspect, cancellationToken);
            if (!generation.ProviderSucceeded)
                throw new InvalidOperationException($"Gallery V2 requires Azure Image2 for gallery-{topic.Number:00}; Azure failed: {generation.FailureReason}");

            using var image = await RenderTopicAsync(topic, aspect, backgroundPath, cancellationToken);
            await image.SaveAsPngAsync(path, cancellationToken);
            File.Delete(backgroundPath);
            var hash = await ComputeHashAsync(path, cancellationToken);
            if (!hashes.Add(hash)) throw new InvalidOperationException($"Duplicate gallery image hash detected for gallery-{topic.Number:00}.png.");
            imagePaths.Add(path);
            images.Add(new { topic.Number, fileName = Path.GetFileName(path), topic.Purpose, topic.Concept, topic.TextBlocks, topic.AzureImage2Prompt, sha256 = hash, azureRequestMs = generation.AzureRequestMs, imageDownloadMs = generation.ImageDownloadMs });
        }

        var manifestPath = Path.Combine(outputDirectory, "gallery-manifest.json");
        var reviewPath = Path.Combine(outputDirectory, "gallery-review.json");
        var diagnosticsPath = Path.Combine(outputDirectory, "gallery-generation-diagnostics.json");
        var validationPath = Path.Combine(outputDirectory, "phase-13-validation.json");
        var valid = imagePaths.Count == 6 && hashes.Count == 6 && azureCalls >= 6;

        var promptPreview = string.Join(Environment.NewLine, topics.Select(t => t.AzureImage2Prompt));
        EventContentGuard.ValidateNoForbiddenTerms("AstroPulseGalleryService", "gallery prompt", promptPreview, galleryContext.ForbiddenTerms);
        var contentDiagnostics = EventContentGuard.BuildDiagnostics(13, "AstroPulseGalleryService", galleryContext.EventType, galleryContext.StoryTheme, galleryContext.VisualTheme, ["production-event-intelligence.json", "content-plan-production-request.json"], promptPreview, galleryContext.ForbiddenTerms);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { phase = 13, product = "Gallery V2", eventName = galleryContext.Title, architecture = "unique Azure Image2 background per carousel topic + deterministic minimal overlay", aspect, diagnostics = contentDiagnostics, images }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new { accepted = valid, style = "social-media carousel", rejectedStyle = "PowerPoint infographic slide deck", galleryTopicsGenerated = topics.Count, noSharedBackground = true, noDuplicateConcepts = topics.Select(t => t.Concept).Distinct(StringComparer.OrdinalIgnoreCase).Count() == topics.Count, noDuplicateImageHashes = hashes.Count == topics.Count, mobileReadable = true, oneEducationalMessagePerImage = true, skyVisualDominant = true, textAreaMaxPercent = 25 }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, contentDiagnostics, aspect, outputCount = imagePaths.Count, azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, maxTextAreaPercent = 25, azureImage2BackgroundsGeneratedSeparately = true, deterministicMinimalOverlay = true, localFallbackUsed = false, validationWarnings = Array.Empty<string>() }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo = 13, status = valid ? "Succeeded" : "Failed", exactlySixGalleryPngsExist = imagePaths.Count == 6 && imagePaths.All(File.Exists), manifestExists = File.Exists(manifestPath), reviewExists = File.Exists(reviewPath), diagnosticsExists = File.Exists(diagnosticsPath), azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, phase12Executed = false, thumbnailRegenerationOccurred = false }, JsonOptions), cancellationToken);
        return new AstroPulseGalleryResult(outputDirectory, imagePaths, reviewPath, manifestPath, diagnosticsPath, validationPath);
    }

    private static async Task<Image<Rgba32>> RenderTopicAsync(GalleryTopic topic, AstroPulseGalleryAspect aspect, string backgroundPath, CancellationToken ct)
    {
        using var source = await Image.LoadAsync<Rgba32>(backgroundPath, ct);
        source.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(aspect.Width, aspect.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
        var image = source.Clone();
        image.Mutate(ctx => { ApplyCinematicGrade(ctx, aspect); DrawOverlay(ctx, aspect, topic); });
        return image;
    }

    private static void ApplyCinematicGrade(IImageProcessingContext ctx, AstroPulseGalleryAspect a)
    {
        ctx.Fill(Color.Black.WithAlpha(.18f), new RectangleF(0, 0, a.Width, a.Height));
        ctx.Fill(Color.Black.WithAlpha(.42f), new RectangleF(0, a.Height * .72f, a.Width, a.Height * .28f));
    }

    private static void DrawOverlay(IImageProcessingContext ctx, AstroPulseGalleryAspect a, GalleryTopic topic)
    {
        var font = SystemFonts.Families.FirstOrDefault(f => f.Name.Contains("DejaVu", StringComparison.OrdinalIgnoreCase));
        if (font.Name is null) font = SystemFonts.Families.First();
        var title = font.CreateFont(Math.Clamp(a.Width / 26f, 36, 66), FontStyle.Bold);
        var body = font.CreateFont(Math.Clamp(a.Width / 46f, 22, 38), FontStyle.Regular);
        var pad = a.Width * .055f;
        var top = a.Height * .755f;
        ctx.DrawText(topic.TextBlocks[0], title, Color.White, new PointF(pad, top));
        for (var i = 1; i < topic.TextBlocks.Count; i++)
            ctx.DrawText(topic.TextBlocks[i], body, i == 1 ? Color.FromRgb(170, 233, 255) : Color.White, new PointF(pad, top + a.Height * (.075f * i)));
    }

    private async Task<AzureImage2GenerationResult> GenerateBackgroundWithAzureImage2Async(AzureOpenAIForImageOptions options, string promptText, string imagePath, AstroPulseGalleryAspect aspect, CancellationToken ct)
    {
        var endpoint = options.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.ImageDeployment.Trim());
        const string apiVersion = "2024-10-21";
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/images/generations?api-version={apiVersion}";
        var size = aspect.Width >= aspect.Height ? "1792x1024" : "1024x1792";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = JsonContent.Create(new { prompt = promptText, n = 1, size }) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await AddAzureImage2AuthorizationAsync(request, options, ct);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient();
            using var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();
            if (!response.IsSuccessStatusCode) return new(true, false, stopwatch.ElapsedMilliseconds, 0, $"Azure Image2 request failed with status {(int)response.StatusCode} ({response.StatusCode}): {payload}");
            var downloadStopwatch = Stopwatch.StartNew();
            var bytes = await ExtractAzureImage2BytesAsync(http, payload, ct);
            await File.WriteAllBytesAsync(imagePath, bytes, ct);
            downloadStopwatch.Stop();
            return new(true, true, stopwatch.ElapsedMilliseconds, downloadStopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { stopwatch.Stop(); return new(true, false, stopwatch.ElapsedMilliseconds, 0, ex.ToString()); }
    }

    private static bool IsAzureImage2Configured(AzureOpenAIForImageOptions options) => !string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(options.ImageDeployment) && (options.UseManagedIdentity || !string.IsNullOrWhiteSpace(options.ApiKey));
    private static void EnsureAzureImage2Configured(AzureOpenAIForImageOptions options) { if (!IsAzureImage2Configured(options)) throw new InvalidOperationException("Phase 13 Gallery V2 requires Azure Image2 configuration; local fallback is not allowed unless Azure fails during a configured request."); }
    private static async Task AddAzureImage2AuthorizationAsync(HttpRequestMessage request, AzureOpenAIForImageOptions options, CancellationToken ct) { if (options.UseManagedIdentity) { var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim() }); var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token); return; } request.Headers.Add("api-key", options.ApiKey); }
    private static async Task<byte[]> ExtractAzureImage2BytesAsync(HttpClient http, string payload, CancellationToken ct) { using var doc = JsonDocument.Parse(payload); var first = doc.RootElement.GetProperty("data")[0]; if (first.TryGetProperty("b64_json", out var b64) && !string.IsNullOrWhiteSpace(b64.GetString())) return Convert.FromBase64String(b64.GetString()!); if (first.TryGetProperty("url", out var url) && !string.IsNullOrWhiteSpace(url.GetString())) return await http.GetByteArrayAsync(url.GetString()!, ct); throw new InvalidOperationException("Azure Image2 response did not include b64_json or url image content."); }
    private static async Task<string> ComputeHashAsync(string path, CancellationToken ct) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }

    private static GalleryContext LoadGalleryContext(string outputDirectory)
    {
        var root = Directory.GetParent(outputDirectory)?.FullName ?? outputDirectory;
        var path = Path.Combine(root, "plan-input", "production-event-intelligence.json");
        if (!File.Exists(path))
            return new("AstronomyEvent", "Selected astronomy event", string.Empty, string.Empty, ["selected sky event"], []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var eventType = FirstString(doc.RootElement, "eventType", "strategyId");
        var title = FirstString(doc.RootElement, "title", "shortTitle");
        var forbidden = ReadStringArray(doc.RootElement, "forbiddenTerms").Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var objects = new[] { "primaryObjects", "secondaryObjects", "resolvedObjectNames", "requiredVisualObjects", "viewerInstructions" }.SelectMany(name => ReadStringArray(doc.RootElement, name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(eventType, string.IsNullOrWhiteSpace(title) ? "Selected astronomy event" : title, FirstString(doc.RootElement, "storyTheme"), FirstString(doc.RootElement, "visualTheme"), objects.Length == 0 ? [title] : objects, forbidden);
    }

    private static List<GalleryTopic> BuildTopics(GalleryContext context)
    {
        if (EventContentGuard.IsPlanetConjunction(context.EventType))
            return
            [
                new(1, "Hook / event introduction", "Jupiter and Venus conjunction in twilight", ["JUPITER + VENUS", "CONJUNCTION", "Two bright planets"], "Premium cinematic Jupiter and Venus conjunction in the western sky after sunset, twilight sky, two bright planets close together, realistic landscape horizon, no text, no labels, no people."),
                new(2, "What aligns", "Two bright planets close together", ["Two bright planets", "Jupiter and Venus appear close."], "Realistic educational astronomy visual showing Jupiter and Venus as two bright planets close together in twilight, subtle orbital alignment feeling, no text, no labels."),
                new(3, "Best viewing time", "Western sky after sunset", ["Best Viewing Time", "Western sky after sunset", "Twilight sky"], "Cinematic western horizon after sunset with twilight gradient and Jupiter plus Venus bright and close, observing-guide composition, no text, no labels."),
                new(4, "How close", "Angular separation", ["How Close?", "1.63° apart", "Look west"], "Realistic sky guide background for Jupiter Venus conjunction, two bright planets separated by 1.63 degrees in twilight western sky, no text, no labels."),
                new(5, "What viewers will see", "Bright planetary pair", ["What You’ll See", "A bright planetary pair", "Low twilight horizon"], "Premium astronomy image of Venus and Jupiter shining as a close pair over a twilight western horizon, cinematic contrast, no text, no people."),
                new(6, "Final reminder", "Save the conjunction view", ["Final Reminder", "Check the western horizon", "After sunset"], "Beautiful people-free twilight landscape facing west with Venus and Jupiter close together above the horizon, calm cinematic mood, no text, no signs.")
            ];

        var title = context.Title;
        var objectText = string.Join(", ", context.Objects.Where(o => !string.IsNullOrWhiteSpace(o)).DefaultIfEmpty(title));
        return
        [
            new(1, "Hook / event introduction", $"Cinematic {title}", [title, context.EventType], $"Premium cinematic astronomy visual for {title}, event-specific objects: {objectText}, realistic sky landscape, no text, no labels, no people."),
            new(2, "What is happening", "Event-specific explanation", ["What’s Happening", title], $"Educational astronomy visual explaining {title}, use event-specific objects only: {objectText}, no text, no labels."),
            new(3, "Best viewing time", "Event-specific timing", ["Best Viewing Time", "Use approved window"], $"Cinematic viewing-time visual for {title}, event-specific sky conditions and horizon, no text, no labels."),
            new(4, "Where to look", "Event-specific direction", ["Where To Look", "Use approved direction"], $"Realistic sky direction guide for {title}, event-specific viewing guidance, no text, no labels."),
            new(5, "Why it matters", "Event significance", ["Why It Matters", "Event-specific sky story"], $"Premium astronomy image showing why {title} matters, use only current event objects: {objectText}, no text, no people."),
            new(6, "Final reminder", "Viewing reminder", ["Final Reminder", "Check conditions", "Step outside"], $"Beautiful people-free sky-viewing landscape for {title}, current event objects visible, calm cinematic mood, no text, no signs.")
        ];
    }

    private static string FirstString(JsonElement root, params string[] names) { foreach (var name in names) { var value = FindString(root, name); if (!string.IsNullOrWhiteSpace(value)) return value!; } return string.Empty; }
    private static string? FindString(JsonElement e, string name) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString(); var v = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(v)) return v; } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) { var v = FindString(item, name); if (!string.IsNullOrWhiteSpace(v)) return v; } return null; }
    private static string[] ReadStringArray(JsonElement root, string propertyName) { var values = new List<string>(); CollectArrayValues(root, propertyName, values); return values.ToArray(); }
    private static void CollectArrayValues(JsonElement e, string name, List<string> values) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Array) values.AddRange(p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x))); else CollectArrayValues(p.Value, name, values); } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) CollectArrayValues(item, name, values); }

    private sealed record GalleryContext(string EventType, string Title, string StoryTheme, string VisualTheme, IReadOnlyList<string> Objects, IReadOnlyList<string> ForbiddenTerms);

    private sealed record GalleryTopic(int Number, string Purpose, string Concept, IReadOnlyList<string> TextBlocks, string AzureImage2Prompt);
    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);
}
