using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Resolves entity identity without using array position or localized display rendering.</summary>
public sealed class Phase7KnowledgeEntityIdentityResolver : IPhase7KnowledgeEntityIdentityResolver
{
    private static readonly string[] Keys = ["stableKnowledgeId", "factId", "objectId", "externalId", "catalogId"];
    private static readonly string[] EntityPrefixes = ["star.", "planet.", "moon.", "constellation.", "object.", "galaxy.", "nebula.", "cluster.", "comet.", "satellite."];

    public Phase7KnowledgeEntityIdentity Resolve(JsonElement item, string fallbackKnowledgeId,
        IReadOnlyList<CertifiedNarrationSource> certifiedObjectRegistry, bool required)
    {
        if (item.ValueKind == JsonValueKind.Object)
            foreach (var key in Keys)
                if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return new(Normalize(value.GetString()!), key, false);

        var display = Display(item);
        var token = Token(display);
        var mapped = certifiedObjectRegistry.SelectMany(x => x.SupportedKnowledgeIds)
            .Where(x => EntityPrefixes.Any(p => x.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => Token(x[(x.IndexOf('.') + 1)..]) == token);
        if (mapped is not null) return new(mapped.ToLowerInvariant(), "CertifiedObjectRegistry", false);

        var canonical = Phase7Determinism.Hash(new { content = CanonicalContent(item) })[..20];
        return new($"{fallbackKnowledgeId.ToLowerInvariant()}.anonymous.{canonical}", "AnonymousContentFallback", required);
    }

    private static string Display(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String) return item.GetString()!;
        if (item.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "canonicalName", "objectName", "name", "catalogId" })
                if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString()!;
        return item.GetRawText();
    }
    private static string Token(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static string CanonicalContent(JsonElement item) => item.ValueKind == JsonValueKind.Object
        ? JsonSerializer.Serialize(item.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToDictionary(x => x.Name, x => x.Value))
        : Display(item).Trim().ToLowerInvariant();
}
