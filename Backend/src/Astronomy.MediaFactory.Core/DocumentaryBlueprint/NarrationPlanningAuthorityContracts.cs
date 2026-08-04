namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class NarrationPlanningContract { public const string Version = "o2-orch-p7.1b-ba.v1"; }

public sealed record SceneKnowledgePacketCollection(
    IReadOnlyList<SceneKnowledgePacket> Long,
    IReadOnlyList<SceneKnowledgePacket> Short,
    string DeterministicChecksum);

public sealed record Phase7NarrationPlanningInputAuthority(
    PublishedStoryFrameAuthority PublishedStoryFrameAuthority,
    PublishedPhase7KnowledgeAuthority PublishedPhase7KnowledgeAuthority,
    SceneKnowledgePacketCollection SceneKnowledgePacketCollection,
    FamilyNarrationProfile FamilyNarrationProfile,
    string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string ProfileVersion,
    IReadOnlyDictionary<string,string> Phase4To7Lineage,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence);

public sealed record Phase7NarrationPlanningInputAuthorityRequest(
    string ExecutionRoot, string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string ProfileVersion, SceneKnowledgePacketCollection SceneKnowledgePacketCollection);
public sealed record Phase7NarrationPlanningInputAuthorityEvaluation(bool IsValid,
    Phase7NarrationPlanningInputAuthority? Authority, string ReasonCode,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public interface IPhase7NarrationPlanningInputAuthorityEvaluator
{
    Task<Phase7NarrationPlanningInputAuthorityEvaluation> EvaluateAsync(
        Phase7NarrationPlanningInputAuthorityRequest request, CancellationToken cancellationToken = default);
}

public sealed record NarrationPlanningTransition(string TransitionId, string? FromStoryFrameId,
    string? ToStoryFrameId, string Kind, string LineageChecksum);
public sealed record NarrationPlanningConstraints(int MaximumSentenceCount, int MinimumSentenceCount,
    int ReadingTimeTargetSeconds, string PauseStrategy, IReadOnlyList<string> EmphasisRules,
    string ClaimOrderingPolicy, string VisualSynchronizationPolicy);
public sealed record NarrationPlanningScene(string PlanningId, string SceneId, string Variant,
    string StoryFrameId, string PacketId, string PacketChecksum, string ViewerQuestion,
    string LearningObjective, string NarrativeGoal, IReadOnlyList<string> PrimaryKnowledgeReferences,
    IReadOnlyList<string> SupportingKnowledgeReferences, IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string> OptionalClaims, IReadOnlyList<string> DeferredClaims, string NarrationIntent,
    NarrationPlanningConstraints NarrationConstraints, IReadOnlyList<string> ForbiddenStatements,
    IReadOnlyList<string> SafetyRequirements, IReadOnlyList<string> CulturalQualificationRequirements,
    IReadOnlyList<string> LocationQualificationRequirements, IReadOnlyList<string> TimeQualificationRequirements,
    int ExpectedDuration, int ExpectedSentenceCount, int EstimatedReadingTime,
    IReadOnlyList<string> VisualSynchronizationTargets, NarrationPlanningTransition IncomingTransition,
    NarrationPlanningTransition OutgoingTransition, string DeterministicChecksum);
public sealed record NarrationPlanningDiagnostics(int PacketCount, int PlanningSceneCount,
    int RequiredClaimCount, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors,
    string DeterministicChecksum);
public sealed record NarrationPlanningAuthority(string ContractVersion, string AuthorityId,
    string ExecutionId, string PlanId, string EventId, string Language, string ProfileId,
    string ProfileVersion, IReadOnlyList<NarrationPlanningScene> LongScenes,
    IReadOnlyList<NarrationPlanningScene> ShortScenes, NarrationPlanningDiagnostics Diagnostics,
    IReadOnlyDictionary<string,string> Phase4To7Lineage,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence, string DeterministicChecksum);

public interface INarrationPlanningAuthorityBuilder
{ NarrationPlanningAuthority Build(Phase7NarrationPlanningInputAuthority input); }

public sealed record NarrationPlanningValidationGate(string Name, bool Passed, IReadOnlyList<string> Errors);
public sealed record NarrationPlanningValidation(bool IsValid, string ReasonCode,
    IReadOnlyList<NarrationPlanningValidationGate> Gates, IReadOnlyList<string> Errors,
    string DeterministicChecksum);
public interface INarrationPlanningValidator
{ NarrationPlanningValidation Validate(Phase7NarrationPlanningInputAuthority input, NarrationPlanningAuthority authority); }
