using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class EvidenceAndConfidenceSerializationTests
{
    private static readonly DateTimeOffset Created = new(2026,1,1,0,0,0,TimeSpan.Zero);
    private static JsonSerializerOptions Json => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyEvidenceAndConfidenceJson();

    [Fact]
    public void Strong_ids_and_score_use_stable_scalar_shapes_and_reject_bad_tokens()
    {
        JsonSerializer.Serialize(new EvidenceId("evidence.synthetic.a"), Json).Should().Be("\"evidence.synthetic.a\"");
        JsonSerializer.Deserialize<EvidenceId>("\"evidence.synthetic.a\"", Json).Value.Should().Be("evidence.synthetic.a");
        JsonSerializer.Serialize(new ConfidenceAssessmentId("confidence.synthetic.moon.v1"), Json).Should().Be("\"confidence.synthetic.moon.v1\"");
        JsonSerializer.Serialize(new KnowledgeConfidenceScore(.5), Json).Should().Be("0.5");
        JsonSerializer.Deserialize<KnowledgeConfidenceScore>("0", Json).Value.Should().Be(0);
        JsonSerializer.Deserialize<KnowledgeConfidenceScore>("1", Json).Value.Should().Be(1);
        foreach (var bad in new[] { "null", "\"\"", "\"bad id\"", "42", "{}" }) new Action(() => JsonSerializer.Deserialize<EvidenceId>(bad, Json)).Should().Throw<JsonException>();
        foreach (var bad in new[] { "-0.1", "1.1", "\"0.5\"", "null" }) new Action(() => JsonSerializer.Deserialize<KnowledgeConfidenceScore>(bad, Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Task22_enums_are_strict_symbolic_names()
    {
        AssertStrictEnum(AstronomyEvidenceType.Observation, "Observation", "observation");
        AssertStrictEnum(AstronomyEvidenceSourceType.Observatory, "Observatory", "observatory");
        AssertStrictEnum(EvidenceFoundationStatus.Verified, "Verified", "verified");
        AssertStrictEnum(KnowledgeEvidenceRole.Primary, "Primary", "primary");
        AssertStrictEnum(KnowledgeConfidenceLevel.High, "High", "high");
        AssertStrictEnum(ConfidenceAssessmentMethod.HumanExpertReview, "HumanExpertReview", "humanExpertReview");
        AssertStrictEnum(ConfidenceAssessorType.HumanExpert, "HumanExpert", "humanExpert");
        AssertStrictEnum(ConfidenceFactorDirection.Supports, "Supports", "supports");
        new Action(() => JsonSerializer.Serialize((AstronomyEvidenceType)999, Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Task22_converter_registration_is_idempotent_and_precedes_enum_fallback()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        options.AddAstronomyEvidenceAndConfidenceJson().AddAstronomyEvidenceAndConfidenceJson();

        AssertSingleConverter<StrictAstronomyEvidenceTypeJsonConverter>(options);
        AssertSingleConverter<StrictAstronomyEvidenceSourceTypeJsonConverter>(options);
        AssertSingleConverter<StrictEvidenceFoundationStatusJsonConverter>(options);
        AssertSingleConverter<StrictKnowledgeEvidenceRoleJsonConverter>(options);
        AssertSingleConverter<StrictKnowledgeConfidenceLevelJsonConverter>(options);
        AssertSingleConverter<StrictConfidenceAssessmentMethodJsonConverter>(options);
        AssertSingleConverter<StrictConfidenceAssessorTypeJsonConverter>(options);
        AssertSingleConverter<StrictConfidenceFactorDirectionJsonConverter>(options);
        StrictConvertersShouldPrecedeFallback(options);

        var reverseOrderOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .AddAstronomyKnowledgeFoundationJson()
            .AddAstronomyEvidenceAndConfidenceJson();
        StrictConvertersShouldPrecedeFallback(reverseOrderOptions);
    }

    [Fact]
    public void Di_resolved_options_apply_same_task22_strict_enum_behavior()
    {
        using var provider = new ServiceCollection().AddCgA2AstronomyKnowledgeFoundation().BuildServiceProvider();
        var options = provider.GetRequiredService<JsonSerializerOptions>();

        JsonSerializer.Serialize(AstronomyEvidenceType.Observation, options).Should().Be("\"Observation\"");
        new Action(() => JsonSerializer.Deserialize<AstronomyEvidenceType>("\"observation\"", options)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyEvidenceType>("0", options)).Should().Throw<JsonException>();
        StrictConvertersShouldPrecedeFallback(options);
    }

    [Fact]
    public void Task21_and_task1_enum_serialization_contracts_are_preserved()
    {
        JsonSerializer.Serialize(AstronomyEntityKind.Planet, Json).Should().Be("\"Planet\"");
        JsonSerializer.Serialize(AstronomyFamilyKind.Event, Json).Should().Be("\"Event\"");
        new Action(() => JsonSerializer.Deserialize<KnowledgeStatementKind>("0", Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<KnowledgeFoundationStatus>("0", Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Evidence_record_round_trips_with_stable_shape_and_strict_properties()
    {
        var record = Record();
        var json = JsonSerializer.Serialize(record, Json);
        json.Should().Contain("\"id\":\"evidence.synthetic.a\"").And.Contain("\"canonicalUri\":\"https://example.test/evidence\"").And.NotContain("$type");
        var copy = JsonSerializer.Deserialize<AstronomyEvidenceRecord>(json, Json)!;
        copy.Id.Should().Be(record.Id); copy.Type.Should().Be(record.Type); copy.Status.Should().Be(record.Status); copy.Source.Should().Be(record.Source); copy.TemporalMetadata.PublishedAtUtc.Should().Be(record.TemporalMetadata.PublishedAtUtc); copy.Audit.Should().Be(record.Audit); copy.ExternalIdentifiers.Should().Equal(record.ExternalIdentifiers); copy.Tags.Should().Equal(record.Tags);
        JsonSerializer.Serialize(record, Json).Should().Be(json);
        new Action(() => JsonSerializer.Deserialize<AstronomyEvidenceRecord>(json.Replace("\"tags\":[\"moon\"]", "\"tags\":[\"moon\",\"moon\"]"), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyEvidenceRecord>(json.Replace("\"title\":\"Synthetic title\"", "\"unknown\":1,\"title\":\"Synthetic title\""), Json)).Should().Throw<JsonException>();
        new Action(() => JsonSerializer.Deserialize<AstronomyEvidenceRecord>(json.Replace("\"id\":\"evidence.synthetic.a\"", "\"id\":\"evidence.synthetic.a\",\"id\":\"evidence.synthetic.b\""), Json)).Should().Throw<JsonException>();
    }

    [Fact]
    public void Evidence_set_and_confidence_assessment_round_trip_and_policy_validation_remains_explicit()
    {
        var set = new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(1), [new(new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(1), new EvidenceId("evidence.synthetic.a"), KnowledgeEvidenceRole.Primary)]);
        var assessment = Assessment();
        var setCopy = JsonSerializer.Deserialize<AstronomyKnowledgeStatementEvidenceSet>(JsonSerializer.Serialize(set, Json), Json)!;
        var assessmentCopy = JsonSerializer.Deserialize<AstronomyKnowledgeConfidenceAssessment>(JsonSerializer.Serialize(assessment, Json), Json)!;
        setCopy.Should().Be(set);
        assessmentCopy.Id.Should().Be(assessment.Id); assessmentCopy.KnowledgeId.Should().Be(assessment.KnowledgeId); assessmentCopy.Score.Should().Be(assessment.Score); assessmentCopy.Assessor.Should().Be(assessment.Assessor); assessmentCopy.EvidenceIds.Should().Equal(assessment.EvidenceIds); assessmentCopy.Factors.Should().Equal(assessment.Factors); assessmentCopy.Rationale.Should().Be(assessment.Rationale);
        new AstronomyEvidenceConfidenceConsistencyValidator().Validate(assessmentCopy, setCopy).IsValid.Should().BeTrue();
        new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(assessmentCopy).IsValid.Should().BeTrue();
        var policyInvalid = JsonSerializer.Deserialize<AstronomyKnowledgeConfidenceAssessment>(JsonSerializer.Serialize(assessment, Json).Replace("\"level\":\"High\"", "\"level\":\"Unknown\""), Json)!;
        new AstronomyKnowledgeConfidenceAssessmentValidator().Validate(policyInvalid).Issues.Should().Contain(i => i.Code == AstronomyEvidenceValidationCodes.AssessmentUnknownLevelHasScore);
    }

    private static void AssertStrictEnum<TEnum>(TEnum value, string exact, string wrongCase) where TEnum : struct, Enum
    {
        JsonSerializer.Serialize(value, Json).Should().Be($"\"{exact}\"");
        JsonSerializer.Deserialize<TEnum>($"\"{exact}\"", Json).Should().Be(value);
        foreach (var bad in new[] { "0", $"\"{wrongCase}\"", "\"UnknownValue\"", "null" })
            new Action(() => JsonSerializer.Deserialize<TEnum>(bad, Json)).Should().Throw<JsonException>();
    }

    private static void AssertSingleConverter<TConverter>(JsonSerializerOptions options) where TConverter : JsonConverter
        => options.Converters.Count(converter => converter.GetType() == typeof(TConverter)).Should().Be(1);

    private static void StrictConvertersShouldPrecedeFallback(JsonSerializerOptions options)
    {
        var fallbackIndex = options.Converters.Select((converter, index) => new { converter, index }).First(entry => entry.converter is JsonStringEnumConverter).index;
        foreach (var converterType in new[]
        {
            typeof(StrictAstronomyEvidenceTypeJsonConverter),
            typeof(StrictAstronomyEvidenceSourceTypeJsonConverter),
            typeof(StrictEvidenceFoundationStatusJsonConverter),
            typeof(StrictKnowledgeEvidenceRoleJsonConverter),
            typeof(StrictKnowledgeConfidenceLevelJsonConverter),
            typeof(StrictConfidenceAssessmentMethodJsonConverter),
            typeof(StrictConfidenceAssessorTypeJsonConverter),
            typeof(StrictConfidenceFactorDirectionJsonConverter)
        })
        {
            options.Converters.Select((converter, index) => new { converter, index }).Single(entry => entry.converter.GetType() == converterType).index.Should().BeLessThan(fallbackIndex);
        }
    }

    private static AstronomyEvidenceRecord Record() => new(new EvidenceId("evidence.synthetic.a"), AstronomyEvidenceType.Observation, EvidenceFoundationStatus.Verified, new AstronomyEvidenceSourceReference("source.synthetic", AstronomyEvidenceSourceType.SpaceAgency, "Synthetic Source", new Uri("https://example.test/evidence"), new EvidenceExternalIdentifier("doi", "10.test/example")), new EvidenceTemporalMetadata(publishedAtUtc: Created, retrievedAtUtc: Created.AddHours(1)), new KnowledgeAuditMetadata(Created, "author"), new EvidenceAttribution(["Contributor"], organizationName:"Org"), "Synthetic title", "Synthetic summary", [new EvidenceExternalIdentifier("bibcode", "2026Test")], [new KnowledgeTag("moon")]);
    private static AstronomyKnowledgeConfidenceAssessment Assessment() => new(new ConfidenceAssessmentId("confidence.synthetic.moon.v1"), new KnowledgeId("knowledge.synthetic.moon"), new KnowledgeVersion(1), KnowledgeConfidenceLevel.High, new KnowledgeConfidenceScore(.82), ConfidenceAssessmentMethod.HumanExpertReview, new ConfidenceAssessorReference("expert.synthetic.one", ConfidenceAssessorType.HumanExpert, "Synthetic Expert"), new KnowledgeAuditMetadata(Created, "author"), [new EvidenceId("evidence.synthetic.a")], [new ConfidenceAssessmentFactor("multiple-independent-sources", ConfidenceFactorDirection.Supports)], "Synthetic rationale.");
}
