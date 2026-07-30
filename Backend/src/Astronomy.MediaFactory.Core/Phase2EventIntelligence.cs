using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed record EventFamilyResolutionRequest(string RequestedEventType, string? Title = null, string? Category = null);
public sealed record ProductionEventFamilyResolution(string RequestedEventType, string NormalizedEventType, string EventFamily, IReadOnlyList<string> AliasesMatched, IReadOnlyList<string> ResolutionEvidence, bool IsKnownFamily);
public interface IProductionEventFamilyResolver { ProductionEventFamilyResolution Resolve(EventFamilyResolutionRequest request); }

public sealed record EventIntelligenceCapabilityResolution(string RequestedEventType, string NormalizedFamily, string CapabilityId, string CapabilityVersion, bool FallbackUsed, string? FallbackReason, bool KnownFamilyWithoutCapability, IReadOnlyList<string> ResolutionEvidence);
public interface IProductionEventIntelligenceCapabilityResolver { EventIntelligenceCapabilityResolution Resolve(ProductionEventFamilyResolution family); IProductionEventIntelligenceCapability GetCapability(EventIntelligenceCapabilityResolution resolution); }

public enum RequirementLevel { Required, Recommended, Optional, ConditionallyRequired, NotApplicable }
public sealed record FieldRequirement(string FieldPath, RequirementLevel RequirementLevel, string? Condition, string ValidationRule, string FailureCode, string Description);
public sealed record EventFamilyValidationPolicy(string PolicyId, string Version, string EventFamily, IReadOnlyList<FieldRequirement> Requirements, decimal MinimumRequiredCoverage, decimal MinimumRecommendedCoverage);
public sealed record EventIntelligenceBuildContext(ProductionPipelineRequest PipelineRequest, ProductionEventIntelligence BaseIntelligence, ProductionEventFamilyResolution Family, IMediaEventStrategy MediaStrategy, string ExecutionId, string TransactionId);
public sealed record EventFamilyIntelligenceResult(object FamilySpecificPayload, IReadOnlyList<CertifiedKnowledgeClaim> KnowledgeClaims, IReadOnlyList<ProductionIntelligenceSource> SourceReferences, IReadOnlyList<string> ProductionGuidance, IReadOnlyList<string> RequiredVisualObjects, IReadOnlyList<string> RequiredNarrationFacts, IReadOnlyList<string> SafetyRules, IReadOnlyList<string> ValidationEvidence, IReadOnlyList<string> Warnings, IReadOnlyList<string> Diagnostics);
public interface IProductionEventIntelligenceCapability
{
    string CapabilityId { get; }
    string Version { get; }
    IReadOnlyCollection<string> SupportedEventFamilies { get; }
    int Priority { get; }
    bool CanHandle(ProductionEventFamilyResolution family);
    Task<EventFamilyIntelligenceResult> BuildAsync(EventIntelligenceBuildContext context, CancellationToken cancellationToken);
    EventFamilyValidationPolicy GetValidationPolicy(EventIntelligenceBuildContext context);
}

public sealed record ConstellationIntelligencePayload(string CanonicalIdentity, IReadOnlyList<string> PrincipalStars, IReadOnlyList<string> RecognitionGeometry, string? SeasonalVisibility, string? Hemisphere, IReadOnlyList<string> DeepSkyHighlights);
public sealed record MeteorShowerIntelligencePayload(string? Radiant, string? ParentBody, DateTimeOffset? ActivityStart, DateTimeOffset? Peak, DateTimeOffset? ActivityEnd, string? BestViewingWindow, string? MoonInterference);
public sealed record PlanetaryAlignmentIntelligencePayload(IReadOnlyList<string> ParticipatingObjects, decimal? AngularSeparationDegrees, string? RelativeOrder, decimal? AltitudeDegrees, string? Direction, string? LocalVisibilityWindow);
public sealed record EclipseIntelligencePayload(string EclipseType, string? VisibilityGeography, DateTimeOffset? Peak, IReadOnlyList<string> SafetyRequirements, string? CalculationSource);
public sealed record LunarEventIntelligencePayload(string LunarEventType, DateTimeOffset? Peak, decimal? MoonIlluminationPercent, string? ViewingGuidance);
public sealed record GenericAstronomyIntelligencePayload(string EventType, IReadOnlyList<string> Objects, string? ObservationGuidance);

public sealed record CertifiedKnowledgeClaim(string KnowledgeId, string Category, string ClaimType, string? Text, object? StructuredValue, string? Unit, IReadOnlyList<string> SourceIds, string? CalculationReference, decimal Confidence, DateTimeOffset? ValidFromUtc, DateTimeOffset? ValidToUtc, string Classification, string ReviewStatus, string Family);
public sealed record KnowledgeCertificationSummary(string Status, int CertifiedClaims, int RejectedClaims, IReadOnlyList<string> Warnings);
public sealed record CertifiedKnowledgeContext(string SchemaVersion, string PlanId, string ExecutionId, string EventFamily, IReadOnlyList<CertifiedKnowledgeClaim> Claims, KnowledgeCertificationSummary Certification);
public sealed record ObservationScope(string RegionId, string? Location, string? Timezone, bool IsGlobal);
public sealed record ObservationTemporalContext(DateTimeOffset? StartUtc, DateTimeOffset? PeakUtc, DateTimeOffset? EndUtc, string? LocalWindow);
public sealed record ObservationVisibilityContext(string? Direction, decimal? AltitudeDegrees, string Status, string? MoonConditions, IReadOnlyList<string> SafetyNotes, decimal Confidence, IReadOnlyList<string> Warnings);
public sealed record ObservationCalculationReference(string ReferenceId, string Source, string? Version);
public sealed record ProductionObservationContext(ObservationScope Scope, ObservationTemporalContext Temporal, ObservationVisibilityContext Visibility, IReadOnlyList<ObservationCalculationReference> CalculationLineage, object? FamilySpecific);
public sealed record ProductionIntelligenceSource(string SourceId, string SourceType, string ProviderId, string? ProviderVersion, string SourceName, string? SourceIdentifier, DateTimeOffset GeneratedUtc, DateTimeOffset? ValidFromUtc, DateTimeOffset? ValidToUtc, string AuthorityLevel, IReadOnlyList<string> ClaimsSupported, string? Checksum, string Classification, IReadOnlyList<string> Warnings);
public sealed record ProductionIntelligenceSourceRegistry(string SchemaVersion, IReadOnlyList<ProductionIntelligenceSource> Sources);
public sealed record NormalizedScore(decimal RawValue, decimal RawMaximum, decimal NormalizedValue, decimal NormalizedMaximum, string DisplayText)
{
    public static NormalizedScore Create(decimal value, decimal maximum)
    {
        if (maximum <= 0 || value < 0 || value > maximum) throw new ArgumentOutOfRangeException(nameof(value), "Score must be inside its declared scale.");
        return new(value, maximum, value, maximum, $"{value:0.##}/{maximum:0.##}");
    }
}

public sealed record Phase2AuthorityMetadata(string SchemaVersion, string PhaseContractVersion, string AuthorityId, string PlanId, string ExecutionId, string TransactionId, DateTimeOffset GeneratedUtc, string Language, string RegionId, string StrategyId, string ValidationStatus, string CertificationStatus, string AuthoritySemanticChecksum);
public sealed record ProductionEventIdentity(string EventType, string NormalizedEventType, string EventFamily, string Title, IReadOnlyList<string> PrimaryObjects);
public sealed record Phase2ArtifactReferences(string CertifiedKnowledgeContext, string ObservationContext, string SourceRegistry, string Diagnostics, string CompatibilityProjection);
public sealed record Phase2ValidationSummary(decimal RequiredCoverage, decimal RecommendedCoverage, int NotApplicableCount, bool SemanticValidationPassed, bool CertificationPassed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record Phase2Lineage(string PlanId, string ExecutionId, string TransactionId, string SourcePhase1AuthorityChecksum, string? SourcePhase1TransactionId, string RequestIdentityChecksum);
public sealed record ProductionEventIntelligenceAuthority(Phase2AuthorityMetadata Metadata, ProductionEventIdentity EventIdentity, EventIntelligenceCapabilityResolution CapabilityResolution, ProductionEventIntelligence Intelligence, object FamilySpecificPayload, Phase2ArtifactReferences ArtifactReferences, Phase2ValidationSummary ValidationSummary, Phase2Lineage Lineage);
public sealed record Phase2ValidationRequest(ProductionEventFamilyResolution Family, EventIntelligenceCapabilityResolution Capability, EventFamilyValidationPolicy Policy, ProductionEventIntelligence Intelligence, EventFamilyIntelligenceResult Result, ProductionObservationContext Observation, ProductionIntelligenceSourceRegistry Sources);
public sealed record Phase2SemanticValidationResult(bool Passed, decimal RequiredCoverage, decimal RecommendedCoverage, int NotApplicableCount, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public interface IProductionEventIntelligenceValidator { Phase2SemanticValidationResult Validate(Phase2ValidationRequest request); }
public sealed record Phase2CertificationRequest(Phase2SemanticValidationResult Validation, CertifiedKnowledgeContext Knowledge);
public sealed record Phase2CertificationResult(bool Passed, string Status, IReadOnlyList<string> Errors);
public interface IProductionEventIntelligenceCertifier { Phase2CertificationResult Certify(Phase2CertificationRequest request); }
public sealed record Phase2ExecutionRequest(ProductionPipelineRequest PipelineRequest, string OutputRoot, bool OverwriteExisting);
public sealed record Phase2ExecutionOutcome(string ReasonCode, bool Reused, bool Recovered, bool DownstreamInvalidated, ProductionEventIntelligenceAuthority Authority, IReadOnlyList<string> OutputFiles, IReadOnlyList<string> Warnings);
public interface IProductionEventIntelligencePhaseService { Task<Phase2ExecutionOutcome> ExecuteAsync(Phase2ExecutionRequest request, CancellationToken cancellationToken); }
