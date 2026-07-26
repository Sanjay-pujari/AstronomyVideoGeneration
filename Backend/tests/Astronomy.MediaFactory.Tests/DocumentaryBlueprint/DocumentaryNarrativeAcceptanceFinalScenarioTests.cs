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
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.NoProgressHoldRequest(), DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.NoProgressReached];
        yield return [OrionDocumentaryNarrativeAcceptanceFixture.NoProgressRejectRequest(), DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.PolicyRejected];
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
        Assert.All(decision.SupportingReasons, reason => Assert.True(Enum.IsDefined(reason)));
        var approvedOrder = new[] { DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain, DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain,
            DocumentaryNarrativeAcceptanceReason.CycleLimitReached, DocumentaryNarrativeAcceptanceReason.NoProgressReached,
            DocumentaryNarrativeAcceptanceReason.RegressionDetected, DocumentaryNarrativeAcceptanceReason.ManualReviewRequired,
            DocumentaryNarrativeAcceptanceReason.PolicyRejected };
        var expectedSupportingReasons = decision.SupportingReasons
            .OrderBy(reason => Array.IndexOf(approvedOrder, reason))
            .ToArray();
        Assert.Equal(expectedSupportingReasons, decision.SupportingReasons);
        var result = new DocumentaryNarrativeAcceptanceCoordinator().Accept(request, OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata());
        var independentlyEvaluated = new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request);
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(independentlyEvaluated), OrionDocumentaryNarrativeAcceptanceFixture.Json(result.Decision));
        Assert.Equal(independentlyEvaluated.Status, result.Decision.Status);
        Assert.Equal(independentlyEvaluated.PrimaryReason, result.Decision.PrimaryReason);
        Assert.Equal(independentlyEvaluated.SupportingReasons.ToArray(), result.Decision.SupportingReasons);
        Assert.Null(result.ReleaseCandidate); Assert.False(result.HasReleaseCandidate);
        Assert.Equal(before, OrionDocumentaryNarrativeAcceptanceFixture.Json(request));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(decision), OrionDocumentaryNarrativeAcceptanceFixture.Json(new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request)));

        if (request.ConvergenceState.Status == DocumentaryNarrativeRevisionConvergenceStatus.StoppedByNoProgress)
        {
            Assert.Equal(status == DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, decision.RequiresManualApproval);
            Assert.Equal(status == DocumentaryNarrativeAcceptanceStatus.Rejected, decision.IsRejected);
            var expected = status == DocumentaryNarrativeAcceptanceStatus.Rejected
                ? new[] { DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain, DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain, DocumentaryNarrativeAcceptanceReason.NoProgressReached }
                : new[] { DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain, DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain };
            Assert.Equal(expected, decision.SupportingReasons);
            Assert.DoesNotContain(DocumentaryNarrativeAcceptanceReason.PolicyRejected, decision.SupportingReasons);
        }
    }

    [Fact]
    public void Multi_cycle_evaluator_builder_coordinator_and_summary_are_exact_nonmutating_and_deterministic()
    {
        var request = OrionDocumentaryNarrativeAcceptanceFixture.AcceptedMultiCycleRequest();
        var requestBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request);
        var stateBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState);
        var cyclesBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.Cycles);
        var policyBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.Policy);
        var metadataBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.Metadata);
        var draftBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.CurrentDraft);
        var validationBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.CurrentValidationResult);

        var evaluator = new DocumentaryNarrativeAcceptanceEvaluator();
        var decision = evaluator.Evaluate(request);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, request.ConvergenceState.Status);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft, request.ConvergenceState.NextAction);
        Assert.Equal(DocumentaryNarrativeAcceptanceStatus.Accepted, decision.Status);
        Assert.Equal(DocumentaryNarrativeAcceptanceReason.ConvergedAndClean, decision.PrimaryReason);
        Assert.Empty(decision.SupportingReasons);
        Assert.True(decision.IsEligibleForReleaseCandidate); Assert.False(decision.RequiresManualApproval); Assert.False(decision.IsRejected);
        Assert.True(decision.CompletedCycleCount >= 2); Assert.Equal(0, decision.CurrentFindingCount); Assert.Equal(0, decision.UnresolvedRevisionItemCount);
        Assert.Equal(request.ConvergenceState.ConvergenceId, decision.ConvergenceId);
        Assert.Equal(request.ConvergenceState.CurrentDraftId, decision.CurrentDraftId);
        Assert.Equal(request.ConvergenceState.CurrentDraftVersion, decision.CurrentDraftVersion);
        Assert.Equal(request.ConvergenceState.CompletedCycleCount, decision.CompletedCycleCount);
        Assert.Equal(request.Metadata.CorrelationId, decision.Metadata.CorrelationId);
        Assert.Equal(request.ConvergenceState.CompletedCycleCount, request.ConvergenceState.Cycles.Select(cycle => cycle.CycleId).Distinct().Count());

        var releaseMetadata = OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata();
        var releaseMetadataBefore = OrionDocumentaryNarrativeAcceptanceFixture.Json(releaseMetadata);
        var builder = new DocumentaryNarrativeReleaseCandidateBuilder();
        var candidate = builder.Build(request.ConvergenceState, decision, releaseMetadata);
        Assert.Equal($"{candidate.DraftId}.narrative-release-candidate.{candidate.DraftVersion}", candidate.ReleaseCandidateId);
        Assert.Equal(request.ConvergenceState.OriginalDraftId, candidate.OriginalDraftId); Assert.Equal(request.ConvergenceState.OriginalDraftVersion, candidate.OriginalDraftVersion);
        Assert.Equal(request.ConvergenceState.CurrentDraftId, candidate.DraftId); Assert.Equal(request.ConvergenceState.CurrentDraftVersion, candidate.DraftVersion);
        Assert.Equal(request.ConvergenceState.ConvergenceId, candidate.ConvergenceId); Assert.Equal(request.ConvergenceState.CompletedCycleCount, candidate.CompletedCycleCount);
        Assert.True(candidate.CompletedCycleCount >= 2); Assert.Equal(0, candidate.FinalFindingCount);
        Assert.True(candidate.IsClean); Assert.True(candidate.IsFullyResolved); Assert.True(candidate.IsAccepted);
        Assert.Same(request.ConvergenceState, candidate.ConvergenceState); Assert.Same(request.ConvergenceState.CurrentDraft, candidate.NarrativeDraft);
        Assert.Same(request.ConvergenceState.CurrentValidationResult, candidate.FinalValidationResult); Assert.Same(decision, candidate.AcceptanceDecision); Assert.Same(releaseMetadata, candidate.Metadata);

        var coordinator = new DocumentaryNarrativeAcceptanceCoordinator();
        var result = coordinator.Accept(request, releaseMetadata);
        var expectedDecision = evaluator.Evaluate(request);
        var expectedCandidate = builder.Build(request.ConvergenceState, expectedDecision, releaseMetadata);
        var expectedResult = new DocumentaryNarrativeAcceptanceResult(expectedDecision, expectedCandidate);
        Assert.Equal(DocumentaryNarrativeAcceptanceStatus.Accepted, result.Decision.Status); Assert.Equal(DocumentaryNarrativeAcceptanceReason.ConvergedAndClean, result.Decision.PrimaryReason);
        Assert.NotNull(result.ReleaseCandidate); Assert.True(result.HasReleaseCandidate);
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(expectedResult), OrionDocumentaryNarrativeAcceptanceFixture.Json(result));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(result), OrionDocumentaryNarrativeAcceptanceFixture.Json(coordinator.Accept(request, releaseMetadata)));

        var summarizer = new DocumentaryNarrativeReleaseCandidateSummarizer();
        var summary = summarizer.Summarize(candidate);
        Assert.Equal(candidate.ReleaseCandidateId, summary.ReleaseCandidateId); Assert.Equal(candidate.DraftId, summary.DraftId); Assert.Equal(candidate.DraftVersion, summary.DraftVersion);
        Assert.Equal(candidate.OriginalDraftId, summary.OriginalDraftId); Assert.Equal(candidate.OriginalDraftVersion, summary.OriginalDraftVersion); Assert.Equal(candidate.ConvergenceId, summary.ConvergenceId);
        Assert.Equal(candidate.CompletedCycleCount, summary.CompletedCycleCount); Assert.Equal(candidate.FinalFindingCount, summary.FinalFindingCount);
        Assert.Equal(candidate.ConvergenceState.TotalAppliedChangeCount, summary.TotalAppliedChangeCount); Assert.Equal(candidate.ConvergenceState.TotalResolvedFindingCount, summary.TotalResolvedFindingCount); Assert.Equal(candidate.ConvergenceState.TotalIntroducedFindingCount, summary.TotalIntroducedFindingCount);
        Assert.Equal(candidate.AcceptanceDecision.Metadata.EvaluatedUtc, summary.AcceptedUtc); Assert.Equal(candidate.AcceptanceDecision.Metadata.EvaluatedBy, summary.AcceptedBy);
        Assert.True(summary.CompletedCycleCount >= 2); Assert.Equal(0, summary.FinalFindingCount); Assert.True(summary.IsClean); Assert.True(summary.IsFullyResolved);
        Assert.Equal(candidate.ConvergenceState.Cycles.Select(cycle => cycle.Status).ToArray(), summary.CycleStatuses);
        var expectedFindingHistory = new[] { candidate.ConvergenceState.InitialFindingCount }.Concat(candidate.ConvergenceState.Cycles.Select(cycle => cycle.RevisedFindingCount)).ToArray();
        Assert.Equal(expectedFindingHistory, summary.FindingCountHistory);
        Assert.Equal(summary.CompletedCycleCount, summary.CycleStatuses.Count); Assert.Equal(summary.CompletedCycleCount + 1, summary.FindingCountHistory.Count); Assert.Equal(0, summary.FindingCountHistory[^1]);
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(summary), OrionDocumentaryNarrativeAcceptanceFixture.Json(summarizer.Summarize(candidate)));

        Assert.Equal(requestBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request)); Assert.Equal(stateBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState));
        Assert.Equal(cyclesBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.Cycles)); Assert.Equal(policyBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.Policy));
        Assert.Equal(metadataBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.Metadata)); Assert.Equal(draftBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.CurrentDraft));
        Assert.Equal(validationBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(request.ConvergenceState.CurrentValidationResult)); Assert.Equal(releaseMetadataBefore, OrionDocumentaryNarrativeAcceptanceFixture.Json(releaseMetadata));
    }

    [Fact]
    public void Multi_cycle_workflow_round_trips_and_independent_reconstruction_is_byte_identical()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        static void RoundTrip<T>(T value, JsonSerializerOptions options)
        {
            var json = JsonSerializer.Serialize(value, options);
            var copy = JsonSerializer.Deserialize<T>(json, options);
            Assert.Equal(json, JsonSerializer.Serialize(copy, options));
        }

        static (DocumentaryNarrativeAcceptanceRequest Request, DocumentaryNarrativeAcceptanceDecision Decision,
            DocumentaryNarrativeReleaseCandidate Candidate, DocumentaryNarrativeAcceptanceResult Result,
            DocumentaryNarrativeReleaseCandidateSummary Summary) Build()
        {
            var request = OrionDocumentaryNarrativeAcceptanceFixture.AcceptedMultiCycleRequest();
            var decision = new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request);
            var candidate = new DocumentaryNarrativeReleaseCandidateBuilder().Build(
                request.ConvergenceState, decision, OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata());
            var result = new DocumentaryNarrativeAcceptanceCoordinator().Accept(
                request, OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata());
            var summary = new DocumentaryNarrativeReleaseCandidateSummarizer().Summarize(candidate);
            return (request, decision, candidate, result, summary);
        }

        var first = Build();
        RoundTrip(first.Request, options); RoundTrip(first.Decision, options); RoundTrip(first.Candidate, options);
        RoundTrip(first.Result, options); RoundTrip(first.Summary, options);
        var second = Build();
        Assert.NotSame(first.Request.ConvergenceState, second.Request.ConvergenceState);
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Request.ConvergenceState), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Request.ConvergenceState));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Request), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Request));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Decision), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Decision));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Candidate), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Candidate));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Result), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Result));
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first.Summary), OrionDocumentaryNarrativeAcceptanceFixture.Json(second.Summary));

        Assert.Equal(first.Request.ConvergenceState.Cycles.Select(cycle => cycle.CycleId), second.Request.ConvergenceState.Cycles.Select(cycle => cycle.CycleId));
        Assert.Equal(first.Request.ConvergenceState.Cycles.Select(cycle => cycle.Status), second.Request.ConvergenceState.Cycles.Select(cycle => cycle.Status));
        Assert.Equal(first.Request.ConvergenceState.OriginalDraftId, second.Request.ConvergenceState.OriginalDraftId);
        Assert.Equal(first.Request.ConvergenceState.CurrentDraftId, second.Request.ConvergenceState.CurrentDraftId);
        Assert.Equal(first.Request.ConvergenceState.CurrentValidationResult.DraftId, second.Request.ConvergenceState.CurrentValidationResult.DraftId);
        Assert.Equal(first.Candidate.ReleaseCandidateId, second.Candidate.ReleaseCandidateId);
        Assert.Equal(first.Decision.Status, second.Decision.Status); Assert.Equal(first.Decision.PrimaryReason, second.Decision.PrimaryReason);
        Assert.Equal(first.Decision.SupportingReasons, second.Decision.SupportingReasons);
        Assert.Equal(first.Decision.CompletedCycleCount, second.Decision.CompletedCycleCount);
        Assert.Equal(first.Decision.Metadata.CorrelationId, second.Decision.Metadata.CorrelationId);
        Assert.Equal(DateTimeOffset.Parse("2026-02-03T04:05:06.1234567+05:30"), first.Decision.Metadata.EvaluatedUtc);
        Assert.Equal(" acceptance editor ", first.Decision.Metadata.EvaluatedBy);
        Assert.Equal(DateTimeOffset.Parse("2026-02-04T05:06:07.7654321-04:00"), first.Candidate.Metadata.CreatedUtc);
        Assert.Equal(" release editor ", first.Candidate.Metadata.CreatedBy);
        Assert.Equal(first.Candidate.ConvergenceState.TotalAppliedChangeCount, first.Summary.TotalAppliedChangeCount);
        Assert.Equal(first.Candidate.ConvergenceState.TotalResolvedFindingCount, first.Summary.TotalResolvedFindingCount);
        Assert.Equal(first.Candidate.ConvergenceState.TotalIntroducedFindingCount, first.Summary.TotalIntroducedFindingCount);
        Assert.Equal(first.Candidate.ConvergenceState.Cycles.Select(cycle => cycle.Status), first.Summary.CycleStatuses);
        Assert.Equal(new[] { first.Candidate.ConvergenceState.InitialFindingCount }
            .Concat(first.Candidate.ConvergenceState.Cycles.Select(cycle => cycle.RevisedFindingCount)), first.Summary.FindingCountHistory);
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
