using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;
using Astronomy.MediaFactory.Core.ExecutionValidation;
using Xunit;

namespace Astronomy.MediaFactory.Tests.ExecutionValidation;

public sealed class MeteorShowerShadowValidationIntegrationTests
{
    [Fact]
    public void Shadow_validation_reports_invalid_without_creating_missing_semantics()
    {
        var domain = AstronomyExecutionContractCatalog.Create();
        var contract = domain.Families.Single(f => f.FamilyId == MeteorShowerExecutionKeys.FamilyId);
        var observation = new MeteorShowerProductionObservation(
            "meteor-shower-geminids-2026",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ContentStrategy: "LocalViewingGuide",
            EventIdentity: new MeteorShowerObservedValue("meteor-shower-geminids-2026", "string", "request.sourceExternalEventId"),
            EventStart: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-13T18:00:00Z"), "DateTimeOffset", "request.startUtc"),
            EventEnd: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-14T12:00:00Z"), "DateTimeOffset", "request.endUtc"),
            ObserverLocation: new MeteorShowerObservedValue("IN-RJ-UDAIPUR", "string", "request.regionId"),
            Language: new MeteorShowerObservedValue("en", "string", "request.language"),
            ObservedRuleValues: ImmutableDictionary<string, MeteorShowerObservedRuleValue>.Empty
                .Add(MeteorShowerExecutionKeys.Rules.ActivityObserved, new MeteorShowerObservedRuleValue(false, "missing", "present", "MeteorActivity was not observed in production semantic output.")));

        var context = new MeteorShowerExecutionContextBuilder().Build(observation, contract);
        var pipeline = ExecutionValidationPipelineFactory.CreateDefault(new FixedClock());
        var semantic = pipeline.Validate(new ExecutionValidationRequest(domain, contract, context, FamilyValidationBoundary.SemanticResolution, StartedUtc: DateTimeOffset.UnixEpoch));
        var post = pipeline.Validate(new ExecutionValidationRequest(domain, contract, context, FamilyValidationBoundary.PostExecution, StartedUtc: DateTimeOffset.UnixEpoch));

        Assert.Equal(ExecutionValidationStatus.Invalid, semantic.Status);
        var missingSemanticKeys = semantic.Issues
            .Where(i => i.IssueCode == ExecutionValidationIssueCode.RequiredSemanticValueMissing)
            .Select(i => i.SourceKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            MeteorShowerExecutionKeys.Semantic.MeteorActivity,
            MeteorShowerExecutionKeys.Semantic.PeakWindow,
            MeteorShowerExecutionKeys.Semantic.Radiant
        }, missingSemanticKeys);
        Assert.Empty(context.SemanticValues.ToArray());
        Assert.Empty(context.ProjectionValues.ToArray());
        Assert.Contains(semantic.Issues.Select(i => new { i.RequirementId, i.SourceKey, i.IssueCode }).ToArray(), i =>
            i.SourceKey == MeteorShowerExecutionKeys.Rules.ActivityObserved &&
            i.IssueCode == ExecutionValidationIssueCode.ValidationRuleFailed);
        Assert.DoesNotContain(post.Issues, i => i.SourceKey == MeteorShowerExecutionKeys.Rules.ActivityObserved);
    }

    [Fact]
    public void Shadow_validation_preserves_observed_values_and_reports_boundaries_structurally()
    {
        var domain = AstronomyExecutionContractCatalog.Create();
        var contract = domain.Families.Single(f => f.FamilyId == MeteorShowerExecutionKeys.FamilyId);
        var observation = new MeteorShowerProductionObservation(
            "geminids-2026",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ContentStrategy: "MeteorShower",
            EventIdentity: new MeteorShowerObservedValue("geminids-2026", "string", "request.sourceExternalEventId"),
            EventStart: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-13T18:00:00Z"), "DateTimeOffset", "request.startUtc"),
            EventEnd: new MeteorShowerObservedValue(DateTimeOffset.Parse("2026-12-14T12:00:00Z"), "DateTimeOffset", "request.endUtc"),
            ObserverLocation: new MeteorShowerObservedValue("Udaipur", "string", "request.regionName"),
            Language: new MeteorShowerObservedValue("en", "string", "request.language"),
            ObservedMeteorActivity: new MeteorShowerObservedValue("Geminids", "MeteorActivity", "semanticResolution"),
            ObservedRadiant: new MeteorShowerObservedValue("Gemini", "Radiant", "semanticResolution"),
            ObservedPeakWindow: new MeteorShowerObservedValue("after midnight", "PeakWindow", "semanticResolution"),
            ObservedProjectedFacts: ImmutableDictionary<string, MeteorShowerObservedValue>.Empty
                .Add(MeteorShowerExecutionKeys.Projection.RadiantFact, new MeteorShowerObservedValue("Gemini", "Radiant", "productionProjection"))
                .Add(MeteorShowerExecutionKeys.Projection.PeakWindowFact, new MeteorShowerObservedValue("after midnight", "PeakWindow", "productionProjection")),
            ObservedRuleValues: ImmutableDictionary<string, MeteorShowerObservedRuleValue>.Empty
                .Add(MeteorShowerExecutionKeys.Rules.SemanticLifecycleComplete, new MeteorShowerObservedRuleValue(true))
                .Add(MeteorShowerExecutionKeys.Rules.RequiredFactsRetained, new MeteorShowerObservedRuleValue(true))
                .Add(MeteorShowerExecutionKeys.Rules.ActivityObserved, new MeteorShowerObservedRuleValue(true)));

        var context = new MeteorShowerExecutionContextBuilder().Build(observation, contract);
        var pipeline = ExecutionValidationPipelineFactory.CreateDefault(new FixedClock());
        var results = new[] { FamilyValidationBoundary.PreExecution, FamilyValidationBoundary.SemanticResolution, FamilyValidationBoundary.Projection, FamilyValidationBoundary.PostExecution }
            .Select(b => pipeline.Validate(new ExecutionValidationRequest(domain, contract, context, b, StartedUtc: DateTimeOffset.UnixEpoch)))
            .ToArray();

        Assert.Equal(new[] { "contentStrategy=MeteorShower", "eventIdentity=geminids-2026", "language=en" }, context.InputValues.OrderBy(p => p.Key, StringComparer.Ordinal).Where(p => p.Key is "contentStrategy" or "eventIdentity" or "language").Select(p => $"{p.Key}={p.Value.Value}").ToArray());
        Assert.Equal(new[] { FamilyValidationBoundary.PreExecution, FamilyValidationBoundary.SemanticResolution, FamilyValidationBoundary.Projection, FamilyValidationBoundary.PostExecution }, results.Select(r => r.Boundary).ToArray());
        Assert.DoesNotContain(results.SelectMany(r => r.Issues).Select(i => i.IssueCode).ToArray(), code => code is ExecutionValidationIssueCode.RequiredSemanticValueMissing or ExecutionValidationIssueCode.RequiredProjectionMissing);
    }

    private sealed class FixedClock : IExecutionClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
