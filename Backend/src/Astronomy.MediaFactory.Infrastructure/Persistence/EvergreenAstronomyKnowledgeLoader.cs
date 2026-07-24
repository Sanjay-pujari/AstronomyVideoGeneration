using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EvergreenAstronomyKnowledgeLoader : IEvergreenAstronomyKnowledgeLoader
{
    private readonly IOptions<AstronomyKnowledgeOptions> options;
    private readonly IOptions<RenderingOptions> renderingOptions;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedReviewStatuses = new(StringComparer.OrdinalIgnoreCase) { "Reviewed" };

    public EvergreenAstronomyKnowledgeLoader(IOptions<AstronomyKnowledgeOptions> options)
        : this(options, Options.Create(new RenderingOptions()))
    {
    }

    public EvergreenAstronomyKnowledgeLoader(IOptions<AstronomyKnowledgeOptions> options, IOptions<RenderingOptions> renderingOptions)
    {
        this.options = options;
        this.renderingOptions = renderingOptions;
    }

    public async Task<EvergreenAstronomyKnowledgeLoadResult> LoadByRelativePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("relativePath is required.");
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("relativePath must not be an absolute path.");
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(p => p == "..")) throw new ArgumentException("relativePath must not contain path traversal segments.");
        var configuredRootPath = options.Value.RootPath;
        var configuredWorkingDirectory = renderingOptions.Value.WorkingDirectory;
        var root = ResolveRootPath(configuredRootPath, configuredWorkingDirectory);
        var requestedRelativePath = normalized.StartsWith("Knowledge" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? normalized[("Knowledge" + Path.DirectorySeparatorChar).Length..] : normalized;
        var fullPath = Path.GetFullPath(Path.Combine(root, requestedRelativePath));
        if (!IsUnderRoot(fullPath, root)) throw new ArgumentException("relativePath resolves outside AstronomyKnowledge:RootPath.");
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            throw new FileNotFoundException(
                $"Astronomy knowledge file was not found. Configured Rendering:WorkingDirectory='{configuredWorkingDirectory}'. Configured AstronomyKnowledge:RootPath='{configuredRootPath}'. Resolved knowledge root='{root}'. Requested relative path='{relativePath.Replace('\\', '/')}'. Resolved full file path='{fullPath}'.",
                fullPath,
                ex);
        }
        EvergreenAstronomyKnowledgePackage? package;
        try { package = JsonSerializer.Deserialize<EvergreenAstronomyKnowledgePackage>(bytes, JsonOptions); }
        catch (JsonException ex) { throw new ArgumentException($"Invalid JSON in relativePath: {ex.Message}", ex); }
        if (package is null) throw new ArgumentException("knowledge package JSON is empty.");
        Validate(package);
        return new(package, relativePath.Replace('\\', '/'), fullPath, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public static string ResolveRootPath(string? configuredRootPath, string? configuredWorkingDirectory)
    {
        var workingDirectory = string.IsNullOrWhiteSpace(configuredWorkingDirectory) ? new RenderingOptions().WorkingDirectory : configuredWorkingDirectory.Trim();
        if (string.IsNullOrWhiteSpace(configuredRootPath)) return Path.GetFullPath(Path.Combine(workingDirectory, "Knowledge"));
        var rootPath = configuredRootPath.Trim();
        return Path.GetFullPath(Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(workingDirectory, rootPath));
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, normalizedRoot, comparison) || fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
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
