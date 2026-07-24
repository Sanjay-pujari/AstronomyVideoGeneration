using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using SemanticCapabilityDefinitionV1 = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.SemanticCapabilityDefinition;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public sealed class SemanticCapabilityCatalogV1 : ISemanticCapabilityCatalogV1
{
    private readonly ImmutableArray<SemanticCapabilityDefinitionV1> _definitions;
    private readonly ImmutableDictionary<string, SemanticCapabilityDefinitionV1> _canonical;
    private readonly ImmutableDictionary<string, SemanticCapabilityDefinitionV1> _aliases;
    private readonly ImmutableArray<LegacySemanticCapabilityMapEntry> _legacy;

    public SemanticCapabilityCatalogV1() : this(BuildDefinitions(), LegacySemanticCapabilityMapV1.Entries) { }
    internal SemanticCapabilityCatalogV1(IEnumerable<SemanticCapabilityDefinitionV1> definitions, IEnumerable<LegacySemanticCapabilityMapEntry> legacy)
    {
        _definitions = definitions.ToImmutableArray();
        _legacy = legacy.ToImmutableArray();
        var result = Validate(_definitions, _legacy);
        if (!result.IsValid) throw new InvalidOperationException("Invalid SemanticCapabilityCatalogV1: " + string.Join("; ", result.Errors));
        _canonical = _definitions.ToImmutableDictionary(d => Normalize(d.CapabilityId), d => d, StringComparer.OrdinalIgnoreCase);
        _aliases = _definitions.SelectMany(d => d.AcceptedAliases.Where(a => !a.Equals(d.CapabilityId, StringComparison.OrdinalIgnoreCase)).Select(a => (a, d))).ToImmutableDictionary(x => Normalize(x.a), x => x.d, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<SemanticCapabilityDefinitionV1> Definitions => _definitions;
    public bool TryGet(SemanticCapabilityId id, out SemanticCapabilityDefinitionV1 definition) => _canonical.TryGetValue(Normalize(id.Value), out definition!);
    public SemanticCapabilityDefinitionV1 GetRequired(SemanticCapabilityId id) => TryGet(id, out var d) ? d : throw new KeyNotFoundException($"Unknown V1 semantic capability: {id.Value}");
    public LegacySemanticCapabilityResolution ResolveLegacyTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return new(term, LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, null, null, LegacySemanticCapabilityMigrationDisposition.UnsupportedLegacyTerm, false, "Blank legacy term.");
        var n = Normalize(term);
        if (_canonical.TryGetValue(n, out var canonical)) return new(term, LegacySemanticCapabilityResolutionStatus.CanonicalMatch, new(canonical.CapabilityId), null, LegacySemanticCapabilityMigrationDisposition.CanonicalCapability, false, null);
        var entries = _legacy.Where(e => Normalize(e.LegacyTerm) == n).ToArray();
        if (entries.Length > 1) return new(term, LegacySemanticCapabilityResolutionStatus.AmbiguousMapping, null, null, LegacySemanticCapabilityMigrationDisposition.UnsupportedLegacyTerm, false, "Legacy term has multiple mappings.");
        if (entries.Length == 1)
        {
            var e = entries[0];
            var status = e.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.StructuredField ? LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration : LegacySemanticCapabilityResolutionStatus.DeprecatedAliasMatch;
            return new(term, status, e.CanonicalCapabilityId, e.StructuredFieldPath, e.MigrationDisposition, true, null);
        }
        if (_aliases.TryGetValue(n, out var alias)) return new(term, LegacySemanticCapabilityResolutionStatus.DeprecatedAliasMatch, new(alias.CapabilityId), null, LegacySemanticCapabilityMigrationDisposition.CanonicalCapability, true, null);
        return new(term, LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, null, null, LegacySemanticCapabilityMigrationDisposition.UnsupportedLegacyTerm, false, "No V1 mapping exists for this term.");
    }
    public SemanticCapabilityCatalogValidationResult Validate() => Validate(_definitions, _legacy);
    public static SemanticCapabilityCatalogValidationResult Validate(IEnumerable<SemanticCapabilityDefinitionV1> definitions, IEnumerable<LegacySemanticCapabilityMapEntry> legacy)
    {
        var errors = new List<string>(); var defs = definitions.ToArray(); var canon = defs.Select(d => Normalize(d.CapabilityId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (defs.Any(d => string.IsNullOrWhiteSpace(d.CapabilityId))) errors.Add("Blank canonical ID.");
        foreach (var g in defs.GroupBy(d => Normalize(d.CapabilityId)).Where(g => g.Count() > 1)) errors.Add($"Duplicate canonical ID: {g.Key}");
        var aliasOwners = defs.SelectMany(d => d.AcceptedAliases.Select(a => (Alias:a, Owner:d.CapabilityId))).Where(x => !x.Alias.Equals(x.Owner, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (aliasOwners.Any(x => string.IsNullOrWhiteSpace(x.Alias))) errors.Add("Blank alias.");
        foreach (var g in aliasOwners.GroupBy(x => Normalize(x.Alias)).Where(g => g.Select(x => x.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)) errors.Add($"Duplicate alias ownership: {g.Key}");
        foreach (var a in aliasOwners.Where(x => canon.Contains(Normalize(x.Alias)))) errors.Add($"Alias equals canonical ID: {a.Alias}");
        foreach (var a in aliasOwners.Where(x => defs.Any(d => d.CapabilityId.Equals(x.Alias, StringComparison.OrdinalIgnoreCase) && d.AcceptedAliases.Any(ra => ra.Equals(x.Owner, StringComparison.OrdinalIgnoreCase))))) errors.Add($"Reciprocal alias: {a.Owner}<->{a.Alias}");
        var map = legacy.ToArray();
        foreach (var g in map.GroupBy(e => Normalize(e.LegacyTerm)).Where(g => g.Count() > 1)) errors.Add($"Duplicate legacy mapping: {g.Key}");
        var structuredMapTerms = map.Where(e => e.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.StructuredField).Select(e => Normalize(e.LegacyTerm)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var a in aliasOwners.Where(x => structuredMapTerms.Contains(Normalize(x.Alias)))) errors.Add($"Legacy field mapping also registered as alias: {a.Alias}");
        foreach (var e in map) { if (e.MigrationDisposition == 0 && e.CanonicalCapabilityId is null) errors.Add($"Missing disposition: {e.LegacyTerm}"); if (e.CanonicalCapabilityId is { } id && !canon.Contains(Normalize(id.Value))) errors.Add($"Unknown canonical target: {e.LegacyTerm}->{id.Value}"); if (e.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.StructuredField && (string.IsNullOrWhiteSpace(e.StructuredFieldPath) || !e.StructuredFieldPath.Contains('.', StringComparison.Ordinal))) errors.Add($"Invalid structured field path: {e.LegacyTerm}"); }
        return new(errors.Count == 0, errors);
    }
    private static string Normalize(string value) => value.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    private static SemanticCapabilityDefinitionV1 Def(string id, string desc, string valueType, string[] evidence, int min, bool loc, bool nar, bool evt, string[] aliases, string policy) => new(id, [id, .. aliases], min, SemanticCapabilityStrictness.Strict, loc, nar, [], [], [.. evidence, $"description:{desc}", $"valueType:{valueType}", "aliasesOrdered:false", $"sourcePolicy:{policy}"], evt);
    private static IEnumerable<SemanticCapabilityDefinitionV1> BuildDefinitions()
    {
        yield return Def("EventIdentity","Canonical event identity.","EventIdentityValue",["EventMetadata","EditorialContract"],80,true,true,true,["EventType","Title","SubjectIdentity"],"v1-policy-event-identity");
        yield return Def("EventWindow","Observation or event time window.","EventWindowValue",["ObservationMetadata","Ephemeris"],80,true,true,true,[],"v1-policy-event-window");
        yield return Def("AstronomicalObjects","Primary astronomical objects.","AstronomicalObjectList",["EventIntelligence"],80,true,true,true,[],"v1-policy-objects");
        yield return Def("SecondaryAstronomicalObjects","Secondary astronomical objects.","AstronomicalObjectList",["EventIntelligence"],70,true,true,true,[],"v1-policy-secondary-objects");
        yield return Def("AngularSeparation","Apparent angular separation.","AngularMeasure",["Ephemeris"],70,true,true,true,["AngularRelationship","Separation"],"v1-policy-angular-separation");
        yield return Def("ObservationDirection","Where to look in the sky.","SkyDirection",["ObservationMetadata"],70,true,true,true,["Direction","SkyDirection","Radiant"],"v1-policy-direction");
        yield return Def("ObservationLocation","Observer location context.","LocationContext",["Request","ObservationMetadata"],70,true,true,true,["LocationContext","Region","VisibilityRegion"],"v1-policy-location");
        yield return Def("ObservationConditions","Sky visibility conditions.","ObservationConditions",["ObservationMetadata"],60,true,true,true,["VisibilityConditions","Visibility"],"v1-policy-conditions");
        yield return Def("ObservationEquipment","Viewing equipment guidance.","ObservationEquipment",["ObservationMetadata"],60,true,true,true,[],"v1-policy-equipment");
        yield return Def("MeteorActivity","Meteor shower activity metrics.","MeteorActivity",["EventIntelligence"],75,true,true,true,[],"v1-policy-meteor");
        yield return Def("FullMoonObservation","Full moon observing facts.","FullMoonObservation",["Ephemeris"],70,true,true,true,[],"v1-policy-full-moon");
        yield return Def("EclipseCircumstances","Eclipse type and circumstances.","EclipseCircumstances",["Ephemeris"],75,true,true,true,[],"v1-policy-eclipse");
        yield return Def("OccultationContacts","Occultation contact timings.","OccultationContacts",["Ephemeris"],75,true,true,true,[],"v1-policy-occultation");
        yield return Def("ObjectKnowledge","Knowledge about astronomical objects.","ObjectKnowledge",["DomainKnowledge"],60,true,true,false,[],"v1-policy-object-knowledge");
        yield return Def("DomainScientificKnowledge","Scientific explanation knowledge.","DomainScientificKnowledge",["DomainKnowledge"],80,true,true,false,[],"v1-policy-science");
        yield return Def("CulturalContext","General cultural naming and mythology context.","CulturalContext",["DomainKnowledge","EditorialContract"],60,true,true,false,["Mythology","WolfMoon","SnowMoon"],"v1-policy-cultural");
        yield return Def("CulturalNameContext","Cultural name origin, mythology, historical naming, and regional sky-culture notes.","CulturalContext",["KnowledgePackage","DomainKnowledge","ReviewedSource","EditorialProjection"],60,true,true,false,[],"v1-policy-cultural-name-context");
        yield return Def("EditorialContext","Editorial framing context.","EditorialContext",["EditorialContract"],60,true,true,false,[],"v1-policy-editorial");
        yield return Def("SafetyGuidance","Safe observing guidance.","SafetyGuidance",["DomainKnowledge"],90,true,true,true,[],"v1-policy-safety");
    }
}
