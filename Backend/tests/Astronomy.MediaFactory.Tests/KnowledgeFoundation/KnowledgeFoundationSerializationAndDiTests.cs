using System.Text.Json;
using Astronomy.MediaFactory.Core.AstronomyDomain.Extensions;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.Certification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class KnowledgeFoundationSerializationAndDiTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static JsonSerializerOptions Json => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyKnowledgePayload<SyntheticPayload>("synthetic.payload");

    [Fact]
    public void Primitives_round_trip_using_stable_scalar_shapes()
    {
        RoundTrip(new KnowledgeId("knowledge.synthetic.identity")).Value.Should().Be("knowledge.synthetic.identity");
        JsonSerializer.Serialize(new KnowledgeId("knowledge.synthetic.identity"), Json).Should().Be("\"knowledge.synthetic.identity\"");
        JsonSerializer.Serialize(new KnowledgeVersion(2), Json).Should().Be("2");
        JsonSerializer.Serialize(new KnowledgeLanguageTag("EN-us"), Json).Should().Be("\"en-US\"");
        JsonSerializer.Serialize(new KnowledgeTag("Moon"), Json).Should().Be("\"moon\"");
        RoundTrip(new KnowledgeValidityRange(Created, Created.AddDays(1))).Should().Be(new KnowledgeValidityRange(Created, Created.AddDays(1)));
        RoundTrip(new KnowledgeAuditMetadata(Created, "author", Created.AddHours(1), "reviewer")).Should().Be(new KnowledgeAuditMetadata(Created, "author", Created.AddHours(1), "reviewer"));
        RoundTrip(new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon")).Should().Be(new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"));
        RoundTrip(new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem)).Should().Be(new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem));
        RoundTrip(new KnowledgeLocalizationReference(new KnowledgeLanguageTag("hi"), "moon.name", true, false)).Should().Be(new KnowledgeLocalizationReference(new KnowledgeLanguageTag("hi"), "moon.name", true, false));
    }

    [Fact]
    public void Enums_serialize_as_stable_strings_not_numbers()
    {
        foreach (var kind in Enum.GetValues<KnowledgeStatementKind>()) JsonSerializer.Serialize(kind, Json).Should().Be($"\"{kind}\"");
        foreach (var status in Enum.GetValues<KnowledgeFoundationStatus>()) JsonSerializer.Serialize(status, Json).Should().Be($"\"{status}\"");
    }

    [Fact]
    public void Complete_statement_round_trips_and_validates_without_clr_metadata()
    {
        var statement = CreateStatement();
        var json = JsonSerializer.Serialize(statement, Json);
        json.Should().Contain("\"id\"").And.Contain("\"payloadKind\":\"synthetic.payload\"").And.NotContain("$type").And.NotContain("AssemblyQualifiedName").And.NotContain("Evidence").And.NotContain("Confidence");

        var roundTrip = JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(json, Json)!;
        roundTrip.HasSameVersionIdentityAs(statement).Should().BeTrue();
        roundTrip.Payload.Should().Be(statement.Payload);
        roundTrip.PrimarySubject.Should().Be(statement.PrimarySubject);
        roundTrip.FamilyContext.Should().Be(statement.FamilyContext);
        roundTrip.Tags.Select(t => t.Value).Should().Equal("lunar", "moon");
        roundTrip.LocalizationReferences.Select(l => $"{l.LanguageTag}:{l.ResourceKey}").Should().Equal("en-US:moon.name", "hi:moon.name");
        roundTrip.Validity.Should().Be(statement.Validity);
        roundTrip.Audit.Should().Be(statement.Audit);
        new AstronomyKnowledgeStatementValidator().Validate(roundTrip).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Malformed_scalar_primitives_are_rejected(string json) => new Action(() => JsonSerializer.Deserialize<KnowledgeId>(json, Json)).Should().Throw<JsonException>();

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Invalid_versions_are_rejected(string json) => new Action(() => JsonSerializer.Deserialize<KnowledgeVersion>(json, Json)).Should().Throw<JsonException>();

    [Fact]
    public void Malformed_statement_state_is_rejected()
    {
        var json = JsonSerializer.Serialize(CreateStatement(), Json);
        foreach (var required in new[] { "id", "version", "kind", "status", "primarySubject", "payload", "audit" })
            new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(RemoveProperty(json, required), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(json.Replace("\"Scientific\"", "\"Unknown\""), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(json.Replace("\"Draft\"", "\"Unknown\""), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeLanguageTag>("\"bad tag\"", Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeTag>("\"bad tag\"", Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeValidityRange>("{\"effectiveFromUtc\":\"2026-01-02T00:00:00+00:00\",\"effectiveToUtc\":\"2026-01-01T00:00:00+00:00\"}", Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeValidityRange>("{\"effectiveFromUtc\":\"2026-01-01T00:00:00+05:30\"}", Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeAuditMetadata>("{\"createdUtc\":\"2026-01-02T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedBy\":\"a\"}", Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Invalid_statement_collections_and_payload_discriminators_are_rejected()
    {
        // Constructor rejects duplicate tags before this point, so exercise serialized shapes directly.
        var baseJson = JsonSerializer.Serialize(CreateStatement(tags: [new("Moon")]), Json);
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(baseJson.Replace("\"tags\":[\"moon\"]", "\"tags\":[\"moon\",\"moon\"]"), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(baseJson.Replace("\"tags\":[\"moon\"]", "\"tags\":[null]"), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(baseJson.Replace("\"localizationReferences\":[", "\"localizationReferences\":[null,"), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(baseJson.Replace("synthetic.payload", "unknown.payload"), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyKnowledgeStatement<SyntheticPayload>>(baseJson.Replace("\"payloadKind\":\"synthetic.payload\",", ""), Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_configuration_is_idempotent_and_preserves_task1_serialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyKnowledgePayload<SyntheticPayload>("synthetic.payload").AddAstronomyKnowledgePayload<SyntheticPayload>("synthetic.payload");
        options.Converters.Count(c => c is KnowledgeIdJsonConverter).Should().Be(1);
        JsonSerializer.Serialize(AstronomyEntityKind.DwarfPlanet, options).Should().Be("\"DwarfPlanet\"");
    }

    [Fact]
    public void Dependency_injection_registers_only_current_knowledge_foundation_services_and_combines_with_existing_foundations()
    {
        var services = new ServiceCollection().AddCgA2AstronomyKnowledgeFoundation().AddCgA2AstronomyKnowledgeFoundation().AddCgA2AstronomyDomainFoundation().AddCgA1CertificationFoundation();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAstronomyKnowledgeStatementValidator>().Should().BeOfType<AstronomyKnowledgeStatementValidator>();
        provider.GetRequiredService<AstronomyKnowledgeStatementValidator>().Should().NotBeNull();
        provider.GetServices<IAstronomyKnowledgeStatementValidator>().Should().HaveCount(1);
        services.Any(d => d.ServiceType.Name.Contains("Evidence", StringComparison.OrdinalIgnoreCase) || d.ServiceType.Name.Contains("Confidence", StringComparison.OrdinalIgnoreCase) || d.ServiceType.Name.Contains("Catalog", StringComparison.OrdinalIgnoreCase) && d.ServiceType.Namespace?.Contains("KnowledgeFoundation") == true || d.ServiceType.Name.Contains("Query", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Fact]
    public void Architecture_boundary_has_no_later_task_or_infrastructure_leakage()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/KnowledgeFoundation"));
        var text = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        text.Should().NotMatchRegex(@"using\s+Astronomy\.MediaFactory\.(Infrastructure|Publishing|Rendering|Api|AIOptimization|ContentGen)\b");
        text.Should().NotMatchRegex(@"\b(Type\.GetType|Activator\.CreateInstance|Assembly\.Load|AssemblyQualifiedName|DbContext|IQueryable|Renderer|Publisher|CertificationCoordinator|TTS|SRT|Orion|Constellation)\b");
        text.Should().NotMatchRegex(@"\b(Evidence|Confidence|Transition|Supersedes|Relationship|KnowledgeCatalog|KnowledgeQuery)\b");
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static AstronomyKnowledgeStatement<SyntheticPayload> CreateStatement(IEnumerable<KnowledgeTag>? tags = null) => new(new KnowledgeId("knowledge.moon.identity"), KnowledgeVersion.Initial, KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Draft, new AstronomyEntityReference("moon", AstronomyEntityKind.Moon, "Moon"), new SyntheticPayload("moon.meaning", 42), new KnowledgeAuditMetadata(Created, "author", Created.AddHours(1), "reviewer"), new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem), [new(new KnowledgeLanguageTag("HI"), "moon.name", true), new(new KnowledgeLanguageTag("en-us"), "moon.name", false, true)], tags ?? [new KnowledgeTag("Moon"), new KnowledgeTag("Lunar")], new KnowledgeValidityRange(Created, Created.AddDays(1)));
    private static string RemoveProperty(string json, string propertyName) { using var doc = JsonDocument.Parse(json); using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); foreach (var p in doc.RootElement.EnumerateObject()) if (!string.Equals(p.Name, propertyName, StringComparison.Ordinal)) p.WriteTo(writer); writer.WriteEndObject(); } return System.Text.Encoding.UTF8.GetString(stream.ToArray()); }
    private sealed record SyntheticPayload(string SemanticKey, int Weight) : IAstronomyKnowledgePayload;
}
