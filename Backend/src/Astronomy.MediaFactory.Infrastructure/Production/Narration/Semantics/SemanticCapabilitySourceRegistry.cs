using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public sealed class SemanticCapabilitySourceRegistry(ISemanticCapabilityCatalog catalog) : ISemanticCapabilitySourceRegistry
{
    public IReadOnlyList<ISemanticCapabilitySourceAdapter> Adapters { get; } = BuildAdapters();
    public IReadOnlyList<ISemanticCapabilitySourceAdapter> GetAdapters(string capabilityId)
    {
        var def = catalog.GetRequired(capabilityId);
        return Adapters.Where(a => def.ApprovedSourceAdapterIds.Contains(a.AdapterId, StringComparer.OrdinalIgnoreCase)).OrderBy(a => a.Precedence).ThenByDescending(a => a.Strength).ToArray();
    }
    public void Validate()
    {
        var dup = Adapters.GroupBy(a => a.AdapterId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidOperationException($"Duplicate adapter ID rejected: {dup.Key}");
        foreach (var a in Adapters) if (!catalog.TryGet(a.SupportedCapabilityId, out _)) throw new InvalidOperationException($"Unsupported adapter/capability combination rejected: {a.AdapterId} -> {a.SupportedCapabilityId}");
    }
    public IReadOnlyList<string> ValidateCoverage(IEnumerable<AstronomyFamilyProfile> familyProfiles)
    {
        return ValidateCoverageDetailed(familyProfiles)
            .Where(r => !r.ResolutionPathValid)
            .Select(r => $"Capability registration invalid:\nFamilyProfile = {r.FamilyProfile}\nFormat = {r.Format}\nBeatRole = {r.BeatRole}\nCapability = {r.Capability}\nRequired = {r.Required}\nCatalogRegistrationFound = {r.CatalogRegistrationFound}\nRegisteredAdapters = {r.RegisteredAdapterIds.Count}\nDerivedRules = {r.ApprovedDerivationRuleIds.Count}\nDomainProviders = {r.ApprovedDomainProviderIds.Count}")
            .ToArray();
    }

    public IReadOnlyList<SemanticCapabilityCoverageRecord> ValidateCoverageDetailed(IEnumerable<AstronomyFamilyProfile> familyProfiles)
    {
        Validate();
        var rows = new List<SemanticCapabilityCoverageRecord>();
        foreach (var p in familyProfiles)
        foreach (var format in new[] { "long", "short" })
        foreach (var role in p.AllowedBeatRoles)
        {
            var required = p.RequiredFactTypes.Concat(RequiredForBeat(p, role, format)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var optional = p.OptionalFactTypes.Distinct(StringComparer.OrdinalIgnoreCase).Except(required, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var cap in required.Select(c => (Capability: c, Required: true)).Concat(optional.Select(c => (Capability: c, Required: false))))
            {
                var found = catalog.TryGet(cap.Capability, out var def);
                var adapters = found ? GetAdapters(cap.Capability).Select(a => a.AdapterId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
                var rules = found ? def!.ApprovedDerivationRuleIds : [];
                var domain = found ? def!.ApprovedDomainKnowledgeFactTypes : [];
                var hasPath = found && (adapters.Length > 0 || rules.Count > 0 || domain.Count > 0);
                var valid = found && (!cap.Required || hasPath);
                rows.Add(new(p.FamilyId, format, role, found ? def!.CapabilityId : cap.Capability, cap.Required, found, adapters, rules, domain, valid, valid ? null : !found ? "CapabilityNotRegistered" : "NoApprovedSourceAvailable"));
            }
        }
        return rows;
    }
    private static IEnumerable<string> RequiredForBeat(AstronomyFamilyProfile p, string role, string format)
    {
        var r = role.ToLowerInvariant();
        if (p.FamilyId is "PlanetaryConjunction" or "PlanetPairing")
        {
            if (r.Contains("hook")) return ["PrimaryObjects", "EventIdentity"];
            if (r.Contains("timing")) return ["ObservationTiming"];
            if (r.Contains("science")) return ["ApparentAlignmentExplanation", "PhysicalProximityClarification"];
            if (r.Contains("observation")) return ["ObservationTiming"];
            if (r.Contains("orientation")) return format == "long" ? ["LocationContext"] : [];
            return ["PrimaryObjects"];
        }
        if (!p.ContentNature.Contains("Event", StringComparison.OrdinalIgnoreCase)) return p.RequiredFactTypes.Where(t => !Regex.IsMatch(t, "Date|Time|Peak|Window", RegexOptions.IgnoreCase));
        return p.RequiredFactTypes;
    }
    private static IReadOnlyList<ISemanticCapabilitySourceAdapter> BuildAdapters() => [
        new GenericAdapter("ProductionRequestEventIdentityAdapter","EventIdentity","Production Request","eventType/title/subjectIdentity",100,1, ["eventTitle","title","shortTitle","eventType","family","subjectIdentity"]),
        new GenericAdapter("ProductionEventIntelligenceEventIdentityAdapter","EventIdentity","Production Event Intelligence","eventType/title/subjectIdentity",95,2, ["eventTitle","title","shortTitle","eventType","family","subjectIdentity"]),
        new GenericAdapter("DocumentaryContractEventIdentityAdapter","EventIdentity","Documentary Contract","eventType/title/subjectIdentity",80,4, ["eventTitle","title","shortTitle","eventType","family","subjectIdentity"]),
        new GenericAdapter("ProductionEventIntelligencePrimaryObjectsAdapter","PrimaryObjects","Production Event Intelligence","primaryObjects/objectPair/objects",95,1, ["primaryObjects","objectPair","objects","primaryObject","objectName","name"]),
        new GenericAdapter("EditorialContractPrimaryObjectsAdapter","PrimaryObjects","Editorial Contract","primaryObjects/objectPair/objects",85,2, ["primaryObjects","objectPair","objects","primaryObject","objectName","name"]),
        new GenericAdapter("DocumentaryContractPrimaryObjectsAdapter","PrimaryObjects","Documentary Contract","allocatedFacts/primaryObjects",80,3, ["primaryObjects","objectPair","objects","primaryObject","objectName","name"]),
        new GenericAdapter("ProductionEventIntelligenceEventDateAdapter","EventDate","Production Event Intelligence","eventDate/date",90,1, ["eventDate","date","eventDateOrWindow"]),
        new GenericAdapter("ObservationMetadataEventDateAdapter","EventDate","Observation Metadata","eventDate/date",85,2, ["eventDate","date","eventDateOrWindow"]),
        new GenericAdapter("BestViewingWindowObservationTimingAdapter","ObservationTiming","Observation Metadata","bestViewingWindowLocal/viewingWindow",100,1, ["bestViewingWindowLocal","viewingWindow","preferredViewingWindow","localViewingInterval"]),
        new GenericAdapter("LocalPeakTimeObservationTimingAdapter","ObservationTiming","Observation Metadata","localPeakTime/peakTime",90,2, ["localPeakTime","peakTime","peakWindow"]),
        new UtcAdapter(),
        new GenericAdapter("TimeOfDayObservationTimingAdapter","ObservationTiming","Observation Metadata","timeOfDayGuidance",75,4, ["timeOfDayGuidance","timeOfDay","broadTimeOfDay"]),
        new GenericAdapter("ObservationDirectionAdapter","ObservationDirection","Observation Metadata","direction/skyDirection",85,1, ["observationDirection","direction","skyDirection","azimuth","radiant"]),
        new GenericAdapter("ObservationLocationAdapter","ObservationLocation","Observation Metadata","location/region/timezone",80,1, ["observationLocation","location","region","visibilityRegion","timezone"]),
        new GenericAdapter("AngularSeparationAdapter","AngularSeparation","Production Event Intelligence","angularSeparation/angularRelationship",80,1, ["angularSeparation","angularRelationship","separation"]),
        new GenericAdapter("VisibilityMethodAdapter","VisibilityMethod","Observation Metadata","visibilityMethod/observationMode",75,1, ["visibilityMethod","observationMode","nakedEye","binocularGuidance","telescopeGuidance","visibilityConditions"]),
        new ZhrAdapter(),
        new GenericAdapter("CulturalNameContextStructuredKnowledgeAdapter","CulturalNameContext","Astronomy Domain Knowledge Provider","culturalNameContext/mythology/traditionalSkyCulture/historicalNaming/regionalCulturalNotes",80,1,["culturalNameContext","mythology","greekMythology","hunterOrion","traditionalSkyCulture","historicalNaming","regionalCulturalNotes","originContext","tradition"]),
        new GenericAdapter("DomainKnowledgeApparentAlignmentAdapter","ApparentAlignmentExplanation","Astronomy Domain Knowledge Provider","PlanetPairingKnowledgeProfile",80,2, ["apparentAlignmentExplanation","physicalProximityClarification","perspectiveExplanation","whyPlanetsAppearClose","apparentPairingScience"])
    ];
}

public class GenericAdapter(string id, string cap, string artifact, string path, int strength, int precedence, string[] fields) : ISemanticCapabilitySourceAdapter
{
    public string AdapterId => id; public string SupportedCapabilityId => cap; public string SourceArtifact => artifact; public string SourcePath => path; public int Strength => strength; public int Precedence => precedence; public string VerificationRule => "Approved JSON path value is present and non-empty."; public string RejectionReason => "SourceValueMissing";
    public virtual bool TryExtract(SemanticCapabilitySourceContext context, out SemanticCapabilityCandidate candidate, out SemanticCapabilityRejection? rejection)
    {
        foreach (var root in Roots(context)) if (TryFind(root.Element, fields, out var field, out var value)) { candidate = new(root.Artifact, field, value, Strength >= 95 ? "Strong" : Strength >= 80 ? "Substitutable" : "Weak"); rejection = null; return true; }
        candidate = default!; rejection = new(SourceArtifact, SourcePath, RejectionReason); return false;
    }
    protected IEnumerable<(string Artifact, JsonElement? Element)> Roots(SemanticCapabilitySourceContext c)
    {
        if (SourceArtifact == "Production Request") yield return (SourceArtifact, c.ProductionRequest);
        else if (SourceArtifact == "Production Event Intelligence") yield return (SourceArtifact, c.ProductionEventIntelligence);
        else if (SourceArtifact == "Editorial Contract") yield return (SourceArtifact, c.EditorialContract);
        else if (SourceArtifact == "Observation Metadata") yield return (SourceArtifact, c.ObservationMetadata);
        else if (SourceArtifact == "Astronomy Domain Knowledge Provider") yield return (SourceArtifact, c.AstronomyDomainKnowledge);
        else if (SourceArtifact == "Documentary Contract") { yield return (SourceArtifact, c.Format == "short" ? c.ShortDocumentaryContract : c.LongDocumentaryContract); yield return (SourceArtifact, c.LongDocumentaryContract); yield return (SourceArtifact, c.ShortDocumentaryContract); }
        else yield return (SourceArtifact, null);
    }
    protected static bool TryFind(JsonElement? e, string[] names, out string field, out object value, string prefix="")
    {
        field=""; value=""; if (e is not { } el) return false;
        if (el.ValueKind == JsonValueKind.Object) foreach (var p in el.EnumerateObject()) { var path = string.IsNullOrWhiteSpace(prefix) ? p.Name : prefix+"."+p.Name; if (names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && ToValue(p.Value) is { } v) { field=path; value=v; return true; } if (TryFind(p.Value, names, out field, out value, path)) return true; }
        if (el.ValueKind == JsonValueKind.Array) foreach (var item in el.EnumerateArray()) if (TryFind(item, names, out field, out value, prefix)) return true;
        return false;
    }
    protected static object? ToValue(JsonElement e) => e.ValueKind switch { JsonValueKind.String => string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString(), JsonValueKind.Number => e.GetRawText(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(x => ToValue(x)?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))), _ => null };
}

public sealed class UtcAdapter() : GenericAdapter("UtcEventIntervalObservationTimingAdapter","ObservationTiming","Observation Metadata","utc interval + verified timezone",80,3,["startUtc","endUtc","peakUtc","peakUTC"])
{
    public override bool TryExtract(SemanticCapabilitySourceContext context, out SemanticCapabilityCandidate candidate, out SemanticCapabilityRejection? rejection)
    {
        if (!TryFind(context.ObservationMetadata, ["timezone","timeZone","verifiedTimezone"], out _, out var tz) || string.IsNullOrWhiteSpace(tz?.ToString())) { candidate=default!; rejection=new(SourceArtifact, SourcePath, "VerificationFailed"); return false; }
        return base.TryExtract(context, out candidate, out rejection);
    }
}

public sealed class ZhrAdapter() : GenericAdapter("ProductionEventIntelligenceZhrAdapter", "Zhr", "Production Event Intelligence", "zhr/zenithalHourlyRate/expectedZhr/activityRate/peakRate", 90, 1, ["zhr", "zenithalHourlyRate", "expectedZhr", "activityRate", "peakRate"])
{
    public override bool TryExtract(SemanticCapabilitySourceContext context, out SemanticCapabilityCandidate candidate, out SemanticCapabilityRejection? rejection)
    {
        if (!TryFindZhr(context.ProductionEventIntelligence, out var field, out var value))
        {
            candidate = default!;
            rejection = new(SourceArtifact, SourcePath, "SourceValueMissing");
            return false;
        }
        candidate = new(SourceArtifact, field, value, "Strong");
        rejection = null;
        return true;
    }
    private static bool TryFindZhr(JsonElement? e, out string field, out object value, string prefix = "")
    {
        field = ""; value = "";
        if (e is not { } el) return false;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                var path = string.IsNullOrWhiteSpace(prefix) ? p.Name : prefix + "." + p.Name;
                if (new[] { "zhr", "zenithalHourlyRate", "expectedZhr", "activityRate", "peakRate" }.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    if (p.Value.ValueKind == JsonValueKind.Object)
                    {
                        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(p.Value.GetRawText()) ?? [];
                        field = path; value = payload; return true;
                    }
                    if (ToValue(p.Value) is { } scalar) { field = path; value = scalar; return true; }
                }
                if (TryFindZhr(p.Value, out field, out value, path)) return true;
            }
        }
        if (el.ValueKind == JsonValueKind.Array) foreach (var item in el.EnumerateArray()) if (TryFindZhr(item, out field, out value, prefix)) return true;
        return false;
    }

}
