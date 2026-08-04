using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

internal static class NarrationPlanningTestData
{
    internal static (Phase7NarrationPlanningInputAuthority Input, NarrationPlanningAuthority Plan) Create()
    {
        var longs = new[] { Packet("long-1", "Long", 1), Packet("long-2", "Long", 2) };
        var shorts = new[] { Packet("short-1", "Short", 1) };
        var collectionDraft = new SceneKnowledgePacketCollection(longs, shorts, "");
        var collection = collectionDraft with { DeterministicChecksum = Phase7Determinism.Hash(new { Long = longs, Short = shorts }) };
        var profile = new FamilyNarrationProfile("profile", "v1", "family", ["en"],
            new(1, 2, 3, new(10, 20, 30), [], [], [], "open", "close"),
            new(1, new(5, 10, 15), [], "hook", "discovery", "action", "close"),
            [], [], ["safe"], [], [], [], [], new Dictionary<string,DurationRange>(), "certified-only", "profile-sum");
#pragma warning disable CS8625
        var input = new Phase7NarrationPlanningInputAuthority(null, null, collection, profile, "exec", "plan",
            "event", "en", "profile", "v1", new Dictionary<string,string> { ["phase6AuthorityId"] = "p6" },
            new Dictionary<string,string> { ["runtime"] = "compatible" });
#pragma warning restore CS8625
        return (input, new NarrationPlanningAuthorityBuilder().Build(input));
    }

    private static SceneKnowledgePacket Packet(string id, string variant, int number)
    {
        var claim = new CertifiedNarrationClaim($"claim-{id}", "Identity", "certified fact", ["source"], ["knowledge"],
            1m, false, false, false, false, false, false, false, false, "en", "claim-sum");
        var draft = new SceneKnowledgePacket(id, "exec", "plan", "event", "family", "en", "profile", "v1", variant,
            $"frame-{id}", $"frame-sum-{id}", $"scene-{id}", $"scene-sum-{id}", number, 1, "Development", "Explain",
            "identity", $"question-{id}", $"What is {id}?", $"objective-{id}", $"Understand {id}", [claim], [], [], [],
            ["safe"], ["certified-only"], ["unsupported statement"], new Dictionary<string,string>(), [],
            new Dictionary<string,string>(), [$"visual-{id}"], [$"knowledge-{id}"], ["source"], 20, 15, 25,
            false, false, [], false, [], [], new Dictionary<string,string>(), "");
        return draft with { ResolvedViewerQuestionText = draft.ViewerQuestionText!, DeterministicChecksum = Phase7Determinism.Hash(draft with { DeterministicChecksum = "" }) };
    }
}

public sealed class NarrationPlanningInputAuthorityTests
{
    [Fact] public void Input_carries_committed_contract_identity() =>
        NarrationPlanningTestData.Create().Input.SceneKnowledgePacketCollection.Long.Should().HaveCount(2);
}
public sealed class NarrationPlanningBuilderTests
{
    [Fact] public void Produces_one_scene_per_packet_and_preserves_governed_content()
    {
        var (input, plan) = NarrationPlanningTestData.Create();
        plan.LongScenes.Concat(plan.ShortScenes).Should().HaveCount(3);
        plan.LongScenes[0].PacketChecksum.Should().Be(input.SceneKnowledgePacketCollection.Long[0].DeterministicChecksum);
        plan.LongScenes[0].ViewerQuestion.Should().Be(input.SceneKnowledgePacketCollection.Long[0].ResolvedViewerQuestionText);
        plan.LongScenes[0].LearningObjective.Should().Be(input.SceneKnowledgePacketCollection.Long[0].SceneObjective);
        plan.LongScenes[0].RequiredClaims.Should().Equal("claim-long-1");
    }
}
public sealed class NarrationPlanningValidatorTests
{
    [Fact] public void All_required_gates_pass() { var x = NarrationPlanningTestData.Create(); new NarrationPlanningValidator().Validate(x.Input, x.Plan).IsValid.Should().BeTrue(); }
}
public sealed class NarrationPlanningDeterminismTests
{
    [Fact] public void Rebuild_is_byte_semantically_deterministic() { var x = NarrationPlanningTestData.Create(); new NarrationPlanningAuthorityBuilder().Build(x.Input).Should().Be(x.Plan); }
}
public sealed class NarrationPlanningTransitionTests
{
    [Fact] public void Transition_graph_is_complete() { var x = NarrationPlanningTestData.Create(); x.Plan.LongScenes[0].OutgoingTransition.TransitionId.Should().Be(x.Plan.LongScenes[1].IncomingTransition.TransitionId); }
}
public sealed class NarrationPlanningLongShortTests
{
    [Fact] public void Long_and_short_are_independently_planned_and_provider_free()
    {
        var plan = NarrationPlanningTestData.Create().Plan;
        plan.LongScenes.Should().HaveCount(2); plan.ShortScenes.Should().ContainSingle();
        typeof(NarrationPlanningAuthorityBuilder).GetConstructors().Single().GetParameters().Should().BeEmpty("planning makes zero provider calls");
    }
}
