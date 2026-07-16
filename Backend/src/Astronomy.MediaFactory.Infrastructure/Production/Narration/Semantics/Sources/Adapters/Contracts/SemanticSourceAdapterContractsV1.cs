using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

public interface ISemanticSourceAdapterV1
{
    string AdapterId { get; }
    SemanticCapabilityId SupportedCapabilityId { get; }
    string SourceId { get; }
    SemanticEvidenceCategoryV1 EvidenceCategory { get; }
    SemanticEvidenceStrengthV1 MaximumEvidenceStrength { get; }
    bool EventSpecific { get; }
    bool SupportsLocalization { get; }
    bool SupportsUnits { get; }
    bool SupportsProvenance { get; }
    SemanticSourceAdapterResultV1 TryExtract(SemanticSourceAdapterContextV1 context);
}

public enum SemanticSourceAdapterStatusV1 { Resolved, SourceUnavailable, ValueMissing, InvalidValue, UnverifiedValue, UnsupportedSourceShape, RejectedByPolicy }
public sealed record SemanticSourceProvenanceV1(string SourceId, string SourceModel, string SourcePropertyPath, bool Verified, string? Standard = null, string? Notes = null);
public sealed record SemanticSourceValidationIssueV1(string Code, string Message, string SourcePropertyPath, bool Blocking = true);
public sealed record SemanticSourceValueV1(object Value, string TypeName);
public sealed record SemanticSourceCandidateV1(SemanticCapabilityId CapabilityId, string AdapterId, string SourceId, SemanticSourceValueV1 TypedValue, string CanonicalValue, string? SpeakableValue, SemanticEvidenceCategoryV1 EvidenceCategory, SemanticEvidenceStrengthV1 EvidenceStrength, decimal Confidence, ImmutableArray<SemanticSourceProvenanceV1> Provenance, ImmutableArray<string> Units, ImmutableArray<string> LocalizationMetadata, ImmutableArray<string> Warnings, ImmutableArray<SemanticSourceValidationIssueV1> ValidationIssues);
public sealed record SemanticSourceRejectionV1(SemanticCapabilityId CapabilityId, string AdapterId, string SourceId, SemanticSourceAdapterStatusV1 Status, string Reason, ImmutableArray<SemanticSourceValidationIssueV1> ValidationIssues);
public sealed record SemanticSourceAdapterResultV1
{
    [JsonConstructor] public SemanticSourceAdapterResultV1(SemanticSourceAdapterStatusV1 status, SemanticSourceCandidateV1? candidate, SemanticSourceRejectionV1? rejection) { Status=status; Candidate=candidate; Rejection=rejection; }
    public SemanticSourceAdapterStatusV1 Status { get; init; }
    public SemanticSourceCandidateV1? Candidate { get; init; }
    public SemanticSourceRejectionV1? Rejection { get; init; }
    public static SemanticSourceAdapterResultV1 Resolved(SemanticSourceCandidateV1 candidate) => new(SemanticSourceAdapterStatusV1.Resolved, candidate, null);
    public static SemanticSourceAdapterResultV1 Reject(SemanticCapabilityId capabilityId, string adapterId, string sourceId, SemanticSourceAdapterStatusV1 status, string reason, IEnumerable<SemanticSourceValidationIssueV1>? issues=null) => new(status, null, new(capabilityId, adapterId, sourceId, status, reason, (issues ?? []).ToImmutableArray()));
}

public sealed record SemanticSourceAdapterContextV1(CanonicalAstronomyEventIdentity? EventIdentity = null, ProductionEventIntelligenceSourceV1? ProductionEventIntelligence = null, ObservationMetadataSourceV1? ObservationMetadata = null, DocumentaryContractSourceV1? DocumentaryContract = null, EditorialContractSourceV1? EditorialContract = null, ContentPlanSourceV1? ContentPlan = null, AstronomyObjectKnowledgeSourceV1? AstronomyObjectKnowledge = null, AstronomyDomainKnowledgeSourceV1? AstronomyDomainKnowledge = null, CulturalAstronomyKnowledgeSourceV1? CulturalAstronomyKnowledge = null, EditorialIntentSourceV1? EditorialIntent = null, DocumentaryStructureSourceV1? DocumentaryStructure = null, string? Language = null, string? TimeZone = null, ObservationLocationValue? LocationContext = null);
public sealed record CanonicalAstronomyEventIdentity(
    string CanonicalEventType,
    string FamilyId,
    string? ProfileId,
    string? SourceEventType,
    string ResolutionSource,
    string? SourceEventId = null,
    ImmutableArray<AstronomicalObjectValue> PrimaryObjects = default,
    ImmutableArray<AstronomicalObjectValue> SecondaryObjects = default,
    string? RegionId = null,
    string? Language = null);

public sealed record EventWindowValue(DateTimeOffset? StartUtc, DateTimeOffset? PeakUtc, DateTimeOffset? EndUtc, DateTimeOffset? LocalViewingStart, DateTimeOffset? LocalViewingEnd, DateTimeOffset? Moonrise, DateTimeOffset? Moonset, string? TimeZone, string? LocalizedWindowDescription);
public sealed record AstronomicalObjectValue(string Name, string? ObjectType, string? Role, string? ScientificClassification, ImmutableArray<SemanticSourceProvenanceV1> Provenance);
public sealed record AngularSeparationValue(decimal Degrees, int? Arcminutes, decimal? Arcseconds, string? Qualifier, string? RelationshipType, DateTimeOffset? MeasurementTime);
public sealed record ObservationConditionsValue(decimal? MoonIlluminationPercent, string? HorizonConstraints, string? AstronomicalTwilightContext, string? BrightnessContrastConditions, string? StableObservingPrinciples, string? CurrentWeather = null, string? CloudCover = null, string? Seeing = null, string? Transparency = null);
public sealed record ObservationEquipmentValue(bool? NakedEyeSuitable, bool? BinocularSuitable, bool? TelescopeSuitable, string? ApertureGuidance, string? FocalLengthGuidance, bool? ImagingSuitable);
public sealed record ObservationLocationValue(string? LocationName, decimal? Latitude, decimal? Longitude, decimal? ElevationMeters, string? TimeZone, bool ClaimsLocalTimeConversion = false);
public sealed record ObservationDirectionValue(string? CardinalDirection, decimal? AzimuthDegrees, decimal? AltitudeDegrees, string? HorizonProgression, string? LocalizedDescription);
public sealed record EclipseCircumstancesValue(string EclipseType, DateTimeOffset? Start, DateTimeOffset? Maximum, DateTimeOffset? End, decimal? Magnitude, decimal? Obscuration, TimeSpan? TotalityDuration, string? VisibilityRegion, string? Path, ImmutableArray<DateTimeOffset> ContactTimes, string? SolarOrLunarClassification);
public sealed record MeteorActivityValue(string? Radiant, EventWindowValue? ActivityWindow, EventWindowValue? PeakWindow, int? Zhr, decimal? VelocityKmS, string? ParentBody, string? MechanicsExplanation = null);
public sealed record CulturalContextValue(string? CulturalName, string? Tradition, string? OriginContext, decimal? Confidence, string? LocalityRegion, string? NarrativeCaution, bool Verified);
public sealed record OccultationContactsValue(DateTimeOffset? Ingress, DateTimeOffset? Maximum, DateTimeOffset? Egress, DateTimeOffset? Reappearance, TimeSpan? Duration, string? OccultingObject, string? HiddenObject);
public sealed record FullMoonObservationValue(DateTimeOffset? Moonrise, DateTimeOffset? Moonset, decimal? AltitudeDegrees, decimal? IlluminationPercent, string? ApparentSize, string? ObservationalNotes);
public sealed record ObjectKnowledgeValue(string ObjectIdentity, ImmutableArray<ObjectKnowledgeFactV1> Facts);
public sealed record ObjectKnowledgeFactV1(string Field, string Value, SemanticSourceProvenanceV1 Provenance);
public sealed record DomainScientificKnowledgeValue(string? Mechanism, string? PerspectiveAlignmentExplanation, string? ScientificSignificance, string? StableObservingPrinciples);
public sealed record EditorialContextValue(string? NarrativeEmphasis, string? ToneIntent, string? BeatPurpose, string? ClosingIntent, ImmutableArray<string> EditorialWarnings);
public sealed record SafetyGuidanceValue(string GuidanceType, string Guidance, string? Standard, bool DirectSolarViewing, bool Authoritative);

public sealed record ProductionEventIntelligenceSourceV1(string? EventType=null, string? FamilyId=null, string? ProfileId=null, ImmutableArray<AstronomicalObjectValue> PrimaryObjects=default, ImmutableArray<AstronomicalObjectValue> SecondaryObjects=default, EventWindowValue? EventWindow=null, AngularSeparationValue? AngularSeparation=null, ObservationDirectionValue? ObservationDirection=null, MeteorActivityValue? MeteorActivity=null, FullMoonObservationValue? FullMoonObservation=null, EclipseCircumstancesValue? EclipseCircumstances=null, OccultationContactsValue? OccultationContacts=null, SafetyGuidanceValue? SafetyGuidance=null, bool Verified=true);
public sealed record ObservationMetadataSourceV1(EventWindowValue? EventWindow=null, AngularSeparationValue? AngularSeparation=null, ObservationDirectionValue? ObservationDirection=null, ObservationLocationValue? ObservationLocation=null, ObservationConditionsValue? ObservationConditions=null, ObservationEquipmentValue? ObservationEquipment=null, MeteorActivityValue? MeteorActivity=null, FullMoonObservationValue? FullMoonObservation=null, EclipseCircumstancesValue? EclipseCircumstances=null, OccultationContactsValue? OccultationContacts=null, bool Verified=true);
public sealed record DocumentaryContractSourceV1(EventWindowValue? EventWindow=null, ImmutableArray<AstronomicalObjectValue> Objects=default, EclipseCircumstancesValue? EclipseCircumstances=null, bool Verified=true);
public sealed record EditorialContractSourceV1(EventWindowValue? EventWindow=null, ImmutableArray<AstronomicalObjectValue> Objects=default, bool Verified=true);
public sealed record ContentPlanSourceV1(string? EventType=null, string? FamilyId=null, string? ProfileId=null, ObservationLocationValue? ObservationLocation=null, string? Title=null, bool Verified=true);
public sealed record AstronomyObjectKnowledgeSourceV1(ImmutableArray<AstronomicalObjectValue> VerifiedObjects=default, ObjectKnowledgeValue? ObjectKnowledge=null, bool Verified=true);
public sealed record AstronomyDomainKnowledgeSourceV1(ObservationConditionsValue? ObservationPrinciples=null, ObservationEquipmentValue? EquipmentGuidance=null, MeteorActivityValue? MeteorMechanics=null, FullMoonObservationValue? FullMoonPrinciples=null, ObjectKnowledgeValue? ObjectKnowledge=null, DomainScientificKnowledgeValue? DomainKnowledge=null, SafetyGuidanceValue? SafetyGuidance=null, bool Verified=true);
public sealed record CulturalAstronomyKnowledgeSourceV1(CulturalContextValue? CulturalContext=null, bool Verified=true);
public sealed record EditorialIntentSourceV1(EditorialContextValue? EditorialContext=null);
public sealed record DocumentaryStructureSourceV1(EditorialContextValue? EditorialContext=null);
