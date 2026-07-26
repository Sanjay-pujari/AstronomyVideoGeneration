namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelineSummarizer
{
    /// <summary>Asset counts describe physical generated/verified/failed execution results, never planned assets.</summary>
    public DocumentaryMediaPipelineSummary Summarize(DocumentaryMediaPipelineExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record);
        var completed=record.VariantRecords.Where(x=>x.Status==DocumentaryMediaPipelineStatus.Complete).ToArray();
        var assets=record.VariantRecords.SelectMany(x=>x.AssetResults).ToArray();
        return new(record.ExecutionId,record.MediaProjectId,record.TopicId,record.MediaProject.TopicProfile.TopicFamily,record.Status,
            record.VariantCount,record.CompletedVariantCount,record.FailedVariantCount,
            completed.Count(x=>x.VariantType is DocumentaryMediaVariantType.LongEnglish or DocumentaryMediaVariantType.LongHindi),
            completed.Count(x=>x.VariantType is DocumentaryMediaVariantType.ShortEnglish or DocumentaryMediaVariantType.ShortHindi),
            completed.Count(x=>x.VariantType is DocumentaryMediaVariantType.LongEnglish or DocumentaryMediaVariantType.ShortEnglish),
            completed.Count(x=>x.VariantType is DocumentaryMediaVariantType.LongHindi or DocumentaryMediaVariantType.ShortHindi),
            assets.Length,assets.Count(x=>(int)x.AssetType<=(int)DocumentaryMediaAssetType.HistoricalIllustrationImage),
            assets.Count(x=>x.AssetType==DocumentaryMediaAssetType.NarrationAudio),assets.Count(x=>x.AssetType==DocumentaryMediaAssetType.SubtitleDocument),
            assets.Count(x=>x.AssetType==DocumentaryMediaAssetType.SceneVideo),assets.Count(x=>x.AssetType==DocumentaryMediaAssetType.VariantVideo),
            record.PlannedDurationMilliseconds,record.EffectiveDurationMilliseconds,record.Metadata.CreatedUtc,record.Metadata.CreatedBy,record.IsComplete);
    }
}
