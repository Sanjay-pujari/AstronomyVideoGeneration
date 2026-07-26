using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeAcceptanceFinalScenarioTests
{
    public static IEnumerable<object[]> Outcomes()
    {
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.NotStartedRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.InProgressRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.CycleLimitHoldRequest(), DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.CycleLimitReached];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.CycleLimitRejectRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.PolicyRejected];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.RegressionRejectRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.RegressionDetected];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.RegressionHoldRequest(), DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.RegressionDetected];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.ManualEscalationHoldRequest(), DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.ManualReviewRequired];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.ManualEscalationRejectRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.PolicyRejected];
    }

    [Theory, MemberData(nameof(Outcomes))]
    public void Evaluator_and_coordinator_certify_every_nonaccepted_outcome(DocumentaryNarrativeAcceptanceRequest request,
        DocumentaryNarrativeAcceptanceStatus status, DocumentaryNarrativeAcceptanceReason primary)
    {
        var before = OrionDocumentaryNarrativeAcceptanceFixture.Json(request);
        var decision = new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request);
        Assert.Equal(status, decision.Status); Assert.Equal(primary, decision.PrimaryReason);
        Assert.Equal(status == DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, decision.RequiresManualApproval);
        Assert.Equal(status == DocumentaryNarrativeAcceptanceStatus.Rejected, decision.IsRejected);
        Assert.False(decision.IsEligibleForReleaseCandidate);
        Assert.Equal(decision.SupportingReasons.Distinct().Count(), decision.SupportingReasons.Count);
        Assert.DoesNotContain(primary, decision.SupportingReasons);
        var approved = new[] { DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain, DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain,
            DocumentaryNarrativeAcceptanceReason.CycleLimitReached, DocumentaryNarrativeAcceptanceReason.NoProgressReached,
            DocumentaryNarrativeAcceptanceReason.RegressionDetected, DocumentaryNarrativeAcceptanceReason.ManualReviewRequired,
            DocumentaryNarrativeAcceptanceReason.PolicyRejected };
        Assert.Equal(decision.SupportingReasons.OrderBy(approved.ToList().IndexOf), decision.SupportingReasons);
        var result = new DocumentaryNarrativeAcceptanceCoordinator().Accept(request, OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata());
        Assert.Same(result.Decision, result.Decision); Assert.Null(result.ReleaseCandidate); Assert.False(result.HasReleaseCandidate);
        Assert.Equal(before, OrionDocumentaryNarrativeAcceptanceFixture.Json(request));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(decision), OrionDocumentaryNarrativeAcceptanceFixture.Json(new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request)));
    }

    [Fact]
    public void Acceptance_result_certifies_valid_and_invalid_status_candidate_combinations()
    {
        var candidate = OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate();
        var accepted = candidate.AcceptanceDecision;
        var held = new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(OrionDocumentaryNarrativeAcceptanceFixture.CycleLimitHoldRequest());
        var rejected = new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(OrionDocumentaryNarrativeAcceptanceFixture.NotStartedRequest());
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeAcceptanceResult(null!, null));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptanceResult(accepted, null));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptanceResult(held, candidate));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptanceResult(rejected, candidate));
        foreach (var result in new[] { new DocumentaryNarrativeAcceptanceResult(accepted, candidate), new(held, null), new(rejected, null) })
        {
            Assert.Equal(result.ReleaseCandidate is not null, result.HasReleaseCandidate);
            var json = OrionDocumentaryNarrativeAcceptanceFixture.Json(result);
            var copy = JsonSerializer.Deserialize<DocumentaryNarrativeAcceptanceResult>(json, OrionDocumentaryNarrativeAcceptanceFixture.JsonOptions());
            Assert.Equal(json, OrionDocumentaryNarrativeAcceptanceFixture.Json(copy));
        }
    }
}
