using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record DocumentaryBlueprintIntegrationRequest(
    string ExecutionId, string EventId, string EventTitle, string Language, string Profile,
    ViewerQuestionBank QuestionBank, ViewerLearningObjectives LearningObjectives,
    ViewerQuestionPlan QuestionPlan, ProductionEventIntelligence ProductionIntelligence,
    IReadOnlyList<string> RequestedVariants);

public sealed record BlueprintArtifactMetadata(string ExecutionId, string EventId, string Language, string Profile,
    string Variant, string Version, string Checksum, DateTimeOffset CreatedUtc, string SourcePhase3Checksum,
    string SourceProductionIntelligenceChecksum);

public sealed record BlueprintCoverage(IReadOnlyList<string> CoveredViewerQuestionIds,
    IReadOnlyList<string> DeferredViewerQuestionIds, IReadOnlyList<string> UncoveredViewerQuestionIds,
    IReadOnlyList<string> CoveredLearningObjectiveIds, IReadOnlyList<string> DeferredLearningObjectiveIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SectionQuestionMap,
    IReadOnlyDictionary<string, IReadOnlyList<ViewerKnowledgeReference>> SectionKnowledgeMap,
    IReadOnlyDictionary<string, string> DeferralReasons);

public sealed record DocumentaryBlueprintArtifact(BlueprintArtifactMetadata Metadata, DocumentaryBlueprint Blueprint,
    BlueprintCoverage Coverage, IReadOnlyList<string> Warnings);

public sealed record BlueprintBuildDiagnostics(string BuilderType, string BuilderVersion, string IntegrationServiceType,
    IReadOnlyList<string> SourceArtifactPaths, IReadOnlyDictionary<string, string> SourceChecksums,
    int QuestionCount, int LearningObjectiveCount, int MasterSectionCount, int LongSectionCount, int ShortSectionCount,
    BlueprintCoverage QuestionAndObjectiveCoverage, int KnowledgeReferenceCount,
    IReadOnlyList<string> EditorialAttentionInputs, IReadOnlyList<string> CompatibilityFallbacksUsed,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> DeterministicIdentityInputs, long BuildDurationMilliseconds);

public sealed record DocumentaryBlueprintIntegrationResult(DocumentaryBlueprintArtifact Master,
    DocumentaryBlueprintArtifact Long, DocumentaryBlueprintArtifact Short, BlueprintBuildDiagnostics Diagnostics);

public interface IDocumentaryBlueprintIntegrationService
{
    Task<DocumentaryBlueprintIntegrationResult> BuildAsync(DocumentaryBlueprintIntegrationRequest request, CancellationToken cancellationToken);
}
