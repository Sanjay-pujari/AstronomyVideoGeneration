using System.Text.Json;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Identity;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class KnowledgePrimitivesTests
{
    [Theory]
    [InlineData("knowledge.moon.identity", "knowledge.moon.identity")]
    [InlineData("  knowledge.moon.identity  ", "knowledge.moon.identity")]
    [InlineData("Knowledge.Moon.Identity", "Knowledge.Moon.Identity")]
    public void Knowledge_id_has_stable_case_sensitive_token_semantics(string input, string expected)
    {
        var id = new KnowledgeId(input);
        var same = new KnowledgeId(expected);

        Assert.Equal(expected, id.Value);
        Assert.Equal(expected, id.ToString());
        Assert.Equal(same, id);
        Assert.Equal(same.GetHashCode(), id.GetHashCode());
        Assert.NotEqual(new KnowledgeId(expected.ToLowerInvariant()), new KnowledgeId(expected.ToUpperInvariant()));
        Assert.True(KnowledgeId.TryParse(input, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("knowledge moon")]
    [InlineData("knowledge\tmoon")]
    [InlineData("knowledge\nmoon")]
    [InlineData("knowledge\u0001moon")]
    public void Knowledge_id_rejects_invalid_tokens(string? value)
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeId(value!));
        Assert.False(KnowledgeId.TryParse(value, out _));
    }

    [Fact]
    public void Knowledge_id_rejects_values_longer_than_repository_token_limit_and_default_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeId(new string('a', 257)));
        Assert.Equal(string.Empty, default(KnowledgeId).ToString());
    }

    [Fact]
    public void Knowledge_version_is_positive_sequential_revision_with_ordering_and_overflow_guard()
    {
        var initial = KnowledgeVersion.Initial;
        var second = initial.Next();

        Assert.Equal("1", initial.ToString());
        Assert.Equal(new KnowledgeVersion(2), second);
        Assert.True(initial < second);
        Assert.True(second > initial);
        Assert.Equal(0, second.CompareTo(new KnowledgeVersion(2)));
        Assert.Equal(second.GetHashCode(), new KnowledgeVersion(2).GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeVersion(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeVersion(-1));
        Assert.Throws<OverflowException>(() => new KnowledgeVersion(int.MaxValue).Next());
        Assert.Equal(0, default(KnowledgeVersion).Revision);
    }

    [Fact]
    public void Knowledge_status_vocabulary_matches_architecture_charter_and_rejects_undefined_values()
    {
        var names = Enum.GetNames<KnowledgeFoundationStatus>();

        Assert.Equal(["Draft", "Reviewed", "Approved", "Deprecated", "Archived"], names);
        foreach (var status in Enum.GetValues<KnowledgeFoundationStatus>())
            Assert.Equal(status, KnowledgeFoundationEnumGuard.RequireDefined(status));
        Assert.Throws<ArgumentOutOfRangeException>(() => KnowledgeFoundationEnumGuard.RequireDefined((KnowledgeFoundationStatus)999));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KnowledgeFoundationStatus>("\"Candidate\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KnowledgeFoundationStatus>("\"Accepted\""));
    }

    [Fact]
    public void Knowledge_type_vocabulary_matches_architecture_charter_and_rejects_undefined_values()
    {
        var names = Enum.GetNames<KnowledgeStatementKind>();

        Assert.Equal(["Scientific", "Observation", "Educational", "Historical", "Cultural", "Safety", "Terminology", "Visual"], names);
        foreach (var kind in Enum.GetValues<KnowledgeStatementKind>())
            Assert.Equal(kind, KnowledgeFoundationEnumGuard.RequireDefined(kind));
        Assert.Throws<ArgumentOutOfRangeException>(() => KnowledgeFoundationEnumGuard.RequireDefined((KnowledgeStatementKind)999));
        foreach (var invalid in new[] { "General", "Identity", "Classification", "Education", "Visualization" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KnowledgeStatementKind>($"\"{invalid}\""));
    }

    [Fact]
    public void Astronomy_references_adapt_task1_identity_and_family_primitives_without_catalog_dependencies()
    {
        var identity = new AstronomyEntityIdentity("moon", "Moon", AstronomyEntityKind.Moon, "solar-system", AstronomyDomainCategory.SolarSystem);
        var entity = AstronomyEntityReference.FromIdentity(identity);
        var descriptor = new StubFamily("meteor-shower", AstronomyFamilyKind.Event);
        var family = AstronomyFamilyReference.FromFamily(descriptor);

        Assert.Equal("moon", entity.EntityId);
        Assert.Equal(AstronomyEntityKind.Moon, entity.EntityKind);
        Assert.Equal("Moon", entity.CanonicalName);
        Assert.Equal("meteor-shower", family.FamilyId);
        Assert.Equal(AstronomyFamilyKind.Event, family.FamilyKind);
        Assert.Throws<ArgumentException>(() => new AstronomyEntityReference("moon id"));
        Assert.Throws<ArgumentException>(() => new AstronomyFamilyReference("family\nid"));
    }

    [Theory]
    [InlineData("und", "und")]
    [InlineData("EN", "en")]
    [InlineData("en-us", "en-US")]
    [InlineData("zh-Hant", "zh-hant")]
    public void Language_tags_are_conservative_structural_bcp47_style_values(string value, string expected)
    {
        var tag = new KnowledgeLanguageTag(value);
        Assert.Equal(expected, tag.Value);
        Assert.Equal(expected, tag.ToString());
        Assert.Equal(new KnowledgeLanguageTag(expected), tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en us")]
    [InlineData("en_US")]
    [InlineData("en--US")]
    [InlineData("-en")]
    [InlineData("en-")]
    [InlineData("e")]
    public void Language_tags_reject_blank_whitespace_and_malformed_punctuation(string? value)
        => Assert.Throws<ArgumentException>(() => new KnowledgeLanguageTag(value!));

    [Fact]
    public void Tags_trim_lowercase_reject_unsafe_values_and_compare_by_normalized_form()
    {
        var tag = new KnowledgeTag("  Lunar-Eclipse  ");
        var same = new KnowledgeTag("lunar-eclipse");

        Assert.Equal("lunar-eclipse", tag.Value);
        Assert.Equal("lunar-eclipse", tag.ToString());
        Assert.Equal(same, tag);
        Assert.Equal(same.GetHashCode(), tag.GetHashCode());
        Assert.Throws<ArgumentException>(() => new KnowledgeTag(""));
        Assert.Throws<ArgumentException>(() => new KnowledgeTag("two words"));
        Assert.Throws<ArgumentException>(() => new KnowledgeTag("bad\u0001tag"));
        Assert.Throws<ArgumentException>(() => new KnowledgeTag(new string('a', 65)));
    }

    [Fact]
    public void Validity_ranges_require_utc_allow_equal_start_only_end_only_and_open_ranges()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);

        Assert.True(new KnowledgeValidityRange().Contains(start));
        Assert.True(new KnowledgeValidityRange(start).IsOpenEnded);
        Assert.True(new KnowledgeValidityRange(null, end).Contains(start));
        Assert.True(new KnowledgeValidityRange(start, start).Contains(start));
        Assert.False(new KnowledgeValidityRange(start, end).Contains(start.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => new KnowledgeValidityRange(end, start));
        Assert.Throws<ArgumentException>(() => new KnowledgeValidityRange(start.ToOffset(TimeSpan.FromHours(-5))));
        Assert.Throws<ArgumentException>(() => new KnowledgeValidityRange(start).Contains(start.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void Audit_metadata_requires_utc_times_actor_tokens_and_coherent_update_pairs()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var updated = created.AddHours(1);
        var audit = new KnowledgeAuditMetadata(created, " creator ", updated, " editor ");

        Assert.Equal(created, audit.CreatedUtc);
        Assert.Equal("creator", audit.CreatedBy);
        Assert.Equal(updated, audit.UpdatedUtc);
        Assert.Equal("editor", audit.UpdatedBy);
        Assert.Throws<ArgumentException>(() => new KnowledgeAuditMetadata(created.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => new KnowledgeAuditMetadata(created, "   "));
        Assert.Throws<ArgumentException>(() => new KnowledgeAuditMetadata(created, null, created.AddTicks(-1), "editor"));
        Assert.Throws<ArgumentException>(() => new KnowledgeAuditMetadata(created, null, updated, null));
        Assert.Throws<ArgumentException>(() => new KnowledgeAuditMetadata(created, null, null, "editor"));
    }

    private sealed class StubFamily(string familyId, AstronomyFamilyKind familyKind) : IAstronomyDomainFamily
    {
        public string FamilyId { get; } = familyId;
        public AstronomyFamilyKind FamilyKind { get; } = familyKind;
        public AstronomyDomainCategory DomainCategory => AstronomyDomainCategory.TransientEvent;
        public IReadOnlySet<AstronomyEntityKind> SupportedEntityKinds { get; } = new HashSet<AstronomyEntityKind> { AstronomyEntityKind.Event };
        public IReadOnlySet<string> SupportedEventTypeAliases { get; } = new HashSet<string>();
        public bool Supports(AstronomyEntityIdentity identity) => identity.FamilyId == FamilyId;
        public global::Astronomy.MediaFactory.Core.AstronomyDomain.Validation.DomainValidationResult ValidateEntity(global::Astronomy.MediaFactory.Core.AstronomyDomain.Entities.IAstronomyDomainEntity entity) => global::Astronomy.MediaFactory.Core.AstronomyDomain.Validation.DomainValidationResult.Success;
    }
}
