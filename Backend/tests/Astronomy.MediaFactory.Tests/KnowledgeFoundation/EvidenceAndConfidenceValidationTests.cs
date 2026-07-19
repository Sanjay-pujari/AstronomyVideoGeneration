using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Validation;
using FluentAssertions;
using System.Reflection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class EvidenceAndConfidenceValidationTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validation_codes_are_stable_unique_prefixed_and_contain_no_stale_renames()
    {
        var expected = new Dictionary<string, string>
        {
            [nameof(AstronomyEvidenceValidationCodes.RecordIdMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.IdMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordTypeUndefined)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.TypeUndefined",
            [nameof(AstronomyEvidenceValidationCodes.RecordStatusUndefined)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.StatusUndefined",
            [nameof(AstronomyEvidenceValidationCodes.RecordSourceMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.SourceMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordSourceInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.SourceInvalid",
            [nameof(AstronomyEvidenceValidationCodes.RecordTemporalMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.TemporalMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordTemporalInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.TemporalInvalid",
            [nameof(AstronomyEvidenceValidationCodes.RecordAuditMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.AuditMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordAuditInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.AuditInvalid",
            [nameof(AstronomyEvidenceValidationCodes.RecordAttributionInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.AttributionInvalid",
            [nameof(AstronomyEvidenceValidationCodes.RecordExternalIdentifierMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.ExternalIdentifierMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordDuplicateExternalIdentifier)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.DuplicateExternalIdentifier",
            [nameof(AstronomyEvidenceValidationCodes.RecordTagMissing)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.TagMissing",
            [nameof(AstronomyEvidenceValidationCodes.RecordDuplicateTag)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.DuplicateTag",
            [nameof(AstronomyEvidenceValidationCodes.RecordTitleInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.TitleInvalid",
            [nameof(AstronomyEvidenceValidationCodes.RecordSummaryInvalid)] = "A2.KNOWLEDGE.EVIDENCE.RECORD.SummaryInvalid",
            [nameof(AstronomyEvidenceValidationCodes.SetKnowledgeIdMissing)] = "A2.KNOWLEDGE.EVIDENCE.SET.KnowledgeIdMissing",
            [nameof(AstronomyEvidenceValidationCodes.SetKnowledgeVersionInvalid)] = "A2.KNOWLEDGE.EVIDENCE.SET.KnowledgeVersionInvalid",
            [nameof(AstronomyEvidenceValidationCodes.SetAssociationCollectionMissing)] = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationCollectionMissing",
            [nameof(AstronomyEvidenceValidationCodes.SetAssociationMissing)] = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationMissing",
            [nameof(AstronomyEvidenceValidationCodes.SetAssociationOwnerMismatch)] = "A2.KNOWLEDGE.EVIDENCE.SET.AssociationOwnerMismatch",
            [nameof(AstronomyEvidenceValidationCodes.SetEvidenceIdMissing)] = "A2.KNOWLEDGE.EVIDENCE.SET.EvidenceIdMissing",
            [nameof(AstronomyEvidenceValidationCodes.SetRoleUndefined)] = "A2.KNOWLEDGE.EVIDENCE.SET.RoleUndefined",
            [nameof(AstronomyEvidenceValidationCodes.SetDuplicateEvidence)] = "A2.KNOWLEDGE.EVIDENCE.SET.DuplicateEvidence",
            [nameof(AstronomyEvidenceValidationCodes.SetMultiplePrimaryEvidence)] = "A2.KNOWLEDGE.EVIDENCE.SET.MultiplePrimaryEvidence",
            [nameof(AstronomyEvidenceValidationCodes.SetOrderingInvalid)] = "A2.KNOWLEDGE.EVIDENCE.SET.OrderingInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentIdMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.IdMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentKnowledgeIdMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.KnowledgeIdMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentKnowledgeVersionInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.KnowledgeVersionInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentLevelUndefined)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.LevelUndefined",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentScoreInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.ScoreInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentMethodUndefined)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.MethodUndefined",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentAssessorMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AssessorMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentAssessorInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AssessorInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentAuditMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AuditMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentAuditInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.AuditInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentEvidenceCollectionMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceCollectionMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentEvidenceOrderingInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceOrderingInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentEvidenceIdMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.EvidenceIdMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentDuplicateEvidence)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.DuplicateEvidence",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentFactorCollectionMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorCollectionMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentFactorMissing)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorMissing",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentFactorInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.FactorInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentDuplicateFactor)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.DuplicateFactor",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentRationaleInvalid)] = "A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.RationaleInvalid",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentUnknownLevelHasScore)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.UnknownLevelHasScore",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentNonUnknownLevelMissingEvidence)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.NonUnknownLevelMissingEvidence",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentMissingExplanation)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.NonUnknownLevelMissingExplanation",
            [nameof(AstronomyEvidenceValidationCodes.AssessmentMethodAssessorMismatch)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.MethodAssessorMismatch",
            [nameof(AstronomyEvidenceValidationCodes.ConsistencyStatementOwnerMismatch)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.StatementOwnerMismatch",
            [nameof(AstronomyEvidenceValidationCodes.ConsistencyReferencedEvidenceNotInSet)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.ReferencedEvidenceNotInSet",
            [nameof(AstronomyEvidenceValidationCodes.ConsistencyEvidenceSetEmpty)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.EvidenceSetEmpty",
            [nameof(AstronomyEvidenceValidationCodes.ConsistencyContradictingEvidenceNotReferenced)] = "A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.ContradictingEvidenceNotReferenced",
        };

        var actual = typeof(AstronomyEvidenceValidationCodes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);
        actual.Should().Equal(expected);
        actual.Values.Should().OnlyHaveUniqueItems();
        actual.Should().Contain(kv => kv.Key.StartsWith("Record", StringComparison.Ordinal) && kv.Value.StartsWith("A2.KNOWLEDGE.EVIDENCE.RECORD.", StringComparison.Ordinal));
        actual.Should().Contain(kv => kv.Key.StartsWith("Set", StringComparison.Ordinal) && kv.Value.StartsWith("A2.KNOWLEDGE.EVIDENCE.SET.", StringComparison.Ordinal));
        actual.Where(kv => kv.Key.StartsWith("Assessment", StringComparison.Ordinal) && !kv.Key.StartsWith("AssessmentUnknown", StringComparison.Ordinal) && !kv.Key.StartsWith("AssessmentNonUnknown", StringComparison.Ordinal) && kv.Key != nameof(AstronomyEvidenceValidationCodes.AssessmentMissingExplanation) && kv.Key != nameof(AstronomyEvidenceValidationCodes.AssessmentMethodAssessorMismatch)).Should().OnlyContain(kv => kv.Value.StartsWith("A2.KNOWLEDGE.CONFIDENCE.ASSESSMENT.", StringComparison.Ordinal));
        actual.Where(kv => kv.Key.StartsWith("Consistency", StringComparison.Ordinal)).Should().OnlyContain(kv => kv.Value.StartsWith("A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.", StringComparison.Ordinal));
        actual.Should().NotContainKey("ConsistencyEvidenceSetMissing");
        actual.Values.Should().NotContain("A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.EvidenceSetMissing");
    }

    [Fact]
    public void Valid_evidence_record_set_confidence_and_consistency_return_success()
    {
        new AstronomyEvidenceRecordValidator().Validate(Record()).Issues.Should().BeEmpty();
        var set = EvidenceSet([Assoc(EvidenceId.Create("evidence.a"), KnowledgeEvidenceRole.Primary), Assoc(EvidenceId.Create("evidence.b"), KnowledgeEvidenceRole.Contradicting)]);
        new AstronomyKnowledgeStatementEvidenceSetValidator().Validate(set).Issues.Should().BeEmpty();
        var assessment = Assessment(evidenceIds: [EvidenceId.Create("evidence.a"), EvidenceId.Create("evidence.b")]);
        new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(assessment).Issues.Should().BeEmpty();
        new AstronomyEvidenceConfidenceConsistencyValidator().Validate(assessment, set).Issues.Should().BeEmpty();
    }

    [Fact]
    public void Confidence_policy_reports_unknown_score_missing_evidence_missing_explanation_and_mismatch()
    {
        var validator = new AstronomyKnowledgeConfidenceAssessmentValidator();
        validator.Validate(Assessment(level: KnowledgeConfidenceLevel.Unknown, score: new KnowledgeConfidenceScore(.5))).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentUnknownLevelHasScore);
        validator.Validate(Assessment(evidenceIds: [])).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentNonUnknownLevelMissingEvidence);
        validator.Validate(Assessment(evidenceIds: [EvidenceId.Create("evidence.a")], factors: [], rationale: null)).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentMissingExplanation);
        validator.Validate(Assessment(method: ConfidenceAssessmentMethod.RuleBased, assessorType: ConfidenceAssessorType.HumanExpert)).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentMethodAssessorMismatch);
    }

    [Theory]
    [InlineData(ConfidenceAssessmentMethod.HumanExpertReview, ConfidenceAssessorType.HumanExpert)]
    [InlineData(ConfidenceAssessmentMethod.HumanEditorialReview, ConfidenceAssessorType.HumanEditor)]
    [InlineData(ConfidenceAssessmentMethod.RuleBased, ConfidenceAssessorType.AutomatedRule)]
    [InlineData(ConfidenceAssessmentMethod.StatisticalAnalysis, ConfidenceAssessorType.StatisticalModel)]
    [InlineData(ConfidenceAssessmentMethod.InstrumentDerived, ConfidenceAssessorType.InstrumentSystem)]
    [InlineData(ConfidenceAssessmentMethod.Imported, ConfidenceAssessorType.ExternalAuthority)]
    [InlineData(ConfidenceAssessmentMethod.Hybrid, ConfidenceAssessorType.HybridProcess)]
    [InlineData(ConfidenceAssessmentMethod.SourceConsensus, ConfidenceAssessorType.HumanExpert)]
    [InlineData(ConfidenceAssessmentMethod.SourceConsensus, ConfidenceAssessorType.AutomatedRule)]
    [InlineData(ConfidenceAssessmentMethod.SourceConsensus, ConfidenceAssessorType.StatisticalModel)]
    [InlineData(ConfidenceAssessmentMethod.SourceConsensus, ConfidenceAssessorType.HybridProcess)]
    public void Method_assessor_compatibility_accepts_defined_pairs(ConfidenceAssessmentMethod method, ConfidenceAssessorType assessorType)
        => new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(Assessment(method: method, assessorType: assessorType)).Issues.Should().NotContain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentMethodAssessorMismatch);

    [Fact]
    public void Consistency_reports_owner_missing_reference_and_omitted_contradiction()
    {
        var set = EvidenceSet([Assoc(EvidenceId.Create("evidence.a"), KnowledgeEvidenceRole.Supporting), Assoc(EvidenceId.Create("evidence.c"), KnowledgeEvidenceRole.Contradicting)]);
        var assessment = Assessment(knowledgeId: KnowledgeId.Create("knowledge.other"), evidenceIds: [EvidenceId.Create("evidence.missing")]);
        var issues = new AstronomyEvidenceConfidenceConsistencyValidator().Validate(assessment, set).Issues;
        issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyStatementOwnerMismatch);
        issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyReferencedEvidenceNotInSet);
        issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyContradictingEvidenceNotReferenced && i.Severity == DomainValidationSeverity.Warning);
    }

    [Fact]
    public void Determinism_no_mutation_and_null_arguments_are_enforced()
    {
        var associations = new[] { Assoc(EvidenceId.Create("evidence.b"), KnowledgeEvidenceRole.Supporting), Assoc(EvidenceId.Create("evidence.a"), KnowledgeEvidenceRole.Primary) };
        var set = EvidenceSet(associations);
        var validator = new AstronomyKnowledgeStatementEvidenceSetValidator();
        var first = validator.Validate(set).Issues.Select(i => i.Code + "|" + i.Path).ToArray();
        var second = validator.Validate(set).Issues.Select(i => i.Code + "|" + i.Path).ToArray();
        first.Should().Equal(second);
        set.Associations.Select(a => a.EvidenceId.Value).Should().Equal("evidence.a", "evidence.b");
        Assert.Throws<ArgumentNullException>(() => new AstronomyEvidenceRecordValidator().Validate(null!));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatementEvidenceSetValidator().Validate(null!));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(null!));
        Assert.Throws<ArgumentNullException>(() => new AstronomyEvidenceConfidenceConsistencyValidator().Validate(null!, set));
    }

    [Fact]
    public void Evidence_validation_scope_has_no_task_22e_or_infrastructure_leakage()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Evidence/Validation"));
        var text = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        text.Should().NotMatchRegex(@"using\s+Astronomy\.MediaFactory\.(Infrastructure|Publishing|Rendering|Api|AIOptimization|ContentGen|Persistence)\b");
        var forbiddenTerms = new[] { "Confidence" + "Calculator", "Confidence" + "Engine", "Weighted" + "Average", "Evidence" + "Weight", "Bay" + "esian", "Score" + "Threshold", "Json" + "Converter", "Json" + "SerializerOptions", "I" + "ServiceCollection", "Db" + "Context", "I" + "Queryable", "Http" + "Client", "Open" + "AI", "Machine" + "Learning", "Certification" + "Coordinator", "DateTimeOffset" + ".UtcNow" };
        foreach (var term in forbiddenTerms) text.Should().NotContain(term);
    }

    [Fact]
    public void Confidence_evidence_ordering_is_constructor_enforced_and_not_reported_as_missing_collection()
    {
        var assessment = Assessment(evidenceIds: [EvidenceId.Create("evidence.b"), EvidenceId.Create("evidence.a")]);

        assessment.EvidenceIds.Select(id => id.Value).Should().Equal("evidence.a", "evidence.b");
        new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(assessment).Issues.Should().NotContain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentEvidenceCollectionMissing);
    }

    [Fact]
    public void Consistency_reports_empty_evidence_set_for_non_unknown_but_not_unknown_confidence()
    {
        var validator = new AstronomyEvidenceConfidenceConsistencyValidator();
        validator.Validate(Assessment(evidenceIds: []), EvidenceSet([])).Issues.Should().ContainSingle(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyEvidenceSetEmpty && i.Severity == DomainValidationSeverity.Error);
        validator.Validate(Assessment(level: KnowledgeConfidenceLevel.Unknown, evidenceIds: [], factors: [], rationale: null), EvidenceSet([])).Issues.Should().NotContain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyEvidenceSetEmpty);
    }

    [Fact]
    public void Consistency_reports_missing_references_with_indexed_paths_and_keeps_deterministic_order()
    {
        var set = EvidenceSet([Assoc(EvidenceId.Create("evidence.z"), KnowledgeEvidenceRole.Supporting)]);
        var assessment = Assessment(evidenceIds: [EvidenceId.Create("evidence.a"), EvidenceId.Create("evidence.b")]);

        var issues = new AstronomyEvidenceConfidenceConsistencyValidator().Validate(assessment, set).Issues;

        issues.Where(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyReferencedEvidenceNotInSet).Select(i => i.Path).Should().Equal("evidenceIds[0]", "evidenceIds[1]");
        issues.Select(i => i.Code + "|" + i.Path).Should().Equal(new AstronomyEvidenceConfidenceConsistencyValidator().Validate(assessment, set).Issues.Select(i => i.Code + "|" + i.Path));
    }

    [Fact]
    public void Consistency_contradicting_reference_warning_depends_on_assessment_evidence_ids()
    {
        var set = EvidenceSet([Assoc(EvidenceId.Create("evidence.a"), KnowledgeEvidenceRole.Supporting), Assoc(EvidenceId.Create("evidence.c"), KnowledgeEvidenceRole.Contradicting)]);
        new AstronomyEvidenceConfidenceConsistencyValidator().Validate(Assessment(evidenceIds: [EvidenceId.Create("evidence.a")]), set).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyContradictingEvidenceNotReferenced && i.Severity == DomainValidationSeverity.Warning);
        new AstronomyEvidenceConfidenceConsistencyValidator().Validate(Assessment(evidenceIds: [EvidenceId.Create("evidence.a"), EvidenceId.Create("evidence.c")]), set).Issues.Should().NotContain(i => i.Code == AstronomyEvidenceValidationCodes.ConsistencyContradictingEvidenceNotReferenced);
    }

    private static AstronomyEvidenceRecord Record() => new(EvidenceId.Create("evidence.a"), AstronomyEvidenceType.Observation, Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.EvidenceFoundationStatus.Verified, new AstronomyEvidenceSourceReference("source.nasa", AstronomyEvidenceSourceType.SpaceAgency, "NASA", new Uri("https://example.test/evidence")), new EvidenceTemporalMetadata(publishedAtUtc: Created, retrievedAtUtc: Created.AddHours(1)), new KnowledgeAuditMetadata(Created, "author"), new EvidenceAttribution(["Contributor"]), "Title", "Summary", [new EvidenceExternalIdentifier("doi", "10.test/example")], [new KnowledgeTag("moon")]);
    private static KnowledgeStatementEvidenceReference Assoc(EvidenceId evidenceId, KnowledgeEvidenceRole role) => new(KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, evidenceId, role);
    private static AstronomyKnowledgeStatementEvidenceSet EvidenceSet(IEnumerable<KnowledgeStatementEvidenceReference> associations) => new(KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, associations);
    private static AstronomyKnowledgeConfidenceAssessment Assessment(KnowledgeId? knowledgeId = null, KnowledgeConfidenceLevel level = KnowledgeConfidenceLevel.High, KnowledgeConfidenceScore? score = null, ConfidenceAssessmentMethod method = ConfidenceAssessmentMethod.HumanExpertReview, ConfidenceAssessorType assessorType = ConfidenceAssessorType.HumanExpert, IEnumerable<EvidenceId>? evidenceIds = null, IEnumerable<ConfidenceAssessmentFactor>? factors = null, string? rationale = "Reviewed against supplied evidence.") => new(ConfidenceAssessmentId.Create("assessment.moon"), knowledgeId ?? KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, level, score, method, new ConfidenceAssessorReference("assessor.one", assessorType, "Assessor One"), new KnowledgeAuditMetadata(Created, "author"), evidenceIds ?? [EvidenceId.Create("evidence.a")], factors ?? [new ConfidenceAssessmentFactor("source-quality", ConfidenceFactorDirection.Supports)], rationale);
}
