using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

public interface IMeteorShowerKnowledgeCatalogV1
{
    MeteorShowerKnowledgeRecordV1? FindByCanonicalShowerIdentity(string? showerIdentity);
}

public sealed record MeteorShowerKnowledgeRecordV1(
    string CanonicalShowerId,
    string DisplayName,
    string RadiantConstellation,
    string? ParentBody,
    bool ParentBodyAuthoritative,
    int? ZenithalHourlyRate,
    string SupportedFamilyId,
    string Provenance,
    decimal Confidence);

public sealed class MeteorShowerKnowledgeCatalogV1 : IMeteorShowerKnowledgeCatalogV1
{
    private static readonly ImmutableArray<MeteorShowerKnowledgeRecordV1> Records =
    [
        new("geminids", "Geminids", "Gemini", "3200 Phaethon", true, 120, "MeteorShower", "IAU Meteor Data Center / NASA Solar System Exploration stable meteor-shower reference", 0.95m),
        new("perseids", "Perseids", "Perseus", "109P/Swift-Tuttle", true, 100, "MeteorShower", "IAU Meteor Data Center / NASA Solar System Exploration stable meteor-shower reference", 0.95m),
        new("leonids", "Leonids", "Leo", "55P/Tempel-Tuttle", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.9m),
        new("orionids", "Orionids", "Orion", "1P/Halley", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.9m),
        new("quadrantids", "Quadrantids", "Boötes", "2003 EH1", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.85m),
        new("lyrids", "Lyrids", "Lyra", "C/1861 G1 Thatcher", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.85m),
        new("eta-aquariids", "Eta Aquariids", "Aquarius", "1P/Halley", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.85m),
        new("taurids", "Taurids", "Taurus", "2P/Encke", true, null, "MeteorShower", "IAU Meteor Data Center stable meteor-shower reference", 0.8m)
    ];

    public MeteorShowerKnowledgeRecordV1? FindByCanonicalShowerIdentity(string? showerIdentity)
    {
        var key = Normalize(showerIdentity);
        if (string.IsNullOrWhiteSpace(key)) return null;
        return Records.FirstOrDefault(r => string.Equals(r.CanonicalShowerId, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(r.DisplayName), key, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0) return string.Empty;
        var first = text.Split(',', ';', '|').FirstOrDefault() ?? text;
        return first.Replace(" meteor shower", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" shower", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" peak", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("η", "eta", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-', ' ');
    }
}
