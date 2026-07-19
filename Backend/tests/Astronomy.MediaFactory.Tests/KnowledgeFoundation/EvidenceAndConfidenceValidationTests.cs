using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Validation;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class EvidenceAndConfidenceValidationTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validation_codes_are_stable()
    {
        AstronomyEvidenceValidationCodes.RecordIdMissing.Should().Be("A2.KNOWLEDGE.EVIDENCE.RECORD.IdMissing");
        AstronomyEvidenceValidationCodes.SetDuplicateEvidence.Should().Be("A2.KNOWLEDGE.EVIDENCE.SET.DuplicateEvidence");
        AstronomyEvidenceValidationCodes.AssessmentUnknownLevelHasScore.Should().Be("A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.UnknownLevelHasScore");
        AstronomyEvidenceValidationCodes.ConsistencyContradictingEvidenceNotReferenced.Should().Be("A2.KNOWLEDGE.CONFIDENCE.CONSISTENCY.ContradictingEvidenceNotReferenced");
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

    private static AstronomyEvidenceRecord Record() => new(EvidenceId.Create("evidence.a"), AstronomyEvidenceType.Observation, Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.EvidenceFoundationStatus.Verified, new AstronomyEvidenceSourceReference("source.nasa", AstronomyEvidenceSourceType.SpaceAgency, "NASA", new Uri("https://example.test/evidence")), new EvidenceTemporalMetadata(publishedAtUtc: Created, retrievedAtUtc: Created.AddHours(1)), new KnowledgeAuditMetadata(Created, "author"), new EvidenceAttribution(["Contributor"]), "Title", "Summary", [new EvidenceExternalIdentifier("doi", "10.test/example")], [new KnowledgeTag("moon")]);
    private static KnowledgeStatementEvidenceReference Assoc(EvidenceId evidenceId, KnowledgeEvidenceRole role) => new(KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, evidenceId, role);
    private static AstronomyKnowledgeStatementEvidenceSet EvidenceSet(IEnumerable<KnowledgeStatementEvidenceReference> associations) => new(KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, associations);
    private static AstronomyKnowledgeConfidenceAssessment Assessment(KnowledgeId? knowledgeId = null, KnowledgeConfidenceLevel level = KnowledgeConfidenceLevel.High, KnowledgeConfidenceScore? score = null, ConfidenceAssessmentMethod method = ConfidenceAssessmentMethod.HumanExpertReview, ConfidenceAssessorType assessorType = ConfidenceAssessorType.HumanExpert, IEnumerable<EvidenceId>? evidenceIds = null, IEnumerable<ConfidenceAssessmentFactor>? factors = null, string? rationale = "Reviewed against supplied evidence.") => new(ConfidenceAssessmentId.Create("assessment.moon"), knowledgeId ?? KnowledgeId.Create("knowledge.moon"), KnowledgeVersion.Initial, level, score, method, new ConfidenceAssessorReference("assessor.one", assessorType, "Assessor One"), new KnowledgeAuditMetadata(Created, "author"), evidenceIds ?? [EvidenceId.Create("evidence.a")], factors ?? [new ConfidenceAssessmentFactor("source-quality", ConfidenceFactorDirection.Supports)], rationale);
}
