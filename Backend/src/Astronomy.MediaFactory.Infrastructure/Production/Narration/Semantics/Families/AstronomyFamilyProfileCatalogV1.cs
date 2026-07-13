using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed class AstronomyFamilyProfileCatalogV1 : IAstronomyFamilyProfileCatalogV1
{
    private readonly ImmutableArray<AstronomyFamilyProfileV1> _profiles;
    private readonly AstronomyFamilyAliasCatalogV1 _aliases;
    public AstronomyFamilyProfileCatalogV1() : this(CreateProfiles(), new AstronomyFamilyAliasCatalogV1()) { }
    public AstronomyFamilyProfileCatalogV1(IEnumerable<AstronomyFamilyProfileV1> profiles, AstronomyFamilyAliasCatalogV1? aliases = null) { _profiles = profiles.ToImmutableArray(); _aliases = aliases ?? new(); Profiles = _profiles; }
    public IReadOnlyCollection<AstronomyFamilyProfileV1> Profiles { get; }
    public bool TryGet(string familyId, out AstronomyFamilyProfileV1 profile) { profile = _profiles.FirstOrDefault(p => p.FamilyId.Equals(familyId, StringComparison.OrdinalIgnoreCase))!; return profile is not null; }
    public AstronomyFamilyProfileV1 GetRequired(string familyId) => TryGet(familyId, out var p) ? p : throw new KeyNotFoundException($"Family profile '{familyId}' was not registered.");
    public bool IsActiveV1Family(string familyId) => _profiles.Any(p => p.ActiveInV1 && p.FamilyId.Equals(familyId, StringComparison.OrdinalIgnoreCase));
    public bool IsFutureFamily(string familyId) => AstronomyFamilyVocabularyV1.FutureFamilyIds.Contains(familyId, StringComparer.OrdinalIgnoreCase);
    public AstronomyFamilyResolutionV1 ResolveEventType(string eventType)
    {
        var input = eventType?.Trim();
        if (string.IsNullOrEmpty(input)) return new(input, AstronomyFamilyResolutionStatusV1.Unsupported, null, null, false, false, false, "Event type is missing.");
        var alias = _aliases.TryResolve(input, out var mapped);
        var canonical = alias ? mapped : input;
        if (IsActiveV1Family(canonical)) return new(input, AstronomyFamilyResolutionStatusV1.Resolved, canonical, canonical, alias, true, false, alias ? $"Alias '{input}' resolved to '{canonical}'." : $"Family '{canonical}' resolved without alias.");
        if (IsFutureFamily(canonical)) return new(input, AstronomyFamilyResolutionStatusV1.FutureFamily, canonical, null, alias, false, true, $"Family '{canonical}' is reserved for a future V1 taxonomy stage.");
        return new(input, AstronomyFamilyResolutionStatusV1.Unsupported, null, null, alias, false, false, $"Unsupported astronomy family '{input}'.");
    }
    public FamilyProfileValidationResult Validate() => Validate(_profiles, _aliases);
    public static FamilyProfileValidationResult Validate(IEnumerable<AstronomyFamilyProfileV1> profiles, AstronomyFamilyAliasCatalogV1 aliases)
    {
        var errors = new List<string>(); var ps = profiles.ToArray(); var activeIds = AstronomyFamilyVocabularyV1.ActiveFamilyIds; var futureIds = AstronomyFamilyVocabularyV1.FutureFamilyIds; var caps = SemanticCapabilityVocabularyV1.CanonicalIds;
        Dup(ps.Select(p=>p.FamilyId), "family ID"); Dup(ps.Select(p=>p.FamilyId), "profile ID");
        foreach (var g in aliases.Aliases.GroupBy(a=>a.Alias,StringComparer.OrdinalIgnoreCase)) { if (g.Count()>1) errors.Add($"Duplicate alias '{g.Key}'."); if (g.Select(x=>x.CanonicalFamilyId).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1) errors.Add($"Ambiguous alias '{g.Key}'."); }
        foreach (var a in aliases.Aliases) { if (!activeIds.Concat(futureIds).Contains(a.CanonicalFamilyId,StringComparer.OrdinalIgnoreCase)) errors.Add($"Alias '{a.Alias}' targets missing profile '{a.CanonicalFamilyId}'."); if (aliases.Aliases.Any(x=>x.Alias.Equals(a.CanonicalFamilyId,StringComparison.OrdinalIgnoreCase))) errors.Add($"Alias cycle detected at '{a.Alias}'."); }
        foreach (var p in ps)
        {
            if (futureIds.Contains(p.FamilyId,StringComparer.OrdinalIgnoreCase) && p.ActiveInV1) errors.Add($"Future family '{p.FamilyId}' registered as active profile.");
            if (p.ActiveInV1 && (p.LongFormStructure is null || p.LongFormStructure.Beats.IsDefaultOrEmpty || p.ShortFormStructure is null || p.ShortFormStructure.Beats.IsDefaultOrEmpty)) errors.Add($"Active profile '{p.FamilyId}' is missing required long or short structure.");
            CheckBeats(p, p.LongFormStructure, "Long"); CheckBeats(p, p.ShortFormStructure, "Short");
            var reqs = Reqs(p).ToArray();
            foreach (var r in reqs)
            {
                if (!caps.Contains(r.SemanticCapabilityId.Value, StringComparer.Ordinal)) errors.Add($"Profile '{p.FamilyId}' references unknown capability '{r.SemanticCapabilityId.Value}'.");
                if (r.RequirementLevel == FamilyRequirementLevelV1.Required && r.MayOmit) errors.Add($"Profile '{p.FamilyId}' has required requirement marked omittable: {r.SemanticCapabilityId.Value}.");
                if (r.RequirementLevel == FamilyRequirementLevelV1.Required && !r.BlocksPhase7) errors.Add($"Profile '{p.FamilyId}' has required requirement not blocking: {r.SemanticCapabilityId.Value}.");
                if (r.RequirementLevel == FamilyRequirementLevelV1.Optional && r.MissingValueBehavior == FamilyMissingValueBehaviorV1.Block) errors.Add($"Profile '{p.FamilyId}' has optional requirement without explicit omission behavior: {r.SemanticCapabilityId.Value}.");
                if (r.RequirementLevel == FamilyRequirementLevelV1.FutureUnavailable && r.BlocksPhase7) errors.Add($"Profile '{p.FamilyId}' has future unavailable blocking requirement: {r.SemanticCapabilityId.Value}.");
            }
            if ((p.FamilyId is AstronomyFamilyVocabularyV1.Constellation or AstronomyFamilyVocabularyV1.DeepSkyObject) && reqs.Any(r=>r.RequirementLevel==FamilyRequirementLevelV1.Required && r.SemanticCapabilityId.Value==SemanticCapabilityVocabularyV1.EventWindow)) errors.Add($"Non-event profile '{p.FamilyId}' requires EventWindow.");
            if (p.FamilyId==AstronomyFamilyVocabularyV1.SolarEclipse && !reqs.Any(r=>r.RequirementLevel==FamilyRequirementLevelV1.Required && r.BlocksPhase7 && r.SemanticCapabilityId.Value==SemanticCapabilityVocabularyV1.SafetyGuidance)) errors.Add("SolarEclipse missing required SafetyGuidance.");
            if (p.FamilyId==AstronomyFamilyVocabularyV1.LunarEclipse && reqs.Any(r=>r.RequirementLevel==FamilyRequirementLevelV1.Required && r.SemanticCapabilityId.Value==SemanticCapabilityVocabularyV1.SafetyGuidance)) errors.Add("LunarEclipse requires SafetyGuidance.");
            if (p.FamilyId==AstronomyFamilyVocabularyV1.NamedFullMoon && reqs.Any(r=>r.RequirementLevel==FamilyRequirementLevelV1.Required && r.SemanticCapabilityId.Value==SemanticCapabilityVocabularyV1.CulturalContext)) errors.Add("NamedFullMoon requires CulturalContext.");
            if (p.FamilyId==AstronomyFamilyVocabularyV1.PlanetGrouping && (p.Policy.MinimumObjectCount ?? 0) < 3) errors.Add("PlanetGrouping minimum object count below 3.");
        }
        return new(errors.Count==0, errors, []);
        void Dup(IEnumerable<string> xs,string label){ foreach(var g in xs.GroupBy(x=>x,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1)) errors.Add($"Duplicate {label} '{g.Key}'."); }
        void CheckBeats(AstronomyFamilyProfileV1 p, FamilyNarrativeStructureV1? s, string f){ if(s is null)return; foreach(var g in s.Beats.GroupBy(b=>b.BeatId,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1)) errors.Add($"Profile '{p.FamilyId}' has duplicate beat ID '{g.Key}' in {f}."); foreach(var g in s.Beats.GroupBy(b=>b.Order).Where(g=>g.Count()>1)) errors.Add($"Profile '{p.FamilyId}' has duplicate beat order '{g.Key}' in {f}."); }
    }
    private static IEnumerable<FamilySemanticRequirementV1> Reqs(AstronomyFamilyProfileV1 p) => p.LongFormStructure.Beats.Concat(p.ShortFormStructure.Beats).SelectMany(b=>b.Requirements);
    private static FamilySemanticRequirementV1 Req(string id) => new(new SemanticCapabilityId(id), FamilyRequirementLevelV1.Required, FamilyMissingValueBehaviorV1.Block, ["Canonical", "Derived"], 80, false, true);
    private static FamilySemanticRequirementV1 Opt(string id) => new(new SemanticCapabilityId(id), FamilyRequirementLevelV1.Optional, FamilyMissingValueBehaviorV1.OmitCapability, ["Canonical", "Editorial"], 40, true, false);
    private static FamilyNarrativeStructureV1 Struct(string format, string[] roles, IEnumerable<FamilySemanticRequirementV1> reqs) => new(format, roles.Select((r,i)=> new FamilyNarrativeBeatV1($"{format}-{i+1}-{r}", r, i+1, $"{r} beat for {format} family narration.", reqs, r is "Closing" ? "Resolve the viewer promise." : null, false)).ToArray());
    private static AstronomyFamilyProfileV1 Profile(string id, string nature, string[] roles, string[] req, string[] opt, int? min=null, bool eventTiming=true) { var rs=req.Select(Req).Concat(opt.Select(Opt)).ToArray(); return new(id,"V1",nature,[id],["Long","Short"],Struct("Long",roles,rs),Struct("Short",roles,rs),new(min,eventTiming),AstronomyFamilyAliasCatalogV1.DefaultAliases.Where(a=>a.CanonicalFamilyId==id).Select(a=>a.Alias).ToArray(),true,new("Sprint2B","ApprovedV1")); }
    private static AstronomyFamilyProfileV1[] CreateProfiles() =>
    [
        Profile("PlanetPairing","Event",["Hook","Orientation","Timing","Observation","Science","Closing"],["EventIdentity","AstronomicalObjects","EventWindow","ObservationDirection","DomainScientificKnowledge"],["ObservationLocation","AngularSeparation","ObservationEquipment","ObservationConditions","EditorialContext"]),
        Profile("PlanetGrouping","Event",["Hook","Orientation","Timing","Identification","Observation","Science","Closing"],["EventIdentity","AstronomicalObjects","EventWindow","ObservationDirection","DomainScientificKnowledge"],["SecondaryAstronomicalObjects","AngularSeparation","ObservationLocation","ObservationEquipment","ObservationConditions","EditorialContext"],3),
        Profile("MeteorShower","Event",["Hook","Orientation","Timing","Observation","Science","Closing"],["EventIdentity","EventWindow","ObservationDirection","MeteorActivity","DomainScientificKnowledge"],["ObservationLocation","ObservationConditions","ObservationEquipment","EditorialContext"]),
        Profile("FullMoon","Event",["Hook","Timing","Orientation","Observation","Science","Closing"],["EventIdentity","EventWindow","FullMoonObservation","DomainScientificKnowledge"],["ObservationLocation","ObservationDirection","ObservationConditions","ObservationEquipment","EditorialContext"]),
        Profile("NamedFullMoon","Event",["Hook","Timing","Orientation","Observation","Science","Closing"],["EventIdentity","EventWindow","FullMoonObservation","DomainScientificKnowledge"],["ObservationLocation","ObservationDirection","ObservationConditions","ObservationEquipment","EditorialContext","CulturalContext"]),
        Profile("SolarEclipse","Event",["Hook","Safety","Timing","Orientation","Observation","Science","Closing"],["EventIdentity","EventWindow","EclipseCircumstances","ObservationLocation","SafetyGuidance","DomainScientificKnowledge"],["ObservationDirection","ObservationConditions","ObservationEquipment","EditorialContext"]),
        Profile("LunarEclipse","Event",["Hook","Timing","Orientation","Observation","Science","Closing"],["EventIdentity","EventWindow","EclipseCircumstances","ObservationLocation","DomainScientificKnowledge"],["ObservationDirection","ObservationConditions","ObservationEquipment","EditorialContext"]),
        Profile("Occultation","Event",["Hook","Timing","Orientation","Observation","Science","Closing"],["EventIdentity","AstronomicalObjects","EventWindow","OccultationContacts","ObservationLocation","DomainScientificKnowledge"],["ObservationDirection","ObservationEquipment","ObservationConditions","EditorialContext"]),
        Profile("Constellation","Reference",["Hook","Identification","Orientation","Science","Significance","Closing"],["ObjectKnowledge"],["EventIdentity","ObservationDirection","CulturalContext","EditorialContext"],null,false),
        Profile("DeepSkyObject","Reference",["Hook","Identification","Orientation","Observation","Science","Significance","Closing"],["ObjectKnowledge","DomainScientificKnowledge"],["EventIdentity","ObservationDirection","ObservationEquipment","ObservationConditions","EditorialContext"],null,false)
    ];
}
