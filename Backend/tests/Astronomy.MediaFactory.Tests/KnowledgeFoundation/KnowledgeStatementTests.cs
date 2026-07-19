using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class KnowledgeStatementTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_statement_preserves_permanent_contract_state_and_typed_payload()
    {
        var payload = new SyntheticPayload("moon.meaning");
        var validity = new KnowledgeValidityRange(Created, Created.AddDays(1));
        var statement = CreateStatement(
            payload: payload,
            familyContext: new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem),
            localizationReferences: [new(new KnowledgeLanguageTag("HI"), "moon.name", isOriginalTerm: true), new(new KnowledgeLanguageTag("en-us"), "moon.name", isCanonicalLabel: true)],
            tags: [new KnowledgeTag("Moon"), new KnowledgeTag("Lunar")],
            validity: validity);

        Assert.Equal(new KnowledgeId("knowledge.moon.identity"), statement.Id);
        Assert.Equal(KnowledgeVersion.Initial, statement.Version);
        Assert.Equal(KnowledgeStatementKind.Scientific, statement.Kind);
        Assert.Equal(KnowledgeFoundationStatus.Draft, statement.Status);
        Assert.Equal(new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"), statement.PrimarySubject);
        Assert.Equal(new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem), statement.FamilyContext);
        Assert.Same(payload, statement.Payload);
        Assert.Same(payload, ((IAstronomyKnowledgeStatement)statement).Payload);
        Assert.Equal(["en-US:moon.name", "hi:moon.name"], statement.LocalizationReferences.Select(reference => $"{reference.LanguageTag}:{reference.ResourceKey}"));
        Assert.Equal(["lunar", "moon"], statement.Tags.Select(tag => tag.Value));
        Assert.Same(validity, statement.Validity);
        Assert.Equal(new KnowledgeAuditMetadata(Created, "author"), statement.Audit);
    }

    [Fact]
    public void Constructor_rejects_missing_default_or_undefined_required_state()
    {
        Assert.Throws<ArgumentException>(() =>
            new AstronomyKnowledgeStatement<SyntheticPayload>(
                default(KnowledgeId),
                KnowledgeVersion.Initial,
                KnowledgeStatementKind.Scientific,
                KnowledgeFoundationStatus.Draft,
                new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
                new SyntheticPayload("moon.meaning"),
                new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AstronomyKnowledgeStatement<SyntheticPayload>(
                new KnowledgeId("knowledge.moon.identity"),
                default(KnowledgeVersion),
                KnowledgeStatementKind.Scientific,
                KnowledgeFoundationStatus.Draft,
                new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
                new SyntheticPayload("moon.meaning"),
                new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateStatement(kind: (KnowledgeStatementKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateStatement(status: (KnowledgeFoundationStatus)999));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatement<SyntheticPayload>(
            new KnowledgeId("knowledge.moon.identity"),
            KnowledgeVersion.Initial,
            KnowledgeStatementKind.Scientific,
            KnowledgeFoundationStatus.Draft,
            null!,
            new SyntheticPayload("moon.meaning"),
            new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatement<SyntheticPayload>(
            new KnowledgeId("knowledge.moon.identity"),
            KnowledgeVersion.Initial,
            KnowledgeStatementKind.Scientific,
            KnowledgeFoundationStatus.Draft,
            new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
            null!,
            new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatement<SyntheticPayload>(
            new KnowledgeId("knowledge.moon.identity"),
            KnowledgeVersion.Initial,
            KnowledgeStatementKind.Scientific,
            KnowledgeFoundationStatus.Draft,
            new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
            new SyntheticPayload("moon.meaning"),
            null!));
    }

    [Fact]
    public void Optional_collections_and_family_context_have_safe_minimum_defaults()
    {
        var statement = CreateStatement();

        Assert.Null(statement.FamilyContext);
        Assert.Empty(statement.LocalizationReferences);
        Assert.Empty(statement.Tags);
        Assert.True(statement.Validity.IsOpenEnded);
    }

    [Fact]
    public void Tags_are_unique_ordered_defensively_copied_and_exposed_read_only()
    {
        var tags = new List<KnowledgeTag> { new("Beta"), new("alpha") };
        var statement = CreateStatement(tags: tags);

        tags.Add(new KnowledgeTag("gamma"));

        Assert.Equal(["alpha", "beta"], statement.Tags.Select(tag => tag.Value));
        Assert.IsNotType<List<KnowledgeTag>>(statement.Tags);
        Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeTag>)statement.Tags).Add(new KnowledgeTag("delta")));
        Assert.Throws<ArgumentException>(() => CreateStatement(tags: [new KnowledgeTag("Example.Tag"), new KnowledgeTag("example.tag")]));
    }

    [Fact]
    public void Localization_references_validate_keys_are_unique_ordered_defensively_copied_and_read_only()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeLocalizationReference(new KnowledgeLanguageTag("en"), "   "));
        Assert.Throws<ArgumentException>(() => new KnowledgeLocalizationReference(new KnowledgeLanguageTag("en"), "bad\nkey"));
        Assert.Throws<ArgumentException>(() => new KnowledgeLocalizationReference(new KnowledgeLanguageTag("en"), new string('a', 257)));

        var localizations = new List<KnowledgeLocalizationReference> { new(new KnowledgeLanguageTag("HI"), "b"), new(new KnowledgeLanguageTag("en-us"), "a") };
        var statement = CreateStatement(localizationReferences: localizations);

        localizations.Add(new KnowledgeLocalizationReference(new KnowledgeLanguageTag("fr"), "c"));

        Assert.Equal(["en-US:a", "hi:b"], statement.LocalizationReferences.Select(reference => $"{reference.LanguageTag}:{reference.ResourceKey}"));
        Assert.IsNotType<List<KnowledgeLocalizationReference>>(statement.LocalizationReferences);
        Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeLocalizationReference>)statement.LocalizationReferences).Add(new KnowledgeLocalizationReference(new KnowledgeLanguageTag("de"), "d")));
        Assert.Throws<ArgumentException>(() => CreateStatement(localizationReferences: [new(new KnowledgeLanguageTag("EN-us"), "same"), new(new KnowledgeLanguageTag("en-US"), "same", isOriginalTerm: true)]));
    }

    [Fact]
    public void Validity_shapes_are_preserved_without_retesting_primitive_rules()
    {
        var end = Created.AddDays(1);
        Assert.True(CreateStatement(validity: new KnowledgeValidityRange()).Validity.IsOpenEnded);
        Assert.Equal(new KnowledgeValidityRange(Created, end), CreateStatement(validity: new KnowledgeValidityRange(Created, end)).Validity);
        Assert.Equal(new KnowledgeValidityRange(Created), CreateStatement(validity: new KnowledgeValidityRange(Created)).Validity);
        Assert.Equal(new KnowledgeValidityRange(null, end), CreateStatement(validity: new KnowledgeValidityRange(null, end)).Validity);
    }

    [Fact]
    public void Statement_equality_is_version_identity_not_payload_identity()
    {
        var sharedPayload = new SyntheticPayload("same");
        var sameVersion = CreateStatement(payload: sharedPayload);
        var sameVersionDifferentContent = CreateStatement(payload: new SyntheticPayload("different"));
        var nextVersion = CreateStatement(version: new KnowledgeVersion(2), payload: sharedPayload);
        var differentId = CreateStatement(id: new KnowledgeId("knowledge.moon.other"), payload: sharedPayload);

        Assert.Equal(sameVersion, sameVersionDifferentContent);
        Assert.True(sameVersion.HasSameVersionIdentityAs(sameVersionDifferentContent));
        Assert.NotEqual(sameVersion, nextVersion);
        Assert.False(sameVersion.HasSameVersionIdentityAs(nextVersion));
        Assert.NotEqual(sameVersion, differentId);
    }

    [Fact]
    public void Shared_contract_exposes_only_task_21b_statement_semantics()
    {
        var names = typeof(IAstronomyKnowledgeStatement).GetProperties().Select(property => property.Name).Order().ToArray();

        Assert.Equal([
            "Audit",
            "FamilyContext",
            "Id",
            "Kind",
            "LocalizationReferences",
            "Payload",
            "PrimarySubject",
            "Status",
            "Tags",
            "Validity",
            "Version"
        ], names);
    }

    private static AstronomyKnowledgeStatement<SyntheticPayload> CreateStatement(
        KnowledgeId? id = null,
        KnowledgeVersion? version = null,
        KnowledgeStatementKind kind = KnowledgeStatementKind.Scientific,
        KnowledgeFoundationStatus status = KnowledgeFoundationStatus.Draft,
        AstronomyEntityReference? primarySubject = null,
        SyntheticPayload? payload = null,
        KnowledgeAuditMetadata? audit = null,
        AstronomyFamilyReference? familyContext = null,
        IEnumerable<KnowledgeLocalizationReference>? localizationReferences = null,
        IEnumerable<KnowledgeTag>? tags = null,
        KnowledgeValidityRange? validity = null)
        => new(
            id ?? new KnowledgeId("knowledge.moon.identity"),
            version ?? KnowledgeVersion.Initial,
            kind,
            status,
            primarySubject ?? new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"),
            payload ?? new SyntheticPayload("moon.meaning"),
            audit ?? new KnowledgeAuditMetadata(Created, "author"),
            familyContext,
            localizationReferences,
            tags,
            validity);

    private sealed record SyntheticPayload(string SemanticKey) : IAstronomyKnowledgePayload;
}
