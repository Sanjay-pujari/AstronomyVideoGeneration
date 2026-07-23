using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EvergreenAstronomyKnowledgeLoader(IOptions<AstronomyKnowledgeOptions> options) : IEvergreenAstronomyKnowledgeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedReviewStatuses = new(StringComparer.OrdinalIgnoreCase) { "Reviewed" };

    public async Task<EvergreenAstronomyKnowledgeLoadResult> LoadByRelativePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("relativePath is required.");
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("relativePath must not be an absolute path.");
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(p => p == "..")) throw new ArgumentException("relativePath must not contain path traversal segments.");
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.RootPath) ? "Knowledge" : options.Value.RootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized.StartsWith("Knowledge" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? normalized[("Knowledge" + Path.DirectorySeparatorChar).Length..] : normalized));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("relativePath resolves outside AstronomyKnowledge:RootPath.");
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        EvergreenAstronomyKnowledgePackage? package;
        try { package = JsonSerializer.Deserialize<EvergreenAstronomyKnowledgePackage>(bytes, JsonOptions); }
        catch (JsonException ex) { throw new ArgumentException($"Invalid JSON in relativePath: {ex.Message}", ex); }
        if (package is null) throw new ArgumentException("knowledge package JSON is empty.");
        Validate(package);
        return new(package, relativePath.Replace('\\', '/'), fullPath, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public static void Validate(EvergreenAstronomyKnowledgePackage p)
    {
        var errors = new List<string>();
        Req(p.SchemaVersion, "schemaVersion"); if (p.SchemaVersion != "1.0") errors.Add("schemaVersion must be 1.0.");
        Req(p.KnowledgeId, "knowledgeId"); Req(p.FamilyCode, "familyCode");
        if (!string.Equals(Norm(p.FamilyCode), "CONSTELLATION", StringComparison.Ordinal)) errors.Add("familyCode must normalize to CONSTELLATION.");
        Req(p.CanonicalName, "canonicalName"); Req(p.KnowledgeVersion, "knowledgeVersion"); Req(p.ReviewStatus, "reviewStatus");
        if (!string.IsNullOrWhiteSpace(p.ReviewStatus) && !SupportedReviewStatuses.Contains(p.ReviewStatus)) errors.Add("reviewStatus has unsupported value.");
        if (p.Sources.Count == 0) errors.Add("sources must contain at least one source.");
        if (!p.LocalizedContent.ContainsKey("en")) errors.Add("localizedContent.en is required.");
        if (!p.LocalizedContent.ContainsKey("hi")) errors.Add("localizedContent.hi is required.");
        foreach (var s in p.Sources) { Req(s.SourceId, "sources[].sourceId"); Req(s.Reference, $"sources[{s.SourceId}].reference"); }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in p.Objects) { if (!ids.Add(o.ObjectId)) errors.Add($"objects[].objectId duplicate: {o.ObjectId}"); if (!names.Add(o.ObjectName)) errors.Add($"objects[].objectName duplicate: {o.ObjectName}"); }
        var primaryOrion = p.Objects.Count(o => o.ObjectRole.Equals("Primary", StringComparison.OrdinalIgnoreCase) && o.ObjectName.Equals("Orion", StringComparison.OrdinalIgnoreCase) && o.ObjectType.Equals("Constellation", StringComparison.OrdinalIgnoreCase));
        if (primaryOrion != 1) errors.Add("objects must contain exactly one primary Orion constellation object.");
        if (errors.Count > 0) throw new ArgumentException("Evergreen astronomy knowledge validation failed: " + string.Join(" ", errors));
        void Req(string? v, string f) { if (string.IsNullOrWhiteSpace(v)) errors.Add($"{f} is required."); }
    }
    private static string Norm(string value) => value.Trim().Replace('-', '_').ToUpperInvariant();
}
