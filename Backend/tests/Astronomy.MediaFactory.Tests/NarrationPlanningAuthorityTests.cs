using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationPlanningInputAuthorityTests
{
    [Fact]
    public void Request_requires_explicit_packet_validation()
    {
        var request = new Phase7NarrationPlanningInputAuthorityRequest("root", "execution", "plan", "event", "en",
            "profile", "v1", new([], [], "checksum"));
        request.PacketValidation.Should().BeNull();
    }
}

public sealed class NarrationPlanningBuilderTests
{
    [Fact]
    public void Contract_uses_typed_goal_and_strategy()
    {
        typeof(NarrationPlanningScene).GetProperty(nameof(NarrationPlanningScene.NarrativeGoal))!.PropertyType.Should().Be<NarrationPlanningGoal>();
        typeof(NarrationPlanningScene).GetProperty(nameof(NarrationPlanningScene.Strategy))!.PropertyType.Should().Be<NarrationPlanningStrategy>();
        NarrationPlanningContract.Version.Should().Be("rc2-phase7-narration-planning.v1");
    }

    [Fact]
    public void Builder_exposes_governed_semantic_result()
    {
        typeof(INarrationPlanningAuthorityBuilder).GetMethod(nameof(INarrationPlanningAuthorityBuilder.Build))!
            .ReturnType.Should().Be<NarrationPlanningAuthorityBuildResult>();
    }
}

public sealed class NarrationPlanningTransitionTests
{
    public static IEnumerable<object[]> IdentityMutations()
    {
        yield return [new Func<NarrationPlanningTransition, NarrationPlanningTransition>(x => x with { ExecutionId = "execution-2" })];
        yield return [new Func<NarrationPlanningTransition, NarrationPlanningTransition>(x => x with { FromStoryFrameId = "frame-0" })];
        yield return [new Func<NarrationPlanningTransition, NarrationPlanningTransition>(x => x with { ToStoryFrameId = "frame-3" })];
        yield return [new Func<NarrationPlanningTransition, NarrationPlanningTransition>(x => x with { Kind = "VariantClosing" })];
        yield return [new Func<NarrationPlanningTransition, NarrationPlanningTransition>(x => x with { NextPacketId = "packet-3" })];
    }

    [Theory]
    [MemberData(nameof(IdentityMutations))]
    public void Every_governed_transition_identity_field_changes_id(
        Func<NarrationPlanningTransition, NarrationPlanningTransition> mutate)
    {
        var transition = Transition();
        NarrationPlanningCanonicalizer.ComputeTransitionId(mutate(transition))
            .Should().NotBe(NarrationPlanningCanonicalizer.ComputeTransitionId(transition));
    }

    [Fact]
    public void Same_transition_in_different_execution_has_different_id()
    {
        var transition = Transition();
        NarrationPlanningCanonicalizer.ComputeTransitionId(transition with { ExecutionId = "other" })
            .Should().NotBe(NarrationPlanningCanonicalizer.ComputeTransitionId(transition));
    }

    private static NarrationPlanningTransition Transition() => new("", "execution-1", "Long", "frame-1", "from-sum",
        "frame-2", "to-sum", "StoryFrameSuccessor", "out", "in", "packet-0", "packet-1", "packet-2", "");
}

public sealed class NarrationPlanningConstraintPolicyTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void Language_policy_is_deterministic_and_coherent(string language)
    {
        var policy = new DefaultNarrationPlanningConstraintPolicy();
        var profile = new FamilyNarrationProfile("profile", "v1", "family", ["en", "hi"],
            new(1, 2, 3, new(10, 20, 30), [], [], [], "open", "close"),
            new(1, new(5, 10, 15), [], "hook", "discovery", "action", "close"),
            [], [], [], [], [], [], [], new Dictionary<string,DurationRange>(), "certified-only", "checksum");
        var request = new NarrationPlanningConstraintRequest(language, "Long", 20, 15, 25, profile, "Explain", "identity", 2, 1);
        var first = policy.Resolve(request);
        policy.Resolve(request).Should().BeEquivalentTo(first);
        first.MinimumSentenceCount.Should().BeLessThanOrEqualTo(first.PreferredSentenceCount);
        first.PreferredSentenceCount.Should().BeLessThanOrEqualTo(first.MaximumSentenceCount);
        first.ReadingTimeTargetSeconds.Should().BeInRange(15, 25);
    }

    [Fact]
    public void Transition_aware_policy_expands_duration_maximum_to_mandatory_floor()
    {
        var policy = new DefaultNarrationPlanningConstraintPolicy();
        var profile = Profile();
        var request = new NarrationPlanningConstraintRequest("en", "Long", 50, 45, 60, profile, "Explain", "Wonder",
            6, 0, 0, 1, 0);

        var result = policy.Resolve(request);

        result.MinimumSentenceCount.Should().BeLessThanOrEqualTo(result.PreferredSentenceCount);
        result.PreferredSentenceCount.Should().BeLessThanOrEqualTo(result.MaximumSentenceCount);
        result.PreferredSentenceCount.Should().BeGreaterThanOrEqualTo(7);
        result.MaximumSentenceCount.Should().BeGreaterThanOrEqualTo(7);
    }

    [Fact]
    public void Opening_and_closing_transition_counts_are_not_mandatory_for_constraints()
    {
        var incoming = Transition(NarrationPlanningPolicyCatalog.VariantOpeningTransition, null, "opening");
        var outgoing = Transition(NarrationPlanningPolicyCatalog.VariantClosingTransition, "closing", null);

        NarrationTransitionSentenceOwnership.MandatorySentenceCount(incoming, true).Should().Be(0);
        NarrationTransitionSentenceOwnership.MandatorySentenceCount(outgoing, false).Should().Be(0);
    }

    private static FamilyNarrationProfile Profile() => new("profile", "v1", "family", ["en", "hi"],
        new(1, 2, 3, new(10, 20, 30), [], [], [], "open", "close"),
        new(1, new(5, 10, 15), [], "hook", "discovery", "action", "close"),
        [], [], [], [], [], [], [], new Dictionary<string,DurationRange>(), "certified-only", "checksum");

    private static NarrationPlanningTransition Transition(string kind, string? source, string? destination) =>
        new("transition", "execution", "Long", "from", "from-sum", "to", "to-sum", kind, source, destination, "previous", "current", "next", "sum");
}

public sealed class NarrationPlanningDependencyInjectionTests
{
    [Fact]
    public void Builder_requires_governed_constraint_policy()
    {
        var parameters = typeof(NarrationPlanningAuthorityBuilder).GetConstructors().Single().GetParameters();
        parameters.Select(x => x.ParameterType).Should().Contain(typeof(INarrationPlanningConstraintPolicy));
        parameters.Select(x => x.ParameterType).Should().Contain(typeof(INarrationPlanningDraftRealizabilityPolicy));
    }

    [Fact]
    public void Planning_contract_references_no_provider_types()
    {
        var names = typeof(NarrationPlanningAuthority).Assembly.GetTypes().Where(t => t.Namespace?.Contains("DocumentaryBlueprint") == true).Select(t => t.FullName!);
        names.Where(n => n.Contains("NarrationPlanningAuthority", StringComparison.Ordinal)).Should().NotContain(n => n.Contains("AzureOpenAI") || n.Contains("AzureSpeech"));
    }
}


public sealed class NarrationPlanningDraftRealizabilityPolicyTests
{
    private static NarrationPlanningConstraints Constraints(int max) => new(1, Math.Min(2, max), max, 20, "Pause", [], "Claims", "Visuals");
    private static NarrationPlanningTransition Transition(string kind, string? source, string? destination) =>
        new("transition", "execution", "Long", "from", "from-sum", "to", "to-sum", kind, source, destination, "previous", "current", "next", "sum");

    [Fact]
    public void Impossible_scene_fails_draft_realizability_gate_budget()
    {
        var policy = new DeterministicNarrationPlanningDraftRealizabilityPolicy();
        var result = policy.Evaluate(new("Long", "identity", ["c1", "c2", "c3", "c4", "c5", "c6"],
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "out", "in"),
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "out", "in"),
            Constraints(6), [], [], [], []));

        result.IsRealizable.Should().BeFalse();
        result.ReasonCode.Should().Be("NARRATION_PLANNING_DRAFT_CAPACITY_INVALID");
        result.MinimumMandatorySentenceCount.Should().Be(8);
    }

    [Fact]
    public void Exact_capacity_passes()
    {
        var policy = new DeterministicNarrationPlanningDraftRealizabilityPolicy();
        var result = policy.Evaluate(new("Long", "identity", ["c1", "c2", "c3", "c4"],
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, "in"),
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, "out", null),
            Constraints(6), [], [], [], []));

        result.IsRealizable.Should().BeTrue();
        result.MinimumMandatorySentenceCount.Should().Be(6);
    }

    [Fact]
    public void Opening_and_closing_transitions_do_not_consume_mandatory_capacity()
    {
        var policy = new DeterministicNarrationPlanningDraftRealizabilityPolicy();
        var result = policy.Evaluate(new("Long", "identity", ["c1", "c2", "c3", "c4", "c5", "c6"],
            Transition(NarrationPlanningPolicyCatalog.VariantOpeningTransition, null, "opening"),
            Transition(NarrationPlanningPolicyCatalog.VariantClosingTransition, "closing", null),
            Constraints(6), [], [], [], []));

        result.IsRealizable.Should().BeTrue();
        result.MandatoryIncomingTransitionSentenceCount.Should().Be(0);
        result.MandatoryOutgoingTransitionSentenceCount.Should().Be(0);
    }

    [Theory]
    [InlineData("in", null, 1)]
    [InlineData(null, "out", 1)]
    [InlineData(null, null, 0)]
    public void Transition_ownership_counts_only_spoken_owned_phrases(string? incomingText, string? outgoingText, int expected)
    {
        var policy = new DeterministicNarrationPlanningDraftRealizabilityPolicy();
        var result = policy.Evaluate(new("Long", "identity", [],
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, null, incomingText),
            Transition(NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, outgoingText, null),
            Constraints(2), [], [], [], []));

        (result.MandatoryIncomingTransitionSentenceCount + result.MandatoryOutgoingTransitionSentenceCount).Should().Be(expected);
    }
}
