using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticSourcePolicyCatalogV1Tests
{
    private readonly SemanticSourcePolicyCatalogV1 _catalog = new();
    private SemanticSourcePolicyV1 P(string id)=>_catalog.GetRequired(new SemanticCapabilityId(id));

    [Fact] public void Exactly_18_Active_V1_Source_Policies_Exist()=>Assert.Equal(18,_catalog.Policies.Count(p=>p.ActiveInV1));
    [Fact] public void Every_Canonical_Capability_Has_Exactly_One_Policy(){foreach(var id in SemanticCapabilityVocabularyV1.CanonicalIds) Assert.Single(_catalog.Policies,p=>p.SemanticCapabilityId.Value==id);}
    [Fact] public void Every_Policy_References_Known_Canonical_Capability(){foreach(var p in _catalog.Policies) Assert.Contains(p.SemanticCapabilityId.Value,SemanticCapabilityVocabularyV1.CanonicalIds);}
    [Fact] public void Catalog_Validation_Succeeds()=>Assert.True(_catalog.Validate().IsValid,string.Join("; ",_catalog.Validate().Errors));
    [Fact] public void Optional_Capabilities_Have_Explicit_Omission_Behavior(){foreach(var p in _catalog.Policies) Assert.Equal(SemanticSourceMissingPolicyV1.OmitCapability,p.MissingOptionalBehavior);}
    [Fact] public void EventIdentity_Forbids_Raw_Json()=>Assert.False(P(SemanticCapabilityVocabularyV1.EventIdentity).RawJsonCompatibilityAllowed);
    [Fact] public void EventWindow_Forbids_Domain_Fallback_For_Timing(){var p=P(SemanticCapabilityVocabularyV1.EventWindow); Assert.False(p.DomainKnowledgeAllowed); Assert.DoesNotContain(SemanticEvidenceCategoryV1.DomainScientificKnowledge,p.AllowedEvidenceCategories);}
    [Fact] public void AstronomicalObjects_Forbids_Title_Inference()=>Assert.Contains("NoTitleInference",P(SemanticCapabilityVocabularyV1.AstronomicalObjects).ApprovedDerivationRuleIds);
    [Fact] public void AngularSeparation_Has_Verified_Event_Data_Sources_Only()=>Assert.All(P(SemanticCapabilityVocabularyV1.AngularSeparation).ApprovedSources,s=>Assert.Equal(SemanticEvidenceCategoryV1.VerifiedEventData,s.EvidenceCategory));
    [Fact] public void Direction_And_Location_Have_Verified_Event_Data_Sources(){Assert.All(P(SemanticCapabilityVocabularyV1.ObservationDirection).ApprovedSources,s=>Assert.Equal(SemanticEvidenceCategoryV1.VerifiedEventData,s.EvidenceCategory)); Assert.All(P(SemanticCapabilityVocabularyV1.ObservationLocation).ApprovedSources,s=>Assert.Equal(SemanticEvidenceCategoryV1.VerifiedEventData,s.EvidenceCategory));}
    [Fact] public void ObservationConditions_Permits_Domain_Explanation_Not_Weather_Fabrication(){var p=P(SemanticCapabilityVocabularyV1.ObservationConditions); Assert.True(p.DomainKnowledgeAllowed); Assert.Contains("NoCurrentWeatherFabrication",p.ApprovedDerivationRuleIds);}
    [Fact] public void ObservationEquipment_Permits_Domain_Knowledge()=>Assert.True(P(SemanticCapabilityVocabularyV1.ObservationEquipment).DomainKnowledgeAllowed);
    [Fact] public void MeteorActivity_Permits_Domain_Explanation_Not_Zhr_Fabrication(){var p=P(SemanticCapabilityVocabularyV1.MeteorActivity); Assert.True(p.DomainKnowledgeAllowed); Assert.Contains("NoZhrFabrication",p.ApprovedDerivationRuleIds);}
    [Fact] public void FullMoonObservation_Excludes_CulturalContext()=>Assert.DoesNotContain(SemanticEvidenceCategoryV1.CulturalContext,P(SemanticCapabilityVocabularyV1.FullMoonObservation).AllowedEvidenceCategories);
    [Fact] public void EclipseCircumstances_Forbids_Domain_Fallback()=>Assert.False(P(SemanticCapabilityVocabularyV1.EclipseCircumstances).DomainKnowledgeAllowed);
    [Fact] public void OccultationContacts_Forbids_Domain_Fallback()=>Assert.False(P(SemanticCapabilityVocabularyV1.OccultationContacts).DomainKnowledgeAllowed);
    [Fact] public void ObjectKnowledge_Supports_Verified_Object_And_Domain(){var p=P(SemanticCapabilityVocabularyV1.ObjectKnowledge); Assert.Contains(SemanticEvidenceCategoryV1.VerifiedObjectData,p.AllowedEvidenceCategories); Assert.Contains(SemanticEvidenceCategoryV1.DomainScientificKnowledge,p.AllowedEvidenceCategories);}
    [Fact] public void DomainScientificKnowledge_Cannot_Supply_Event_Measurements(){var p=P(SemanticCapabilityVocabularyV1.DomainScientificKnowledge); Assert.False(p.EventSpecificVerificationRequired); Assert.Contains("NoEventMeasurements",p.ApprovedDerivationRuleIds);}
    [Fact] public void CulturalContext_Is_Optional_And_Uses_Only_Cultural_Evidence(){var p=P(SemanticCapabilityVocabularyV1.CulturalContext); Assert.Equal(SemanticSourceMissingPolicyV1.OmitCapability,p.MissingOptionalBehavior); Assert.All(p.AllowedEvidenceCategories,c=>Assert.Equal(SemanticEvidenceCategoryV1.CulturalContext,c));}
    [Fact] public void EditorialContext_Cannot_Satisfy_Scientific_Requirements(){Assert.All(P(SemanticCapabilityVocabularyV1.EditorialContext).AllowedEvidenceCategories,c=>Assert.Equal(SemanticEvidenceCategoryV1.EditorialContext,c)); Assert.True(_catalog.Validate().IsValid);}
    [Fact] public void SafetyGuidance_Has_Authoritative_Or_Strong_Sources(){var p=P(SemanticCapabilityVocabularyV1.SafetyGuidance); Assert.All(p.ApprovedSources,s=>Assert.True(s.MinimumStrength>=SemanticEvidenceStrengthV1.Strong));}
    [Fact] public void Source_Approval_Evaluates_Descriptors(){var r=_catalog.EvaluateSource(new(SemanticCapabilityVocabularyV1.AngularSeparation),new(SemanticSourcePolicyVocabularyV1.ObservationMetadata,SemanticEvidenceCategoryV1.VerifiedEventData,SemanticEvidenceStrengthV1.Strong,true,true,true,false,true,true,false)); Assert.True(r.Approved);}
}
