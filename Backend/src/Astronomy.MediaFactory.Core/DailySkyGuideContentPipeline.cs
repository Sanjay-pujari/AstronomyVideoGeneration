using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class DailySkyGuideContentPipeline(PipelineOrchestrator orchestrator, IContentCategorySettingsService settingsService) : IContentCategoryPipeline
{
    public ContentPipelineType PipelineType => ContentPipelineType.DailySkyGuide;

    public async Task<ContentPipelineRunResult> RunAsync(ContentPipelineRunRequest request, CancellationToken ct)
    {
        if (!await settingsService.IsEnabledAsync(PipelineType, ct))
            return new ContentPipelineRunResult(PipelineType, false, "Pipeline is disabled.");

        var settings = await settingsService.GetSettingsAsync(PipelineType, ct);
        var prompts = await settingsService.GetPromptSettingsAsync(PipelineType, request.Language ?? "en", ct);
        var publishing = await settingsService.GetPublishingSettingsAsync(PipelineType, ct);

        var run = await orchestrator.RunAsync(new RunPipelineRequest(
            request.Date,
            ContentType.DailySkyGuide,
            "Udaipur",
            "Asia/Kolkata",
            request.PublishToYouTube ?? false,
            request.UseTopicPlanner ?? false,
            RegionId: request.RegionId,
            Language: request.Language), ct);

        var report = new { pipelineType = PipelineType.ToString(), settingsUsed = settings, promptsUsed = prompts, publishingSettingsUsed = publishing, enabled = settings?.Enabled ?? false, durationPolicy = new { settings?.TargetDurationSeconds, settings?.MaxDurationSeconds }, outputFolder = run.OutputFolder };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder ?? ".", "content-category-pipeline-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);

        return new ContentPipelineRunResult(PipelineType, true, "Pipeline started.", run.Id);
    }
}
