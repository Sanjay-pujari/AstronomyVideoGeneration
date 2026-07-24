using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
public static class SemanticSourcePolicyValidatorV1
{
    public static SemanticSourcePolicyValidationResult Validate(IEnumerable<SemanticSourcePolicyV1> policies)
    {
        var errors=new List<string>(); var ps=policies.ToArray(); var canon=SemanticCapabilityVocabularyV1.CanonicalIds;
        foreach(var g in ps.GroupBy(p=>p.SemanticCapabilityId.Value,StringComparer.Ordinal).Where(g=>g.Count()>1)) errors.Add($"Duplicate capability policy '{g.Key}'.");
        foreach(var p in ps){ if(!canon.Contains(p.SemanticCapabilityId.Value,StringComparer.Ordinal)) errors.Add($"Unknown canonical capability '{p.SemanticCapabilityId.Value}'."); if(p.ApprovedSources.IsDefault) errors.Add($"Mutable/default source collection for '{p.SemanticCapabilityId.Value}'."); if(p.AllowedEvidenceCategories.IsDefault) errors.Add($"Mutable/default evidence collection for '{p.SemanticCapabilityId.Value}'."); }
        foreach(var id in canon) if(!ps.Any(p=>p.SemanticCapabilityId.Value==id)) errors.Add($"Missing policy for canonical capability '{id}'.");
        foreach(var p in ps)
        {
            var id=p.SemanticCapabilityId.Value;
            if(p.ApprovedSources.Length==0) errors.Add($"Empty source list for '{id}'.");
            foreach(var g in p.ApprovedSources.GroupBy(s=>s.SourceId,StringComparer.Ordinal).Where(g=>g.Count()>1)) errors.Add($"Duplicate source ID '{g.Key}' in '{id}'.");
            foreach(var g in p.ApprovedSources.GroupBy(s=>s.Priority).Where(g=>g.Count()>1)) errors.Add($"Duplicate source priority '{g.Key}' in '{id}'.");
            foreach(var s in p.ApprovedSources){ if(!p.AllowedEvidenceCategories.Contains(s.EvidenceCategory)) errors.Add($"Source category not allowed in '{id}': {s.SourceId}."); if(s.MinimumStrength==SemanticEvidenceStrengthV1.None) errors.Add($"Invalid minimum evidence strength in '{id}'."); }
            if(p.MinimumEvidenceStrength==SemanticEvidenceStrengthV1.None) errors.Add($"Invalid minimum evidence strength in '{id}'.");
            if(p.EventSpecificVerificationRequired && !p.ApprovedSources.Any(s=>s.EventSpecific && !s.CompatibilityOnly)) errors.Add($"Event-specific capability with only non-event sources: '{id}'.");
            if(p.MissingRequiredBehavior==SemanticSourceMissingPolicyV1.Block && !p.ApprovedSources.Any(s=>!s.CompatibilityOnly)) errors.Add($"Required capability with no approved source: '{id}'.");
            if(p.MissingRequiredBehavior==SemanticSourceMissingPolicyV1.Block && p.ApprovedSources.Any(s=>s.CompatibilityOnly)) errors.Add($"Compatibility-only source satisfying required certification in '{id}'.");
            if(p.MissingRequiredBehavior==SemanticSourceMissingPolicyV1.Block && p.RawJsonCompatibilityAllowed) errors.Add($"Raw JSON allowed for certified required capability '{id}'.");
            if(IsEventMeasurement(id) && p.DomainKnowledgeAllowed && p.AllowedEvidenceCategories.All(c=>c==SemanticEvidenceCategoryV1.VerifiedEventData)) errors.Add($"Domain fallback allowed for event-specific measurement '{id}'.");
            if(IsScientific(id) && p.ApprovedSources.Any(s=>s.EvidenceCategory==SemanticEvidenceCategoryV1.CulturalContext)) errors.Add($"Cultural source approved for scientific capability '{id}'.");
            if(IsScientific(id) && p.ApprovedSources.Any(s=>s.EvidenceCategory==SemanticEvidenceCategoryV1.EditorialContext)) errors.Add($"Editorial source approved for scientific capability '{id}'.");
            foreach(var r in p.ApprovedDerivationRuleIds) if(!SemanticSourcePolicyVocabularyV1.ApprovedDerivationRuleIds.Contains(r,StringComparer.Ordinal)) errors.Add($"Unknown derivation rule '{r}' in '{id}'.");
        }
        return new(errors.Count==0, errors.ToArray(), Array.Empty<string>());
    }
    private static bool IsEventMeasurement(string id)=>id is "EventWindow" or "AngularSeparation" or "ObservationDirection" or "ObservationLocation" or "EclipseCircumstances" or "OccultationContacts";
    private static bool IsScientific(string id)=>id is not "CulturalContext" and not "CulturalNameContext" and not "EditorialContext";
}
