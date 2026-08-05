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

public sealed class NarrationDraftTransitionCapacityPolicyTests
{
    [Fact]
    public void First_long_scene_uses_governed_successor_only_capacity()
    {
        var incoming = Transition(NarrationPlanningPolicyCatalog.VariantOpeningTransition, null, "opening text");
        var outgoing = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "successor out", null);

        var result = MinimumMandatory(6, incoming, outgoing, 7);

        result.Incoming.Should().Be(0);
        result.Outgoing.Should().Be(1);
        result.Minimum.Should().Be(7);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Internal_scene_uses_both_owned_successor_sides()
    {
        var incoming = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in");
        var outgoing = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "successor out", null);

        var result = MinimumMandatory(4, incoming, outgoing, 6);

        result.Incoming.Should().Be(1);
        result.Outgoing.Should().Be(1);
        result.Minimum.Should().Be(6);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Final_scene_does_not_charge_variant_closing_capacity()
    {
        var incoming = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in");
        var outgoing = Transition(NarrationPlanningPolicyCatalog.VariantClosingTransition, "closing text", null);

        var result = MinimumMandatory(5, incoming, outgoing, 6);

        result.Incoming.Should().Be(1);
        result.Outgoing.Should().Be(0);
        result.Minimum.Should().Be(6);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Invalid_scene_with_two_owned_successors_exceeds_capacity()
    {
        var incoming = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in");
        var outgoing = Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "successor out", null);

        var result = MinimumMandatory(6, incoming, outgoing, 7);

        result.Minimum.Should().Be(8);
        result.Valid.Should().BeFalse();
        NarrationDraftReasonCodes.RequiredContentExceedsTimingCapacity.Should().Be("NARRATION_DRAFT_REQUIRED_CONTENT_EXCEEDS_TIMING_CAPACITY");
    }

    private static (int Incoming, int Outgoing, int Qualifications, int Minimum, bool Valid) MinimumMandatory(
        int requiredClaimCount, NarrationPlanningTransition incoming, NarrationPlanningTransition outgoing, int maximum)
    {
        var mandatoryIncoming = NarrationTransitionSentenceOwnership.MandatorySentenceCount(incoming, true);
        var mandatoryOutgoing = NarrationTransitionSentenceOwnership.MandatorySentenceCount(outgoing, false);
        var mandatoryQualifications = NarrationTransitionSentenceOwnership.MandatoryQualificationSentenceCount([], [], [], []);
        var minimum = requiredClaimCount + mandatoryIncoming + mandatoryOutgoing + mandatoryQualifications;
        return (mandatoryIncoming, mandatoryOutgoing, mandatoryQualifications, minimum, minimum <= maximum);
    }

    private static NarrationPlanningTransition Transition(string kind, string? source, string? destination) =>
        new("transition", "execution", "Long", "from", "from-sum", "to", "to-sum", kind, source, destination, "previous", "current", "next", "sum");
}

public sealed class NarrationDraftTransitionRealizationTests
{
    [Theory]
    [InlineData(NarrationPlanningPolicyCatalog.VariantOpeningTransition, NarrationDraftTransitionOwnership.IncomingDestination, null)]
    [InlineData(NarrationPlanningPolicyCatalog.VariantClosingTransition, NarrationDraftTransitionOwnership.OutgoingSource, null)]
    public void Non_successor_transitions_do_not_create_draft_transition_phrases(string kind, NarrationDraftTransitionOwnership ownership, string? expected)
    {
        var policy = new DeterministicNarrationDraftTransitionPhrasePolicy(new DeterministicNarrationDraftLanguagePolicy());
        var phrase = policy.Create(new(Transition(kind, "source text", "destination text"), ownership, "Long", "en"));
        phrase?.Text.Should().Be(expected);
        phrase.Should().BeNull();
    }

    [Fact]
    public void Successor_transition_requires_owned_authored_text()
    {
        var policy = new DeterministicNarrationDraftTransitionPhrasePolicy(new DeterministicNarrationDraftLanguagePolicy());
        policy.Create(new(Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "", ""), NarrationDraftTransitionOwnership.IncomingDestination, "Long", "en")).Should().BeNull();
        policy.Create(new(Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "source text", null), NarrationDraftTransitionOwnership.OutgoingSource, "Long", "en")).Should().NotBeNull();
    }

    [Fact]
    public void First_scene_builder_realizes_only_outgoing_successor_transition()
    {
        var result = Build(6, Transition(NarrationPlanningPolicyCatalog.VariantOpeningTransition, null, "variant opening text"),
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "successor out", null), 7, false);

        result.IsValid.Should().BeTrue();
        var scene = result.Authority!.LongScenes.Single();
        scene.IncomingTransitionPhrase.Should().BeNull();
        scene.OutgoingTransitionPhrase.Should().NotBeNull();
        scene.RequiredClaimUsage.Should().HaveCount(6);
        scene.SentenceCount.Should().Be(7);
        scene.Sentences.Count(x => x.IsTransition).Should().Be(1);
        scene.Sentences.Should().NotContain(x => x.SentenceRole == "IncomingTransition");
    }

    [Fact]
    public void Internal_scene_builder_realizes_both_successor_transitions()
    {
        var result = Build(4, Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in"),
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "successor out", null), 6, false);

        result.IsValid.Should().BeTrue();
        var scene = result.Authority!.LongScenes.Single();
        scene.IncomingTransitionPhrase.Should().NotBeNull();
        scene.OutgoingTransitionPhrase.Should().NotBeNull();
        scene.RequiredClaimUsage.Should().HaveCount(4);
        scene.SentenceCount.Should().Be(6);
        scene.Sentences.Count(x => x.IsTransition).Should().Be(2);
    }

    [Fact]
    public void Final_scene_builder_keeps_variant_closing_out_of_transition_roles()
    {
        var result = Build(5, Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in"),
            Transition(NarrationPlanningPolicyCatalog.VariantClosingTransition, "variant closing text", null), 8, true);

        result.IsValid.Should().BeTrue();
        var scene = result.Authority!.LongScenes.Single();
        scene.IncomingTransitionPhrase.Should().NotBeNull();
        scene.OutgoingTransitionPhrase.Should().BeNull();
        scene.Closing.Should().NotBeNullOrWhiteSpace();
        scene.Sentences.Should().NotContain(x => x.SentenceRole == "OutgoingTransition");
        scene.Sentences.Count(x => x.IsTransition).Should().Be(1);
    }

    [Fact]
    public void Builder_and_validator_enforce_successor_transition_sentence_invariant()
    {
        var result = Build(4, Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "successor in"),
            Transition(NarrationPlanningPolicyCatalog.VariantClosingTransition, "variant closing text", null), 8, true);
        var input = LastInput!;
        var authority = result.Authority!;
        var validation = new NarrationDraftValidator(new NarrationDraftSafetyValidator()).Validate(input, authority);

        validation.IsValid.Should().BeTrue();
        foreach (var pair in input.NarrationPlanningAuthority.LongScenes.Zip(authority.LongScenes))
        {
            var expectedTransitionSentenceCount = NarrationTransitionSentenceOwnership.MandatorySentenceCount(pair.First.IncomingTransition, true) +
                NarrationTransitionSentenceOwnership.MandatorySentenceCount(pair.First.OutgoingTransition, false);
            pair.Second.Sentences.Count(x => x.IsTransition).Should().Be(expectedTransitionSentenceCount);
            pair.Second.Sentences.Where(x => x.IsTransition).Should().OnlyContain(x => x.SentenceRole == "IncomingTransition" || x.SentenceRole == "OutgoingTransition");
            if (pair.First.IncomingTransition.Kind == NarrationPlanningPolicyCatalog.VariantOpeningTransition)
                pair.Second.Sentences.Should().NotContain(x => x.IsTransition && x.SentenceRole == "IncomingTransition");
            if (pair.First.OutgoingTransition.Kind == NarrationPlanningPolicyCatalog.VariantClosingTransition)
                pair.Second.Sentences.Should().NotContain(x => x.IsTransition && x.SentenceRole == "OutgoingTransition");
        }
    }

    private static Phase7NarrationDraftInputAuthority? LastInput { get; set; }

    private static NarrationDraftAuthorityBuildResult Build(int required, NarrationPlanningTransition incoming, NarrationPlanningTransition outgoing, int maximum, bool includeClosing)
    {
        var claims = Enumerable.Range(1, required).Select(i => new CertifiedNarrationClaim($"claim-{i}", "Identity", $"Certified claim {i} has exact text", ["source"], [$"ref-{i}"], .99m, false, false, false, false, false, false, false, false, "en", "sum")).ToArray();
        var scene = Scene(required, incoming, outgoing, maximum, includeClosing);
        var diagnostics = new NarrationPlanningDiagnostics(1, 1, 1, 0, 0, 0, required, required, 0, 0, 0, 0, 0, 0, 0, 0, 0, required, 0, 0, 0, 0, [], [], [], "");
        diagnostics = diagnostics with { DeterministicChecksum = NarrationPlanningCanonicalizer.DiagnosticsChecksum(diagnostics) };
        var authority = new NarrationPlanningAuthority(NarrationPlanningContract.Version, "", "execution", "plan", "event", "en", "profile", "v1", "story-sum", "knowledge-sum", "packet-sum", [scene], [], diagnostics, new Dictionary<string, string>(), new Dictionary<string, string>(), "");
        authority = authority with { AuthorityId = NarrationPlanningCanonicalizer.AuthorityId(authority) };
        authority = authority with { DeterministicChecksum = NarrationPlanningCanonicalizer.AuthorityChecksum(authority) };
        LastInput = new Phase7NarrationDraftInputAuthority(null!, authority, diagnostics, null!, null!, null!, null!, Profile(), "execution", "plan", "event", "en", "profile", "v1", new Dictionary<string, string>(), new Dictionary<string, string>()) { CertifiedClaims = claims };
        return new NarrationDraftAuthorityBuilder(new DeterministicNarrationDraftLanguagePolicy(), new DeterministicNarrationDraftTimingPolicy(), new DeterministicNarrationDraftRealizationPolicy(new DeterministicNarrationDraftLanguagePolicy()), new DeterministicNarrationDraftOpeningPolicy(new DeterministicNarrationDraftLanguagePolicy()), new DeterministicNarrationDraftClosingPolicy(new DeterministicNarrationDraftLanguagePolicy()), new DeterministicNarrationDraftTransitionPhrasePolicy(new DeterministicNarrationDraftLanguagePolicy()), new NarrationDraftSafetyValidator()).Build(LastInput);
    }

    private static NarrationPlanningScene Scene(int required, NarrationPlanningTransition incoming, NarrationPlanningTransition outgoing, int maximum, bool includeClosing)
    {
        var goal = new NarrationPlanningGoal("First", "Wonder", "question", "objective", Enumerable.Range(1, required).Select(i => $"claim-{i}").ToArray(), "profile", NarrationPlanningPolicyCatalog.GoalPolicy, "");
        goal = goal with { DeterministicChecksum = NarrationPlanningCanonicalizer.GoalChecksum(goal) };
        var strategy = new NarrationPlanningStrategy("Opening", "First", "Wonder", NarrationPlanningPolicyCatalog.OpeningMode, NarrationPlanningPolicyCatalog.DevelopmentMode, NarrationPlanningPolicyCatalog.ClosingMode, NarrationPlanningPolicyCatalog.ClaimIntroductionPolicy, NarrationPlanningPolicyCatalog.OptionalClaimUsagePolicy, NarrationPlanningPolicyCatalog.DeferredClaimPolicy, NarrationPlanningPolicyCatalog.CallbackPolicy, "");
        strategy = strategy with { DeterministicChecksum = NarrationPlanningCanonicalizer.StrategyChecksum(strategy) };
        var scene = new NarrationPlanningScene("", "scene", "Long", "frame", "frame-sum", "source-sum", "packet", "packet-sum", "What should we notice?", includeClosing ? "Closing learning objective" : "", goal, [], [], goal.RequiredClaimIds, [], [], strategy, new(NarrationPlanningPolicyCatalog.RequiredClaimUsage, NarrationPlanningPolicyCatalog.OptionalClaimUsage, NarrationPlanningPolicyCatalog.DeferredClaimUsage), new(1, required, maximum, 1, "SemanticBeatBoundaries", [], "RequiredInPacketOrder", "Visual"), [], [], [], [], [], [], [], [], 1, 1, 100, required, 1, [], incoming, outgoing, "");
        scene = scene with { PlanningId = NarrationPlanningCanonicalizer.PlanningId(scene) };
        return scene with { DeterministicChecksum = NarrationPlanningCanonicalizer.SceneChecksum(scene) };
    }

    private static NarrationPlanningTransition Transition(string kind, string? source, string? destination)
    {
        var transition = new NarrationPlanningTransition("", "execution", "Long", "from", "from-sum", "to", "to-sum", kind, source, destination, "previous", "current", "next", "");
        transition = transition with { TransitionId = NarrationPlanningCanonicalizer.TransitionId(transition) };
        return transition with { DeterministicChecksum = NarrationPlanningCanonicalizer.TransitionChecksum(transition) };
    }

    private static FamilyNarrationProfile Profile() => new("profile", "v1", "event", ["en"], new(1, 1, 1, new(1, 50, 100), [], [], [], "", ""), new(1, new(1, 50, 100), [], "", "", "", ""), [], [], [], [], [], [], [], new Dictionary<string, DurationRange>(), "", "sum");
}
