using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryBlueprintProjectionChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string CalculateVariant(DocumentaryBlueprintVariantArtifact artifact) =>
        Hash(JsonSerializer.Serialize(artifact with { DeterministicChecksum = string.Empty }, Options));

    public static bool HasValidVariantChecksum(DocumentaryBlueprintVariantArtifact artifact) =>
        string.Equals(artifact.DeterministicChecksum, CalculateVariant(artifact), StringComparison.Ordinal);

    public static string CalculateAggregate(DocumentaryBlueprintAggregate aggregate) =>
        Hash(JsonSerializer.Serialize(aggregate with { DeterministicChecksum = string.Empty }, Options));

    public static bool HasValidAggregateChecksum(DocumentaryBlueprintAggregate aggregate) =>
        string.Equals(aggregate.DeterministicChecksum, CalculateAggregate(aggregate), StringComparison.Ordinal);

    internal static string Id(string prefix, params object[] parts) =>
        prefix + Hash(string.Join('|', parts.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture))))[..20];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
