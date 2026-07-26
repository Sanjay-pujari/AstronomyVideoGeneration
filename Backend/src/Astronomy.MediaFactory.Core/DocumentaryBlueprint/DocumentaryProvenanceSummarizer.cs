namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProvenanceSummarizer
{
    public DocumentaryProvenanceSummary Summarize(DocumentaryProvenanceRecord provenanceRecord)
    {
        ArgumentNullException.ThrowIfNull(provenanceRecord);DocumentaryProvenanceValidator.ValidateRecord(provenanceRecord);
        return new(provenanceRecord.ProvenanceId,provenanceRecord.PackageId,provenanceRecord.ManifestId,provenanceRecord.ReleaseCandidateId,provenanceRecord.ConvergenceId,provenanceRecord.OriginalDraftId,provenanceRecord.OriginalDraftVersion,provenanceRecord.CurrentDraftId,provenanceRecord.CurrentDraftVersion,provenanceRecord.CompletedCycleCount,provenanceRecord.ArtifactNodeCount,provenanceRecord.RelationshipEdgeCount,provenanceRecord.ArtifactNodes.Select(x=>x.ArtifactType).Distinct().ToArray(),provenanceRecord.RelationshipEdges.Select(x=>x.RelationshipType).Distinct().ToArray(),provenanceRecord.Metadata.CreatedUtc,provenanceRecord.Metadata.CreatedBy,true);
    }
}
