using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>The single owner of the Phase 7 accepted-candidate semantic checksum.</summary>
public static class Phase7NarrationReleaseCandidateChecksum
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public const string ImplementationName = "Phase7NarrationReleaseCandidateChecksum.ComputeScenes/v1";

    public static string ComputeScenes<TScenes>(TScenes scenes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(scenes, Options)))).ToLowerInvariant();
}
