using System.Collections;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeRevisionConvergenceStateTests
{
    [Fact] public void Constructor_enforces_identity_lineage_status_action_and_count()
    {
        var s=OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        DocumentaryNarrativeRevisionConvergenceState Make(string? id=null, DocumentaryNarrativeRevisionConvergenceStatus? status=null,
            DocumentaryNarrativeRevisionConvergenceNextAction? action=null,int count=0) => new(id??s.ConvergenceId,s.OriginalDraft,s.InitialValidationResult,s.CurrentDraft,s.CurrentValidationResult,s.Cycles,s.Policy,s.Metadata,status??s.Status,action??s.NextAction,count);
        Assert.Throws<ArgumentException>(()=>Make("wrong"));
        Assert.Throws<ArgumentException>(()=>Make(status:DocumentaryNarrativeRevisionConvergenceStatus.InProgress));
        Assert.Throws<ArgumentException>(()=>Make(action:DocumentaryNarrativeRevisionConvergenceNextAction.None));
        Assert.Throws<ArgumentException>(()=>Make(action:DocumentaryNarrativeRevisionConvergenceNextAction.ObtainExternalRevisionSubmission));
        Assert.Throws<ArgumentException>(()=>Make(count:1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Make(count:-1));
    }
    [Fact] public void Constructor_rejects_nulls_undefined_enums_and_validation_mismatch()
    {
        var s=OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionConvergenceState(s.ConvergenceId,null!,s.InitialValidationResult,s.CurrentDraft,s.CurrentValidationResult,s.Cycles,s.Policy,s.Metadata,s.Status,s.NextAction,0));
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionConvergenceState(s.ConvergenceId,s.OriginalDraft,s.InitialValidationResult,s.CurrentDraft,s.CurrentValidationResult,null!,s.Policy,s.Metadata,s.Status,s.NextAction,0));
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionConvergenceState(s.ConvergenceId,s.OriginalDraft,new("wrong",[]),s.CurrentDraft,s.CurrentValidationResult,s.Cycles,s.Policy,s.Metadata,s.Status,s.NextAction,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentaryNarrativeRevisionConvergenceState(s.ConvergenceId,s.OriginalDraft,s.InitialValidationResult,s.CurrentDraft,s.CurrentValidationResult,s.Cycles,s.Policy,s.Metadata,(DocumentaryNarrativeRevisionConvergenceStatus)99,s.NextAction,0));
    }
    [Fact] public void Cycles_are_defensively_copied_and_derived_values_are_exact()
    {
        var s=OrionDocumentaryNarrativeRevisionConvergenceFixture.OneCycleSuccessfulState(); var list=s.Cycles.ToList();
        var copy=new DocumentaryNarrativeRevisionConvergenceState(s.ConvergenceId,s.OriginalDraft,s.InitialValidationResult,s.CurrentDraft,s.CurrentValidationResult,list,s.Policy,s.Metadata,s.Status,s.NextAction,s.ConsecutiveNoProgressCycleCount);
        list.Clear(); Assert.Single(copy.Cycles); Assert.Throws<NotSupportedException>(()=>((IList)copy.Cycles).Clear());
        Assert.Equal(copy.Cycles.Sum(x=>x.AppliedChangeCount),copy.TotalAppliedChangeCount); Assert.Equal(copy.Cycles.Sum(x=>x.ValidationComparison.ResolvedFindingCount),copy.TotalResolvedFindingCount);
        Assert.Equal(copy.Cycles.Sum(x=>x.ValidationComparison.IntroducedFindingCount),copy.TotalIntroducedFindingCount); Assert.True(copy.IsClean); Assert.False(copy.RequiresAnotherCycle);
    }
}
