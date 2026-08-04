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
}

public sealed class NarrationPlanningDependencyInjectionTests
{
    [Fact]
    public void Builder_requires_governed_constraint_policy()
    {
        var parameter = typeof(NarrationPlanningAuthorityBuilder).GetConstructors().Single().GetParameters().Single();
        parameter.ParameterType.Should().Be<INarrationPlanningConstraintPolicy>();
    }

    [Fact]
    public void Planning_contract_references_no_provider_types()
    {
        var names = typeof(NarrationPlanningAuthority).Assembly.GetTypes().Where(t => t.Namespace?.Contains("DocumentaryBlueprint") == true).Select(t => t.FullName!);
        names.Where(n => n.Contains("NarrationPlanningAuthority", StringComparison.Ordinal)).Should().NotContain(n => n.Contains("AzureOpenAI") || n.Contains("AzureSpeech"));
    }
}
