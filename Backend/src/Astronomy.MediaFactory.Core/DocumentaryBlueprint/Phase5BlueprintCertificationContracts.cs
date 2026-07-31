using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record BlueprintSceneIntent(string Variant, string SceneId, int Sequence,
    DocumentaryNarrativeStage NarrativeStage, DocumentarySceneRole SceneRole, string ViewerQuestionId,
    string LearningObjectiveId, EditorialOutcome EditorialOutcome, IReadOnlyList<string> KnowledgeReferenceIds,
    int EstimatedDurationSeconds, SceneTransition TransitionIntent, string SourceAggregateId,
    string SourceAggregateChecksum, string SourceProjectionChecksum);

public sealed record BlueprintSceneIntentProjection(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourcePhase4AggregateId, string SourceAggregateChecksum,
    string SourceLongChecksum, string SourceShortChecksum, string ContractVersion,
    IReadOnlyList<BlueprintSceneIntent> Scenes, string SemanticChecksum);

public sealed record BlueprintVariantCoverageResult(string Variant, bool QuestionsCovered,
    bool LearningObjectivesCovered, bool KnowledgeSelectionsUsed, bool NarrativeStagesCovered,
    bool HasUnresolvedIds, bool HasOrphanReferences, bool DeferralsValid,
    bool KnowledgeDuplicationValid, bool IsValid, IReadOnlyList<string> Issues);

public sealed record BlueprintCoverageReport(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourcePhase4AggregateId, string SourceAggregateChecksum,
    string SourceLongChecksum, string SourceShortChecksum, string ContractVersion,
    IReadOnlyList<BlueprintVariantCoverageResult> Variants, bool IsValid, string SemanticChecksum);

public sealed record BlueprintVariantTransitionResult(string Variant, bool ValidOpening, bool ValidClosing,
    bool ContiguousProgression, bool StageProgressionValid, bool NoAbruptTopicShift,
    bool TransitionConsistencyValid, bool IntermediateHandoffsValid, bool IsValid,
    IReadOnlyList<string> Issues);

public sealed record BlueprintTransitionReport(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourcePhase4AggregateId, string SourceAggregateChecksum,
    string SourceLongChecksum, string SourceShortChecksum, string ContractVersion,
    IReadOnlyList<BlueprintVariantTransitionResult> Variants, bool IsValid, string SemanticChecksum);

public sealed record BlueprintPauseTestSceneResult(string Variant, string SceneId, int Sequence,
    bool Passed, IReadOnlyDictionary<string, bool> RuleResults, IReadOnlyList<string> Issues);

public sealed record BlueprintPauseTestReport(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourcePhase4AggregateId, string SourceAggregateChecksum,
    string SourceLongChecksum, string SourceShortChecksum, string ContractVersion,
    IReadOnlyList<BlueprintPauseTestSceneResult> Scenes, int PassedSceneCount, int FailedSceneCount,
    bool IsValid, string SemanticChecksum);

public sealed record BlueprintVariantValidation(string Variant, int SceneCount, int DurationSeconds,
    bool StructureValid, bool SceneIdsValid, bool SequenceValid, bool ProfileValid,
    bool QuestionLineageValid, bool LearningObjectiveLineageValid, bool KnowledgeLineageValid,
    IReadOnlyList<DocumentaryBlueprintValidationFinding> EditorialFindings, bool IsValid);

public sealed record BlueprintValidationReport(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, string SourcePhase4AggregateId, string SourceAggregateChecksum,
    string SourceLongChecksum, string SourceShortChecksum, string ContractVersion,
    IReadOnlyList<BlueprintVariantValidation> Variants, bool LongShortDistinct, bool CoverageValid,
    bool TransitionsValid, bool PauseTestValid, IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Warnings, bool OverallValid, string SemanticChecksum);

public sealed record PublishedBlueprintCertification(DocumentaryBlueprintCertification Certification,
    DocumentaryBlueprintEditorialContract EditorialContract, BlueprintValidationReport Validation,
    BlueprintSceneIntentProjection SceneIntents, BlueprintCoverageReport Coverage,
    BlueprintTransitionReport Transitions, BlueprintPauseTestReport PauseTest,
    string SourceAggregateId, string SourceAggregateChecksum, string ContractVersion,
    string SemanticChecksum);

public sealed record Phase5ArtifactInventoryEntry(string RelativePath, string ArtifactRole,
    string? SemanticChecksum, string PhysicalSha256, long Size, string SourcePhase4Checksum);

public sealed record Phase5CommittedStateEvaluation(bool IsValid, string ReasonCode,
    IReadOnlyList<string> Errors, IReadOnlyList<Phase5ArtifactInventoryEntry> Artifacts,
    PublishedBlueprintCertification? PublishedAuthority);

public static class Phase5SemanticChecksum
{
    public static string Calculate<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
