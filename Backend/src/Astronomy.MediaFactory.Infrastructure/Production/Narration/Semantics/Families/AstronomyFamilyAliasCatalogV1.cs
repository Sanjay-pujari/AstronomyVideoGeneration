using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record AstronomyFamilyAliasV1(string Alias, string CanonicalFamilyId);

public sealed class AstronomyFamilyAliasCatalogV1
{
    public static readonly AstronomyFamilyAliasV1[] DefaultAliases =
    [
        new("PlanetaryConjunction", AstronomyFamilyVocabularyV1.PlanetPairing),
        new("PlanetConjunction", AstronomyFamilyVocabularyV1.PlanetPairing),
        new("PLANET_CONJUNCTION", AstronomyFamilyVocabularyV1.PlanetPairing),
        new("PLANET_PAIRING", AstronomyFamilyVocabularyV1.PlanetPairing),
        new("Planet Grouping", AstronomyFamilyVocabularyV1.PlanetGrouping),
        new("MultiPlanetGrouping", AstronomyFamilyVocabularyV1.PlanetGrouping),
        new("PLANET_GROUPING", AstronomyFamilyVocabularyV1.PlanetGrouping),
        new("Meteor Shower", AstronomyFamilyVocabularyV1.MeteorShower),
        new("Full Moon", AstronomyFamilyVocabularyV1.FullMoon),
        new("Named Full Moon", AstronomyFamilyVocabularyV1.NamedFullMoon),
        new("Solar Eclipse", AstronomyFamilyVocabularyV1.SolarEclipse),
        new("Lunar Eclipse", AstronomyFamilyVocabularyV1.LunarEclipse),
        new("Deep Sky Object", AstronomyFamilyVocabularyV1.DeepSkyObject),
        new("DeepSky", AstronomyFamilyVocabularyV1.DeepSkyObject),
        new("BlackHoleOrScientificExplainer", AstronomyFamilyVocabularyV1.ScientificExplainer)
    ];
    public AstronomyFamilyAliasCatalogV1() : this(DefaultAliases) { }
    public AstronomyFamilyAliasCatalogV1(IEnumerable<AstronomyFamilyAliasV1> aliases) { Aliases = aliases.Select(a => new AstronomyFamilyAliasV1(a.Alias.Trim(), a.CanonicalFamilyId.Trim())).ToImmutableArray(); }
    public ImmutableArray<AstronomyFamilyAliasV1> Aliases { get; }
    public bool TryResolve(string input, out string canonical)
    {
        var hit = Aliases.FirstOrDefault(a => a.Alias.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase));
        canonical = hit?.CanonicalFamilyId ?? string.Empty;
        return hit is not null;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<AstronomyFamilyResolutionStatusV1>))]
public enum AstronomyFamilyResolutionStatusV1 { Resolved, FutureFamily, Unsupported }
public sealed record AstronomyFamilyResolutionV1(string? InputEventType, AstronomyFamilyResolutionStatusV1 Status, string? CanonicalFamilyId, string? ProfileId, bool AliasApplied, bool ActiveInV1, bool FutureFamily, string Diagnostic);
