using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeAcceptanceFixture
{
    internal const string Correlation = OrionDocumentaryNarrativeRevisionCycleFixture.Correlation;
    internal static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
    internal static DocumentaryNarrativeAcceptancePolicy StrictPolicy() => new(true, true, true, true, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy ManualHoldPolicy() => new(true, true, true, false, true, true, true, true, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy RejectTerminalStopsPolicy() => new(true, true, true, true, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy CycleLimitHoldPolicy() => new(true, true, true, false, true, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy CycleLimitRejectPolicy() => new(true, true, true, false, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy NoProgressHoldPolicy() => new(true, true, true, false, false, true, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy NoProgressRejectPolicy() => new(true, true, true, false, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy RegressionRejectPolicy() => new(true, true, true, true, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy RegressionHoldPolicy() => new(true, true, true, false, false, false, true, false, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy ManualEscalationHoldPolicy() => new(true, true, true, false, false, false, false, true, "1.0");
    internal static DocumentaryNarrativeAcceptancePolicy ManualEscalationRejectPolicy() => new(true, true, true, false, false, false, false, false, "1.0");
    internal static DocumentaryNarrativeAcceptanceMetadata AcceptanceMetadata() => new(DateTimeOffset.Parse("2026-02-03T04:05:06.1234567+05:30"), " acceptance editor ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeReleaseCandidateMetadata ReleaseMetadata() => new(DateTimeOffset.Parse("2026-02-04T05:06:07.7654321-04:00"), " release editor ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
    internal static DocumentaryNarrativeRevisionConvergenceState InitiallyCleanConvergenceState() => OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyCleanState();
    internal static DocumentaryNarrativeRevisionConvergenceState SuccessfulOneCycleConvergenceState() => OrionDocumentaryNarrativeRevisionConvergenceFixture.OneCycleSuccessfulState();
    internal static DocumentaryNarrativeRevisionConvergenceState SuccessfulMultiCycleConvergenceState()
    {
        var firstCycle = OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult();
        var state = new DocumentaryNarrativeRevisionConvergenceStarter().Start(
            firstCycle.Plan.SourceDraft,
            firstCycle.Plan.SourceValidationResult,
            new DocumentaryNarrativeRevisionConvergencePolicy(2, false, 2, true, true, "1.0"),
            OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata());
        var advancer = new DocumentaryNarrativeRevisionConvergenceAdvancer();
        state = advancer.Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(state, firstCycle));

        var secondCycle = NextCycle(state, "multi-cycle.final", true);
        return advancer.Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(state, secondCycle));
    }
    private static DocumentaryNarrativeRevisionCycleResult NextCycle(
        DocumentaryNarrativeRevisionConvergenceState state,
        string scenario,
        bool submitRevisions)
    {
        var source = state.CurrentDraft;
        var plan = new DocumentaryNarrativeRevisionCyclePlanner().Plan(
            source,
            state.CurrentValidationResult,
            $"request.orion.{scenario}",
            OrionDocumentaryNarrativeRevisionCycleFixture.RequestMetadata(source),
            OrionDocumentaryNarrativeRevisionCycleFixture.ExecutionMetadata(),
            OrionDocumentaryNarrativeRevisionCycleFixture.CycleMetadata());
        var submissions = submitRevisions
            ? plan.WorkPackage.PassageWorkItems
                .Select(work => new DocumentaryNarrativePassageRevisionSubmission(
                    work.WorkItemId,
                    work.PassageId,
                    work.OriginalText,
                    $"{work.OriginalText} Orion's measured light reveals a detailed stellar history.",
                    work.RevisionItemIds))
                .ToArray()
            : [];
        var submission = new DocumentaryNarrativeRevisionSubmission(
            $"submission.orion.{scenario}", plan.WorkPackage.WorkPackageId, plan.RevisionRequest.RevisionRequestId,
            plan.SourceDraftId, plan.SourceDraftVersion, OrionDocumentaryNarrativeRevisionCycleFixture.SubmissionMetadata(), submissions);
        var revisionMetadata = new DocumentaryNarrativeRevisionMetadata(
            OrionDocumentaryNarrativeRevisionCycleFixture.Created.AddMinutes(4), " revision reviewer ",
            plan.SourceDraftId, plan.SourceDraftVersion, "3", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation);
        return new DocumentaryNarrativeRevisionCycleCompleter().Complete(new(
            plan, submission, revisionMetadata, OrionDocumentaryNarrativeRevisionCycleFixture.Completed,
            " completion reviewer ", "1.0", OrionDocumentaryNarrativeRevisionCycleFixture.Correlation));
    }
    private static DocumentaryNarrativeRevisionConvergenceState Advance(DocumentaryNarrativeRevisionCycleResult cycle, DocumentaryNarrativeRevisionConvergencePolicy policy) =>
        new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(
            new DocumentaryNarrativeRevisionConvergenceStarter().Start(cycle.Plan.SourceDraft, cycle.Plan.SourceValidationResult, policy, OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata()), cycle));
    internal static DocumentaryNarrativeRevisionConvergenceState NotStartedState() => OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
    internal static DocumentaryNarrativeRevisionConvergenceState InProgressState() => Advance(OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult(), OrionDocumentaryNarrativeRevisionConvergenceFixture.NoProgressThresholdTwoPolicy());
    internal static DocumentaryNarrativeRevisionConvergenceState CycleLimitState() => Advance(OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult(), new(1, false, 2, true, true, "1.0"));
    internal static DocumentaryNarrativeRevisionConvergenceState NoProgressState()
    {
        var firstCycle = OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult();
        var starter = new DocumentaryNarrativeRevisionConvergenceStarter().Start(
            firstCycle.Plan.SourceDraft, firstCycle.Plan.SourceValidationResult,
            OrionDocumentaryNarrativeRevisionConvergenceFixture.NoProgressThresholdOnePolicy(),
            OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata());
        var advancer = new DocumentaryNarrativeRevisionConvergenceAdvancer();
        var inProgress = advancer.Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(starter, firstCycle));
        return advancer.Advance(OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(
            inProgress, NextCycle(inProgress, "no-progress", false)));
    }
    internal static DocumentaryNarrativeRevisionConvergenceState RegressionState() => Advance(OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult(), OrionDocumentaryNarrativeRevisionConvergenceFixture.RegressionStoppingPolicy());
    internal static DocumentaryNarrativeRevisionConvergenceState ManualEscalationState() => Advance(OrionDocumentaryNarrativeRevisionCycleFixture.ManualOnlyResult(), OrionDocumentaryNarrativeRevisionConvergenceFixture.DefaultPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest Request(DocumentaryNarrativeRevisionConvergenceState state, DocumentaryNarrativeAcceptancePolicy policy) => new(state, policy, AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceRequest NotStartedRequest() => Request(NotStartedState(), StrictPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest InProgressRequest() => Request(InProgressState(), StrictPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest CycleLimitHoldRequest() => Request(CycleLimitState(), CycleLimitHoldPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest CycleLimitRejectRequest() => Request(CycleLimitState(), CycleLimitRejectPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest NoProgressHoldRequest() => Request(NoProgressState(), NoProgressHoldPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest NoProgressRejectRequest() => Request(NoProgressState(), NoProgressRejectPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest RegressionRejectRequest() => Request(RegressionState(), RegressionRejectPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest RegressionHoldRequest() => Request(RegressionState(), RegressionHoldPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest ManualEscalationHoldRequest() => Request(ManualEscalationState(), ManualEscalationHoldPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest ManualEscalationRejectRequest() => Request(ManualEscalationState(), ManualEscalationRejectPolicy());
    internal static DocumentaryNarrativeAcceptanceRequest AcceptedInitiallyCleanRequest() => new(InitiallyCleanConvergenceState(), StrictPolicy(), AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceRequest AcceptedOneCycleRequest() => new(SuccessfulOneCycleConvergenceState(), StrictPolicy(), AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceRequest AcceptedMultiCycleRequest() => new(SuccessfulMultiCycleConvergenceState(), StrictPolicy(), AcceptanceMetadata());
    internal static DocumentaryNarrativeAcceptanceDecision AcceptedDecision(DocumentaryNarrativeRevisionConvergenceState? state = null)
    {
        state ??= InitiallyCleanConvergenceState();
        return new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(new(state, StrictPolicy(), AcceptanceMetadata()));
    }
    internal static DocumentaryNarrativeReleaseCandidate InitiallyCleanReleaseCandidate()
    {
        var state = InitiallyCleanConvergenceState();
        return new DocumentaryNarrativeReleaseCandidateBuilder().Build(state, AcceptedDecision(state), ReleaseMetadata());
    }
    internal static DocumentaryNarrativeReleaseCandidate OneCycleReleaseCandidate()
    {
        var state = SuccessfulOneCycleConvergenceState();
        return new DocumentaryNarrativeReleaseCandidateBuilder().Build(state, AcceptedDecision(state), ReleaseMetadata());
    }
    internal static DocumentaryNarrativeAcceptanceDecision AcceptedMultiCycleDecision()
    {
        var request = AcceptedMultiCycleRequest();
        return new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request);
    }
    internal static DocumentaryNarrativeReleaseCandidate MultiCycleReleaseCandidate()
    {
        var state = SuccessfulMultiCycleConvergenceState();
        return new DocumentaryNarrativeReleaseCandidateBuilder().Build(state, AcceptedDecision(state), ReleaseMetadata());
    }
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions());
}
