namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryExportMaterializationSummarizer
{
    public DocumentaryExportMaterializationSummary Summarize(DocumentaryExportMaterializationRecord record)
    {ArgumentNullException.ThrowIfNull(record);if(!DocumentaryExportMaterializationValidator.RecordValid(record))throw new ArgumentException("Materialization record is inconsistent.",nameof(record));return new(record.MaterializationId,record.Manifest.ManifestId,record.ExportSpecificationId,record.CertificationId,record.ProvenanceId,record.PackageId,record.ReleaseCandidateId,record.ConvergenceId,record.SerializerProfile,record.CharacterEncoding,record.PayloadCount,record.DependencyCount,record.TotalCharacterCount,record.TotalByteCount,record.Payloads.Select(x=>x.PayloadType).ToArray(),record.Payloads.Select(x=>x.ContentType).ToArray(),record.Metadata.CreatedUtc,record.Metadata.CreatedBy,true);}
}
