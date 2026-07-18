using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;
using Astronomy.MediaFactory.Core.ExecutionValidation;

namespace Astronomy.MediaFactory.Tests;

public sealed class MeteorShowerExecutionContextBuilderTests
{
    [Fact]
    public void Build_preserves_identity_stable_keys_evidence_metadata_and_exact_strategy_values()
    {
        var observation = Observation();
        var context = new MeteorShowerExecutionContextBuilder().Build(observation, MeteorShowerExecutionContractFactory.Create());

        Assert.Equal("exec-2cb", context.ExecutionId);
        Assert.Equal("Astronomy", context.DomainId);
        Assert.Equal(MeteorShowerExecutionKeys.FamilyId, context.FamilyId);
        Assert.Equal(MeteorShowerExecutionKeys.ContractVersion, context.ContractVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-07-18T00:00:00Z"), context.CreatedUtc);

        Assert.Equal(new[] { MeteorShowerExecutionKeys.Inputs.ContentStrategy, MeteorShowerExecutionKeys.Inputs.EventEnd, MeteorShowerExecutionKeys.Inputs.EventIdentity, MeteorShowerExecutionKeys.Inputs.EventStart, MeteorShowerExecutionKeys.Inputs.Format, MeteorShowerExecutionKeys.Inputs.Language, MeteorShowerExecutionKeys.Inputs.ObserverLocation }, Keys(context.InputValues));
        Assert.Equal(" LocalViewingGuide ", context.InputValues[MeteorShowerExecutionKeys.Inputs.ContentStrategy].Value);
        Assert.Equal("Best after 1 a.m.; do not normalize.", context.Metadata["localViewingGuide.value"]);

        Assert.Equal(new[] { MeteorShowerExecutionKeys.Semantic.MeteorActivity, MeteorShowerExecutionKeys.Semantic.PeakWindow, MeteorShowerExecutionKeys.Semantic.Radiant }, Keys(context.SemanticValues));
        Assert.Equal("semantic-source", context.SemanticValues[MeteorShowerExecutionKeys.Semantic.MeteorActivity].SourceId);
        Assert.Equal("semantic evidence", Assert.Single(context.SemanticValues[MeteorShowerExecutionKeys.Semantic.MeteorActivity].Evidence));

        Assert.Equal(new[] { MeteorShowerExecutionKeys.Projection.PeakWindowFact, MeteorShowerExecutionKeys.Projection.RadiantFact }, Keys(context.ProjectionValues));
        Assert.Equal("Gemini", context.ProjectionValues[MeteorShowerExecutionKeys.Projection.RadiantFact].Value);

        Assert.Equal(new[] { MeteorShowerExecutionKeys.Rules.ActivityObserved, MeteorShowerExecutionKeys.Rules.FamilyStrategyConsistency, MeteorShowerExecutionKeys.Rules.RequiredFactsRetained, MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete }, RuleKeys(context.ValidationRuleValues));
        Assert.False(context.ValidationRuleValues[MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete].Passed);
        Assert.Equal("rule evidence", Assert.Single(context.ValidationRuleValues[MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete].Evidence));

        Assert.Equal("trace-1", context.Metadata["traceId"]);
        Assert.Contains("Immutable", context.InputValues.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_maps_only_observed_values_and_does_not_manufacture_semantic_or_projection_values()
    {
        var observation = new MeteorShowerProductionObservation(
            "exec-missing",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            ContentStrategy: "MeteorShower",
            EventIdentity: new MeteorShowerObservedValue("Perseids request value", "request.eventIdentity"),
            EventStart: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-08-12T00:00:00Z")),
            EventEnd: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-08-13T00:00:00Z")),
            Language: new MeteorShowerObservedValue("en"),
            Format: new MeteorShowerObservedValue("long"),
            LocalViewingGuide: new MeteorShowerObservedValue("  Keep spacing  "),
            ObservedMeteorActivity: null,
            ObservedProjectedFacts: ImmutableDictionary<string, MeteorShowerObservedValue>.Empty.Add(MeteorShowerExecutionKeys.Projection.RadiantFact, new MeteorShowerObservedValue(null)));

        var context = new MeteorShowerExecutionContextBuilder().Build(observation, MeteorShowerExecutionContractFactory.Create());

        Assert.Contains(MeteorShowerExecutionKeys.Inputs.EventIdentity, context.InputValues.Keys);
        Assert.DoesNotContain(MeteorShowerExecutionKeys.Semantic.MeteorActivity, context.SemanticValues.Keys);
        Assert.Empty(context.ProjectionValues);
        Assert.Equal("  Keep spacing  ", context.Metadata["localViewingGuide.value"]);
    }

    [Fact]
    public void Build_treats_zero_and_false_as_present_but_null_and_whitespace_text_as_absent_for_inputs()
    {
        var observation = new MeteorShowerProductionObservation(
            "exec-presence",
            DateTimeOffset.UnixEpoch,
            ContentStrategy: "   ",
            EventIdentity: new MeteorShowerObservedValue("   "),
            EventStart: new MeteorShowerObservedValue(0),
            EventEnd: new MeteorShowerObservedValue(false),
            Language: new MeteorShowerObservedValue(null));

        var context = new MeteorShowerExecutionContextBuilder().Build(observation, MeteorShowerExecutionContractFactory.Create());

        Assert.DoesNotContain(MeteorShowerExecutionKeys.Inputs.EventIdentity, context.InputValues.Keys);
        Assert.DoesNotContain(MeteorShowerExecutionKeys.Inputs.Language, context.InputValues.Keys);
        Assert.DoesNotContain(MeteorShowerExecutionKeys.Inputs.ContentStrategy, context.InputValues.Keys);
        Assert.Equal(0, context.InputValues[MeteorShowerExecutionKeys.Inputs.EventStart].Value);
        Assert.Equal(false, context.InputValues[MeteorShowerExecutionKeys.Inputs.EventEnd].Value);
    }

    [Fact]
    public void Repeated_build_produces_semantically_identical_contexts_without_comparing_instances()
    {
        var builder = new MeteorShowerExecutionContextBuilder();
        var contract = MeteorShowerExecutionContractFactory.Create();
        var first = builder.Build(Observation(), contract);
        var second = builder.Build(Observation(), contract);

        Assert.Equal(first.ExecutionId, second.ExecutionId);
        Assert.Equal(first.DomainId, second.DomainId);
        Assert.Equal(first.FamilyId, second.FamilyId);
        Assert.Equal(first.ContractVersion, second.ContractVersion);
        Assert.Equal(Pairs(first.InputValues), Pairs(second.InputValues));
        Assert.Equal(Pairs(first.SemanticValues), Pairs(second.SemanticValues));
        Assert.Equal(Pairs(first.ProjectionValues), Pairs(second.ProjectionValues));
        Assert.Equal(RulePairs(first.ValidationRuleValues), RulePairs(second.ValidationRuleValues));
        Assert.Equal(first.Metadata.OrderBy(x => x.Key).ToArray(), second.Metadata.OrderBy(x => x.Key).ToArray());
    }


    private static MeteorShowerProductionObservation Observation() => new(
        "exec-2cb",
        DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
        ContentStrategy: " LocalViewingGuide ",
        EventIdentity: new MeteorShowerObservedValue("Geminids", "request.eventIdentity", "request", ["request evidence"]),
        EventStart: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-13T00:00:00Z")),
        EventEnd: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-15T12:00:00Z")),
        ObserverLocation: new MeteorShowerObservedValue("US"),
        Language: new MeteorShowerObservedValue("en"),
        Format: new MeteorShowerObservedValue("long"),
        LocalViewingGuide: new MeteorShowerObservedValue("Best after 1 a.m.; do not normalize.", SourceId: "guide-source", Evidence: ["guide evidence"]),
        ObservedMeteorActivity: new MeteorShowerObservedValue("canonical-meteor-activity", "canonical.semantic", "semantic-source", ["semantic evidence"]),
        ObservedRadiant: new MeteorShowerObservedValue("Gemini"),
        ObservedPeakWindow: new MeteorShowerObservedValue("midnight to dawn"),
        ObservedProjectedFacts: ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[] { Pair(MeteorShowerExecutionKeys.Projection.RadiantFact, new MeteorShowerObservedValue("Gemini")), Pair(MeteorShowerExecutionKeys.Projection.PeakWindowFact, new MeteorShowerObservedValue("midnight to dawn")) }),
        ObservedRuleValues: ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
        {
            Rule(MeteorShowerExecutionKeys.Rules.FamilyStrategyConsistency, true),
            Rule(MeteorShowerExecutionKeys.Rules.ActivityObserved, true),
            Rule(MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete, false, "incomplete", "complete", "observed incomplete", ["rule evidence"]),
            Rule(MeteorShowerExecutionKeys.Rules.RequiredFactsRetained, true)
        }),
        Metadata: ImmutableDictionary<string, string>.Empty.Add("traceId", "trace-1"));

    private static KeyValuePair<string, MeteorShowerObservedValue> Pair(string key, MeteorShowerObservedValue value) => new(key, value);
    private static KeyValuePair<string, MeteorShowerObservedRuleValue> Rule(string key, bool passed, string? actual = null, string? expected = null, string? message = null, ImmutableArray<string> evidence = default) => new(key, new MeteorShowerObservedRuleValue(passed, actual, expected, message, evidence));
    private static string[] Keys(ImmutableDictionary<string, ExecutionValue> values) => values.Keys.Order(StringComparer.Ordinal).ToArray();
    private static string[] RuleKeys(ImmutableDictionary<string, ExecutionRuleValue> values) => values.Keys.Order(StringComparer.Ordinal).ToArray();
    private static (string Key, object? Value, bool IsPresent, string? SourceId)[] Pairs(ImmutableDictionary<string, ExecutionValue> values) => values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, x.Value.Value, x.Value.IsPresent, x.Value.SourceId)).ToArray();
    private static (string Key, bool Passed, string? Actual, string? Expected, string? Message)[] RulePairs(ImmutableDictionary<string, ExecutionRuleValue> values) => values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, x.Value.Passed, x.Value.Actual, x.Value.Expected, x.Value.Message)).ToArray();
}
