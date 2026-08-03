using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Canonical, schema-based cultural tradition resolution.</summary>
public static class Phase7CulturalClaimPolicy
{
    private static readonly IReadOnlyDictionary<string,string> SupportedTraditions =
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["greek"]="Greek", ["roman"]="Roman", ["arabic"]="Arabic",
            ["chinese"]="Chinese", ["indianHindu"]="IndianHindu"
        };

    /// <summary>
    /// Resolves a named tradition from the governed path first, then from explicit
    /// adapter metadata. Unknown and <c>other</c> branches intentionally do not resolve.
    /// </summary>
    public static string ResolveCulturalTradition(string approvedFieldPath,
        IReadOnlyDictionary<string,string>? metadata = null)
    {
        var parts=Phase7CanonicalFieldPathPolicy.Canonicalize(approvedFieldPath)
            .Split('.',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length>=3 && parts[0].Equals("cultureAndMythology",StringComparison.OrdinalIgnoreCase)
            && SupportedTraditions.TryGetValue(parts[1],out var fromPath)) return fromPath;
        if(metadata is not null && metadata.TryGetValue("traditionIdentity",out var declared)
            && SupportedTraditions.TryGetValue(declared,out var fromMetadata)) return fromMetadata;
        return "";
    }
}
