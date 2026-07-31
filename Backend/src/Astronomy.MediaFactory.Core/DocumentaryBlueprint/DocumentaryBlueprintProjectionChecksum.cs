using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryBlueprintProjectionChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string CalculateVariant(DocumentaryBlueprintVariantArtifact artifact) =>
        Hash(CanonicalJson(artifact with { DeterministicChecksum = string.Empty }));

    public static bool HasValidVariantChecksum(DocumentaryBlueprintVariantArtifact artifact) =>
        string.Equals(artifact.DeterministicChecksum, CalculateVariant(artifact), StringComparison.Ordinal);

    public static string CalculateAggregate(DocumentaryBlueprintAggregate aggregate) =>
        Hash(CanonicalJson(aggregate with { DeterministicChecksum = string.Empty }));

    public static bool HasValidAggregateChecksum(DocumentaryBlueprintAggregate aggregate) =>
        string.Equals(aggregate.DeterministicChecksum, CalculateAggregate(aggregate), StringComparison.Ordinal);

    internal static string Id(string prefix, params object[] parts) =>
        prefix + Hash(string.Join('|', parts.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture))))[..20];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Canonicalizes JSON object/dictionary keys ordinally. Array order remains semantic (notably scene order).</summary>
    internal static string CanonicalJson<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options);
        return Sort(node)?.ToJsonString(Options) ?? "null";
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => KeyValuePair.Create(x.Key, Sort(x.Value)))),
        JsonArray value => new JsonArray(value.Select(Sort).ToArray()),
        _ => node?.DeepClone()
    };
}
