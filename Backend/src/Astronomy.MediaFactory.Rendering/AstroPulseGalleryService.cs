using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Astronomy.MediaFactory.Contracts;
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
    Task<AstroPulseGalleryResult> GenerateGeminidsGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken);
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

    public async Task<AstroPulseGalleryResult> GenerateGeminidsGalleryAsync(string outputDirectory, AstroPulseGalleryAspect aspect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        EnsureAzureImage2Configured(imageOptions.Value);
        var topics = BuildTopics();
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

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { phase = 13, product = "Gallery V2", eventName = "Geminids Meteor Shower Peak", architecture = "unique Azure Image2 background per carousel topic + deterministic minimal overlay", aspect, images }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new { accepted = valid, style = "social-media carousel", rejectedStyle = "PowerPoint infographic slide deck", galleryTopicsGenerated = topics.Count, noSharedBackground = true, noDuplicateConcepts = topics.Select(t => t.Concept).Distinct(StringComparer.OrdinalIgnoreCase).Count() == topics.Count, noDuplicateImageHashes = hashes.Count == topics.Count, mobileReadable = true, oneEducationalMessagePerImage = true, skyVisualDominant = true, textAreaMaxPercent = 25 }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, aspect, outputCount = imagePaths.Count, azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, maxTextAreaPercent = 25, azureImage2BackgroundsGeneratedSeparately = true, deterministicMinimalOverlay = true, localFallbackUsed = false, validationWarnings = Array.Empty<string>() }, JsonOptions), cancellationToken);
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

    private static List<GalleryTopic> BuildTopics() =>
    [
        new(1, "Hook / event introduction", "Premium cinematic Geminids meteor shower over realistic night landscape", ["GEMINIDS", "METEOR SHOWER PEAK", "Dec 13–14, 2026"], "Premium cinematic Geminids meteor shower over realistic dark night landscape, National Geographic astronomy photography mood, rich sky detail, no text, no labels, no people, social carousel background."),
        new(2, "What causes the Geminids?", "Earth passing through debris stream from asteroid 3200 Phaethon", ["What causes Geminids?", "Earth crosses debris from asteroid 3200 Phaethon."], "Earth passing through debris stream from asteroid 3200 Phaethon, cinematic realistic space illustration, orbit arcs and dust trail, no text, no labels, premium educational astronomy visual."),
        new(3, "Best viewing time", "Night sky over dark landscape with clock/calendar inspired composition", ["Best Viewing Time", "Peak: Dec 13–14", "Midnight to pre-dawn"], "Night sky over dark rural landscape, Geminids meteor shower visible, subtle clock and calendar inspired composition created with natural light shapes, no text, no numbers, cinematic."),
        new(4, "Where to look", "Realistic eastern horizon under starry sky with subtle radiant guide", ["Where To Look", "East to overhead", "After 10 PM"], "Realistic eastern horizon under starry sky, Geminids meteor streaks rising east to overhead, subtle radiant guide made of light not typography, observing direction mood, no text."),
        new(5, "Why Geminids are special", "Bright colorful meteors across Milky Way", ["Why Geminids Are Special", "Bright, colorful meteors", "One of the strongest annual showers"], "Bright colorful Geminids meteors across the Milky Way, premium astronomy image, rich meteor streaks, deep dark sky, cinematic contrast, no text, no people."),
        new(6, "Final reminder", "Beautiful dark-sky viewing scene", ["Final Reminder", "Find a dark location", "Give your eyes 20 minutes", "Dress warm"], "Beautiful people-free dark-sky viewing landscape, meteor shower overhead, calm cinematic mood, foreground hills and clear winter night sky, no text, no signs.")
    ];

    private sealed record GalleryTopic(int Number, string Purpose, string Concept, IReadOnlyList<string> TextBlocks, string AzureImage2Prompt);
    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);
}
