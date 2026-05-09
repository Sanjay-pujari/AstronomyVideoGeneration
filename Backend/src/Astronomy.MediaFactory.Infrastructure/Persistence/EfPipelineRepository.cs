using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EfPipelineRepository : IPipelineRepository
{
    private readonly MediaFactoryDbContext _db;

    public EfPipelineRepository(MediaFactoryDbContext db) { _db = db; }

    public async Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken)
    {
        await _db.PipelineRuns.AddAsync(run, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken)
        => _db.PipelineRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken)
        => await _db.PipelineRuns.OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PipelineRun>> GetGeneratedSpecialEventRunsAsync(int take, CancellationToken cancellationToken)
        => await _db.PipelineRuns.AsNoTracking()
            .Where(x => x.ContentType == ContentType.SpecialEventGuide && !string.IsNullOrWhiteSpace(x.EventId))
            .OrderByDescending(x => x.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<bool> HasSpecialEventRunAsync(string eventId, DateOnly runDate, string regionId, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken)
        => _db.PipelineRuns.AnyAsync(x => x.ContentType == ContentType.SpecialEventGuide
            && x.EventId == eventId
            && x.RunDate == runDate
            && ((x.RegionId != null && x.RegionId == regionId) || (x.RegionId == null && x.LocationName == regionId))
            && statuses.Contains(x.Status), cancellationToken);

    public Task<bool> HasPipelineRunAsync(DateOnly runDate, ContentType contentType, string locationName, string timeZone, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken)
        => _db.PipelineRuns.AnyAsync(x => x.RunDate == runDate
            && x.ContentType == contentType
            && ((x.RegionId != null && x.RegionId == locationName) || (x.RegionId == null && x.LocationName == locationName))
            && statuses.Contains(x.Status), cancellationToken);

    public async Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken)
        => await _db.GeneratedScripts.AddAsync(script, cancellationToken);

    public async Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken)
        => await _db.GeneratedScripts.OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(cancellationToken);

    public async Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken)
        => await _db.MediaAssets.AddAsync(asset, cancellationToken);

    public async Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken)
        => await _db.PublishedVideos.AddAsync(publishedVideo, cancellationToken);

    public async Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken)
        => await _db.ShortVideos.AddAsync(shortVideo, cancellationToken);

    public async Task AddMonetizationRecordAsync(MonetizationRecord monetizationRecord, CancellationToken cancellationToken)
        => await _db.MonetizationRecords.AddAsync(monetizationRecord, cancellationToken);

    public async Task AddPlatformPublicationRecordAsync(PlatformPublicationRecord record, CancellationToken cancellationToken)
        => await _db.PlatformPublicationRecords.AddAsync(record, cancellationToken);

    public async Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken)
        => await _db.PipelineJobs.AddAsync(job, cancellationToken);

    public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken)
        => _db.PipelineJobs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken)
        => await _db.PipelineJobs.OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(cancellationToken);

    public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => _db.PipelineJobs
            .Where(x => (x.Status == PipelineJobStatus.Pending && x.ScheduledAt <= now)
                || (x.Status == PipelineJobStatus.Retrying && x.NextAttemptAt <= now))
            .OrderBy(x => x.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken)
    {
        var hasJob = await _db.PipelineJobs.AnyAsync(x =>
            x.JobType == PipelineJobType.GenerateMainVideo
            && x.RunDate == runDate
            && x.ContentType == contentType
            && x.Status != PipelineJobStatus.Failed, cancellationToken);

        if (hasJob)
            return true;

        return await _db.PipelineRuns.AnyAsync(x =>
            x.RunDate == runDate
            && x.ContentType == contentType
            && x.Status == PipelineRunStatus.Succeeded, cancellationToken);
    }

    public async Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken)
        => await _db.VideoAnalytics.AddAsync(analytics, cancellationToken);

    public async Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken)
        => await _db.VideoAnalytics.AsNoTracking().OrderByDescending(x => x.RetrievedAt).Take(take).ToListAsync(cancellationToken);


    public async Task UpsertPlatformContentAnalyticsAsync(PlatformContentAnalytics analytics, CancellationToken cancellationToken)
    {
        var existing = await _db.PlatformContentAnalytics.FirstOrDefaultAsync(x =>
            x.Platform == analytics.Platform
            && x.PlatformContentType == analytics.PlatformContentType
            && x.PlatformMediaId == analytics.PlatformMediaId
            && x.CollectedUtc == analytics.CollectedUtc, cancellationToken);

        if (existing is null)
        {
            await _db.PlatformContentAnalytics.AddAsync(analytics, cancellationToken);
            return;
        }

        existing.PipelineRunId = analytics.PipelineRunId;
        existing.PlatformUrl = analytics.PlatformUrl;
        existing.Title = analytics.Title;
        existing.PublishedUtc = analytics.PublishedUtc;
        existing.Views = analytics.Views;
        existing.Likes = analytics.Likes;
        existing.Comments = analytics.Comments;
        existing.Shares = analytics.Shares;
        existing.Reach = analytics.Reach;
        existing.Impressions = analytics.Impressions;
        existing.WatchTimeMinutes = analytics.WatchTimeMinutes;
        existing.AverageViewDurationSeconds = analytics.AverageViewDurationSeconds;
        existing.Ctr = analytics.Ctr;
        existing.EngagementRate = analytics.EngagementRate;
        existing.DurationSeconds = analytics.DurationSeconds;
        existing.Hashtags = analytics.Hashtags;
        existing.RegionId = analytics.RegionId;
        existing.LocationName = analytics.LocationName;
        existing.TargetDate = analytics.TargetDate;
        existing.ContentCategory = analytics.ContentCategory;
        existing.ThumbnailPath = analytics.ThumbnailPath;
        existing.PerformanceScore = analytics.PerformanceScore;
        existing.IsAnalyticsAvailable = analytics.IsAnalyticsAvailable;
        existing.LastError = analytics.LastError;
    }

    public async Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsAsync(PlatformAnalyticsQuery query, CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(query.Days, 1, 365));
        var q = _db.PlatformContentAnalytics.AsNoTracking().Where(x => x.CollectedUtc >= from);
        if (!string.IsNullOrWhiteSpace(query.Platform))
            q = q.Where(x => x.Platform == query.Platform);
        if (!string.IsNullOrWhiteSpace(query.Location))
            q = q.Where(x => (x.RegionId != null && x.RegionId.Contains(query.Location)) || (x.LocationName != null && x.LocationName.Contains(query.Location)));
        if (!string.IsNullOrWhiteSpace(query.ContentType) && Enum.TryParse<ContentType>(query.ContentType, true, out var contentType))
            q = q.Where(x => x.ContentCategory == contentType);
        if (!string.IsNullOrWhiteSpace(query.ContentType) && !Enum.TryParse<ContentType>(query.ContentType, true, out _))
            q = q.Where(x => x.PlatformContentType == query.ContentType);

        return await q.OrderByDescending(x => x.CollectedUtc).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await _db.PlatformContentAnalytics.AsNoTracking().Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CollectedUtc).ToListAsync(cancellationToken);

    public async Task<AnalyticsDashboardSummary> GetAnalyticsDashboardSummaryAsync(int days, CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var analytics = await _db.PlatformContentAnalytics.AsNoTracking().Where(x => x.CollectedUtc >= from && x.IsAnalyticsAvailable).ToListAsync(cancellationToken);
        var top = analytics.OrderByDescending(EngagementValue).ThenByDescending(x => x.Views ?? 0).Take(10).ToArray();
        var bestPlatform = analytics.GroupBy(x => x.Platform).OrderByDescending(g => g.Sum(x => x.Views ?? 0)).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
        var bestReel = analytics.Where(x => x.PlatformContentType.Contains("reel", StringComparison.OrdinalIgnoreCase) || x.PlatformContentType.Contains("short", StringComparison.OrdinalIgnoreCase)).OrderByDescending(EngagementValue).FirstOrDefault();
        var bestHour = analytics.Where(x => x.PublishedUtc.HasValue).GroupBy(x => x.PublishedUtc!.Value.UtcDateTime.Hour).OrderByDescending(g => g.Sum(x => x.Views ?? 0)).Select(g => (int?)g.Key).FirstOrDefault();
        return new AnalyticsDashboardSummary(top, analytics.Sum(x => x.Views ?? 0), analytics.Sum(EngagementValue), bestPlatform, bestReel, bestHour);
    }

    private static long EngagementValue(PlatformContentAnalytics x) => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0);

    public async Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken)
    {
        var query = _db.VideoAnalytics.AsNoTracking();
        if (from.HasValue)
            query = query.Where(x => x.RetrievedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.RetrievedAt <= to.Value);

        return await query.OrderByDescending(x => x.RetrievedAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken)
        => await _db.VideoAnalytics.AsNoTracking().Where(x => x.VideoId == videoId).OrderByDescending(x => x.RetrievedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken)
    {
        var query = _db.VideoAnalytics.AsNoTracking().Where(x => x.ContentType == contentType);
        if (from.HasValue)
            query = query.Where(x => x.RetrievedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.RetrievedAt <= to.Value);

        return await query.OrderByDescending(x => x.Views).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken)
    {
        var query = _db.VideoAnalytics.AsNoTracking().Where(x => x.IsShort == shortsOnly);
        if (from.HasValue)
            query = query.Where(x => x.RetrievedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.RetrievedAt <= to.Value);

        return await query.OrderByDescending(x => x.Views).Take(take).ToListAsync(cancellationToken);
    }


    public async Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.PublishedVideos.AsNoTracking().Where(x => x.CreatedAt >= from).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.GeneratedScripts.AsNoTracking().Where(x => x.CreatedUtc >= from).OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.PublishedVideos.Where(x => x.CreatedAt >= from && x.YouTubeVideoId != null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.ShortVideos.Where(x => x.CreatedAt >= from && x.YouTubeVideoId != null).ToListAsync(cancellationToken);

    public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken)
        => _db.GeneratedScripts.Where(x => x.Title == title).OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<PlatformPublicationRecord?> GetPlatformPublicationRecordAsync(Guid id, CancellationToken cancellationToken)
        => _db.PlatformPublicationRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<PlatformPublicationRecord>> GetRecentPlatformPublicationRecordsAsync(int take, CancellationToken cancellationToken)
        => await _db.PlatformPublicationRecords.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByShortIdAsync(Guid shortVideoId, CancellationToken cancellationToken)
        => await _db.PlatformPublicationRecords.AsNoTracking().Where(x => x.ParentShortVideoId == shortVideoId).OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await (from record in _db.PlatformPublicationRecords.AsNoTracking()
                  join shortVideo in _db.ShortVideos.AsNoTracking() on record.ParentShortVideoId equals shortVideo.Id
                  join publishedVideo in _db.PublishedVideos.AsNoTracking() on shortVideo.ParentVideoId equals publishedVideo.Id
                  where publishedVideo.PipelineRunId == pipelineRunId
                  orderby record.CreatedUtc descending
                  select record).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PipelineStageExecution>> GetStageExecutionsAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await _db.PipelineStageExecutions.Where(x => x.PipelineRunId == pipelineRunId).OrderBy(x => x.CreatedUtc).ToListAsync(cancellationToken);

    public Task<PipelineStageExecution?> GetLatestStageExecutionAsync(Guid pipelineRunId, string stageName, CancellationToken cancellationToken)
        => _db.PipelineStageExecutions
            .Where(x => x.PipelineRunId == pipelineRunId && x.StageName == stageName)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddStageExecutionAsync(PipelineStageExecution stageExecution, CancellationToken cancellationToken)
        => await _db.PipelineStageExecutions.AddAsync(stageExecution, cancellationToken);

    public async Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await _db.PublishedVideos.AsNoTracking().Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
