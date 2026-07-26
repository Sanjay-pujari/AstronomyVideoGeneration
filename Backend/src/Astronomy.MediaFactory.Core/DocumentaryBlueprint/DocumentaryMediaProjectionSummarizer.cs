namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionSummarizer
{
    public DocumentaryMediaProjectionSummary Summarize(DocumentaryMediaProject project){ArgumentNullException.ThrowIfNull(project);if(!DocumentaryMediaProjectionValidator.ProjectValid(project))throw new ArgumentException("The media project is not complete.",nameof(project));var scenes=project.Variants.SelectMany(x=>x.Scenes).ToArray();return new(project.MediaProjectId,project.MaterializationId,project.TopicId,project.TopicProfile.TopicFamily,4,2,2,2,2,scenes.Length,scenes.SelectMany(x=>x.Narration).Sum(x=>x.Text.Length),scenes.Sum(x=>x.SubtitleCues.Count),scenes.Sum(x=>x.VisualPrompts.Count),project.TotalPlannedDurationMilliseconds,project.Variants.Select(x=>x.VariantType).ToArray(),DocumentaryMediaProjectionInventory.Languages,DocumentaryMediaProjectionInventory.Formats,project.Metadata.CreatedUtc,project.Metadata.CreatedBy,true);}
}
