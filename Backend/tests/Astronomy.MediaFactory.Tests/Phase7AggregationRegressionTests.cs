using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7AggregationRegressionTests
{
    [Fact]
    public void FailedPhaseErrorsAndReasonsBuildAuthoritativeResponseErrors()
    {
        var failedWithErrors = Phase(7, ProductionPhaseStatus.Failed, ["Required Phase 7 output invalid: artifactId=SceneIdentityDiagnostics, validatorId=Phase7RequiredOutputValidator, failureReason=InvalidJson"], "generic");
        var failedWithReason = Phase(8, ProductionPhaseStatus.Failed, [], "phase 8 reason");

        var errors = ContentPlanProductionExecutionService.BuildAuthoritativeErrors(["top", "top"], [failedWithErrors, failedWithReason]);

        Assert.Equal(3, errors.Count);
        Assert.Contains("top", errors);
        Assert.Contains("Required Phase 7 output invalid: artifactId=SceneIdentityDiagnostics, validatorId=Phase7RequiredOutputValidator, failureReason=InvalidJson", errors);
        Assert.Contains("phase 8 reason", errors);
    }

    [Fact]
    public void DuplicatePhaseErrorsAreDeduplicated()
    {
        var errors = ContentPlanProductionExecutionService.BuildAuthoritativeErrors(["same"], [Phase(7, ProductionPhaseStatus.Failed, ["same"], null)]);
        Assert.Single(errors);
    }

    [Fact]
    public void RequestedRangeCannotHideActuallyExecutedFailedPhase7()
    {
        var request = Request(start: 8, end: 20);
        var phases = Enumerable.Range(1, 6).Select(no => Phase(no, ProductionPhaseStatus.Succeeded)).Concat([Phase(7, ProductionPhaseStatus.Failed)]).ToArray();

        var diagnostics = ContentPlanProductionExecutionService.BuildSuccessAggregationDiagnostics(request, phases, []);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], diagnostics.ExecutedPhaseNumbers);
        Assert.False(diagnostics.AllExecutedPhasesSucceeded);
        Assert.Equal([7], diagnostics.FailedExecutedPhases);
    }

    [Fact]
    public void Phase7FailureAggregationUsesLastCompletedAndLastFailedConsistently()
    {
        var phases = Enumerable.Range(1, 6).Select(no => Phase(no, ProductionPhaseStatus.Succeeded)).Concat([Phase(7, ProductionPhaseStatus.Failed)]).ToArray();
        var diagnostics = ContentPlanProductionExecutionService.BuildSuccessAggregationDiagnostics(Request(1, 7), phases, []);

        var lastCompleted = CalculateLastCompleted(phases);
        var lastFailed = phases.Where(p => p.Status == ProductionPhaseStatus.Failed).Select(p => (int?)p.PhaseNo).LastOrDefault();
        var partialPhaseSuccess = diagnostics.AllExecutedPhasesSucceeded;
        var resultSuccess = diagnostics.AllExecutedPhasesSucceeded && diagnostics.FailedExecutedPhases.Count == 0;
        var topLevelSuccess = resultSuccess;

        Assert.Equal(6, lastCompleted);
        Assert.Equal(7, lastFailed);
        Assert.False(partialPhaseSuccess);
        Assert.False(resultSuccess);
        Assert.Equal(resultSuccess, topLevelSuccess);
    }

    [Fact]
    public void AllPhasesOneThroughSevenSucceededAggregatesSuccessEverywhere()
    {
        var phases = Enumerable.Range(1, 7).Select(no => Phase(no, ProductionPhaseStatus.Succeeded)).ToArray();
        var diagnostics = ContentPlanProductionExecutionService.BuildSuccessAggregationDiagnostics(Request(1, 7), phases, []);
        var resultSuccess = diagnostics.AllExecutedPhasesSucceeded && diagnostics.FailedExecutedPhases.Count == 0;
        var topLevelSuccess = resultSuccess;

        Assert.True(diagnostics.AllExecutedPhasesSucceeded);
        Assert.Empty(diagnostics.FailedExecutedPhases);
        Assert.True(resultSuccess);
        Assert.Equal(resultSuccess, topLevelSuccess);
    }

    [Fact]
    public void Rc2OverlaySourceDoesNotContainHardCodedPhase7MissingOutputFailure()
    {
        var repoRoot = RepositoryTestPaths.Root();
        var sourcePath = Path.Combine(repoRoot, "Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("Validation failed: required output missing.", source);
    }

    private static ContentPlanProductionExecutionRequest Request(int start, int end)
        => new(Guid.NewGuid(), false, StartPhaseNo: start, EndPhaseNo: end);

    private static ProductionPhaseResult Phase(int no, ProductionPhaseStatus status, IReadOnlyList<string>? errors = null, string? reason = null)
        => new(no, $"Phase {no}", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, [], [], $"validation/phase-{no:00}.json", [], errors ?? [], status == ProductionPhaseStatus.Failed, reason);

    private static int? CalculateLastCompleted(IReadOnlyList<ProductionPhaseResult> phases)
    {
        int? last = null;
        foreach (var phase in phases.OrderBy(p => p.PhaseNo))
        {
            if (phase.Status == ProductionPhaseStatus.Failed) break;
            if (phase.Status == ProductionPhaseStatus.Succeeded) last = phase.PhaseNo;
        }
        return last;
    }
}
