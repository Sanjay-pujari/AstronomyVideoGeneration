namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProductionCertificationSummarizer
{
    public DocumentaryProductionCertificationSummary Summarize(DocumentaryProductionCertificationRecord record)
    {
        DocumentaryProductionCertificationValidator.ValidateRecord(record);
        var types=record.VariantCertificationRecords.Select(x=>x.VariantType).ToList();
        return new(record.ProductionCertificationId,record.PipelineExecutionId,record.MediaProjectId,record.MaterializationId,record.TopicId,record.MediaProject.TopicProfile.TopicFamily,record.VariantCount,
            types.Count(x=>x is DocumentaryMediaVariantType.LongEnglish or DocumentaryMediaVariantType.LongHindi),types.Count(x=>x is DocumentaryMediaVariantType.ShortEnglish or DocumentaryMediaVariantType.ShortHindi),types.Count(x=>x is DocumentaryMediaVariantType.LongEnglish or DocumentaryMediaVariantType.ShortEnglish),types.Count(x=>x is DocumentaryMediaVariantType.LongHindi or DocumentaryMediaVariantType.ShortHindi),record.VerifiedOutputCount,record.TraceabilityLinkCount,record.Metadata.CertifiedUtc,record.Metadata.CertifiedBy,true);
    }
}
