using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class Phase7FoundationContract { public const string Version = "rc2-phase7-foundation.v1"; }
public enum KnowledgeDomainStatus { Available, Missing, NotApplicable, Deferred, RequiresHumanReview }
public enum NarrationKnowledgeDomainKey { Identity, Appearance, Recognition, RecognitionGeometry, ScientificStructure, PhysicalCharacteristics, KeyObjects, DeepSkyObjects, Orbit, Rotation, Atmosphere, Surface, Moons, Rings, Exploration, Lifecycle, Variability, Multiplicity, Distance, Scale, Formation, Evolution, StarFormation, History, CultureAndMythology, RegionalTraditions, AstrologyClarification, Observation, Timing, Visibility, LocationDependence, WeatherDependence, MoonInterference, Safety, Equipment, Astrophotography, ImagingAppearance, ScientificSignificance, Geometry, ContactTimeline, VisibilityFootprint, ParentBody, Radiant, ActivityRate, Uncertainty, OrbitalMotion, ArtificialNaturalDistinction, LocalizedContent, EditorialSafety, InterestingFacts }
public static class NarrationKnowledgeDomains
{
    public static string Id(NarrationKnowledgeDomainKey key) => key.ToString();
    public static bool TryParse(string value, out NarrationKnowledgeDomainKey key) => Enum.TryParse(value.Replace("-", "").Replace("_", "").Replace(" ", ""), true, out key);
}

public sealed record PublishedStoryFrameAuthority(
    StoryFramesAuthority Authority, StoryFrameIndex Index, StoryFrameDiagnostics Diagnostics,
    string SourcePhase4AggregateId, string SourcePhase4Checksum, string SourceLongChecksum, string SourceShortChecksum,
    string SourcePhase5PublicationId, IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> ManifestEvidence, IReadOnlyList<string> ValidationEvidence,
    string ContractVersion, IReadOnlyDictionary<string, string> RuntimeCompatibilityEvidence);
public sealed record Phase6CommittedAuthorityRequest(string ExecutionRoot, string ExecutionId, string PlanId, string EventId, string Language);
public sealed record Phase6CommittedAuthorityEvaluation(bool IsValid, PublishedStoryFrameAuthority? Authority,
    string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public interface IPhase6CommittedAuthorityEvaluator
{
    Task<Phase6CommittedAuthorityEvaluation> EvaluateAsync(Phase6CommittedAuthorityRequest request, CancellationToken cancellationToken = default);
}

public sealed record DurationRange(int MinimumSeconds, int PreferredSeconds, int MaximumSeconds);
public sealed record LongNarrationProfile(int MinimumScenes, int PreferredScenes, int MaximumScenes,
    DurationRange Duration, IReadOnlyList<string> MandatorySectionKeys, IReadOnlyList<string> OptionalSectionKeys,
    IReadOnlyList<string> PreferredNarrativeOrder, string RequiredOpeningBehavior, string RequiredClosingBehavior);
public sealed record ShortNarrationProfile(int PreferredSceneCount, DurationRange Duration,
    IReadOnlyList<string> BeatKeys, string HookRule, string CentralDiscoveryRule, string ViewingActionRule, string ClosingRule);
public sealed record FamilyNarrationProfile(string ProfileId, string ContractVersion, string EventFamily,
    IReadOnlyList<string> SupportedLanguages, LongNarrationProfile LongProfile, ShortNarrationProfile ShortProfile,
    IReadOnlyList<string> MandatoryKnowledgeDomains, IReadOnlyList<string> OptionalKnowledgeDomains,
    IReadOnlyList<string> SafetyRules, IReadOnlyList<string> LocalizationRules, IReadOnlyList<string> TerminologyRules,
    IReadOnlyList<string> ObservationRules, IReadOnlyList<string> CulturalRules, IReadOnlyDictionary<string, DurationRange> DurationRules,
    string AllowedEditorialConnectivePolicy, string DeterministicChecksum);
public sealed record FamilyNarrationProfileResolution(bool IsValid, FamilyNarrationProfile? Profile, string ReasonCode, IReadOnlyList<string> Errors);
public interface IFamilyNarrationProfileResolver
{
    FamilyNarrationProfileResolution Resolve(string eventFamily, string language);
    IReadOnlyList<FamilyNarrationProfile> Profiles { get; }
}

public sealed record CertifiedNarrationClaim(string ClaimId, string Domain, string Text,
    IReadOnlyList<string> SourceIds, IReadOnlyList<string> KnowledgeReferenceIds, decimal Confidence,
    bool IsApproximate, bool IsLocationDependent, bool IsDateTimeDependent, bool IsCultural,
    bool IsMythological, bool IsAstrologyRelated, bool RequiresQualification, bool RequiresHumanReview,
    string Language, string Checksum)
{
    public string SemanticIdentity { get; init; } = ClaimId;
    public string ProvenancePrecision { get; init; } = "Exact";
    public string SelectionReason { get; init; } = "CertifiedKnowledge";
    public bool WeatherDependent { get; init; }
    public bool MoonDependent { get; init; }
    public bool Uncertain { get; init; }
}
public sealed record CertifiedNarrationSource(string SourceId, string SourceType, string Title,
    string PublisherOrAuthority, string UrlOrReference, bool Reviewed, bool Certified,
    IReadOnlyList<string> SupportedKnowledgeIds, IReadOnlyList<string> SupportedClaimIds,
    IReadOnlyList<string> SupportedDomains, string Language, decimal Confidence, string Checksum)
{
    public IReadOnlyList<string> SupportedApprovedFieldPaths { get; init; } = [];
    public string Disposition { get; init; } = "CertifiedSupporting";
    public IReadOnlyList<string> RegistryDiagnostics { get; init; } = [];
    public string ReviewState { get; init; } = "";
    public string AuthorityState { get; init; } = "";
}
public enum Phase7SourceEligibility { EligibleForRequiredClaim, EligibleForOptionalClaim, AuditOnly, Rejected }
public sealed record Phase7SourceEligibilityRequest(CertifiedNarrationSource Source, string Language,
    string KnowledgeId, string SemanticIdentity, string ApprovedFieldPath, bool Required,
    bool OptionalReviewedEvidenceAllowed, bool RequiresHumanReview);
public sealed record Phase7SourceEligibilityResult(Phase7SourceEligibility Eligibility, string ReasonCode,
    bool Authoritative, Phase7ProvenancePrecision Precision);
public interface IPhase7SourceEligibilityPolicy
{
    Phase7SourceEligibilityResult Classify(Phase7SourceEligibilityRequest request);
}
public sealed record NarrationKnowledgeDomain(string Domain, KnowledgeDomainStatus Status,
    IReadOnlyList<CertifiedNarrationClaim> Claims, IReadOnlyList<string> Warnings);
public sealed record ResolvedNarrationKnowledge(string PayloadId, string PayloadChecksum, string SourceRegistryId,
    string SourceRegistryChecksum, string Language, IReadOnlyList<NarrationKnowledgeDomain> Domains,
    IReadOnlyDictionary<string, string> LocalizedVocabulary, IReadOnlyList<string> ProtectedTerms,
    IReadOnlyDictionary<string, string> PronunciationHints, IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingIssues, string DeterministicChecksum)
{
    public IReadOnlyList<Phase7KnowledgeAdapterDiagnostic> AdapterDiagnostics { get; init; } = [];
    public IReadOnlyList<Phase7KnowledgeMergeDecision> MergeDecisions { get; init; } = [];
    public Phase7SourceAuditSummary SourceAuditSummary { get; init; } = new(0,0,0,0);
    public IReadOnlyList<string> UnknownSections { get; init; } = [];
    public IReadOnlyList<string> UnknownProperties { get; init; } = [];
    public IReadOnlyList<Phase7ClaimSupportEvidence> ClaimSupportEvidence { get; init; } = [];
    public IReadOnlyList<Phase7KnowledgeEntity> KnowledgeEntities { get; init; } = [];
}
public sealed record CertifiedKnowledgePayload(string PayloadId, string EventId, string EventFamily, string EventType,
    string Language, string RawDataJson, string? MetadataJson, string? EvergreenJson,
    string SourceRegistryId, IReadOnlyList<string> ReviewedSourceIds, string VerificationStatus)
{
    public string CertifiedEventFamily { get; init; } = EventFamily;
    public string? EvergreenRelativePath { get; init; }
    public string? EvergreenPayloadId { get; init; }
    public string? EvergreenChecksum { get; init; }
    public IReadOnlyList<CertifiedNarrationSource> ReviewedSources { get; init; } = [];
    public IReadOnlyList<CertifiedNarrationSource> AllResolvedSources { get; init; } = [];
    public IReadOnlyList<CertifiedNarrationSource> CertifiedSupportingSources { get; init; } = [];
    public IReadOnlyList<CertifiedNarrationSource> RejectedSources { get; init; } = [];
    public IReadOnlyList<CertifiedNarrationSource> UnverifiedSources { get; init; } = [];
    public string CertificationStatus { get; init; } = VerificationStatus;
    public string PayloadChecksum { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
public interface IPhase7CertifiedKnowledgeSource
{
    Task<CertifiedKnowledgePayload?> ResolveAsync(string eventId, string language, CancellationToken cancellationToken = default);
    async Task<Phase7CertifiedKnowledgeSourceResult> ResolveResultAsync(string eventId, string language, CancellationToken cancellationToken = default)
    {
        var payload = await ResolveAsync(eventId, language, cancellationToken);
        return payload is null
            ? new(false, null, "P7KNOWLEDGE_EVENT_MISSING", ["Certified event intelligence was not found."], [])
            : new(true, payload, "P7KNOWLEDGE_VALID", [], payload.Warnings);
    }
}
public sealed record Phase7CertifiedKnowledgeSourceResult(bool IsValid, CertifiedKnowledgePayload? Payload,
    string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public interface IPhase7KnowledgeResolver
{
    ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile);
}

public enum Phase7KnowledgeOrigin { Event, Evergreen }
public enum Phase7ProvenancePrecision { ExactClaim, ExactKnowledgeEntity, ExactApprovedField, CoarseDomain, None }
public enum Phase7KnowledgeMergeClassification { Equivalent, EventSpecificSpecialization, EventMorePrecise, EvergreenMorePrecise, Contradictory, Incomparable }
public sealed record Phase7KnowledgeAuthorityScope(
    string? ScopeType = null, string? Location = null, decimal? Latitude = null, decimal? Longitude = null,
    DateTimeOffset? StartUtc = null, DateTimeOffset? EndUtc = null, DateOnly? ReferenceDate = null,
    string? EventInstanceId = null, string? ObservationWindowId = null)
{
    public bool HasExplicitEvidence => !string.IsNullOrWhiteSpace(ScopeType) || !string.IsNullOrWhiteSpace(Location)
        || Latitude.HasValue || Longitude.HasValue || StartUtc.HasValue || EndUtc.HasValue || ReferenceDate.HasValue
        || !string.IsNullOrWhiteSpace(EventInstanceId) || !string.IsNullOrWhiteSpace(ObservationWindowId);
}
public sealed record Phase7KnowledgeComparisonMetadata(string? NormalizedValue = null, string? ValueType = null,
    string? Unit = null, bool? Approximation = null, decimal? Uncertainty = null, decimal? Confidence = null);
public enum Phase7KnowledgeScopeComparison { SameScope, EventIsSpecialization, DistinctNonConflictingScopes, InsufficientScopeEvidence, ConflictingScope }
public interface IPhase7KnowledgeScopeComparer
{
    Phase7KnowledgeScopeComparison Compare(Phase7KnowledgeAuthorityScope evergreen, Phase7KnowledgeAuthorityScope @event);
}
public sealed record Phase7KnowledgeMergeRequest(string SemanticIdentity, NarrationKnowledgeDomainKey Domain,
    string ApprovedFieldPath, Phase7AdapterClaimCandidate EvergreenCandidate, Phase7AdapterClaimCandidate EventCandidate,
    Phase7KnowledgeAuthorityScope EvergreenScope, Phase7KnowledgeAuthorityScope EventScope,
    Phase7KnowledgeComparisonMetadata EvergreenComparisonMetadata, Phase7KnowledgeComparisonMetadata EventComparisonMetadata,
    IReadOnlyDictionary<string,string> EventExecutionContext);
public sealed record Phase7KnowledgeMergeResult(Phase7KnowledgeMergeClassification Classification, string Reason,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingIssues);
public interface IPhase7KnowledgeMergeClassifier { Phase7KnowledgeMergeResult Classify(Phase7KnowledgeMergeRequest request); }
public sealed record Phase7KnowledgeMergeDecision(string SemanticIdentity, Phase7KnowledgeMergeClassification Classification,
    Phase7AdapterClaimCandidate EvergreenClaimCandidate, Phase7AdapterClaimCandidate EventClaimCandidate,
    IReadOnlyList<string> SelectedClaimIds, string Reason, Phase7KnowledgeAuthorityScope EvergreenScope,
    Phase7KnowledgeAuthorityScope EventScope, IReadOnlyDictionary<string,string> ComparisonEvidence,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingIssues);
public sealed record Phase7SourceAuditSummary(int AllResolvedSourceCount, int RejectedSourceCount,
    int UncertifiedSourceCount, int UnsupportedSourceCount);
public sealed record Phase7KnowledgeSectionContext(Phase7KnowledgeOrigin Origin, string PayloadId,
    string PayloadVersion, string PayloadChecksum, string Language, string SectionName, JsonElement SectionJson,
    IReadOnlyList<CertifiedNarrationSource> SourceRegistry, string EventFamily, string EventType);
public sealed record Phase7KnowledgeEntity(string KnowledgeId, string EntityType, string DisplayName,
    IReadOnlyList<string> SourceIds, string Checksum);
public sealed record Phase7AdapterClaimCandidate(string KnowledgeId, string ApprovedFieldPath,
    NarrationKnowledgeDomainKey Domain, string Text, IReadOnlyList<string> SourceIds,
    bool RequiresQualification, bool RequiresHumanReview, string SemanticIdentity)
{
    public Phase7KnowledgeOrigin Origin { get; init; }
    public string AdapterId { get; init; } = "";
    public string AdapterVersion { get; init; } = "";
    public string IdentityPrecision { get; init; } = "StableKnowledgeId";
    public string? NormalizedValue { get; init; }
    public string? ValueType { get; init; }
    public string? Unit { get; init; }
    public string? ScopeType { get; init; }
    public string? Location { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public DateTimeOffset? StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public DateOnly? ReferenceDate { get; init; }
    public string? EventInstanceId { get; init; }
    public string? ObservationWindowId { get; init; }
    public bool? Approximate { get; init; }
    public decimal? Uncertainty { get; init; }
    public decimal? Confidence { get; init; }
}
public sealed record Phase7KnowledgeEntityIdentity(string KnowledgeId, string IdentityPrecision, bool RequiresHumanReview);
public interface IPhase7KnowledgeEntityIdentityResolver
{
    Phase7KnowledgeEntityIdentity Resolve(JsonElement item, string fallbackKnowledgeId,
        IReadOnlyList<CertifiedNarrationSource> certifiedObjectRegistry, bool required);
}
public sealed record Phase7ClaimSupportEvidence(string ClaimId, string SemanticIdentity, string SourceId,
    string KnowledgeId, string ApprovedFieldPath, Phase7ProvenancePrecision ProvenancePrecision,
    string AdapterId, Phase7KnowledgeOrigin Origin, string SelectionReason, string? MergeDecisionId,
    decimal Confidence)
{
    public string AdapterVersion { get; init; } = "";
    public Phase7SourceEligibility SourceEligibility { get; init; } = Phase7SourceEligibility.AuditOnly;
    public bool RequiresHumanReview { get; init; }
    public string QualificationReason { get; init; } = "";
    public string AuthorityScope { get; init; } = "";
}
public sealed record Phase7KnowledgeSectionAdapterResult(IReadOnlyList<Phase7AdapterClaimCandidate> Claims,
    IReadOnlyList<Phase7KnowledgeEntity> KnowledgeEntities, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> UnknownProperties, string AdapterChecksum);
public interface IPhase7KnowledgeSectionAdapter
{
    string AdapterId { get; }
    string AdapterVersion { get; }
    IReadOnlySet<string> SupportedSectionNames { get; }
    IReadOnlySet<NarrationKnowledgeDomainKey> ProducedDomains { get; }
    IReadOnlySet<string> ApprovedFieldPaths => new HashSet<string>();
    Phase7KnowledgeSectionAdapterResult Extract(Phase7KnowledgeSectionContext context);
}
public sealed record Phase7KnowledgeAdapterDiagnostic(string AdapterId, string AdapterVersion, string SectionName,
    Phase7KnowledgeOrigin Origin, int ApprovedPropertyCount, int ExtractedClaimCount, int UnknownPropertyCount,
    IReadOnlyList<string> UnknownProperties, int ExactClaimProvenanceCount, int ExactEntityProvenanceCount,
    int ExactFieldProvenanceCount, int CoarseProvenanceCount, int UnsupportedClaimCount,
    IReadOnlyDictionary<string,int> MergeDecisionCounts, int RejectedSourceCount, int UncertifiedSourceCount);

public sealed record Phase7InputAuthorityRequest(string ExecutionRoot, string ExecutionId, string PlanId,
    string EventId, string Language, string ExpectedProfile, IReadOnlyList<string> ExpectedVariants);
public sealed record Phase7CommittedInputAuthority(PublishedStoryFrameAuthority StoryFrameAuthority,
    string EventFamily, string EventType, string Language, string Profile, string ProfileVersion,
    string SourceEventIntelligenceId, string KnowledgePayloadId, string KnowledgePayloadChecksum,
    string SourceRegistryId, string SourceRegistryChecksum, string? EvergreenPayloadId, string? EvergreenPayloadChecksum,
    FamilyNarrationProfile FamilyProfile, IReadOnlyList<StoryFrameAuthorityFrame> LongStoryFrames,
    IReadOnlyList<StoryFrameAuthorityFrame> ShortStoryFrames, IReadOnlyList<StoryFrameSceneIndex> LongSourceScenes,
    IReadOnlyList<StoryFrameSceneIndex> ShortSourceScenes, IReadOnlyList<string> LineageEvidence,
    IReadOnlyList<string> InputArtifactPaths, IReadOnlyDictionary<string, string> RuntimeProviderCompatibilityMetadata,
    ResolvedNarrationKnowledge Knowledge);
public sealed record Phase7InputAuthorityEvaluation(bool IsValid, Phase7CommittedInputAuthority? Authority,
    string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public interface IPhase7InputAuthorityEvaluator
{
    Task<Phase7InputAuthorityEvaluation> EvaluateAsync(Phase7InputAuthorityRequest request, CancellationToken cancellationToken = default);
}

public sealed record SceneKnowledgePacket(string PacketId, string ExecutionId, string PlanId, string EventId,
    string EventFamily, string Language, string ProfileId, string ProfileVersion, string Variant,
    string StoryFrameId, string StoryFrameChecksum, string SourceSceneId, string SourceSceneChecksum,
    int SceneNumber, int FrameNumber, string NarrativeStage, string SceneRole, string SectionKey,
    string ViewerQuestionId, string? ViewerQuestionText, string LearningObjectiveId, string SceneObjective,
    IReadOnlyList<CertifiedNarrationClaim> RequiredClaims, IReadOnlyList<CertifiedNarrationClaim> OptionalClaims,
    IReadOnlyList<CertifiedNarrationClaim> DeferredClaims, IReadOnlyList<string> CulturalContext,
    IReadOnlyList<string> SafetyRules, IReadOnlyList<string> EditorialConstraints, IReadOnlyList<string> ProhibitedClaims,
    IReadOnlyDictionary<string, string> LocalizedVocabulary, IReadOnlyList<string> ProtectedTerms,
    IReadOnlyDictionary<string, string> PronunciationHints, IReadOnlyList<string> VisualEvidenceIds,
    IReadOnlyList<string> KnowledgeReferenceIds, IReadOnlyList<string> SourceIds, int TargetDurationSeconds,
    int MinimumDurationSeconds, int MaximumDurationSeconds, bool LocationDependence, bool DateTimeDependence,
    IReadOnlyList<string> ApproximationWarnings, bool HumanReviewRequired, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingIssues, IReadOnlyDictionary<string, string> UpstreamSemanticLineage,
    string DeterministicChecksum)
{
    public string SourceViewerQuestionId { get; init; } = ViewerQuestionId;
    public string ResolvedViewerQuestionText { get; init; } = ViewerQuestionText ?? "";
    public string ViewerQuestionResolutionReason { get; init; } = "CertifiedClaimAndSceneRole";
    public string ViewerQuestionResolutionChecksum { get; init; } = "";
    public IReadOnlyList<string> VisualPlanningLineage { get; init; } = [];
}
public enum Phase7KnowledgeReferenceStatus { Resolved, Deferred, Missing, Ambiguous, CrossVariantInvalid, Unsupported }
public sealed record Phase7KnowledgeReferenceResolution(string ReferenceId, Phase7KnowledgeReferenceStatus Status,
    IReadOnlyList<CertifiedNarrationClaim> Claims, string ReasonCode);
public interface IPhase7KnowledgeReferenceResolver
{
    IReadOnlyList<Phase7KnowledgeReferenceResolution> Resolve(IReadOnlyList<string> referenceIds, ResolvedNarrationKnowledge knowledge, bool optional = false);
}
public interface IPhase7SceneKnowledgePacketBuilder
{
    IReadOnlyList<SceneKnowledgePacket> Build(Phase7CommittedInputAuthority authority, string variant);
}

public sealed record NarrationWordRange(int Minimum, int Preferred, int Maximum);
public sealed record NarrationScenePlan(string SceneId, string StoryFrameId, string SectionKey,
    string NarrativePurpose, string OpeningStrategy, IReadOnlyList<string> RequiredClaimIds,
    IReadOnlyList<string> OptionalClaimIds, IReadOnlyList<string> ProhibitedClaimIds, string EmotionalProgression,
    string EducationalProgression, string TransitionIntent, string CallbackIntent, string ClosingIntent,
    int TargetDurationSeconds, NarrationWordRange TargetWordRange, IReadOnlyList<string> SafetyRules,
    IReadOnlyList<string> LanguageRules, bool HumanReviewRequired);
public sealed record VariantNarrationPlan(string PlanId, string ExecutionId, string EventId, string EventFamily,
    string Language, string ProfileId, string ProfileVersion, string Variant, string SourceStoryFrameAuthorityId,
    string SourceStoryFrameAuthorityChecksum, int ScenePlanCount, int TargetTotalDurationSeconds,
    IReadOnlyList<NarrationScenePlan> Scenes, string DeterministicChecksum);
public sealed record NarrationPlanningAuthority(string ContractVersion, VariantNarrationPlan Long, VariantNarrationPlan Short, string DeterministicChecksum);
public interface IPhase7NarrationPlanningBuilder { VariantNarrationPlan Build(Phase7CommittedInputAuthority authority, IReadOnlyList<SceneKnowledgePacket> packets, string variant); }

public sealed record Phase7FoundationValidationGate(string Name, bool Passed, IReadOnlyList<string> Errors);
public sealed record Phase7FoundationValidation(bool IsValid, string ReasonCode, IReadOnlyList<Phase7FoundationValidationGate> Gates,
    IReadOnlyList<string> Errors, string DeterministicChecksum)
{ public Phase7FoundationValidationMode Mode { get; init; } = Phase7FoundationValidationMode.InMemoryCandidate; public Phase7FoundationArtifactInventory? ArtifactInventory { get; init; } }
public enum Phase7FoundationValidationMode { InMemoryCandidate, StagedPhysical, CommittedPhysical }
public sealed record Phase7FoundationArtifactInventoryEntry(string RelativePath, string ContractType, string ContractVersion,
    string SemanticChecksum, string PhysicalSha256, long SizeBytes, string ExecutionId, string PlanId, string EventId,
    string? Variant, bool Required, string SourceAuthorityId, string SourceAuthorityChecksum, string LineageChecksum);
public sealed record Phase7FoundationArtifactInventory(IReadOnlyList<Phase7FoundationArtifactInventoryEntry> Artifacts, string DeterministicChecksum);
public interface IPhase7FoundationValidator
{
    Phase7FoundationValidation Validate(Phase7CommittedInputAuthority input, IReadOnlyList<SceneKnowledgePacket> longPackets,
        IReadOnlyList<SceneKnowledgePacket> shortPackets, VariantNarrationPlan longPlan, VariantNarrationPlan shortPlan,
        IReadOnlyList<string> artifactPaths);
    Phase7FoundationValidation Validate(Phase7CommittedInputAuthority input, IReadOnlyList<SceneKnowledgePacket> longPackets,
        IReadOnlyList<SceneKnowledgePacket> shortPackets, VariantNarrationPlan longPlan, VariantNarrationPlan shortPlan,
        IReadOnlyList<string> artifactPaths, Phase7FoundationCompleteSetReadback physicalReadback);
    Phase7FoundationValidation Validate(Phase7CommittedInputAuthority input, IReadOnlyList<SceneKnowledgePacket> longPackets,
        IReadOnlyList<SceneKnowledgePacket> shortPackets, VariantNarrationPlan longPlan, VariantNarrationPlan shortPlan,
        IReadOnlyList<string> artifactPaths, Phase7FoundationValidationMode mode, Phase7FoundationCompleteSetReadback? physicalReadback = null);
}

public sealed record Phase7FoundationPhysicalReadbackEvidence(string ArtifactPath, bool Exists, long SizeBytes,
    string PhysicalSha256, bool DeserializationSucceeded, string ContractType, string ContractVersion,
    bool IdentityMatched, bool SemanticChecksumMatched, bool LineageMatched, bool SafePath,
    IReadOnlyList<string> Errors);
public sealed record Phase7FoundationCompleteSetReadback(IReadOnlyList<Phase7FoundationPhysicalReadbackEvidence> Artifacts,
    bool IsValid, IReadOnlyList<string> Errors)
{ public Phase7FoundationArtifactInventory? ExpectedInventory { get; init; } }

public sealed class Phase7NarrationOptions
{
    public const string SectionName = "Phase7Narration";
    public bool Enabled { get; set; } = true;
    public string ContractVersion { get; set; } = "rc2-phase7-narration.v1";
    public string FoundationContractVersion { get; set; } = Phase7FoundationContract.Version;
    public int MaxRevisionAttempts { get; set; } = 3;
    public bool RequireAcceptedReleaseCandidate { get; set; } = true;
    public bool UseSpeechDurationValidation { get; set; }
    public bool FailOnUnsupportedClaim { get; set; } = true;
    public bool AllowEditorialConnectiveText { get; set; }
    public string ProfileDirectory { get; set; } = "profiles/phase7";
    public int DefaultEnglishWordsPerMinute { get; set; } = 135;
    public int DefaultHindiWordsPerMinute { get; set; } = 120;
    public decimal MinClaimConfidence { get; set; } = .8m;
    public bool RequireSourceIds { get; set; } = true;
    public bool RequireHumanReviewForCulturalUncertainty { get; set; } = true;
}

public sealed record Phase7FoundationDiagnostics(string ExecutionId, string PlanId, string EventId, string EventFamily,
    string Language, string ProfileId, string ProfileVersion, bool InputAuthorityValid, bool KnowledgePayloadResolved,
    bool SourceRegistryValid, bool LocalizationResolved, int LongStoryFrameCount, int ShortStoryFrameCount,
    int LongPacketCount, int ShortPacketCount, int TotalPacketCount, int LongPlanningSceneCount, int ShortPlanningSceneCount,
    IReadOnlyList<string> AvailableKnowledgeDomains, IReadOnlyList<string> MissingKnowledgeDomains,
    IReadOnlyList<string> DeferredKnowledgeDomains, int PlaceholderFieldsDetected, int PlaceholderFieldsResolved,
    IReadOnlyList<string> UnresolvedPlaceholders, int LocationDependentClaimCount, int DateTimeDependentClaimCount,
    int ApproximateClaimCount, int HumanReviewClaimCount, int WarningCount, int BlockingIssueCount,
    IReadOnlyList<string> InputArtifactPaths, IReadOnlyList<string> OutputArtifactPaths, string DeterministicChecksum)
{
    public bool RawPayloadLoaded { get; init; }
    public bool EvergreenPayloadLoaded { get; init; }
    public bool KnowledgeMergeSucceeded { get; init; }
    public bool FamilyAuthorityCertified { get; init; }
    public bool ClaimProvenanceValid { get; init; }
    public bool MandatoryDomainsSatisfied { get; init; }
    public bool KnowledgeReferencesResolved { get; init; }
    public bool LongSemanticEnrichmentComplete { get; init; }
    public bool ShortSemanticEnrichmentComplete { get; init; }
    public bool LocationTimeSafetyPassed { get; init; }
    public bool CulturalSafetyPassed { get; init; }
    public bool PhysicalReadbackPassed { get; init; }
    public int RawClaimCount { get; init; }
    public int EvergreenClaimCount { get; init; }
    public int MergedClaimCount { get; init; }
    public int ExactSourceMappedClaimCount { get; init; }
    public int CoarseSourceMappedClaimCount { get; init; }
    public int ResolvedReferenceCount { get; init; }
    public int DeferredReferenceCount { get; init; }
    public int MissingReferenceCount { get; init; }
    public int UnresolvedPlaceholderCount { get; init; }
}
public sealed record Phase7FoundationExecutionResult(bool IsValid, string ReasonCode, string OutputDirectory,
    Phase7FoundationValidation Validation, Phase7FoundationDiagnostics Diagnostics);
public interface IPhase7FoundationService
{
    Task<Phase7FoundationExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, CancellationToken cancellationToken = default);
}
public interface IPhase7FoundationFileSystem { }
public interface IPhase7FoundationExecutionLock { Task<IAsyncDisposable> AcquireAsync(string executionRoot, CancellationToken cancellationToken); }
public interface IPhase7FoundationRecoveryService { Task RecoverAsync(string executionRoot, CancellationToken cancellationToken = default); }
public interface IPhase7FoundationTransactionCoordinator { Task<Phase7FoundationExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, CancellationToken cancellationToken = default); }
public sealed record PublishedPhase7FoundationAuthority(Phase7CommittedInputAuthority Phase7CommittedInputAuthority,
    FamilyNarrationProfile FamilyNarrationProfile, ResolvedNarrationKnowledge ResolvedNarrationKnowledge,
    IReadOnlyList<SceneKnowledgePacket> LongSceneKnowledgePackets, IReadOnlyList<SceneKnowledgePacket> ShortSceneKnowledgePackets,
    VariantNarrationPlan LongVariantNarrationPlan, VariantNarrationPlan ShortVariantNarrationPlan,
    Phase7FoundationDiagnostics FoundationDiagnostics, Phase7FoundationValidation FoundationValidation,
    IReadOnlyList<string> ArtifactPaths, IReadOnlyDictionary<string,string> SemanticChecksums,
    IReadOnlyDictionary<string,string> PhysicalHashes, IReadOnlyDictionary<string,string> ContractVersions,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence);
public sealed record Phase7FoundationCommittedStateEvaluation(bool IsValid, PublishedPhase7FoundationAuthority? Authority,
    string ReasonCode, IReadOnlyList<string> Errors);
public interface IPhase7FoundationCommittedStateEvaluator
{
    Task<Phase7FoundationCommittedStateEvaluation> EvaluateAsync(string executionRoot, CancellationToken cancellationToken = default);
}

public static class Phase7Determinism
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
    public static string ClaimId(string payloadId, string domain, int ordinal) => $"claim-{Hash(new { payloadId, domain, ordinal })[..20]}";
    public static string SemanticClaimId(string knowledgeId, string claimPath, string language, string version)
        => $"claim-{Hash(new { knowledgeId = knowledgeId.ToLowerInvariant(), claimPath = claimPath.ToLowerInvariant(), language = language.ToLowerInvariant(), version })[..24]}";
}
