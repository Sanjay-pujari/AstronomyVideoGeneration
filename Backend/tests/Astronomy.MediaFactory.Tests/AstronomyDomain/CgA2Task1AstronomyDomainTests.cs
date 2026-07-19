using System.Text.Json;
using Astronomy.MediaFactory.Core.AstronomyDomain.Catalog;
using Astronomy.MediaFactory.Core.AstronomyDomain.Classification;
using Astronomy.MediaFactory.Core.AstronomyDomain.Entities;
using Astronomy.MediaFactory.Core.AstronomyDomain.Extensions;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Identity;
using Astronomy.MediaFactory.Core.AstronomyDomain.Localization;
using Astronomy.MediaFactory.Core.AstronomyDomain.Relationships;
using Astronomy.MediaFactory.Core.AstronomyDomain.Sources;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.AstronomyDomain.Validation;
using Astronomy.MediaFactory.Core.Certification;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests.AstronomyDomain;

public sealed class CgA2Task1AstronomyDomainTests
{
    private static JsonSerializerOptions Json => new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static AstronomyDomainValidator V => new(new AstronomyRelationshipPolicy());

    private static AstronomyEntityIdentity Id(string eid = "synthetic.event.alpha", string family = "synthetic.events", IReadOnlyList<string>? aliases = null)
        => new(eid, "Synthetic Alpha", AstronomyEntityKind.Event, family, AstronomyDomainCategory.TransientEvent, Aliases: aliases ?? [$"Synthetic Alias {eid}"]);

    private static AstronomyClassification Cls => new(AstronomyDomainCategory.TransientEvent, AstronomyFamilyKind.Event, AstronomyEntityKind.Event, AstronomySubjectTemporality.Transient, new("Synthetic", "Primary"), Tags: ["synthetic"]);

    private static AstronomyDomainEntity Ent(string id = "synthetic.event.alpha", AstronomyContentStatus st = AstronomyContentStatus.Approved)
        => new(Id(id), Cls, [new("en", "Synthetic Alpha", SearchAliases: [$"Alpha EN {id}"]), new("hi", "सिंथेटिक अल्फा", SearchAliases: [$"Alpha HI {id}"])], Metadata: new(Status: st, Keywords: ["synthetic"]));

    [Fact]
    public void Taxonomy_serializes_as_stable_strings()
    {
        JsonSerializer.Serialize(AstronomyEntityKind.DwarfPlanet, Json).Should().Be("\"DwarfPlanet\"");
        Enum.GetNames<AstronomyEntityKind>().Should().OnlyHaveUniqueItems().And.Contain(["Event", "Constellation", "ScientificConcept"]);
        Enum.GetValues<AstronomyDomainCategory>().Distinct().Count().Should().Be(9);
    }

    [Fact]
    public void Identity_validation_covers_required_stable_language_neutral_rules()
    {
        V.ValidateEntity(Ent()).IsValid.Should().BeTrue();
        V.ValidateEntity(Ent("bad id")).Issues.Should().Contain(i => i.Code == "A2.DOMAIN.IDENTITY.EntityIdContainsWhitespace");
        V.ValidateEntity(new AstronomyDomainEntity(Id("", aliases: ["x", "X", "Synthetic Alpha"]), Cls)).Issues.Select(i => i.Code).Should().Contain(["A2.DOMAIN.IDENTITY.EntityIdMissing", "A2.DOMAIN.IDENTITY.DuplicateAlias", "A2.DOMAIN.IDENTITY.AliasDuplicatesCanonicalName"]);
        var e = Ent();
        e.Localizations.Select(l => l.DisplayName).Should().Contain(["Synthetic Alpha", "सिंथेटिक अल्फा"]);
        e.Identity.EntityId.Should().Be("synthetic.event.alpha");
    }

    [Fact]
    public void Family_registry_resolves_by_id_and_event_type_without_content_strategy()
    {
        var f = new SyntheticFamily("synthetic.events", new HashSet<string> { "SyntheticEvent" });
        var r = new AstronomyDomainFamilyRegistry([f]);
        r.ResolveByFamilyId(" SYNTHETIC.EVENTS ").Should().Be(f);
        r.ResolveByEventType(" syntheticevent ").Should().Be(f);
        r.ResolveByEventType("SyntheticEvent").Should().Be(r.ResolveByEventType("SyntheticEvent"));
        r.Invoking(x => x.ResolveByEventType("missing")).Should().Throw<KeyNotFoundException>().WithMessage("*missing*");
        new Action(() => new AstronomyDomainFamilyRegistry([new SyntheticFamily("", new HashSet<string>())])).Should().Throw<InvalidOperationException>();
        new Action(() => new AstronomyDomainFamilyRegistry([new SyntheticFamily("a", new HashSet<string> { "x" }), new SyntheticFamily("A", new HashSet<string> { "y" })])).Should().Throw<InvalidOperationException>();
        new Action(() => new AstronomyDomainFamilyRegistry([new SyntheticFamily("a", new HashSet<string> { "x" }), new SyntheticFamily("b", new HashSet<string> { "X" })])).Should().Throw<InvalidOperationException>();
        new Action(() => new AstronomyDomainFamilyRegistry([new SyntheticFamily("a", new HashSet<string> { "" })])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Relationships_validate_direction_confidence_dates_and_sources()
    {
        V.ValidateRelationship(new("r1", "a", "b", AstronomyRelationshipType.Contains)).IsValid.Should().BeTrue();
        V.ValidateRelationship(new("r2", "a", "b", AstronomyRelationshipType.AssociatedWith, RelationshipDirection.Bidirectional)).IsValid.Should().BeTrue();
        var bad = new AstronomyRelationship("r3", "same", "same", AstronomyRelationshipType.Contains, RelationshipDirection.Bidirectional, -.1, ["s", "S"], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1));
        V.ValidateRelationship(bad).Issues.Select(i => i.Code).Should().Contain(["A2.DOMAIN.RELATIONSHIP.SelfReference", "A2.DOMAIN.RELATIONSHIP.ConfidenceOutOfRange", "A2.DOMAIN.RELATIONSHIP.DuplicateSourceId", "A2.DOMAIN.RELATIONSHIP.InvalidDateRange", "A2.DOMAIN.RELATIONSHIP.InvalidBidirectionalType"]);
        V.ValidateRelationship(new("r4", "", "", AstronomyRelationshipType.Contains)).Issues.Select(i => i.Code).Should().Contain(["A2.DOMAIN.RELATIONSHIP.SourceMissing", "A2.DOMAIN.RELATIONSHIP.TargetMissing"]);
    }

    [Fact]
    public void Localization_validation_preserves_shared_entity_identity()
    {
        var e = Ent();
        e.Localizations.Should().HaveCount(2);
        V.ValidateLocalization(new("en", "Name", RegionCode: "US")).IsValid.Should().BeTrue();
        V.ValidateLocalization(new("", "", SearchAliases: ["a", "A"], IsMachineTranslated: true, ReviewStatus: LocalizationReviewStatus.Approved)).Issues.Select(i => i.Code).Should().Contain(["A2.DOMAIN.LOCALIZATION.LanguageMissing", "A2.DOMAIN.LOCALIZATION.DisplayNameMissing", "A2.DOMAIN.LOCALIZATION.DuplicateAlias", "A2.DOMAIN.LOCALIZATION.MachineTranslationApproved"]);
        V.ValidateEntity(new AstronomyDomainEntity(Id(), Cls, [new("en", "One"), new("EN", "Two")])).Issues.Should().Contain(i => i.Code == "A2.DOMAIN.LOCALIZATION.DuplicateLocale");
    }

    [Fact]
    public void Sources_validate_url_retrieval_and_authority_serialization()
    {
        V.ValidateSource(new("official", AstronomySourceType.ScientificAgency, Publisher: "Agency", Url: new("https://example.test"), RetrievedUtc: DateTimeOffset.UtcNow, Reliability: SourceReliability.Authoritative, AuthorityLevel: SourceAuthorityLevel.Official)).IsValid.Should().BeTrue();
        V.ValidateSource(new("hist", AstronomySourceType.HistoricalPrimarySource, Author: "Archivist")).IsValid.Should().BeTrue();
        V.ValidateSource(new("bad", AstronomySourceType.GeneralReference, Url: new Uri("relative/path", UriKind.Relative), Reliability: SourceReliability.Authoritative)).Issues.Select(i => i.Code).Should().Contain(["A2.DOMAIN.SOURCE.InvalidUrl", "A2.DOMAIN.SOURCE.RetrievedUtcMissing", "A2.DOMAIN.SOURCE.GeneralReferenceAuthoritative"]);
        JsonSerializer.Serialize(SourceReliability.Authoritative, Json).Should().Be("\"Authoritative\"");
    }

    [Fact]
    public void Validator_aggregates_is_deterministic_and_does_not_mutate()
    {
        var e = new AstronomyDomainEntity(Id("bad id", aliases: ["x", "X"]), Cls);
        var before = e.Identity.Aliases.Count;
        var a = V.ValidateEntity(e);
        var b = V.ValidateEntity(e);
        a.Issues.Should().HaveCountGreaterThan(1).And.Equal(b.Issues);
        e.Identity.Aliases.Count.Should().Be(before);
        a.IsValid.Should().BeFalse();
        V.ValidateSource(new("g", AstronomySourceType.GeneralReference, Reliability: SourceReliability.Authoritative)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Catalog_supports_lookup_search_filters_deprecated_and_defensive_ordering()
    {
        var c = new InMemoryAstronomyDomainCatalog([Ent("synthetic.event.beta"), Ent("synthetic.event.alpha"), Ent("synthetic.event.old", AstronomyContentStatus.Deprecated)]);
        (await c.GetByIdAsync("SYNTHETIC.EVENT.ALPHA")).Should().NotBeNull();
        (await c.GetByIdAsync("Alpha EN synthetic.event.alpha")).Should().NotBeNull();
        (await c.SearchAsync(new(SearchText: "सिंथेटिक"))).Should().HaveCount(2);
        (await c.SearchAsync(new(FamilyIds: new HashSet<string> { "synthetic.events" }, EntityKinds: new HashSet<AstronomyEntityKind> { AstronomyEntityKind.Event }, DomainCategories: new HashSet<AstronomyDomainCategory> { AstronomyDomainCategory.TransientEvent }, Tags: new HashSet<string> { "synthetic" }))).Select(e => e.Identity.EntityId).Should().Equal("synthetic.event.alpha", "synthetic.event.beta");
        (await c.SearchAsync(new(IncludeDeprecated: false))).Should().HaveCount(2);
        (await c.SearchAsync(new(IncludeDeprecated: true))).Should().HaveCount(3);
        new Action(() => new InMemoryAstronomyDomainCatalog([Ent(), Ent()])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Catalog_constructor_rejects_alias_conflicts_across_different_entities()
    {
        var original = Ent("synthetic.event.original");
        var conflict = new AstronomyDomainEntity(
            Id("synthetic.event.conflict", aliases: ["Synthetic Alias synthetic.event.original"]),
            Cls,
            [new("en", "Conflict", SearchAliases: ["Conflict EN"])]);

        new Action(() => new InMemoryAstronomyDomainCatalog([original, conflict])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Catalog_add_is_atomic_when_alias_conflicts()
    {
        var catalog = new InMemoryAstronomyDomainCatalog();
        catalog.Add(Ent("synthetic.event.original"));

        var conflict = new AstronomyDomainEntity(Id("synthetic.event.conflict", aliases: ["Synthetic Alias synthetic.event.original"]), Cls, [new("en", "Conflict", SearchAliases: ["Conflict EN"])]);
        new Action(() => catalog.Add(conflict)).Should().Throw<InvalidOperationException>();

        (await catalog.GetByIdAsync("synthetic.event.original")).Should().NotBeNull();
        (await catalog.GetByIdAsync("synthetic.event.conflict")).Should().BeNull();
        (await catalog.GetByIdAsync("Synthetic Alias synthetic.event.original")).Should().NotBeNull();
        (await catalog.GetByIdAsync("Conflict EN")).Should().BeNull();
    }

    [Fact]
    public async Task Catalog_public_apis_validate_null_and_respect_cancellation()
    {
        var catalog = new InMemoryAstronomyDomainCatalog([Ent()]);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        new Action(() => catalog.Add(null!)).Should().Throw<ArgumentNullException>();
        await new Func<Task>(() => catalog.GetByIdAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await new Func<Task>(() => catalog.SearchAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await new Func<Task>(() => catalog.GetRelationshipsAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await new Func<Task>(() => catalog.GetByIdAsync("synthetic.event.alpha", canceled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await new Func<Task>(() => catalog.SearchAsync(new(), canceled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await new Func<Task>(() => catalog.GetRelationshipsAsync("synthetic.event.alpha", canceled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Catalog_empty_lookups_and_invalid_limits_are_normalized_deterministically()
    {
        var catalog = new InMemoryAstronomyDomainCatalog([Ent("synthetic.event.alpha")]);
        (await catalog.GetByIdAsync("   ")).Should().BeNull();
        (await catalog.GetRelationshipsAsync("   ")).Should().BeEmpty();
        (await catalog.SearchAsync(new(Limit: 0))).Should().HaveCount(1);
    }

    [Fact]
    public void Dependency_injection_resolves_cg_a2_and_cg_a1_independently()
    {
        var services = new ServiceCollection().AddCgA2AstronomyDomainFoundation().AddCgA1CertificationFoundation();
        using var p = services.BuildServiceProvider();
        p.GetRequiredService<IAstronomyDomainFamilyRegistry>().Families.Should().BeEmpty();
        p.GetRequiredService<IAstronomyRelationshipPolicy>().Should().NotBeNull();
        p.GetRequiredService<IAstronomyDomainValidator>().Should().NotBeNull();
        p.GetRequiredService<IAstronomyDomainCatalog>().Should().NotBeNull();
    }

    [Fact]
    public void Serialization_uses_camel_case_string_enums_and_no_clr_metadata()
    {
        var json = JsonSerializer.Serialize(Ent(), Json);
        json.Should().Contain("\"entityId\"").And.Contain("\"entityKind\":\"Event\"").And.Contain("\"schemaVersion\":\"1.0\"");
        json.Should().NotContain("$type").And.NotContain("AssemblyQualifiedName");
        JsonSerializer.Serialize(new AstronomyRelationship("r", "a", "b", AstronomyRelationshipType.AppearsNear), Json).Should().Contain("\"relationshipType\":\"AppearsNear\"");
        JsonSerializer.Serialize(new AstronomySourceReference("s", AstronomySourceType.GeneralReference), Json).Should().Contain("\"sourceType\":\"GeneralReference\"");
    }

    [Fact]
    public void Architecture_boundary_has_no_production_or_certification_dependencies()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/AstronomyDomain"));
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(".Generated.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        files.Should().OnlyContain(file => file.Contains($"src{Path.DirectorySeparatorChar}Astronomy.MediaFactory.Core{Path.DirectorySeparatorChar}AstronomyDomain{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        files.Should().Contain(file => Path.GetFileName(file).Equals("Families.cs", StringComparison.OrdinalIgnoreCase));

        var forbiddenNamespaceImports = new[]
        {
            "Astronomy.MediaFactory.Infrastructure",
            "Astronomy.MediaFactory.Publishing",
            "Astronomy.MediaFactory.Rendering",
            "Astronomy.MediaFactory.Api",
            "Astronomy.MediaFactory.Core.Certification"
        };
        var forbiddenTypeReferences = new[]
        {
            "NarrationGeneratorV5",
            "IRenderer",
            "Renderer",
            "IPublisher",
            "IContentPublishService",
            "IYouTubePublishingService",
            "CertificationCoordinator",
            "ContentStrategy"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbiddenNamespace in forbiddenNamespaceImports)
                text.Should().NotMatchRegex($@"(^|[;=({{,])\s*(using\s+)?{System.Text.RegularExpressions.Regex.Escape(forbiddenNamespace)}(\.|\s*;)");
            foreach (var forbiddenType in forbiddenTypeReferences)
                text.Should().NotMatchRegex($@"\b{System.Text.RegularExpressions.Regex.Escape(forbiddenType)}\b");
        }
    }

    private sealed class SyntheticFamily(string id, IReadOnlySet<string> aliases) : IAstronomyDomainFamily
    {
        public string FamilyId => id;
        public AstronomyFamilyKind FamilyKind => AstronomyFamilyKind.Event;
        public AstronomyDomainCategory DomainCategory => AstronomyDomainCategory.TransientEvent;
        public IReadOnlySet<AstronomyEntityKind> SupportedEntityKinds => new HashSet<AstronomyEntityKind> { AstronomyEntityKind.Event };
        public IReadOnlySet<string> SupportedEventTypeAliases => aliases;
        public bool Supports(AstronomyEntityIdentity identity) => string.Equals(identity.FamilyId, id, StringComparison.OrdinalIgnoreCase) && SupportedEntityKinds.Contains(identity.EntityKind);
        public DomainValidationResult ValidateEntity(IAstronomyDomainEntity entity) => DomainValidationResult.Success;
    }
}
