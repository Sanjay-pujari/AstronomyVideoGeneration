using Astronomy.MediaFactory.Analytics;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class ManualAnalyticsIngestionService : IAnalyticsIngestionService
{
    private readonly MediaFactoryDbContext _db;
    public ManualAnalyticsIngestionService(MediaFactoryDbContext db) => _db = db;

    public async Task IngestManualAsync(IReadOnlyCollection<AnalyticsIngestionDto> records, CancellationToken cancellationToken)
    {
        foreach (var r in records)
        {
            _db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics
            {
                PipelineRunId = r.PipelineRunId,
                Impressions = r.Impressions,
                Views = r.Views,
                Ctr = r.Ctr,
                AverageWatchDuration = r.AverageWatchDuration,
                WatchTimeMinutes = r.WatchTimeMinutes,
                Likes = r.Likes,
                Comments = r.Comments,
                Shares = r.Shares,
                SubscribersGained = r.SubscribersGained,
                Platform = r.Platform,
                ContentType = r.ContentType,
                Language = r.Language,
                RegionId = r.RegionId,
                PublishedAtUtc = EnsureUtc(r.PublishedAtUtc)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task InitializeForPipelineRunAsync(AnalyticsPipelineInitializationRequest request, CancellationToken cancellationToken)
    {
        var publishedAtUtc = EnsureUtc(request.PublishedAtUtc);

        foreach (var platform in request.Platforms.DefaultIfEmpty("YouTube"))
        {
            var existingVideo = await _db.PlatformVideoAnalytics.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == request.ContentType, cancellationToken);
            if (existingVideo is null) _db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics
            {
                PipelineRunId = request.PipelineRunId,
                Platform = platform,
                ContentType = request.ContentType,
                Language = request.Language,
                RegionId = request.RegionId,
                PublishedAtUtc = publishedAtUtc
            });

            foreach (var hook in request.HookTexts.DefaultIfEmpty(string.Empty))
            {
                var existingHook = await _db.HookPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == request.ContentType, cancellationToken);
                if (existingHook is null) _db.HookPerformance.Add(new HookPerformance
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    ContentType = request.ContentType,
                    Language = request.Language,
                    RegionId = request.RegionId,
                    PublishedAtUtc = publishedAtUtc
                });
            }

            foreach (var thumbnail in request.Thumbnails)
            {
                var existingThumb = await _db.ThumbnailPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == thumbnail.ThumbnailType, cancellationToken);
                if (existingThumb is null) _db.ThumbnailPerformance.Add(new ThumbnailPerformance
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    ContentType = request.ContentType,
                    Language = request.Language,
                    RegionId = request.RegionId,
                    PublishedAtUtc = publishedAtUtc
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : value.ToUniversalTime();
}
