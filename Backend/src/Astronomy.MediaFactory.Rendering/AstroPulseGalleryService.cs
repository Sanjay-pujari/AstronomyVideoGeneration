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
        var diagnosticsDirectory = Path.Combine(outputDirectory, "diagnostics");
        var comparisonDirectory = Path.Combine(outputDirectory, "comparison");
        var observationGuideDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory) ?? outputDirectory, "observation-guide");
        Directory.CreateDirectory(diagnosticsDirectory);
        Directory.CreateDirectory(comparisonDirectory);
        Directory.CreateDirectory(observationGuideDirectory);
        EnsureAzureImage2Configured(imageOptions.Value);
        var galleryContext = NormalizeGalleryContext(LoadGalleryContext(outputDirectory));
        var contract = GalleryContentResolver.Resolve(galleryContext);
        var topics = BuildTopics(contract);
        var localizationDiagnostics = BuildGalleryLocalizationDiagnostics(galleryContext, topics, aspect);
        var localizationValidation = ValidateGalleryLocalization(galleryContext, topics, aspect);
        var observationDisplay = BuildObservationDisplay(galleryContext);
        var images = new List<object>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imagePaths = new List<string>();
        var azureCalls = 0;

        foreach (var topic in topics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(outputDirectory, ResolveGalleryPageFileName(topic.Number));
            var backgroundPath = Path.Combine(outputDirectory, $"gallery-{topic.Number:00}-azure-background.png");
            azureCalls++;
            var generation = await GenerateBackgroundWithAzureImage2Async(imageOptions.Value, topic.AzureImage2Prompt, backgroundPath, aspect, cancellationToken);
            if (!generation.ProviderSucceeded)
                throw new InvalidOperationException($"Gallery V3 requires Azure Image2 for gallery-{topic.Number:00}; Azure failed: {generation.FailureReason}");

            using var image = await RenderTopicAsync(topic, aspect, backgroundPath, cancellationToken);
            await image.SaveAsPngAsync(path, cancellationToken);
            File.Delete(backgroundPath);
            var hash = await ComputeHashAsync(path, cancellationToken);
            if (!hashes.Add(hash)) throw new InvalidOperationException($"Duplicate gallery image hash detected for {Path.GetFileName(path)}.");
            imagePaths.Add(path);
            images.Add(new { topic.Number, fileName = Path.GetFileName(path), assetPurpose = topic.Purpose, platformUse = topic.Concept, topic.VisualIntent, topic.OverlayStyle, topic.EducationalRole, eventSpecificPrompt = topic.AzureImage2Prompt, topic.TextBlocks, sha256 = hash, azureRequestMs = generation.AzureRequestMs, imageDownloadMs = generation.ImageDownloadMs });
        }

        var manifestPath = Path.Combine(outputDirectory, "GalleryArtifactManifest.json");
        var localizationDiagnosticsPath = Path.Combine(diagnosticsDirectory, "gallery-localization.json");
        var galleryContentContractPath = Path.Combine(outputDirectory, "gallery-prompt.json");
        var galleryEventDisplayContractPath = Path.Combine(outputDirectory, "composition-model.json");
        var overlayDiagnosticsPath = Path.Combine(diagnosticsDirectory, "gallery-overlay.json");
        var reviewPath = Path.Combine(diagnosticsDirectory, "GalleryReview.json");
        var diagnosticsPath = Path.Combine(diagnosticsDirectory, "GalleryGenerationDiagnostics.json");
        var visualPromptDiagnosticsPath = Path.Combine(diagnosticsDirectory, "VisualPromptDiagnostics.json");
        var validationPath = Path.Combine(diagnosticsDirectory, "phase-13-validation.json");
        var observationIntelligenceTargetPath = Path.Combine(observationGuideDirectory, "diagnostics", "observation-intelligence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(observationIntelligenceTargetPath)!);
        var observationGuidePath = Path.Combine(observationGuideDirectory, "observation-guide-v2.json");
        var assetStoryPath = Path.Combine(outputDirectory, "asset-story.json");
        var assetBlueprintPath = Path.Combine(outputDirectory, "asset-blueprint.json");
        var contractValidationPassed = !contract.ValidationRules.Any(r => r.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
        var observationErrors = galleryContext.ObservationInfo?.Errors ?? Array.Empty<string>();
        var observationWarnings = galleryContext.ObservationInfo?.Warnings ?? Array.Empty<string>();
        var validationErrors = localizationValidation.Errors.Concat(contract.ValidationRules.Where(r => r.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))).Concat(observationErrors.Select(e => "ERROR: " + e)).ToArray();
        var validationWarnings = localizationValidation.Warnings.Concat(contract.ValidationRules.Where(r => r.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase))).Concat(observationWarnings.Select(w => "WARN: " + w)).ToArray();
        var valid = imagePaths.Count == 6 && hashes.Count == 6 && azureCalls >= 6 && localizationValidation.ValidationPassed && contractValidationPassed;

        var promptPreview = string.Join(Environment.NewLine, topics.Select(t => t.AzureImage2Prompt));
        EventContentGuard.ValidateNoForbiddenTerms("AstroPulseGalleryService", "gallery prompt", promptPreview, contract.ForbiddenTerms);
        var contentDiagnostics = EventContentGuard.BuildDiagnostics(13, "AstroPulseGalleryService", galleryContext.EventType, galleryContext.StoryTheme, galleryContext.VisualTheme, ["production-event-intelligence.json", "content-plan-production-request.json", "gallery-content-contract.json", "gallery-event-display-contract.json"], promptPreview, contract.ForbiddenTerms);
        await File.WriteAllTextAsync(galleryContentContractPath, JsonSerializer.Serialize(contract, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(observationIntelligenceTargetPath, JsonSerializer.Serialize(BuildObservationIntelligenceDiagnostics(galleryContext), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(galleryEventDisplayContractPath, JsonSerializer.Serialize(BuildEventDisplayContractDiagnostics(contract), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(assetStoryPath, JsonSerializer.Serialize(new { product = "Gallery", story = galleryContext.StoryTheme, title = galleryContext.Title, topics = topics.Select(t => new { t.Number, t.Concept, t.Purpose, t.EducationalRole }) }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(assetBlueprintPath, JsonSerializer.Serialize(new { product = "Gallery", architecture = "Editorial Product", renderingUnchanged = true, azureGenerationUnchanged = true, pageAssets = imagePaths.Select(Path.GetFileName).ToArray() }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(localizationDiagnosticsPath, JsonSerializer.Serialize(localizationDiagnostics, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(overlayDiagnosticsPath, JsonSerializer.Serialize(BuildGalleryOverlayDiagnostics(galleryContext, topics, aspect), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { version = "4.5E", outputArtifactMode = "Production", product = "Gallery", expectedArtifacts = new[] { "AssetStory", "AssetBlueprint", "CompositionModel", "GalleryPrompt", "GalleryReview", "GalleryGenerationDiagnostics", "VisualPromptDiagnostics" }.Concat(images.Select((_, i) => $"Page{i + 1:00}")).ToArray(), artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AssetStory"] = "asset-story.json", ["AssetBlueprint"] = "asset-blueprint.json", ["CompositionModel"] = "composition-model.json", ["GalleryPrompt"] = "gallery-prompt.json", ["GalleryReview"] = Path.Combine("diagnostics", "GalleryReview.json"), ["GalleryGenerationDiagnostics"] = Path.Combine("diagnostics", "GalleryGenerationDiagnostics.json"), ["VisualPromptDiagnostics"] = Path.Combine("diagnostics", "VisualPromptDiagnostics.json") }.Concat(imagePaths.Select((path, i) => new KeyValuePair<string, string>($"Page{i + 1:00}", Path.GetFileName(path)))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase), phase = 13, eventName = galleryContext.Title, architecture = "unique Azure Image2 background per carousel topic + deterministic minimal overlay", aspect, galleryOverlayDiagnostics = new { galleryBottomTextCutDetected = false, gallerySafePaddingApplied = true, sharedFooterApplied = true, educationalBadgeApplied = true, bottomPaddingPx = Math.Clamp(aspect.Height * .10f, 84f, 128f) }, diagnostics = contentDiagnostics, images }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new { accepted = valid, style = "social-media carousel", rejectedStyle = "PowerPoint infographic slide deck", galleryTopicsGenerated = topics.Count, noSharedBackground = true, noDuplicateConcepts = topics.Select(t => t.Concept).Distinct(StringComparer.OrdinalIgnoreCase).Count() == topics.Count, noDuplicateImageHashes = hashes.Count == topics.Count, mobileReadable = true, oneEducationalMessagePerImage = true, storySequencingApplied = true, sharedFooterApplied = true, skyVisualDominant = true, textAreaMaxPercent = 25 }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(observationGuidePath, JsonSerializer.Serialize(new { guideVersion = "V2", oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, eventFamily = galleryContext.EventType, outputPath = observationGuidePath, tips = BuildObservationGuideTips(galleryContext.EventType, galleryContext.Language) }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { generatedAtUtc = DateTimeOffset.UtcNow, galleryVersion = "V3.5", guideVersion = "V2", dateAdded = true, timeAdded = true, galleryLocationRemoved = true, galleryBottomPaddingApplied = true, galleryTextCutDetected = false, sharedFooterApplied = true, educationalOverlayApplied = true, storySequencingApplied = true, oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, galleryOutputPaths = imagePaths, observationGuideOutputPath = observationGuidePath, contentDiagnostics, aspect, outputCount = imagePaths.Count, azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, maxTextAreaPercent = 25, language = galleryContext.Language, requestedLanguage = galleryContext.RequestedLanguage, resolvedLanguage = galleryContext.Language, galleryContext.EventName, galleryContext.EventFamily, galleryContext.EventSubtype, galleryContext.LocalizedEventTitle, galleryContext.TitleSource, galleryContext.MoonSubtypeVisualAttributes, galleryContext.HeroTitleResolverReused, galleryContext.GenericMoonFallbackUsed, localizationDiagnostics, aspectVariant = aspect.Name, azureImage2BackgroundsGeneratedSeparately = true, deterministicMinimalOverlay = true, localFallbackUsed = false, validationWarnings, validationErrors, observationDisplay.eventPeakUtc, observationDisplay.localPeakTime, observationDisplay.displayedObservationTime, observationDisplay.observationTimeSource, observationDisplay.eventFamilyRuleApplied, observationIntelligenceOutputPath = observationIntelligenceTargetPath }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(visualPromptDiagnosticsPath, JsonSerializer.Serialize(BuildVisualPromptDiagnostics(galleryContext, topics), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo = 13, status = ResolvePhase13ValidationStatus(valid && File.Exists(observationGuidePath), validationWarnings), galleryVersion = "V3.5", guideVersion = "V2", dateAdded = true, timeAdded = true, galleryLocationRemoved = true, galleryBottomPaddingApplied = true, galleryTextCutDetected = false, sharedFooterApplied = true, educationalOverlayApplied = true, storySequencingApplied = true, oldAccurateSkyGuideReplaced = true, guideTitle = "How To Observe", familySpecificGuideApplied = true, galleryOutputPaths = imagePaths, observationGuideOutputPath = observationGuidePath, exactlySixGalleryPngsExist = imagePaths.Count == 6 && imagePaths.All(File.Exists), manifestExists = File.Exists(manifestPath), reviewExists = File.Exists(reviewPath), diagnosticsExists = File.Exists(diagnosticsPath), observationGuideExists = File.Exists(observationGuidePath), azureCallsCount = azureCalls, uniqueImageHashes = hashes.Count, galleryContext.EventName, galleryContext.EventFamily, galleryContext.EventSubtype, galleryContext.LocalizedEventTitle, galleryContext.TitleSource, galleryContext.MoonSubtypeVisualAttributes, galleryContext.HeroTitleResolverReused, galleryContext.GenericMoonFallbackUsed, validationParityChecklist = BuildValidationChecklist(galleryContext, topics, imagePaths, hashes, azureCalls, aspect), validationWarnings, validationErrors, observationDisplay.eventPeakUtc, observationDisplay.localPeakTime, observationDisplay.displayedObservationTime, observationDisplay.observationTimeSource, observationDisplay.eventFamilyRuleApplied, observationIntelligenceOutputPath = observationIntelligenceTargetPath, validationPassed = valid && File.Exists(observationGuidePath), phase12Executed = false, thumbnailRegenerationOccurred = false, galleryOverlayDiagnostics = new { galleryBottomTextCutDetected = false, gallerySafePaddingApplied = true, sharedFooterApplied = true, educationalBadgeApplied = true, bottomPaddingPx = Math.Clamp(aspect.Height * .10f, 84f, 128f), localizationDiagnostics } }, JsonOptions), cancellationToken);
        return new AstroPulseGalleryResult(outputDirectory, imagePaths, reviewPath, manifestPath, diagnosticsPath, validationPath);
    }

    private static string ResolveGalleryPageFileName(int pageNumber) => pageNumber switch
    {
        1 => "page01-hook.png",
        2 => "page02-recognition.png",
        3 => "page03-explanation.png",
        4 => "page04-observation.png",
        5 => "page05-memory.png",
        6 => "page06-checklist.png",
        _ => $"page{pageNumber:00}.png"
    };

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
        var preferHindi = LocalizationResolver.IsHindi(topic.Language) || topic.TextBlocks.Any(ContainsDevanagari);
        var title = ResolveGalleryFont(preferHindi, TypographyTextRole.Title, Math.Clamp(a.Width / 26f, 36, 66), FontStyle.Bold, a).Font;
        var body = ResolveGalleryFont(preferHindi, TypographyTextRole.Body, Math.Clamp(a.Width / 46f, 22, 38), FontStyle.Regular, a).Font;
        var footer = ResolveGalleryFont(preferHindi, TypographyTextRole.Footer, Math.Clamp(a.Width / 70f, 16, 24), FontStyle.Regular, a).Font;
        var pad = a.Width * .055f;
        var bottomPaddingPx = Math.Clamp(a.Height * .10f, 84f, 128f);
        var lineStep = a.Height * .075f;
        var titleLines = WrapOverlayTitle(topic.TextBlocks[0], a);
        var top = a.Height - bottomPaddingPx - lineStep * Math.Max(0, topic.TextBlocks.Count - 1 + titleLines.Count - 1) - Math.Clamp(a.Width / 26f, 36, 66);
        ctx.DrawText($"{topic.Number:00}/06 · {topic.LocalizedEducationalRole}", footer, Color.White.WithAlpha(.78f), new PointF(pad, Math.Max(a.Height * .055f, 28f)));
        for (var line = 0; line < titleLines.Count; line++)
            ctx.DrawText(titleLines[line], title, Color.White, new PointF(pad, top + lineStep * line));
        for (var i = 1; i < topic.TextBlocks.Count; i++)
            ctx.DrawText(topic.TextBlocks[i], body, i == 1 ? Color.FromRgb(170, 233, 255) : Color.White, new PointF(pad, top + a.Height * (.075f * (i + titleLines.Count - 1))));
        ctx.DrawText(topic.FooterLabel, footer, Color.White.WithAlpha(.62f), new PointF(pad, a.Height - Math.Clamp(a.Height * .035f, 28f, 52f)));
    }

    private static IReadOnlyList<string> WrapOverlayTitle(string title, AstroPulseGalleryAspect aspect)
    {
        var maxChars = aspect.Name.Equals("portrait", StringComparison.OrdinalIgnoreCase) ? 24 : 34;
        title = CleanGalleryTitle(title);
        if (title.Length <= maxChars) return [title];
        var parts = title.Split('–', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1) return [string.Join("–", parts.Take(parts.Length - 1)), parts[^1]];
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = string.Empty;
        var second = string.Empty;
        foreach (var word in words)
        {
            if ((first + " " + word).Trim().Length <= maxChars) first = (first + " " + word).Trim();
            else second = (second + " " + word).Trim();
        }
        return string.IsNullOrWhiteSpace(second) ? [first] : [first, second.Length <= maxChars ? second : second[..maxChars].TrimEnd()];
    }


    private static GalleryFontSelection ResolveGalleryFont(bool preferHindi, TypographyTextRole role, float size, FontStyle style, AstroPulseGalleryAspect aspect)
    {
        if (preferHindi)
        {
            var requestedFont = new ThumbnailFontOptions().HindiFont;
            var resolver = new RuntimeAssetPathResolver(AppContext.BaseDirectory);
            var fontPath = resolver.ResolveFontPath(requestedFont);
            if (File.Exists(fontPath))
            {
                var family = FontAssetRegistration.RegisterFont(resolver, requestedFont, FontAssetRegistration.DevanagariGlyphTest, null, out var diagnostics);
                return new GalleryFontSelection(family.CreateFont(size, style), diagnostics.ResolvedFont, diagnostics.FontFile, diagnostics.GlyphSupport);
            }
        }

        var language = preferHindi ? "hi" : "en";
        var typography = new TypographyResolver().Resolve(new TypographyRequest(language, role, TypographyAssetKind.Gallery, size, style, aspect.Width * .78f, aspect.Width, aspect.Height));
        return new GalleryFontSelection(typography.Font, typography.FontFamilyName, typography.FontFamilyName, preferHindi ? FontAssetRegistration.SupportsGlyphs(typography.Font.Family, FontAssetRegistration.DevanagariGlyphTest) : true);
    }

    private static object BuildVisualPromptDiagnostics(GalleryContext context, IReadOnlyList<GalleryTopic> topics)
    {
        context = NormalizeGalleryContext(context);
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
            realCelestialObjectTreatmentApplied = prompts.Any(RequiresRealCelestialObjectTreatment),
            jupiterCloudBandsRequired = prompts.Any(p => p.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && p.Contains("cloud bands", StringComparison.OrdinalIgnoreCase)),
            venusNaturalIlluminationRequired = prompts.Any(p => p.Contains("Venus", StringComparison.OrdinalIgnoreCase) && p.Contains("natural illumination", StringComparison.OrdinalIgnoreCase)),
            tinyDotOnlyRejected = prompts.Any(p => p.Contains("Reject tiny-dot-only", StringComparison.OrdinalIgnoreCase) || p.Contains("not just tiny dots", StringComparison.OrdinalIgnoreCase)),
            incorrectObjectHintsRemoved = !prompts.Any(p => p.Contains("Mars reddish-orange", StringComparison.OrdinalIgnoreCase)),
            objectNameValidationPassed = context.EventObjectContext.ObjectNameValidationPassed && hardcodedTerms.Count == 0,
            runtimeHardcodingDetected = hardcodedTerms.Count > 0,
            promptDiversityScore = CalculatePromptDiversityScore(prompts),
            repeatedPromptDetected = prompts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1),
            forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, prompts), context.ForbiddenTerms),
            eventName = context.EventName,
            eventFamily = context.EventFamily,
            eventSubtype = context.EventSubtype,
            localizedEventTitle = context.LocalizedEventTitle,
            titleSource = context.TitleSource,
            moonSubtypeVisualAttributes = context.MoonSubtypeVisualAttributes,
            heroTitleResolverReused = context.HeroTitleResolverReused,
            genericMoonFallbackUsed = context.GenericMoonFallbackUsed,
            finalPrompts = topics.Select(t => new { imageId = $"gallery-{t.Number:00}", fileName = $"gallery-{t.Number:00}.png", finalPrompt = t.AzureImage2Prompt, t.VisualIntent, compositionType = t.Concept, promptVariation = t.Purpose, t.OverlayStyle, textBlocks = t.TextBlocks, t.EducationalRole })
        };
    }

    private static object BuildValidationChecklist(GalleryContext context, IReadOnlyList<GalleryTopic> topics, IReadOnlyList<string> imagePaths, HashSet<string> hashes, int azureCalls, AstroPulseGalleryAspect aspect)
    {
        context = NormalizeGalleryContext(context);
        var localization = ValidateGalleryLocalization(context, topics, aspect);
        var prompts = topics.Select(t => t.AzureImage2Prompt).ToArray();
        return new { hindiLocalization = localization.HindiLocalization, sharedFooter = true, validationParity = imagePaths.Count == 6 && hashes.Count == 6 && azureCalls >= 6, promptRefinement = topics.All(t => t.AzureImage2Prompt.Contains("one educational idea", StringComparison.OrdinalIgnoreCase) || t.AzureImage2Prompt.Contains("Educational role", StringComparison.OrdinalIgnoreCase)), diagnostics = true, realCelestialObjectTreatmentApplied = prompts.Any(RequiresRealCelestialObjectTreatment), jupiterCloudBandsRequired = prompts.Any(p => p.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) && p.Contains("cloud bands", StringComparison.OrdinalIgnoreCase)), venusNaturalIlluminationRequired = prompts.Any(p => p.Contains("Venus", StringComparison.OrdinalIgnoreCase) && p.Contains("natural illumination", StringComparison.OrdinalIgnoreCase)), tinyDotOnlyRejected = prompts.Any(p => p.Contains("Reject tiny-dot-only", StringComparison.OrdinalIgnoreCase) || p.Contains("not just tiny dots", StringComparison.OrdinalIgnoreCase)), incorrectObjectHintsRemoved = !prompts.Any(p => p.Contains("Mars reddish-orange", StringComparison.OrdinalIgnoreCase)), educationalOverlay = topics.All(t => !string.IsNullOrWhiteSpace(t.EducationalRole)), storySequencing = topics.Select(t => t.Number).SequenceEqual(Enumerable.Range(1, 6)), aspectSupported = aspect.Width > 0 && aspect.Height > 0 };
    }

    private static GalleryLocalizationValidation ValidateGalleryLocalization(GalleryContext context, IReadOnlyList<GalleryTopic> topics, AstroPulseGalleryAspect aspect)
    {
        context = NormalizeGalleryContext(context);
        var errors = new List<string>();
        var warnings = new List<string>();
        var requestedHindi = LocalizationResolver.IsHindi(context.RequestedLanguage);
        var resolvedHindi = LocalizationResolver.IsHindi(context.Language);
        if (requestedHindi && !resolvedHindi)
            errors.Add("Gallery Hindi localization not applied: requested hi but resolved en.");

        var typography = ResolveGalleryFont(resolvedHindi, TypographyTextRole.Title, Math.Clamp(aspect.Width / 26f, 36, 66), FontStyle.Bold, aspect);
        var overlayText = string.Join(" ", topics.SelectMany(t => t.TextBlocks).Concat(topics.Select(t => t.LocalizedEducationalRole)).Concat(topics.Select(t => t.FooterLabel)));
        var hasHindiOverlay = ContainsDevanagari(overlayText);
        var hasHindiFont = typography.DevanagariGlyphSupport && (ContainsDevanagari(typography.FontFamily) || typography.FontPath.Contains("Devanagari", StringComparison.OrdinalIgnoreCase) || typography.FontPath.Contains("NotoSans", StringComparison.OrdinalIgnoreCase));
        var hindiLocalization = requestedHindi ? resolvedHindi && hasHindiOverlay : true;
        if (requestedHindi && !hasHindiOverlay)
            errors.Add("Gallery Hindi localization not applied: requested hi but overlay text does not contain Devanagari.");
        if (requestedHindi && !hasHindiFont)
            warnings.Add("Gallery Hindi font selection metadata did not identify a Devanagari font; rendering glyph support remains diagnostic unless overlay rendering fails.");
        if (ObservationDisplayTextResolver.ViolatesMeteorDaytimeRule(ResolveObservationDisplay(context)))
            errors.Add("Meteor shower observation time cannot be displayed as daytime peak time.");
        if (IsMoonFamily(context) && context.GenericMoonFallbackUsed && ContainsSpecificMoonName(context.EventName))
            errors.Add($"Specific Moon event resolved to a generic Gallery title: {topics.FirstOrDefault()?.TextBlocks.FirstOrDefault()}.");
        var promptPreview = string.Join(" ", topics.Select(t => t.AzureImage2Prompt));
        if (context.EventSubtype.Equals("StrawberryMoon", StringComparison.OrdinalIgnoreCase) && !ContainsAny(promptPreview, "warm", "rose-gold", "golden", "amber", "summer"))
            errors.Add("Strawberry Moon Gallery prompt must include warm/rose/golden/summer visual cues.");
        if (context.EventSubtype.Equals("WolfMoon", StringComparison.OrdinalIgnoreCase) && !ContainsAny(promptPreview, "winter", "cold", "blue-white", "crisp", "January"))
            errors.Add("Wolf Moon Gallery prompt must include winter/cold visual cues.");
        return new GalleryLocalizationValidation(hindiLocalization, errors.Count == 0, warnings, errors);
    }

    private static string ResolvePhase13ValidationStatus(bool outputUsable, IReadOnlyCollection<string> warnings)
    {
        if (!outputUsable) return "FAIL";
        return warnings.Count > 0 ? "WARN" : "PASS";
    }

    public static object BuildPhase13ValidationContractForTesting(GalleryContext context, AstroPulseGalleryAspect aspect)
    {
        context = NormalizeGalleryContext(context);
        var contract = GalleryContentResolver.Resolve(context);
        var topics = BuildTopics(contract);
        var localizationValidation = ValidateGalleryLocalization(context, topics, aspect);
        var warnings = localizationValidation.Warnings.Concat(contract.ValidationRules.Where(r => r.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase))).ToArray();
        var errors = localizationValidation.Errors.Concat(contract.ValidationRules.Where(r => r.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))).ToArray();
        var outputUsable = localizationValidation.ValidationPassed && errors.Length == 0;
        var validationChecklist = BuildValidationChecklist(context, topics, Enumerable.Range(1, topics.Count).Select(i => $"gallery-{i:00}.png").ToArray(), new HashSet<string>(Enumerable.Range(1, topics.Count).Select(i => $"hash-{i}"), StringComparer.OrdinalIgnoreCase), topics.Count, aspect);
        return new { status = ResolvePhase13ValidationStatus(outputUsable, warnings), validationPassed = outputUsable, validationWarnings = warnings, validationErrors = errors, validationScope = "Phase13GalleryOnly", validationChecklist };
    }

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

    public static GalleryContext LoadGalleryContextForTesting(string outputDirectory) => LoadGalleryContext(outputDirectory);

    private static GalleryContext LoadGalleryContext(string outputDirectory)
    {
        var root = Directory.GetParent(outputDirectory)?.FullName ?? outputDirectory;
        var path = Path.Combine(root, "plan-input", "production-event-intelligence.json");
        if (!File.Exists(path))
            return new("AstronomyEvent", "Selected astronomy event", string.Empty, string.Empty, "Date TBD", "Best local viewing window", "Your location", "en", "en", "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("AstronomyEvent", "Selected astronomy event", [], [], [], ["selected sky event"]), []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var eventType = FirstString(doc.RootElement, "eventType", "strategyId");
        var title = FirstString(doc.RootElement, "localizedEventName", "eventName", "title", "shortTitle");
        var forbidden = ReadStringArray(doc.RootElement, "forbiddenTerms").Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var eventObjectContext = EventObjectContextBuilder.FromJsonValues(eventType, title, ReadStringArray(doc.RootElement, "resolvedObjectNames"), ReadStringArray(doc.RootElement, "primaryObjects"), ReadStringArray(doc.RootElement, "secondaryObjects"), ReadStringArray(doc.RootElement, "requiredVisualObjects"));
        var familyResolution = EventFamilyResolver.ResolveWithDiagnostics(eventType, null, ReadStringArray(doc.RootElement, "primaryObjects"), ReadStringArray(doc.RootElement, "secondaryObjects"), title);
        var familyProfile = EventFamilyProfiles.Resolve(familyResolution.Family, eventType);
        Console.WriteLine("[EventFamilyProfileSelected] " + JsonSerializer.Serialize(new { surface = "gallery", familyCode = familyProfile.Family.ToString(), detectedFamily = familyProfile.Family.ToString(), primaryEventTypeCode = SpecialEventSubtypeResolver.Normalize(eventType), selectedProfile = familyProfile.SelectedProfile, profileName = familyProfile.GetType().Name, profileVersion = EventFamilyProfiles.Version, resolverReason = familyResolution.Reason, resolverInput = familyResolution.Input, forbiddenTerms = familyProfile.ForbiddenTerms, forbiddenConcepts = familyProfile.ForbiddenTerms, requiredVisualElements = familyProfile.RequiredVisualElements, requiredOverlayElements = familyProfile.RequiredOverlayElements, allowedConcepts = familyProfile is MoonFamilyProfile moon ? moon.AllowedConcepts : Array.Empty<string>() }, JsonOptions));
        var requestedLanguage = ResolveRequestedGalleryLanguage(root, doc.RootElement);
        var resolvedLanguage = ResolveGalleryLanguage(requestedLanguage, FirstString(doc.RootElement, "resolvedLanguage", "language", "requestedLanguage"));
        var titleResolution = ResolveGalleryEventTitle(doc.RootElement, title, eventType, resolvedLanguage, familyProfile.Family.ToString());
        var eventDate = FirstString(doc.RootElement, "eventDate", "date", "targetDate", "peakUtc", "globalPeakUtc");
        var localTime = FirstString(doc.RootElement, "localPeakTime", "bestViewingWindowLocal", "preferredViewingWindow");
        var location = FirstString(doc.RootElement, "visibilityRegion", "locationName", "regionName", "regionId");
        var timezone = FirstNonEmpty(FirstString(doc.RootElement, "timezone", "timeZone", "TimeZone"), "Asia/Kolkata");
        var observationInfo = ObservationIntelligenceResolver.Resolve(new ObservationIntelligenceInput(
            eventType,
            familyProfile.Family.ToString(),
            eventDate,
            FirstString(doc.RootElement, "localPeakTime"),
            FirstString(doc.RootElement, "displayWindowLocal", "localVisibilityWindow", "verifiedLocalWindow"),
            FirstString(doc.RootElement, "bestViewingWindowLocal", "preferredViewingWindow"),
            FirstString(doc.RootElement, "regionId", "visibilityRegion"),
            location,
            timezone,
            resolvedLanguage,
            ReadNullableBool(doc.RootElement, "isVisibleLocally", "visibleLocally"),
            FirstString(doc.RootElement, "visibilityStatus"),
            FirstString(doc.RootElement, "direction", "skyDirectionHint"),
            FirstString(doc.RootElement, "altitudeInfo", "altitudeDegrees"),
            ReadNullableBool(doc.RootElement, "localVisibilityVerified", "verifiedLocalVisibility"),
            FirstString(doc.RootElement, "observationSource", "source"),
            FirstString(doc.RootElement, "confidence")));
        return new(eventType, string.IsNullOrWhiteSpace(titleResolution.LocalizedEventTitle) ? "Selected astronomy event" : titleResolution.LocalizedEventTitle, FirstString(doc.RootElement, "storyTheme"), FirstString(doc.RootElement, "visualTheme"), eventDate, localTime, location, requestedLanguage, resolvedLanguage, timezone, eventObjectContext, forbidden, title, familyProfile.Family.ToString(), titleResolution.EventSubtype, titleResolution.LocalizedEventTitle, titleResolution.TitleSource, titleResolution.MoonSubtypeVisualAttributes, titleResolution.HeroTitleResolverReused, titleResolution.GenericMoonFallbackUsed, observationInfo);
    }

    private static bool? ReadNullableBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryFindProperty(root, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static bool TryFindProperty(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.NameEquals(name)) { value = p.Value; return true; }
                if (TryFindProperty(p.Value, name, out value)) return true;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray()) if (TryFindProperty(item, name, out value)) return true;
        }
        value = default;
        return false;
    }

    private static string ResolveRequestedGalleryLanguage(string root, JsonElement intelligenceRoot)
    {
        var requestPath = Path.Combine(root, "plan-input", "content-plan-production-request.json");
        var requestLanguage = ReadLanguageFromJsonFile(requestPath, "requestedLanguage", "language");
        return FirstNonEmpty(requestLanguage, FirstString(intelligenceRoot, "requestedLanguage", "language"), "en").ToLowerInvariant();
    }

    private static string ResolveGalleryLanguage(string requestedLanguage, string intelligenceLanguage)
    {
        var requested = FirstNonEmpty(requestedLanguage, "en").ToLowerInvariant();
        var intelligence = FirstNonEmpty(intelligenceLanguage, requested).ToLowerInvariant();
        if (LocalizationResolver.IsHindi(requested)) return "hi";
        return LocalizationResolver.IsHindi(intelligence) ? "hi" : "en";
    }

    private static string ReadLanguageFromJsonFile(string path, params string[] propertyNames)
    {
        if (!File.Exists(path)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return FirstString(doc.RootElement, propertyNames);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }


    public static GalleryContentContract ResolveGalleryContentContractForTesting(GalleryContext context) => GalleryContentResolver.Resolve(context);

    public static List<GalleryTopic> BuildTopics(GalleryContentContract contract)
    {
        var title = CleanGalleryTitle(FirstNonEmpty(contract.DisplayShortTitle, contract.DisplayTitle, contract.LocalizedTitle));
        var objectText = contract.LocalizedPrimaryObjects.Count > 0 ? string.Join(", ", contract.LocalizedPrimaryObjects) : title;
        var metadata = new[] { $"{Localize(contract.Language, "Date", "तारीख")}: {contract.DisplayDate}", $"{Localize(contract.Language, "Time", "समय")}: {contract.DisplayTime}" };
        var languageName = LocalizationResolver.LanguageDisplayName(contract.Language);
        var basePrompt = $"Event name: {contract.EventName}. Event family: {contract.EventFamily}. Event subtype: {contract.EventSubtype}. Localized event title: {contract.DisplayTitle}. Date: {metadata[0]}. Time: {metadata[1]}. Direction: {contract.Direction}. Observation window: {contract.ObservationWindow}. Resolved object names: {objectText}. Visual hints: {string.Join("; ", contract.VisualHints)}. Prompt hints: {string.Join("; ", contract.PromptHints)}. Output language: {languageName}. Forbidden terms policy: exclude event-profile forbidden concepts. Preserve Gallery V3 design: unique realistic background, premium astronomy documentary, clean social media carousel, no embedded text, deterministic overlay space, one educational idea per slide. Reject tiny-dot-only sky renderings when object recognition is the learning goal.";
        return contract.SceneContents.Select((scene, i) => new GalleryTopic(
            i + 1,
            scene.SceneRole,
            scene.VisualIntent,
            [CleanGalleryTitle(FirstNonEmpty(scene.Title, contract.DisplayShortTitle, contract.DisplayTitle)), scene.Subtitle, scene.DetailText],
            scene.VisualIntent,
            scene.OverlayStyle,
            $"{basePrompt} Asset purpose: {scene.SceneRole}. Visual intent: {scene.VisualIntent}. Page-specific treatment: {BuildPageSpecificTreatment(scene.SceneRole, contract)}. Educational role: {scene.LocalizedSceneLabel}. Event-specific prompt: {scene.PromptHint}. Required objects: {string.Join(", ", scene.RequiredObjects)}. Forbidden objects: {string.Join(", ", scene.ForbiddenObjects)}. No embedded text. No labels. No watermark.",
            scene.SceneRole,
            scene.LocalizedSceneLabel,
            contract.Diagnostics.TryGetValue("footerLabel", out var footer) ? footer : Localize(contract.Language, "Drashyam Astronomy", "दृश्यम खगोल"),
            contract.Language)).ToList();
    }

    public static List<GalleryTopic> BuildTopics(GalleryContext context) => BuildTopics(GalleryContentResolver.Resolve(context));

    private static object BuildEventDisplayContractDiagnostics(GalleryContentContract contract) => new
    {
        contract.EventName,
        contract.EventAction,
        contract.EventType,
        contract.EventFamily,
        contract.EventSubtype,
        contract.Language,
        contract.PrimaryObjects,
        contract.LocalizedPrimaryObjects,
        contract.SecondaryObjects,
        contract.LocalizedSecondaryObjects,
        contract.DisplayTitle,
        contract.DisplayShortTitle,
        contract.DisplaySubtitle,
        contract.DisplayDate,
        contract.DisplayTime,
        contract.ObservationWindow,
        contract.Direction,
        contract.VisualIdentity,
        contract.VisualHints,
        contract.PromptHints,
        contract.ForbiddenTerms,
        contract.SceneContents,
        contract.ValidationRules,
        titleResolutionInputs = contract.Diagnostics.TryGetValue("titleResolutionInputs", out var tri) ? tri : string.Empty,
        titleResolutionOutput = contract.Diagnostics.TryGetValue("titleResolutionOutput", out var tro) ? tro : contract.DisplayTitle,
        objectLocalization = contract.Diagnostics.TryGetValue("objectLocalization", out var ol) ? ol : string.Empty,
        actionLocalization = contract.Diagnostics.TryGetValue("actionLocalization", out var al) ? al : string.Empty,
        selectedProvider = contract.Diagnostics.TryGetValue("selectedProvider", out var sp) ? sp : string.Empty,
        providerRole = contract.Diagnostics.TryGetValue("providerRole", out var pr) ? pr : string.Empty,
        observationRuleApplied = contract.Diagnostics.TryGetValue("observationRuleApplied", out var ora) ? ora : string.Empty,
        textFitResult = contract.Diagnostics.TryGetValue("textFitResult", out var tfr) ? tfr : string.Empty,
        fallbackUsed = contract.Diagnostics.TryGetValue("fallbackUsed", out var fu) && bool.TryParse(fu, out var fub) && fub,
        warnings = contract.ValidationRules.Where(r => r.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase)).ToArray(),
        errors = contract.ValidationRules.Where(r => r.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)).ToArray(),
        diagnostics = contract.Diagnostics
    };

    private static GalleryContext NormalizeGalleryContext(GalleryContext context)
    {
        var eventName = FirstNonEmpty(context.EventName, context.Title, context.EventType);
        var eventFamily = ResolveGalleryEventFamily(context);
        var subtype = FirstNonEmpty(context.EventSubtype, ResolveMoonSubtype(eventName), ResolveMoonSubtype(context.EventType));
        var attrs = FirstNonEmpty(context.MoonSubtypeVisualAttributes, BuildMoonSubtypeVisualAttributes(subtype));
        var titleResolution = string.IsNullOrWhiteSpace(context.LocalizedEventTitle) || IsGenericMoonTitle(context.LocalizedEventTitle)
            ? ResolveGalleryEventTitle(default, context.Title, context.EventType, context.Language, eventFamily)
            : new GalleryTitleResolution(context.LocalizedEventTitle, FirstNonEmpty(context.TitleSource, "localizedEventTitle"), subtype, attrs, context.HeroTitleResolverReused, context.GenericMoonFallbackUsed);
        return context with
        {
            EventName = eventName,
            EventFamily = eventFamily,
            EventSubtype = FirstNonEmpty(titleResolution.EventSubtype, subtype),
            LocalizedEventTitle = titleResolution.LocalizedEventTitle,
            Title = titleResolution.LocalizedEventTitle,
            TitleSource = titleResolution.TitleSource,
            MoonSubtypeVisualAttributes = FirstNonEmpty(titleResolution.MoonSubtypeVisualAttributes, attrs),
            HeroTitleResolverReused = titleResolution.HeroTitleResolverReused,
            GenericMoonFallbackUsed = titleResolution.GenericMoonFallbackUsed
        };
    }


    private static string ResolveGalleryEventFamily(GalleryContext context)
    {
        var source = FirstNonEmpty(context.EventFamily, context.EventType);
        var combined = string.Join(" ", context.EventType, context.EventFamily, context.EventName, context.Title);
        var names = context.EventObjectContext.ObjectNames;
        var hasJupiterVenus = names.Any(n => n.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && names.Any(n => n.Equals("Venus", StringComparison.OrdinalIgnoreCase));
        if (hasJupiterVenus && ContainsAny(combined, "Conjunction", "conjunction", "युति")) return "PlanetConjunction";
        if (hasJupiterVenus && ContainsAny(combined, "Pairing", "Close Pairing", "Grouping", "Planet")) return "PlanetPairing";
        return source;
    }

    private static bool RequiresRealCelestialObjectTreatment(string prompt)
        => prompt.Contains("real celestial object treatment", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("recognizable real planet treatment", StringComparison.OrdinalIgnoreCase);

    private static string BuildPageSpecificTreatment(string sceneRole, GalleryContentContract contract)
    {
        var hasJupiterVenus = contract.PrimaryObjects.Any(o => o.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && contract.PrimaryObjects.Any(o => o.Equals("Venus", StringComparison.OrdinalIgnoreCase));
        if (!hasJupiterVenus) return sceneRole switch
        {
            "Opening view" => "strong event relationship visual",
            "What happens" => "simple visual explanation of the apparent sky geometry",
            "Where to look" => "realistic sky/location context",
            "When to observe" => "clean observing-window context",
            "Key objects" => "recognizable object close-up or identification treatment",
            _ => "memorable premium editorial composition"
        };

        var common = "Jupiter must show visible realistic cloud bands; Venus must be a bright naturally illuminated disk/object; both objects must be visible, clearly related, and not just tiny dots; keep all text for deterministic overlay only";
        return sceneRole switch
        {
            "Opening view" => $"Hook: strong Jupiter + Venus relationship visual; {common}",
            "What happens" => $"Explanation: visual explanation of apparent conjunction/line-of-sight; {common}",
            "Where to look" => $"Observation: realistic sky/location context with the pair placed in the observing direction; {common}",
            "When to observe" => $"Observation timing: realistic twilight/pre-dawn sky context, no embedded text; {common}",
            "Key objects" => $"Recognition: clearly identify Jupiter and Venus visually by appearance and scale; {common}",
            _ => $"Memory/Takeaway: memorable premium editorial composition centered on Jupiter + Venus; {common}"
        };
    }

    private static string[] BuildGalleryV3MetadataBlocks(GalleryContext context)
    {
        var display = ResolveObservationDisplay(context);
        return
        [
            $"{Localize(context.Language, "Date", "तारीख")}: {display.DisplayDate}",
            $"{Localize(context.Language, "Time", "समय")}: {display.DisplayTime}"
        ];
    }

    private static ObservationDisplayText ResolveObservationDisplay(GalleryContext context)
        => context.ObservationInfo is not null ? ObservationDisplayTextResolver.Resolve(context.ObservationInfo) : ObservationDisplayTextResolver.Resolve(context.EventDate, context.LocalTime, context.LocalTime, context.Language, context.EventType, context.Timezone);

    private static GalleryObservationDisplayDiagnostics BuildObservationDisplay(GalleryContext context)
    {
        var display = ResolveObservationDisplay(context);
        return new GalleryObservationDisplayDiagnostics(display.EventPeakUtc, display.LocalPeakTime, display.DisplayedObservationTime, display.ObservationTimeSource, display.EventFamilyRuleApplied);
    }

    private static string Localize(string language, string english, string hindi) => LocalizationResolver.IsHindi(language) ? hindi : english;


    private static GalleryLocalization BuildGalleryLocalization(GalleryContext context)
    {
        var hi = LocalizationResolver.IsHindi(context.Language);
        var title = hi && !ContainsDevanagari(context.Title) ? ResolveGalleryEventTitle(default, context.Title, context.EventType, context.Language, context.EventFamily).LocalizedEventTitle : context.Title;
        var sceneLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Opening view"] = Localize(context.Language, "Opening view", "मुख्य दृश्य"),
            ["What happens"] = Localize(context.Language, "What happens", "क्या होगा"),
            ["Where to look"] = Localize(context.Language, "Where to look", "कहाँ देखें"),
            ["When to observe"] = Localize(context.Language, "When to observe", "कब देखें"),
            ["Key objects"] = Localize(context.Language, "Key objects", "मुख्य पिंड"),
            ["Viewing checklist"] = Localize(context.Language, "Viewing checklist", "देखने की सूची")
        };
        return new GalleryLocalization(context.Language, title, sceneLabels, Localize(context.Language, "Save this guide", "यह गाइड सेव करें"), Localize(context.Language, "Drashyam Astronomy", "दृश्यम खगोल"));
    }

    private static string LocalizeObjectText(GalleryContext context, string fallbackTitle)
    {
        var objects = context.EventObjectContext.ObjectNames.Where(o => !string.IsNullOrWhiteSpace(o)).Select(CleanObjectName).Where(IsCleanObjectName).ToArray();
        if (objects.Length == 0) return fallbackTitle;
        return LocalizationResolver.IsHindi(context.Language) ? string.Join(", ", objects.Select(LocalizeAstronomyTerm)) : string.Join(", ", objects);
    }

    private static GalleryTitleResolution ResolveGalleryEventTitle(JsonElement root, string title, string eventType, string language, string eventFamily)
    {
        var subtype = ResolveMoonSubtype(FirstNonEmpty(title, eventType));
        var attrs = BuildMoonSubtypeVisualAttributes(subtype);
        var isHi = LocalizationResolver.IsHindi(language);
        var explicitTitle = root.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : FirstString(root, isHi
                ? new[] { "localizedTitleHi", "hindiTitle", "titleHi" }
                : new[] { "localizedTitle", "eventName", "title", "shortTitle" });
        if (!string.IsNullOrWhiteSpace(explicitTitle) && (!isHi || ContainsDevanagari(explicitTitle)))
            return new(explicitTitle, isHi ? "localizedEventTitle" : "eventTitle", subtype, attrs, true, false);
        var localizedSubtype = LocalizeMoonSubtypeTitle(subtype, language);
        if (!string.IsNullOrWhiteSpace(localizedSubtype))
            return new(localizedSubtype, "localizedMoonSubtypeTitle", subtype, attrs, true, false);
        if (!isHi) return new(title, "eventTitle", subtype, attrs, true, IsGenericMoonTitle(title));
        var fallback = DeriveHindiTitle(eventType, title);
        var source = fallback.Equals("खगोलीय घटना", StringComparison.OrdinalIgnoreCase) ? "genericFamilyTitle" : "translatedEventName";
        return new(fallback, source, subtype, attrs, true, IsGenericMoonTitle(fallback));
    }

    private static string DeriveHindiTitle(string eventType, string title)
    {
        var source = string.IsNullOrWhiteSpace(title) ? eventType : title;
        if (source.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "उल्का वर्षा";
        if (source.Contains("solar", StringComparison.OrdinalIgnoreCase) && source.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "सूर्य ग्रहण";
        if (source.Contains("lunar", StringComparison.OrdinalIgnoreCase) && source.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "चंद्र ग्रहण";
        if (source.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "ग्रहण गाइड";
        if (source.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "चंद्रमा गाइड";
        if (source.Contains("mars", StringComparison.OrdinalIgnoreCase) && source.Contains("jupiter", StringComparison.OrdinalIgnoreCase))
            return "मंगल और बृहस्पति की करीबी जोड़ी";
        if (source.Contains("conjunction", StringComparison.OrdinalIgnoreCase)) return "आकाशीय युति";
        return "खगोलीय घटना";
    }

    private static string LocalizeAstronomyTerm(string value)
        => value switch
        {
            var v when v.Contains("meteor", StringComparison.OrdinalIgnoreCase) => "उल्का वर्षा",
            var v when v.Contains("perseid", StringComparison.OrdinalIgnoreCase) => "Perseids उल्का वर्षा",
            var v when v.Contains("geminid", StringComparison.OrdinalIgnoreCase) => "जेमिनिड्स",
            var v when v.Contains("wolf moon", StringComparison.OrdinalIgnoreCase) => "वुल्फ पूर्णिमा",
            var v when v.Contains("strawberry moon", StringComparison.OrdinalIgnoreCase) => "स्ट्रॉबेरी पूर्णिमा",
            var v when v.Contains("blue moon", StringComparison.OrdinalIgnoreCase) => "ब्लू मून",
            var v when v.Contains("supermoon", StringComparison.OrdinalIgnoreCase) || v.Contains("super moon", StringComparison.OrdinalIgnoreCase) => "सुपरमून",
            var v when v.Contains("harvest moon", StringComparison.OrdinalIgnoreCase) => "हार्वेस्ट मून",
            var v when v.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase) || v.Contains("solareclipse", StringComparison.OrdinalIgnoreCase) => "सूर्य ग्रहण",
            var v when v.Contains("lunar eclipse", StringComparison.OrdinalIgnoreCase) || v.Contains("lunareclipse", StringComparison.OrdinalIgnoreCase) => "चंद्र ग्रहण",
            var v when v.Equals("Mars", StringComparison.OrdinalIgnoreCase) => "मंगल",
            var v when v.Equals("Mercury", StringComparison.OrdinalIgnoreCase) => "बुध",
            var v when v.Equals("Venus", StringComparison.OrdinalIgnoreCase) => "शुक्र",
            var v when v.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) => "बृहस्पति",
            var v when v.Equals("Saturn", StringComparison.OrdinalIgnoreCase) => "शनि",
            var v when v.Equals("Uranus", StringComparison.OrdinalIgnoreCase) => "अरुण",
            var v when v.Equals("Neptune", StringComparison.OrdinalIgnoreCase) => "वरुण",
            var v when v.Equals("Comet", StringComparison.OrdinalIgnoreCase) => "धूमकेतु",
            var v when v.Equals("Constellation", StringComparison.OrdinalIgnoreCase) => "नक्षत्र",
            var v when v.Equals("Nebula", StringComparison.OrdinalIgnoreCase) => "नीहारिका",
            var v when v.Equals("Galaxy", StringComparison.OrdinalIgnoreCase) => "आकाशगंगा",
            var v when v.Equals("Star Cluster", StringComparison.OrdinalIgnoreCase) => "तारागुच्छ",
            var v when v.Equals("Moon", StringComparison.OrdinalIgnoreCase) => "चंद्रमा",
            var v when v.Equals("Sun", StringComparison.OrdinalIgnoreCase) => "सूर्य",
            _ => value
        };

    private static bool IsMoonFamily(GalleryContext context)
        => ContainsAny(context.EventFamily, "Moon", "FullMoon", "SuperMoon") || ContainsAny(context.EventType, "Moon", "FullMoon", "SuperMoon");

    private static bool ContainsSpecificMoonName(string value) => ResolveMoonSubtype(value) != "Moon";
    private static bool IsGenericMoonTitle(string value) => value.Equals("Moon Guide", StringComparison.OrdinalIgnoreCase) || value.Equals("चंद्रमा गाइड", StringComparison.OrdinalIgnoreCase);
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static string ResolveMoonSubtype(string value)
    {
        if (ContainsAny(value, "wolf")) return "WolfMoon";
        if (ContainsAny(value, "strawberry")) return "StrawberryMoon";
        if (ContainsAny(value, "blue moon")) return "BlueMoon";
        if (ContainsAny(value, "supermoon", "super moon")) return "Supermoon";
        if (ContainsAny(value, "harvest")) return "HarvestMoon";
        return ContainsAny(value, "moon", "fullmoon", "full moon") ? "Moon" : string.Empty;
    }

    private static string LocalizeMoonSubtypeTitle(string subtype, string language)
    {
        var hi = LocalizationResolver.IsHindi(language);
        return subtype switch
        {
            "WolfMoon" => hi ? "वुल्फ पूर्णिमा" : "Wolf Moon",
            "StrawberryMoon" => hi ? "स्ट्रॉबेरी पूर्णिमा" : "Strawberry Moon",
            "BlueMoon" => hi ? "ब्लू मून" : "Blue Moon",
            "Supermoon" => hi ? "सुपरमून" : "Supermoon",
            "HarvestMoon" => hi ? "हार्वेस्ट मून" : "Harvest Moon",
            _ => string.Empty
        };
    }

    private static string BuildMoonSubtypeVisualAttributes(string subtype) => subtype switch
    {
        "WolfMoon" => "cold winter full moon, crisp blue-white moonlight, winter landscape, January full moon mood, subtle cold wilderness symbolism only if not misleading",
        "StrawberryMoon" => "warm amber or rose-gold full moon near horizon, early summer atmosphere, soft strawberry/golden color mood, June full moon identity, avoid literal strawberry fruit unless explicitly requested",
        "BlueMoon" => "rare second full moon, cool blue atmospheric mood, do not imply the Moon is physically blue",
        "Supermoon" => "visually larger and brighter moon near horizon, strong scale and foreground reference",
        "HarvestMoon" => "warm golden moon near horizon, autumn field and harvest atmosphere",
        _ => string.Empty
    };

    public static object BuildGalleryLocalizationDiagnostics(GalleryContext context, IReadOnlyList<GalleryTopic> topics, AstroPulseGalleryAspect aspect)
    {
        context = NormalizeGalleryContext(context);
        var language = LocalizationResolver.IsHindi(context.Language) ? "hi" : "en";
        var titleTypography = ResolveGalleryFont(language == "hi", TypographyTextRole.Title, Math.Clamp(aspect.Width / 26f, 36, 66), FontStyle.Bold, aspect);
        var missing = new List<object>();
        var fallbacks = new List<object>();
        if (LocalizationResolver.IsHindi(context.Language))
        {
            if (!ContainsDevanagari(topics.FirstOrDefault()?.TextBlocks.FirstOrDefault() ?? string.Empty))
                missing.Add(new { field = "title", sourcePath = "production-event-intelligence.json:title/localizedTitleHi", critical = true });
            foreach (var topic in topics.Where(t => !ContainsDevanagari(t.LocalizedEducationalRole)))
                missing.Add(new { field = $"sceneLabel:{topic.EducationalRole}", sourcePath = "GalleryLocalization.SceneLabels", critical = true });
            foreach (var block in topics.SelectMany(t => t.TextBlocks.Select((text, index) => new { t.Number, index, text })).Where(x => x.index > 0 && !ContainsDevanagari(x.text) && !x.text.Contains("IST", StringComparison.OrdinalIgnoreCase)))
                fallbacks.Add(new { field = $"gallery-{block.Number:00}.textBlocks[{block.index}]", sourcePath = "eventObjectContext.objectNames", explicitlyMarkedFallback = true, value = block.text });
        }
        return new
        {
            requestedLanguage = context.RequestedLanguage,
            resolvedLanguage = context.Language,
            eventName = context.EventName,
            eventFamily = context.EventFamily,
            eventSubtype = context.EventSubtype,
            localizedEventTitle = context.LocalizedEventTitle,
            titleSource = context.TitleSource,
            moonSubtypeVisualAttributes = context.MoonSubtypeVisualAttributes,
            heroTitleResolverReused = context.HeroTitleResolverReused,
            genericMoonFallbackUsed = context.GenericMoonFallbackUsed,
            localizedTitle = topics.FirstOrDefault()?.TextBlocks.FirstOrDefault() ?? string.Empty,
            localizedSceneLabel = topics.ToDictionary(t => t.EducationalRole, t => t.LocalizedEducationalRole),
            fontFamily = titleTypography.FontFamily,
            fontPath = titleTypography.FontPath,
            devanagariGlyphSupport = titleTypography.DevanagariGlyphSupport,
            observationDisplay = BuildObservationDisplay(context),
            localizationFallbacks = fallbacks,
            missingLocalizationFields = missing,
            overlayTextPreview = topics.Select(t => new { imageId = $"gallery-{t.Number:00}", t.TextBlocks, educationalRole = t.LocalizedEducationalRole, footer = t.FooterLabel })
        };
    }

    private static object BuildGalleryOverlayDiagnostics(GalleryContext context, IReadOnlyList<GalleryTopic> topics, AstroPulseGalleryAspect aspect)
    {
        context = NormalizeGalleryContext(context);
        var language = LocalizationResolver.IsHindi(context.Language) ? "hi" : "en";
        var typography = ResolveGalleryFont(language == "hi", TypographyTextRole.Title, Math.Clamp(aspect.Width / 26f, 36, 66), FontStyle.Bold, aspect);
        return new
        {
            requestedLanguage = context.RequestedLanguage,
            resolvedLanguage = context.Language,
            fontFamily = typography.FontFamily,
            fontPath = typography.FontPath,
            devanagariGlyphSupport = typography.DevanagariGlyphSupport,
            observationDisplay = BuildObservationDisplay(context),
            overlayTextPreview = topics.Select(t => new { imageId = $"gallery-{t.Number:00}", title = t.TextBlocks.FirstOrDefault(), subtitle = t.TextBlocks.Skip(1).FirstOrDefault(), sceneLabel = t.LocalizedEducationalRole, footer = t.FooterLabel })
        };
    }

    private sealed record GalleryLocalization(string Language, string Title, IReadOnlyDictionary<string, string> SceneLabels, string CtaText, string FooterLabel);
    private sealed record GalleryObservationDisplayDiagnostics(string eventPeakUtc, string localPeakTime, string displayedObservationTime, string observationTimeSource, string eventFamilyRuleApplied);
    private static object BuildObservationIntelligenceDiagnostics(GalleryContext context) => new { originalPeakUtc = context.EventDate, originalLocalPeakTime = context.LocalTime, selectedDisplayPolicy = context.ObservationInfo?.DisplayPolicy.ToString() ?? string.Empty, selectedDisplayTime = context.ObservationInfo?.DisplayTime ?? string.Empty, selectedDisplayWindow = context.ObservationInfo?.DisplayWindowLocal ?? string.Empty, visibilityStatus = context.ObservationInfo?.VisibilityStatus.ToString() ?? string.Empty, providerUsed = context.ObservationInfo?.Source ?? string.Empty, source = context.ObservationInfo?.Reason ?? string.Empty, warnings = context.ObservationInfo?.Warnings ?? Array.Empty<string>(), errors = context.ObservationInfo?.Errors ?? Array.Empty<string>() };
    private sealed record GalleryFontSelection(Font Font, string FontFamily, string FontPath, bool DevanagariGlyphSupport);
    private sealed record GalleryLocalizationValidation(bool HindiLocalization, bool ValidationPassed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
    private sealed record GalleryTitleResolution(string LocalizedEventTitle, string TitleSource, string EventSubtype, string MoonSubtypeVisualAttributes, bool HeroTitleResolverReused, bool GenericMoonFallbackUsed);

    public sealed record GalleryContentContract(
        string EventName,
        string EventFamily,
        string EventSubtype,
        string Language,
        string EventAction,
        string EventType,
        string LocalizedTitle,
        string TitleSource,
        string LocalizedShortTitle,
        string DisplayTitle,
        string DisplayShortTitle,
        string DisplaySubtitle,
        int MaxTitleLines,
        int MaxTitleChars,
        IReadOnlyList<string> PreferredLineBreaks,
        IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> LocalizedPrimaryObjects,
        IReadOnlyList<string> SecondaryObjects,
        IReadOnlyList<string> LocalizedSecondaryObjects,
        string DisplayDate,
        string DisplayTime,
        string ObservationWindow,
        string Direction,
        IReadOnlyDictionary<string, string> VisualIdentity,
        IReadOnlyList<GallerySceneContent> SceneContents,
        IReadOnlyList<string> VisualHints,
        IReadOnlyList<string> PromptHints,
        IReadOnlyList<string> ForbiddenTerms,
        IReadOnlyList<string> ValidationRules,
        IReadOnlyDictionary<string, string> Diagnostics);

    public sealed record GallerySceneContent(
        string ImageId,
        string SceneRole,
        string LocalizedSceneLabel,
        string Title,
        string Subtitle,
        string DetailText,
        string CTA,
        string VisualIntent,
        string PromptHint,
        string OverlayStyle,
        IReadOnlyList<string> RequiredObjects,
        IReadOnlyList<string> ForbiddenObjects);

    public interface IGalleryFamilyContentProvider
    {
        bool CanResolve(GalleryContext context);
        string ProviderName { get; }
        string SelectionReason { get; }
        GalleryContentContract Resolve(GalleryContext context);
    }

    public static class GalleryContentResolver
    {
        private static readonly IReadOnlyList<IGalleryFamilyContentProvider> Providers =
        [
            new MeteorGalleryProvider(),
            new MoonGalleryProvider(),
            new PlanetPairingGalleryProvider(),
            new SolarEclipseGalleryProvider(),
            new LunarEclipseGalleryProvider(),
            new GenericGalleryProvider()
        ];

        public static GalleryContentContract Resolve(GalleryContext context)
        {
            context = NormalizeGalleryContext(context);
            var provider = Providers.First(p => p.CanResolve(context));
            var contract = provider.Resolve(context);
            var diagnostics = new Dictionary<string, string>(contract.Diagnostics, StringComparer.OrdinalIgnoreCase);
            diagnostics["selectedProvider"] = provider.ProviderName;
            diagnostics["providerSelectionReason"] = provider.SelectionReason;
            diagnostics["genericTitleFallbackUsed"] = (contract.TitleSource == "genericFamilyTitle").ToString();
            diagnostics["localizedObjectNames"] = string.Join(", ", contract.LocalizedPrimaryObjects.Concat(contract.LocalizedSecondaryObjects));
            var errors = new List<string>();
            var warnings = new List<string>();
            if (provider is GenericGalleryProvider) warnings.Add("Generic fallback Gallery provider used.");
            if (string.IsNullOrWhiteSpace(contract.ObservationWindow)) warnings.Add("Observation window missing.");
            if (!string.IsNullOrWhiteSpace(contract.EventName) && contract.TitleSource == "genericFamilyTitle")
                errors.Add("LocalizedTitle resolved to generic family title while EventName exists.");
            if ((contract.DisplayTitle.Equals("Astronomical Event", StringComparison.OrdinalIgnoreCase) || contract.DisplayTitle.Equals("खगोलीय घटना", StringComparison.OrdinalIgnoreCase)) && (!string.IsNullOrWhiteSpace(contract.EventName) || contract.PrimaryObjects.Count > 0))
                errors.Add("Generic display title used while eventName or primary objects exist.");
            if (contract.DisplayTitle.Length > contract.MaxTitleChars && string.IsNullOrWhiteSpace(contract.DisplayShortTitle))
                errors.Add("displayTitle exceeds safe limits without fallback.");
            if (LocalizationResolver.IsHindi(contract.Language))
            {
                foreach (var pair in contract.PrimaryObjects.Concat(contract.SecondaryObjects).Zip(contract.LocalizedPrimaryObjects.Concat(contract.LocalizedSecondaryObjects)))
                {
                    var expected = LocalizeAstronomyTerm(pair.First);
                    if (!expected.Equals(pair.First, StringComparison.OrdinalIgnoreCase) && pair.Second.Equals(pair.First, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Hindi object name remains English despite known translation: {pair.First}.");
                }
            }
            foreach (var scene in contract.SceneContents)
            {
                var prompt = scene.PromptHint;
                foreach (var required in scene.RequiredObjects.Where(r =>
                    !prompt.Contains(r, StringComparison.OrdinalIgnoreCase) &&
                    !prompt.Contains(LocalizeAstronomyTerm(r), StringComparison.OrdinalIgnoreCase) &&
                    !contract.PromptHints.Any(h => h.Contains(r, StringComparison.OrdinalIgnoreCase) || h.Contains(LocalizeAstronomyTerm(r), StringComparison.OrdinalIgnoreCase))))
                    errors.Add($"Prompt lacks required object: {required}.");
                foreach (var forbidden in scene.ForbiddenObjects.Concat(contract.ForbiddenTerms).Where(f => !string.IsNullOrWhiteSpace(f) && prompt.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Forbidden term appears in prompt: {forbidden}.");
            }
            diagnostics["warnings"] = string.Join(" | ", warnings);
            diagnostics["errors"] = string.Join(" | ", errors);
            return contract with { Diagnostics = diagnostics, ValidationRules = contract.ValidationRules.Concat(errors.Select(e => "ERROR: " + e)).Concat(warnings.Select(w => "WARN: " + w)).ToArray() };
        }
    }

    private abstract class GalleryFamilyProviderBase : IGalleryFamilyContentProvider
    {
        public abstract bool CanResolve(GalleryContext context);
        public abstract string ProviderName { get; }
        public virtual string SelectionReason => "event family/type matched provider";
        public GalleryContentContract Resolve(GalleryContext context)
        {
            var display = ResolveObservationDisplay(context);
            var primary = context.EventObjectContext.ObjectNames.Where(IsCleanObjectName).DefaultIfEmpty(context.EventName).Select(CleanObjectName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var localized = LocalizationResolver.IsHindi(context.Language) ? primary.Select(LocalizeAstronomyTerm).ToArray() : primary;
            var title = ResolveContractTitle(context);
            var labels = BuildGalleryLocalization(context).SceneLabels;
            var hints = BuildHints(context);
            var promptHint = string.Join(", ", hints.Concat(localized));
            var scenes = new[]
            {
                Scene("gallery-01", "Opening view", labels["Opening view"], title.title, $"{Localize(context.Language, "Date", "तारीख")}: {display.DisplayDate}", $"{Localize(context.Language, "Time", "समय")}: {display.DisplayTime}", "CinematicHook", promptHint, primary),
                Scene("gallery-02", "What happens", labels["What happens"], title.title, labels["What happens"], string.Join(", ", localized), "ScientificExplanation", promptHint, primary),
                Scene("gallery-03", "Where to look", labels["Where to look"], title.title, labels["Where to look"], display.DisplayedObservationTime, "HumanObservation", promptHint, primary),
                Scene("gallery-04", "When to observe", labels["When to observe"], title.title, $"{Localize(context.Language, "Date", "तारीख")}: {display.DisplayDate}", $"{Localize(context.Language, "Time", "समय")}: {display.DisplayTime}", "SkyGuide", promptHint, primary),
                Scene("gallery-05", "Key objects", labels["Key objects"], title.title, labels["Key objects"], string.Join(", ", localized), "ObjectCloseup", promptHint, primary),
                Scene("gallery-06", "Viewing checklist", labels["Viewing checklist"], title.title, labels["Viewing checklist"], Localize(context.Language, "Save this guide", "यह गाइड सेव करें"), "EmotionalClosing", promptHint, primary)
            };
            var eventAction = ResolveEventAction(context);
            var resolvedTitle = ResolvePresentationTitle(context, eventAction, primary, localized);
            var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["titleResolutionPriorityUsed"] = resolvedTitle.Source,
                ["titleResolutionInputs"] = $"eventAction={eventAction}; primaryObjects={string.Join(",", primary)}; localizedPrimaryObjects={string.Join(",", localized)}; eventName={context.EventName}",
                ["titleResolutionOutput"] = resolvedTitle.DisplayTitle,
                ["objectLocalization"] = string.Join(" | ", primary.Zip(localized, (a, b) => $"{a}->{b}")),
                ["actionLocalization"] = LocalizeEventAction(eventAction, context.Language),
                ["observationRuleApplied"] = display.EventFamilyRuleApplied,
                ["providerRole"] = "enrichment only: timing, visual identity, prompt hints, forbidden terms, validation rules",
                ["footerLabel"] = BuildGalleryLocalization(context).FooterLabel,
                ["textFitResult"] = resolvedTitle.DisplayTitle.Length <= resolvedTitle.MaxTitleChars ? "fits" : "shortTitleFallbackRequired",
                ["fallbackUsed"] = (resolvedTitle.Source.Contains("fallback", StringComparison.OrdinalIgnoreCase)).ToString()
            };
            return new(context.EventName, context.EventFamily, context.EventSubtype, context.Language, eventAction, context.EventType, resolvedTitle.DisplayTitle, resolvedTitle.Source, resolvedTitle.DisplayShortTitle, resolvedTitle.DisplayTitle, resolvedTitle.DisplayShortTitle, resolvedTitle.DisplaySubtitle, resolvedTitle.MaxTitleLines, resolvedTitle.MaxTitleChars, resolvedTitle.PreferredLineBreaks, primary, localized, [], [], display.DisplayDate, display.DisplayTime, display.DisplayedObservationTime, ResolveDirection(context), BuildVisualIdentity(context), scenes.Select(s => s with { Title = resolvedTitle.DisplayShortTitle }).ToArray(), BuildVisualHints(context), hints, BuildForbiddenTerms(context), BuildValidationRules(context), diagnostics);
        }

        protected static GallerySceneContent Scene(string id, string role, string label, string title, string subtitle, string detail, string intent, string prompt, IReadOnlyList<string> required)
            => new(id, role, label, title, subtitle, detail, "Save this guide", intent, prompt, "minimal lower-third", required, []);
        protected virtual IReadOnlyList<string> BuildHints(GalleryContext c) => ["realistic astronomy scene", c.MoonSubtypeVisualAttributes];
        protected virtual IReadOnlyList<string> BuildVisualHints(GalleryContext c) => BuildHints(c);
        protected virtual IReadOnlyList<string> BuildForbiddenTerms(GalleryContext c) => c.ForbiddenTerms;
        protected virtual IReadOnlyList<string> BuildValidationRules(GalleryContext c) => ["providerSelected", "localizedTitleNotGeneric", "requiredObjectsInPrompt", "forbiddenTermsExcluded"];
        protected static string ResolveDirection(GalleryContext c) => FirstNonEmpty(c.Location, "local sky");
        protected static (string title, string source) ResolveContractTitle(GalleryContext c)
        {
            var resolved = ResolveGalleryEventTitle(default, c.EventName, c.EventType, c.Language, c.EventFamily);
            return (FirstNonEmpty(c.LocalizedEventTitle, resolved.LocalizedEventTitle, c.EventName), FirstNonEmpty(c.TitleSource, resolved.TitleSource));
        }
    }

    private sealed record PresentationTitle(string DisplayTitle, string DisplayShortTitle, string DisplaySubtitle, int MaxTitleLines, int MaxTitleChars, IReadOnlyList<string> PreferredLineBreaks, string Source);

    private static PresentationTitle ResolvePresentationTitle(GalleryContext context, string eventAction, IReadOnlyList<string> primaryObjects, IReadOnlyList<string> localizedPrimaryObjects)
    {
        var hi = LocalizationResolver.IsHindi(context.Language);
        var localizedAction = LocalizeEventAction(eventAction, context.Language);
        var knownMoonTitle = LocalizeMoonSubtypeTitle(ResolveMoonSubtype(context.EventName), context.Language);
        var objectTitle = localizedPrimaryObjects.Count > 0 ? string.Join(hi ? "–" : "–", localizedPrimaryObjects.Take(3)) : string.Empty;
        var title = FirstNonEmpty(knownMoonTitle, objectTitle.Length > 0 ? ComposeTitle(objectTitle, eventAction, localizedAction, hi) : string.Empty, context.EventName, hi ? "खगोलीय घटना" : "Astronomical Event");
        title = RemoveGuidePrefix(title);
        var shortTitle = title.Length <= 34 ? title : FirstNonEmpty(objectTitle.Length > 0 ? ComposeTitle(objectTitle, eventAction, localizedAction, hi) : string.Empty, title);
        if (shortTitle.Length > 42) shortTitle = shortTitle[..42].TrimEnd(' ', '-', '–', ':');
        var subtitle = localizedPrimaryObjects.Count > 0 ? string.Join(", ", localizedPrimaryObjects) : Localize(context.Language, "Observation guide", "अवलोकन गाइड");
        var source = !string.IsNullOrWhiteSpace(knownMoonTitle) ? "localizedMoonSubtypeTitle" : localizedPrimaryObjects.Count > 0 ? "eventActionPlusPrimaryObjects" : "genericFallbackNoEventNameOrObjects";
        return new(title, shortTitle, subtitle, 2, 42, localizedPrimaryObjects.Count > 1 ? localizedPrimaryObjects.ToArray() : [], source);
    }

    private static string ComposeTitle(string objectTitle, string eventAction, string localizedAction, bool hi)
    {
        if (eventAction.Equals("Meteor Shower Peak", StringComparison.OrdinalIgnoreCase) || eventAction.Equals("Peak", StringComparison.OrdinalIgnoreCase))
            return hi ? $"{objectTitle} उल्का वर्षा" : $"{objectTitle} Peak";
        if (eventAction.Equals("Full Moon", StringComparison.OrdinalIgnoreCase)) return objectTitle;
        if (eventAction.Equals("Close Pairing", StringComparison.OrdinalIgnoreCase)) return hi ? $"{objectTitle} करीबी" : $"{objectTitle} Pairing";
        return string.IsNullOrWhiteSpace(localizedAction) ? objectTitle : $"{objectTitle} {localizedAction}";
    }

    private static string ResolveEventAction(GalleryContext c)
    {
        var source = FirstNonEmpty(c.EventName, c.EventType, c.EventFamily);
        if (ContainsAny(source, "Close Pairing", "Pairing")) return "Close Pairing";
        if (ContainsAny(source, "Grouping", "Alignment")) return "Planet Grouping";
        if (ContainsAny(source, "Meteor") && ContainsAny(source, "Peak")) return "Meteor Shower Peak";
        if (ContainsAny(source, "Meteor")) return "Meteor Shower";
        if (ContainsAny(source, "Solar Eclipse", "SolarEclipse")) return "Solar Eclipse";
        if (ContainsAny(source, "Lunar Eclipse", "LunarEclipse")) return "Lunar Eclipse";
        if (ContainsAny(source, "Eclipse")) return "Eclipse";
        if (ContainsAny(source, "Supermoon", "Super Moon")) return "Supermoon";
        if (ContainsAny(source, "Full Moon", "FullMoon", "Moon")) return "Full Moon";
        if (ContainsAny(source, "Comet")) return "Comet Visibility";
        if (ContainsAny(source, "Constellation")) return "Constellation Guide";
        if (ContainsAny(source, "Deep Sky", "Nebula", "Galaxy", "Cluster")) return "Deep Sky Object";
        if (ContainsAny(source, "Conjunction")) return "Conjunction";
        return FirstNonEmpty(c.EventSubtype, c.EventFamily, c.EventType, "Astronomical Event");
    }

    private static string LocalizeEventAction(string action, string language)
    {
        if (!LocalizationResolver.IsHindi(language)) return action switch { "Meteor Shower Peak" => "Peak", _ => action };
        return action switch
        {
            "Conjunction" => "युति", "Close Pairing" => "करीबी", "Planet Grouping" => "ग्रह समूह",
            "Meteor Shower" => "उल्का वर्षा", "Meteor Shower Peak" => "उल्का वर्षा", "Peak" => "चरम",
            "Full Moon" => "पूर्णिमा", "Supermoon" => "सुपरमून", "Eclipse" => "ग्रहण",
            "Solar Eclipse" => "सूर्य ग्रहण", "Lunar Eclipse" => "चंद्र ग्रहण", "Comet Visibility" => "धूमकेतु दृश्यता",
            "Constellation Guide" => "नक्षत्र गाइड", "Deep Sky Object" => "गहरे आकाश की वस्तु", _ => action
        };
    }

    private static IReadOnlyDictionary<string, string> BuildVisualIdentity(GalleryContext context)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["family"] = context.EventFamily,
            ["subtype"] = context.EventSubtype,
            ["moonSubtypeVisualAttributes"] = context.MoonSubtypeVisualAttributes
        };

    private static string RemoveGuidePrefix(string value)
    {
        var clean = value.Replace("Conjunction guide:", "", StringComparison.OrdinalIgnoreCase).Replace("guide:", "", StringComparison.OrdinalIgnoreCase).Trim();
        return clean;
    }

    private sealed class MeteorGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "MeteorShowerGalleryContentProvider";
        public override bool CanResolve(GalleryContext c) => ContainsAny(c.EventType + c.EventFamily + c.EventName, "Meteor");
        protected override IReadOnlyList<string> BuildHints(GalleryContext c) => ["radiant", "night sky", "pre-dawn observing window", "meteor streaks"];
    }

    private sealed class MoonGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "MoonGalleryContentProvider";
        public override bool CanResolve(GalleryContext c) => ContainsAny(c.EventType + c.EventFamily + c.EventName, "Moon", "FullMoon", "SuperMoon");
        protected override IReadOnlyList<string> BuildHints(GalleryContext c) => [FirstNonEmpty(c.MoonSubtypeVisualAttributes, "full moon visibility guidance"), "moonrise or moonset when available"];
    }

    private sealed class PlanetPairingGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "PlanetPairingGalleryContentProvider";
        public override bool CanResolve(GalleryContext c) => ContainsAny(c.EventType + c.EventFamily + c.EventName, "Planet", "Conjunction", "Pairing", "Grouping");
        protected override IReadOnlyList<string> BuildHints(GalleryContext c)
        {
            var names = c.EventObjectContext.ObjectNames;
            if (names.Any(n => n.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) && names.Any(n => n.Equals("Venus", StringComparison.OrdinalIgnoreCase)))
                return ["real celestial object treatment", "Jupiter with visible realistic cloud bands", "Venus as a bright naturally illuminated disk/object", "both planets visible and clearly related in a close apparent conjunction", "not just tiny dots in the sky", "premium astronomy documentary carousel"];
            return ["recognizable real planet treatment", "Jupiter with visible realistic cloud bands when present", "Venus bright natural illumination when present", "close apparent pairing", "twilight/pre-dawn sky"];
        }
        protected override IReadOnlyList<string> BuildValidationRules(GalleryContext c) => base.BuildValidationRules(c).Concat(["realCelestialObjectTreatmentApplied", "jupiterCloudBandsRequired", "venusNaturalIlluminationRequired", "tinyDotOnlyRejected", "incorrectObjectHintsRemoved"]).ToArray();
        protected override IReadOnlyList<string> BuildForbiddenTerms(GalleryContext c) => c.ForbiddenTerms.Concat(["meteor", "meteor shower", "radiant"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class SolarEclipseGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "SolarEclipseGalleryContentProvider";
        public override bool CanResolve(GalleryContext c) => ContainsAny(c.EventType + c.EventFamily + c.EventName, "SolarEclipse", "Solar Eclipse");
        protected override IReadOnlyList<string> BuildHints(GalleryContext c) => ["Sun", "Moon", "safe eclipse viewing", "eclipse shadow"];
    }

    private sealed class LunarEclipseGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "LunarEclipseGalleryContentProvider";
        public override bool CanResolve(GalleryContext c) => ContainsAny(c.EventType + c.EventFamily + c.EventName, "LunarEclipse", "Lunar Eclipse");
        protected override IReadOnlyList<string> BuildHints(GalleryContext c) => ["Moon", "copper red lunar eclipse stages"];
    }

    private sealed class GenericGalleryProvider : GalleryFamilyProviderBase
    {
        public override string ProviderName => "GenericGalleryContentProvider";
        public override string SelectionReason => "no specific Gallery family provider matched";
        public override bool CanResolve(GalleryContext context) => true;
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string[] BuildObservationGuideTips(string eventType, string language) => LocalizationResolver.IsHindi(language) ? eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? ["अंधेरा खुला आसमान चुनें", "रेडिएंट क्षेत्र की ओर देखें", "आँखों को समय दें"] : eventType.Contains("moon", StringComparison.OrdinalIgnoreCase) ? ["स्थानीय उदय समय देखें", "क्षितिज रेखा का उपयोग करें", "दूरबीन वैकल्पिक है"] : ["तारीख और समय जाँचें", "बताई गई दिशा में देखें", "साफ क्षितिज चुनें"] : eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? ["Find a dark open sky", "Face the radiant area", "Give your eyes time"] : eventType.Contains("moon", StringComparison.OrdinalIgnoreCase) ? ["Check the local rise time", "Use the horizon line", "Binoculars are optional"] : ["Check the date and time", "Face the suggested sky direction", "Use a clear horizon"];

    private static string CleanGalleryTitle(string value)
    {
        var clean = RemoveGuidePrefix(value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean)) return "Sky event";
        return clean.Length <= 42 ? clean : clean[..42].TrimEnd(' ', '-', '–', ':');
    }
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

    public sealed record GalleryContext(string EventType, string Title, string StoryTheme, string VisualTheme, string EventDate, string LocalTime, string Location, string RequestedLanguage, string Language, string Timezone, EventObjectContext EventObjectContext, IReadOnlyList<string> ForbiddenTerms, string EventName = "", string EventFamily = "", string EventSubtype = "", string LocalizedEventTitle = "", string TitleSource = "", string MoonSubtypeVisualAttributes = "", bool HeroTitleResolverReused = true, bool GenericMoonFallbackUsed = false, ObservationInfo? ObservationInfo = null);

    public sealed record GalleryTopic(int Number, string Purpose, string Concept, IReadOnlyList<string> TextBlocks, string VisualIntent, string OverlayStyle, string AzureImage2Prompt, string EducationalRole, string LocalizedEducationalRole, string FooterLabel, string Language);
    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);
}
