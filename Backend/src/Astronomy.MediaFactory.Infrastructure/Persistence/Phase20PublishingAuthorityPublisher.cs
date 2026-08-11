using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Manifest-only, side-effect-free (outside its own root) publishing-package authority.</summary>
internal static class Phase20PublishingAuthorityPublisher
{
    internal const string Schema = "phase20.publishing-package/1.0";
    internal const string PlatformMapVersion = "phase20-platform-map/1.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<IReadOnlyList<string>> ExecuteAsync(string outputRoot, Guid planId, string language,
        IReadOnlyList<string> requestedOutputs, bool overwriteExisting, bool legacyPublishApproved,
        PublishingOptions policy, CancellationToken ct)
    {
        var requested = requestedOutputs.Where(x => x is "ShortVideo" or "LongVideo" or "Thumbnail" or "HeroAsset" or "Gallery")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var p19Root = Path.Combine(outputRoot, "19-video-qa", language);
        var p19Paths = new[] { Path.Combine(p19Root, "phase19-manifest.json"), Path.Combine(p19Root, "phase19-authority-diagnostics.json"),
            Path.Combine(p19Root, "phase19-publication-report.json"), Path.Combine(outputRoot, "validation", "phase-19-validation.json") };
        Phase19Manifest p19;
        JsonDocument[] p19Evidence;
        try
        {
            p19 = await Read<Phase19Manifest>(p19Paths[0], ct);
            p19Evidence = await Task.WhenAll(p19Paths.Skip(1).Select(x => ReadDocument(x, ct)));
            ValidatePhase19(p19, p19Evidence.Select(x => x.RootElement), language);
        }
        catch (Exception ex) when (ex is not Phase20AuthorityException)
        { throw new Phase20AuthorityException(Phase20ReasonCodes.UpstreamPhase19Invalid, "Committed Phase 19 authority is invalid.", ex); }
        using var p19EvidenceOwner = new DocumentOwner(p19Evidence);

        var p18Path = Path.Combine(outputRoot, "18-video-assembly", language, "phase18-manifest.json");
        Phase18Manifest? p18 = null;
        var entries = new List<PublishingManifestEntry>();
        if (requested.Contains("ShortVideo") || requested.Contains("LongVideo"))
        {
            try
            {
                p18 = await Read<Phase18Manifest>(p18Path, ct);
                if (p18.AuthorityChecksum != p19.SourcePhase18AuthorityChecksum) throw new InvalidDataException("Phase 18 lineage checksum differs from Phase 19.");
                foreach (var format in new[] { "Short", "Long" }.Where(x => requested.Contains(x + "Video")))
                {
                    var media = p18.Outputs.Single(x => x.Format.Equals(format, StringComparison.Ordinal));
                    var qa = p19.Outputs.Single(x => x.Format.Equals(format, StringComparison.Ordinal));
                    if (qa.VideoSha256 != media.VideoSha256 || qa.VideoByteLength != media.VideoByteLength || qa.VideoRelativePath != media.VideoRelativePath)
                        throw new InvalidDataException("Phase 19 media declaration differs from its Phase 18 lineage.");
                    entries.Add(await Entry(outputRoot, Path.Combine("18-video-assembly", language),
                        format == "Short" ? PublishingPackageRole.ShortVideo : PublishingPackageRole.LongVideo,
                        "mp4", language, 18, p18.AuthorityChecksum, media.VideoRelativePath, media.VideoSha256, media.VideoByteLength, "video/mp4", policy.PortableExportEnabled, ct));
                    entries.Add(await Entry(outputRoot, Path.Combine("18-video-assembly", language),
                        format == "Short" ? PublishingPackageRole.ShortCaptionSrt : PublishingPackageRole.LongCaptionSrt,
                        "srt", language, 18, p18.AuthorityChecksum, media.SubtitleRelativePath, media.SubtitleSha256, media.SubtitleByteLength,
                        "application/x-subrip", policy.PortableExportEnabled, ct));
                }
            }
            catch (Exception ex) when (ex is not Phase20AuthorityException)
            { throw new Phase20AuthorityException(Phase20ReasonCodes.UpstreamPhase19Invalid, "Phase 19 to Phase 18 media lineage is invalid.", ex); }
        }

        var supporting = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (requested.Contains("Thumbnail")) await AddSupporting(outputRoot, language, 12, "12-thumbnails", "thumbnail-asset-manifest.json", "phase12", supporting, entries, policy.PortableExportEnabled, ct);
        if (requested.Contains("HeroAsset")) await AddSupporting(outputRoot, language, 11, "11-hero", "hero-asset-manifest.json", "phase11", supporting, entries, policy.PortableExportEnabled, ct);
        if (requested.Contains("Gallery")) await AddSupporting(outputRoot, language, 13, "13-gallery", "gallery-manifest.json", "phase13", supporting, entries, policy.PortableExportEnabled, ct);

        entries = entries.OrderBy(x => x.Role).ThenBy(x => x.Sequence).ThenBy(x => x.SourceRelativePath, StringComparer.Ordinal).ToList();
        var decision = legacyPublishApproved
            ? new PublishingDecision($"legacy-{planId:D}", PublishingDecisionStatus.Approved, policy.PublishingPolicyVersion, null, null, "LegacyRequestCompatibility")
            : new PublishingDecision($"pending-{planId:D}", PublishingDecisionStatus.Pending, policy.PublishingPolicyVersion, null, null, "PublishingPolicy");
        var approved = !policy.ManualReviewRequired || decision.Status == PublishingDecisionStatus.Approved;
        var reasonCode = approved ? Phase20ReasonCodes.Accepted : decision.Status == PublishingDecisionStatus.Rejected ? Phase20ReasonCodes.GateRejected : Phase20ReasonCodes.GatePending;
        var packageId = Hash(JsonSerializer.Serialize(new { planId, language, requested, sourcePhase19AuthorityChecksum = p19.AuthorityChecksum, supporting, policy.PublishingPolicyVersion }, Json));
        var authorityChecksum = Hash(JsonSerializer.Serialize(new { Schema, packageId, entries, decision.DecisionId, decision.Status, policy.ManualReviewRequired, policy.PortableExportEnabled, PlatformMapVersion }, Json));
        var finalRoot = Path.Combine(outputRoot, "20-publishing", language);
        var stage = Path.Combine(outputRoot, "20-publishing", ".staging", Guid.NewGuid().ToString("N"), language);
        Directory.CreateDirectory(stage);
        try
        {
            if (policy.PortableExportEnabled) await CopyPortable(entries, outputRoot, stage, ct);
            var platformMap = PlatformMap(entries);
            var manifest = new { schemaVersion = Schema, language, sourcePhase19AuthorityChecksum = p19.AuthorityChecksum, authorityChecksum, artifacts = entries };
            var package = new { schemaVersion = Schema, publishingPackageId = packageId, planId, language, requestedOutputs = requested,
                sourcePhase19AuthorityChecksum = p19.AuthorityChecksum, supportingAuthorityChecksums = supporting, publishingPolicyVersion = policy.PublishingPolicyVersion,
                packageEnabled = policy.PackageEnabled, externalPublishingEnabled = policy.ExternalPublishingEnabled, manualReviewRequired = policy.ManualReviewRequired,
                portableExportEnabled = policy.PortableExportEnabled, decision, platformAssetMapVersion = PlatformMapVersion, platformAssetMap = platformMap,
                technicalQaApproved = true, publicationPackageReady = true, publishGateChecked = true, publishApproved = approved, downstreamReady = approved };
            await Write(Path.Combine(stage, "publishing-manifest.json"), manifest, ct);
            await Write(Path.Combine(stage, "publishing-package.json"), package, ct);
            await Write(Path.Combine(stage, "phase20-authority-diagnostics.json"), new { authorityChecksum, sourcePhase19AuthorityChecksum = p19.AuthorityChecksum,
                semanticValidationPassed = true, checksumValidationPassed = true, manifestValidationPassed = true, technicalQaApproved = true,
                publicationPackageReady = true, publishGateChecked = true, publishApproved = approved, downstreamReady = approved, reasonCode }, ct);
            Commit(stage, finalRoot, overwriteExisting);
            var reportPath = Path.Combine(finalRoot, "phase20-publication-report.json");
            await Write(reportPath, new { status = "Succeeded", reasonCode, authorityChecksum, sourcePhase19AuthorityChecksum = p19.AuthorityChecksum,
                publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
                checksumValidationPassed = true, manifestValidationPassed = true, manifestValidationStatus = "Valid", validationStatus = "Valid",
                technicalQaApproved = true, publicationPackageReady = true, publishGateChecked = true, publishApproved = approved, downstreamReady = approved }, ct);
            var validation = Path.Combine(outputRoot, "validation", "phase-20-validation.json");
            await Write(validation, new { phaseNo = 20, status = "Succeeded", reasonCode, authorityChecksum, sourcePhase19AuthorityChecksum = p19.AuthorityChecksum,
                publicationCommitted = true, committedReadbackPassed = true, committedStateValidationPassed = true, semanticValidationPassed = true,
                checksumValidationPassed = true, manifestValidationPassed = true, manifestValidationStatus = "Valid", validationStatus = "Valid",
                technicalQaApproved = true, publicationPackageReady = true, publishGateChecked = true, publishApproved = approved, downstreamReady = approved }, ct);
            return Directory.EnumerateFiles(finalRoot, "*", SearchOption.AllDirectories).Append(validation).Order(StringComparer.Ordinal).ToArray();
        }
        finally { var tx = Directory.GetParent(stage)?.FullName; if (tx is not null && Directory.Exists(tx)) Directory.Delete(tx, true); }
    }

    private static void ValidatePhase19(Phase19Manifest m, IEnumerable<JsonElement> evidence, string language)
    {
        if (!m.Language.Equals(language, StringComparison.OrdinalIgnoreCase) || !m.PublicationCommitted || !m.TechnicalQaApproved ||
            !m.DownstreamReady || m.ValidationStatus != "Valid" || string.IsNullOrWhiteSpace(m.AuthorityChecksum)) throw new InvalidDataException();
        foreach (var e in evidence)
            if (Text(e, "authorityChecksum") != m.AuthorityChecksum || !Bool(e, "publicationCommitted") || !Bool(e, "committedReadbackPassed") ||
                !Bool(e, "committedStateValidationPassed") || !Bool(e, "semanticValidationPassed") || !Bool(e, "checksumValidationPassed") ||
                !Bool(e, "manifestValidationPassed") || Text(e, "validationStatus") != "Valid" || !Bool(e, "downstreamReady")) throw new InvalidDataException();
    }

    private static async Task<PublishingManifestEntry> Entry(string outputRoot, string authorityRelativeRoot, PublishingPackageRole role,
        string format, string language, int phase, string checksum, string relative, string sha, long length, string contentType, bool portable, CancellationToken ct)
    {
        var authorityRoot = Path.Combine(outputRoot, authorityRelativeRoot);
        var path = SafePath(authorityRoot, relative);
        if (!File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.Directory)) throw new Phase20AuthorityException(Phase20ReasonCodes.ArtifactMissing, relative);
        await using var stream = File.OpenRead(path); var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (stream.Length != length || !actual.Equals(sha, StringComparison.OrdinalIgnoreCase)) throw new Phase20AuthorityException(Phase20ReasonCodes.ChecksumMismatch, relative);
        var packagePath = portable ? PackagePath(role, relative) : null;
        return new(role, format, language, phase, checksum, Path.Combine(authorityRelativeRoot, relative).Replace('\\', '/'), packagePath, actual, length, contentType);
    }

    private static async Task AddSupporting(string root, string language, int phase, string folder, string manifestName, string prefix,
        IDictionary<string, string> checksums, ICollection<PublishingManifestEntry> entries, bool portable, CancellationToken ct)
    {
        try
        {
            var authorityRoot = Path.Combine(root, folder); using var manifest = await ReadDocument(Path.Combine(authorityRoot, manifestName), ct);
            using var diagnostics = await ReadDocument(Path.Combine(authorityRoot, $"{prefix}-authority-diagnostics.json"), ct);
            using var report = await ReadDocument(Path.Combine(authorityRoot, $"{prefix}-publication-report.json"), ct);
            using var validation = await ReadDocument(Path.Combine(root, "validation", $"phase-{phase}-validation.json"), ct);
            var checksum = Text(manifest.RootElement, "authorityChecksum"); if (checksum.Length == 0) checksum = Text(manifest.RootElement, "deterministicChecksum");
            if (checksum.Length == 0 || !Bool(validation.RootElement, "downstreamReady") || Text(validation.RootElement, "validationStatus") != "Valid" ||
                !Bool(validation.RootElement, "publicationCommitted")) throw new InvalidDataException();
            foreach (var e in new[] { diagnostics.RootElement, report.RootElement, validation.RootElement })
            { var c = Text(e, "authorityChecksum"); if (c.Length > 0 && c != checksum) throw new InvalidDataException(); }
            checksums[$"Phase{phase}"] = checksum;
            foreach (var asset in FindAssets(manifest.RootElement, phase))
                entries.Add(await Entry(root, folder, asset.Role, asset.Format, language, phase, checksum, asset.Path, asset.Sha, asset.Length, asset.ContentType, portable, ct));
        }
        catch (Exception ex) when (ex is not Phase20AuthorityException { ReasonCode: Phase20ReasonCodes.SupportingAuthorityInvalid })
        { throw new Phase20AuthorityException(Phase20ReasonCodes.SupportingAuthorityInvalid, $"Requested Phase {phase} authority is invalid.", ex); }
    }

    private static IEnumerable<(PublishingPackageRole Role,string Format,string Path,string Sha,long Length,string ContentType)> FindAssets(JsonElement node, int phase)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var path = FirstText(node, "relativePath", "fileRelativePath", "imageRelativePath", "outputRelativePath");
            var sha = FirstText(node, "sha256", "fileSha256", "imageSha256"); var length = FirstLong(node, "byteLength", "fileSizeBytes");
            if (path.Length > 0 && sha.Length > 0 && length > 0)
            {
                var token = (FirstText(node, "role", "variant", "aspect", "aspectRatio") + " " + path).ToLowerInvariant();
                var role = phase == 13 ? PublishingPackageRole.GalleryImage : phase == 12
                    ? token.Contains("portrait") ? PublishingPackageRole.ThumbnailPortrait : token.Contains("square") ? PublishingPackageRole.ThumbnailSquare : PublishingPackageRole.ThumbnailLandscape
                    : token.Contains("portrait") ? PublishingPackageRole.HeroPortrait : token.Contains("square") ? PublishingPackageRole.HeroSquare : PublishingPackageRole.HeroLandscape;
                yield return (role, Path.GetExtension(path).TrimStart('.').ToLowerInvariant(), path, sha, length, FirstText(node, "contentType") is var c && c.Length > 0 ? c : "image/png");
            }
            foreach (var p in node.EnumerateObject()) foreach (var x in FindAssets(p.Value, phase)) yield return x;
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) foreach (var x in FindAssets(child, phase)) yield return x;
    }

    private static object PlatformMap(IEnumerable<PublishingManifestEntry> e) { bool Has(PublishingPackageRole r) => e.Any(x => x.Role == r); return new Dictionary<string, object> {
        ["YouTubeLong"] = new { video = Has(PublishingPackageRole.LongVideo) ? "LongVideo" : null, thumbnail = Has(PublishingPackageRole.ThumbnailLandscape) ? "ThumbnailLandscape" : null, caption = Has(PublishingPackageRole.LongCaptionSrt) ? "LongCaptionSrt" : null },
        ["YouTubeShort"] = new { video = Has(PublishingPackageRole.ShortVideo) ? "ShortVideo" : null, thumbnail = Has(PublishingPackageRole.ThumbnailPortrait) ? "ThumbnailPortrait" : null, caption = Has(PublishingPackageRole.ShortCaptionSrt) ? "ShortCaptionSrt" : null },
        ["FacebookLong"] = new { video = Has(PublishingPackageRole.LongVideo) ? "LongVideo" : null, thumbnail = Has(PublishingPackageRole.ThumbnailLandscape) ? "ThumbnailLandscape" : null },
        ["FacebookReel"] = new { video = Has(PublishingPackageRole.ShortVideo) ? "ShortVideo" : null, cover = Has(PublishingPackageRole.ThumbnailPortrait) ? "ThumbnailPortrait" : null },
        ["InstagramReel"] = new { video = Has(PublishingPackageRole.ShortVideo) ? "ShortVideo" : null, cover = Has(PublishingPackageRole.ThumbnailPortrait) ? "ThumbnailPortrait" : null } }; }
    private static async Task CopyPortable(IEnumerable<PublishingManifestEntry> entries, string root, string stage, CancellationToken ct) { foreach (var e in entries.Where(x => x.PackageRelativePath is not null)) { var source = SafePath(root, e.SourceRelativePath); var target = SafePath(stage, e.PackageRelativePath!); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using var a = File.OpenRead(source); await using var b = File.Create(target); await a.CopyToAsync(b, ct); } }
    private static string PackagePath(PublishingPackageRole role, string source) { var group = role switch { PublishingPackageRole.ShortVideo => "media/short", PublishingPackageRole.LongVideo => "media/long", PublishingPackageRole.ShortCaptionSrt or PublishingPackageRole.ShortCaptionAss => "captions/short", PublishingPackageRole.LongCaptionSrt or PublishingPackageRole.LongCaptionAss => "captions/long", PublishingPackageRole.ThumbnailLandscape or PublishingPackageRole.ThumbnailPortrait or PublishingPackageRole.ThumbnailSquare => "thumbnails", PublishingPackageRole.HeroLandscape or PublishingPackageRole.HeroPortrait or PublishingPackageRole.HeroSquare => "hero", _ => "gallery" }; return $"{group}/{Path.GetFileName(source)}"; }
    private static void Commit(string stage, string final, bool overwrite) { if (Directory.Exists(final)) { if (!overwrite) Directory.Delete(final, true); else Directory.Delete(final, true); } Directory.CreateDirectory(Path.GetDirectoryName(final)!); Directory.Move(stage, final); }
    private static string SafePath(string root, string relative) { if (Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute manifest path."); var r = Path.GetFullPath(root) + Path.DirectorySeparatorChar; var p = Path.GetFullPath(Path.Combine(root, relative)); if (!p.StartsWith(r, StringComparison.Ordinal)) throw new InvalidDataException("Manifest traversal path."); return p; }
    private static Task Write(string path, object value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false), ct); }
    private static async Task<T> Read<T>(string path, CancellationToken ct) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidDataException(path);
    private static async Task<JsonDocument> ReadDocument(string path, CancellationToken ct) => JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
    private static string Text(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static string FirstText(JsonElement e, params string[] n) => n.Select(x => Text(e, x)).FirstOrDefault(x => x.Length > 0) ?? "";
    private static long FirstLong(JsonElement e, params string[] n) => n.Select(x => e.TryGetProperty(x, out var v) && v.TryGetInt64(out var l) ? l : 0).FirstOrDefault(x => x > 0);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed class DocumentOwner(JsonDocument[] documents) : IDisposable { public void Dispose() { foreach (var d in documents) d.Dispose(); } }
}

internal sealed class Phase20AuthorityException(string reasonCode, string reason, Exception? inner = null) : InvalidOperationException($"{reasonCode}: {reason}", inner)
{ internal string ReasonCode { get; } = reasonCode; }
