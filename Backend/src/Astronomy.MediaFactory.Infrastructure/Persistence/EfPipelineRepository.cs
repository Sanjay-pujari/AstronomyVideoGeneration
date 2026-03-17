using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.PublishedVideos.Where(x => x.CreatedAt >= from && x.YouTubeVideoId != null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken)
        => await _db.ShortVideos.Where(x => x.CreatedAt >= from && x.YouTubeVideoId != null).ToListAsync(cancellationToken);

    public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken)
        => _db.GeneratedScripts.Where(x => x.Title == title).OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
