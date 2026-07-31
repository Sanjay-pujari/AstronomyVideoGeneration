namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record DocumentarySceneBlueprintTraceability(
    string SceneId, string SourceOpportunityId, string SourceOpportunityChecksum,
    string PrimaryViewerQuestionId, IReadOnlyList<string> SupportingViewerQuestionIds,
    string LearningObjectiveId, QuestionEvidenceStatus QuestionEvidenceStatus,
    string ProfileSlotId, int MinimumDurationSeconds, int MaximumDurationSeconds,
    IReadOnlyList<DocumentaryEditorialConstraint> EditorialConstraints,
    IReadOnlyList<string> MustNotClaim,
    IReadOnlyList<DocumentaryKnowledgeSelection> KnowledgeSelections);

public sealed record DocumentaryBlueprintVariantArtifact(
    string SchemaVersion, string ContractVersion, string ProjectionVersion, string VariantArtifactId,
    string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string ProfileVersion, string Variant,
    string SourceIntentId, string SourceVariantIntentId, string SourceIntentChecksum,
    string SourceVariantIntentChecksum, DocumentarySourceLineage SourceLineage,
    DocumentaryBlueprint Blueprint, IReadOnlyList<DocumentarySceneBlueprintTraceability> SceneTraceability,
    DocumentaryCoverageSummary QuestionCoverage, DocumentaryCoverageSummary KnowledgeCoverage,
    IReadOnlyList<DocumentaryQuestionDeferral> DeferredQuestions,
    IReadOnlyList<DocumentaryEditorialConstraint> EditorialConstraints,
    int ExpectedSceneCount, int ActualSceneCount, int DurationBudgetSeconds,
    int TotalAllocatedDurationSeconds, string DeterministicChecksum);

public sealed record DocumentaryBlueprintAggregateCoverage(
    IReadOnlyList<string> CoveredQuestions, IReadOnlyList<string> EditorialQuestions,
    IReadOnlyList<string> DeferredQuestions, IReadOnlyList<string> CoveredKnowledgeReferences);

public sealed record DocumentaryBlueprintAggregateDurationSummary(
    int LongDurationSeconds, int ShortDurationSeconds, int TotalDurationSeconds);

public sealed record DocumentaryBlueprintProjectionDiagnostic(string Code, string Message);

public sealed record DocumentaryBlueprintAggregate(
    string SchemaVersion, string ContractVersion, string ProjectionVersion, string AggregateId,
    string ExecutionId, string PlanId, string EventId, string Language,
    string ProfileId, string ProfileVersion, string SourceIntentId, string SourceIntentChecksum,
    DocumentarySourceLineage SourceLineage, DocumentaryBlueprint LongBlueprint,
    DocumentaryBlueprint ShortBlueprint, string LongProjectionChecksum, string ShortProjectionChecksum,
    DocumentaryBlueprintAggregateCoverage AggregateCoverage,
    DocumentaryBlueprintAggregateDurationSummary AggregateDurationSummary,
    IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> ProjectionDiagnostics,
    string DeterministicChecksum);

public sealed record DocumentaryBlueprintProjectionRequest(DocumentaryIntent Intent, DocumentaryBlueprintProfile Profile);

public sealed record DocumentaryBlueprintProjectionResult(
    bool Success, DocumentaryBlueprintAggregate? Aggregate,
    DocumentaryBlueprintVariantArtifact? LongArtifact, DocumentaryBlueprintVariantArtifact? ShortArtifact,
    IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> Errors,
    IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> Warnings,
    int LongSceneCount, int ShortSceneCount, int LongDurationSeconds, int ShortDurationSeconds,
    bool QuestionReconciliationPassed, bool ObjectiveReconciliationPassed,
    bool KnowledgeReconciliationPassed, bool SafetyReconciliationPassed,
    bool DurationReconciliationPassed, bool TransitionReconciliationPassed,
    bool VariantIndependencePassed, bool ChecksumValidationPassed,
    IReadOnlyList<string> ProjectionEvidence);

public interface IDocumentaryBlueprintProjector
{
    DocumentaryBlueprintProjectionResult Project(DocumentaryBlueprintProjectionRequest request);
}
