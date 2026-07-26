using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryProvenanceTests
{
    private static DocumentaryProductionPackage Package(int cycles)
    {
        var candidate=cycles switch {0=>OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate(),1=>OrionDocumentaryNarrativeAcceptanceFixture.OneCycleReleaseCandidate(),_=>OrionDocumentaryNarrativeAcceptanceFixture.MultiCycleReleaseCandidate()};
        var policy=new DocumentaryProductionPackagePolicy(true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProductionPackageSection>(),"1.0");
        var metadata=new DocumentaryProductionPackageMetadata(DateTimeOffset.Parse("2026-07-26T11:12:13.1234567+05:30")," packager ","1.0",candidate.Metadata.CorrelationId);
        return Assert.IsType<DocumentaryProductionPackage>(new DocumentaryProductionPackageAssembler().Assemble(new(candidate,policy,metadata)).Package);
    }
    private static DocumentaryProvenancePolicy Policy()=>new(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProvenanceArtifactType>(),Enum.GetValues<DocumentaryProvenanceRelationshipType>(),"1.0");
    private static DocumentaryProvenanceBuildResult Build(int cycles)
    { var p=Package(cycles);return new DocumentaryProvenanceBuilder().Build(new(p,Policy(),new(DateTimeOffset.Parse("2026-07-26T12:13:14.1234567-04:00")," certifier ","1.0",p.Metadata.CorrelationId))); }

    [Fact] public void Inventories_are_exact()
    {
        Assert.Equal(["Complete","Rejected"],Enum.GetNames<DocumentaryProvenanceStatus>());
        Assert.Equal(["ProductionPackageNotComplete","PackageIdentityMismatch","ManifestIdentityMismatch","ArtifactInventoryMismatch","RelationshipInventoryMismatch","DraftLineageMismatch","ValidationLineageMismatch","RevisionLineageMismatch","ConvergenceLineageMismatch","AcceptanceLineageMismatch","ReleaseCandidateLineageMismatch","CorrelationMismatch","RequiredNodeMissing","RequiredEdgeMissing","PolicyRejected"],Enum.GetNames<DocumentaryProvenanceRejectionReason>());
        Assert.Equal(["OriginalNarrativeDraft","OriginalValidationResult","RevisionCycle","RevisedNarrativeDraft","RevisedValidationResult","ConvergenceState","AcceptanceDecision","NarrativeReleaseCandidate","ProductionPackageManifest","ProductionPackage"],Enum.GetNames<DocumentaryProvenanceArtifactType>());
        Assert.Equal(["Validates","Revises","ProducesDraft","ProducesValidation","AdvancesConvergence","ConvergesTo","AcceptedBy","ProducesReleaseCandidate","ManifestDescribes","PackagedInto"],Enum.GetNames<DocumentaryProvenanceRelationshipType>());
    }
    [Theory][InlineData(0,7,6)][InlineData(1,10,11)][InlineData(2,13,16)]
    public void Graphs_are_canonical_complete_and_summarized(int cycles,int nodeCount,int edgeCount)
    {
        var result=Build(cycles);var record=Assert.IsType<DocumentaryProvenanceRecord>(result.ProvenanceRecord);
        Assert.True(result.IsComplete);Assert.True(result.HasProvenanceRecord);Assert.False(result.IsRejected);Assert.Empty(result.RejectionReasons);
        Assert.Equal(nodeCount,record.ArtifactNodeCount);Assert.Equal(edgeCount,record.RelationshipEdgeCount);
        Assert.Equal(Enumerable.Range(0,nodeCount),record.ArtifactNodes.Select(x=>x.Sequence));Assert.Equal(Enumerable.Range(0,edgeCount),record.RelationshipEdges.Select(x=>x.Sequence));
        Assert.All(record.ArtifactNodes,x=>Assert.Equal(record.Metadata.CorrelationId,x.CorrelationId));Assert.All(record.RelationshipEdges,x=>Assert.Equal(record.Metadata.CorrelationId,x.CorrelationId));
        var summary=new DocumentaryProvenanceSummarizer().Summarize(record);Assert.Equal(nodeCount,summary.ArtifactNodeCount);Assert.Equal(edgeCount,summary.RelationshipEdgeCount);Assert.True(summary.IsComplete);
    }
    [Fact] public void Contracts_reject_invalid_identity_policy_and_result_combinations()
    {
        Assert.Throws<ArgumentException>(()=>new DocumentaryProvenanceArtifactNode("wrong",DocumentaryProvenanceArtifactType.OriginalNarrativeDraft,"id","1",0,"c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProvenanceRelationshipEdge("wrong",DocumentaryProvenanceRelationshipType.Validates,"a","b",0,"c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProvenancePolicy(false,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProvenanceArtifactType>(),Enum.GetValues<DocumentaryProvenanceRelationshipType>(),"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProvenanceBuildResult(DocumentaryProvenanceStatus.Complete,[DocumentaryProvenanceRejectionReason.PolicyRejected],Build(0).ProvenanceRecord));
        Assert.Throws<ArgumentException>(()=>new DocumentaryProvenanceBuildResult(DocumentaryProvenanceStatus.Rejected,[],null));
    }
    [Fact] public void Web_json_round_trip_is_byte_identical_and_operations_do_not_mutate()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var result=Build(2);var before=JsonSerializer.Serialize(result,options);
        var reconstructed=Assert.IsType<DocumentaryProvenanceBuildResult>(JsonSerializer.Deserialize<DocumentaryProvenanceBuildResult>(before,options));
        Assert.Equal(before,JsonSerializer.Serialize(reconstructed,options));var record=Assert.IsType<DocumentaryProvenanceRecord>(result.ProvenanceRecord);
        _=new DocumentaryProvenanceSummarizer().Summarize(record);Assert.Equal(before,JsonSerializer.Serialize(result,options));
    }
    [Fact] public void Operations_are_sealed_parameterless_stateless_and_synchronous()
    {
        foreach(var pair in new[]{(typeof(DocumentaryProvenanceBuilder),"Build"),(typeof(DocumentaryProvenanceSummarizer),"Summarize")})
        {Assert.True(pair.Item1.IsSealed);Assert.Empty(pair.Item1.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));Assert.Single(pair.Item1.GetConstructors());var method=Assert.Single(pair.Item1.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly));Assert.Equal(pair.Item2,method.Name);Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));}
    }
}
