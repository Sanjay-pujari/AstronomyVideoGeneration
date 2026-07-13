namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public static class AstronomyFamilyVocabularyV1
{
    public const string PlanetPairing = nameof(PlanetPairing);
    public const string PlanetGrouping = nameof(PlanetGrouping);
    public const string MeteorShower = nameof(MeteorShower);
    public const string FullMoon = nameof(FullMoon);
    public const string NamedFullMoon = nameof(NamedFullMoon);
    public const string SolarEclipse = nameof(SolarEclipse);
    public const string LunarEclipse = nameof(LunarEclipse);
    public const string Occultation = nameof(Occultation);
    public const string Constellation = nameof(Constellation);
    public const string DeepSkyObject = nameof(DeepSkyObject);
    public const string PlanetProfile = nameof(PlanetProfile);
    public const string Comet = nameof(Comet);
    public const string ScientificExplainer = nameof(ScientificExplainer);
    public const string Opposition = nameof(Opposition);
    public const string Elongation = nameof(Elongation);
    public const string Transit = nameof(Transit);
    public const string LunarPhase = nameof(LunarPhase);

    public static readonly string[] ActiveFamilyIds = [PlanetPairing, PlanetGrouping, MeteorShower, FullMoon, NamedFullMoon, SolarEclipse, LunarEclipse, Occultation, Constellation, DeepSkyObject];
    public static readonly string[] FutureFamilyIds = [PlanetProfile, Comet, ScientificExplainer, Opposition, Elongation, Transit, LunarPhase];
}
