using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase6ResponseAggregationTests
{
    public static TheoryData<string> ReuseCodes => new()
    {
        "P1_RESUME_REUSABLE", "P2_REUSED", "P3_REUSED", "P4PUB_ALREADY_PUBLISHED",
        "P4REUSE_VALID", "P5REUSE_VALID", "P6REUSE_VALID"
    };

    [Theory]
    [MemberData(nameof(ReuseCodes))]
    public void SatisfiedClassifier_RecognizesCanonicalReuseCodes(string code) =>
        Assert.True(ProductionPhaseSatisfaction.IsSatisfied(Phase(6, ProductionPhaseStatus.Skipped, code)));

    [Fact]
    public void Aggregation_SucceededPhaseCountsAsSatisfied() =>
        Assert.True(ProductionPhaseSatisfaction.IsSatisfied(Phase(1, ProductionPhaseStatus.Succeeded)));

    [Fact]
    public void Aggregation_P6ReuseValidCountsAsSatisfied() =>
        Assert.True(ProductionPhaseSatisfaction.IsSatisfied(Phase(6, ProductionPhaseStatus.Skipped, "P6REUSE_VALID")));

    [Fact]
    public void SatisfiedClassifier_DoesNotRecognizeArbitrarySkippedReason() =>
        Assert.False(ProductionPhaseSatisfaction.IsSatisfied(Phase(6, ProductionPhaseStatus.Skipped, "OUT_OF_SCOPE")));

    [Fact]
    public void SatisfiedClassifier_DoesNotUseReasonText()
    {
        var phase = Phase(6, ProductionPhaseStatus.Skipped) with { Reason = "Valid reuse P6REUSE_VALID" };
        Assert.False(ProductionPhaseSatisfaction.IsSatisfied(phase));
    }

    [Fact]
    public void Aggregation_Phase6ReuseSetsLastCompletedPhaseToSix()
    {
        var diagnostics = Aggregate([
            Phase(1, ProductionPhaseStatus.Skipped, "P1_RESUME_REUSABLE"),
            Phase(2, ProductionPhaseStatus.Skipped, "P2_REUSED"),
            Phase(3, ProductionPhaseStatus.Skipped, "P3_REUSED"),
            Phase(4, ProductionPhaseStatus.Succeeded),
            Phase(5, ProductionPhaseStatus.Succeeded),
            Phase(6, ProductionPhaseStatus.Skipped, "P6REUSE_VALID")]);

        Assert.True(diagnostics.Success);
        Assert.True(diagnostics.PartialPhaseSuccess);
        Assert.Equal(6, diagnostics.LastCompletedPhaseNo);
        Assert.Null(diagnostics.LastFailedPhaseNo);
        Assert.Empty(diagnostics.FailedExecutedPhases);
        Assert.Equal([1, 2, 3, 4, 5, 6], diagnostics.SatisfiedPhaseNumbers);
        Assert.Equal([1, 2, 3, 6], diagnostics.ReusedPhaseNumbers);
        Assert.Equal([4, 5], diagnostics.ExecutedPhaseNumbers);
    }

    [Fact]
    public void Aggregation_UnsupportedSkipDoesNotCountAsReusableCompletion()
    {
        var diagnostics = Aggregate([Phase(1, ProductionPhaseStatus.Skipped, "UNSUPPORTED")], 1, 1);
        Assert.False(diagnostics.Success);
        Assert.Null(diagnostics.LastCompletedPhaseNo);
    }

    [Fact]
    public void Aggregation_FailedPhaseOverridesReusablePhases()
    {
        var diagnostics = Aggregate([
            Phase(1, ProductionPhaseStatus.Skipped, "P1_RESUME_REUSABLE"),
            Phase(2, ProductionPhaseStatus.Failed)], 1, 2);
        Assert.False(diagnostics.Success);
        Assert.Equal(2, diagnostics.LastFailedPhaseNo);
    }

    private static SuccessAggregationDiagnostics Aggregate(IReadOnlyList<ProductionPhaseResult> phases, int start = 1, int end = 6) =>
        ContentPlanProductionExecutionService.BuildSuccessAggregationDiagnostics(
            new ContentPlanProductionExecutionRequest(Guid.NewGuid(), false, StartPhaseNo: start, EndPhaseNo: end), phases, []);

    private static ProductionPhaseResult Phase(int no, ProductionPhaseStatus status, string? code = null) =>
        new(no, $"Phase {no}", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, [], [], null, [], [], false)
        { ReasonCode = code };
}
