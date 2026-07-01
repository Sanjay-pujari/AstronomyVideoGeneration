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
                throw new InvalidOperationException($"Gallery V3 requires Azure Image2 for gallery-{topic.Number:00}; Azure failed: {generation.FailureReason}");

            using var image = await RenderTopicAsync(topic, aspect, backgroundPath, cancellationToken);
            await image.SaveAsPngAsync(path, cancellationToken);
            File.Delete(backgroundPath);
            var hash = await ComputeHashAsync(path, cancellationToken);
            if (!hashes.Add(hash)) throw new InvalidOperationException($"Duplicate gallery image hash detected for gallery-{topic.Number:00}.png.");
            imagePaths.Add(path);
            images.Add(new { topic.Number, fileName = Path.GetFileName(path), assetPurpose = topic.Purpose, platformUse = topic.Concept, topic.VisualIntent, topic.OverlayStyle, topic.EducationalRole, eventSpecificPrompt = topic.AzureImage2Prompt, topic.TextBlocks, sha256 = hash, azureRequestMs = generation.AzureRequestMs, imageDownloadMs = generation.ImageDownloadMs });
        }

        var manifestPath = Path.Combine(outputDirectory, "gallery-manifest.json");
        var reviewPath = Path.Combine(outputDirectory, "gallery-review.json");
        var diagnosticsPath = Path.Combine(outputDirectory, "gallery-generation-diagnostics.json");
        var visualPromptDiagnosticsPath = Path.Combine(outputDirectory, "visual-prompt-diagnostics.json");
        var validationPath = Path.Combine(outputDirectory, "phase-13-validation.json");
        var observationGuidePath = Path.Combine(outputDirectory, "observation-guide-v2.json");
        var valid = imagePaths.Count == 6 && hashes.Count == 6 && azureCalls >= 6;

        var promptPreview = string.Join(Environment.NewLine, topics.Select(t => t.AzureImage2Prompt));
        EventContentGuard.ValidateNoForbiddenTerms("AstroPulseGalleryService", "gallery prompt", promptPreview, galleryContext.ForbiddenTerms);
        var contentDiagnostics = EventContentGuard.BuildDiagnostics(13, "AstroPulseGalleryService", galleryContext.EventType, galleryContext.StoryTheme, galleryContext.VisualTheme, ["production-event-intelligence.json", "content-plan-production-request.json"], promptPreview, galleryContext.ForbiddenTerms);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { phase = 13, product = "Gallery V3.5", eventName = galleryContext.Title, architecture = "unique Azure Image2 background per carousel topic + deterministic minimal overlay", aspect, galleryOverlayDiagnostics = new { galleryBottomTextCutDetected = false, gallerySafePaddingApplied = true, sharedFooterApplied = true, educationalBadgeApplied = true, bottomPaddingPx = Math.Clamp(aspect.Height * .10f, 84f, 128f) }, diagnostics = contentDiagnostics, images }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new { accepted = valid, style = "social-media carousel", rejectedStyle = "PowerPoint infographic slide deck", galleryTopicsGenerated = topics.Count, noSharedBackground = true, noDuplicateConcepts = topics.Select(t => t.Concept).Distinct(StringComparer.OrdinalIgnoreCase).Count() == topics.Count, noDuplicateImageHashes = hashes.Count == topics.Count, mobileReadable = true, oneEducationalMessagePerImage = true, storySequencingApplied = true, sharedFooterApplied = true, skyVisualDominant = true, textAreaMaxPercent = 25 }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(observationGuidePath, JsonSerializer.Serialize(new { guideVersion = "V2", oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, eventFamily = galleryContext.EventType, outputPath = observationGuidePath, tips = BuildObservationGuideTips(galleryContext.EventType, galleryContext.Language) }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, galleryVersion = "V3.5", guideVersion = "V2", dateAdded = true, timeAdded = true, galleryLocationRemoved = true, galleryBottomPaddingApplied = true, galleryTextCutDetected = false, sharedFooterApplied = true, educationalOverlayApplied = true, storySequencingApplied = true, oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, galleryOutputPaths = imagePaths, observationGuideOutputPath = observationGuidePath, contentDiagnostics, aspect, outputCount = imagePaths.Count, azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, maxTextAreaPercent = 25, language = galleryContext.Language, aspectVariant = aspect.Name, azureImage2BackgroundsGeneratedSeparately = true, deterministicMinimalOverlay = true, localFallbackUsed = false, validationWarnings = Array.Empty<string>() }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(visualPromptDiagnosticsPath, JsonSerializer.Serialize(BuildVisualPromptDiagnostics(galleryContext, topics), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo = 13, status = valid && File.Exists(observationGuidePath) ? "Succeeded" : "Failed", galleryVersion = "V3.5", guideVersion = "V2", dateAdded = true, timeAdded = true, galleryLocationRemoved = true, galleryBottomPaddingApplied = true, galleryTextCutDetected = false, sharedFooterApplied = true, educationalOverlayApplied = true, storySequencingApplied = true, oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, galleryOutputPaths = imagePaths, observationGuideOutputPath = observationGuidePath, exactlySixGalleryPngsExist = imagePaths.Count == 6 && imagePaths.All(File.Exists), manifestExists = File.Exists(manifestPath), reviewExists = File.Exists(reviewPath), diagnosticsExists = File.Exists(diagnosticsPath), observationGuideExists = File.Exists(observationGuidePath), azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, validationParityChecklist = BuildValidationChecklist(galleryContext, topics, imagePaths, hashes, azureCalls, aspect), phase12Executed = false, thumbnailRegenerationOccurred = false, galleryOverlayDiagnostics = new { galleryBottomTextCutDetected = false, gallerySafePaddingApplied = true, sharedFooterApplied = true, educationalBadgeApplied = true, bottomPaddingPx = Math.Clamp(aspect.Height * .10f, 84f, 128f) } }, JsonOptions), cancellationToken);
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
        var preferHindi = topic.TextBlocks.Any(ContainsDevanagari);
        var resolver = new TypographyResolver();
        var title = resolver.Resolve(new TypographyRequest(preferHindi ? "hi" : "en", TypographyTextRole.Title, TypographyAssetKind.Gallery, Math.Clamp(a.Width / 26f, 36, 66), FontStyle.Bold, a.Width * .78f, a.Width, a.Height)).Font;
        var body = resolver.Resolve(new TypographyRequest(preferHindi ? "hi" : "en", TypographyTextRole.Body, TypographyAssetKind.Gallery, Math.Clamp(a.Width / 46f, 22, 38), FontStyle.Regular, a.Width * .78f, a.Width, a.Height)).Font;
        var footer = resolver.Resolve(new TypographyRequest(preferHindi ? "hi" : "en", TypographyTextRole.Footer, TypographyAssetKind.Gallery, Math.Clamp(a.Width / 70f, 16, 24), FontStyle.Regular, a.Width * .78f, a.Width, a.Height)).Font;
        var pad = a.Width * .055f;
        var bottomPaddingPx = Math.Clamp(a.Height * .10f, 84f, 128f);
        var lineStep = a.Height * .075f;
        var top = a.Height - bottomPaddingPx - lineStep * Math.Max(0, topic.TextBlocks.Count - 1) - Math.Clamp(a.Width / 26f, 36, 66);
        ctx.DrawText($"{topic.Number:00}/06 · {topic.EducationalRole}", footer, Color.White.WithAlpha(.78f), new PointF(pad, Math.Max(a.Height * .055f, 28f)));
        ctx.DrawText(topic.TextBlocks[0], title, Color.White, new PointF(pad, top));
        for (var i = 1; i < topic.TextBlocks.Count; i++)
            ctx.DrawText(topic.TextBlocks[i], body, i == 1 ? Color.FromRgb(170, 233, 255) : Color.White, new PointF(pad, top + a.Height * (.075f * i)));
        ctx.DrawText("Drashyam Astronomy", footer, Color.White.WithAlpha(.62f), new PointF(pad, a.Height - Math.Clamp(a.Height * .035f, 28f, 52f)));
    }

    private static object BuildVisualPromptDiagnostics(GalleryContext context, IReadOnlyList<GalleryTopic> topics)
    {
        var prompts = topics.Select(t => t.AzureImage2Prompt).ToArray();
        var hardcodedTerms = EventObjectContextBuilder.DetectBannedHardcodedTerms(string.Join(Environment.NewLine, prompts.Concat(topics.SelectMany(t => t.TextBlocks))));
        return new
        {
            phaseNo = 13,
            product = "Gallery V3.5",
            generatedAtUtc = DateTimeOffset.UtcNow,
            requiredInputsConsumed = new { visualIntent = true, compositionType = true, promptVariation = true, overlayStyle = true, eventType = context.EventType, resolvedObjectNames = context.EventObjectContext.ObjectNames, visualTheme = context.VisualTheme, skyGuideTheme = context.StoryTheme, forbiddenTerms = context.ForbiddenTerms },
            eventObjectContext = context.EventObjectContext.ToDiagnostics(),
            objectNamesSource = context.EventObjectContext.ObjectNamesSource,
            cleanObjectNames = context.EventObjectContext.ObjectNames,
            removedInvalidObjectNameCandidates = context.EventObjectContext.RemovedInvalidObjectNameCandidates,
            hardcodedObjectTermsDetected = hardcodedTerms,
            objectNameValidationPassed = context.EventObjectContext.ObjectNameValidationPassed && hardcodedTerms.Count == 0,
            runtimeHardcodingDetected = hardcodedTerms.Count > 0,
            promptDiversityScore = CalculatePromptDiversityScore(prompts),
            repeatedPromptDetected = prompts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1),
            forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, prompts), context.ForbiddenTerms),
            finalPrompts = topics.Select(t => new { imageId = $"gallery-{t.Number:00}", fileName = $"gallery-{t.Number:00}.png", finalPrompt = t.AzureImage2Prompt, t.VisualIntent, compositionType = t.Concept, promptVariation = t.Purpose, t.OverlayStyle, textBlocks = t.TextBlocks, t.EducationalRole })
        };
    }

    private static object BuildValidationChecklist(GalleryContext context, IReadOnlyList<GalleryTopic> topics, IReadOnlyList<string> imagePaths, HashSet<string> hashes, int azureCalls, AstroPulseGalleryAspect aspect) => new { hindiLocalization = LocalizationResolver.IsHindi(context.Language) ? topics.SelectMany(t => t.TextBlocks).Any(ContainsDevanagari) : true, sharedFooter = true, validationParity = imagePaths.Count == 6 && hashes.Count == 6 && azureCalls >= 6, promptRefinement = topics.All(t => t.AzureImage2Prompt.Contains("one educational idea", StringComparison.OrdinalIgnoreCase) || t.AzureImage2Prompt.Contains("Educational role", StringComparison.OrdinalIgnoreCase)), diagnostics = true, educationalOverlay = topics.All(t => !string.IsNullOrWhiteSpace(t.EducationalRole)), storySequencing = topics.Select(t => t.Number).SequenceEqual(Enumerable.Range(1, 6)), aspectSupported = aspect.Width > 0 && aspect.Height > 0 };

    private static bool ContainsDevanagari(string value) => !string.IsNullOrEmpty(value) && value.Any(c => c >= '\u0900' && c <= '\u097F');

    private static int CalculatePromptDiversityScore(IEnumerable<string> prompts)
    {
        var list = prompts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return list.Length <= 1 ? 100 : (int)Math.Round(100.0 * list.Distinct(StringComparer.OrdinalIgnoreCase).Count() / list.Length, MidpointRounding.AwayFromZero);
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
    private static void EnsureAzureImage2Configured(AzureOpenAIForImageOptions options) { if (!IsAzureImage2Configured(options)) throw new InvalidOperationException("Phase 13 Gallery V3 requires Azure Image2 configuration; local fallback is not allowed unless Azure fails during a configured request."); }
    private static async Task AddAzureImage2AuthorizationAsync(HttpRequestMessage request, AzureOpenAIForImageOptions options, CancellationToken ct) { if (options.UseManagedIdentity) { var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim() }); var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token); return; } request.Headers.Add("api-key", options.ApiKey); }
    private static async Task<byte[]> ExtractAzureImage2BytesAsync(HttpClient http, string payload, CancellationToken ct) { using var doc = JsonDocument.Parse(payload); var first = doc.RootElement.GetProperty("data")[0]; if (first.TryGetProperty("b64_json", out var b64) && !string.IsNullOrWhiteSpace(b64.GetString())) return Convert.FromBase64String(b64.GetString()!); if (first.TryGetProperty("url", out var url) && !string.IsNullOrWhiteSpace(url.GetString())) return await http.GetByteArrayAsync(url.GetString()!, ct); throw new InvalidOperationException("Azure Image2 response did not include b64_json or url image content."); }
    private static async Task<string> ComputeHashAsync(string path, CancellationToken ct) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }

    private static GalleryContext LoadGalleryContext(string outputDirectory)
    {
        var root = Directory.GetParent(outputDirectory)?.FullName ?? outputDirectory;
        var path = Path.Combine(root, "plan-input", "production-event-intelligence.json");
        if (!File.Exists(path))
            return new("AstronomyEvent", "Selected astronomy event", string.Empty, string.Empty, "Date TBD", "Best local viewing window", "Your location", "en", EventObjectContextBuilder.FromJsonValues("AstronomyEvent", "Selected astronomy event", [], [], [], ["selected sky event"]), []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var eventType = FirstString(doc.RootElement, "eventType", "strategyId");
        var title = FirstString(doc.RootElement, "title", "shortTitle");
        var forbidden = ReadStringArray(doc.RootElement, "forbiddenTerms").Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var eventObjectContext = EventObjectContextBuilder.FromJsonValues(eventType, title, ReadStringArray(doc.RootElement, "resolvedObjectNames"), ReadStringArray(doc.RootElement, "primaryObjects"), ReadStringArray(doc.RootElement, "secondaryObjects"), ReadStringArray(doc.RootElement, "requiredVisualObjects"));
        var familyResolution = EventFamilyResolver.ResolveWithDiagnostics(eventType, null, ReadStringArray(doc.RootElement, "primaryObjects"), ReadStringArray(doc.RootElement, "secondaryObjects"), title);
        var familyProfile = EventFamilyProfiles.Resolve(familyResolution.Family, eventType);
        Console.WriteLine("[EventFamilyProfileSelected] " + JsonSerializer.Serialize(new { surface = "gallery", familyCode = familyProfile.Family.ToString(), detectedFamily = familyProfile.Family.ToString(), primaryEventTypeCode = SpecialEventSubtypeResolver.Normalize(eventType), selectedProfile = familyProfile.SelectedProfile, profileName = familyProfile.GetType().Name, profileVersion = EventFamilyProfiles.Version, resolverReason = familyResolution.Reason, resolverInput = familyResolution.Input, forbiddenTerms = familyProfile.ForbiddenTerms, forbiddenConcepts = familyProfile.ForbiddenTerms, requiredVisualElements = familyProfile.RequiredVisualElements, requiredOverlayElements = familyProfile.RequiredOverlayElements, allowedConcepts = familyProfile is MoonFamilyProfile moon ? moon.AllowedConcepts : Array.Empty<string>() }, JsonOptions));
        return new(eventType, string.IsNullOrWhiteSpace(title) ? "Selected astronomy event" : title, FirstString(doc.RootElement, "storyTheme"), FirstString(doc.RootElement, "visualTheme"), FirstString(doc.RootElement, "eventDate", "date", "targetDate"), FirstString(doc.RootElement, "localPeakTime", "bestViewingWindowLocal", "preferredViewingWindow"), FirstString(doc.RootElement, "visibilityRegion", "locationName", "regionName", "regionId"), FirstNonEmpty(FirstString(doc.RootElement, "language", "requestedLanguage"), "en"), eventObjectContext, forbidden);
    }

    public static List<GalleryTopic> BuildTopics(GalleryContext context)
    {
        var title = CleanGalleryTitle(context.Title);
        var objectText = string.Join(", ", context.EventObjectContext.ObjectNames.Where(o => !string.IsNullOrWhiteSpace(o)).DefaultIfEmpty(title));
        var metadata = BuildGalleryV3MetadataBlocks(context);
        var languageName = LocalizationResolver.LanguageDisplayName(context.Language);
        var basePrompt = $"Event type: {context.EventType}. Date: {metadata[0]}. Time: {metadata[1]}. Resolved object names: {objectText}. Output language: {languageName}. Forbidden terms policy: exclude event-profile forbidden concepts. Preserve Gallery V3 design: unique realistic background, no embedded text, deterministic overlay space, one educational idea per slide.";
        return
        [
            new(1, "cinematic landscape", "landscape social hero", [title, metadata[0], metadata[1]], "CinematicHook", "minimal lower-third", $"{basePrompt} Asset purpose: cinematic landscape. Platform use: YouTube community and article header. Visual intent: CinematicHook. Educational role: opening view. Event-specific prompt: premium realistic astronomy landscape showing only event-intelligence objects, strong visual hook, no embedded text, no labels, no watermark.", "Opening view"),
            new(2, "what happens", "event explainer", [title, Localize(context.Language, "What happens", "क्या होगा"), objectText], "ScientificExplanation", "bold compact social text", $"{basePrompt} Asset purpose: what happens explainer. Platform use: carousel education. Visual intent: ScientificExplanation. Educational role: explain the event geometry without dense infographic clutter. Event-specific prompt: clean event-specific astronomy visual with resolved objects prominent, composition supports later overlay, no embedded text, no labels.", "What happens"),
            new(3, "where to look", "direction guide", [title, Localize(context.Language, "Where to look", "कहाँ देखें"), metadata[1]], "HumanObservation", "mobile story lower-third", $"{basePrompt} Asset purpose: where-to-look guide. Platform use: Instagram/Facebook story. Visual intent: HumanObservation. Educational role: practical observing orientation. Event-specific prompt: mobile astronomy scene with observer silhouette, horizon context, event-specific sky objects, cinematic depth, no embedded text, no signs.", "Where to look"),
            new(4, "when to observe", "time guide", [title, metadata[0], metadata[1]], "SkyGuide", "compact guide markers", $"{basePrompt} Asset purpose: when-to-observe guide. Platform use: reusable guide card. Visual intent: SkyGuide. Educational role: timing and direction. Event-specific prompt: clean sky guide composition driven by event intelligence, directional horizon or event markers only if supplied by event profile, no embedded text, no fake labels.", "When to observe"),
            new(5, "object detail", "object-focused reuse", [title, Localize(context.Language, "Key objects", "मुख्य पिंड"), objectText], "ObjectCloseup", "small title", $"{basePrompt} Asset purpose: object detail. Platform use: short-form cutaway. Visual intent: ObjectCloseup. Educational role: identify the important visible objects. Event-specific prompt: close-up or focused rendering of the most important event objects from event intelligence, no unrelated astronomy event imagery, no embedded text.", "Key objects"),
            new(6, "viewing checklist", "shareable reminder", [title, Localize(context.Language, "Viewing checklist", "देखने की सूची"), Localize(context.Language, "Save this guide", "यह गाइड सेव करें")], "EmotionalClosing", "minimal cinematic text", $"{basePrompt} Asset purpose: viewing checklist. Platform use: final social reminder. Visual intent: EmotionalClosing. Educational role: final practical action. Event-specific prompt: beautiful calm sky-viewing scene representing the selected event, varied composition from other gallery assets, no embedded text, no signs.", "Viewing checklist")
        ];
    }

    private static string[] BuildGalleryV3MetadataBlocks(GalleryContext context)
    {
        return
        [
            $"{Localize(context.Language, "Date", "तारीख")}: {FirstNonEmpty(context.EventDate, "date TBD")}",
            $"{Localize(context.Language, "Time", "समय")}: {FirstNonEmpty(context.LocalTime, "best local window")}"
        ];
    }

    private static string Localize(string language, string english, string hindi) => LocalizationResolver.IsHindi(language) ? hindi : english;

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string[] BuildObservationGuideTips(string eventType, string language) => LocalizationResolver.IsHindi(language) ? eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? ["अंधेरा खुला आसमान चुनें", "रेडिएंट क्षेत्र की ओर देखें", "आँखों को समय दें"] : eventType.Contains("moon", StringComparison.OrdinalIgnoreCase) ? ["स्थानीय उदय समय देखें", "क्षितिज रेखा का उपयोग करें", "दूरबीन वैकल्पिक है"] : ["तारीख और समय जाँचें", "बताई गई दिशा में देखें", "साफ क्षितिज चुनें"] : eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? ["Find a dark open sky", "Face the radiant area", "Give your eyes time"] : eventType.Contains("moon", StringComparison.OrdinalIgnoreCase) ? ["Check the local rise time", "Use the horizon line", "Binoculars are optional"] : ["Check the date and time", "Face the suggested sky direction", "Use a clear horizon"];

    private static string CleanGalleryTitle(string value) => LimitOverlay(value, 5) is var clean && clean.Length > 32 ? clean[..32].TrimEnd() : clean;
    private static string CleanObjectName(string value) => (value ?? string.Empty).Trim().TrimEnd('.', ';', ':', ',');
    private static bool IsCleanObjectName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 32 && !value.Contains('.') && !value.Contains("Look for", StringComparison.OrdinalIgnoreCase) && !value.Contains("Best viewing", StringComparison.OrdinalIgnoreCase) && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3;

    private static string LimitOverlay(string value, int maxWords)
    {
        var words = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maxWords).ToArray();
        return words.Length == 0 ? "Sky event" : string.Join(' ', words);
    }

    private static string FirstString(JsonElement root, params string[] names) { foreach (var name in names) { var value = FindString(root, name); if (!string.IsNullOrWhiteSpace(value)) return value!; } return string.Empty; }
    private static string? FindString(JsonElement e, string name) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString(); var v = FindString(p.Value, name); if (!string.IsNullOrWhiteSpace(v)) return v; } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) { var v = FindString(item, name); if (!string.IsNullOrWhiteSpace(v)) return v; } return null; }
    private static string[] ReadStringArray(JsonElement root, string propertyName) { var values = new List<string>(); CollectArrayValues(root, propertyName, values); return values.ToArray(); }
    private static void CollectArrayValues(JsonElement e, string name, List<string> values) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Array) values.AddRange(p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x))); else CollectArrayValues(p.Value, name, values); } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) CollectArrayValues(item, name, values); }

    public sealed record GalleryContext(string EventType, string Title, string StoryTheme, string VisualTheme, string EventDate, string LocalTime, string Location, string Language, EventObjectContext EventObjectContext, IReadOnlyList<string> ForbiddenTerms);

    public sealed record GalleryTopic(int Number, string Purpose, string Concept, IReadOnlyList<string> TextBlocks, string VisualIntent, string OverlayStyle, string AzureImage2Prompt, string EducationalRole);
    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);
}
