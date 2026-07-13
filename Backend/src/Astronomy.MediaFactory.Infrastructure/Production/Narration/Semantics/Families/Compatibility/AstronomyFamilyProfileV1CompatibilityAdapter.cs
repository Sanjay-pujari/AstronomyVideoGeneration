using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

public sealed class AstronomyFamilyProfileV1CompatibilityAdapter : IAstronomyFamilyProfileV1CompatibilityAdapter
{
    public string AdapterId => "AstronomyFamilyProfileV1CompatibilityAdapter-Sprint2C";

    public FamilyProfileCompatibilityResult Convert(AstronomyFamilyProfileV1 profile, FamilyProfileCompatibilityContext context)
    {
        var requiredCaps = Requirements(profile, required: true).ToArray();
        var optionalCaps = Requirements(profile, required: false).ToArray();
        var mappings = requiredCaps.Select(c => Map(profile.FamilyId, c, true)).Concat(optionalCaps.Select(c => Map(profile.FamilyId, c, false))).ToArray();
        var blocking = mappings.Where(m => m.Required && m.MappingKind == FamilyProfileCompatibilityMappingKind.UNSUPPORTED_FOR_CURRENT_RUNTIME)
            .Select(m => $"V1 capability '{m.V1CapabilityId}' for family '{profile.FamilyId}' cannot be represented by the current legacy runtime.").ToArray();
        var required = mappings.Where(m => m.Required && m.MappingKind != FamilyProfileCompatibilityMappingKind.UNSUPPORTED_FOR_CURRENT_RUNTIME).SelectMany(m => m.LegacyRequirements).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var optional = mappings.Where(m => !m.Required && m.MappingKind != FamilyProfileCompatibilityMappingKind.UNSUPPORTED_FOR_CURRENT_RUNTIME && m.MappingKind != FamilyProfileCompatibilityMappingKind.OPTIONAL_COMPATIBILITY_OMISSION).SelectMany(m => m.LegacyRequirements).Concat(ImplicitOptionalRequirements(profile.FamilyId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var omitted = mappings.Where(m => m.MappingKind == FamilyProfileCompatibilityMappingKind.OPTIONAL_COMPATIBILITY_OMISSION).Select(m => m.V1CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var diagnostics = new FamilyProfileCompatibilityDiagnostics(
            context.InputEventType ?? string.Empty, context.CanonicalEventType ?? profile.FamilyId, context.CanonicalFamilyId ?? profile.FamilyId, profile.FamilyId,
            context.AliasApplied, AdapterId, profile.FamilyId, required, omitted,
            mappings.Where(m => m.MappingKind == FamilyProfileCompatibilityMappingKind.UNSUPPORTED_FOR_CURRENT_RUNTIME).ToArray(), blocking, "V1", mappings, profile.Policy.MinimumObjectCount);
        if (blocking.Length > 0) return new(false, null, diagnostics, blocking);
        return new(true, Legacy(profile, required, optional), diagnostics, []);
    }

    private static IEnumerable<string> Requirements(AstronomyFamilyProfileV1 p, bool required) => p.LongFormStructure.Beats.Concat(p.ShortFormStructure.Beats)
        .SelectMany(b => b.Requirements).Where(r => required ? r.RequirementLevel == FamilyRequirementLevelV1.Required : r.RequirementLevel == FamilyRequirementLevelV1.Optional)
        .Select(r => r.SemanticCapabilityId.Value).Distinct(StringComparer.OrdinalIgnoreCase);

    private static FamilyProfileCompatibilityMapping Map(string family, string cap, bool required)
    {
        static FamilyProfileCompatibilityMapping M(string f, string c, bool r, FamilyProfileCompatibilityMappingKind k, string[] legacy, string d) => new(f, c, k, legacy, r, d);
        if (!required && cap is SemanticCapabilityVocabularyV1.EditorialContext or SemanticCapabilityVocabularyV1.ObservationLocation)
            return M(family, cap, false, FamilyProfileCompatibilityMappingKind.OPTIONAL_COMPATIBILITY_OMISSION, [], "Optional V1 context is recorded but omitted because the legacy Phase 7 profile does not require a minimum field for current behavior.");
        var legacy = (family, cap) switch
        {
            (_, SemanticCapabilityVocabularyV1.EventIdentity) => [family is "MeteorShower" or "NamedFullMoon" ? "Name" : "EventIdentity"],
            ("PlanetPairing" or "PlanetGrouping", SemanticCapabilityVocabularyV1.AstronomicalObjects) => ["PrimaryObjects"],
            ("Occultation", SemanticCapabilityVocabularyV1.AstronomicalObjects) => ["OccultingObject"],
            (_, SemanticCapabilityVocabularyV1.SecondaryAstronomicalObjects) => ["HiddenObject"],
            (_, SemanticCapabilityVocabularyV1.AngularSeparation) => ["AngularRelationship"],
            (_, SemanticCapabilityVocabularyV1.ObservationDirection) => ["ObservationDirection"],
            (_, SemanticCapabilityVocabularyV1.ObservationLocation) => ["VisibilityRegion"],
            (_, SemanticCapabilityVocabularyV1.ObservationConditions) => ["VisibilityConditions"],
            (_, SemanticCapabilityVocabularyV1.ObservationEquipment) => ["BinocularGuidance"],
            ("MeteorShower", SemanticCapabilityVocabularyV1.EventWindow) => ["EventDateOrWindow", "PeakWindow"],
            ("MeteorShower", SemanticCapabilityVocabularyV1.MeteorActivity) => required ? ["Radiant", "PeakWindow"] : ["Zhr"],
            ("FullMoon" or "NamedFullMoon", SemanticCapabilityVocabularyV1.EventWindow) => ["EventDateOrWindow"],
            ("FullMoon" or "NamedFullMoon", SemanticCapabilityVocabularyV1.FullMoonObservation) => ["MoonPhase"],
            ("SolarEclipse" or "LunarEclipse", SemanticCapabilityVocabularyV1.EventWindow) => ["EventDateOrWindow"],
            ("SolarEclipse" or "LunarEclipse", SemanticCapabilityVocabularyV1.EclipseCircumstances) => ["EclipseType", "VisibilityRegion", "Mechanism"],
            ("Occultation", SemanticCapabilityVocabularyV1.EventWindow) => ["StartTime"],
            ("Occultation", SemanticCapabilityVocabularyV1.OccultationContacts) => ["HiddenObject", "VisibilityRegion", "Mechanism"],
            ("PlanetPairing" or "PlanetGrouping", SemanticCapabilityVocabularyV1.EventWindow) => ["ObservationTiming"],
            (_, SemanticCapabilityVocabularyV1.ObjectKnowledge) => [family == "DeepSkyObject" ? "ObjectName" : "Name", "ScientificIdentity"],
            (_, SemanticCapabilityVocabularyV1.DomainScientificKnowledge) => [family is "PlanetPairing" or "PlanetGrouping" ? "ApparentPairingScience" : "ScientificImportance"],
            (_, SemanticCapabilityVocabularyV1.CulturalContext) => ["CulturalNameContext"],
            (_, SemanticCapabilityVocabularyV1.SafetyGuidance) => ["SafetyGuidance"],
            _ => Array.Empty<string>()
        };
        if (legacy.Length == 0) return M(family, cap, required, required ? FamilyProfileCompatibilityMappingKind.UNSUPPORTED_FOR_CURRENT_RUNTIME : FamilyProfileCompatibilityMappingKind.OPTIONAL_COMPATIBILITY_OMISSION, [], "No safe current-runtime representation exists.");
        var kind = legacy.Length == 1 && legacy[0].Equals(cap, StringComparison.OrdinalIgnoreCase) ? FamilyProfileCompatibilityMappingKind.EXACT_LEGACY_CAPABILITY : FamilyProfileCompatibilityMappingKind.EXPANDED_TO_LEGACY_REQUIREMENTS;
        return M(family, cap, required, kind, legacy, kind == FamilyProfileCompatibilityMappingKind.EXACT_LEGACY_CAPABILITY ? "Canonical capability is already accepted by the legacy resolver." : "Family-aware explicit legacy requirement expansion.");
    }

    private static IEnumerable<string> ImplicitOptionalRequirements(string family) => family switch
    {
        "MeteorShower" => ["Zhr"],
        "NamedFullMoon" => ["MoonriseTime", "CulturalNameContext"],
        "FullMoon" => ["MoonriseTime"],
        _ => []
    };

    private static AstronomyFamilyProfile Legacy(AstronomyFamilyProfileV1 p, IReadOnlyList<string> req, IReadOnlyList<string> opt) => new(
        p.FamilyId, p.ContentNature == "Reference" ? "EducationalObjectProfile" : "TimedObservationEvent", p.FamilyId switch { "MeteorShower" => "ObservationGuide", "Occultation" or "SolarEclipse" or "LunarEclipse" => "TimedMechanismExplainer", "Constellation" => "ObjectProfile", "DeepSkyObject" => "DeepSkyProfile", _ => "ObservationExplainer" },
        p.FamilyId switch { "Constellation" => "ConstellationShort", "DeepSkyObject" => "DeepSkyShort", _ => "SkyWatchShort" }, req, opt,
        p.LongFormStructure.Beats.Select(b => b.NarrativeRole).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), p.LongFormStructure.Beats.OrderBy(b => b.Order).Select(b => b.NarrativeRole).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        p.Policy.EventTimingRequired ? "Use verified observing details only." : "Observation details optional.", p.Policy.EventTimingRequired ? "Event date or window is required." : "No event date required.", ["V1Compatibility"], ["Unverified runtime facts"], ["Compatibility adapter must not invent semantic values"]);
}
