using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeRevisionConvergenceFixture
{
    internal static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
    internal static DocumentaryNarrativeRevisionConvergencePolicy DefaultPolicy() => new(3, true, 2, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergencePolicy RegressionStoppingPolicy() => DefaultPolicy();
    internal static DocumentaryNarrativeRevisionConvergencePolicy RegressionContinuingPolicy() => new(3, false, 2, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergencePolicy OneCyclePolicy() => new(1, true, 2, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergencePolicy TwoCyclePolicy() => new(2, true, 2, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergencePolicy NoProgressThresholdOnePolicy() => new(3, false, 1, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergencePolicy NoProgressThresholdTwoPolicy() => new(3, false, 2, true, true, "1.0");
    internal static DocumentaryNarrativeRevisionConvergenceMetadata Metadata() => new(
        DateTimeOffset.Parse("2026-01-15T14:02:03.1234567+05:30"), " convergence coordinator ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeDraft InitiallyCleanDraft() => OrionDocumentaryNarrativeRevisionCycleFixture.CleanDraft();
    internal static DocumentaryNarrativeDraftValidationResult InitiallyCleanValidation() => new DocumentaryNarrativeDraftValidator().Validate(InitiallyCleanDraft());
    internal static DocumentaryNarrativeRevisionConvergenceState InitiallyCleanState() =>
        new DocumentaryNarrativeRevisionConvergenceStarter().Start(InitiallyCleanDraft(), InitiallyCleanValidation(), DefaultPolicy(), Metadata());
    internal static DocumentaryNarrativeDraft InitiallyInvalidDraft() => OrionDocumentaryNarrativeRevisionCycleFixture.CompleteSuccessfulPlan().SourceDraft;
    internal static DocumentaryNarrativeDraftValidationResult InitiallyInvalidValidation() => OrionDocumentaryNarrativeRevisionCycleFixture.CompleteSuccessfulPlan().SourceValidationResult;
    internal static DocumentaryNarrativeRevisionConvergenceState InitiallyInvalidState(DocumentaryNarrativeRevisionConvergencePolicy? policy = null) =>
        new DocumentaryNarrativeRevisionConvergenceStarter().Start(InitiallyInvalidDraft(), InitiallyInvalidValidation(), policy ?? DefaultPolicy(), Metadata());
    internal static DocumentaryNarrativeRevisionCycleResult SuccessfulCycle() => OrionDocumentaryNarrativeRevisionCycleFixture.CompletedSuccessfullyResult();
    internal static DocumentaryNarrativeRevisionConvergenceAdvanceRequest Request(DocumentaryNarrativeRevisionConvergenceState state,
        DocumentaryNarrativeRevisionCycleResult cycle) => new(state, cycle, DateTimeOffset.Parse("2026-01-15T18:02:03.7654321-04:00"),
            " convergence advancer ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeRevisionConvergenceState OneCycleSuccessfulState() =>
        new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(Request(InitiallyInvalidState(), SuccessfulCycle()));
    internal static DocumentaryNarrativeRevisionConvergenceState ResolvedAndIntroducedState()
    {
        var cycle = OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult();
        var initial = new DocumentaryNarrativeRevisionConvergenceStarter().Start(cycle.Plan.SourceDraft,
            cycle.Plan.SourceValidationResult, RegressionContinuingPolicy(), Metadata());
        return new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(Request(initial, cycle));
    }
}
