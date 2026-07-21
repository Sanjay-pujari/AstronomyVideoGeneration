using Astronomy.MediaFactory.Core.AstronomyDomain.Classification;
using Astronomy.MediaFactory.Core.AstronomyDomain.Entities;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Identity;
using Astronomy.MediaFactory.Core.AstronomyDomain.Sources;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;

namespace Astronomy.MediaFactory.Core.Constellations;

public static class ConstellationFamilyIds
{
    public const string FamilyId = "CONSTELLATION";
    public const string CanonicalProfileId = "Constellation";
    public const string OrionEntityId = "constellation:iau:ori";
}

public sealed record ConstellationStar(string Name, string BayerDesignation, string CommonRole, string Notes);
public sealed record ConstellationDeepSkyObject(string Name, string CatalogId, string ObjectType, string ObservationGuidance);
public sealed record ConstellationCulturalTradition(string TraditionId, string CultureOrSourceCommunity, string Summary, IReadOnlyList<string> SourceIds);
public sealed record ConstellationKnowledgeSource(string SourceId, string Title, string Publisher, string Url, string Authority);

public sealed record ConstellationKnowledge(
    AstronomyDomainEntity Entity,
    string IauAbbreviation,
    string GenitiveName,
    string CelestialRegion,
    string BestObservingSeasonNorthernHemisphere,
    string HemisphericVisibility,
    string ObservationDifficulty,
    string RecognitionGuidance,
    IReadOnlyList<ConstellationStar> PrincipalStars,
    IReadOnlyList<ConstellationDeepSkyObject> NotableDeepSkyObjects,
    IReadOnlyList<string> ScientificSignificance,
    IReadOnlyList<string> HistoricalSignificance,
    IReadOnlyList<ConstellationCulturalTradition> CulturalTraditions,
    IReadOnlyList<string> EducationalLearningPoints,
    IReadOnlyList<string> VisualGuidance,
    IReadOnlyList<ConstellationKnowledgeSource> ControlledSources);

public interface IConstellationKnowledgeProvider
{
    bool TryGetByEntityId(string entityId, out ConstellationKnowledge? knowledge);
    ConstellationKnowledge GetOrion();
}

public sealed class ConstellationDomainFamily : IAstronomyDomainFamily
{
    public string FamilyId => ConstellationFamilyIds.FamilyId;
    public AstronomyFamilyKind FamilyKind => AstronomyFamilyKind.SkyPattern;
    public AstronomyDomainCategory DomainCategory => AstronomyDomainCategory.EvergreenSky;
    public IReadOnlySet<AstronomyEntityKind> SupportedEntityKinds { get; } = new HashSet<AstronomyEntityKind> { AstronomyEntityKind.Constellation };
    public IReadOnlySet<string> SupportedEventTypeAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ConstellationFamilyIds.CanonicalProfileId };
    public bool Supports(AstronomyEntityIdentity identity) => identity.EntityKind == AstronomyEntityKind.Constellation && string.Equals(identity.FamilyId, FamilyId, StringComparison.OrdinalIgnoreCase);
    public DomainValidationResult ValidateEntity(IAstronomyDomainEntity entity)
    {
        var issues = new List<DomainValidationIssue>();
        if (!Supports(entity.Identity)) issues.Add(new("CONSTELLATION.Entity.Unsupported", "Entity must be a Constellation in the CONSTELLATION family.", DomainValidationSeverity.Error, "identity", entity.Identity.EntityId, FamilyId));
        if (string.IsNullOrWhiteSpace(entity.Identity.CanonicalCode)) issues.Add(new("CONSTELLATION.Entity.IauAbbreviationMissing", "IAU abbreviation is required for constellation entities.", DomainValidationSeverity.Error, "identity.canonicalCode", entity.Identity.EntityId, FamilyId));
        if (!entity.Metadata.IsEvergreen) issues.Add(new("CONSTELLATION.Entity.NotEvergreen", "Constellation entities must be marked evergreen.", DomainValidationSeverity.Error, "metadata.isEvergreen", entity.Identity.EntityId, FamilyId));
        return new DomainValidationResult(issues);
    }
}

public sealed class OrionConstellationKnowledgeProvider : IConstellationKnowledgeProvider
{
    private static readonly ConstellationKnowledge Orion = BuildOrion();
    public bool TryGetByEntityId(string entityId, out ConstellationKnowledge? knowledge)
    {
        knowledge = string.Equals(entityId?.Trim(), ConstellationFamilyIds.OrionEntityId, StringComparison.OrdinalIgnoreCase) || string.Equals(entityId?.Trim(), "Orion", StringComparison.OrdinalIgnoreCase) ? Orion : null;
        return knowledge is not null;
    }
    public ConstellationKnowledge GetOrion() => Orion;

    private static ConstellationKnowledge BuildOrion() => new(
        new AstronomyDomainEntity(
            new AstronomyEntityIdentity(ConstellationFamilyIds.OrionEntityId, "Orion", AstronomyEntityKind.Constellation, ConstellationFamilyIds.FamilyId, AstronomyDomainCategory.EvergreenSky, "Orion", "Ori", ["The Hunter"], new Dictionary<string,string>{{"IAUAbbreviation","Ori"}}, true, "International Astronomical Union"),
            new AstronomyClassification(AstronomyDomainCategory.EvergreenSky, AstronomyFamilyKind.SkyPattern, AstronomyEntityKind.Constellation, AstronomySubjectTemporality.Seasonal, new ScientificClassification("IAU", "Official constellation", Authority: "International Astronomical Union"), Tags: ["constellation", "winter sky", "naked-eye", "beginner"]),
            Sources: [new AstronomySourceReference("iau-constellation-orion", AstronomySourceType.AstronomicalCatalog, "International Astronomical Union", "The Constellations", Url: new Uri("https://www.iau.org/public/themes/constellations/"), Reliability: SourceReliability.Authoritative, AuthorityLevel: SourceAuthorityLevel.Official)],
            Metadata: new AstronomyDomainMetadata(Status: AstronomyContentStatus.Approved, Keywords: ["Orion", "Ori", "constellation"], IsEvergreen: true, IsTimeSensitive: false, RequiresLocation: false, RequiresObservationTime: false)),
        "Ori", "Orionis", "Celestial equator region; recognizable from both hemispheres during its season.", "December through February evenings", "Visible from much of both hemispheres; highest and easiest from equatorial and mid-northern latitudes during northern winter evenings.", "Beginner", "Find the three-star Belt, then use Betelgeuse and Bellatrix above it and Rigel and Saiph below it to trace the hourglass.",
        [new("Betelgeuse", "Alpha Orionis", "shoulder", "Red supergiant appearance cue; do not imply stable brightness."), new("Rigel", "Beta Orionis", "foot", "Blue-white bright star anchoring the lower pattern."), new("Bellatrix", "Gamma Orionis", "shoulder", "Bright star opposite Betelgeuse."), new("Saiph", "Kappa Orionis", "foot", "Lower corner of Orion's hourglass."), new("Alnitak", "Zeta Orionis", "belt", "Eastern Belt star."), new("Alnilam", "Epsilon Orionis", "belt", "Central Belt star."), new("Mintaka", "Delta Orionis", "belt", "Western Belt star near the celestial equator.")],
        [new("Orion Nebula", "M42", "emission nebula", "Visible as a fuzzy patch in Orion's Sword; binoculars improve the view."), new("Horsehead Nebula", "Barnard 33", "dark nebula", "Iconic astrophotography target, not a naked-eye object.")],
        ["Useful teaching field for stellar color and stellar evolution examples.", "Contains bright guide stars and well-known star-forming regions."],
        ["Recognized in Greco-Roman star lore as a hunter figure; present the story as cultural history, not science."],
        [new("greco-roman-orion", "Greco-Roman classical tradition", "The sky figure is commonly interpreted as a hunter in classical star lore.", ["iau-constellation-orion"] )],
        ["Constellation stars only appear near each other on the sky; they are not physically adjacent at one distance.", "A constellation is an official sky region as well as a recognizable pattern."],
        ["Show Belt first, then shoulders and feet.", "Label major stars without overcrowding.", "Avoid implying physical scale or depth proximity among stars.", "Separate cultural artwork overlays from scientific sky-map geometry."],
        [new("iau-constellation-orion", "The Constellations", "International Astronomical Union", "https://www.iau.org/public/themes/constellations/", "Official constellation names and abbreviations"), new("nasa-orion-nebula", "Orion Nebula overview", "NASA", "https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-messier-catalog/messier-42/", "Scientific deep-sky reference")]);
}
