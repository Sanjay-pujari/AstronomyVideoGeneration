namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public sealed class CanonicalAstronomyEventIdentityResolverV1 : ICanonicalAstronomyEventIdentityResolverV1
{
    private readonly AstronomyEventAliasCatalogV1 _catalog;

    private static readonly IReadOnlyDictionary<string, (string Family, string Profile)> CanonicalProfiles =
        new Dictionary<string, (string Family, string Profile)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlanetPairing"] = ("PlanetPairing", "PlanetPairing"),
            ["PlanetGrouping"] = ("PlanetGrouping", "PlanetGrouping"),
            ["MeteorShower"] = ("MeteorShower", "MeteorShower"),
            ["FullMoon"] = ("FullMoon", "FullMoon"),
            ["NamedFullMoon"] = ("NamedFullMoon", "NamedFullMoon"),
            ["SolarEclipse"] = ("Eclipse", "SolarEclipse"),
            ["LunarEclipse"] = ("Eclipse", "LunarEclipse"),
            ["Occultation"] = ("Occultation", "Occultation"),
            ["Constellation"] = ("Constellation", "Constellation"),
            ["DeepSkyObject"] = ("DeepSkyObject", "DeepSkyObject")
        };

    public CanonicalAstronomyEventIdentityResolverV1()
        : this(new AstronomyEventAliasCatalogV1())
    {
    }

    public CanonicalAstronomyEventIdentityResolverV1(AstronomyEventAliasCatalogV1 catalog)
    {
        _catalog = catalog;
    }

    public CanonicalAstronomyEventIdentity Resolve(string? eventType, string resolutionSource = "ExplicitEventType")
    {
        var validation = _catalog.Validate();
        if (!validation.IsValid)
            return new(eventType, null, null, null, resolutionSource, [], false, validation.Errors);

        var normalized = _catalog.Normalize(eventType);
        var profile = normalized.CanonicalEventType is not null && CanonicalProfiles.TryGetValue(normalized.CanonicalEventType, out var p) ? p : default;

        return new(
            normalized.InputEventType,
            normalized.CanonicalEventType,
            normalized.Supported ? profile.Family : null,
            normalized.Supported ? profile.Profile : null,
            resolutionSource,
            normalized.AppliedAliases,
            normalized.Supported,
            normalized.DiagnosticMessages);
    }
}
