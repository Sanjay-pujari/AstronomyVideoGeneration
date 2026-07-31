using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum QuestionEvidenceStatus { ResolvedGrounded, EditorialOnly, Mixed, Deferred, Rejected }

public sealed record DocumentarySourceLineage(string Phase1ExecutionId, string Phase1PlanId,
    string Phase2AuthorityPath, string Phase2SemanticChecksum, string? Phase2PhysicalChecksum,
    string CertifiedKnowledgeContextPath, string CertifiedKnowledgeContextChecksum,
    string Phase3QuestionBankPath, string Phase3QuestionBankChecksum,
    string Phase3LearningObjectivesPath, string Phase3LearningObjectivesChecksum,
    string Phase3QuestionPlanPath, string Phase3QuestionPlanChecksum,
    string Language, string ProfileId, string ProfileVersion);

public sealed record DocumentaryEditorialConstraint(string Code, string? Description = null);
public sealed record DocumentaryNarrativeSlot(string SlotId, int Order, string NarrativeStage, string SceneRole,
    string PurposeCode, IReadOnlyList<string> PreferredQuestionCategories, IReadOnlyList<string> AllowedQuestionCategories,
    IReadOnlyList<string> PreferredKnowledgeCategories, bool RequiredKnowledge, string ObjectiveTemplateCode,
    string OutcomeTemplateCode, string TransitionIntentCode, int DurationWeight, bool CanReusePrimaryQuestion,
    bool CanUseEditorialOnlyQuestion, string ClosingBehavior)
{
    /// <summary>Profile-owned values; the planner never derives these from display labels.</summary>
    public string VisualOpportunityIntent { get; init; } = "ProfileDefined";
    public string EditorialOutcome { get; init; } = "Editorial outcome to be resolved from the profile.";
    public bool CanConsolidateSupportingQuestions { get; init; }
    public EditorialPriority EditorialPriority { get; init; } = EditorialPriority.Medium;
    public string VisualOpportunityType { get; init; } = "HighLevelVisualOpportunity";
    public bool VisualIsScientificallyRequired { get; init; }
    public string ObjectiveCuriosityGoal { get; init; } = "Sustain the profile-defined viewer question.";
    public string ObjectiveEmotionalGoal { get; init; } = "Deliver the profile-defined editorial outcome.";
    public string TransitionNextQuestionSeed { get; init; } = "Continue the profile-defined learning journey.";
    public string TransitionEditorialDirection { get; init; } = "Follow the profile-defined transition intent.";
}
public sealed record DocumentaryVariantProfile(string Variant, bool Required, int ExpectedSceneCount,
    int MinimumSceneCount, int MaximumSceneCount, int DurationBudgetSeconds, int MinimumSceneDurationSeconds,
    int MaximumSceneDurationSeconds, IReadOnlyList<DocumentaryNarrativeSlot> NarrativeSlots,
    IReadOnlyList<string> RequiredQuestionCoverage, IReadOnlyList<string> RequiredKnowledgeCoverage,
    string TransitionPolicy)
{
    /// <summary>Question id to stable, profile-authorized deferral reason code.</summary>
    public IReadOnlyDictionary<string, string> AuthorizedHighQuestionDeferrals { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
}
public sealed record DocumentaryBlueprintProfile(string ProfileId, string ProfileVersion, string FamilyCode,
    string AudienceCode, DocumentaryVariantProfile LongProfile, DocumentaryVariantProfile ShortProfile,
    string QuestionCoveragePolicy, string KnowledgeCoveragePolicy, string EditorialSafetyPolicy);

public sealed record DocumentaryKnowledgeSelection(string KnowledgeSelectionId, string Variant,
    string SceneOpportunityId, string PrimaryViewerQuestionId, string KnowledgeReferenceId, string SourceArtifact,
    string SourcePointer, string SemanticChecksum, string PurposeCode, string SelectionReasonCode, bool IsPrimary,
    QuestionEvidenceStatus EvidenceStatus);
public sealed record DocumentaryQuestionCoverageRecord(string QuestionId, string Variant, string CoverageType,
    string SceneOpportunityId, string PrimaryQuestionId, string ReasonCode, string ProfilePermissionCode);
public sealed record DocumentaryQuestionDeferral(string QuestionId, string Variant,
    QuestionEvidenceStatus EvidenceStatus, string ReasonCode, string ProfilePermissionCode,
    string? ConsolidatedIntoQuestionId, string NotesCode);
public sealed record DocumentarySceneOpportunity(string OpportunityId, string Variant, int Order, string ProfileSlotId,
    string NarrativeStage, string SceneRole, string PurposeCode, string PrimaryViewerQuestionId,
    string PrimaryViewerQuestionText, IReadOnlyList<string> SupportingViewerQuestionIds,
    QuestionEvidenceStatus QuestionEvidenceStatus, string LearningObjectiveId, string LearningObjectiveText,
    string EditorialOutcomeCode, string EditorialOutcome, IReadOnlyList<DocumentaryKnowledgeSelection> SelectedKnowledgeReferences,
    IReadOnlyList<DocumentaryQuestionCoverageRecord> QuestionCoverageRecords,
    IReadOnlyList<DocumentaryEditorialConstraint> EditorialConstraints, IReadOnlyList<string> MustNotClaim,
    string TransitionIntent, int TargetDurationSeconds, int MinimumDurationSeconds, int MaximumDurationSeconds,
    string VisualOpportunityIntent, string DeterministicChecksum)
{
    public EditorialPriority EditorialPriority { get; init; } = EditorialPriority.Medium;
    public string ObjectiveCuriosityGoal { get; init; } = "Sustain the profile-defined viewer question.";
    public string ObjectiveEmotionalGoal { get; init; } = "Deliver the profile-defined editorial outcome.";
    public string VisualOpportunityType { get; init; } = "HighLevelVisualOpportunity";
    public bool VisualIsScientificallyRequired { get; init; }
    public string TransitionNextQuestionSeed { get; init; } = "Continue the profile-defined learning journey.";
    public string TransitionEditorialDirection { get; init; } = "Follow the profile-defined transition intent.";
}
public sealed record DocumentaryCoverageSummary(IReadOnlyList<string> CoveredQuestions,
    IReadOnlyList<string> EditorialQuestions, IReadOnlyList<string> DeferredQuestions,
    IReadOnlyList<string> ReusedQuestions, IReadOnlyList<string> ConsolidatedQuestions,
    IReadOnlyList<string> CoveredKnowledgeReferences);
public sealed record DocumentaryVariantIntent(string Variant, string VariantIntentId, string ProfileId,
    string ProfileVersion, int ExpectedSceneCount, int DurationBudgetSeconds,
    IReadOnlyList<DocumentarySceneOpportunity> SceneOpportunities, DocumentaryCoverageSummary QuestionCoverage,
    DocumentaryCoverageSummary KnowledgeCoverage, IReadOnlyList<DocumentaryQuestionDeferral> DeferredQuestions,
    IReadOnlyList<DocumentaryEditorialConstraint> EditorialConstraints, int TotalAllocatedDurationSeconds,
    string DeterministicChecksum);
public sealed record DocumentaryIntent(string SchemaVersion, string ContractVersion, string PlannerVersion,
    string IntentId, string ExecutionId, string PlanId, string EventId, string Language, string ProfileId,
    string ProfileVersion, string GeneratedFromPhase2Checksum, string GeneratedFromPhase3Checksum,
    DocumentarySourceLineage SourceLineage, string AudienceIntent, string DocumentaryGoal,
    IReadOnlyList<string> LearningJourney, IReadOnlyList<string> EditorialPriorities,
    IReadOnlyList<string> ScientificPriorities, string KnowledgePolicy, string QuestionPolicy,
    DocumentaryVariantIntent LongVariantIntent, DocumentaryVariantIntent ShortVariantIntent,
    DocumentaryCoverageSummary CoverageSummary, IReadOnlyList<DocumentaryEditorialConstraint> EditorialConstraints,
    string DeterministicChecksum);

public sealed record CertifiedDocumentaryKnowledgeReference(string ReferenceId, string SourceArtifact,
    string SourcePointer, string SemanticChecksum, string Category);
public sealed record DocumentaryIntentPlanningRequest(string ExecutionId, string PlanId, string EventId,
    string Language, DocumentarySourceLineage SourceLineage, ViewerQuestionBank QuestionBank,
    ViewerLearningObjectives LearningObjectives, ViewerQuestionPlan QuestionPlan,
    DocumentaryBlueprintProfile Profile, IReadOnlyList<CertifiedDocumentaryKnowledgeReference> CertifiedKnowledge,
    string AudienceIntent, string DocumentaryGoal);
public sealed record DocumentaryPlanningIssue(string Code, string Message);
public sealed record DocumentarySlotCandidateDiagnostic(string SlotId, string Variant, string QuestionId,
    bool VariantEligible, bool CategoryEligible, bool EditorialEligible, bool ReuseEligible,
    QuestionEvidenceStatus EvidenceStatus, bool KnowledgeEligible, bool ObjectiveAvailable,
    IReadOnlyList<string> RejectionReasons);
public sealed record DocumentaryIntentPlanningResult(bool Success, DocumentaryIntent? Intent,
    IReadOnlyList<DocumentaryPlanningIssue> Errors, IReadOnlyList<DocumentaryPlanningIssue> Warnings,
    string ProfileResolution, DocumentaryCoverageSummary? QuestionAllocationSummary,
    DocumentaryCoverageSummary? KnowledgeAllocationSummary, DocumentaryCoverageSummary? CoverageSummary,
    IReadOnlyList<string> DeterminismEvidence)
{
    public DocumentaryCoverageSummary? LongQuestionCoverage { get; init; }
    public DocumentaryCoverageSummary? ShortQuestionCoverage { get; init; }
    public DocumentaryCoverageSummary? LongKnowledgeCoverage { get; init; }
    public DocumentaryCoverageSummary? ShortKnowledgeCoverage { get; init; }
    public DocumentaryCoverageSummary? AggregateCoverage { get; init; }
    public IReadOnlyList<DocumentarySlotCandidateDiagnostic> CandidateDiagnostics { get; init; } = [];
}
public interface IDocumentaryIntentPlanner { DocumentaryIntentPlanningResult Plan(DocumentaryIntentPlanningRequest request); }
public interface IDocumentaryBlueprintProfileResolver { DocumentaryBlueprintProfile? Resolve(string profileId, string familyCode, string audienceCode); }
