using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class EvidenceFoundationPrimitivesTests
{
    private static readonly DateTimeOffset Observed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Retrieved = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("evidence.synthetic.lunar-distance", "evidence.synthetic.lunar-distance")]
    [InlineData("  evidence.synthetic.lunar-distance  ", "evidence.synthetic.lunar-distance")]
    [InlineData("Evidence.Synthetic.LunarDistance", "Evidence.Synthetic.LunarDistance")]
    public void Evidence_id_follows_knowledge_token_semantics(string input, string expected)
    {
        var id = new EvidenceId(input);
        Assert.Equal(expected, id.Value);
        Assert.Equal(expected, id.ToString());
        Assert.Equal(new EvidenceId(expected), id);
        Assert.True(EvidenceId.TryParse(input, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("evidence moon")]
    [InlineData("evidence\tmoon")]
    [InlineData("evidence\nmoon")]
    [InlineData("evidence\u0001moon")]
    public void Evidence_id_rejects_blank_whitespace_and_control_characters(string? value)
    {
        Assert.Throws<ArgumentException>(() => new EvidenceId(value!));
        Assert.False(EvidenceId.TryParse(value, out _));
    }

    [Fact]
    public void Evidence_id_rejects_excessive_length_and_default_is_observably_invalid()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceId(new string('a', EvidenceId.MaxLength + 1)));
        Assert.Equal(string.Empty, default(EvidenceId).ToString());
        Assert.True(string.IsNullOrWhiteSpace(default(EvidenceId).Value));
    }

    [Fact]
    public void Evidence_taxonomies_match_task_2_2a_boundary_and_guards_reject_undefined_values()
    {
        Assert.Equal(["Observation", "Measurement", "CatalogRecord", "Ephemeris", "ResearchPublication", "ReferencePublication", "InstitutionalDataset", "HistoricalRecord", "ExpertAssessment", "DerivedAnalysis"], Enum.GetNames<AstronomyEvidenceType>());
        Assert.Equal(["Observatory", "SpaceAgency", "ResearchInstitution", "AcademicPublication", "ScientificCatalog", "EphemerisService", "Instrument", "Researcher", "HistoricalArchive", "EducationalInstitution", "Other"], Enum.GetNames<AstronomyEvidenceSourceType>());
        Assert.Equal(["Draft", "Verified", "Disputed", "Superseded", "Withdrawn", "Archived"], Enum.GetNames<EvidenceFoundationStatus>());
        Assert.Equal(Enum.GetValues<AstronomyEvidenceType>().Length, Enum.GetValues<AstronomyEvidenceType>().Distinct().Count());
        Assert.Equal(Enum.GetValues<AstronomyEvidenceSourceType>().Length, Enum.GetValues<AstronomyEvidenceSourceType>().Distinct().Count());
        Assert.Equal(Enum.GetValues<EvidenceFoundationStatus>().Length, Enum.GetValues<EvidenceFoundationStatus>().Distinct().Count());
        foreach (var value in Enum.GetValues<AstronomyEvidenceType>()) Assert.Equal(value, EvidenceFoundationEnumGuard.RequireDefined(value));
        foreach (var value in Enum.GetValues<AstronomyEvidenceSourceType>()) Assert.Equal(value, EvidenceFoundationEnumGuard.RequireDefined(value));
        foreach (var value in Enum.GetValues<EvidenceFoundationStatus>()) Assert.Equal(value, EvidenceFoundationEnumGuard.RequireDefined(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceFoundationEnumGuard.RequireDefined((AstronomyEvidenceType)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceFoundationEnumGuard.RequireDefined((AstronomyEvidenceSourceType)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceFoundationEnumGuard.RequireDefined((EvidenceFoundationStatus)999));
    }

    [Fact]
    public void External_identifier_normalizes_scheme_preserves_value_case_and_compares_by_scheme_and_value()
    {
        var id = new EvidenceExternalIdentifier(" DOI ", " 10.1234/Example ");
        Assert.Equal("doi", id.Scheme);
        Assert.Equal("10.1234/Example", id.Value);
        Assert.Equal("doi:10.1234/Example", id.ToString());
        Assert.Equal(new EvidenceExternalIdentifier("doi", "10.1234/Example"), id);
        Assert.NotEqual(new EvidenceExternalIdentifier("doi", "10.1234/example"), id);
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier("", "value"));
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier("doi", ""));
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier("bad\u0001", "value"));
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier("doi", "bad\u0001"));
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier(new string('a', EvidenceExternalIdentifier.MaxSchemeLength + 1), "value"));
        Assert.Throws<ArgumentException>(() => new EvidenceExternalIdentifier("doi", new string('a', EvidenceExternalIdentifier.MaxValueLength + 1)));
    }

    [Fact]
    public void Source_reference_requires_stable_identity_defined_type_name_and_https_absolute_credentialless_uri()
    {
        var external = new EvidenceExternalIdentifier("catalog", "Record-1");
        var source = new AstronomyEvidenceSourceReference(" source.synthetic ", AstronomyEvidenceSourceType.Observatory, " Synthetic Observatory ", new Uri("https://example.test/archive"), external);
        Assert.Equal("source.synthetic", source.SourceId);
        Assert.Equal("Synthetic Observatory", source.DisplayName);
        Assert.Equal(new Uri("https://example.test/archive"), source.CanonicalUri);
        Assert.Equal(external, source.ExternalIdentifier);
        Assert.Equal(new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.Observatory, "Synthetic Observatory", new Uri("https://example.test/archive"), external), source);
        Assert.Throws<ArgumentException>(() => new AstronomyEvidenceSourceReference("", AstronomyEvidenceSourceType.Observatory, "Name"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEvidenceSourceReference("source.synthetic", (AstronomyEvidenceSourceType)999, "Name"));
        Assert.Throws<ArgumentException>(() => new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.Observatory, "   "));
        Assert.Throws<ArgumentException>(() => new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.Observatory, "Name", new Uri("relative/path", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() => new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.Observatory, "Name", new Uri("http://example.test")));
        Assert.Throws<ArgumentException>(() => new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.Observatory, "Name", new Uri("https://user:pass@example.test")));
    }

    [Fact]
    public void Attribution_preserves_order_copies_callers_rejects_blank_and_duplicate_contributors_and_normalizes_optional_fields()
    {
        var contributors = new List<string> { " Alice ", "Bob" };
        var attribution = new EvidenceAttribution(contributors, organizationName: " Org ", publisherName: "  ", publicationTitle: " Title ", editionOrVersion: " v1 ");
        contributors[0] = "Mallory";
        contributors.Add("Eve");
        Assert.Equal(["Alice", "Bob"], attribution.Contributors);
        Assert.Equal("Org", attribution.OrganizationName);
        Assert.Null(attribution.PublisherName);
        Assert.Equal("Title", attribution.PublicationTitle);
        Assert.Equal("v1", attribution.EditionOrVersion);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(attribution.Contributors);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)attribution.Contributors).Add("Eve"));
        Assert.Equal(new EvidenceAttribution(["Alice", "Bob"], "Org", publicationTitle: "Title", editionOrVersion: "v1"), attribution);
        Assert.NotEqual(new EvidenceAttribution(["Bob", "Alice"], "Org", publicationTitle: "Title", editionOrVersion: "v1"), attribution);
        Assert.Throws<ArgumentException>(() => new EvidenceAttribution(["Alice", "Alice"]));
        Assert.Throws<ArgumentException>(() => new EvidenceAttribution(["Alice", "  "]));
        Assert.Throws<ArgumentException>(() => new EvidenceAttribution(["bad\u0001"]));
    }

    [Fact]
    public void Temporal_metadata_requires_utc_deterministic_values_and_reuses_knowledge_validity_range_for_applicability()
    {
        var applicability = new KnowledgeValidityRange(Observed, Retrieved);
        var temporal = new EvidenceTemporalMetadata(Observed, Published, Retrieved, applicability);
        Assert.Equal(Observed, temporal.ObservedAtUtc);
        Assert.Equal(Published, temporal.PublishedAtUtc);
        Assert.Equal(Retrieved, temporal.RetrievedAtUtc);
        Assert.Equal(applicability, temporal.Applicability);
        Assert.True(new EvidenceTemporalMetadata(observedAtUtc: Observed).Applicability.IsOpenEnded);
        Assert.Null(new EvidenceTemporalMetadata().RetrievedAtUtc);
        Assert.Throws<ArgumentException>(() => new EvidenceTemporalMetadata(Observed.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => new EvidenceTemporalMetadata(publishedAtUtc: Retrieved, retrievedAtUtc: Published));
        Assert.Throws<ArgumentException>(() => new EvidenceTemporalMetadata(applicability: new KnowledgeValidityRange(Retrieved, Observed)));
    }

    [Fact]
    public void Evidence_foundation_scope_excludes_records_associations_confidence_services_serialization_di_persistence_and_clients()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Evidence"));
        var text = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly).Select(File.ReadAllText));
        Assert.DoesNotContain("KnowledgeConfidence", text, StringComparison.Ordinal);
        Assert.Contains("EvidenceId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonConverter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceCollection", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", text, StringComparison.Ordinal);
    }
}
