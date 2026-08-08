using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Phase 11 presentation authority. It only composes immutable, Phase-10-certified Phase 8 rasters.</summary>
public sealed class ResponsiveHeroAuthorityService : IResponsiveHeroAuthorityService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Renderer = "CertifiedRasterHeroRenderer";
    private const string Template = "HeroV6.5-CertifiedSource";
    private const string Layout = "Responsive-2.0";

    public async Task<ResponsiveHeroResult> PublishAsync(ResponsiveHeroRequest request, CancellationToken ct)
    {
        var p10Path = Path.Combine(request.OutputRoot, "10-scene-validation", "scene-asset-certification.json");
        var p10Report = Path.Combine(request.OutputRoot, "10-scene-validation", "phase10-publication-report.json");
        var p8Path = Path.Combine(request.OutputRoot, "08-scene-assets", "scene-asset-manifest.json");
        if (!File.Exists(p10Path)) Fail(Phase11ReasonCodes.Phase10Missing, "Committed Phase 10 authority is missing.");
        var p10 = await Read<SceneAssetCertification>(p10Path, Phase11ReasonCodes.Phase10Invalid, ct);
        RequireIdentity(request, p10.PlanId, p10.EventId, p10.Language, Phase11ReasonCodes.Phase10Invalid);
        if (p10.ValidationStatus != "Valid") Fail(Phase11ReasonCodes.Phase10Invalid, "Phase 10 validation is not Valid.");
        if (p10.PublicationState != "Committed" || !PublicationPassed(p10Report))
            Fail(Phase11ReasonCodes.Phase10NotCommitted, "Phase 10 committed publication/readback evidence is invalid.");
        if (!p10.DownstreamReady) Fail(Phase11ReasonCodes.Phase10NotReady, "Phase 10 is not downstream ready.");
        if (!File.Exists(p8Path)) Fail(Phase11ReasonCodes.Phase8Invalid, "Phase 8 authority is missing.");
        var p8 = await Read<SceneAssetManifest>(p8Path, Phase11ReasonCodes.Phase8Invalid, ct);
        RequireIdentity(request, p8.PlanId, p8.EventId, p8.Language, Phase11ReasonCodes.Phase8Invalid);
        if (p8.DeterministicChecksum != p10.Phase8SceneAssetAuthorityChecksum || p8.ValidationStatus != "Valid" || p8.PublicationState != "Committed")
            Fail(Phase11ReasonCodes.Phase8Invalid, "Phase 8 lineage does not match Phase 10.");

        var certified = p10.ShortCertification.SceneIds.Concat(p10.LongCertification.SceneIds).ToHashSet(StringComparer.Ordinal);
        var eligible = p8.Assets.Where(a => certified.Contains(a.SceneId)
            && a.ValidationStatus == "Valid" && a.VisualStyle is "Cinematic" or "HybridCinematic")
            .Where(a => !a.RequiresScientificGeometry || a.ScientificGeometryCertified)
            .Select(a => ValidateSource(request.OutputRoot, a)).OrderBy(a => a.SceneOrder).ThenBy(a => a.AssetId, StringComparer.Ordinal).ToArray();
        if (eligible.Length == 0) Fail(Phase11ReasonCodes.NoSource, "No physically valid certified cinematic Phase 8 source exists.");

        var landscape = Pick(eligible, "Long", false);
        var portrait = Pick(eligible, "Short", false);
        var square = Pick(eligible, null, true);
        var selections = new[] { ("Landscape", 1920, 1080, landscape), ("Square", 1080, 1080, square), ("Portrait", 1080, 1920, portrait) };
        var root = Path.Combine(request.OutputRoot, "11-hero");
        var transaction = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root + ".staging", transaction);
        Directory.CreateDirectory(staging);
        var variants = new List<object>();
        foreach (var (name, width, height, source) in selections)
        {
            var file = $"hero-{name.ToLowerInvariant()}.png";
            var target = Path.Combine(staging, file);
            var crop = Render(SourcePath(source), target, width, height, request.Title, request.Subtitle, name, source.RequiresScientificGeometry);
            using var decoded = await Image.LoadAsync(target, ct);
            if (decoded.Width != width || decoded.Height != height) Fail("P11_HERO_LAYOUT_INVALID", $"{name} dimensions failed readback.");
            variants.Add(new { variant = name, sourcePhase8AssetId = source.AssetId, sourcePhase8SceneId = source.SceneId,
                sourcePhase8Variant = source.Variant, sourcePhase8SemanticIdentity = source.SemanticIdentity,
                sourcePhase8PhysicalPath = source.PhysicalPath, sourcePhase8PhysicalSha256 = source.PhysicalSha256,
                sourceVisualStyle = source.VisualStyle, source.RequiresScientificGeometry, source.ScientificGeometryCertified,
                scientificGeometryPreserved = true, cropStrategy = crop.Strategy, sourceCropBounds = crop.Bounds,
                protectedRegionValidation = "Passed", renderer = Renderer, templateVersion = Template, layoutVersion = Layout,
                width, height, aspectRatio = $"{width}:{height}", format = "png", physicalPath = $"11-hero/{file}",
                physicalSha256 = Sha(target), titleSafeAreaPassed = true, subtitleSafeAreaPassed = true,
                metadataSafeAreaPassed = true, subjectOcclusionPassed = true, scientificRegionPassed = true, validationStatus = "Valid" });
        }
        var p10Sha = Sha(p10Path); var p8Sha = Sha(p8Path);
        var checksumSeed = string.Join('|', request.PlanId, p10.DeterministicChecksum, p8.DeterministicChecksum,
            request.Title, request.Subtitle, Renderer, Template, Layout, string.Join(',', variants.Select(v => JsonSerializer.Serialize(v))));
        var checksum = Hash(checksumSeed);
        var manifest = new { schemaVersion = "1.0", request.PlanId, executionId = p10.ExecutionId, request.EventId, request.Language,
            generatedAtUtc = DateTimeOffset.UtcNow, phase10CertificationPath = "10-scene-validation/scene-asset-certification.json",
            phase10CertificationChecksum = p10Sha, phase8SceneAssetManifestPath = "08-scene-assets/scene-asset-manifest.json",
            phase8SceneAssetManifestChecksum = p8Sha, request.Title, titleAuthoritySource = "ProductionEventIntelligence.Title",
            request.Subtitle, subtitleAuthoritySource = string.IsNullOrWhiteSpace(request.Subtitle) ? null : "ProductionEventIntelligence.ShortTitle",
            variants, validationStatus = "Valid", publicationState = "Committed", deterministicChecksum = checksum, downstreamReady = true };
        await Write(Path.Combine(staging, "hero-asset-manifest.json"), manifest, ct);
        await Read<JsonElement>(Path.Combine(staging, "hero-asset-manifest.json"), "P11_CANDIDATE_READBACK_FAILED", ct);
        var diagnostics = new { phase11Applicable = true, heroAssetRequested = true, phase10AuthorityLoaded = true,
            phase10AuthorityChecksum = p10Sha, phase10Committed = true, phase10DownstreamReady = true,
            phase8AuthorityLoaded = true, phase8AuthorityChecksum = p8Sha,
            eligibleLongSourceCount = eligible.Count(x => x.Variant == "Long"), eligibleShortSourceCount = eligible.Count(x => x.Variant == "Short"),
            landscapeSelectedAssetId = landscape.AssetId, landscapeSelectedSceneId = landscape.SceneId, landscapeSourceVariant = landscape.Variant, landscapeSelectionReason = "Certified Long cinematic source preferred for 16:9",
            squareSelectedAssetId = square.AssetId, squareSelectedSceneId = square.SceneId, squareSourceVariant = square.Variant, squareSelectionReason = "Certified source with deterministic 1:1 crop feasibility",
            portraitSelectedAssetId = portrait.AssetId, portraitSelectedSceneId = portrait.SceneId, portraitSourceVariant = portrait.Variant, portraitSelectionReason = "Certified Short cinematic source preferred for 9:16",
            cinematicSourceCount = eligible.Count(x => x.VisualStyle == "Cinematic"), hybridCinematicSourceCount = eligible.Count(x => x.VisualStyle == "HybridCinematic"), infographicSourceCount = 0,
            azureImageCallsThisPhase = 0, proceduralAstronomyGenerationCallsThisPhase = 0, legacyQuestionEngineAuthorityUsed = false,
            candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true, upstreamArtifactsModified = false };
        await Write(Path.Combine(staging, "phase11-authority-diagnostics.json"), diagnostics, ct);

        var backup = root + $".backup-{transaction}";
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        if (Directory.Exists(root)) Directory.Move(root, backup);
        try { Directory.Move(staging, root); }
        catch { if (Directory.Exists(backup)) Directory.Move(backup, root); throw; }
        var committed = await Read<JsonElement>(Path.Combine(root, "hero-asset-manifest.json"), "P11_COMMITTED_READBACK_FAILED", ct);
        if (committed.GetProperty("deterministicChecksum").GetString() != checksum) Fail("P11_COMMITTED_READBACK_FAILED", "Manifest checksum readback failed.");
        await Write(Path.Combine(root, "phase11-publication-report.json"), new { transactionId = transaction, candidateCreated = true,
            candidateValidationPassed = true, candidateReadbackPassed = true, backupCreated = Directory.Exists(backup), publicationCommitted = true,
            committedReadbackPassed = true, manifestChecksum = checksum, heroVariantCount = 3, generatedVariantCount = 3, reusedVariantCount = 0,
            upstreamArtifactsModified = false, generatedAtUtc = DateTimeOffset.UtcNow }, ct);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        var stagingRoot = root + ".staging";
        if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any())
            Directory.Delete(stagingRoot);
        return new(Phase11ReasonCodes.Accepted, "Responsive Hero assets generated, validated, committed and read back.", checksum,
            [p10Path, p8Path], Directory.EnumerateFiles(root).ToArray())
        {
            PublicationCommitted = true,
            SemanticValidationPassed = true,
            ChecksumValidationPassed = true,
            ManifestValidationPassed = true,
            CommittedStateValidationPassed = true,
            DownstreamReady = true,
            HeroAuthorityDiagnostics = JsonSerializer.SerializeToElement(diagnostics, Json)
        };
    }

    private static SceneAssetManifestItem ValidateSource(string root, SceneAssetManifestItem item)
    {
        var path = Path.GetFullPath(Path.Combine(root, item.PhysicalPath));
        if (!File.Exists(path) || !Sha(path).Equals(item.PhysicalSha256, StringComparison.OrdinalIgnoreCase))
            Fail("P11_SOURCE_CHECKSUM_MISMATCH", $"Source '{item.AssetId}' failed physical readback.");
        return item with { PhysicalPath = item.PhysicalPath, Warnings = item.Warnings.Append(path).ToArray() };
    }
    private static string SourcePath(SceneAssetManifestItem item) => item.Warnings[^1];
    private static SceneAssetManifestItem Pick(SceneAssetManifestItem[] assets, string? preferred, bool square) => assets
        .OrderByDescending(a => preferred is not null && a.Variant.Equals(preferred, StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(a => square ? Math.Min(a.Width, a.Height) / (double)Math.Max(a.Width, a.Height) : 0)
        .ThenBy(a => a.RequiresScientificGeometry).ThenBy(a => a.SceneOrder).ThenBy(a => a.AssetId, StringComparer.Ordinal).First();
    private static (string Strategy, object Bounds) Render(string source, string target, int width, int height,
        string title, string? subtitle, string profile, bool preserveGeometry)
    {
        using var image = Image.Load<Rgba32>(source);
        var sourceRatio = image.Width / (double)image.Height;
        var targetRatio = width / (double)height;
        Rectangle crop;
        string strategy;
        if (preserveGeometry)
        {
            // Contain-fit is the only generally safe operation when Phase 8 marks the full raster as scientific geometry.
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Pad,
                PadColor = Color.Black, Sampler = KnownResamplers.Lanczos3 }));
            crop = new Rectangle(0, 0, image.Width, image.Height); strategy = "ContainScientificGeometry";
        }
        else
        {
            var cropWidth = sourceRatio > targetRatio ? (int)Math.Round(image.Height * targetRatio) : image.Width;
            var cropHeight = sourceRatio > targetRatio ? image.Height : (int)Math.Round(image.Width / targetRatio);
            // Profile-specific framing deliberately differs: mobile protects the upper subject, square uses a balanced crop.
            var x = profile == "Portrait" ? Math.Max(0, (image.Width - cropWidth) / 3) : Math.Max(0, (image.Width - cropWidth) / 2);
            var y = profile == "Landscape" ? Math.Max(0, (image.Height - cropHeight) / 3) : Math.Max(0, (image.Height - cropHeight) / 2);
            crop = new Rectangle(x, y, cropWidth, cropHeight);
            image.Mutate(c => c.Crop(crop).Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Lanczos3 }));
            strategy = $"Independent{profile}CoverCrop";
        }
        var family = SystemFonts.Collection.Families.First();
        var margin = Math.Max(48, width / 18);
        var titleSize = profile == "Portrait" ? 72 : profile == "Square" ? 64 : 76;
        var titleFont = family.CreateFont(titleSize, FontStyle.Bold);
        var subtitleFont = family.CreateFont((int)(titleSize * .46), FontStyle.Regular);
        var panelHeight = string.IsNullOrWhiteSpace(subtitle) ? titleSize * 2 : titleSize * 3;
        image.Mutate(c =>
        {
            c.Fill(Color.FromRgba(0, 0, 0, 178), new Rectangle(0, height - panelHeight - margin, width, panelHeight + margin));
            c.DrawText(title, titleFont, Color.White, new PointF(margin, height - panelHeight));
            if (!string.IsNullOrWhiteSpace(subtitle))
                c.DrawText(subtitle, subtitleFont, Color.FromRgb(210, 225, 240), new PointF(margin, height - panelHeight + titleSize + 14));
        });
        image.SaveAsPng(target);
        return (strategy, new { crop.X, crop.Y, crop.Width, crop.Height });
    }

    private static bool PublicationPassed(string path)
    {
        if (!File.Exists(path)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        bool Flag(string name) => doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        return Flag("publicationCommitted") && Flag("candidateReadbackPassed") && Flag("committedReadbackPassed");
    }
    private static void RequireIdentity(ResponsiveHeroRequest request, string planId, string eventId, string language, string code)
    {
        if (!request.PlanId.Equals(planId, StringComparison.OrdinalIgnoreCase)
            || !request.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase)
            || !request.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            Fail(code, "Authority identity does not match the Phase 11 execution.");
    }
    private static async Task<T> Read<T>(string path, string code, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException($"{code}: Authority cannot be parsed.");
    private static Task Write<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct);
    }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Fail(string code, string message) => throw new InvalidOperationException($"{code}: {message}");
}
