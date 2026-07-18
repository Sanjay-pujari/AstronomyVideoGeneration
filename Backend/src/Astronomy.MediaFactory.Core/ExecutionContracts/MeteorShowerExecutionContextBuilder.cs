using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionValidation;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public interface IMeteorShowerExecutionContextBuilder
{
    FamilyExecutionContext Build(MeteorShowerProductionObservation observation, FamilyExecutionContract contract);
}

/// <summary>Builds immutable Meteor Shower execution contexts from already-observed production state only.</summary>
public sealed class MeteorShowerExecutionContextBuilder : IMeteorShowerExecutionContextBuilder
{
    private const string DomainId = "Astronomy";
    public FamilyExecutionContext Build(MeteorShowerProductionObservation observation, FamilyExecutionContract contract)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(contract);

        var inputs = ImmutableDictionary.CreateBuilder<string, ExecutionValue>(StringComparer.OrdinalIgnoreCase);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.EventIdentity, observation.EventIdentity);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.EventStart, observation.EventStart);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.EventEnd, observation.EventEnd);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.ObserverLocation, observation.ObserverLocation);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.Language, observation.Language);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.Format, observation.Format);
        AddInput(inputs, MeteorShowerExecutionKeys.Inputs.ContentStrategy, new MeteorShowerObservedValue(observation.ContentStrategy, "request.contentStrategy"));

        var semantic = ImmutableDictionary.CreateBuilder<string, ExecutionValue>(StringComparer.OrdinalIgnoreCase);
        AddObserved(semantic, MeteorShowerExecutionKeys.Semantic.MeteorActivity, observation.ObservedMeteorActivity);
        AddObserved(semantic, MeteorShowerExecutionKeys.Semantic.Radiant, observation.ObservedRadiant);
        AddObserved(semantic, MeteorShowerExecutionKeys.Semantic.PeakWindow, observation.ObservedPeakWindow);

        var projections = ImmutableDictionary.CreateBuilder<string, ExecutionValue>(StringComparer.OrdinalIgnoreCase);
        AddProjected(projections, MeteorShowerExecutionKeys.Projection.RadiantFact, observation.ObservedProjectedFacts);
        AddProjected(projections, MeteorShowerExecutionKeys.Projection.PeakWindowFact, observation.ObservedProjectedFacts);

        var rules = ImmutableDictionary.CreateBuilder<string, ExecutionRuleValue>(StringComparer.OrdinalIgnoreCase);
        AddRule(rules, MeteorShowerExecutionKeys.Rules.FamilyStrategyConsistency, observation.ObservedRuleValues);
        AddRule(rules, MeteorShowerExecutionKeys.Rules.ActivityObserved, observation.ObservedRuleValues);
        AddRule(rules, MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete, observation.ObservedRuleValues);
        AddRule(rules, MeteorShowerExecutionKeys.Rules.RequiredFactsRetained, observation.ObservedRuleValues);

        var metadata = observation.Metadata.ToBuilder();
        PreserveMetadata(metadata, "localViewingGuide", observation.LocalViewingGuide);

        return new FamilyExecutionContext(
            observation.ExecutionId,
            DomainId,
            contract.FamilyId,
            contract.ContractVersion,
            observation.ObservedUtc,
            Format: ValueAsString(observation.Format),
            Language: ValueAsString(observation.Language),
            RegionId: ValueAsString(observation.ObserverLocation),
            InputValues: inputs.ToImmutable(),
            SemanticValues: semantic.ToImmutable(),
            ProjectionValues: projections.ToImmutable(),
            ValidationRuleValues: rules.ToImmutable(),
            Metadata: metadata.ToImmutable());
    }

    private static void AddInput(ImmutableDictionary<string, ExecutionValue>.Builder target, string key, MeteorShowerObservedValue? observed)
    {
        if (!IsObservedInputPresent(observed)) return;
        target.Add(key, ToExecutionValue(key, observed!));
    }

    private static bool IsObservedInputPresent(MeteorShowerObservedValue? observed) => observed?.Value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true
    };

    private static void AddObserved(ImmutableDictionary<string, ExecutionValue>.Builder target, string key, MeteorShowerObservedValue? observed)
    {
        if (observed?.Value is null) return;
        target.Add(key, ToExecutionValue(key, observed));
    }

    private static void AddProjected(ImmutableDictionary<string, ExecutionValue>.Builder target, string key, ImmutableDictionary<string, MeteorShowerObservedValue> observed)
    {
        if (observed.TryGetValue(key, out var value) && value.Value is not null) target.Add(key, ToExecutionValue(key, value));
    }

    private static ExecutionValue ToExecutionValue(string key, MeteorShowerObservedValue observed) => new(key, observed.Value, true, observed.ValueType, observed.SourceId, observed.Evidence, observed.Metadata);

    private static void AddRule(ImmutableDictionary<string, ExecutionRuleValue>.Builder target, string key, ImmutableDictionary<string, MeteorShowerObservedRuleValue> observed)
    {
        if (!observed.TryGetValue(key, out var value)) return;
        target.Add(key, new ExecutionRuleValue(key, value.Passed, value.Actual, value.Expected, value.Message, value.Evidence, value.Metadata));
    }

    private static void PreserveMetadata(ImmutableDictionary<string, string>.Builder metadata, string prefix, MeteorShowerObservedValue? observed)
    {
        if (observed?.Value is not null) metadata[$"{prefix}.value"] = observed.Value.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(observed?.SourceId)) metadata[$"{prefix}.sourceId"] = observed.SourceId!;
        if (observed is not null && !observed.Evidence.IsDefaultOrEmpty) metadata[$"{prefix}.evidence"] = string.Join(" | ", observed.Evidence);
    }

    private static string? ValueAsString(MeteorShowerObservedValue? observed) => observed?.Value as string;
}
