namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryProvenanceValidator
{
    internal static IReadOnlyList<DocumentaryProvenanceArtifactNode> Nodes(DocumentaryProductionPackage package,string correlation)
    {
        var values=new List<(DocumentaryProvenanceArtifactType Type,string Id,string Version)>
        {
            (DocumentaryProvenanceArtifactType.OriginalNarrativeDraft,package.OriginalDraftId,package.OriginalDraftVersion),
            (DocumentaryProvenanceArtifactType.OriginalValidationResult,package.ConvergenceState.InitialValidationResult.DraftId,package.OriginalDraftVersion)
        };
        foreach(var cycle in package.RevisionCycles)
        {
            values.Add((DocumentaryProvenanceArtifactType.RevisionCycle,cycle.CycleId,cycle.Plan.Metadata.CycleSchemaVersion));
            values.Add((DocumentaryProvenanceArtifactType.RevisedNarrativeDraft,cycle.TargetDraftId,cycle.TargetDraftVersion));
            values.Add((DocumentaryProvenanceArtifactType.RevisedValidationResult,cycle.RevisedValidationResult.DraftId,cycle.TargetDraftVersion));
        }
        values.Add((DocumentaryProvenanceArtifactType.ConvergenceState,package.ConvergenceId,package.ConvergenceState.Metadata.ConvergenceSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.AcceptanceDecision,$"{package.ConvergenceId}.acceptance",package.AcceptanceDecision.Metadata.AcceptanceSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.NarrativeReleaseCandidate,package.ReleaseCandidateId,package.ReleaseCandidate.Metadata.ReleaseCandidateSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.ProductionPackageManifest,package.Manifest.ManifestId,package.Manifest.ManifestSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.ProductionPackage,package.PackageId,package.Metadata.PackageSchemaVersion));
        return values.Select((x,i)=>new DocumentaryProvenanceArtifactNode($"{x.Type}.{x.Id}.{x.Version}",x.Type,x.Id,x.Version,i,correlation)).ToArray();
    }

    internal static IReadOnlyList<DocumentaryProvenanceRelationshipEdge> Edges(DocumentaryProductionPackage package,IReadOnlyList<DocumentaryProvenanceArtifactNode> nodes,string correlation)
    {
        var specs=new List<(DocumentaryProvenanceRelationshipType Type,string Source,string Target)>();
        specs.Add((DocumentaryProvenanceRelationshipType.Validates,nodes[1].NodeId,nodes[0].NodeId));
        var convergence=nodes[2+3*package.CompletedCycleCount].NodeId;
        for(var i=0;i<package.CompletedCycleCount;i++)
        {
            var cycle=nodes[2+3*i];var draft=nodes[3+3*i];var validation=nodes[4+3*i];var source=i==0?nodes[0]:nodes[3+3*(i-1)];
            specs.Add((DocumentaryProvenanceRelationshipType.Revises,cycle.NodeId,source.NodeId));
            specs.Add((DocumentaryProvenanceRelationshipType.ProducesDraft,cycle.NodeId,draft.NodeId));
            specs.Add((DocumentaryProvenanceRelationshipType.ProducesValidation,cycle.NodeId,validation.NodeId));
            specs.Add((DocumentaryProvenanceRelationshipType.Validates,validation.NodeId,draft.NodeId));
            specs.Add((DocumentaryProvenanceRelationshipType.AdvancesConvergence,cycle.NodeId,convergence));
        }
        var finalDraft=package.CompletedCycleCount==0?nodes[0]:nodes[3+3*(package.CompletedCycleCount-1)];
        var acceptance=nodes[^4];var candidate=nodes[^3];var manifest=nodes[^2];var productionPackage=nodes[^1];
        specs.Add((DocumentaryProvenanceRelationshipType.ConvergesTo,convergence,finalDraft.NodeId));
        specs.Add((DocumentaryProvenanceRelationshipType.AcceptedBy,convergence,acceptance.NodeId));
        specs.Add((DocumentaryProvenanceRelationshipType.ProducesReleaseCandidate,acceptance.NodeId,candidate.NodeId));
        specs.Add((DocumentaryProvenanceRelationshipType.ManifestDescribes,manifest.NodeId,productionPackage.NodeId));
        specs.Add((DocumentaryProvenanceRelationshipType.PackagedInto,candidate.NodeId,productionPackage.NodeId));
        return specs.Select((x,i)=>new DocumentaryProvenanceRelationshipEdge($"{x.Type}.{x.Source}.to.{x.Target}",x.Type,x.Source,x.Target,i,correlation)).ToArray();
    }

    internal static void ValidateRecord(DocumentaryProvenanceRecord record)
    {
        var p=record.ProductionPackage;
        if(!p.IsComplete)throw new ArgumentException("Package must be complete.");
        DocumentaryProductionPackageValidator.ValidateComplete(p.PackageId,p.ReleaseCandidate,p.Manifest,p.Metadata);
        if(record.ProvenanceId!=$"{p.PackageId}.provenance"||record.PackageId!=p.PackageId||record.ManifestId!=p.Manifest.ManifestId||record.ReleaseCandidateId!=p.ReleaseCandidateId||record.ConvergenceId!=p.ConvergenceId||record.OriginalDraftId!=p.OriginalDraftId||record.OriginalDraftVersion!=p.OriginalDraftVersion||record.CurrentDraftId!=p.CurrentDraftId||record.CurrentDraftVersion!=p.CurrentDraftVersion||record.CompletedCycleCount!=p.CompletedCycleCount)throw new ArgumentException("Record identities are inconsistent.");
        var correlation=p.Metadata.CorrelationId;
        if(!new[]{p.Manifest.CorrelationId,p.ReleaseCandidate.Metadata.CorrelationId,p.AcceptanceDecision.Metadata.CorrelationId,p.ConvergenceState.Metadata.CorrelationId,record.Metadata.CorrelationId}.All(x=>string.Equals(x,correlation,StringComparison.Ordinal)))throw new ArgumentException("Record correlation is inconsistent.");
        var expectedNodes=Nodes(p,correlation);var expectedEdges=Edges(p,expectedNodes,correlation);
        if(!EquivalentNodes(record.ArtifactNodes,expectedNodes)||!EquivalentEdges(record.RelationshipEdges,expectedEdges))throw new ArgumentException("Record graph is not canonical.");
    }
    private static bool EquivalentNodes(IReadOnlyList<DocumentaryProvenanceArtifactNode>a,IReadOnlyList<DocumentaryProvenanceArtifactNode>b)=>a.Count==b.Count&&a.Select((x,i)=>x.NodeId==b[i].NodeId&&x.ArtifactType==b[i].ArtifactType&&x.ArtifactIdentity==b[i].ArtifactIdentity&&x.ArtifactVersion==b[i].ArtifactVersion&&x.Sequence==i&&x.CorrelationId==b[i].CorrelationId).All(x=>x);
    private static bool EquivalentEdges(IReadOnlyList<DocumentaryProvenanceRelationshipEdge>a,IReadOnlyList<DocumentaryProvenanceRelationshipEdge>b)=>a.Count==b.Count&&a.Select((x,i)=>x.EdgeId==b[i].EdgeId&&x.RelationshipType==b[i].RelationshipType&&x.SourceNodeId==b[i].SourceNodeId&&x.TargetNodeId==b[i].TargetNodeId&&x.Sequence==i&&x.CorrelationId==b[i].CorrelationId).All(x=>x);
}
