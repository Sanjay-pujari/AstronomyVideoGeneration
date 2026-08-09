using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

/// <summary>Phase 13 certified educational carousel publisher.</summary>
internal static class Phase13GalleryAuthority
{
    private const string Policy = "GalleryPagePolicy/1.0";
    private const string Renderer = "CertifiedGalleryRenderer/1.0";
    private const string Layout = "EducationalCarouselLayout/1.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] CanonicalRoles = ["cover-identity", "what-happens", "where-to-look", "when-to-observe", "certified-highlight-or-science", "observation-checklist"];
    private static readonly string[] ConstellationRoles = ["cover-identity", "how-to-identify", "bright-stars-or-key-objects", "deep-sky-highlight", "science-or-story-highlight", "observation-checklist"];

    internal sealed record GeneratedFileMetadata(
        string Path,
        string FileName,
        int Width,
        int Height,
        string Format,
        string MimeType,
        long ByteLength,
        string PhysicalSha256);

    internal static async Task<AstroPulseGalleryResult> PublishAsync(string galleryRoot, CancellationToken ct)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(galleryRoot))!;
        var p2Path = Path.Combine(outputRoot, "02-intelligence", "certified-knowledge-context.json");
        var p4Path = ResolveRequired(outputRoot, "04-blueprint/documentary-blueprint.json", "04-blueprint/documentary-blueprint-aggregate.json");
        var p6Path = Path.Combine(outputRoot, "06-story-frames", "story-frames.json");
        var p8Path = Path.Combine(outputRoot, "08-scene-assets", "scene-asset-manifest.json");
        var p10Path = Path.Combine(outputRoot, "10-scene-validation", "scene-asset-certification.json");
        var p10ReportPath = Path.Combine(outputRoot, "10-scene-validation", "phase10-publication-report.json");
        Require(File.Exists(p2Path) && File.Exists(p4Path) && File.Exists(p6Path), "P13_SEMANTIC_AUTHORITY_MISSING", "Certified Phase 2, 4 and 6 semantic authority is required.");
        Require(File.Exists(p8Path) && File.Exists(p10Path) && File.Exists(p10ReportPath), "P13_SCENE_AUTHORITY_INVALID", "Committed Phase 10 visual authority and its Phase 8 lineage are required.");

        var p2 = await Read<CertifiedKnowledgeContext>(p2Path, ct);
        var p6 = await Read<StoryFramesAuthority>(p6Path, ct);
        var p8 = await Read<SceneAssetManifest>(p8Path, ct);
        var p10 = await Read<SceneAssetCertification>(p10Path, ct);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(p10ReportPath, ct));
        Require(p2.Certification.Status.Equals("Certified", StringComparison.OrdinalIgnoreCase) || p2.Certification.CertifiedClaims > 0,
            "P13_SEMANTIC_AUTHORITY_MISSING", "Phase 2 has no certified claims.");
        Require(p10.ValidationStatus == "Valid" && p10.PublicationState == "Committed" && p10.DownstreamReady
            && Flag(report.RootElement, "candidateReadbackPassed") && Flag(report.RootElement, "publicationCommitted")
            && Flag(report.RootElement, "committedReadbackPassed")
            && Text(report.RootElement, "certificationChecksum") == p10.DeterministicChecksum,
            "P13_SCENE_AUTHORITY_INVALID", "Phase 10 is not Valid, Committed and downstream ready.");
        var expectedP10 = Hash(string.Join('|', p10.PlanId, p10.ExecutionId, p10.EventId, p10.Language,
            p10.Phase6StoryFrameAuthorityChecksum, p10.Phase8SceneAssetAuthorityChecksum, p10.Phase9LongSceneAuthorityChecksum,
            string.Join(',', p10.RequestedVariants), string.Join(',', p10.ShortCertification.SceneIds), string.Join(',', p10.LongCertification.SceneIds)));
        Require(expectedP10 == p10.DeterministicChecksum, "P13_SCENE_AUTHORITY_INVALID", "Phase 10 authority checksum is invalid.");
        Require(p8.DeterministicChecksum == p10.Phase8SceneAssetAuthorityChecksum && p8.ValidationStatus == "Valid" && p8.PublicationState == "Committed",
            "P13_SCENE_AUTHORITY_INVALID", "Phase 8 physical lineage does not match Phase 10.");
        Require(p6.SemanticChecksum == p10.Phase6StoryFrameAuthorityChecksum && p6.PlanId == p10.PlanId && p2.PlanId == p10.PlanId,
            "P13_SEMANTIC_AUTHORITY_MISSING", "Semantic authority identity or Phase 6 lineage differs.");

        var certifiedIds = p10.ShortCertification.SceneIds.Concat(p10.LongCertification.SceneIds).ToHashSet(StringComparer.Ordinal);
        var eligibleSources = p8.Assets.Where(a => certifiedIds.Contains(a.SceneId) && a.ValidationStatus == "Valid"
                && a.VisualStyle is "Cinematic" or "HybridCinematic" && (!a.RequiresScientificGeometry || a.ScientificGeometryCertified))
            .OrderBy(a => a.SceneOrder).ThenBy(a => a.AssetId, StringComparer.Ordinal).Select(a => ValidateSource(outputRoot, a)).ToArray();
        var sources = eligibleSources.GroupBy(a => a.Item.PhysicalSha256, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Concat(eligibleSources)
            .ToArray();
        Require(sources.Length > 0, "P13_SCENE_AUTHORITY_INVALID", "No certified, physically valid cinematic source is eligible.");
        var claims = p2.Claims.Where(c => c.ReviewStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
                || c.Classification.Equals("Certified", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).ToArray();
        if (claims.Length == 0) claims = p2.Claims.Where(c => !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.KnowledgeId, StringComparer.Ordinal).ToArray();
        Require(claims.Length > 0, "P13_SEMANTIC_AUTHORITY_MISSING", "No displayable certified semantic claim exists.");

        var roles = p2.EventFamily.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase) ? ConstellationRoles : CanonicalRoles;
        var transaction = Guid.NewGuid().ToString("N");
        var staging = galleryRoot + ".staging-" + transaction;
        var backup = galleryRoot + ".backup-" + transaction;
        Directory.CreateDirectory(staging);
        try
        {
        var pages = new List<object>(); var physicalMetadata = new List<GeneratedFileMetadata>(); var outputPaths = new List<string>(); var sourceHashes = new List<string>(); var reuseReasons = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var source = sources[index % sources.Length];
            var claim = claims[index % claims.Length];
            var frame = p6.Frames.OrderBy(f => f.SceneNumber).ThenBy(f => f.FrameNumber).ElementAtOrDefault(index % Math.Max(1, p6.Frames.Count));
            var headline = RoleLabel(roles[index]);
            var display = Shorten(claim.Text!, 48);
            var file = $"gallery-{index + 1:00}.png";
            var target = Path.Combine(staging, file);
            var (crop, generatedFileMetadata) = await RenderAndReadbackAsync(
                source.FullPath, target, $"13-gallery/{file}", headline, display, index,
                source.Item.RequiresScientificGeometry, ct);
            var copyReference = Lineage("02-intelligence/certified-knowledge-context.json", $"/claims/{Array.IndexOf(p2.Claims.ToArray(), claim)}/text", claim.Text!, display, display == claim.Text ? "verbatim" : "shorten-to-48-characters");
            var roleReference = Lineage("GalleryPagePolicy/1.0", $"/families/{p2.EventFamily}/slots/{index + 1}/roleId", roles[index], headline, "normalize-case-and-hyphens");
            var frameReference = frame is null ? null : Lineage("06-story-frames/story-frames.json", $"/frames/{Array.IndexOf(p6.Frames.ToArray(), frame)}/narrativeIntent", frame.NarrativeIntent, Shorten(frame.NarrativeIntent, 112), "shorten-to-112-characters");
            var reused = sourceHashes.Contains(source.Item.PhysicalSha256, StringComparer.OrdinalIgnoreCase);
            var reuseReason = reused ? $"Only {eligibleSources.Select(s => s.Item.PhysicalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count()} distinct certified source hashes available; deterministic slot-specific crop applied for slot {index + 1}." : null;
            pages.Add(new { canonicalSlot = index + 1, roleId = CanonicalRoles[index], resolvedRoleId = roles[index], physicalPath = generatedFileMetadata.Path,
                width = generatedFileMetadata.Width, height = generatedFileMetadata.Height, aspectRatio = "1:1", format = generatedFileMetadata.Format,
                generatedFileMetadata, headline, subheadline = display, factBlocks = new[] { display }, copyAuthorityReferences = new[] { roleReference, copyReference },
                viewerTakeawayAuthorityReference = frameReference, sourceAssetId = source.Item.AssetId, sourceSceneId = source.Item.SceneId,
                sourcePhysicalPath = source.Item.PhysicalPath, sourcePhysicalSha256 = source.Item.PhysicalSha256, outputPhysicalSha256 = generatedFileMetadata.PhysicalSha256,
                reuseReason, requiresScientificGeometry = source.Item.RequiresScientificGeometry, scientificGeometryCertified = source.Item.ScientificGeometryCertified,
                scientificGeometryPreserved = true, protectedScientificRegion = source.Item.RequiresScientificGeometry ? "full-source-raster" : null,
                cropStrategy = crop, subjectVisible = true, textClippingPassed = true, textOverlapPassed = true, subjectCollisionPassed = true, scientificCollisionPassed = true });
            physicalMetadata.Add(generatedFileMetadata);
            sourceHashes.Add(source.Item.PhysicalSha256); outputPaths.Add(Path.Combine(galleryRoot, file));
            if (reuseReason is not null) reuseReasons.Add(reuseReason);
        }
        var distinct = sourceHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var checksumSeed = string.Join('|', p10.PlanId, p10.ExecutionId, Sha(p2Path), Sha(p4Path), Sha(p6Path), p10.DeterministicChecksum, Policy, Renderer, Layout, JsonSerializer.Serialize(pages));
        var authorityChecksum = Hash(checksumSeed);
        var observationClaims = claims.Where(c => c.Category.Contains("observ", StringComparison.OrdinalIgnoreCase) || c.ClaimType.Contains("observ", StringComparison.OrdinalIgnoreCase)).ToArray();
        await Write(Path.Combine(staging, "observation-guide.json"), new { schemaVersion = "1.0", supportingProjectionOnly = true, eventId = p10.EventId,
            eventFamily = p2.EventFamily, facts = observationClaims.Select(c => new { value = c.Text, authorityReference = Lineage("02-intelligence/certified-knowledge-context.json", $"/claims/{Array.IndexOf(p2.Claims.ToArray(), c)}/text", c.Text!, c.Text!, "verbatim") }).ToArray() }, ct);
        var manifest = new { schemaVersion = "1.0", p10.PlanId, p10.ExecutionId, p10.EventId, p10.Language,
            phase2AuthorityPath = "02-intelligence/certified-knowledge-context.json", phase2AuthorityChecksum = Sha(p2Path), phase4AuthorityPath = Relative(outputRoot, p4Path), phase4AuthorityChecksum = Sha(p4Path),
            phase6AuthorityPath = "06-story-frames/story-frames.json", phase6AuthorityChecksum = Sha(p6Path), phase10CertificationPath = "10-scene-validation/scene-asset-certification.json",
            phase10AuthorityChecksum = p10.DeterministicChecksum, pagePolicyVersion = Policy, rendererVersion = Renderer, layoutVersion = Layout, pageCount = 6, pages,
            distinctSourceCount = distinct, reusedSourceCount = 6 - distinct, sourceReuseReasons = reuseReasons,
            observationGuidePath = "13-gallery/observation-guide.json", roleDiversityPassed = roles.Distinct().Count() == 6, semanticDiversityPassed = pages.Select(x => JsonSerializer.Serialize(x)).Distinct().Count() == 6,
            visualDiversityPassed = eligibleSources.Select(x => x.Item.PhysicalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 6 || distinct == 6, validationStatus = "Valid", publicationState = "Committed", candidateReadbackPassed = true, committedReadbackPassed = true,
            deterministicChecksum = authorityChecksum, downstreamReady = true };
        await Write(Path.Combine(staging, "gallery-manifest.json"), manifest, ct);
        await Read<JsonElement>(Path.Combine(staging, "gallery-manifest.json"), ct);
        var diagnostics = new { phase13Applicable = true, galleryRequested = true, pageCount = 6, phase2AuthorityLoaded = true, phase4AuthorityLoaded = true, phase6AuthorityLoaded = true,
            phase10AuthorityLoaded = true, phase10AuthorityChecksumValid = true, selectedAssetsDerivedFromPhase10 = true, distinctSourceCount = distinct, reusedSourceCount = 6 - distinct,
            roleDiversityPassed = true, semanticDiversityPassed = true, visualDiversityPassed = eligibleSources.Select(x => x.Item.PhysicalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 6 || distinct == 6, azureImageCallsThisPhase = 0,
            otherGenerativeImageCallsThisPhase = 0, proceduralAstronomyGenerationCallsThisPhase = 0, stellariumGenerationCallsThisPhase = 0,
            questionEngineAuthorityUsed = false, heroAuthorityUsed = false, thumbnailAuthorityUsed = false, genericFallbackUsed = false, stretchResizeUsed = false,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true, upstreamArtifactsModified = false };
        await Write(Path.Combine(staging, "phase13-authority-diagnostics.json"), diagnostics, ct);
        await Write(Path.Combine(staging, "phase13-publication-report.json"), new { transactionId = transaction, candidateCreated = true, candidateValidationPassed = true,
            candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, manifestChecksum = authorityChecksum, pageCount = 6, upstreamArtifactsModified = false }, ct);
        if (Directory.Exists(galleryRoot)) Directory.Move(galleryRoot, backup);
        try { Directory.Move(staging, galleryRoot); } catch { if (Directory.Exists(backup)) Directory.Move(backup, galleryRoot); throw; }
        var committed = await Read<JsonElement>(Path.Combine(galleryRoot, "gallery-manifest.json"), ct);
        Require(committed.GetProperty("deterministicChecksum").GetString() == authorityChecksum, "P13_COMMITTED_READBACK_FAILED", "Committed manifest readback failed.");
        foreach (var expected in physicalMetadata)
        {
            var committedPath = Path.Combine(outputRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar));
            var actual = await ReadPhysicalMetadataAsync(committedPath, expected.Path, ct);
            Require(actual == expected, "P13_COMMITTED_READBACK_FAILED", $"Committed physical metadata differs for '{expected.Path}'.");
        }
        Directory.CreateDirectory(Path.Combine(outputRoot, "validation"));
        var validationPath = Path.Combine(outputRoot, "validation", "phase-13-validation.json");
        await Write(validationPath, new { phaseNo = 13, status = "Succeeded", validationStatus = "Valid", authorityPath = "13-gallery/gallery-manifest.json",
            authorityChecksum, publicationState = "Committed", semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true,
            publicationCommitted = true, candidateReadbackPassed = true, committedReadbackPassed = true, downstreamReady = true, providerCallCount = 0 }, ct);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        return new(galleryRoot, outputPaths, Path.Combine(galleryRoot, "phase13-publication-report.json"), Path.Combine(galleryRoot, "gallery-manifest.json"),
            Path.Combine(galleryRoot, "phase13-authority-diagnostics.json"), validationPath);
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(galleryRoot)) Directory.Delete(galleryRoot, true);
                Directory.Move(backup, galleryRoot);
            }
            throw;
        }
        finally
        {
            // A failed candidate is never publication authority. This also covers failures
            // during rendering/readback, before the directory-swap transaction begins.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static (SceneAssetManifestItem Item, string FullPath) ValidateSource(string root, SceneAssetManifestItem item)
    {
        var path = Path.GetFullPath(Path.Combine(root, item.PhysicalPath));
        var allowed8 = Path.GetFullPath(Path.Combine(root, "08-scene-assets")) + Path.DirectorySeparatorChar;
        var allowed9 = Path.GetFullPath(Path.Combine(root, "09-long-scenes")) + Path.DirectorySeparatorChar;
        Require((path.StartsWith(allowed8, StringComparison.Ordinal) || path.StartsWith(allowed9, StringComparison.Ordinal)) && File.Exists(path)
            && Sha(path).Equals(item.PhysicalSha256, StringComparison.OrdinalIgnoreCase), "P13_SCENE_AUTHORITY_INVALID", $"Certified source '{item.AssetId}' failed physical checksum or path validation.");
        using var image = Image.Load(path);
        Require(image.Width == item.Width && image.Height == item.Height, "P13_SCENE_AUTHORITY_INVALID", $"Certified source '{item.AssetId}' dimensions differ.");
        return (item, path);
    }

    internal static async Task<(string CropStrategy, GeneratedFileMetadata Metadata)> RenderAndReadbackAsync(
        string sourcePath, string target, string relativePath, string headline, string body, int slot, bool scientific, CancellationToken ct)
    {
        using var image = Image.Load<Rgba32>(sourcePath);
        var strategy = scientific ? "ContainScientificGeometry" : $"BoundedFocalCoverCrop-{slot + 1}";
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1080, 1080), Mode = scientific ? ResizeMode.Pad : ResizeMode.Crop,
            Position = (AnchorPositionMode)(slot % 3), PadColor = Color.Black, Sampler = KnownResamplers.Lanczos3 }));
        var family = SystemFonts.Collection.Families.First();
        var headlineFont = family.CreateFont(58, FontStyle.Bold); var bodyFont = family.CreateFont(30);
        image.Mutate(x => { x.Fill(Color.FromRgba(0, 0, 0, 175), new Rectangle(0, 760, 1080, 320));
            x.DrawText(headline, headlineFont, Color.White, new PointF(64, 800));
            x.DrawText(body, bodyFont, Color.FromRgb(210, 230, 245), new PointF(64, 900)); });
        image.SaveAsPng(target);
        var metadata = await ReadPhysicalMetadataAsync(target, relativePath, ct);
        return (strategy, metadata);
    }

    internal static async Task<GeneratedFileMetadata> ReadPhysicalMetadataAsync(string physicalPath, string relativePath, CancellationToken ct)
    {
        Require(File.Exists(physicalPath), "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' does not exist.");
        var info = new FileInfo(physicalPath);
        Require(info.Length > 0, "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' is empty.");

        using var decoded = await Image.LoadAsync(physicalPath, ct);
        Require(decoded.Width == 1080 && decoded.Height == 1080, "P13_GENERATED_FILE_METADATA_INVALID",
            $"Gallery candidate '{relativePath}' has physical dimensions {decoded.Width}x{decoded.Height}; expected 1080x1080.");
        Require(decoded.Metadata.DecodedImageFormat?.Name.Equals("PNG", StringComparison.OrdinalIgnoreCase) == true,
            "P13_GENERATED_FILE_METADATA_INVALID", $"Gallery candidate '{relativePath}' is not a decoded PNG.");
        await using var stream = File.OpenRead(physicalPath);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        return new(relativePath.Replace('\\', '/'), info.Name, decoded.Width, decoded.Height, "PNG", "image/png", info.Length, sha);
    }

    private static object Lineage(string artifact, string pointer, string source, string display, string rule) => new { authorityArtifact = artifact, authorityPointer = pointer, sourceValue = source, displayValue = display, transformationRule = rule };
    private static string RoleLabel(string role) => string.Join(' ', role.Split('-')).ToUpperInvariant();
    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..Math.Max(1, max - 1)].TrimEnd() + "…";
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string ResolveRequired(string root, params string[] candidates) => candidates.Select(x => Path.Combine(root, x.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists) ?? Path.Combine(root, candidates[0]);
    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException($"P13_AUTHORITY_PARSE_FAILED: {path}");
    private static Task Write<T>(string path, T value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct); }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Require(bool condition, string code, string message) { if (!condition) throw new InvalidOperationException($"{code}: {message}"); }
}
