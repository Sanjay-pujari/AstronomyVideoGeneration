using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Certification;
public sealed class SemanticSourcePolicyCertifierV1(ISemanticSourcePolicyCatalogV1 sourcePolicies, ISemanticCapabilityCatalogV1 capabilities) : ISemanticSourcePolicyCertifierV1
{
    public SemanticSourcePolicyCertificationReportV1 Certify(AstronomyFamilyProfileV1 profile)
    {
        var entries=new List<SemanticSourcePolicyCertificationEntryV1>(); var gaps=new List<string>(); var blockers=new List<string>();
        var reqs=profile.LongFormStructure.Beats.Concat(profile.ShortFormStructure.Beats).SelectMany(b=>b.Requirements).GroupBy(r=>r.SemanticCapabilityId.Value).Select(g=>g.First()).OrderBy(r=>r.SemanticCapabilityId.Value,StringComparer.Ordinal).ToArray();
        foreach(var r in reqs)
        {
            var required=r.RequirementLevel==FamilyRequirementLevelV1.Required; var id=r.SemanticCapabilityId.Value;
            SemanticSourceCertificationStatusV1 status; string msg;
            if(!capabilities.TryGet(r.SemanticCapabilityId,out _)){ status=SemanticSourceCertificationStatusV1.MissingCapabilityPolicy; msg="Capability is not canonical."; blockers.Add($"{id}: {msg}"); }
            else if(!sourcePolicies.TryGet(r.SemanticCapabilityId,out var p)){ status=SemanticSourceCertificationStatusV1.MissingCapabilityPolicy; msg="Source policy missing."; if(required) blockers.Add($"{id}: {msg}"); else gaps.Add($"{id}: {msg}"); }
            else if(required && !p.ApprovedSources.Any(s=>!s.CompatibilityOnly && s.ActiveInV1)){ status=SemanticSourceCertificationStatusV1.MissingApprovedSource; msg="Required capability has no approved non-compatibility source."; blockers.Add($"{id}: {msg}"); }
            else if(required && p.RawJsonCompatibilityAllowed){ status=SemanticSourceCertificationStatusV1.CompatibilityOnly; msg="Required capability allows raw JSON compatibility."; blockers.Add($"{id}: {msg}"); }
            else if(!required && p.MissingOptionalBehavior!=SemanticSourceMissingPolicyV1.OmitCapability && r.RequirementLevel!=FamilyRequirementLevelV1.FutureUnavailable){ status=SemanticSourceCertificationStatusV1.InvalidPolicy; msg="Optional capability lacks explicit omission behavior."; gaps.Add($"{id}: {msg}"); }
            else { status=required?SemanticSourceCertificationStatusV1.Certified:SemanticSourceCertificationStatusV1.CertifiedWithOptionalGaps; msg=required?"Required policy is policy-certifiable; adapter availability is not claimed.":"Optional policy has explicit omission behavior."; }
            entries.Add(new(profile.FamilyId,id,required,status,msg));
        }
        if(profile.FamilyId==AstronomyFamilyVocabularyV1.SolarEclipse && !entries.Any(e=>e.CapabilityId==SemanticCapabilityVocabularyV1.SafetyGuidance && e.Required && e.Status==SemanticSourceCertificationStatusV1.Certified)) blockers.Add("SolarEclipse SafetyGuidance is not certified.");
        if(profile.FamilyId==AstronomyFamilyVocabularyV1.LunarEclipse && entries.Any(e=>e.CapabilityId==SemanticCapabilityVocabularyV1.SafetyGuidance && e.Required)) blockers.Add("LunarEclipse must not require SafetyGuidance.");
        var final=blockers.Count>0?SemanticSourceCertificationStatusV1.InvalidPolicy:gaps.Count>0?SemanticSourceCertificationStatusV1.CertifiedWithOptionalGaps:SemanticSourceCertificationStatusV1.Certified;
        return new(profile.FamilyId,final,entries,gaps,blockers);
    }
}
