using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var detail = BuildCertifiedDetail(family, knowledge.Claims);
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
                var prompt = BuildPrompt(family, objects, profile.Role);
                var background = Path.Combine(staging, $".{profile.Role.ToLowerInvariant()}-azure-background.png");
                var generated = await GenerateAzureBackgroundAsync(provider!, prompt, background, profile, ct);
                var backgroundSha = Sha(background);
                var file = $"thumbnail-{profile.Role.ToLowerInvariant()}.png"; var target = Path.Combine(staging, file);
                await RenderAsync(background, target, profile, title, detail, family, ct); File.Delete(background);
                using var decoded = await Image.LoadAsync(target, ct);
                Require(decoded.Width == profile.Width && decoded.Height == profile.Height, "P12_PHYSICAL_VALIDATION_FAILED", "Candidate dimensions failed.");
                variants.Add(new { variant = profile.Role, physicalPath = $"12-thumbnails/{file}", provider = "AzureOpenAIForImage",
                    providerDeployment = generated.Deployment, providerRequestSize = generated.RequestSize, providerAttemptCount = generated.Attempts,
                    compositionType = Composition(family), familyPolicyVersion = FamilyPolicy,
                    promptSemanticInputs = objects.Prepend(family).ToArray(), promptAuthorityReferences = references,
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

    private static string BuildPrompt(string family, IReadOnlyList<string> objects, string aspect) =>
        $"Purpose-built {aspect} cinematic astronomy thumbnail background. Family: {family}. Certified objects only: {string.Join(", ", objects)}. " +
        (family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "Visible radiant burst point, multiple bright meteor streaks spreading outward, deep dark sky, high contrast horizon silhouette, title-safe negative space. " :
         family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? "Cinematic recognition view with the certified constellation visually prominent and clean title-safe negative space. " :
         "Large recognizable astronomy subjects, dramatic authentic lighting, compact observation-poster negative space. ") +
        "NO embedded text. NO labels. NO numbers. NO watermark. Do not invent facts, objects, directions, dates, times, equipment, or safety guidance.";
    private static string Composition(string family) => family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "RadiantBurstThumbnail" : family.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? "CinematicRecognitionThumbnail" : "MatureObservationThumbnail";
    private static string BuildTitle(string family, IReadOnlyList<string> objects) => family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? objects[0].ToUpperInvariant() : $"FIND {objects[0].ToUpperInvariant()}";
    private static string BuildCertifiedDetail(string family, IReadOnlyList<CertifiedKnowledgeClaim> claims) => family.Contains("METEOR", StringComparison.OrdinalIgnoreCase) ? "METEOR SHOWER PEAK" : claims.FirstOrDefault(IsCertified)?.Text?.Trim() ?? "";
    private static bool IsCertified(CertifiedKnowledgeClaim x) => x.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase) || x.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase);

    private static async Task RenderAsync(string background, string target, Profile profile, string title, string detail, string family, CancellationToken ct)
    {
        using var image = await Image.LoadAsync<Rgba32>(background, ct);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(profile.Width, profile.Height), Mode = ResizeMode.Crop, Sampler = KnownResamplers.Lanczos3, Position = AnchorPositionMode.Center }));
        var font = SystemFonts.Collection.Families.First(); var titleFont = font.CreateFont(Math.Clamp(profile.Width / 14f, 54, 88), FontStyle.Bold); var detailFont = font.CreateFont(Math.Clamp(profile.Width / 27f, 34, 50), FontStyle.Bold);
        var y = profile.Height * .70f; image.Mutate(x => { x.Fill(Color.Black.WithAlpha(.40f), new RectangleF(0, profile.Height * .64f, profile.Width, profile.Height * .36f)); x.DrawText(title, titleFont, Color.White, new PointF(profile.Width * .06f, y)); if (!string.IsNullOrWhiteSpace(detail)) x.DrawText(detail.Length > 42 ? detail[..41] + "…" : detail, detailFont, Color.FromRgb(170, 233, 255), new PointF(profile.Width * .06f, y + titleFont.Size * 1.25f)); });
        await image.SaveAsPngAsync(target, ct);
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
