using System.Collections;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeAcceptanceContractTests
{
    [Fact] public void Policy_enforces_schema_strictness_and_regression_exclusivity()
    {
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptancePolicy(false,true,true,false,false,false,false,false,"1.0"));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptancePolicy(true,false,true,false,false,false,false,false,"1.0"));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptancePolicy(true,true,false,false,false,false,false,false,"1.0"));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptancePolicy(true,true,true,true,false,false,true,false,"1.0"));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeAcceptancePolicy(true,true,true,false,false,false,false,false,"2.0"));
    }

    [Fact] public void Metadata_preserves_offsets_precision_and_whitespace_and_rejects_invalid_values()
    {
        var acceptance=OrionDocumentaryNarrativeAcceptanceFixture.AcceptanceMetadata();
        var release=OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata();
        Assert.Equal(TimeSpan.FromHours(5.5), acceptance.EvaluatedUtc.Offset); Assert.Equal(" acceptance editor ", acceptance.EvaluatedBy);
        Assert.Equal(TimeSpan.FromHours(-4), release.CreatedUtc.Offset); Assert.Equal(" release editor ", release.CreatedBy);
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeAcceptanceMetadata(default,"actor","1.0","c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeReleaseCandidateMetadata(default,"actor","1.0","c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeAcceptanceMetadata(DateTimeOffset.Parse("2026-01-01")," ","1.0","c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeReleaseCandidateMetadata(DateTimeOffset.Parse("2026-01-01"),"actor","1.0"," "));
    }

    [Theory]
    [InlineData(DocumentaryNarrativeAcceptanceStatus.Accepted, DocumentaryNarrativeAcceptanceReason.PolicyRejected)]
    [InlineData(DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.ConvergedAndClean)]
    [InlineData(DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval, DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState)]
    [InlineData(DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.CycleLimitReached)]
    [InlineData(DocumentaryNarrativeAcceptanceStatus.Rejected, DocumentaryNarrativeAcceptanceReason.ManualReviewRequired)]
    public void Decision_rejects_contradictory_primary_reasons(DocumentaryNarrativeAcceptanceStatus status, DocumentaryNarrativeAcceptanceReason reason)
    {
        var s=OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanConvergenceState();
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeAcceptanceDecision(s.ConvergenceId,status,reason,[],s.CurrentDraftId,s.CurrentDraftVersion,0,0,0,OrionDocumentaryNarrativeAcceptanceFixture.StrictPolicy(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptanceMetadata()));
    }

    [Fact] public void Accepted_decision_requires_clean_evidence_and_no_supporting_reasons()
    {
        var s=OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanConvergenceState();
        DocumentaryNarrativeAcceptanceDecision Make(int findings=0,int unresolved=0,IReadOnlyList<DocumentaryNarrativeAcceptanceReason>? support=null) => new(s.ConvergenceId,DocumentaryNarrativeAcceptanceStatus.Accepted,DocumentaryNarrativeAcceptanceReason.ConvergedAndClean,support??[],s.CurrentDraftId,s.CurrentDraftVersion,findings,0,unresolved,OrionDocumentaryNarrativeAcceptanceFixture.StrictPolicy(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptanceMetadata());
        Assert.Throws<ArgumentException>(()=>Make(findings:1)); Assert.Throws<ArgumentException>(()=>Make(unresolved:1));
        Assert.Throws<ArgumentException>(()=>Make(support:[DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain]));
        var decision=Make(); Assert.True(decision.IsEligibleForReleaseCandidate); Assert.False(decision.IsRejected); Assert.False(decision.RequiresManualApproval);
    }

    [Fact] public void Supporting_reasons_are_unique_defined_primary_excluding_and_defensively_copied()
    {
        var s=OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanConvergenceState(); var reasons=new List<DocumentaryNarrativeAcceptanceReason>{DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain};
        var d=new DocumentaryNarrativeAcceptanceDecision(s.ConvergenceId,DocumentaryNarrativeAcceptanceStatus.Rejected,DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState,reasons,s.CurrentDraftId,s.CurrentDraftVersion,1,0,0,OrionDocumentaryNarrativeAcceptanceFixture.StrictPolicy(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptanceMetadata());
        reasons.Clear(); Assert.Single(d.SupportingReasons); Assert.Throws<NotSupportedException>(()=>((IList)d.SupportingReasons).Clear());
    }

    [Fact] public void Core_contracts_round_trip_deterministically()
    {
        var values=new object[]{OrionDocumentaryNarrativeAcceptanceFixture.StrictPolicy(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptanceMetadata(),OrionDocumentaryNarrativeAcceptanceFixture.ReleaseMetadata(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptedInitiallyCleanRequest(),OrionDocumentaryNarrativeAcceptanceFixture.AcceptedDecision(),OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate()};
        foreach(var value in values) { var json=OrionDocumentaryNarrativeAcceptanceFixture.Json(value); var copy=JsonSerializer.Deserialize(json,value.GetType(),OrionDocumentaryNarrativeAcceptanceFixture.JsonOptions()); Assert.Equal(json,JsonSerializer.Serialize(copy,value.GetType(),OrionDocumentaryNarrativeAcceptanceFixture.JsonOptions())); }
    }
}
