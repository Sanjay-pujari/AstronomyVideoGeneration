namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class Phase7NarrationAuthorityOrchestrationReasonCodes
{
    public const string Completed = "P7_NARRATION_AUTHORITY_COMPLETED";
    public const string KnowledgeCommittedStateInvalid = "P7_NARRATION_AUTHORITY_KNOWLEDGE_COMMITTED_STATE_INVALID";
    public const string PacketBuildInvalid = "P7_NARRATION_AUTHORITY_PACKET_BUILD_INVALID";
    public const string PlanningBuildInvalid = "P7_NARRATION_AUTHORITY_PLANNING_BUILD_INVALID";
}

public sealed record Phase7NarrationAuthorityOrchestrationRequest(
    string ExecutionRoot,
    string ExecutionId,
    string PlanId,
    string EventId,
    string RegionId,
    string Language,
    string ProfileId,
    string ProfileVersion,
    bool OverwriteExisting,
    bool RetryFailedOnly,
    string DependencyExpansionMode,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityCoordinates,
    IReadOnlyList<string> RequestedVariants)
{
    public string EventType { get; init; } = "";
    public string ContentCategory { get; init; } = "";
    public Phase7CanonicalProfileIdentity? CanonicalProfileIdentity { get; init; }
}

public sealed record Phase7AuthorityStageResult(
    string StageCode,
    string StageName,
    bool Success,
    string Status,
    string ReasonCode,
    bool Reused,
    bool PublicationCommitted,
    bool CommittedStateValidationPassed,
    IReadOnlyList<string> OutputFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> BlockingIssues)
{
    public int LongCount { get; init; }
    public int ShortCount { get; init; }
    public int FailedGateCount { get; init; }
    public int PassedGateCount { get; init; }
}

public sealed record Phase7ProviderIsolationEvidence(int AzureOpenAiCalls, int PromptComposerCalls,
    int NarrationGeneratorCalls, int TranslationCalls, int AzureSpeechCalls, int TtsCalls, int RenderingCalls);

public sealed record Phase7NarrationAuthorityOrchestrationResult(
    bool Success,
    string ExecutionId,
    string PlanId,
    string EventId,
    string Language,
    string ProfileId,
    string ProfileVersion,
    IReadOnlyList<Phase7AuthorityStageResult> StageResults,
    string? LastCompletedInternalStage,
    string? FailedInternalStage,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> BlockingIssues,
    Phase7ProviderIsolationEvidence ProviderIsolation,
    string? PlanningAuthorityId,
    string? PlanningAuthorityChecksum,
    string? DraftAuthorityId,
    string? DraftAuthorityChecksum,
    int LongDraftSceneCount,
    int ShortDraftSceneCount,
    string? DraftValidationReason,
    IReadOnlyList<NarrationDraftValidationGate> DraftGateStatuses)
{
    public string ReasonCode { get; init; } = Success
        ? Phase7NarrationAuthorityOrchestrationReasonCodes.Completed
        : StageResults.LastOrDefault(s => !s.Success)?.ReasonCode ?? "P7_NARRATION_AUTHORITY_FAILED";
}

public interface IPhase7NarrationAuthorityOrchestrator
{
    Task<Phase7NarrationAuthorityOrchestrationResult> ExecuteAsync(
        Phase7NarrationAuthorityOrchestrationRequest request,
        CancellationToken cancellationToken = default);
}
