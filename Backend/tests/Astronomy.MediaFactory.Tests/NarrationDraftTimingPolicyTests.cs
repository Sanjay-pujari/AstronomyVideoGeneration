using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationDraftTimingPolicyTests
{
    [Fact]
    public void Draft_policy_resolves_single_scene_timing_decision_from_planning_constraints()
    {
        var policy = new DeterministicNarrationDraftTimingPolicy();
        var request = new NarrationDraftTimingPolicyRequest("en", "Long", "identity", 20, 30, 40, 3, 2, 4, 2, 2,
            "SemanticBeatBoundaries", 1);

        var decision = policy.Resolve(request);
        var budget = policy.Budget(request);

        decision.Should().Be(new NarrationDraftSceneTimingDecision("Long", "identity", 2, 4, 20, 40,
            DeterministicNarrationDraftTimingPolicy.PolicyId, DeterministicNarrationDraftTimingPolicy.PolicyVersion));
        budget.PermittedOptionalClaimCapacity.Should().Be(1);
        budget.WordsPerMinute.Should().Be(150m);
    }

    [Fact]
    public void Draft_contract_exposes_precise_timing_failure_codes_and_summaries()
    {
        NarrationDraftReasonCodes.SceneSentenceCountBelowMinimum.Should().Be("NARRATION_DRAFT_SCENE_SENTENCE_COUNT_BELOW_MINIMUM");
        NarrationDraftReasonCodes.SceneSentenceCountAboveMaximum.Should().Be("NARRATION_DRAFT_SCENE_SENTENCE_COUNT_ABOVE_MAXIMUM");
        NarrationDraftReasonCodes.SceneDurationBelowMinimum.Should().Be("NARRATION_DRAFT_SCENE_DURATION_BELOW_MINIMUM");
        NarrationDraftReasonCodes.SceneDurationAboveMaximum.Should().Be("NARRATION_DRAFT_SCENE_DURATION_ABOVE_MAXIMUM");
        NarrationDraftReasonCodes.TransitionSentenceCountInvalid.Should().Be("NARRATION_DRAFT_TRANSITION_SENTENCE_COUNT_INVALID");
        NarrationDraftReasonCodes.TimingPolicyUnresolved.Should().Be("NARRATION_DRAFT_TIMING_POLICY_UNRESOLVED");
        NarrationDraftReasonCodes.RequiredContentExceedsTimingCapacity.Should().Be("NARRATION_DRAFT_REQUIRED_CONTENT_EXCEEDS_TIMING_CAPACITY");
        NarrationDraftReasonCodes.InsufficientGovernedContentForMinimum.Should().Be("NARRATION_DRAFT_INSUFFICIENT_GOVERNED_CONTENT_FOR_MINIMUM");
        typeof(Phase7NarrationDraftAuthorityServiceResult).GetProperty(nameof(Phase7NarrationDraftAuthorityServiceResult.SceneFailureSummaries))
            .Should().NotBeNull();
    }
}
