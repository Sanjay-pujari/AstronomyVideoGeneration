using System.Reflection;
using System.Text.Json;
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
            ["dawn"],
            [],
            "Strong",
            [new SemanticCapabilityCandidate("ObservationMetadata", "localPeakTime", "before dawn", "Strong")],
            [],
            []);

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
        Assert.Throws<ArgumentException>(() => new SemanticCapabilityDefinition(" ", ["Alias"], 1, SemanticCapabilityStrictness.Strict, true, true, [], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticCapabilityDefinition("Capability", ["Alias"], -1, SemanticCapabilityStrictness.Strict, true, true, [], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fact(confidence: 1.1m));
        Assert.Throws<ArgumentNullException>(() => new RequiredSemanticFactResolutionResult([], null!));
    }

    [Fact]
    public void ListInputs_AreSnapshotForImmutability()
    {
        var aliases = new List<string> { "ObservationTiming" };
        var definition = new SemanticCapabilityDefinition("ObservationTiming", aliases, 75, SemanticCapabilityStrictness.Strict, true, true, [], [], []);

        aliases.Add("MutatedAlias");

        Assert.Single(definition.AcceptedAliases);
        Assert.DoesNotContain("MutatedAlias", definition.AcceptedAliases);
    }

    private static SemanticCapabilityDefinition Definition() => new(
        "ObservationTiming",
        ["ObservationTiming", "LocalPeakTime"],
        75,
        SemanticCapabilityStrictness.Strict,
        localizable: true,
        narratable: true,
        approvedSourceAdapterIds: ["ObservationMetadataAdapter"],
        approvedDerivationRuleIds: [],
        approvedDomainKnowledgeFactTypes: [],
        eventSpecific: false);

    private static ResolvedSemanticFact Fact(SemanticVerificationStatus status = SemanticVerificationStatus.Verified, decimal confidence = 0.9m) => new(
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
        safeForNarration: true);
}
