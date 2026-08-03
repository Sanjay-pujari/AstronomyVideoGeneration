using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeValidationCanonicalizationTests
{
    [Fact]
    public void ValidationCanonicalization_SortsGates()
    {
        var result=Phase7KnowledgeValidationCanonicalizer.Canonicalize(Validation(
            [Gate("z"),Gate("a")]));
        Assert.Equal(["a","z"],result.Gates.Select(g=>g.Name));
    }

    [Fact]
    public void ValidationCanonicalization_SortsErrorsAndWarnings()
    {
        var result=Phase7KnowledgeValidationCanonicalizer.Canonicalize(Validation(
            [new("gate",false,["z","a"],["y","b"])], ["z","a"], ["y","b"]));
        Assert.Equal(["a","z"],result.Errors);
        Assert.Equal(["b","y"],result.Warnings);
        Assert.Equal(["a","z"],result.Gates[0].Errors);
        Assert.Equal(["b","y"],result.Gates[0].Warnings);
    }

    [Fact]
    public void EquivalentValidationDifferentOrderingMatches()
    {
        var left=Validation([Gate("z"),Gate("a")],["z","a"]);
        var right=Validation([Gate("a"),Gate("z")],["a","z"]);
        Assert.True(Phase7KnowledgeValidationCanonicalizer.Equivalent(left,right));
        Assert.Equal(Phase7KnowledgeValidationCanonicalizer.ComputeChecksum(left),
            Phase7KnowledgeValidationCanonicalizer.ComputeChecksum(right));
    }

    [Fact]
    public void ChangedGateStateDoesNotMatch()
    {
        Assert.False(Phase7KnowledgeValidationCanonicalizer.Equivalent(
            Validation([Gate("gate")]), Validation([Gate("gate") with { Passed=false }])));
    }

    private static Phase7KnowledgeValidationGate Gate(string name)=>new(name,true,[],[]);

    private static Phase7KnowledgeValidation Validation(IReadOnlyList<Phase7KnowledgeValidationGate> gates,
        IReadOnlyList<string>? errors=null,IReadOnlyList<string>? warnings=null)=>
        new(Phase7KnowledgeContract.Version,"execution","plan","event","authority",true,"P7KNOWLEDGE_VALID",
            Phase7KnowledgeValidationMode.CommittedPhysical,gates,errors??[],warnings??[],null,"");
}
