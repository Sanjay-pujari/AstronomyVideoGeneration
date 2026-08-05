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
        typeof(Phase7AuthorityStageResult).GetProperty(nameof(Phase7AuthorityStageResult.DraftSceneFailureSummaries))
            .Should().NotBeNull();
    }

    [Fact]
    public void Draft_failure_summary_contract_exposes_scene_specific_timing_evidence()
    {
        var summary = new NarrationDraftSceneFailureSummary(
            "Long", 2, "scene-2", "frame-2", "planning-2", "where",
            4, 5, 7, 12.5m, 15m, 25m, ["required-1"], ["required-1"],
            ["optional-1"], ["sentence-in", "sentence-out"], ["SceneSentenceRangeGate"],
            [NarrationDraftReasonCodes.SceneSentenceCountBelowMinimum]);

        summary.SceneNumber.Should().Be(2);
        summary.SentenceCount.Should().Be(4);
        summary.TransitionSentenceIds.Should().Equal("sentence-in", "sentence-out");
        summary.FailedGateNames.Should().ContainSingle().Which.Should().Be("SceneSentenceRangeGate");
        summary.ReasonCodes.Should().ContainSingle().Which.Should().Be(NarrationDraftReasonCodes.SceneSentenceCountBelowMinimum);
    }

    [Fact]
    public void Orchestration_stage_keeps_packet_and_draft_failure_summaries_separate()
    {
        var draftSummary = new NarrationDraftSceneFailureSummary(
            "Short", 1, "scene-1", "frame-1", "planning-1", "hook",
            9, 3, 8, 31m, 10m, 30m, [], [], [], ["transition-1"],
            ["SceneSentenceRangeGate"], [NarrationDraftReasonCodes.SceneSentenceCountAboveMaximum]);

        var result = new Phase7AuthorityStageResult(
            "NarrationDraftAuthority", "P7.1C-A Narration Draft Authority", false, "Failed",
            NarrationDraftReasonCodes.SceneSentenceCountAboveMaximum, false, false, false, [], [], [], [])
        {
            DraftSceneFailureSummaries = [draftSummary]
        };

        result.DraftSceneFailureSummaries.Should().ContainSingle().Which.SceneNumber.Should().Be(1);
        result.PacketFailureSummaries.Should().BeEmpty();
    }
}
