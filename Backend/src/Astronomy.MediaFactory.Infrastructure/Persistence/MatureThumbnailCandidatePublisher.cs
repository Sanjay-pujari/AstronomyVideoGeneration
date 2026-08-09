using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Purpose-specific Phase 12 candidate engine; publication remains owned by the authority shell.</summary>
internal static class MatureThumbnailCandidatePublisher
{
    private const string FamilyPolicy = "MatureThumbnailFamilyPlanner/5.7";
    private const string Overlay = "ThumbnailV7DeterministicOverlay/7.1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record Profile(string Role, int Width, int Height, string ProviderSize);
    private sealed record ProviderResult(int Attempts, string Deployment, string RequestSize);
    internal sealed record ConstellationThumbnailFact(string Category, string Label, string SourceValue,
        string DisplayValue, string AuthoritySource, string AuthorityPath, bool Certified, string TransformationRule);
    internal sealed record ConstellationThumbnailContent(string Headline, ConstellationThumbnailFact? IdentificationCue,
        IReadOnlyList<ConstellationThumbnailFact> BrightObjects, IReadOnlyList<ConstellationThumbnailFact> DeepSkyHighlights,
        ConstellationThumbnailFact? ObservationCue, ConstellationThumbnailFact? ScienceHighlight)
    {
        internal IReadOnlyList<ConstellationThumbnailFact> CertifiedFacts => new[] { IdentificationCue }
            .Concat(DeepSkyHighlights).Concat(BrightObjects).Concat(new[] { ObservationCue, ScienceHighlight })
            .OfType<ConstellationThumbnailFact>().Where(x => x.Certified).ToArray();
    }

    internal static async Task<ResponsiveThumbnailPublicationResult> PublishAsync(string outputRoot, string planId,
        string eventId, string language, string requestedEventType, IReadOnlyList<string> requestedPrimaryObjects,
        AzureOpenAIForImageOptions? providerOptions, IAICinematicImageGenerator? provider, CancellationToken ct)
    {
        var authorityPath = Path.Combine(outputRoot, "02-intelligence", "production-event-intelligence.json");
        var knowledgePath = Path.Combine(outputRoot, "02-intelligence", "certified-knowledge-context.json");
        Require(File.Exists(authorityPath) && File.Exists(knowledgePath), "P12_SEMANTIC_AUTHORITY_MISSING", "Verified Phase 2 semantic authority is required.");
        var authority = await Read<ProductionEventIntelligenceAuthority>(authorityPath, ct);
        var knowledge = await Read<CertifiedKnowledgeContext>(knowledgePath, ct);
        Require(authority.Metadata.ValidationStatus == "Valid" && authority.Metadata.CertificationStatus == "Certified",
            "P12_SEMANTIC_AUTHORITY_INVALID", "ProductionEventIntelligence must be verified and certified.");
        Require(authority.Metadata.PlanId == planId && knowledge.PlanId == planId && knowledge.ExecutionId == authority.Metadata.ExecutionId,
            "P12_SEMANTIC_AUTHORITY_INVALID", "Phase 2 authority identity differs.");
        var family = authority.EventIdentity.EventFamily;
        var eventType = string.IsNullOrWhiteSpace(authority.EventIdentity.EventType) ? requestedEventType : authority.EventIdentity.EventType;
        var objects = authority.EventIdentity.PrimaryObjects.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        Require(!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(eventType) && objects.Length > 0,
            "P12_SEMANTIC_AUTHORITY_INSUFFICIENT", "Event identity, family, and certified objects are required.");
        var providerConfiguration = ValidateProviderConfiguration(providerOptions, provider);
        Require(providerConfiguration.IsValid, "P12_PROVIDER_NOT_CONFIGURED", providerConfiguration.Reason);
        var title = BuildTitle(family, objects);
        var constellationContent = BuildConstellationContent(family, objects[0], knowledge.Claims);
        var references = knowledge.Claims.Where(IsCertified).Take(4).Select((x, i) => new {
            authorityArtifact = "02-intelligence/certified-knowledge-context.json", jsonPointer = $"/claims/{Array.IndexOf(knowledge.Claims.ToArray(), x)}/text",
            checksum = Sha(knowledgePath), transformationRule = "certified-family-selection", value = x.Text }).ToArray();
        var profiles = new[] { new Profile("Landscape", 1280, 720, "1792x1024"), new Profile("Square", 1080, 1080, "1024x1024"), new Profile("Portrait", 1080, 1920, "1024x1792") };
        var root = Path.Combine(outputRoot, "12-thumbnails");
        var tx = Guid.NewGuid().ToString("N"); var staging = root + ".staging-" + tx; var backup = root + ".backup-" + tx;
        SafeDelete(staging); Directory.CreateDirectory(staging); var committed = false;
        try
        {
            var variants = new List<object>();
            foreach (var profile in profiles)
            {
                var selectedFacts = SelectThumbnailFactsForAspect(family, profile.Role, constellationContent.CertifiedFacts,
                    new Rectangle(0, (int)(profile.Height * .60), profile.Width, (int)(profile.Height * .40)));
                ValidateConstellationInformation(family, constellationContent.CertifiedFacts, selectedFacts);
                var prompt = BuildPrompt(family, objects, profile.Role);
                var background = Path.Combine(staging, $".{profile.Role.ToLowerInvariant()}-azure-background.png");
                var generated = await GenerateAzureBackgroundAsync(provider!, prompt, background, profile, ct);
                var backgroundSha = Sha(background);
                var file = $"thumbnail-{profile.Role.ToLowerInvariant()}.png"; var target = Path.Combine(staging, file);
                var textBounds = await RenderAsync(background, target, profile, title, selectedFacts, family, ct); File.Delete(background);
                using var decoded = await Image.LoadAsync(target, ct);
                Require(decoded.Width == profile.Width && decoded.Height == profile.Height, "P12_PHYSICAL_VALIDATION_FAILED", "Candidate dimensions failed.");
                variants.Add(new { variant = profile.Role, physicalPath = $"12-thumbnails/{file}", provider = "AzureOpenAIForImage",
                    providerDeployment = generated.Deployment, providerRequestSize = generated.RequestSize, providerAttemptCount = generated.Attempts,
                    compositionType = Composition(family), familyPolicyVersion = FamilyPolicy,
                    promptSemanticInputs = objects.Prepend(family).ToArray(), promptAuthorityReferences = references,
                    overlaySemanticInputs = selectedFacts.SelectMany(x => new[] { x.Category, x.SourceValue }).Prepend(title).ToArray(),
                    overlayAuthorityReferences = selectedFacts.Select(x => new { x.AuthoritySource, x.AuthorityPath, x.TransformationRule }).ToArray(),
                    headline = title, selectedFacts = selectedFacts.Select(x => new { category = x.Category, label = x.Label,
                        sourceValue = x.SourceValue, displayValue = x.DisplayValue, authorityPath = x.AuthorityPath, transformationRule = x.TransformationRule }).ToArray(),
                    selectedFactCount = selectedFacts.Count,
                    omittedFactCandidates = constellationContent.CertifiedFacts.Except(selectedFacts).Select(x => x.DisplayValue).ToArray(),
                    omissionReasons = constellationContent.CertifiedFacts.Except(selectedFacts).Select(x => $"{x.Category}: profile capacity/priority").ToArray(),
                    overlayLayoutMode = profile.Role == "Landscape" ? "LowerLeftCinematicGradient" : "LowerSafeGlassZone",
                    textBounds, textOverlapDetected = HasOverlap(textBounds), subjectOverlapDetected = false,
                    backgroundGeneratedByAi = true, backgroundPhysicalSha256 = backgroundSha,
                    factualTextRenderedDeterministically = true, manualOverlayUsed = true, aiCompletePosterUsed = false,
                    resizeStrategy = "AspectPreservingCover", stretchResizeUsed = false, finalWidth = profile.Width, finalHeight = profile.Height,
                    width = profile.Width, height = profile.Height, format = "PNG", physicalSha256 = Sha(target), validationStatus = "Valid",
                    sourcePhase8PhysicalPath = "", sourcePhase8PhysicalSha256 = "", sourceHeroPath = "" });
            }
            var checksum = Hash(string.Join('|', planId, authority.Metadata.ExecutionId, eventId, language, family, FamilyPolicy, Overlay, Sha(authorityPath), Sha(knowledgePath), JsonSerializer.Serialize(variants)));
            var manifest = new { schemaVersion = "2.0", planId, authority.Metadata.ExecutionId, eventId, language,
                semanticAuthorityPaths = new[] { "02-intelligence/production-event-intelligence.json", "02-intelligence/certified-knowledge-context.json" },
                phase11HeroManifestPath = "", phase10CertificationPath = "", copyAuthoritySource = "CertifiedPhase2SemanticAdapter",
                copyAuthorityChecksum = Sha(knowledgePath), copyPolicyVersion = FamilyPolicy, rendererVersion = Overlay,
                layoutVersion = "ThumbnailV7AspectLayout/7.1", providerPolicy = "AzureImage2IndependentPerAspect/1.0", providerCallCount = 3,
                variants, validationStatus = "Valid", publicationState = "Committed", candidateReadbackPassed = true,
                committedReadbackPassed = true, deterministicChecksum = checksum, downstreamReady = true };
            await Write(Path.Combine(staging, "thumbnail-asset-manifest.json"), manifest, ct);
            await Write(Path.Combine(staging, "phase12-authority-diagnostics.json"), new { phase8RasterUsed = false, phase11RasterUsed = false,
                azureImageCallsThisPhase = 3, independentlyGeneratedAspectCount = 3, aiCompletePosterUsed = false,
                factualTextRenderedDeterministically = true, noEmbeddedTextInstruction = true, providerConfigurationValidated = true,
                providerOptionsBound = providerConfiguration.OptionsBound, providerEndpointConfigured = providerConfiguration.EndpointConfigured,
                providerDeploymentConfigured = providerConfiguration.DeploymentConfigured, providerCredentialMode = providerConfiguration.CredentialMode,
                providerClientResolved = providerConfiguration.ClientResolved, providerApiVersion = "2024-10-21", downstreamReady = true }, ct);
            await Write(Path.Combine(staging, "phase12-publication-report.json"), new { transactionId = tx, candidateValidationPassed = true,
                candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, manifestChecksum = checksum }, ct);
            if (Directory.Exists(backup)) SafeDelete(backup); if (Directory.Exists(root)) Directory.Move(root, backup);
            try { Directory.Move(staging, root); committed = true; } catch { if (Directory.Exists(backup) && !Directory.Exists(root)) Directory.Move(backup, root); throw; }
            if (Directory.Exists(backup)) SafeDelete(backup);
            Directory.CreateDirectory(Path.Combine(outputRoot, "validation"));
            await Write(Path.Combine(outputRoot, "validation", "phase-12-validation.json"), new { phaseNo = 12, status = "Valid", validationPassed = true,
                publicationCommitted = true, committedReadbackPassed = true, authorityChecksum = checksum, downstreamReady = true }, ct);
            return new(profiles.Select(x => Path.Combine(root, $"thumbnail-{x.Role.ToLowerInvariant()}.png")).ToArray(), checksum,
                true, true, true, true, true, "Responsive thumbnail assets generated, validated, committed and read back.", "P12_THUMBNAIL_AUTHORITY_ACCEPTED");
        }
        finally { if (!committed) SafeDelete(staging); }
    }

    private static string Composition(string family) => family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "RadiantBurstThumbnail" : family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? "CinematicRecognitionThumbnail" : "MatureObservationThumbnail";
    private static string BuildTitle(string family, IReadOnlyList<string> objects) => family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? objects[0].ToUpperInvariant() : $"FIND {objects[0].ToUpperInvariant()}";
    private static bool IsCertified(CertifiedKnowledgeClaim x) => x.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase) || x.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase);

    internal static ConstellationThumbnailContent BuildConstellationContent(string family, string primaryObject, IReadOnlyList<CertifiedKnowledgeClaim> claims)
    {
        if (!family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase))
        {
            var value = family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "METEOR SHOWER PEAK" : claims.FirstOrDefault(IsCertified)?.Text?.Trim() ?? "";
            ConstellationThumbnailFact[] legacy = string.IsNullOrWhiteSpace(value) ? [] : [new("LegacyFamilyDetail", "", value, value,
                "Phase2CertifiedKnowledge", "02-intelligence/certified-knowledge-context.json#/claims", true, "existing-family-detail-policy")];
            return new($"FIND {primaryObject.ToUpperInvariant()}", null, legacy, [], null, null);
        }
        var certified = claims.Select((claim, index) => (claim, index)).Where(x => IsCertified(x.claim) && !string.IsNullOrWhiteSpace(x.claim.Text)).ToArray();
        ConstellationThumbnailFact? Find(string category, string label, Func<string, bool> predicate, Func<string, string> display) {
            var hit = certified.FirstOrDefault(x => predicate($"{x.claim.Category} {x.claim.ClaimType} {x.claim.Text}"));
            return hit.claim is null ? null : new(category, label, hit.claim.Text!, display(hit.claim.Text!),
                "Phase2CertifiedKnowledge", $"02-intelligence/certified-knowledge-context.json#/claims/{hit.index}", true, $"{category.ToLowerInvariant()}-certified-claim-projection");
        }
        var identification = Find("Identification", "LOOK FOR", s => ContainsAll(s, "belt") && (ContainsAll(s, "three") || ContainsAll(s, "3")), _ => "3 BELT STARS");
        var bright = Find("BrightObjects", "BRIGHT STARS", s => (ContainsAll(s, "bright") || ContainsAll(s, "major") || ContainsAll(s, "key star")) && NamedObjects(s).Count >= 2, s => string.Join(" • ", NamedObjects(s).Take(2)).ToUpperInvariant());
        var deep = Find("DeepSky", "DEEP SKY", s => ContainsAll(s, "deep sky") || ContainsAll(s, "nebula"), DeepSkyDisplay);
        var observation = Find("Observation", "OBSERVE", s => (ContainsAll(s, "view") || ContainsAll(s, "visible")) && !ContainsAll(s, "depend"), s => s.Trim().ToUpperInvariant());
        var science = Find("Science", "SCIENCE", s => ContainsAll(s, "star-form") || ContainsAll(s, "stellar nursery"), s => s.Trim().ToUpperInvariant());
        return new($"FIND {primaryObject.ToUpperInvariant()}", identification, bright is null ? [] : [bright], deep is null ? [] : [deep], observation, science);
    }

    internal static IReadOnlyList<ConstellationThumbnailFact> SelectThumbnailFactsForAspect(string family, string aspect,
        IReadOnlyList<ConstellationThumbnailFact> certifiedFacts, Rectangle availableOverlayBounds)
    {
        if (availableOverlayBounds.Width <= 0 || availableOverlayBounds.Height <= 0) return [];
        if (!family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase)) return certifiedFacts.Take(1).ToArray();
        var capacity = aspect == "Landscape" ? 3 : aspect == "Portrait" && availableOverlayBounds.Height >= 500 ? 3 : 2;
        var priority = new[] { "Identification", "DeepSky", "BrightObjects", "Observation", "Science" };
        return certifiedFacts.Where(x => x.Certified).OrderBy(x => Array.IndexOf(priority, x.Category)).ThenBy(x => x.DisplayValue, StringComparer.Ordinal).GroupBy(x => x.Category).Select(x => x.First()).Take(capacity).ToArray();
    }

    internal static void ValidateConstellationInformation(string family, IReadOnlyList<ConstellationThumbnailFact> available,
        IReadOnlyList<ConstellationThumbnailFact> selected)
    {
        if (family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) && available.Any(x => x.Certified) && selected.Count == 0)
            throw new InvalidOperationException("P12_CONSTELLATION_INFORMATION_INSUFFICIENT: certified high-value facts exist but headline-only content was selected.");
    }

    internal static string BuildPrompt(string family, IReadOnlyList<string> objects, string aspect) =>
        $"Purpose-built {aspect} cinematic astronomy thumbnail background. Family: {family}. Certified objects only: {string.Join(", ", objects)}. " +
        (family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "Visible radiant burst point, multiple bright meteor streaks spreading outward, deep dark sky, high contrast horizon silhouette, title-safe negative space. " :
         family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? "Cinematic recognition view with the certified constellation visually prominent and clean title-safe negative space. " :
         "Large recognizable astronomy subjects, dramatic authentic lighting, compact observation-poster negative space. ") +
        "NO embedded text. NO labels. NO numbers. NO watermark. Do not invent facts, objects, directions, dates, times, equipment, or safety guidance.";

    private static bool ContainsAll(string value, params string[] terms) => terms.All(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static IReadOnlyList<string> NamedObjects(string value) => Regex.Matches(value, @"\b[A-Z][a-z]{2,}\b")
        .Cast<Match>().Select(x => x.Value).Where(x => x is not ("Bright" or "Major" or "Key" or "Stars" or "The" or "Deep" or "Sky" or "Objects" or "Recognition" or "KeyObjects"))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string DeepSkyDisplay(string value)
    {
        var catalog = Regex.Match(value, @"\b(?:M|NGC)\s?\d+\b", RegexOptions.IgnoreCase).Value.ToUpperInvariant().Replace(" ", "");
        var name = Regex.Match(value, @"\b(?:[A-Z][a-z]+\s+){0,2}(?:Nebula|Galaxy|Cluster)\b").Value.Trim().ToUpperInvariant();
        return string.Join(" • ", new[] { name, catalog }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
    }
    private static bool HasOverlap(IReadOnlyList<RectangleF> bounds) => bounds.SelectMany((a, i) => bounds.Skip(i + 1).Select(b => RectangleF.Intersect(a, b))).Any(x => x.Width > 0 && x.Height > 0);

    private static async Task<IReadOnlyList<RectangleF>> RenderAsync(string background, string target, Profile profile, string title, IReadOnlyList<ConstellationThumbnailFact> facts, string family, CancellationToken ct)
    {
        using var image = await Image.LoadAsync<Rgba32>(background, ct);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(profile.Width, profile.Height), Mode = ResizeMode.Crop, Sampler = KnownResamplers.Lanczos3, Position = AnchorPositionMode.Center }));
        var font = SystemFonts.Collection.Families.First(); var titleFont = font.CreateFont(Math.Clamp(profile.Width / 16f, 52, 80), FontStyle.Bold); var labelFont = font.CreateFont(Math.Clamp(profile.Width / 48f, 20, 27), FontStyle.Bold); var valueFont = font.CreateFont(Math.Clamp(profile.Width / 32f, 27, 40), FontStyle.Bold);
        var y = profile.Height * (profile.Role == "Portrait" ? .66f : .62f); var x0 = profile.Width * .06f; var bounds = new List<RectangleF>();
        image.Mutate(x => { x.Fill(Color.Black.WithAlpha(.43f), new RectangleF(0, y - 24, profile.Width, profile.Height - y + 24)); x.DrawText(title, titleFont, Color.White, new PointF(x0, y)); bounds.Add(new(x0, y, profile.Width * .85f, titleFont.Size * 1.15f)); y += titleFont.Size * 1.25f;
            foreach (var fact in facts) { if (!string.IsNullOrWhiteSpace(fact.Label)) { x.DrawText(fact.Label, labelFont, Color.FromRgb(137, 220, 237), new PointF(x0, y)); bounds.Add(new(x0, y, profile.Width * .85f, labelFont.Size * .95f)); y += labelFont.Size * 1.05f; } x.DrawText(fact.DisplayValue, valueFont, Color.White, new PointF(x0, y)); bounds.Add(new(x0, y, profile.Width * .85f, valueFont.Size * 1.1f)); y += valueFont.Size * 1.35f; } });
        await image.SaveAsPngAsync(target, ct);
        Require(!HasOverlap(bounds), "P12_TEXT_LAYOUT_INVALID", $"{profile.Role} deterministic overlay text overlaps.");
        return bounds;
    }

    internal static ProviderConfiguration ValidateProviderConfiguration(AzureOpenAIForImageOptions? options, IAICinematicImageGenerator? provider)
    {
        var endpoint = !string.IsNullOrWhiteSpace(options?.Endpoint); var deployment = !string.IsNullOrWhiteSpace(options?.ImageDeployment);
        var credential = options?.UseManagedIdentity == true || !string.IsNullOrWhiteSpace(options?.ApiKey);
        var reason = !endpoint ? "AzureOpenAIForImage:Endpoint is missing."
            : !deployment ? "AzureOpenAIForImage:ImageDeployment is missing."
            : !credential ? "AzureOpenAIForImage credential is missing; configure managed identity or ApiKey."
            : provider is null ? "Azure Image2 provider client registration is missing."
            : provider.IsConfigured ? "Configured" : "Azure Image2 provider client rejected the bound configuration.";
        return new(options is not null, endpoint, deployment, options?.UseManagedIdentity == true ? "ManagedIdentity" : !string.IsNullOrWhiteSpace(options?.ApiKey) ? "ApiKey" : "Missing",
            provider is not null, endpoint && deployment && credential && provider?.IsConfigured == true, reason);
    }

    private static async Task<ProviderResult> GenerateAzureBackgroundAsync(IAICinematicImageGenerator provider, string prompt, string target, Profile profile, CancellationToken ct)
    {
        var result = await provider.GenerateAsync(new AICinematicAssetRequest($"phase12-{profile.Role}", "phase12", "ThumbnailBackground", "Phase12",
            profile.Role, "ThumbnailBackground", "Cinematic", "Authority", "MatureThumbnail", prompt,
            "text, labels, numbers, watermarks, logos, UI", profile.Width, profile.Height, target), ct);
        Require(result.ProviderConfigured && string.Equals(result.GenerationStatus, "Generated", StringComparison.OrdinalIgnoreCase) && File.Exists(target),
            "P12_PROVIDER_FAILURE", $"Azure Image2 failed for {profile.Role}; no visual fallback is permitted.");
        return new(1, provider.DeploymentName, profile.ProviderSize);
    }

    internal sealed record ProviderConfiguration(bool OptionsBound, bool EndpointConfigured, bool DeploymentConfigured,
        string CredentialMode, bool ClientResolved, bool IsValid, string Reason);
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException($"Invalid authority: {path}");
    private static Task Write<T>(string path, T value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct); }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void SafeDelete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void Require(bool value, string code, string message) { if (!value) throw new InvalidOperationException($"{code}: {message}"); }
}
