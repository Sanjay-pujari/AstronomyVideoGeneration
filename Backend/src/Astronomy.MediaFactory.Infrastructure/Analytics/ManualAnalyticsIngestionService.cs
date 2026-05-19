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
        var platforms = request.Platforms
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hooks = request.HookTexts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var thumbnails = request.Thumbnails
            .GroupBy(x => $"{x.ThumbnailPath}|{x.ThumbnailType}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

        foreach (var platform in platforms.DefaultIfEmpty("YouTube"))
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
            var existingPlatformContent = await _db.PlatformContentAnalytics.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.PlatformContentType == request.ContentType, cancellationToken);
            if (existingPlatformContent is null)
            {
                _db.PlatformContentAnalytics.Add(new PlatformContentAnalytics
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    PlatformContentType = request.ContentType,
                    Language = request.Language,
                    RegionId = request.RegionId,
                    PublishedUtc = publishedAtUtc,
                    CollectedUtc = publishedAtUtc,
                    IsAnalyticsAvailable = true,
                    Impressions = 0,
                    Views = 0,
                    Likes = 0,
                    Comments = 0,
                    Shares = 0
                });
            }

            foreach (var hook in hooks.DefaultIfEmpty(request.ContentType))
            {
                var hookContentType = $"{request.ContentType}|{hook}";
                var existingHook = await _db.HookPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == hookContentType, cancellationToken);
                if (existingHook is null) _db.HookPerformance.Add(new HookPerformance
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    ContentType = hookContentType,
                    Language = request.Language,
                    RegionId = request.RegionId,
                    PublishedAtUtc = publishedAtUtc
                });
            }

            foreach (var thumbnail in thumbnails)
            {
                var thumbContentType = $"{request.ContentType}|{thumbnail.ThumbnailType}";
                var existingThumb = await _db.ThumbnailPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == thumbContentType, cancellationToken);
                if (existingThumb is null) _db.ThumbnailPerformance.Add(new ThumbnailPerformance
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    ContentType = thumbContentType,
                    ThumbnailType = thumbnail.ThumbnailType,
                    ThumbnailPath = thumbnail.ThumbnailPath,
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
