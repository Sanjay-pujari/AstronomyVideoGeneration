namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class NarrationPlanningContract { public const string Version = "rc2-phase7-narration-planning.v1"; }

/// <summary>Provider-independent identities which govern narration planning semantics.</summary>
public static class NarrationPlanningPolicyCatalog
{
    public const string GoalPolicy = "CertifiedClaimsAndViewerQuestion";
    public const string OpeningMode = "VariantOpeningWhenFirst";
    public const string DevelopmentMode = "RequiredThenOptional";
    public const string ClosingMode = "VariantClosingWhenLast";
    public const string ClaimIntroductionPolicy = "RequiredInPacketOrder";
    public const string OptionalClaimUsagePolicy = "OptionalOnlyWhenTimeAllows";
    public const string DeferredClaimPolicy = "UnavailableForFactualDrafting";
    public const string CallbackPolicy = "PacketLineageOnly";
    public const string RequiredClaimUsage = "MandatoryFactualAuthority";
    public const string OptionalClaimUsage = "ConditionalFactualAuthority";
    public const string DeferredClaimUsage = "UnavailableForFactualDrafting";
    public const string CulturalQualificationPrefix = "QualifyCulture";
    public const string LocationQualificationPrefix = "QualifyLocation";
    public const string TimeQualificationPrefix = "QualifyDateTime";
    public const string AstrologyQualificationPrefix = "ClarifyAstrology";
    public const string HumanReviewPrefix = "HumanReview";
    public const string VariantOpeningTransition = "VariantOpening";
    public const string StoryFrameSuccessorTransition = "StoryFrameSuccessor";
    public const string VariantClosingTransition = "VariantClosing";
}

public sealed record SceneKnowledgePacketCollection(IReadOnlyList<SceneKnowledgePacket> Long,
    IReadOnlyList<SceneKnowledgePacket> Short, string DeterministicChecksum);

public sealed record Phase7NarrationPlanningInputAuthority(PublishedStoryFrameAuthority PublishedStoryFrameAuthority,
    PublishedPhase7KnowledgeAuthority PublishedPhase7KnowledgeAuthority, SceneKnowledgePacketCollection SceneKnowledgePacketCollection,
    Phase7SceneKnowledgePacketValidation PacketValidation, FamilyNarrationProfile FamilyNarrationProfile,
    string ExecutionId, string PlanId, string EventId, string Language, string ProfileId, string ProfileVersion,
    IReadOnlyDictionary<string,string> Phase4To7Lineage, IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence);

public sealed record Phase7NarrationPlanningInputAuthorityRequest(string ExecutionRoot, string ExecutionId, string PlanId,
    string EventId, string Language, string ProfileId, string ProfileVersion,
    SceneKnowledgePacketCollection SceneKnowledgePacketCollection, Phase7SceneKnowledgePacketValidation? PacketValidation = null);
public sealed record Phase7NarrationPlanningInputAuthorityEvaluation(bool IsValid,
    Phase7NarrationPlanningInputAuthority? Authority, string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public interface IPhase7NarrationPlanningInputAuthorityEvaluator { Task<Phase7NarrationPlanningInputAuthorityEvaluation> EvaluateAsync(
    Phase7NarrationPlanningInputAuthorityRequest request, CancellationToken cancellationToken = default); }

public sealed record NarrationPlanningGoal(string SceneRole, string SectionKey, string ViewerQuestionId,
    string LearningObjectiveId, IReadOnlyList<string> RequiredClaimIds, string ProfileId, string GoalPolicy,
    string DeterministicChecksum);
public sealed record NarrationPlanningStrategy(string NarrativeStage, string SceneRole, string SectionKey,
    string OpeningMode, string DevelopmentMode, string ClosingMode, string ClaimIntroductionPolicy,
    string OptionalClaimUsagePolicy, string DeferredClaimPolicy, string CallbackPolicy, string DeterministicChecksum);
public sealed record NarrationClaimUsagePolicy(string Required, string Optional, string Deferred);
public sealed record NarrationPlanningConstraintRequest(string Language, string Variant, int TargetDurationSeconds,
    int MinimumDurationSeconds, int MaximumDurationSeconds, FamilyNarrationProfile Profile, string SceneRole,
    string SectionKey, int RequiredClaimCount, int OptionalClaimCount,
    int MandatoryIncomingTransitionSentenceCount, int MandatoryOutgoingTransitionSentenceCount,
    int MandatoryQualificationSentenceCount)
{
    public NarrationPlanningConstraintRequest(string Language, string Variant, int TargetDurationSeconds,
        int MinimumDurationSeconds, int MaximumDurationSeconds, FamilyNarrationProfile Profile, string SceneRole,
        string SectionKey, int RequiredClaimCount, int OptionalClaimCount)
        : this(Language, Variant, TargetDurationSeconds, MinimumDurationSeconds, MaximumDurationSeconds, Profile,
            SceneRole, SectionKey, RequiredClaimCount, OptionalClaimCount, 0, 0, 0)
    {
    }
}
public sealed record NarrationPlanningConstraints(int MinimumSentenceCount, int PreferredSentenceCount,
    int MaximumSentenceCount, int ReadingTimeTargetSeconds, string PauseStrategy, IReadOnlyList<string> EmphasisRules,
    string ClaimOrderingPolicy, string VisualSynchronizationPolicy);
public interface INarrationPlanningConstraintPolicy { NarrationPlanningConstraints Resolve(NarrationPlanningConstraintRequest request); }
public sealed record NarrationPlanningRealizabilityBudget(
    int RequiredClaimSentenceCount, int MandatoryQualificationSentenceCount,
    int MandatoryIncomingTransitionSentenceCount, int MandatoryOutgoingTransitionSentenceCount,
    int MinimumStructuralSentenceCount, int MinimumMandatorySentenceCount, int MaximumSentenceCount,
    bool IsRealizable, string ReasonCode);
public sealed record NarrationPlanningDraftRealizabilityRequest(
    string Variant, string SectionKey, IReadOnlyList<string> RequiredClaimIds,
    NarrationPlanningTransition IncomingTransition, NarrationPlanningTransition OutgoingTransition,
    NarrationPlanningConstraints Constraints, IReadOnlyList<string> LocationQualificationRequirements,
    IReadOnlyList<string> TimeQualificationRequirements, IReadOnlyList<string> CulturalQualificationRequirements,
    IReadOnlyList<string> AstrologyQualificationRequirements);
public interface INarrationPlanningDraftRealizabilityPolicy
{
    NarrationPlanningRealizabilityBudget Evaluate(NarrationPlanningDraftRealizabilityRequest request);
}
public sealed record NarrationPlanningSceneRealizabilityDiagnostic(
    string PlanningId, string Variant, int SceneNumber, string SceneId, string StoryFrameId, string SectionKey,
    IReadOnlyList<string> RequiredClaimIds, int RequiredClaimSentenceCount, int IncomingTransitionSentenceCount,
    int OutgoingTransitionSentenceCount, int MandatoryQualificationSentenceCount, int MinimumMandatorySentenceCount,
    int MinimumSentenceCount, int PreferredSentenceCount, int MaximumSentenceCount, bool IsDraftRealizable,
    IReadOnlyList<string> ReasonCodes);

public sealed record NarrationPlanningTransition(string TransitionId, string ExecutionId, string Variant,
    string? FromStoryFrameId, string? FromStoryFrameChecksum, string? ToStoryFrameId, string? ToStoryFrameChecksum,
    string Kind, string? SourceTransitionOut, string? DestinationTransitionIn, string? PreviousPacketId,
    string? CurrentPacketId, string? NextPacketId, string DeterministicChecksum);
public sealed record NarrationPlanningScene(string PlanningId, string SceneId, string Variant, string StoryFrameId,
    string StoryFrameChecksum, string SourceSceneChecksum, string PacketId, string PacketChecksum, string ViewerQuestion,
    string LearningObjective, NarrationPlanningGoal NarrativeGoal, IReadOnlyList<string> PrimaryKnowledgeReferences,
    IReadOnlyList<string> SupportingKnowledgeReferences, IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string> OptionalClaims, IReadOnlyList<string> DeferredClaims, NarrationPlanningStrategy Strategy,
    NarrationClaimUsagePolicy ClaimUsagePolicy, NarrationPlanningConstraints NarrationConstraints,
    IReadOnlyList<string> ForbiddenStatements, IReadOnlyList<string> SafetyRequirements,
    IReadOnlyList<string> EditorialConstraints, IReadOnlyList<string> CulturalQualificationRequirements,
    IReadOnlyList<string> LocationQualificationRequirements, IReadOnlyList<string> TimeQualificationRequirements,
    IReadOnlyList<string> AstrologyQualificationRequirements, IReadOnlyList<string> HumanReviewRequirements,
    int MinimumDuration, int ExpectedDuration, int MaximumDuration, int ExpectedSentenceCount,
    int EstimatedReadingTime, IReadOnlyList<string> VisualSynchronizationTargets,
    NarrationPlanningTransition IncomingTransition, NarrationPlanningTransition OutgoingTransition,
    string DeterministicChecksum);
public sealed record NarrationPlanningDiagnostics(int PacketCount, int PlanningSceneCount, int LongPlanningSceneCount,
    int ShortPlanningSceneCount, int PrimaryReferenceCount, int SupportingReferenceCount, int RequiredReferenceCount,
    int ResolvedReferenceCount, int DeferredReferenceCount, int MissingReferenceCount,
    int AmbiguousReferenceCount, int CrossVariantReferenceCount, int UnsupportedReferenceCount,
    int UnresolvedReferenceCount, int TransitionCount, int BlockingIssueCount,
    int FailedGateCount, int RequiredClaimCount, int OptionalClaimCount, int DeferredClaimCount, int WarningCount, int ErrorCount,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, IReadOnlyList<NarrationPlanningSceneRealizabilityDiagnostic> RealizabilityDiagnostics, string DeterministicChecksum);
public sealed record NarrationPlanningAuthority(string ContractVersion, string AuthorityId, string ExecutionId,
    string PlanId, string EventId, string Language, string ProfileId, string ProfileVersion,
    string StoryFrameAuthorityChecksum, string KnowledgeAuthorityChecksum, string PacketCollectionChecksum,
    IReadOnlyList<NarrationPlanningScene> LongScenes, IReadOnlyList<NarrationPlanningScene> ShortScenes,
    NarrationPlanningDiagnostics Diagnostics, IReadOnlyDictionary<string,string> Phase4To7Lineage,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence, string DeterministicChecksum);
public sealed record NarrationPlanningAuthorityBuildResult(bool IsValid, NarrationPlanningAuthority? Authority,
    string ReasonCode, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingIssues);
public interface INarrationPlanningAuthorityBuilder
{
    NarrationPlanningAuthorityBuildResult Build(Phase7NarrationPlanningInputAuthority input);
}
public sealed record NarrationPlanningValidationGate(string Name, bool Passed, IReadOnlyList<string> Errors);
public sealed record NarrationPlanningValidation(bool IsValid, string ReasonCode,
    IReadOnlyList<NarrationPlanningValidationGate> Gates, IReadOnlyList<string> Errors, string DeterministicChecksum);
public interface INarrationPlanningValidator { NarrationPlanningValidation Validate(Phase7NarrationPlanningInputAuthority input,
    NarrationPlanningAuthority authority); }
