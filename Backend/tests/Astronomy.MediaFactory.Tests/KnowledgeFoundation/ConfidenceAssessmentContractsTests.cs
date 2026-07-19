using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class ConfidenceAssessmentContractsTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Confidence_taxonomies_match_task_2_2c_boundary_and_guards_reject_undefined_values()
    {
        Assert.Equal(["Unknown", "VeryLow", "Low", "Moderate", "High", "VeryHigh"], Enum.GetNames<KnowledgeConfidenceLevel>());
        Assert.Equal(["HumanExpertReview", "HumanEditorialReview", "RuleBased", "StatisticalAnalysis", "InstrumentDerived", "SourceConsensus", "Hybrid", "Imported"], Enum.GetNames<ConfidenceAssessmentMethod>());
        Assert.Equal(["HumanExpert", "HumanEditor", "AutomatedRule", "StatisticalModel", "InstrumentSystem", "ExternalAuthority", "HybridProcess"], Enum.GetNames<ConfidenceAssessorType>());
        Assert.Equal(["Supports", "Reduces", "Neutral"], Enum.GetNames<ConfidenceFactorDirection>());
        Assert.Equal(Enum.GetValues<KnowledgeConfidenceLevel>().Length, Enum.GetValues<KnowledgeConfidenceLevel>().Distinct().Count());
        Assert.Equal(Enum.GetValues<ConfidenceAssessmentMethod>().Length, Enum.GetValues<ConfidenceAssessmentMethod>().Distinct().Count());
        Assert.Equal(Enum.GetValues<ConfidenceAssessorType>().Length, Enum.GetValues<ConfidenceAssessorType>().Distinct().Count());
        Assert.Equal(Enum.GetValues<ConfidenceFactorDirection>().Length, Enum.GetValues<ConfidenceFactorDirection>().Distinct().Count());
        foreach (var value in Enum.GetValues<KnowledgeConfidenceLevel>()) Assert.Equal(value, ConfidenceAssessmentEnumGuard.RequireDefined(value));
        foreach (var value in Enum.GetValues<ConfidenceAssessmentMethod>()) Assert.Equal(value, ConfidenceAssessmentEnumGuard.RequireDefined(value));
        foreach (var value in Enum.GetValues<ConfidenceAssessorType>()) Assert.Equal(value, ConfidenceAssessmentEnumGuard.RequireDefined(value));
        foreach (var value in Enum.GetValues<ConfidenceFactorDirection>()) Assert.Equal(value, ConfidenceAssessmentEnumGuard.RequireDefined(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceAssessmentEnumGuard.RequireDefined((KnowledgeConfidenceLevel)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceAssessmentEnumGuard.RequireDefined((ConfidenceAssessmentMethod)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceAssessmentEnumGuard.RequireDefined((ConfidenceAssessorType)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceAssessmentEnumGuard.RequireDefined((ConfidenceFactorDirection)999));
    }

    [Fact]
    public void Confidence_score_is_bounded_finite_normalized_value_with_optional_assessment_semantics()
    {
        Assert.Equal(0d, new KnowledgeConfidenceScore(0d).Value);
        Assert.Equal(1d, new KnowledgeConfidenceScore(1d).Value);
        Assert.Equal(.5d, new KnowledgeConfidenceScore(.5d).Value);
        Assert.Equal(new KnowledgeConfidenceScore(.5d), KnowledgeConfidenceScore.FromNormalizedValue(.5d));
        Assert.Equal("0.5", new KnowledgeConfidenceScore(.5d).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeConfidenceScore(-.01d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeConfidenceScore(1.01d));
        Assert.Throws<ArgumentException>(() => new KnowledgeConfidenceScore(double.NaN));
        Assert.Throws<ArgumentException>(() => new KnowledgeConfidenceScore(double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new KnowledgeConfidenceScore(double.NegativeInfinity));
        Assert.Null(new AstronomyKnowledgeConfidenceAssessment(new ConfidenceAssessmentId("confidence.synthetic.no-score"), new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(2), KnowledgeConfidenceLevel.Unknown, null, ConfidenceAssessmentMethod.Imported, new ConfidenceAssessorReference("external.synthetic", ConfidenceAssessorType.ExternalAuthority, "External Synthetic"), KnowledgeAuditMetadata.Create(Created, "tester"), [], []).Score);
    }

    [Fact]
    public void Assessment_identity_follows_token_semantics_and_default_is_invalid()
    {
        var id = new ConfidenceAssessmentId(" confidence.synthetic.moon.v1 ");
        Assert.Equal("confidence.synthetic.moon.v1", id.Value);
        Assert.Equal("confidence.synthetic.moon.v1", id.ToString());
        Assert.Equal(new ConfidenceAssessmentId("confidence.synthetic.moon.v1"), id);
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentId(""));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentId("confidence moon"));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentId("confidence\u0001moon"));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentId(new string('a', ConfidenceAssessmentId.MaxLength + 1)));
        Assert.Equal(string.Empty, default(ConfidenceAssessmentId).ToString());
        Assert.True(string.IsNullOrWhiteSpace(default(ConfidenceAssessmentId).Value));
    }

    [Fact]
    public void Assessor_reference_normalizes_metadata_rejects_invalid_values_and_uses_value_equality()
    {
        var human = new ConfidenceAssessorReference(" expert.synthetic.one ", ConfidenceAssessorType.HumanExpert, " Synthetic Expert ", " Org ", " v1 ");
        Assert.Equal("expert.synthetic.one", human.AssessorId);
        Assert.Equal("Synthetic Expert", human.DisplayName);
        Assert.Equal("Org", human.Organization);
        Assert.Equal("v1", human.ModelOrSystemVersion);
        Assert.Equal(new ConfidenceAssessorReference("expert.synthetic.one", ConfidenceAssessorType.HumanExpert, "Synthetic Expert", "Org", "v1"), human);

        var automated = new ConfidenceAssessorReference("rules.confidence.v1", ConfidenceAssessorType.AutomatedRule, "Rules Confidence", " ", null);
        Assert.Null(automated.Organization);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfidenceAssessorReference("id", (ConfidenceAssessorType)999, "Name"));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessorReference("", ConfidenceAssessorType.HumanEditor, "Name"));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessorReference("id", ConfidenceAssessorType.HumanEditor, ""));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessorReference("id", ConfidenceAssessorType.HumanEditor, "bad\u0001"));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessorReference("id", ConfidenceAssessorType.HumanEditor, new string('a', ConfidenceAssessorReference.MaxDisplayNameLength + 1)));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessorReference("id", ConfidenceAssessorType.HumanEditor, "Name", new string('a', ConfidenceAssessorReference.MaxOptionalTextLength + 1)));
    }

    [Fact]
    public void Assessment_factor_is_minimal_structured_explainability_without_numeric_weighting()
    {
        var factor = new ConfidenceAssessmentFactor(" Multiple-Independent-Sources ", ConfidenceFactorDirection.Supports, " Preserved Note ");
        Assert.Equal("multiple-independent-sources", factor.Code);
        Assert.Equal(ConfidenceFactorDirection.Supports, factor.Direction);
        Assert.Equal("Preserved Note", factor.Note);
        Assert.Equal(new ConfidenceAssessmentFactor("multiple-independent-sources", ConfidenceFactorDirection.Supports, "Preserved Note"), factor);
        Assert.Null(new ConfidenceAssessmentFactor("recent-observation", ConfidenceFactorDirection.Neutral, " ").Note);
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentFactor("", ConfidenceFactorDirection.Supports));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfidenceAssessmentFactor("code", (ConfidenceFactorDirection)999));
        Assert.Throws<ArgumentException>(() => new ConfidenceAssessmentFactor("bad\u0001", ConfidenceFactorDirection.Supports));
    }

    [Fact]
    public void Minimal_valid_assessment_exposes_one_statement_revision_evidence_ids_factors_rationale_and_audit()
    {
        var assessment = CreateAssessment();
        Assert.Equal(new ConfidenceAssessmentId("confidence.synthetic.moon.v1"), assessment.Id);
        Assert.Equal(new KnowledgeId("knowledge.synthetic.moon"), assessment.KnowledgeId);
        Assert.Equal(new KnowledgeVersion(2), assessment.KnowledgeVersion);
        Assert.Equal(KnowledgeConfidenceLevel.High, assessment.Level);
        Assert.Equal(new KnowledgeConfidenceScore(.82d), assessment.Score);
        Assert.Equal(ConfidenceAssessmentMethod.HumanExpertReview, assessment.Method);
        Assert.Equal("expert.synthetic.one", assessment.Assessor.AssessorId);
        Assert.Equal([new EvidenceId("evidence.synthetic.a"), new EvidenceId("evidence.synthetic.b")], assessment.EvidenceIds);
        Assert.Equal([new ConfidenceAssessmentFactor("limited-sample", ConfidenceFactorDirection.Reduces), new ConfidenceAssessmentFactor("multiple-independent-sources", ConfidenceFactorDirection.Supports)], assessment.Factors);
        Assert.Equal("Strong support with a preserved Case.", assessment.Rationale);
        Assert.Equal(KnowledgeAuditMetadata.Create(Created, "tester"), assessment.Audit);
    }

    [Fact]
    public void Required_field_guards_reject_default_ids_invalid_enums_null_assessor_and_null_audit()
    {
        Assert.Throws<ArgumentException>(() =>
            new AstronomyKnowledgeConfidenceAssessment(
                default(ConfidenceAssessmentId),
                new KnowledgeId("knowledge.synthetic.moon"),
                new KnowledgeVersion(2),
                KnowledgeConfidenceLevel.High,
                new KnowledgeConfidenceScore(.82d),
                ConfidenceAssessmentMethod.HumanExpertReview,
                new ConfidenceAssessorReference(
                    "expert.synthetic.one",
                    ConfidenceAssessorType.HumanExpert,
                    "Synthetic Expert"),
                KnowledgeAuditMetadata.Create(Created, "tester"),
                [],
                []));

        Assert.Throws<ArgumentException>(() =>
            new AstronomyKnowledgeConfidenceAssessment(
                new ConfidenceAssessmentId("confidence.synthetic.moon.v1"),
                default(KnowledgeId),
                new KnowledgeVersion(2),
                KnowledgeConfidenceLevel.High,
                new KnowledgeConfidenceScore(.82d),
                ConfidenceAssessmentMethod.HumanExpertReview,
                new ConfidenceAssessorReference(
                    "expert.synthetic.one",
                    ConfidenceAssessorType.HumanExpert,
                    "Synthetic Expert"),
                KnowledgeAuditMetadata.Create(Created, "tester"),
                [],
                []));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AstronomyKnowledgeConfidenceAssessment(
                new ConfidenceAssessmentId("confidence.synthetic.moon.v1"),
                new KnowledgeId("knowledge.synthetic.moon"),
                default(KnowledgeVersion),
                KnowledgeConfidenceLevel.High,
                new KnowledgeConfidenceScore(.82d),
                ConfidenceAssessmentMethod.HumanExpertReview,
                new ConfidenceAssessorReference(
                    "expert.synthetic.one",
                    ConfidenceAssessorType.HumanExpert,
                    "Synthetic Expert"),
                KnowledgeAuditMetadata.Create(Created, "tester"),
                [],
                []));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAssessment(level: (KnowledgeConfidenceLevel)999));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAssessment(method: (ConfidenceAssessmentMethod)999));

        Assert.Throws<ArgumentNullException>(() =>
            new AstronomyKnowledgeConfidenceAssessment(
                new ConfidenceAssessmentId("confidence.synthetic.null-assessor"),
                new KnowledgeId("knowledge.synthetic.moon"),
                new KnowledgeVersion(2),
                KnowledgeConfidenceLevel.Unknown,
                null,
                ConfidenceAssessmentMethod.Imported,
                null!,
                KnowledgeAuditMetadata.Create(Created, "tester"),
                [],
                []));

        Assert.Throws<ArgumentNullException>(() =>
            new AstronomyKnowledgeConfidenceAssessment(
                new ConfidenceAssessmentId("confidence.synthetic.null-audit"),
                new KnowledgeId("knowledge.synthetic.moon"),
                new KnowledgeVersion(2),
                KnowledgeConfidenceLevel.Unknown,
                null,
                ConfidenceAssessmentMethod.Imported,
                new ConfidenceAssessorReference(
                    "external.synthetic",
                    ConfidenceAssessorType.ExternalAuthority,
                    "External Synthetic"),
                null!,
                [],
                []));
    }

    [Fact]
    public void Evidence_ids_are_copied_sorted_unique_read_only_and_reject_null_collection_or_default_ids()
    {
        var ids = new List<EvidenceId> { new("evidence.synthetic.b"), new("evidence.synthetic.a") };
        var assessment = CreateAssessment(evidenceIds: ids);
        ids.Add(new EvidenceId("evidence.synthetic.c"));
        Assert.Equal([new EvidenceId("evidence.synthetic.a"), new EvidenceId("evidence.synthetic.b")], assessment.EvidenceIds);
        Assert.Throws<NotSupportedException>(() => ((IList<EvidenceId>)assessment.EvidenceIds).Add(new EvidenceId("x")));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeConfidenceAssessment(new ConfidenceAssessmentId("confidence.synthetic.null-evidence"), new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(2), KnowledgeConfidenceLevel.Unknown, null, ConfidenceAssessmentMethod.Imported, new ConfidenceAssessorReference("external.synthetic", ConfidenceAssessorType.ExternalAuthority, "External Synthetic"), KnowledgeAuditMetadata.Create(Created, "tester"), null!, []));
        Assert.Throws<ArgumentException>(() => CreateAssessment(evidenceIds: [default]));
        Assert.Throws<ArgumentException>(() => CreateAssessment(evidenceIds: [new EvidenceId("a"), new EvidenceId("a")]));
    }

    [Fact]
    public void Factors_are_copied_sorted_unique_read_only_and_reject_null_collection_or_null_entries()
    {
        var factors = new List<ConfidenceAssessmentFactor> { new("source-disagreement", ConfidenceFactorDirection.Reduces), new("multiple-independent-sources", ConfidenceFactorDirection.Supports) };
        var assessment = CreateAssessment(factors: factors);
        factors.Add(new ConfidenceAssessmentFactor("recent-observation", ConfidenceFactorDirection.Supports));
        Assert.Equal(["multiple-independent-sources", "source-disagreement"], assessment.Factors.Select(f => f.Code).ToArray());
        Assert.Throws<NotSupportedException>(() => ((IList<ConfidenceAssessmentFactor>)assessment.Factors).Add(new ConfidenceAssessmentFactor("x", ConfidenceFactorDirection.Neutral)));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeConfidenceAssessment(new ConfidenceAssessmentId("confidence.synthetic.null-factors"), new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(2), KnowledgeConfidenceLevel.Unknown, null, ConfidenceAssessmentMethod.Imported, new ConfidenceAssessorReference("external.synthetic", ConfidenceAssessorType.ExternalAuthority, "External Synthetic"), KnowledgeAuditMetadata.Create(Created, "tester"), [], null!));
        Assert.Throws<ArgumentException>(() => CreateAssessment(factors: [null!]));
        Assert.Throws<ArgumentException>(() => CreateAssessment(factors: [new ConfidenceAssessmentFactor("A", ConfidenceFactorDirection.Supports), new ConfidenceAssessmentFactor("a", ConfidenceFactorDirection.Reduces)]));
    }

    [Fact]
    public void Rationale_trims_blanks_to_null_bounds_control_characters_and_preserves_case()
    {
        Assert.Equal("Mixed Case rationale.", CreateAssessment(rationale: " Mixed Case rationale. ").Rationale);
        Assert.Null(CreateAssessment(rationale: "   ").Rationale);
        Assert.Throws<ArgumentException>(() => CreateAssessment(rationale: new string('a', AstronomyKnowledgeConfidenceAssessment.MaxRationaleLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAssessment(rationale: "bad\u0001"));
    }

    [Fact]
    public void Assessment_uses_entity_equality_by_assessment_id_and_does_not_auto_map_score_level_evidence_or_factors()
    {
        var highNoScore = new AstronomyKnowledgeConfidenceAssessment(new ConfidenceAssessmentId("confidence.synthetic.moon.v1"), new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(2), KnowledgeConfidenceLevel.High, null, ConfidenceAssessmentMethod.HumanExpertReview, new ConfidenceAssessorReference("expert.synthetic.one", ConfidenceAssessorType.HumanExpert, "Synthetic Expert"), KnowledgeAuditMetadata.Create(Created, "tester"), [], []);
        Assert.Null(highNoScore.Score);
        Assert.Equal(KnowledgeConfidenceLevel.High, highNoScore.Level);
        var unknownScored = CreateAssessment(score: new KnowledgeConfidenceScore(.91d), level: KnowledgeConfidenceLevel.Unknown, factors: []);
        Assert.Equal(new KnowledgeConfidenceScore(.91d), unknownScored.Score);
        Assert.Empty(unknownScored.Factors);

        var sameIdDifferentMetadata = CreateAssessment(level: KnowledgeConfidenceLevel.Low, score: new KnowledgeConfidenceScore(.1d), evidenceIds: [new EvidenceId("zzz")]);
        Assert.Equal(highNoScore, sameIdDifferentMetadata);
        Assert.True(highNoScore.HasSameAssessmentIdentityAs(sameIdDifferentMetadata));
        Assert.Equal(highNoScore.GetHashCode(), sameIdDifferentMetadata.GetHashCode());
        Assert.NotEqual(highNoScore, CreateAssessment(id: new ConfidenceAssessmentId("confidence.synthetic.other")));
    }

    [Fact]
    public void Confidence_assessment_scope_excludes_engines_serialization_di_persistence_clients_current_time_and_certification()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Evidence/Confidence"));
        var text = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (var forbidden in new[] { "ConfidenceCalculator", "ConfidenceEngine", "EvidenceWeight", "WeightedAverage", "Bayesian", "ConsensusAlgorithm", "EvidenceValidator", "ConfidenceValidator", "ValidationCodes", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "DateTimeOffset.UtcNow", "CertificationCoordinator", "OpenAI", "MachineLearning" })
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
    }

    private static AstronomyKnowledgeConfidenceAssessment CreateAssessment(
        ConfidenceAssessmentId? id = null,
        KnowledgeId? knowledgeId = null,
        KnowledgeVersion? knowledgeVersion = null,
        KnowledgeConfidenceLevel level = KnowledgeConfidenceLevel.High,
        KnowledgeConfidenceScore? score = null,
        ConfidenceAssessmentMethod method = ConfidenceAssessmentMethod.HumanExpertReview,
        ConfidenceAssessorReference? assessor = null,
        KnowledgeAuditMetadata? audit = null,
        IEnumerable<EvidenceId>? evidenceIds = null,
        IEnumerable<ConfidenceAssessmentFactor>? factors = null,
        string? rationale = " Strong support with a preserved Case. ")
        => new(
            id ?? new ConfidenceAssessmentId("confidence.synthetic.moon.v1"),
            knowledgeId ?? new KnowledgeId("knowledge.synthetic.moon"),
            knowledgeVersion ?? new KnowledgeVersion(2),
            level,
            score ?? new KnowledgeConfidenceScore(.82d),
            method,
            assessor ?? new ConfidenceAssessorReference("expert.synthetic.one", ConfidenceAssessorType.HumanExpert, "Synthetic Expert"),
            audit ?? KnowledgeAuditMetadata.Create(Created, "tester"),
            evidenceIds ?? [new EvidenceId("evidence.synthetic.b"), new EvidenceId("evidence.synthetic.a")],
            factors ?? [new ConfidenceAssessmentFactor("multiple-independent-sources", ConfidenceFactorDirection.Supports), new ConfidenceAssessmentFactor("limited-sample", ConfidenceFactorDirection.Reduces)],
            rationale);
}
