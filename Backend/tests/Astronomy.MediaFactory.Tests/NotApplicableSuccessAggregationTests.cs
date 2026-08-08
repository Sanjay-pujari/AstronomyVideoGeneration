using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class NotApplicableSuccessAggregationTests
{
    [Fact]
    public void Phase11NotRequestedPartialRunIsSuccessful() => Assert.True(Phase11().Success);

    [Fact]
    public void Phase11NotRequestedDoesNotCountAsFailedPlan() => Assert.Equal(0, Phase11().FailedPlans);

    [Fact]
    public void Phase11NotRequestedPreservesExecutedPhaseNumbersEmpty() => Assert.Empty(Phase11().ExecutedPhaseNumbers);

    [Fact]
    public void Phase11NotRequestedIsGovernedSatisfiedOutcome()
    {
        var diagnostics = Phase11();
        Assert.True(diagnostics.AllExecutedPhasesSucceeded);
        Assert.Equal([11], diagnostics.SatisfiedPhaseNumbers);
        Assert.Equal([11], diagnostics.NotApplicablePhaseNumbers);
        Assert.Equal([11], diagnostics.SkippedPhaseNumbers);
    }

    [Fact]
    public void Phase11SkipReasonCodePropagatesToCanonicalValidation() =>
        Assert.Equal(Phase11ReasonCodes.NotRequested, ProductionPipelineExecutionService.GovernedNotApplicableReasonCode(11));

    [Fact]
    public void Phase9LongNotRequestedStillAggregatesSuccessfully()
    {
        var diagnostics = Aggregate(NotApplicable(9, Phase9ReasonCodes.LongNotRequested), 9);
        Assert.True(diagnostics.Success);
        Assert.Equal([9], diagnostics.NotApplicablePhaseNumbers);
    }

    [Fact]
    public void UngovernedSkipDoesNotBecomeSuccess() =>
        Assert.False(Aggregate(NotApplicable(11, "P11_UNKNOWN_SKIP"), 11).Success);

    [Fact]
    public void DependencyBlockedSkipStillFails() =>
        Assert.False(Aggregate(NotApplicable(11, "P11_DEPENDENCY_BLOCKED"), 11).Success);

    [Fact]
    public void FailedPhaseStillFailsAggregation()
    {
        var failed = Phase(11, ProductionPhaseStatus.Failed) with { ReasonCode = "P11_INTERNAL_ERROR" };
        var diagnostics = Aggregate(failed, 11);
        Assert.False(diagnostics.Success);
        Assert.Equal([11], diagnostics.FailedExecutedPhases);
    }

    [Fact]
    public void NotApplicableRunPerformsNoCleanup()
    {
        var phase = NotApplicable(11, Phase11ReasonCodes.NotRequested);
        Assert.Empty(phase.OutputFiles);
        Assert.False(phase.PublicationCommitted);
        Assert.False(phase.CommittedStateValidationPassed);
    }

    private static SuccessAggregationDiagnostics Phase11() =>
        Aggregate(NotApplicable(11, Phase11ReasonCodes.NotRequested), 11);

    private static SuccessAggregationDiagnostics Aggregate(ProductionPhaseResult phase, int phaseNo) =>
        ContentPlanProductionExecutionService.BuildSuccessAggregationDiagnostics(
            new ContentPlanProductionExecutionRequest(Guid.NewGuid(), false, StartPhaseNo: phaseNo, EndPhaseNo: phaseNo),
            [phase], []);

    private static ProductionPhaseResult NotApplicable(int phaseNo, string reasonCode) =>
        Phase(phaseNo, ProductionPhaseStatus.Skipped) with
        {
            Reason = "Output type not requested",
            ReasonCode = reasonCode
        };

    private static ProductionPhaseResult Phase(int phaseNo, ProductionPhaseStatus status) =>
        new(phaseNo, $"Phase {phaseNo}", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            0, [], [], $"validation/phase-{phaseNo:00}-validation.json", [], [], false);
}
