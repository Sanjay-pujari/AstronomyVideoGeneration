using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticCandidateSelectorCompletenessV1Tests
{
    private static readonly SemanticCapabilityId Capability = new("EventWindow");

    [Fact]
    public void NullValueReturnsZero() => Assert.Equal(0, SemanticCandidateSelectorV1.Completeness(null));

    [Fact]
    public void NonEmptyScalarStringIsComplete() => Assert.Equal(1, SemanticCandidateSelectorV1.Completeness("before dawn"));

    [Fact]
    public void EmptyStringIsIncomplete() => Assert.Equal(0, SemanticCandidateSelectorV1.Completeness("   "));

    [Fact]
    public void TypedRecordWithPopulatedFieldsHasHigherCompletenessThanSameRecordWithMissingFields()
    {
        Assert.True(SemanticCandidateSelectorV1.Completeness(new SemanticRecord("Mars", "planet", 1)) >
            SemanticCandidateSelectorV1.Completeness(new SemanticRecord("Mars", null, null)));
    }

    [Fact]
    public void IndexerBearingObjectDoesNotThrow()
    {
        var ex = Record.Exception(() => SemanticCandidateSelectorV1.Completeness(new IndexerBearingValue("semantic")));
        Assert.Null(ex);
    }

    [Fact]
    public void StringDoesNotExposeCharsIndexerToReflection() => Assert.Equal(1, SemanticCandidateSelectorV1.Completeness("abc"));

    [Fact]
    public void DictionaryDoesNotExposeItemIndexerToReflection() => Assert.Equal(1, SemanticCandidateSelectorV1.Completeness(new Dictionary<string, string> { ["a"] = "b" }));

    [Fact]
    public void CollectionCompletenessIsDeterministic()
    {
        var first = SemanticCandidateSelectorV1.Completeness(new[] { "b", "a" });
        var second = SemanticCandidateSelectorV1.Completeness(new[] { "a", "b" });
        Assert.Equal(first, second);
    }

    [Fact]
    public void DefaultImmutableArrayIsIncomplete()
    {
        var defaultArray = default(ImmutableArray<string>);
        Assert.Equal(0, SemanticCandidateSelectorV1.Completeness(defaultArray));
    }

    [Fact]
    public void RecordContainingDefaultImmutableArrayDoesNotThrowAndTreatsItAsIncomplete()
    {
        var ex = Record.Exception(() => SemanticCandidateSelectorV1.Completeness(new CollectionRecord(default)));

        Assert.Null(ex);
        Assert.Equal(0, SemanticCandidateSelectorV1.Completeness(new CollectionRecord(default)));
    }

    [Fact]
    public void IndependentlyAllocatedEquivalentValuesReceiveEqualCompleteness()
    {
        Assert.Equal(
            SemanticCandidateSelectorV1.Completeness(new SemanticRecord("Mars", "planet", 1)),
            SemanticCandidateSelectorV1.Completeness(new SemanticRecord("Mars", "planet", 1)));
    }

    [Fact]
    public void ReversedCandidateInputOrderProducesSameSelectedCandidate()
    {
        var a = Candidate("adapter-a", "source-a", new SemanticRecord("Mars", "planet", 1), SemanticEvidenceStrengthV1.Strong);
        var b = Candidate("adapter-b", "source-b", new SemanticRecord("Mars", null, null), SemanticEvidenceStrengthV1.Strong);

        Assert.Equal("adapter-a", Select([a, b]).SelectedCandidate!.AdapterId);
        Assert.Equal("adapter-a", Select([b, a]).SelectedCandidate!.AdapterId);
    }

    [Fact]
    public void CompletenessDoesNotOutrankStrongerEvidence()
    {
        var completeWeak = Candidate("adapter-a", "source-a", new SemanticRecord("Mars", "planet", 1), SemanticEvidenceStrengthV1.Moderate);
        var sparseStrong = Candidate("adapter-b", "source-b", new SemanticRecord("Mars", null, null), SemanticEvidenceStrengthV1.Strong);

        Assert.Equal("adapter-b", Select([completeWeak, sparseStrong]).SelectedCandidate!.AdapterId);
    }

    [Fact]
    public void CompletenessDoesNotOutrankHigherApprovedSourcePriority()
    {
        var highPrioritySparse = Candidate("adapter-a", "source-a", new SemanticRecord("Mars", null, null), SemanticEvidenceStrengthV1.Strong);
        var lowPriorityComplete = Candidate("adapter-b", "source-b", new SemanticRecord("Mars", "planet", 1), SemanticEvidenceStrengthV1.Strong);

        Assert.Equal("source-a", Select([lowPriorityComplete, highPrioritySparse]).SelectedCandidate!.SourceId);
    }

    private static SemanticCandidateSelectionV1 Select(SemanticSourceCandidateV1[] candidates)
    {
        var policy = new SemanticSourcePolicyV1(
            Capability,
            "test",
            [SemanticEvidenceCategoryV1.VerifiedEventData],
            [Approved("source-a", 0), Approved("source-b", 1)],
            SemanticEvidenceStrengthV1.Weak,
            false,
            false,
            false,
            false,
            false,
            SemanticSourceMultiplicityV1.HighestEvidenceStrength,
            SemanticSourceConflictPolicyV1.RecordAndUseHighestStrength,
            SemanticSourceMissingPolicyV1.Block,
            SemanticSourceMissingPolicyV1.OmitCapability,
            false,
            [],
            [],
            true,
            new("test", "test", "test"));
        var request = new SemanticResolutionRequestV1(Capability, true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, [SemanticEvidenceCategoryV1.VerifiedEventData], new SemanticSourceAdapterContextV1());
        var evaluations = candidates.Select(c => new SemanticCandidateEvaluationV1(c.AdapterId + ":" + c.SourceId, c.CapabilityId, c.AdapterId, c.SourceId, true, SemanticCandidateDispositionV1.Eligible, c.EvidenceStrength, policy.ApprovedSources.First(s => s.SourceId == c.SourceId).Priority, c.Confidence, "High", true, true, false, [], [], new([]), null));
        return new SemanticCandidateSelectorV1().Select(request, policy, candidates, evaluations, new(Capability, []));
    }

    private static ApprovedSemanticSourceV1 Approved(string sourceId, int priority) => new(sourceId, SemanticEvidenceCategoryV1.VerifiedEventData, SemanticEvidenceStrengthV1.Weak, priority, true, true, true, false, false, true, false, true, "test");

    private static SemanticSourceCandidateV1 Candidate(string adapterId, string sourceId, object value, SemanticEvidenceStrengthV1 strength) => new(
        Capability,
        adapterId,
        sourceId,
        new(value, value.GetType().Name),
        "canonical",
        "speakable",
        SemanticEvidenceCategoryV1.VerifiedEventData,
        strength,
        0.9m,
        [new(sourceId, sourceId + "Model", "Value", true)],
        [],
        [],
        [],
        []);

    private sealed record SemanticRecord(string? Name, string? Kind, int? Rank);

    private sealed record CollectionRecord(ImmutableArray<string> Values);

    private sealed class IndexerBearingValue
    {
        public IndexerBearingValue(string semanticName) => SemanticName = semanticName;
        public string SemanticName { get; }
        public string this[int index] => throw new InvalidOperationException("The indexer must not be read by completeness.");
    }
}
