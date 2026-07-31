using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryIntentChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public static string Calculate(DocumentaryIntent intent) => Hash(JsonSerializer.Serialize(intent with { DeterministicChecksum = "" }, Options));
    public static bool HasValidChecksum(DocumentaryIntent intent) => string.Equals(intent.DeterministicChecksum, Calculate(intent), StringComparison.Ordinal);
    internal static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value is string s ? s : JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
    internal static string Id(string prefix, params object[] parts) => prefix + DocumentaryIntentChecksum.Hash(string.Join('|', parts.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture))))[..20];
}
