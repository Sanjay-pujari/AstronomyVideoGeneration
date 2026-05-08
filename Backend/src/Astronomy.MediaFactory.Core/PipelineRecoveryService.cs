using Astronomy.MediaFactory.Contracts;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineRecoveryService : IPipelineRecoveryService
{
    private static readonly Dictionary<string, string[]> PublishStagesByPlatform = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = [PipelineStageNames.YouTubeLongPublished, PipelineStageNames.YouTubeShortPublished],
        ["facebook"] = [PipelineStageNames.FacebookReelPublished],
        ["instagram"] = [PipelineStageNames.InstagramReelPublished],
        ["all"] = [PipelineStageNames.YouTubeLongPublished, PipelineStageNames.YouTubeShortPublished, PipelineStageNames.FacebookReelPublished, PipelineStageNames.InstagramReelPublished]
    };

    private readonly IPipelineRepository _repository;

    public PipelineRecoveryService(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public async Task<PipelineStatusResponse?> GetStatusAsync(Guid pipelineRunId, CancellationToken cancellationToken, bool includeInternal = false)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        var allStages = await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken);
        var stages = FilterStages(allStages, includeInternal).ToArray();
        var published = await _repository.GetPublishedVideosByRunAsync(pipelineRunId, cancellationToken);
        var platformPublications = await _repository.GetPlatformPublicationRecordsByRunAsync(pipelineRunId, cancellationToken);
        var failed = stages.FirstOrDefault(s => s.Status == PersistentStageStatuses.Failed);
        var urls = BuildPublishedUrls(published, platformPublications);

        return new PipelineStatusResponse(
            run.Id,
            run.Status,
            stages.Select(ToDto).ToArray(),
            urls,
            failed?.StageName,
            failed?.LastError ?? run.FailureReason,
            run.OutputFolder);
    }

    public async Task<PipelineStatusResponse?> ResumeAsync(Guid pipelineRunId, string? forceStage, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        var stages = (await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken)).ToList();
        if (!string.IsNullOrWhiteSpace(forceStage))
        {
            var forced = stages.FirstOrDefault(s => s.StageName.Equals(forceStage, StringComparison.OrdinalIgnoreCase));
            if (forced is not null)
            {
                forced.Status = PersistentStageStatuses.Pending;
                forced.LastError = null;
                forced.CompletedUtc = null;
            }
        }

        var firstResumeStage = stages.FirstOrDefault(s => s.Status is PersistentStageStatuses.Failed or PersistentStageStatuses.Pending);
        if (firstResumeStage is not null)
            run.Status = PipelineRunStatus.Running;
        run.ResumeSupported = true;
        await _repository.SaveChangesAsync(cancellationToken);
        await WriteStateAsync(run, stages, firstResumeStage?.StageName, cancellationToken);
        return await GetStatusAsync(pipelineRunId, cancellationToken);
    }

    public async Task<PipelineStatusResponse?> RetryPublishAsync(Guid pipelineRunId, string platform, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        platform = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
        if (!PublishStagesByPlatform.TryGetValue(platform, out var targetStages))
            targetStages = PublishStagesByPlatform["all"];

        var stages = (await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken)).ToList();
        foreach (var stageName in targetStages)
        {
            var stage = stages.FirstOrDefault(s => s.StageName == stageName);
            if (stage is null)
            {
                stage = new PipelineStageExecution { PipelineRunId = pipelineRunId, StageName = stageName, Status = PersistentStageStatuses.Pending, MaxAttempts = 3 };
                await _repository.AddStageExecutionAsync(stage, cancellationToken);
                stages.Add(stage);
            }
            else if (stage.Status != PersistentStageStatuses.Succeeded)
            {
                stage.Status = PersistentStageStatuses.Pending;
                stage.LastError = null;
                stage.CompletedUtc = null;
            }
        }

        await WritePublishIdempotencyAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        await WriteStateAsync(run, stages, targetStages.FirstOrDefault(), cancellationToken);
        return await GetStatusAsync(pipelineRunId, cancellationToken);
    }

    private static IReadOnlyCollection<PipelineStageExecution> FilterStages(IReadOnlyCollection<PipelineStageExecution> stages, bool includeInternal)
    {
        var filtered = includeInternal
            ? stages
            : stages.Where(s => PipelineStageNames.All.Contains(s.StageName, StringComparer.OrdinalIgnoreCase));

        return filtered
            .GroupBy(s => s.StageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.CreatedUtc).First())
            .OrderBy(s => Array.IndexOf(PipelineStageNames.All, s.StageName) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(s => s.StartedUtc)
            .ToArray();
    }

    private static IReadOnlyCollection<string> BuildPublishedUrls(
        IReadOnlyCollection<PublishedVideo> published,
        IReadOnlyCollection<PlatformPublicationRecord> platformPublications)
    {
        var urls = new List<string>();
        urls.AddRange(published
            .Where(p => !string.IsNullOrWhiteSpace(p.YouTubeVideoId) && p.Status.Equals("Published", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"https://www.youtube.com/watch?v={p.YouTubeVideoId}"));
        urls.AddRange(platformPublications
            .Where(p => p.Status == PlatformPublicationStatus.Published)
            .Select(p => p.ExternalUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!));

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static PipelineStageStatusDto ToDto(PipelineStageExecution s)
        => new(s.StageName, s.Status, s.AttemptCount, s.MaxAttempts, s.StartedUtc, s.CompletedUtc, s.LastError, s.OutputPath, s.DiagnosticPath);

    private async Task WriteStateAsync(PipelineRun run, IReadOnlyCollection<PipelineStageExecution> stages, string? currentStage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.OutputFolder))
            return;
        Directory.CreateDirectory(run.OutputFolder);
        var failed = stages.FirstOrDefault(s => s.Status == PersistentStageStatuses.Failed);
        var payload = new
        {
            runId = run.Id,
            overallStatus = run.Status.ToString(),
            stages = stages.Select(ToDto),
            currentStage,
            failedStage = failed?.StageName,
            retryable = failed is not null && failed.AttemptCount < failed.MaxAttempts,
            resumeCommandSuggestion = $"POST /api/pipeline/resume/{run.Id}"
        };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "pipeline-state.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private async Task WritePublishIdempotencyAsync(PipelineRun run, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.OutputFolder))
            return;
        var published = await _repository.GetPublishedVideosByRunAsync(run.Id, cancellationToken);
        var payload = new
        {
            runId = run.Id,
            checkedAtUtc = DateTimeOffset.UtcNow,
            youtubeAlreadyPublished = published.Any(x => !string.IsNullOrWhiteSpace(x.YouTubeVideoId) && x.Status.Equals("Published", StringComparison.OrdinalIgnoreCase)),
            published = published.Select(x => new { x.Id, x.YouTubeVideoId, x.BlobUrl, x.Status })
        };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "publish-idempotency-check.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
