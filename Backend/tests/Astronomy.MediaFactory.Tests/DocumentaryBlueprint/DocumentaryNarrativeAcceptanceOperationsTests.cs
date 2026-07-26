using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeAcceptanceOperationsTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Evaluator_accepts_clean_convergence_deterministically(bool oneCycle)
    {
        var request=oneCycle?OrionDocumentaryNarrativeAcceptanceFixture.AcceptedOneCycleRequest():OrionDocumentaryNarrativeAcceptanceFixture.AcceptedInitiallyCleanRequest();
        var before=OrionDocumentaryNarrativeAcceptanceFixture.Json(request); var evaluator=new DocumentaryNarrativeAcceptanceEvaluator();
        var first=evaluator.Evaluate(request); var second=evaluator.Evaluate(request);
        Assert.Equal(DocumentaryNarrativeAcceptanceStatus.Accepted,first.Status); Assert.Equal(DocumentaryNarrativeAcceptanceReason.ConvergedAndClean,first.PrimaryReason); Assert.Empty(first.SupportingReasons);
        Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(first),OrionDocumentaryNarrativeAcceptanceFixture.Json(second)); Assert.Equal(before,OrionDocumentaryNarrativeAcceptanceFixture.Json(request));
    }

    [Fact] public void Evaluator_rejects_null_and_correlation_mismatch()
    {
        var evaluator=new DocumentaryNarrativeAcceptanceEvaluator(); Assert.Throws<ArgumentNullException>(()=>evaluator.Evaluate(null!));
        var request=OrionDocumentaryNarrativeAcceptanceFixture.AcceptedInitiallyCleanRequest();
        var metadata=new DocumentaryNarrativeAcceptanceMetadata(request.Metadata.EvaluatedUtc,request.Metadata.EvaluatedBy,"1.0",request.Metadata.CorrelationId.ToUpperInvariant());
        Assert.Throws<ArgumentException>(()=>evaluator.Evaluate(new(request.ConvergenceState,request.Policy,metadata)));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Builder_coordinator_and_summarizer_create_exact_immutable_outputs(bool oneCycle)
    {
        var request=oneCycle?OrionDocumentaryNarrativeAcceptanceFixture.AcceptedOneCycleRequest():OrionDocumentaryNarrativeAcceptanceFixture.AcceptedInitiallyCleanRequest();
        var decision=new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request); var candidate=new DocumentaryNarrativeReleaseCandidateBuilder().Build(request.ConvergenceState,decision,OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata());
        Assert.Equal($"{candidate.DraftId}.narrative-release-candidate.{candidate.DraftVersion}",candidate.ReleaseCandidateId); Assert.Same(request.ConvergenceState.CurrentDraft,candidate.NarrativeDraft); Assert.Same(request.ConvergenceState.CurrentValidationResult,candidate.FinalValidationResult);
        var coordinated=new DocumentaryNarrativeAcceptanceCoordinator().Accept(request,OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata()); Assert.True(coordinated.HasReleaseCandidate); Assert.Equal(OrionDocumentaryNarrativeAcceptanceFixture.Json(candidate),OrionDocumentaryNarrativeAcceptanceFixture.Json(coordinated.ReleaseCandidate));
        var summary=new DocumentaryNarrativeReleaseCandidateSummarizer().Summarize(candidate); Assert.True(summary.IsClean); Assert.True(summary.IsFullyResolved); Assert.Equal(summary.CompletedCycleCount+1,summary.FindingCountHistory.Count); Assert.Equal(0,summary.FindingCountHistory[^1]);
    }

    [Fact] public void Summary_rejects_invalid_histories_flags_and_values()
    {
        var c=OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate();
        DocumentaryNarrativeReleaseCandidateSummary Make(int final=0,IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus>? statuses=null,IReadOnlyList<int>? history=null,bool clean=true,bool resolved=true) => new(c.ReleaseCandidateId,c.DraftId,c.DraftVersion,c.OriginalDraftId,c.OriginalDraftVersion,c.ConvergenceId,0,final,0,0,0,statuses??[],history??[final],c.AcceptanceDecision.Metadata.EvaluatedUtc,c.AcceptanceDecision.Metadata.EvaluatedBy,clean,resolved);
        Assert.Throws<ArgumentException>(()=>Make(final:1)); Assert.Throws<ArgumentException>(()=>Make(clean:false)); Assert.Throws<ArgumentException>(()=>Make(resolved:false)); Assert.Throws<ArgumentException>(()=>Make(history:[-1])); Assert.Throws<ArgumentException>(()=>Make(statuses:[(DocumentaryNarrativeRevisionCycleStatus)99]));
    }

    [Fact] public void Public_operations_have_the_exact_stateless_synchronous_boundary()
    {
        var expected=new Dictionary<Type,string>{{typeof(DocumentaryNarrativeAcceptanceEvaluator),"Evaluate"},{typeof(DocumentaryNarrativeReleaseCandidateBuilder),"Build"},{typeof(DocumentaryNarrativeAcceptanceCoordinator),"Accept"},{typeof(DocumentaryNarrativeReleaseCandidateSummarizer),"Summarize"}};
        foreach(var pair in expected) { Assert.True(pair.Key.IsSealed); Assert.Empty(pair.Key.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)); Assert.Single(pair.Key.GetConstructors()); var method=Assert.Single(pair.Key.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly)); Assert.Equal(pair.Value,method.Name); Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType)); }
        Assert.Equal(["Accepted","HeldForManualApproval","Rejected"],Enum.GetNames<DocumentaryNarrativeAcceptanceStatus>());
        Assert.Equal(["ConvergedAndClean","RequiresManualApproval","CycleLimitReached","NoProgressReached","RegressionDetected","ManualReviewRequired","ValidationFindingsRemain","UnresolvedRevisionItemsRemain","NonTerminalConvergenceState","PolicyRejected"],Enum.GetNames<DocumentaryNarrativeAcceptanceReason>());
    }
}
