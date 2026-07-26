namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal sealed class DocumentaryProvenanceGraphSpecification
{
    internal DocumentaryProvenanceGraphSpecification(IReadOnlyList<DocumentaryProvenanceArtifactNode> nodes,IReadOnlyList<DocumentaryProvenanceRelationshipEdge> edges)
    { Nodes=DocumentaryProvenanceInventory.Copy(nodes,nameof(nodes)); Edges=DocumentaryProvenanceInventory.Copy(edges,nameof(edges)); }
    internal IReadOnlyList<DocumentaryProvenanceArtifactNode> Nodes{get;}
    internal IReadOnlyList<DocumentaryProvenanceRelationshipEdge> Edges{get;}
}

internal static class DocumentaryProvenanceValidator
{
    private static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.Ordinal);
    private static IReadOnlyList<DocumentaryProvenanceRejectionReason> Ordered(IEnumerable<DocumentaryProvenanceRejectionReason> reasons)=>
        Array.AsReadOnly(reasons.Distinct().OrderBy(x=>(int)x).ToArray());

    internal static IReadOnlyList<DocumentaryProvenanceArtifactNode> Nodes(DocumentaryProductionPackage package,string correlation)
    {
        var values=new List<(DocumentaryProvenanceArtifactType Type,string Id,string Version)>{
            (DocumentaryProvenanceArtifactType.OriginalNarrativeDraft,package.OriginalDraftId,package.OriginalDraftVersion),
            (DocumentaryProvenanceArtifactType.OriginalValidationResult,package.ConvergenceState.InitialValidationResult.DraftId,package.OriginalDraftVersion)};
        foreach(var c in package.RevisionCycles){values.Add((DocumentaryProvenanceArtifactType.RevisionCycle,c.CycleId,c.Plan.Metadata.CycleSchemaVersion));values.Add((DocumentaryProvenanceArtifactType.RevisedNarrativeDraft,c.TargetDraftId,c.TargetDraftVersion));values.Add((DocumentaryProvenanceArtifactType.RevisedValidationResult,c.RevisedValidationResult.DraftId,c.TargetDraftVersion));}
        values.Add((DocumentaryProvenanceArtifactType.ConvergenceState,package.ConvergenceId,package.ConvergenceState.Metadata.ConvergenceSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.AcceptanceDecision,$"{package.ConvergenceId}.acceptance",package.AcceptanceDecision.Metadata.AcceptanceSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.NarrativeReleaseCandidate,package.ReleaseCandidateId,package.ReleaseCandidate.Metadata.ReleaseCandidateSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.ProductionPackageManifest,package.Manifest.ManifestId,package.Manifest.ManifestSchemaVersion));
        values.Add((DocumentaryProvenanceArtifactType.ProductionPackage,package.PackageId,package.Metadata.PackageSchemaVersion));
        return values.Select((x,i)=>new DocumentaryProvenanceArtifactNode($"{x.Type}.{x.Id}.{x.Version}",x.Type,x.Id,x.Version,i,correlation)).ToArray();
    }

    internal static IReadOnlyList<DocumentaryProvenanceRelationshipEdge> Edges(DocumentaryProductionPackage p,IReadOnlyList<DocumentaryProvenanceArtifactNode> nodes,string correlation)
    {
        var s=new List<(DocumentaryProvenanceRelationshipType Type,string Source,string Target)>{(DocumentaryProvenanceRelationshipType.Validates,nodes[1].NodeId,nodes[0].NodeId)};
        var convergence=nodes[2+3*p.CompletedCycleCount].NodeId;
        for(var i=0;i<p.CompletedCycleCount;i++){var cycle=nodes[2+3*i];var draft=nodes[3+3*i];var validation=nodes[4+3*i];var source=i==0?nodes[0]:nodes[3+3*(i-1)];s.Add((DocumentaryProvenanceRelationshipType.Revises,cycle.NodeId,source.NodeId));s.Add((DocumentaryProvenanceRelationshipType.ProducesDraft,cycle.NodeId,draft.NodeId));s.Add((DocumentaryProvenanceRelationshipType.ProducesValidation,cycle.NodeId,validation.NodeId));s.Add((DocumentaryProvenanceRelationshipType.Validates,validation.NodeId,draft.NodeId));s.Add((DocumentaryProvenanceRelationshipType.AdvancesConvergence,cycle.NodeId,convergence));}
        var finalDraft=p.CompletedCycleCount==0?nodes[0]:nodes[3+3*(p.CompletedCycleCount-1)];
        s.Add((DocumentaryProvenanceRelationshipType.ConvergesTo,convergence,finalDraft.NodeId));s.Add((DocumentaryProvenanceRelationshipType.AcceptedBy,convergence,nodes[^4].NodeId));s.Add((DocumentaryProvenanceRelationshipType.ProducesReleaseCandidate,nodes[^4].NodeId,nodes[^3].NodeId));s.Add((DocumentaryProvenanceRelationshipType.ManifestDescribes,nodes[^2].NodeId,nodes[^1].NodeId));s.Add((DocumentaryProvenanceRelationshipType.PackagedInto,nodes[^3].NodeId,nodes[^1].NodeId));
        return s.Select((x,i)=>new DocumentaryProvenanceRelationshipEdge($"{x.Type}.{x.Source}.to.{x.Target}",x.Type,x.Source,x.Target,i,correlation)).ToArray();
    }

    internal static DocumentaryProvenanceGraphSpecification CreateCanonicalGraph(DocumentaryProductionPackage p,string correlation){var n=Nodes(p,correlation);return new(n,Edges(p,n,correlation));}

    internal static IReadOnlyList<DocumentaryProvenanceRejectionReason> ValidatePackageForProvenance(DocumentaryProductionPackage p,DocumentaryProvenancePolicy policy,DocumentaryProvenanceMetadata metadata)
    {
        var r=new List<DocumentaryProvenanceRejectionReason>(); var correlation=p.Metadata.CorrelationId;
        if(!p.IsComplete||!p.IsAccepted||!p.IsClean||!p.IsFullyResolved||p.FinalFindingCount!=0||p.UnresolvedRevisionItemCount!=0)r.Add(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete);
        try{DocumentaryProductionPackageValidator.ValidateComplete(p.PackageId,p.ReleaseCandidate,p.Manifest,p.Metadata);}catch(ArgumentException){r.Add(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete);}
        if(!Eq(p.PackageId,$"{p.ReleaseCandidateId}.production-package"))r.Add(DocumentaryProvenanceRejectionReason.PackageIdentityMismatch);
        if(!Eq(p.Manifest.PackageId,p.PackageId)||!Eq(p.Manifest.ManifestId,$"{p.PackageId}.manifest")||p.Manifest.Entries.Count!=6||!Eq(p.Manifest.CorrelationId,correlation)||!DocumentaryProductionPackageValidator.ManifestMatches(p.Manifest,p.ReleaseCandidate,p.PackageId))r.Add(DocumentaryProvenanceRejectionReason.ManifestIdentityMismatch);
        if(!Eq(p.OriginalDraftId,p.ConvergenceState.OriginalDraftId)||!Eq(p.OriginalDraftVersion,p.ConvergenceState.OriginalDraftVersion)||!Eq(p.CurrentDraftId,p.ConvergenceState.CurrentDraftId)||!Eq(p.CurrentDraftVersion,p.ConvergenceState.CurrentDraftVersion)||!Eq(p.NarrativeDraft.DraftId,p.CurrentDraftId)||!Eq(p.NarrativeDraft.Version,p.CurrentDraftVersion)||!DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(p.NarrativeDraft,p.ReleaseCandidate.NarrativeDraft)||!DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(p.NarrativeDraft,p.ConvergenceState.CurrentDraft))r.Add(DocumentaryProvenanceRejectionReason.DraftLineageMismatch);
        if(!Eq(p.ConvergenceState.InitialValidationResult.DraftId,p.OriginalDraftId)||!Eq(p.FinalValidationResult.DraftId,p.CurrentDraftId)||!DocumentaryNarrativeRevisionConvergenceStateValidator.ValidationResultsAreEquivalent(p.FinalValidationResult,p.ReleaseCandidate.FinalValidationResult)||!DocumentaryNarrativeRevisionConvergenceStateValidator.ValidationResultsAreEquivalent(p.FinalValidationResult,p.ConvergenceState.CurrentValidationResult)||p.RevisionCycles.Any(c=>!Eq(c.RevisedValidationResult.DraftId,c.TargetDraftId)))r.Add(DocumentaryProvenanceRejectionReason.ValidationLineageMismatch);
        var cycles=p.RevisionCycles; var revisionBad=cycles.Count!=p.CompletedCycleCount||cycles.Count!=p.ConvergenceState.Cycles.Count||!DocumentaryProductionPackageValidator.RevisionCyclesAreEquivalent(cycles,p.ConvergenceState.Cycles)||cycles.Select(c=>c.CycleId).Distinct(StringComparer.Ordinal).Count()!=cycles.Count;
        for(var i=0;i<cycles.Count;i++){var c=cycles[i];var sourceId=i==0?p.OriginalDraftId:cycles[i-1].TargetDraftId;var sourceVersion=i==0?p.OriginalDraftVersion:cycles[i-1].TargetDraftVersion;revisionBad|=!Eq(c.SourceDraftId,sourceId)||!Eq(c.SourceDraftVersion,sourceVersion)||!Eq(c.RevisedValidationResult.DraftId,c.TargetDraftId)||!Eq(c.CorrelationId,correlation)||!Eq(c.Plan.Metadata.CorrelationId,correlation)||!Eq(c.Submission.Metadata.CorrelationId,correlation)||!Eq(c.BindingRequest.Metadata.CorrelationId,correlation);}
        if(cycles.Count>0)revisionBad|=!Eq(cycles[^1].TargetDraftId,p.CurrentDraftId)||!Eq(cycles[^1].TargetDraftVersion,p.CurrentDraftVersion);if(revisionBad)r.Add(DocumentaryProvenanceRejectionReason.RevisionLineageMismatch);
        var cstate=p.ConvergenceState;var convergenceBad=cstate.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||cstate.NextAction!=DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft||!Eq(p.ConvergenceId,cstate.ConvergenceId)||p.CompletedCycleCount!=cstate.CompletedCycleCount||!Eq(p.CurrentDraftId,cstate.CurrentDraftId)||!Eq(p.CurrentDraftVersion,cstate.CurrentDraftVersion)||cstate.CurrentFindingCount!=0;try{DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(cstate);}catch(ArgumentException){convergenceBad=true;}if(convergenceBad)r.Add(DocumentaryProvenanceRejectionReason.ConvergenceLineageMismatch);
        var a=p.AcceptanceDecision; if(a.Status!=DocumentaryNarrativeAcceptanceStatus.Accepted||a.PrimaryReason!=DocumentaryNarrativeAcceptanceReason.ConvergedAndClean||a.SupportingReasons.Count!=0||!Eq(a.ConvergenceId,p.ConvergenceId)||!Eq(a.CurrentDraftId,p.CurrentDraftId)||!Eq(a.CurrentDraftVersion,p.CurrentDraftVersion)||a.CurrentFindingCount!=0||a.CompletedCycleCount!=p.CompletedCycleCount||a.UnresolvedRevisionItemCount!=0||!DocumentaryProductionPackageValidator.AcceptanceDecisionsAreEquivalent(a,p.ReleaseCandidate.AcceptanceDecision))r.Add(DocumentaryProvenanceRejectionReason.AcceptanceLineageMismatch);
        var rc=p.ReleaseCandidate;var releaseBad=!Eq(p.ReleaseCandidateId,rc.ReleaseCandidateId)||!Eq(rc.ReleaseCandidateId,$"{rc.DraftId}.narrative-release-candidate.{rc.DraftVersion}")||!Eq(rc.ConvergenceId,p.ConvergenceId)||!Eq(rc.OriginalDraftId,p.OriginalDraftId)||!Eq(rc.OriginalDraftVersion,p.OriginalDraftVersion)||!Eq(rc.DraftId,p.CurrentDraftId)||!Eq(rc.DraftVersion,p.CurrentDraftVersion)||rc.CompletedCycleCount!=p.CompletedCycleCount||rc.FinalFindingCount!=0||!rc.IsAccepted||!rc.IsClean||!rc.IsFullyResolved;try{DocumentaryNarrativeReleaseCandidateValidator.Validate(rc);}catch(ArgumentException){releaseBad=true;}if(releaseBad)r.Add(DocumentaryProvenanceRejectionReason.ReleaseCandidateLineageMismatch);
        if(!new[]{p.Manifest.CorrelationId,rc.Metadata.CorrelationId,a.Metadata.CorrelationId,cstate.Metadata.CorrelationId,metadata.CorrelationId}.All(x=>Eq(x,correlation))||cycles.Any(c=>!Eq(c.CorrelationId,correlation)||!Eq(c.Plan.Metadata.CorrelationId,correlation)||!Eq(c.Submission.Metadata.CorrelationId,correlation)||!Eq(c.BindingRequest.Metadata.CorrelationId,correlation)))r.Add(DocumentaryProvenanceRejectionReason.CorrelationMismatch);
        if(!policy.RequireCompleteProductionPackage||!policy.RequireOriginalDraftLineage||!policy.RequireValidationLineage||!policy.RequireRevisionCycleLineage||!policy.RequireConvergenceLineage||!policy.RequireAcceptanceLineage||!policy.RequireReleaseCandidateLineage||!policy.RequireManifestLineage||!policy.RequiredArtifactTypes.SequenceEqual(DocumentaryProvenanceInventory.Artifacts)||!policy.RequiredRelationshipTypes.SequenceEqual(DocumentaryProvenanceInventory.Relationships)||!Eq(policy.PolicySchemaVersion,DocumentaryProvenanceInventory.Schema))r.Add(DocumentaryProvenanceRejectionReason.PolicyRejected);
        return Ordered(r);
    }

    internal static IReadOnlyList<DocumentaryProvenanceRejectionReason> ValidateGraph(DocumentaryProductionPackage p,IReadOnlyList<DocumentaryProvenanceArtifactNode> nodes,IReadOnlyList<DocumentaryProvenanceRelationshipEdge> edges,string correlation)
    {
        ArgumentNullException.ThrowIfNull(nodes);ArgumentNullException.ThrowIfNull(edges);var expected=CreateCanonicalGraph(p,correlation);var r=new List<DocumentaryProvenanceRejectionReason>();
        bool nodeMismatch=nodes.Count!=expected.Nodes.Count||nodes.Select((x,i)=>i<expected.Nodes.Count&&x.NodeId==expected.Nodes[i].NodeId&&x.ArtifactType==expected.Nodes[i].ArtifactType&&x.ArtifactIdentity==expected.Nodes[i].ArtifactIdentity&&x.ArtifactVersion==expected.Nodes[i].ArtifactVersion&&x.Sequence==i&&Eq(x.CorrelationId,correlation)).Any(x=>!x)||nodes.Select(x=>x.NodeId).Distinct(StringComparer.Ordinal).Count()!=nodes.Count||nodes.Select(x=>x.Sequence).Distinct().Count()!=nodes.Count;
        bool edgeMismatch=edges.Count!=expected.Edges.Count||edges.Select((x,i)=>i<expected.Edges.Count&&x.EdgeId==expected.Edges[i].EdgeId&&x.RelationshipType==expected.Edges[i].RelationshipType&&x.SourceNodeId==expected.Edges[i].SourceNodeId&&x.TargetNodeId==expected.Edges[i].TargetNodeId&&x.Sequence==i&&Eq(x.CorrelationId,correlation)).Any(x=>!x)||edges.Select(x=>x.EdgeId).Distinct(StringComparer.Ordinal).Count()!=edges.Count||edges.Select(x=>x.Sequence).Distinct().Count()!=edges.Count;
        if(nodeMismatch)r.Add(DocumentaryProvenanceRejectionReason.ArtifactInventoryMismatch);if(edgeMismatch)r.Add(DocumentaryProvenanceRejectionReason.RelationshipInventoryMismatch);
        if(expected.Nodes.Select(x=>x.NodeId).Except(nodes.Select(x=>x.NodeId),StringComparer.Ordinal).Any())r.Add(DocumentaryProvenanceRejectionReason.RequiredNodeMissing);
        if(expected.Edges.Select(x=>x.EdgeId).Except(edges.Select(x=>x.EdgeId),StringComparer.Ordinal).Any())r.Add(DocumentaryProvenanceRejectionReason.RequiredEdgeMissing);
        return Ordered(r);
    }

    internal static void ValidateRecord(DocumentaryProvenanceRecord record)
    {
        var p=record.ProductionPackage;var semantic=ValidatePackageForProvenance(p,record.Policy,record.Metadata);if(semantic.Count!=0)throw new ArgumentException("Package lineage is invalid.");
        if(record.CompletedCycleCount<0||record.ProvenanceId!=$"{p.PackageId}.provenance"||record.PackageId!=p.PackageId||record.ManifestId!=p.Manifest.ManifestId||record.ReleaseCandidateId!=p.ReleaseCandidateId||record.ConvergenceId!=p.ConvergenceId||record.OriginalDraftId!=p.OriginalDraftId||record.OriginalDraftVersion!=p.OriginalDraftVersion||record.CurrentDraftId!=p.CurrentDraftId||record.CurrentDraftVersion!=p.CurrentDraftVersion||record.CompletedCycleCount!=p.CompletedCycleCount)throw new ArgumentException("Record identities are inconsistent.");
        if(ValidateGraph(p,record.ArtifactNodes,record.RelationshipEdges,p.Metadata.CorrelationId).Count!=0)throw new ArgumentException("Record graph is not canonical.");
        var ids=record.ArtifactNodes.Select(x=>x.NodeId).ToHashSet(StringComparer.Ordinal);if(record.RelationshipEdges.Any(x=>!ids.Contains(x.SourceNodeId)||!ids.Contains(x.TargetNodeId)))throw new ArgumentException("Record graph has a dangling endpoint.");
    }
}
