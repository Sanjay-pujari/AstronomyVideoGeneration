using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public sealed class SemanticCapabilityCatalog : ISemanticCapabilityCatalog
{
    private readonly IReadOnlyDictionary<string, SemanticCapabilityDefinition> _byId;
    public SemanticCapabilityCatalog()
    {
        Capabilities = Build().ToArray();
        Validate();
        _byId = Capabilities.SelectMany(c => c.AcceptedAliases.Append(c.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Select(a => new { Alias = canonical(a), Definition = c }))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(x => x.Alias.Equals(x.Definition.CapabilityId, StringComparison.OrdinalIgnoreCase))?.Definition ?? g.First().Definition, StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<SemanticCapabilityDefinition> Capabilities { get; }
    public bool TryGet(string capabilityId, out SemanticCapabilityDefinition definition) => _byId.TryGetValue(canonical(capabilityId), out definition!);
    public SemanticCapabilityDefinition GetRequired(string capabilityId) => TryGet(capabilityId, out var d) ? d : throw new InvalidOperationException($"Capability registration invalid: Capability = {capabilityId}");
    public void Validate()
    {
        var dup = Capabilities.GroupBy(c => c.CapabilityId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidOperationException($"Duplicate capability ID rejected: {dup.Key}");
    }
    private static IEnumerable<SemanticCapabilityDefinition> Build()
    {
        string[] timing = ["BestViewingWindowObservationTimingAdapter","LocalPeakTimeObservationTimingAdapter","UtcEventIntervalObservationTimingAdapter","TimeOfDayObservationTimingAdapter"];
        yield return Cap("EventIdentity", ["EventIdentity","EventType","Name","Title","SubjectIdentity","ObjectName"], 80, ["ProductionRequestEventIdentityAdapter","ProductionEventIntelligenceEventIdentityAdapter","DocumentaryContractEventIdentityAdapter"]);
        yield return Cap("PrimaryObjects", ["PrimaryObjects","OccultingObject","HiddenObject","ObjectName","Name","Objects"], 80, ["ProductionEventIntelligencePrimaryObjectsAdapter","EditorialContractPrimaryObjectsAdapter","DocumentaryContractPrimaryObjectsAdapter"]);
        yield return Cap("SecondaryObjects", ["SecondaryObjects","HiddenObject","OccultingObject"], 70, ["ProductionEventIntelligencePrimaryObjectsAdapter","EditorialContractPrimaryObjectsAdapter","DocumentaryContractPrimaryObjectsAdapter"]);
        yield return Cap("EventDate", ["EventDate","EventDateOrWindow","Date"], 80, ["ProductionEventIntelligenceEventDateAdapter","ObservationMetadataEventDateAdapter"]);
        yield return Cap("EventTiming", ["EventTiming","StartTime","EndTime","PeakTime"], 75, timing);
        yield return Cap("ObservationTiming", ["ObservationTiming","EventDateOrWindow","ViewingWindow","BestViewingWindowLocal","LocalPeakTime","PeakTime","PeakWindow","StartTime","EndTime","TimeOfDay"], 75, timing);
        yield return Cap("LocalPeakTime", ["LocalPeakTime","PeakTime","PeakUTC","PeakWindow"], 75, ["LocalPeakTimeObservationTimingAdapter"]);
        yield return Cap("ObservationDirection", ["ObservationDirection","Direction","SkyDirection","SkyLocation","Radiant"], 75, ["ObservationDirectionAdapter"]);
        yield return Cap("ObservationLocation", ["ObservationLocation","LocationContext","Region","VisibilityRegion","Location","Timezone"], 70, ["ObservationLocationAdapter"]);
        yield return Cap("AngularRelationship", ["AngularRelationship","AngularSeparation","Separation"], 70, ["AngularSeparationAdapter"]);
        yield return Cap("AngularSeparation", ["AngularSeparation","AngularRelationship","Separation"], 70, ["AngularSeparationAdapter"]);
        yield return Cap("ObservationMode", ["ObservationMode","VisibilityMethod","NakedEye","BinocularGuidance"], 70, ["VisibilityMethodAdapter"]);
        yield return Cap("VisibilityMethod", ["VisibilityMethod","ObservationMode","NakedEye","BinocularGuidance","TelescopeGuidance"], 70, ["VisibilityMethodAdapter"]);
        yield return Cap("VisibilityConditions", ["VisibilityConditions","DarkSkyGuidance","MoonPhase","Visibility"], 60, ["VisibilityMethodAdapter"]);
        yield return Cap("Zhr", ["Zhr","ZHR","ZenithalHourlyRate","Zenithal Hourly Rate"], 75, ["ProductionEventIntelligenceZhrAdapter"], strictness: "OptionalEventSpecific", eventSpecific: true);
        yield return Cap("ApparentAlignmentExplanation", ["ApparentPairingScience","ApparentAlignmentExplanation","PerspectiveExplanation","WhyPlanetsAppearClose"], 80, ["DomainKnowledgeApparentAlignmentAdapter"], domain:["ApparentAlignmentExplanation"]);
        yield return Cap("PhysicalProximityClarification", ["PhysicalProximityClarification","PerspectiveExplanation","WhyPlanetsAppearClose"], 80, ["DomainKnowledgeApparentAlignmentAdapter"], domain:["PhysicalProximityClarification"]);
        foreach (var id in new[]{"ScientificMechanism","ScientificSignificance","IdentificationPattern","SkyRegion","MajorStars","ScientificIdentity","Mechanism","SafetyGuidance","Radiant","EclipseType","Name","ObjectType","PlanetType","ObjectName","Concept","Evidence","ScientificImportance","Orbit"})
            yield return Cap(id, [id,"Name","ObjectName","ObjectType","ScientificImportance","ScientificIdentity","Mechanism"], 60, ["DocumentaryContractPrimaryObjectsAdapter","EditorialContractPrimaryObjectsAdapter","ProductionEventIntelligencePrimaryObjectsAdapter"], domain: id.StartsWith("Scientific") ? [id] : []);
    }
    private static SemanticCapabilityDefinition Cap(string id, IReadOnlyList<string> aliases, int min, IReadOnlyList<string> adapters, IReadOnlyList<string>? rules=null, IReadOnlyList<string>? domain=null, string strictness="Strict", bool eventSpecific=false) => new(id, aliases, min, strictness, true, true, adapters, rules ?? [], domain ?? [], eventSpecific);
    private static string canonical(string id) => id.Equals("LocationContext", StringComparison.OrdinalIgnoreCase) ? "ObservationLocation" : id.Equals("ApparentPairingScience", StringComparison.OrdinalIgnoreCase) ? "ApparentAlignmentExplanation" : id;
}
