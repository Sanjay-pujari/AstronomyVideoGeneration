using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeMergeClassifierTests
{
    private static Phase7AdapterClaimCandidate Candidate(string text) => new("object.example", "distance.value",
        NarrationKnowledgeDomainKey.Distance, text, [], false, false, "object.example.distance.value");
    private static Phase7KnowledgeMergeRequest Request(Phase7KnowledgeComparisonMetadata evergreen,
        Phase7KnowledgeComparisonMetadata @event, Phase7KnowledgeAuthorityScope? evergreenScope=null, Phase7KnowledgeAuthorityScope? eventScope=null)
    {
        var a=Candidate(evergreen.NormalizedValue ?? "evergreen"); var b=Candidate(@event.NormalizedValue ?? "event");
        return new(a.SemanticIdentity,a.Domain,a.ApprovedFieldPath,a,b,evergreenScope??new(),eventScope??new(),evergreen,@event,new Dictionary<string,string>());
    }

    [Theory]
    [InlineData("1344", "1500")]
    [InlineData("spiral", "elliptical")]
    public void DifferentValueSameScopeIsContradictory(string evergreen, string @event)
    {
        var result=new Phase7KnowledgeMergeClassifier().Classify(Request(new(evergreen,"fact","unit"),new(@event,"fact","unit")));
        Assert.Equal(Phase7KnowledgeMergeClassification.Contradictory,result.Classification);
    }

    [Fact]
    public void DifferentUnitsDoNotAutomaticallyCreateDifferentScopes()
    {
        var result=new Phase7KnowledgeMergeClassifier().Classify(Request(new("10","decimal","km"),new("10","decimal","m")));
        Assert.Equal(Phase7KnowledgeMergeClassification.Incomparable,result.Classification);
    }

    [Fact]
    public void DifferentConfidenceDoesNotCreateDifferentScopes()
    {
        var result=new Phase7KnowledgeMergeClassifier().Classify(Request(new("10","decimal","km",Confidence:.8m),new("10","decimal","km",Confidence:.9m)));
        Assert.Equal(Phase7KnowledgeMergeClassification.EventMorePrecise,result.Classification);
    }

    [Fact]
    public void SpecializationRequiresTrueScopeEvidence()
    {
        var unscoped=new Phase7KnowledgeMergeClassifier().Classify(Request(new("general"),new("specific")));
        Assert.NotEqual(Phase7KnowledgeMergeClassification.EventSpecificSpecialization,unscoped.Classification);
        var scoped=new Phase7KnowledgeMergeClassifier().Classify(Request(new("general"),new("general"),new(),new("Observation","Delhi")));
        Assert.Equal(Phase7KnowledgeMergeClassification.EventSpecificSpecialization,scoped.Classification);
    }
}
