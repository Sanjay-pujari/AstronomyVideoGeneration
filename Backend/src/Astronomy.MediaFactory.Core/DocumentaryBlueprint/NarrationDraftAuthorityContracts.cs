namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class NarrationDraftContract { public const string Version = "rc2-phase7-narration-draft.v1"; }

public static class NarrationDraftReasonCodes
{
    public const string InputValid="NARRATION_DRAFT_INPUT_VALID", PlanningMissing="NARRATION_DRAFT_PLANNING_AUTHORITY_MISSING",
        PlanningInvalid="NARRATION_DRAFT_PLANNING_AUTHORITY_INVALID", LineageStale="NARRATION_DRAFT_PLANNING_LINEAGE_STALE",
        ProfileMismatch="NARRATION_DRAFT_PROFILE_MISMATCH", LanguageMismatch="NARRATION_DRAFT_LANGUAGE_MISMATCH",
        RuntimeIncompatible="NARRATION_DRAFT_RUNTIME_INCOMPATIBLE", AuthorityValid="NARRATION_DRAFT_AUTHORITY_VALID",
        InputInvalid="NARRATION_DRAFT_INPUT_INVALID", SceneInvalid="NARRATION_DRAFT_PLANNING_SCENE_INVALID",
        RequiredMissing="NARRATION_DRAFT_REQUIRED_CLAIM_MISSING", RequiredDuplicated="NARRATION_DRAFT_REQUIRED_CLAIM_DUPLICATED",
        RequiredOverBudget="NARRATION_DRAFT_REQUIRED_CLAIMS_EXCEED_BUDGET", OptionalInvalid="NARRATION_DRAFT_OPTIONAL_CLAIM_INVALID",
        DeferredUsed="NARRATION_DRAFT_DEFERRED_CLAIM_USED", QualificationMissing="NARRATION_DRAFT_QUALIFICATION_MISSING",
        SafetyInvalid="NARRATION_DRAFT_SAFETY_INVALID", TransitionInvalid="NARRATION_DRAFT_TRANSITION_INVALID",
        TimingInvalid="NARRATION_DRAFT_TIMING_INVALID",
        SceneSentenceCountBelowMinimum="NARRATION_DRAFT_SCENE_SENTENCE_COUNT_BELOW_MINIMUM",
        SceneSentenceCountAboveMaximum="NARRATION_DRAFT_SCENE_SENTENCE_COUNT_ABOVE_MAXIMUM",
        SceneDurationBelowMinimum="NARRATION_DRAFT_SCENE_DURATION_BELOW_MINIMUM",
        SceneDurationAboveMaximum="NARRATION_DRAFT_SCENE_DURATION_ABOVE_MAXIMUM",
        TransitionSentenceCountInvalid="NARRATION_DRAFT_TRANSITION_SENTENCE_COUNT_INVALID",
        TimingPolicyUnresolved="NARRATION_DRAFT_TIMING_POLICY_UNRESOLVED",
        RequiredContentExceedsTimingCapacity="NARRATION_DRAFT_REQUIRED_CONTENT_EXCEEDS_TIMING_CAPACITY",
        InsufficientGovernedContentForMinimum="NARRATION_DRAFT_INSUFFICIENT_GOVERNED_CONTENT_FOR_MINIMUM",
        LanguageInvalid="NARRATION_DRAFT_LANGUAGE_INVALID",
        CertifiedLanguageClaimMissing="NARRATION_DRAFT_CERTIFIED_LANGUAGE_CLAIM_MISSING",
        HumanReviewInvalid="NARRATION_DRAFT_HUMAN_REVIEW_CLAIM_INVALID",
        ClaimAuthorityMissing="NARRATION_DRAFT_COMMITTED_CLAIM_AUTHORITY_MISSING",
        ClaimAuthorityInvalid="NARRATION_DRAFT_COMMITTED_CLAIM_AUTHORITY_INVALID",
        ClaimLineageStale="NARRATION_DRAFT_CLAIM_LINEAGE_STALE",
        UnsupportedKnowledgeResult="NARRATION_DRAFT_UNSUPPORTED_KNOWLEDGE_EVALUATION_RESULT",
        ClaimChecksumMismatch="NARRATION_DRAFT_CLAIM_CHECKSUM_MISMATCH",
        UnknownPlanningClaim="NARRATION_DRAFT_UNKNOWN_PLANNING_CLAIM",
        ClaimPartitionMismatch="NARRATION_DRAFT_CLAIM_PARTITION_MISMATCH";
}

public sealed record Phase7NarrationDraftInputAuthority(PublishedNarrationPlanningAuthority PublishedNarrationPlanningAuthority,
    NarrationPlanningAuthority NarrationPlanningAuthority, NarrationPlanningDiagnostics NarrationPlanningDiagnostics,
    NarrationPlanningPublicationReport NarrationPlanningPublicationReport, NarrationPlanningPhysicalValidation NarrationPlanningPhysicalValidation,
    NarrationPlanningManifestEntry PlanningManifestEntry, NarrationPlanningPublicationEvidence PlanningPublicationEvidence,
    FamilyNarrationProfile FamilyNarrationProfile, string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string ProfileVersion, IReadOnlyDictionary<string,string> Phase4To7Lineage,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence)
{
    /// <summary>The certified, language-specific text boundary. Claims are copied from the committed knowledge package; no provider is consulted.</summary>
    public IReadOnlyList<CertifiedNarrationClaim> CertifiedClaims { get; init; } = [];
    public PublishedPhase7KnowledgeAuthority CommittedClaimAuthority { get; init; } = null!;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
/// <summary>Only committed-state coordinates are accepted; callers cannot submit narration claims.</summary>
public sealed record Phase7NarrationDraftInputAuthorityRequest(Phase7NarrationPlanningInputAuthorityRequest PlanningRequest,
    Phase7KnowledgeCommittedStateRequest CommittedClaimAuthorityRequest);
public sealed record Phase7NarrationDraftInputAuthorityEvaluation(bool IsValid, Phase7NarrationDraftInputAuthority? Authority,
    string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{ public IReadOnlyList<string> BlockingIssues { get; init; } = []; }
public interface IPhase7NarrationDraftInputAuthorityEvaluator { Task<Phase7NarrationDraftInputAuthorityEvaluation> EvaluateAsync(
    Phase7NarrationDraftInputAuthorityRequest request, CancellationToken cancellationToken=default); }

public sealed record NarrationDraftTransitionPhrase(string TransitionId,string Kind,string Text,string Variant,
    IReadOnlyList<string> PlanningTransitionIds,string DeterministicChecksum);
public sealed record NarrationDraftSentence(string SentenceId,int Ordinal,string Text,string SentenceRole,
    IReadOnlyList<string> ClaimIds,IReadOnlyList<string> KnowledgeReferenceIds,IReadOnlyList<string> QualificationIds,
    IReadOnlyList<string> SafetyRuleIds,IReadOnlyList<string> VisualTargetIds,bool IsRequired,bool IsOptional,bool IsTransition,
    decimal EstimatedDurationSeconds,string DeterministicChecksum);
public sealed record NarrationDraftClaimUsage(string ClaimId,string ClaimPartition,string SentenceId,string UsageMode,
    IReadOnlyList<string> QualificationIds,string DeterministicChecksum);
public sealed record NarrationDraftScene(string DraftSceneId,string PlanningId,string SceneId,string Variant,string StoryFrameId,
    string PacketId,string PlanningChecksum,string ViewerQuestion,string LearningObjective,string Opening,
    IReadOnlyList<NarrationDraftSentence> Sentences,string Closing,NarrationDraftTransitionPhrase? IncomingTransitionPhrase,
    NarrationDraftTransitionPhrase? OutgoingTransitionPhrase,IReadOnlyList<NarrationDraftClaimUsage> RequiredClaimUsage,
    IReadOnlyList<NarrationDraftClaimUsage> OptionalClaimUsage,IReadOnlyList<string> DeferredClaimIds,
    IReadOnlyList<string> AppliedQualifications,IReadOnlyList<string> AppliedSafetyRules,IReadOnlyList<string> AppliedEditorialConstraints,
    int WordCount,int SentenceCount,decimal EstimatedReadingTimeSeconds,int MinimumDurationSeconds,int TargetDurationSeconds,
    int MaximumDurationSeconds,string DeterministicChecksum);
public sealed record NarrationDraftSceneFailureSummary(
    string Variant,int SceneNumber,string SceneId,string StoryFrameId,string PlanningSceneId,string SectionKey,
    int SentenceCount,int MinimumSentenceCount,int MaximumSentenceCount,decimal EstimatedDurationSeconds,
    decimal MinimumDurationSeconds,decimal MaximumDurationSeconds,IReadOnlyList<string> RequiredClaimIds,
    IReadOnlyList<string> RealizedRequiredClaimIds,IReadOnlyList<string> RealizedOptionalClaimIds,
    IReadOnlyList<string> TransitionSentenceIds,IReadOnlyList<string> FailedGateNames,IReadOnlyList<string> ReasonCodes);

public sealed record NarrationDraftDiagnostics(int PlanningSceneCount,int DraftSceneCount,int LongDraftSceneCount,int ShortDraftSceneCount,
    int SentenceCount,int RequiredClaimCount,int RequiredClaimUsageCount,int OptionalClaimCount,int OptionalClaimUsageCount,
    int DeferredClaimCount,int DeferredClaimUsageCount,int QualifiedClaimCount,int TransitionSentenceCount,int TotalWordCount,
    decimal EstimatedReadingTimeSeconds,int BlockingIssueCount,int FailedGateCount,int WarningCount,int ErrorCount,
    IReadOnlyList<string> Warnings,IReadOnlyList<string> Errors,string DeterministicChecksum);
public sealed record NarrationDraftAuthority(string ContractVersion,string AuthorityId,string ExecutionId,string PlanId,string EventId,
    string Language,string ProfileId,string ProfileVersion,string PlanningAuthorityId,string PlanningAuthorityChecksum,
    IReadOnlyList<NarrationDraftScene> LongScenes,IReadOnlyList<NarrationDraftScene> ShortScenes,NarrationDraftDiagnostics Diagnostics,
    IReadOnlyDictionary<string,string> Phase4To7Lineage,IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence,string DeterministicChecksum);
public sealed record NarrationDraftValidationGate(string Name,bool Passed,IReadOnlyList<string> Errors);
public sealed record NarrationDraftValidation(bool IsValid,string ReasonCode,IReadOnlyList<NarrationDraftValidationGate> Gates,
    IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings,string DeterministicChecksum);
public sealed record NarrationDraftAuthorityBuildResult(bool IsValid,NarrationDraftAuthority? Authority,string ReasonCode,
    IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings,IReadOnlyList<string> BlockingIssues)
{ public IReadOnlyList<NarrationDraftSceneFailureSummary> SceneFailureSummaries { get; init; } = []; }

public sealed record NarrationDraftSceneTimingDecision(
    string Variant,string SectionKey,int MinimumSentenceCount,int MaximumSentenceCount,
    decimal MinimumDurationSeconds,decimal MaximumDurationSeconds,string PolicyId,string PolicyVersion);
public sealed record NarrationDraftTimingPolicyRequest(string Language,string Variant,string SectionKey,int MinimumDurationSeconds,int TargetDurationSeconds,int MaximumDurationSeconds,
    int PreferredSentenceCount,int MinimumSentenceCount,int MaximumSentenceCount,int RequiredClaimCount,int OptionalClaimCount,string ProfilePacing,
    int MandatoryStructuralSentenceCount=0);
public sealed record NarrationDraftTimingBudget(int TargetWords,int MinimumWords,int MaximumWords,decimal TargetReadingTimeSeconds,
    decimal MinimumSentenceDurationSeconds,int PermittedOptionalClaimCapacity,decimal WordsPerMinute);
public interface INarrationDraftLanguagePolicy { bool Supports(string language);string Terminate(string text,string language);string OpeningBridge(string question,string objective,string language);string Conjunction(string language);decimal EstimateReadingTime(string text,string language);bool PreservesProtectedTokens(string certified,string realized); }
public interface INarrationDraftTimingPolicy { NarrationDraftSceneTimingDecision Resolve(NarrationDraftTimingPolicyRequest request); NarrationDraftTimingBudget Budget(NarrationDraftTimingPolicyRequest request); }
public interface INarrationDraftRealizationPolicy { string Realize(CertifiedNarrationClaim claim,IReadOnlyList<string> qualifications,string language); }
public interface INarrationDraftClaimCoalescingPolicy { bool CanCoalesce(NarrationPlanningScene scene,CertifiedNarrationClaim first,CertifiedNarrationClaim second,int maximumWords); }
public interface INarrationDraftOpeningPolicy { string Create(NarrationPlanningScene scene,string language); }
public interface INarrationDraftClosingPolicy { string Create(NarrationPlanningScene scene,bool hasNext,string language); }
public enum NarrationDraftTransitionOwnership { IncomingDestination, OutgoingSource }
public sealed record NarrationDraftTransitionPhraseRequest(NarrationPlanningTransition Transition,
    NarrationDraftTransitionOwnership Ownership,string Variant,string Language);
public interface INarrationDraftTransitionPhrasePolicy { NarrationDraftTransitionPhrase? Create(NarrationDraftTransitionPhraseRequest request); }
public interface INarrationDraftSafetyValidator { IReadOnlyList<string> Validate(NarrationPlanningScene planning,NarrationDraftScene draft,IReadOnlyDictionary<string,CertifiedNarrationClaim> claims); }
public interface INarrationDraftAuthorityBuilder { NarrationDraftAuthorityBuildResult Build(Phase7NarrationDraftInputAuthority input); }
public interface INarrationDraftValidator { NarrationDraftValidation Validate(Phase7NarrationDraftInputAuthority input,NarrationDraftAuthority authority); }
public sealed record Phase7NarrationDraftAuthorityServiceResult(bool Success,string InputEvaluationReason,string BuildReason,
    string ValidationReason,NarrationDraftAuthority? Authority,NarrationDraftValidation? Validation,
    IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings,IReadOnlyList<string> BlockingIssues)
{ public IReadOnlyList<NarrationDraftSceneFailureSummary> SceneFailureSummaries { get; init; } = []; }
public interface IPhase7NarrationDraftAuthorityService { Task<Phase7NarrationDraftAuthorityServiceResult> ExecuteAsync(
    Phase7NarrationDraftInputAuthorityRequest request,CancellationToken cancellationToken=default); }
