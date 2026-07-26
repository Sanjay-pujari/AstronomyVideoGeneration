using System.Collections;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceSummaryContractCertificationTests
{
    private static DocumentaryNarrativeRevisionConvergenceSummary Valid() =>
        new("convergence.orion", "draft.original", "1", "draft.current", "2", 2, 1, 1,
            1, 1, 0, 0, [DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings],
            [2, 1], [1], [0], true, false, false);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Rejects_every_negative_aggregate_count(int index)
    {
        var counts = new[] { 2, 1, 1, 1, 1, 0, 0 }; counts[index] = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentaryNarrativeRevisionConvergenceSummary(
            "c", "o", "1", "n", "2", counts[0], counts[1], counts[2], counts[3], counts[4], counts[5], counts[6],
            [DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings], [2, 1], [1], [0], true, false, false));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Rejects_each_null_history(int index)
    {
        object[] histories = { new[] { DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings },
            new[] { 2, 1 }, new[] { 1 }, new[] { 0 } };
        histories[index] = null!;
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeRevisionConvergenceSummary("c", "o", "1", "n", "2",
            2, 1, 1, 1, 1, 0, 0, (IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus>)histories[0],
            (IReadOnlyList<int>)histories[1], (IReadOnlyList<int>)histories[2], (IReadOnlyList<int>)histories[3], true, false, false));
    }

    [Fact]
    public void Rejects_history_lengths_endpoints_negative_elements_and_inconsistent_booleans()
    {
        static DocumentaryNarrativeRevisionConvergenceSummary Make(IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus>? statuses = null,
            IReadOnlyList<int>? findings = null, IReadOnlyList<int>? applied = null, IReadOnlyList<int>? unresolved = null,
            bool improved = true, bool regressed = false, bool clean = false) => new("c", "o", "1", "n", "2", 2, 1, 1, 1, 1, 0, 0,
                statuses ?? [DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings], findings ?? [2, 1], applied ?? [1], unresolved ?? [0], improved, regressed, clean);
        Assert.Throws<ArgumentException>(() => Make(statuses: []));
        Assert.Throws<ArgumentException>(() => Make(findings: [2]));
        Assert.Throws<ArgumentException>(() => Make(applied: []));
        Assert.Throws<ArgumentException>(() => Make(unresolved: []));
        Assert.Throws<ArgumentException>(() => Make(findings: [3, 1]));
        Assert.Throws<ArgumentException>(() => Make(findings: [2, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(applied: [-1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(unresolved: [-1]));
        Assert.Throws<ArgumentException>(() => Make(improved: false));
        Assert.Throws<ArgumentException>(() => Make(regressed: true));
        Assert.Throws<ArgumentException>(() => Make(clean: true));
    }

    [Fact]
    public void Zero_cycle_contract_requires_one_finding_entry_and_defensively_copies()
    {
        var history = new List<int> { 2 };
        var summary = new DocumentaryNarrativeRevisionConvergenceSummary("c", "o", "1", "o", "1", 2, 2, 0, 0, 0, 0, 0,
            [], history, [], [], false, false, false);
        history[0] = 99;
        Assert.Equal([2], summary.FindingCountHistory);
        Assert.Throws<NotSupportedException>(() => ((IList)summary.FindingCountHistory).Clear());
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionConvergenceSummary("c", "o", "1", "o", "1", 2, 2, 0,
            0, 0, 0, 0, [], [], [], [], false, false, false));
    }

    [Fact]
    public void Valid_contract_round_trips_deterministically() =>
        DocumentaryNarrativeRevisionConvergencePolicyTests.RoundTrip(Valid());
}

public sealed class DocumentaryNarrativeRevisionConvergenceRemainingArchitectureTests
{
    [Fact]
    public void Exact_enum_and_property_inventories_are_read_only()
    {
        Assert.Equal(["NotStarted", "InProgress", "ConvergedSuccessfully", "StoppedByCycleLimit", "StoppedByNoProgress", "StoppedByRegression", "RequiresManualEscalation"], Enum.GetNames<DocumentaryNarrativeRevisionConvergenceStatus>());
        Assert.Equal(["None", "PlanNextRevisionCycle", "ObtainExternalRevisionSubmission", "PerformManualReview", "AcceptCurrentDraft", "TerminateRevisionProcess"], Enum.GetNames<DocumentaryNarrativeRevisionConvergenceNextAction>());
        Properties<DocumentaryNarrativeRevisionConvergencePolicy>("MaximumCycleCount", "StopOnRegression", "MaximumConsecutiveNoProgressCycles", "RequireCleanValidationForSuccess", "RequireNoUnresolvedRevisionItemsForSuccess", "PolicySchemaVersion");
        Properties<DocumentaryNarrativeRevisionConvergenceMetadata>("CreatedUtc", "CreatedBy", "ConvergenceSchemaVersion", "CorrelationId");
        Properties<DocumentaryNarrativeRevisionConvergenceAdvanceRequest>("CurrentState", "CompletedCycleResult", "AdvancedUtc", "AdvancedBy", "AdvanceSchemaVersion", "CorrelationId");
        Properties<DocumentaryNarrativeRevisionConvergenceSummary>("ConvergenceId", "OriginalDraftId", "OriginalDraftVersion", "CurrentDraftId", "CurrentDraftVersion", "InitialFindingCount", "CurrentFindingCount", "CompletedCycleCount", "TotalAppliedChangeCount", "TotalResolvedFindingCount", "TotalRemainingFindingCount", "TotalIntroducedFindingCount", "CycleStatuses", "FindingCountHistory", "AppliedChangeCountHistory", "UnresolvedRevisionItemCountHistory", "HasImproved", "HasRegressed", "IsClean");
    }

    [Fact]
    public void Exact_operations_are_sealed_parameterless_synchronous_and_stateless()
    {
        Operation<DocumentaryNarrativeRevisionConvergenceStarter>("Start", typeof(DocumentaryNarrativeRevisionConvergenceState), typeof(DocumentaryNarrativeDraft), typeof(DocumentaryNarrativeDraftValidationResult), typeof(DocumentaryNarrativeRevisionConvergencePolicy), typeof(DocumentaryNarrativeRevisionConvergenceMetadata));
        Operation<DocumentaryNarrativeRevisionConvergenceAdvancer>("Advance", typeof(DocumentaryNarrativeRevisionConvergenceState), typeof(DocumentaryNarrativeRevisionConvergenceAdvanceRequest));
        Operation<DocumentaryNarrativeRevisionConvergenceSummarizer>("Summarize", typeof(DocumentaryNarrativeRevisionConvergenceSummary), typeof(DocumentaryNarrativeRevisionConvergenceState));
    }

    [Fact]
    public void Public_surface_has_no_forbidden_capability()
    {
        string[] forbidden = ["Prompt", "PromptText", "SystemPrompt", "UserPrompt", "Provider", "ProviderName", "ModelName", "DeploymentName", "Temperature", "TopP", "MaxTokens", "TokenCount", "RawModelResponse", "GeneratedText", "SuggestedText", "CorrectedText", "AutoFix", "Http", "Endpoint", "RetryCount", "RetryDelay", "Repository", "Database", "Storage", "Scheduler", "Cron", "Queue", "MessageBus", "Ssml", "Voice", "Audio", "Subtitle", "Srt", "Vtt"];
        Type[] types = [typeof(DocumentaryNarrativeRevisionConvergencePolicy), typeof(DocumentaryNarrativeRevisionConvergenceMetadata), typeof(DocumentaryNarrativeRevisionConvergenceState), typeof(DocumentaryNarrativeRevisionConvergenceAdvanceRequest), typeof(DocumentaryNarrativeRevisionConvergenceSummary), typeof(DocumentaryNarrativeRevisionConvergenceStarter), typeof(DocumentaryNarrativeRevisionConvergenceAdvancer), typeof(DocumentaryNarrativeRevisionConvergenceSummarizer)];
        Assert.All(types.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)), m => Assert.DoesNotContain(forbidden, word => m.Name.Contains(word, StringComparison.Ordinal)));
    }

    private static void Properties<T>(params string[] expected)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(expected.Order(), properties.Select(x => x.Name).Order());
        Assert.All(properties, property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    private static void Operation<T>(string name, Type result, params Type[] parameters)
    {
        var type = typeof(T); Assert.True(type.IsSealed); Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(name, method.Name); Assert.Equal(result, method.ReturnType);
        Assert.Equal(parameters, method.GetParameters().Select(x => x.ParameterType));
        Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }
}

public sealed class DocumentaryNarrativeRevisionConvergenceScenarioCertificationTests
{
    [Fact]
    public void Initial_states_have_exact_status_action_and_summary_histories()
    {
        var clean = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyCleanState();
        var invalid = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, clean.Status);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft, clean.NextAction);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.NotStarted, invalid.Status);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceNextAction.PlanNextRevisionCycle, invalid.NextAction);
        var summarizer = new DocumentaryNarrativeRevisionConvergenceSummarizer();
        Assert.Equal([0], summarizer.Summarize(clean).FindingCountHistory);
        Assert.Equal([invalid.InitialFindingCount], summarizer.Summarize(invalid).FindingCountHistory);
    }

    [Fact]
    public void Successful_first_cycle_certifies_lineage_cumulative_metrics_non_mutation_and_determinism()
    {
        var initial = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var cycle = OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle();
        var request = OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(initial, cycle);
        var options = OrionDocumentaryNarrativeRevisionConvergenceFixture.JsonOptions();
        var before = JsonSerializer.Serialize(request, options);
        var state = new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(request);
        Assert.Equal(before, JsonSerializer.Serialize(request, options));
        Assert.Equal(cycle.TargetDraftId, state.CurrentDraftId); Assert.Equal(cycle.TargetDraftVersion, state.CurrentDraftVersion);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, state.Status);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft, state.NextAction);
        Assert.Equal(cycle.AppliedChangeCount, state.TotalAppliedChangeCount);
        Assert.Equal(JsonSerializer.Serialize(state, options), JsonSerializer.Serialize(OrionDocumentaryNarrativeRevisionConvergenceFixture.OneCycleSuccessfulState(), options));
        Assert.Throws<InvalidOperationException>(() => new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(state, cycle)));
    }

    [Fact]
    public void Non_clean_cycle_can_continue_or_stop_at_cycle_limit_and_summaries_preserve_remaining_evidence()
    {
        var cycle = OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult();
        DocumentaryNarrativeRevisionConvergenceState Run(DocumentaryNarrativeRevisionConvergencePolicy policy) =>
            new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(
                new DocumentaryNarrativeRevisionConvergenceStarter().Start(cycle.Plan.SourceDraft, cycle.Plan.SourceValidationResult, policy, OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata()), cycle));
        var continuing = Run(OrionDocumentaryNarrativeRevisionConvergenceFixture.RegressionContinuingPolicy());
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.InProgress, continuing.Status);
        Assert.True(continuing.RequiresAnotherCycle);
        var stopped = Run(new DocumentaryNarrativeRevisionConvergencePolicy(1, false, 2, true, true, "1.0"));
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.StoppedByCycleLimit, stopped.Status);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceNextAction.TerminateRevisionProcess, stopped.NextAction);
        var summary = new DocumentaryNarrativeRevisionConvergenceSummarizer().Summarize(continuing);
        Assert.Equal(continuing.Cycles.Sum(x => x.ValidationComparison.RemainingFindingCount), summary.TotalRemainingFindingCount);
        Assert.Equal([continuing.InitialFindingCount, continuing.CurrentFindingCount], summary.FindingCountHistory);
        Assert.Throws<InvalidOperationException>(() => new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(stopped, cycle)));
    }
}
