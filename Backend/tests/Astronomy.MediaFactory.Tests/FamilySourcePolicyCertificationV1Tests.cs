using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
namespace Astronomy.MediaFactory.Tests;
public sealed class FamilySourcePolicyCertificationV1Tests
{
    private readonly AstronomyFamilyProfileCatalogV1 _families=new(); private readonly SemanticSourcePolicyCatalogV1 _policies=new();
    [Fact] public void Every_Required_V1_Family_Capability_Has_Approved_NonCompatibility_Source(){foreach(var f in _families.Profiles.Where(p=>p.ActiveInV1)) foreach(var r in f.LongFormStructure.Beats.Concat(f.ShortFormStructure.Beats).SelectMany(b=>b.Requirements).Where(r=>r.RequirementLevel==FamilyRequirementLevelV1.Required).GroupBy(r=>r.SemanticCapabilityId.Value).Select(g=>g.First())) Assert.Contains(_policies.GetRequired(r.SemanticCapabilityId).ApprovedSources,s=>!s.CompatibilityOnly&&s.ActiveInV1);}
    [Fact] public void SolarEclipse_Required_SafetyGuidance_Policy_Certifies(){var report=_policies.CertifyFamilyProfile(_families.GetRequired(AstronomyFamilyVocabularyV1.SolarEclipse)); Assert.Contains(report.Entries,e=>e.CapabilityId==SemanticCapabilityVocabularyV1.SafetyGuidance&&e.Required&&e.Status==SemanticSourceCertificationStatusV1.Certified); Assert.Empty(report.Blockers);}
    [Fact] public void LunarEclipse_Does_Not_Require_SafetyGuidance(){var f=_families.GetRequired(AstronomyFamilyVocabularyV1.LunarEclipse); Assert.DoesNotContain(f.LongFormStructure.Beats.Concat(f.ShortFormStructure.Beats).SelectMany(b=>b.Requirements),r=>r.RequirementLevel==FamilyRequirementLevelV1.Required&&r.SemanticCapabilityId.Value==SemanticCapabilityVocabularyV1.SafetyGuidance);}
    [Fact] public void Every_Active_Family_Receives_Deterministic_Policy_Certification_Result(){var a=_families.Profiles.Where(p=>p.ActiveInV1).Select(p=>_policies.CertifyFamilyProfile(p)).ToArray(); var b=_families.Profiles.Where(p=>p.ActiveInV1).Select(p=>_policies.CertifyFamilyProfile(p)).ToArray(); Assert.Equal(a,b); Assert.All(a,r=>Assert.True(r.Status is SemanticSourceCertificationStatusV1.Certified or SemanticSourceCertificationStatusV1.CertifiedWithOptionalGaps));}
}
