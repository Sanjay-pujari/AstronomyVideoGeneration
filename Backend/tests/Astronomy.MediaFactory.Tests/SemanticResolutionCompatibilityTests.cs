using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using LegacyResolvedSemanticFact = Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts.ResolvedSemanticFact;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticResolutionCompatibilityTests
{
    [Fact]
    public void V1_To_Legacy_Compatibility_Mapping_Is_Deterministically_Equivalent()
    {
        var fact = ResolvedFact();

        var first = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en");
        var second = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en");

        AssertLegacyFactsEquivalent(first!, second!);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal("source-1", first!.SourceArtifact);
        Assert.Equal("Solar eclipse", first.CanonicalValue);
    }

    [Fact]
    public void SourceInputs_Are_Sequence_Equal_After_Independent_Mapping()
    {
        var fact = ResolvedFact();

        var first = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en")!;
        var second = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en")!;

        Assert.NotSame(first.SourceInputs, second.SourceInputs);
        Assert.True(first.SourceInputs!.Value.SequenceEqual(second.SourceInputs!.Value));
    }

    [Fact]
    public void SourceInputs_Are_Not_Caller_Owned_Mutable_Arrays()
    {
        var fact = ResolvedFact();

        var first = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en")!;
        var second = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "EventIdentity", "beat-1", "Required", "en")!;
        var callerOwnedCopy = first.SourceInputs!.Value.ToArray();
        callerOwnedCopy[0] = "mutated";

        Assert.Equal("path", second.SourceInputs!.Value[0]);
    }

    private static ResolvedSemanticFactV1 ResolvedFact() => new(new SemanticCapabilityId("EventIdentity"), SemanticResolutionStatusV1.Resolved, true, new SemanticSourceValueV1("Solar eclipse", "String"), "Solar eclipse", "Solar eclipse", "candidate-1", "adapter-1", "source-1", SemanticEvidenceCategoryV1.VerifiedEventData, SemanticEvidenceStrengthV1.Strong, .95m, [new("source-1", "model", "path", true)], [], [], [], "FirstApprovedByPriority", [], [], "Resolved", "Resolved");

    private static void AssertLegacyFactsEquivalent(LegacyResolvedSemanticFact first, LegacyResolvedSemanticFact second)
    {
        Assert.Equal(first.FactType, second.FactType);
        Assert.Equal(first.FactKey, second.FactKey);
        Assert.Equal(first.CanonicalValue, second.CanonicalValue);
        Assert.Equal(first.Unit, second.Unit);
        Assert.Equal(first.SemanticMeaning, second.SemanticMeaning);
        Assert.Equal(first.SourceArtifact, second.SourceArtifact);
        Assert.Equal(first.SourceField, second.SourceField);
        Assert.Equal(first.SourceBeatId, second.SourceBeatId);
        Assert.Equal(first.VerificationStatus, second.VerificationStatus);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Requiredness, second.Requiredness);
        Assert.Equal(first.LocalizedDisplayValue, second.LocalizedDisplayValue);
        Assert.Equal(first.SpeakableValue, second.SpeakableValue);
        Assert.Equal(first.Language, second.Language);
        Assert.Equal(first.SafeForNarration, second.SafeForNarration);
        Assert.Equal(first.FactOrigin, second.FactOrigin);
        Assert.Equal(first.DerivationRuleId, second.DerivationRuleId);
        Assert.True((first.SourceInputs?.ToArray() ?? []).SequenceEqual(second.SourceInputs?.ToArray() ?? []));
    }
}
