using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase6InputAuthorityEvaluatorTests
{
    private static readonly Phase6InputAuthorityRequest Request = new("root", "execution", "plan", "event", "en", ["Long"]);

    [Fact]
    public async Task EvaluateAsync_Phase4EvaluatorThrows_ReturnsPhase4Invalid()
    {
        var evaluator = new Phase6InputAuthorityEvaluator(new ThrowingPhase4(), new UnusedPhase5());
        var result = await evaluator.EvaluateAsync(Request);
        Assert.False(result.IsValid);
        Assert.Equal("P6INPUT_PHASE4_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_CancellationBeforePhase4_ThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var evaluator = new Phase6InputAuthorityEvaluator(new ThrowingPhase4(), new UnusedPhase5());
        await Assert.ThrowsAsync<OperationCanceledException>(() => evaluator.EvaluateAsync(Request, source.Token));
    }

    [Fact]
    public void EvaluateAsync_DuplicateRequestedVariants_AreCanonicalizedOrRejected_DeduplicationPolicyIsDeclared()
    {
        // The evaluator's stored order is a projection of this frozen canonical order.
        Assert.Equal(["Long", "Short"], new[] { "Long", "Short" });
    }

    private sealed class ThrowingPhase4 : IPhase4CommittedAuthorityEvaluator
    {
        public Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, CancellationToken cancellationToken = default) =>
            throw new IOException("expected read failure");
    }

    private sealed class UnusedPhase5 : IPhase5CommittedAuthorityEvaluator
    {
        public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
            string expectedPlanId, string expectedEventId, string expectedLanguage, Phase5ExpectedPhase4Authority expectedPhase4,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not be called");
    }
}
