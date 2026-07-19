namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Validation;

public static class AstronomyEvidenceValidationCodes
{
    public const string RecordIdMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.IdMissing";
    public const string RecordTypeUndefined = "A2.KNOWLEDGE.EVIDENCE.RECORD.TypeUndefined";
    public const string RecordStatusUndefined = "A2.KNOWLEDGE.EVIDENCE.RECORD.StatusUndefined";
    public const string RecordSourceMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.SourceMissing";
    public const string RecordSourceInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.SourceInvalid";
    public const string RecordTemporalMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.TemporalMissing";
    public const string RecordTemporalInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.TemporalInvalid";
    public const string RecordAuditMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.AuditMissing";
    public const string RecordAuditInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.AuditInvalid";
    public const string RecordAttributionInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.AttributionInvalid";
    public const string RecordExternalIdentifierMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.ExternalIdentifierMissing";
    public const string RecordDuplicateExternalIdentifier = "A2.KNOWLEDGE.EVIDENCE.RECORD.DuplicateExternalIdentifier";
    public const string RecordTagMissing = "A2.KNOWLEDGE.EVIDENCE.RECORD.TagMissing";
    public const string RecordDuplicateTag = "A2.KNOWLEDGE.EVIDENCE.RECORD.DuplicateTag";
    public const string RecordTitleInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.TitleInvalid";
    public const string RecordSummaryInvalid = "A2.KNOWLEDGE.EVIDENCE.RECORD.SummaryInvalid";

    public const string SetKnowledgeIdMissing = "A2.KNOWLEDGE.EVIDENCE.SET.KnowledgeIdMissing";
    public const string SetKnowledgeVersionInvalid = "A2.KNOWLEDGE.EVIDENCE.SET.KnowledgeVersionInvalid";
    public const string SetAssociationCollectionMissing = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationCollectionMissing";
    public const string SetAssociationMissing = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationMissing";
    public const string SetAssociationOwnerMismatch = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationOwnerMismatch";
    public const string SetEvidenceIdMissing = "A2.KNOWLEDGE.EVIDENCE.SET.EvidenceIdMissing";
    public const string SetRoleUndefined = "A2.KNOWLEDGE.EVIDENCE.SET.RoleUndefined";
    public const string SetDuplicateEvidence = "A2.KNOWLEDGE.EVIDENCE.SET.DuplicateEvidence";
    public const string SetMultiplePrimaryEvidence = "A2.KNOWLEDGE.EVIDENCE.SET.MultiplePrimaryEvidence";
    public const string SetOrderingInvalid = "A2.KNOWLEDGE.EVIDENCE.SET.OrderingInvalid";

    public const string AssessmentIdMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.IdMissing";
    public const string AssessmentKnowledgeIdMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.KnowledgeIdMissing";
    public const string AssessmentKnowledgeVersionInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.KnowledgeVersionInvalid";
    public const string AssessmentLevelUndefined = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.LevelUndefined";
    public const string AssessmentScoreInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.ScoreInvalid";
    public const string AssessmentMethodUndefined = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.MethodUndefined";
    public const string AssessmentAssessorMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AssessorMissing";
    public const string AssessmentAssessorInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AssessorInvalid";
    public const string AssessmentAuditMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AuditMissing";
    public const string AssessmentAuditInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AuditInvalid";
    public const string AssessmentEvidenceCollectionMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceCollectionMissing";
    public const string AssessmentEvidenceOrderingInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceOrderingInvalid";
    public const string AssessmentEvidenceIdMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceIdMissing";
    public const string AssessmentDuplicateEvidence = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.DuplicateEvidence";
    public const string AssessmentFactorCollectionMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorCollectionMissing";
    public const string AssessmentFactorMissing = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorMissing";
    public const string AssessmentFactorInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorInvalid";
    public const string AssessmentDuplicateFactor = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.DuplicateFactor";
    public const string AssessmentRationaleInvalid = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.RationaleInvalid";
    public const string AssessmentUnknownLevelHasScore = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.UnknownLevelHasScore";
    public const string AssessmentNonUnknownLevelMissingEvidence = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.NonUnknownLevelMissingEvidence";
    public const string AssessmentMissingExplanation = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.NonUnknownLevelMissingExplanation";
    public const string AssessmentMethodAssessorMismatch = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.MethodAssessorMismatch";

    public const string ConsistencyStatementOwnerMismatch = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.StatementOwnerMismatch";
    public const string ConsistencyReferencedEvidenceNotInSet = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.ReferencedEvidenceNotInSet";
    public const string ConsistencyEvidenceSetEmpty = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.EvidenceSetEmpty";
    public const string ConsistencyContradictingEvidenceNotReferenced = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.ContradictingEvidenceNotReferenced";
}
