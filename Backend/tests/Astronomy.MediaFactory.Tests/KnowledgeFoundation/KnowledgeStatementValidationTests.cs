using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class KnowledgeStatementValidationTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AstronomyKnowledgeStatementValidator Validator = new();

    [Fact]
    public void Valid_statement_returns_successful_result_with_no_issues()
    {
        var result = Validator.Validate(ValidStatement());
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Null_statement_throws_argument_null_exception()
        => Assert.Throws<ArgumentNullException>(() => Validator.Validate(null!));

    [Fact]
    public void Validator_reports_foundational_identity_version_enum_subject_and_payload_failures()
    {
        var statement = InvalidStatement.Valid() with
        {
            Id = default,
            Version = default,
            Kind = (KnowledgeStatementKind)999,
            Status = (KnowledgeFoundationStatus)999,
            PrimarySubject = null,
            Payload = null
        };

        Validator.Validate(statement).Issues.Select(i => i.Code).Should().Contain([
            AstronomyKnowledgeValidationCodes.IdMissing,
            AstronomyKnowledgeValidationCodes.VersionInvalid,
            AstronomyKnowledgeValidationCodes.KindUndefined,
            AstronomyKnowledgeValidationCodes.StatusUndefined,
            AstronomyKnowledgeValidationCodes.SubjectMissing,
            AstronomyKnowledgeValidationCodes.PayloadMissing]);
    }

    [Fact]
    public void Validator_reports_invalid_subject_and_family_reference_state_without_catalog_resolution()
    {
        var subject = new AstronomyEntityReference("moon") { EntityId = " ", EntityKind = (AstronomyEntityKind)999 };
        var family = new AstronomyFamilyReference("solar-system") { FamilyId = "", FamilyKind = (AstronomyFamilyKind)999 };
        var result = Validator.Validate(InvalidStatement.Valid() with { PrimarySubject = subject, FamilyContext = family });

        result.Issues.Select(i => i.Code).Should().Contain([
            AstronomyKnowledgeValidationCodes.SubjectInvalid,
            AstronomyKnowledgeValidationCodes.FamilyContextInvalid]);
    }

    [Fact]
    public void Validator_reports_null_malformed_and_duplicate_tags()
    {
        var tags = new List<KnowledgeTag?> { new("Moon"), null, new("moon") };
        var result = Validator.Validate(InvalidStatement.Valid() with { TagsOverride = tags! });

        result.Issues.Select(i => i.Code).Should().Contain([
            AstronomyKnowledgeValidationCodes.TagMissing,
            AstronomyKnowledgeValidationCodes.DuplicateTag]);

        Validator.Validate(InvalidStatement.Valid() with { TagsOverride = null }).Issues.Should().Contain(i => i.Code == AstronomyKnowledgeValidationCodes.TagCollectionMissing);
        Validator.Validate(InvalidStatement.Valid() with { TagsOverride = [new KnowledgeTag("moon"), new KnowledgeTag("lunar")] }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_reports_null_malformed_and_duplicate_localization_references()
    {
        var duplicateA = new KnowledgeLocalizationReference(new KnowledgeLanguageTag("en-US"), "moon.name");
        var duplicateB = new KnowledgeLocalizationReference(new KnowledgeLanguageTag("en-us"), "moon.name", isOriginalTerm: true);
        var references = new List<KnowledgeLocalizationReference?> { duplicateA, null, duplicateB };
        var result = Validator.Validate(InvalidStatement.Valid() with { LocalizationReferencesOverride = references! });

        result.Issues.Select(i => i.Code).Should().Contain([
            AstronomyKnowledgeValidationCodes.LocalizationMissing,
            AstronomyKnowledgeValidationCodes.DuplicateLocalization]);

        Validator.Validate(InvalidStatement.Valid() with { LocalizationReferencesOverride = null }).Issues.Should().Contain(i => i.Code == AstronomyKnowledgeValidationCodes.LocalizationCollectionMissing);
        Validator.Validate(InvalidStatement.Valid() with { LocalizationReferencesOverride = [new(new KnowledgeLanguageTag("en"), "moon.name"), new(new KnowledgeLanguageTag("hi"), "moon.name")] }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_reports_missing_validity_and_audit_and_accepts_valid_shapes()
    {
        Validator.Validate(InvalidStatement.Valid() with { Validity = null, Audit = null }).Issues.Select(i => i.Code).Should().Contain([
            AstronomyKnowledgeValidationCodes.ValidityMissing,
            AstronomyKnowledgeValidationCodes.AuditMissing]);

        Validator.Validate(InvalidStatement.Valid() with { Validity = new KnowledgeValidityRange(), Audit = new KnowledgeAuditMetadata(Created) }).IsValid.Should().BeTrue();
        Validator.Validate(InvalidStatement.Valid() with { Validity = new KnowledgeValidityRange(Created, Created.AddDays(1)), Audit = new KnowledgeAuditMetadata(Created, "author", Created.AddHours(1), "editor") }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Aggregate_validation_order_is_deterministic_and_validation_does_not_mutate_input_collections()
    {
        var tags = new List<KnowledgeTag?> { new("Moon"), new("moon") };
        var localizations = new List<KnowledgeLocalizationReference?> { new(new KnowledgeLanguageTag("en"), "key"), new(new KnowledgeLanguageTag("en"), "key") };
        var statement = InvalidStatement.Valid() with
        {
            Id = default,
            Version = default,
            Kind = (KnowledgeStatementKind)999,
            Status = (KnowledgeFoundationStatus)999,
            PrimarySubject = null,
            Payload = null,
            TagsOverride = tags!,
            LocalizationReferencesOverride = localizations!
        };

        var beforeTags = tags.ToArray();
        var beforeLocalizations = localizations.ToArray();
        var first = Validator.Validate(statement).Issues.Select(i => i.Code + "|" + i.Path).ToArray();
        var second = Validator.Validate(statement).Issues.Select(i => i.Code + "|" + i.Path).ToArray();

        first.Should().Equal(second);
        first.Should().BeInAscendingOrder(StringComparer.Ordinal);
        tags.Should().Equal(beforeTags);
        localizations.Should().Equal(beforeLocalizations);
        statement.Tags.Should().BeSameAs(tags);
        statement.LocalizationReferences.Should().BeSameAs(localizations);
    }

    [Fact]
    public void Constructor_guards_and_validator_report_equivalent_major_invariants()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatement<SyntheticPayload>(default, KnowledgeVersion.Initial, KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Draft, new AstronomyEntityReference("moon"), new SyntheticPayload("a"), new KnowledgeAuditMetadata(Created)));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatement<SyntheticPayload>(new KnowledgeId("knowledge.moon"), KnowledgeVersion.Initial, KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Draft, null!, new SyntheticPayload("a"), new KnowledgeAuditMetadata(Created)));

        var result = Validator.Validate(InvalidStatement.Valid() with { Id = default, PrimarySubject = null });
        result.Issues.Select(i => i.Code).Should().Contain([AstronomyKnowledgeValidationCodes.IdMissing, AstronomyKnowledgeValidationCodes.SubjectMissing]);
    }

    [Fact]
    public void Foundational_validator_is_payload_type_neutral_and_public_api_stays_within_task_21c_scope()
    {
        Validator.Validate(ValidStatement(new SyntheticPayload("one"))).IsValid.Should().BeTrue();
        Validator.Validate(ValidStatement(new AlternatePayload("two"))).IsValid.Should().BeTrue();

        var publicNames = typeof(AstronomyKnowledgeStatementValidator).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation")
            .Select(t => t.Name)
            .Order()
            .ToArray();

        publicNames.Should().BeEquivalentTo([
            nameof(AstronomyKnowledgeStatementValidator),
            nameof(AstronomyKnowledgeValidationCodes),
            nameof(IAstronomyKnowledgeStatementValidator)]);
        publicNames.Should().NotContain(n => n.Contains("Evidence") || n.Contains("Confidence") || n.Contains("Catalog") || n.Contains("Query") || n.Contains("Transition") || n.Contains("Relationship"));
    }

    private static AstronomyKnowledgeStatement<IAstronomyKnowledgePayload> ValidStatement(IAstronomyKnowledgePayload? payload = null)
        => new(
            new KnowledgeId("knowledge.moon.identity"),
            KnowledgeVersion.Initial,
            KnowledgeStatementKind.Scientific,
            KnowledgeFoundationStatus.Draft,
            new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
            payload ?? new SyntheticPayload("moon.meaning"),
            new KnowledgeAuditMetadata(Created, "author"),
            new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem),
            [new(new KnowledgeLanguageTag("en"), "moon.name")],
            [new KnowledgeTag("moon")],
            new KnowledgeValidityRange(Created));

    private sealed record SyntheticPayload(string SemanticKey) : IAstronomyKnowledgePayload;
    private sealed record AlternatePayload(string SemanticKey) : IAstronomyKnowledgePayload;

    private sealed record InvalidStatement : IAstronomyKnowledgeStatement
    {
        public KnowledgeId Id { get; init; }
        public KnowledgeVersion Version { get; init; }
        public KnowledgeStatementKind Kind { get; init; }
        public KnowledgeFoundationStatus Status { get; init; }
        public AstronomyEntityReference PrimarySubject { get; init; } = null!;
        public AstronomyFamilyReference? FamilyContext { get; init; }
        public IAstronomyKnowledgePayload Payload { get; init; } = null!;
        public IReadOnlyList<KnowledgeLocalizationReference> LocalizationReferences => LocalizationReferencesOverride!;
        public IReadOnlyList<KnowledgeTag> Tags => TagsOverride!;
        public KnowledgeValidityRange Validity { get; init; } = null!;
        public KnowledgeAuditMetadata Audit { get; init; } = null!;
        public IReadOnlyList<KnowledgeLocalizationReference>? LocalizationReferencesOverride { get; init; }
        public IReadOnlyList<KnowledgeTag>? TagsOverride { get; init; }

        public static InvalidStatement Valid() => new()
        {
            Id = new KnowledgeId("knowledge.moon.identity"),
            Version = KnowledgeVersion.Initial,
            Kind = KnowledgeStatementKind.Scientific,
            Status = KnowledgeFoundationStatus.Draft,
            PrimarySubject = new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
            Payload = new SyntheticPayload("moon.meaning"),
            LocalizationReferencesOverride = [],
            TagsOverride = [],
            Validity = new KnowledgeValidityRange(Created),
            Audit = new KnowledgeAuditMetadata(Created, "author")
        };
    }
}
