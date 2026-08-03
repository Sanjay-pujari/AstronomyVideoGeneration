using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class FamilyNarrationProfileResolver : IFamilyNarrationProfileResolver
{
    private readonly IReadOnlyDictionary<string, FamilyNarrationProfile> registrations;
    public FamilyNarrationProfileResolver()
    {
        var profiles = new[]
        {
            CreateConstellation(),
            Create("planet-documentary-v1", "PLANET", ["identity-and-appearance","orbit-and-rotation","atmosphere-and-surface","exploration","observation","misconceptions"], ["visual-hook","defining-feature","viewing-action","close"]),
            Create("moon-documentary-v1", "MOON", ["phase-geometry","illumination","rise-set-context","surface-features","observation","photography"], ["what-phase","why-it-looks-that-way","viewing-action","close"]),
            Create("star-documentary-v1", "STAR", ["identity","type-and-color","distance","apparent-brightness","lifecycle","constellation-context","observation"], ["visual-hook","defining-feature","viewing-action","close"]),
            Create("deep-sky-documentary-v1", "DEEP_SKY_OBJECT", ["identity","classification","distance-and-scale","scientific-structure","observation","astrophotography"], ["visual-hook","central-discovery","viewing-action","close"]),
            Create("meteor-shower-documentary-v1", "METEOR_SHOWER", ["parent-body","radiant","timing","expected-rate","moon-interference","weather-dependence","safety","history"], ["hook","peak-window","viewing-action","close"]),
            Create("conjunction-documentary-v1", "CONJUNCTION", ["objects","apparent-geometry","line-of-sight-geometry","timing","orientation","visibility","astrophotography"], ["hook","apparent-alignment","viewing-action","close"]),
            Create("grouping-documentary-v1", "GROUPING", ["key-objects","geometry","distance","timing","visibility","astrophotography"], ["hook","apparent-grouping","viewing-action","close"]),
            Create("occultation-documentary-v1", "OCCULTATION", ["key-objects","geometry","contact-timeline","timing","visibility-footprint","scientific-significance","equipment","safety"], ["hook","disappearance-reappearance","viewing-action","close"]),
            Create("transit-documentary-v1", "TRANSIT", ["key-objects","geometry","contact-timeline","visibility","equipment","safety","scientific-significance"], ["hook","contact-timeline","safe-viewing","close"]),
            Create("opposition-documentary-v1", "OPPOSITION", ["orbit","geometry","appearance","timing","location-dependence","equipment","astrophotography"], ["hook","brightness-and-size","viewing-action","close"]),
            Create("elongation-documentary-v1", "ELONGATION", ["geometry","timing","visibility","location-dependence","safety"], ["hook","morning-evening-geometry","viewing-action","close"]),
            Create("close-approach-documentary-v1", "CLOSE_APPROACH", ["key-objects","geometry","distance","uncertainty","timing","visibility","equipment","astrophotography"], ["hook","separation-and-uncertainty","viewing-action","close"]),
            Create("eclipse-documentary-v1", "ECLIPSE", ["geometry","contact-timeline","visibility","safety","scientific-significance","weather-dependence"], ["hook","geometry","safe-viewing","close"]),
            Create("comet-documentary-v1", "COMET", ["orbit-and-source","nucleus-coma-tails","brightness-and-uncertainty","observation","astrophotography","history"], ["hook","changing-comet","viewing-action","close"]),
            Create("satellite-documentary-v1", "SATELLITE", ["identity","orbital-motion","timing-and-track","brightness","observation","artificial-natural-distinction"], ["hook","orbital-motion","viewing-action","close"])
        };
        var map = profiles.ToDictionary(x => x.EventFamily, StringComparer.OrdinalIgnoreCase);
        Alias(map, "MOON", "LUNAR_PHASE");
        Alias(map, "DEEP_SKY_OBJECT", "GALAXY", "NEBULA", "STAR_FORMING_REGION", "CLUSTER");
        Alias(map, "GROUPING", "PLANET_GROUPING");
        Alias(map, "CONJUNCTION", "PLANET_CONJUNCTION");
        Alias(map, "SATELLITE", "ISS_PASS");
        registrations = map;
        Profiles = map.Values.DistinctBy(x => x.ProfileId).OrderBy(x => x.ProfileId, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<FamilyNarrationProfile> Profiles { get; }
    public FamilyNarrationProfileResolution Resolve(string eventFamily, string language)
    {
        if (!registrations.TryGetValue(eventFamily ?? "", out var profile))
            return new(false, null, "P7INPUT_EVENT_FAMILY_UNSUPPORTED", [$"Unsupported event family '{eventFamily}'."]);
        var normalized = NormalizeLanguage(language);
        if (!profile.SupportedLanguages.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return new(false, null, "P7INPUT_LANGUAGE_INVALID", [$"Language '{language}' is not supported by profile '{profile.ProfileId}'."]);
        return new(true, profile, "P7PROFILE_VALID", []);
    }

    private static void Alias(IDictionary<string, FamilyNarrationProfile> map, string canonical, params string[] aliases)
    { foreach (var alias in aliases) map[alias] = map[canonical]; }
    private static string NormalizeLanguage(string value) => value?.Split('-', '_')[0].ToLowerInvariant() ?? "";

    private static FamilyNarrationProfile CreateConstellation()
    {
        var mandatory = new[] { "opening-recognition","identity","recognition-geometry","key-objects","scientific-significance","history","culture-and-mythology","astronomy-astrology-clarification","observation","closing" };
        var order = new[] { "opening-recognition","identity","recognition-geometry","key-stars","deep-sky-star-formation","line-of-sight-geometry","history","culture-and-mythology","astronomy-astrology-clarification","observation","astrophotography","closing" };
        var safety = new[] { "Separate mythology from science.", "Separate astrology traditions from astronomical claims.", "Do not state a universal exact viewing time without location/date.", "Use tradition-specific cultural wording.", "Do not claim all constellation objects are physically related.", "Mark approximate observational values.", "Do not imply cultural systems are one-to-one equivalents." };
        var draft = new FamilyNarrationProfile("constellation-documentary-v1", Phase7FoundationContract.Version, "CONSTELLATION", ["en","hi"],
            new(8,12,16,new(480,600,900),mandatory,["deep-sky","astrophotography","scientific-significance"],order,"Open with recognition, not unsupported spectacle.","Close with an evidence-grounded observing invitation."),
            new(4,new(60,90,120),["hook","central-discovery","viewing-action","memorable-close"],"Create curiosity from an approved recognition claim.","Select one certified scientific insight.","Give qualified observation guidance.","End with a grounded memorable idea."),
            ["Identity","Recognition","ScientificStructure","KeyObjects","Observation","History","CultureAndMythology","AstrologyClarification"],
            ["DeepSkyObjects","Astrophotography","ScientificSignificance","RegionalTraditions"], safety,
            ["Resolve each language directly from certified localized content.","Protect catalog names and scientific designations."],
            ["Use IAU names where certified.","Do not blindly translate proper names."],
            ["Qualify location, date, time, weather, and approximation dependencies."],
            ["Attribute traditions specifically.","Qualify uncertain cultural associations."],
            new Dictionary<string, DurationRange>{{"Long",new(480,600,900)},{"Short",new(60,90,120)}},
            "Concise non-factual transitions are allowed only when explicitly configured.", "");
        return draft with { DeterministicChecksum = ProfileHash(draft) };
    }

    private static FamilyNarrationProfile Create(string id, string family, IReadOnlyList<string> domains, IReadOnlyList<string> beats)
    {
        domains = domains.Select(Canonical).Distinct(StringComparer.Ordinal).ToArray();
        var safety = new[] { "Ground factual claims in certified sources.", "Qualify location, date, time, weather, and uncertainty.", "Separate cultural tradition from scientific claims." };
        var draft = new FamilyNarrationProfile(id, Phase7FoundationContract.Version, family, ["en","hi"],
            new(4,8,15,new(360,600,900),domains,[],domains,"Open with a certified recognition or identity fact.","Close without adding a new unsupported claim."),
            new(4,new(60,90,120),beats,beats[0],beats.ElementAtOrDefault(1) ?? "central-discovery",beats.ElementAtOrDefault(2) ?? "viewing-action",beats[^1]),
            domains, ["History","CultureAndMythology","Astrophotography"], safety,
            ["Resolve en and hi independently from certified localization."], ["Preserve proper names and designations."],
            ["Never manufacture local viewing times."], ["Use tradition-specific attribution."],
            new Dictionary<string, DurationRange>{{"Long",new(360,600,900)},{"Short",new(60,90,120)}}, "Configured editorial connective text only.", "");
        return draft with { DeterministicChecksum = ProfileHash(draft) };
    }
    private static string ProfileHash(FamilyNarrationProfile p) => Phase7Determinism.Hash(p with { DeterministicChecksum = "" });
    private static string Canonical(string value)
    {
        var aliases = new Dictionary<string, NarrationKnowledgeDomainKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["objects"] = NarrationKnowledgeDomainKey.KeyObjects, ["apparent-geometry"] = NarrationKnowledgeDomainKey.Geometry,
            ["line-of-sight-geometry"] = NarrationKnowledgeDomainKey.Distance, ["orientation"] = NarrationKnowledgeDomainKey.RecognitionGeometry,
            ["identity-and-appearance"] = NarrationKnowledgeDomainKey.Identity, ["orbit-and-rotation"] = NarrationKnowledgeDomainKey.Orbit,
            ["atmosphere-and-surface"] = NarrationKnowledgeDomainKey.Atmosphere, ["misconceptions"] = NarrationKnowledgeDomainKey.EditorialSafety,
            ["phase-geometry"] = NarrationKnowledgeDomainKey.Geometry, ["illumination"] = NarrationKnowledgeDomainKey.Appearance,
            ["rise-set-context"] = NarrationKnowledgeDomainKey.Timing, ["surface-features"] = NarrationKnowledgeDomainKey.Surface,
            ["photography"] = NarrationKnowledgeDomainKey.Astrophotography, ["type-and-color"] = NarrationKnowledgeDomainKey.PhysicalCharacteristics,
            ["apparent-brightness"] = NarrationKnowledgeDomainKey.Appearance, ["constellation-context"] = NarrationKnowledgeDomainKey.Identity,
            ["classification"] = NarrationKnowledgeDomainKey.Identity, ["distance-and-scale"] = NarrationKnowledgeDomainKey.Distance,
            ["expected-rate"] = NarrationKnowledgeDomainKey.ActivityRate, ["orbit-and-source"] = NarrationKnowledgeDomainKey.Orbit,
            ["nucleus-coma-tails"] = NarrationKnowledgeDomainKey.PhysicalCharacteristics, ["brightness-and-uncertainty"] = NarrationKnowledgeDomainKey.Uncertainty,
            ["timing-and-track"] = NarrationKnowledgeDomainKey.Timing, ["brightness"] = NarrationKnowledgeDomainKey.Appearance
        };
        if (aliases.TryGetValue(value, out var alias)) return alias.ToString();
        return NarrationKnowledgeDomains.TryParse(value, out var key) ? key.ToString() : throw new InvalidOperationException($"Unknown narration knowledge domain '{value}'.");
    }
}
