using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class CanonicalSemanticContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SemanticCapabilityDefinition_Constructs_And_IsValueEqual()
    {
        var left = Definition();
        var right = Definition();

        Assert.Equal(left, right);
        Assert.Equal("ObservationTiming", left.CapabilityId);
        Assert.Equal(SemanticCapabilityStrictness.Strict, left.Strictness);
    }

    [Fact]
    public void Contracts_Serialize_WithStableCamelCaseEnumNames_AndRoundTrip()
    {
        var resolution = new SemanticCapabilityResolution(
            "ObservationTiming",
            SemanticCapabilityResolutionStatus.Resolved,
            "ObservationMetadataAdapter",
            "before dawn",
            "before dawn",
            new[] { "dawn" },
            Array.Empty<string>(),
            "Strong",
            new[] { new SemanticCapabilityCandidate("ObservationMetadata", "localPeakTime", "before dawn", "Strong") },
            Array.Empty<SemanticCapabilityRejection>(),
            Array.Empty<string>());

        var json = JsonSerializer.Serialize(resolution, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<SemanticCapabilityResolution>(json, JsonOptions);

        Assert.Contains("\"capability\":\"ObservationTiming\"", json);
        Assert.Contains("\"status\":\"Resolved\"", json);
        Assert.Equal(resolution, roundTrip);
    }

    [Theory]
    [InlineData(SemanticVerificationStatus.Verified)]
    [InlineData(SemanticVerificationStatus.Derived)]
    [InlineData(SemanticVerificationStatus.Missing)]
    public void Enums_RoundTrip_AsStrings(SemanticVerificationStatus status)
    {
        var fact = Fact(status: status);
        var json = JsonSerializer.Serialize(fact, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<ResolvedSemanticFact>(json, JsonOptions);

        Assert.Contains($"\"verificationStatus\":\"{status}\"", json);
        Assert.Equal(status, roundTrip!.VerificationStatus);
    }

    [Fact]
    public void NullableAnnotations_Mark_OnlyOptionalContractMembersNullable()
    {
        var context = new NullabilityInfoContext();
        var sourceBeat = typeof(ResolvedSemanticFact).GetProperty(nameof(ResolvedSemanticFact.SourceBeatId))!;
        var language = typeof(ResolvedSemanticFact).GetProperty(nameof(ResolvedSemanticFact.Language))!;
        var selectedSource = typeof(SemanticCapabilityResolution).GetProperty(nameof(SemanticCapabilityResolution.SelectedSource))!;

        Assert.Equal(NullabilityState.Nullable, context.Create(sourceBeat).ReadState);
        Assert.Equal(NullabilityState.NotNull, context.Create(language).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(selectedSource).ReadState);
    }

    [Fact]
    public void Validation_Invariants_RejectInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => new SemanticCapabilityDefinition(" ", ["Alias"], 1, SemanticCapabilityStrictness.Strict, true, true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticCapabilityDefinition("Capability", ["Alias"], -1, SemanticCapabilityStrictness.Strict, true, true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fact(confidence: 1.1m));
        Assert.Throws<ArgumentNullException>(() => new RequiredSemanticFactResolutionResult(Array.Empty<ResolvedBeatFacts>(), null!));
    }

    [Fact]
    public void ListInputs_AreSnapshotForImmutability()
    {
        var aliases = new List<string> { "ObservationTiming" };
        var definition = new SemanticCapabilityDefinition("ObservationTiming", aliases, 75, SemanticCapabilityStrictness.Strict, true, true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        aliases.Add("MutatedAlias");

        Assert.Single(definition.AcceptedAliases);
        Assert.DoesNotContain("MutatedAlias", definition.AcceptedAliases);
    }

    [Fact]
    public void IndependentlyAllocatedCollectionContents_AreStructurallyEqual()
    {
        var left = Definition(["ObservationTiming", "LocalPeakTime"]);
        var right = Definition(new List<string> { "ObservationTiming", "LocalPeakTime" });

        Assert.Equal(left, right);
    }

    [Fact]
    public void DifferentCollectionContents_AreNotEqual()
    {
        var left = Definition(["ObservationTiming", "LocalPeakTime"]);
        var right = Definition(["ObservationTiming", "TwilightWindow"]);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void EqualContracts_ProduceEqualHashCodes()
    {
        var left = Definition(["ObservationTiming", "LocalPeakTime"]);
        var right = Definition(new List<string> { "ObservationTiming", "LocalPeakTime" });

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void SourceInputMutation_DoesNotAffectResolvedFactContract()
    {
        var sourceInputs = new List<string> { "metadata.localPeakTime" };
        var fact = Fact(sourceInputs: sourceInputs);

        sourceInputs.Add("metadata.mutated");

        Assert.Equal(new[] { "metadata.localPeakTime" }, fact.SourceInputs!.Value.ToArray());
    }

    [Fact]
    public void JsonRoundTrip_PreservesStructuralEqualityForCollectionContracts()
    {
        var result = new RequiredSemanticFactResolutionResult(
            new[] { new ResolvedBeatFacts("beat-1", "Observation", new[] { Fact(sourceInputs: new[] { "metadata.localPeakTime" }) }) },
            new SemanticResolutionDiagnostics(0, 0, Array.Empty<string>()));

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<RequiredSemanticFactResolutionResult>(json, JsonOptions);

        Assert.Equal(result, roundTrip);
    }

    [Fact]
    public void NestedCandidateAndRejectionCollections_AreStructurallyEqual()
    {
        static SemanticCapabilityResolution Resolution() => new(
            "ObservationTiming",
            SemanticCapabilityResolutionStatus.Rejected,
            null,
            null,
            null,
            new[] { "civil dawn" },
            new[] { "weak source" },
            "Weak",
            new[] { new SemanticCapabilityCandidate("ObservationMetadata", "localPeakTime", "civil dawn", "Weak") },
            new[] { new SemanticCapabilityRejection("QuestionAnswerSet", "answer[0]", "Unapproved source") },
            new[] { "astronomical dawn" });

        Assert.Equal(Resolution(), Resolution());
        Assert.Equal(Resolution().GetHashCode(), Resolution().GetHashCode());
    }


    [Fact]
    public void CollectionBearingContracts_RoundTrip_WithEmptyImmutableCollections()
    {
        var definition = new SemanticCapabilityDefinition("ObservationTiming", ["ObservationTiming"], 75, SemanticCapabilityStrictness.Strict, true, true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var resolution = new SemanticCapabilityResolution("ObservationTiming", SemanticCapabilityResolutionStatus.Missing, null, null, null, Array.Empty<string>(), Array.Empty<string>(), "None", Array.Empty<SemanticCapabilityCandidate>(), Array.Empty<SemanticCapabilityRejection>(), Array.Empty<string>());
        var beat = new ResolvedBeatFacts("beat-1", "Observation", Array.Empty<ResolvedSemanticFact>());
        var diagnostics = new SemanticResolutionDiagnostics(0, 0, Array.Empty<string>());
        var result = new RequiredSemanticFactResolutionResult(Array.Empty<ResolvedBeatFacts>(), diagnostics);

        Assert.Equal(definition, RoundTrip(definition));
        Assert.Equal(resolution, RoundTrip(resolution));
        Assert.Equal(beat, RoundTrip(beat));
        Assert.Equal(diagnostics, RoundTrip(diagnostics));
        Assert.Equal(result, RoundTrip(result));
    }

    [Fact]
    public void DiagnosticsPayload_RoundTrips_AsStableContract()
    {
        var diagnostics = new SemanticResolutionDiagnostics(2, 1, new[] { "weak source", "missing timing" });

        var json = JsonSerializer.Serialize(diagnostics, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<SemanticResolutionDiagnostics>(json, JsonOptions);

        Assert.Contains("\"warningCount\":2", json);
        Assert.Contains("\"missingRequiredCount\":1", json);
        Assert.Equal(diagnostics, roundTrip);
    }

    [Fact]
    public void NullOptionalProperties_Deserialize_AsNull()
    {
        var fact = Fact(sourceInputs: null);
        var resolution = new SemanticCapabilityResolution("ObservationTiming", SemanticCapabilityResolutionStatus.Missing, null, null, null, Array.Empty<string>(), Array.Empty<string>(), "None", Array.Empty<SemanticCapabilityCandidate>(), Array.Empty<SemanticCapabilityRejection>(), Array.Empty<string>());

        var factRoundTrip = RoundTrip(fact);
        var resolutionRoundTrip = RoundTrip(resolution);

        Assert.Null(factRoundTrip!.Unit);
        Assert.Null(factRoundTrip.SourceBeatId);
        Assert.Null(factRoundTrip.SourceInputs);
        Assert.Null(resolutionRoundTrip!.SelectedSource);
        Assert.Null(resolutionRoundTrip.CanonicalValue);
        Assert.Null(resolutionRoundTrip.SpeakableValue);
    }

    [Fact]
    public void NestedCandidatesAndRejections_RoundTripStructurally()
    {
        var resolution = new SemanticCapabilityResolution(
            "ObservationTiming",
            SemanticCapabilityResolutionStatus.Rejected,
            null,
            "civil dawn",
            null,
            new[] { "civil dawn", "astronomical dawn" },
            new[] { "weak source" },
            "Weak",
            new[] { new SemanticCapabilityCandidate("ObservationMetadata", "localPeakTime", "civil dawn", "Weak") },
            new[] { new SemanticCapabilityRejection("QuestionAnswerSet", "answer[0]", "Unapproved source") },
            new[] { "astronomical dawn" });

        Assert.Equal(resolution, RoundTrip(resolution));
    }

    [Fact]
    public void JsonConstructorParameters_BindToMatchingProperties()
    {
        var contractTypes = typeof(SemanticCapabilityDefinition).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(SemanticCapabilityDefinition).Namespace)
            .Where(type => type.IsPublic && type.IsClass && type.GetConstructors().Any(constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null))
            .ToArray();

        Assert.NotEmpty(contractTypes);

        foreach (var type in contractTypes)
        {
            foreach (var constructor in type.GetConstructors().Where(constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null))
            {
                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var parameter in constructor.GetParameters())
                {
                    Assert.True(properties.TryGetValue(parameter.Name!, out var property), $"{type.Name}.{parameter.Name} has no matching public property.");
                    Assert.Equal(property!.PropertyType, parameter.ParameterType);
                }

                Assert.All(properties.Values, property => Assert.Contains(constructor.GetParameters(), parameter => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    [Fact]
    public void CollectionOrder_IsSemanticAndPreservedInEquality()
    {
        var left = Definition(["ObservationTiming", "LocalPeakTime"]);
        var right = Definition(["LocalPeakTime", "ObservationTiming"]);

        Assert.NotEqual(left, right);
        Assert.Equal(new[] { "ObservationTiming", "LocalPeakTime" }, left.AcceptedAliases.ToArray());
    }

    private static T? RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions);

    private static SemanticCapabilityDefinition Definition(IReadOnlyList<string>? acceptedAliases = null) => new(
        "ObservationTiming",
        acceptedAliases ?? new[] { "ObservationTiming", "LocalPeakTime" },
        75,
        SemanticCapabilityStrictness.Strict,
        localizable: true,
        narratable: true,
        approvedSourceAdapterIds: new[] { "ObservationMetadataAdapter" },
        approvedDerivationRuleIds: Array.Empty<string>(),
        approvedDomainKnowledgeFactTypes: Array.Empty<string>(),
        eventSpecific: false);

    private static ResolvedSemanticFact Fact(SemanticVerificationStatus status = SemanticVerificationStatus.Verified, decimal confidence = 0.9m, IReadOnlyList<string>? sourceInputs = null) => new(
        "ObservationTiming",
        "observation.localPeakTime",
        "before dawn",
        null,
        "Local peak observing time",
        "ObservationMetadata",
        "localPeakTime",
        null,
        status,
        confidence,
        SemanticFactRequiredness.Required,
        "before dawn",
        "before dawn",
        "en-US",
        safeForNarration: true,
        sourceInputs: sourceInputs);
}
