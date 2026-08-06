namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class Phase7NarrationAuthorityOrchestrationReasonCodes
{
    public const string Completed = "P7_NARRATION_AUTHORITY_COMPLETED";
    public const string ReuseValid = "P7_NARRATION_AUTHORITY_REUSE_VALID";
    public const string UnhandledFailure = "P7_NARRATION_AUTHORITY_UNHANDLED_FAILURE";
    public const string RuntimeAuthorityReady = "P7_NARRATION_RUNTIME_AUTHORITY_READY";
}

public enum Phase7NarrationAuthorityExecutionTarget
{
    ThroughCommittedPlanning = 0,
    ThroughDeterministicDraftAuthority = 1
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
    public Phase7NarrationAuthorityExecutionTarget ExecutionTarget { get; init; }
        = Phase7NarrationAuthorityExecutionTarget.ThroughDeterministicDraftAuthority;
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
    public int TotalGateCount { get; init; }
    public IReadOnlyList<Phase7ScenePacketFailureSummary> PacketFailureSummaries { get; init; } = [];
    public IReadOnlyList<NarrationDraftSceneFailureSummary> DraftSceneFailureSummaries { get; init; } = [];
}

public sealed record Phase7ProviderIsolationSnapshot(DateTimeOffset CapturedAtUtc);
public sealed record Phase7ProviderIsolationEvidence(bool RuntimeCountersAvailable, bool ProviderDependenciesInjected,
    bool ProviderInvocationDetected, int AzureOpenAiCalls, int PromptComposerCalls, int NarrationGeneratorCalls,
    int TranslationCalls, int AzureSpeechCalls, int TtsCalls, int RenderingCalls);
public interface IPhase7ProviderIsolationAudit
{
    Phase7ProviderIsolationSnapshot CaptureStart();
    Phase7ProviderIsolationEvidence Complete(Phase7ProviderIsolationSnapshot start);
}

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
    public bool KnowledgeAuthorityReused => StageResults.Any(s => s.StageCode == "KnowledgeAuthority" && s.Reused);
    public bool PlanningAuthorityReused => StageResults.Any(s => s.StageCode == "NarrationPlanningPublication" && s.Reused);
    public bool EntirePhysicalAuthorityReused => KnowledgeAuthorityReused && PlanningAuthorityReused;
    public bool DraftValidationPassed => DraftGateStatuses.Count > 0 && DraftGateStatuses.All(g => g.Passed);
    public int DraftPassedGateCount => DraftGateStatuses.Count(g => g.Passed);
    public int DraftFailedGateCount => DraftGateStatuses.Count(g => !g.Passed);
    public Phase7NarrationAuthorityExecutionTarget RequestedTarget { get; init; }
        = Phase7NarrationAuthorityExecutionTarget.ThroughDeterministicDraftAuthority;
    public Phase7NarrationAuthorityExecutionTarget? CompletedTarget { get; init; }
    public bool RuntimeAuthorityReady { get; init; }
    public string DraftValidationStatus { get; init; } = "NotRun";
    public string ReasonCode { get; init; } = ResolveReasonCode(Success, StageResults);

    public static string ResolveReasonCodeForResult(bool success, IReadOnlyList<Phase7AuthorityStageResult> stageResults)
    {
        if (!success)
        {
            return stageResults.LastOrDefault(s => !s.Success)?.ReasonCode ?? "P7_NARRATION_AUTHORITY_FAILED";
        }

        var knowledgeAuthorityReused = stageResults.Any(s => s.StageCode == "KnowledgeAuthority" && s.Reused);
        var planningAuthorityReused = stageResults.Any(s => s.StageCode == "NarrationPlanningPublication" && s.Reused);

        return knowledgeAuthorityReused && planningAuthorityReused
            ? Phase7NarrationAuthorityOrchestrationReasonCodes.ReuseValid
            : Phase7NarrationAuthorityOrchestrationReasonCodes.Completed;
    }

    private static string ResolveReasonCode(bool success, IReadOnlyList<Phase7AuthorityStageResult> stageResults) =>
        ResolveReasonCodeForResult(success, stageResults);
}

public interface IPhase7NarrationAuthorityOrchestrator
{
    Task<Phase7NarrationAuthorityOrchestrationResult> ExecuteAsync(
        Phase7NarrationAuthorityOrchestrationRequest request,
        CancellationToken cancellationToken = default);
}
