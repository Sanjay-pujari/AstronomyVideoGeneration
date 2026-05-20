using Astronomy.MediaFactory.Analytics;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class ManualAnalyticsIngestionService : IAnalyticsIngestionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManualAnalyticsIngestionService> _logger;
    public ManualAnalyticsIngestionService(IServiceScopeFactory scopeFactory, ILogger<ManualAnalyticsIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task IngestManualAsync(IReadOnlyCollection<AnalyticsIngestionDto> records, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
        _logger.LogInformation("Analytics ingestion scope created. DbContextHash={DbContextHash}", db.GetHashCode());

        foreach (var r in records)
        {
            db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics
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

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Analytics ingestion scope disposed. DbContextHash={DbContextHash}", db.GetHashCode());
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


        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
        _logger.LogInformation("Analytics initialization scope created. DbContextHash={DbContextHash}; PipelineRunId={PipelineRunId}", db.GetHashCode(), request.PipelineRunId);

        var expectedColumns = new[] { "Id", "PipelineRunId", "Platform", "ContentType", "ThumbnailType", "ThumbnailPath", "Language", "RegionId", "PublishedAtUtc", "UpdatedUtc" };
        var detectedColumns = await GetThumbnailPerformanceColumnsAsync(db, cancellationToken);
        var missingColumns = expectedColumns.Where(c => !detectedColumns.Contains(c, StringComparer.Ordinal)).ToArray();
        var schemaMismatchDetected = missingColumns.Length > 0;
        _logger.LogInformation("Thumbnail analytics schema diagnostics. expected={ExpectedColumns}; detected={DetectedColumns}; missing={MissingColumns}; migrationRequired={MigrationRequired}", expectedColumns, detectedColumns, missingColumns, schemaMismatchDetected);

        foreach (var platform in platforms.DefaultIfEmpty("YouTube"))
        {
            var existingVideo = await db.PlatformVideoAnalytics.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == request.ContentType, cancellationToken);
            if (existingVideo is null) db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics
            {
                PipelineRunId = request.PipelineRunId,
                Platform = platform,
                ContentType = request.ContentType,
                Language = request.Language,
                RegionId = request.RegionId,
                PublishedAtUtc = publishedAtUtc
            });
            var existingPlatformContent = await db.PlatformContentAnalytics.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.PlatformContentType == request.ContentType, cancellationToken);
            if (existingPlatformContent is null)
            {
                db.PlatformContentAnalytics.Add(new PlatformContentAnalytics
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
                var existingHook = await db.HookPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == hookContentType, cancellationToken);
                if (existingHook is null) db.HookPerformance.Add(new HookPerformance
                {
                    PipelineRunId = request.PipelineRunId,
                    Platform = platform,
                    ContentType = hookContentType,
                    Language = request.Language,
                    RegionId = request.RegionId,
                    PublishedAtUtc = publishedAtUtc
                });
            }

            try
            {
                foreach (var thumbnail in thumbnails)
                {
                    var thumbContentType = $"{request.ContentType}|{thumbnail.ThumbnailType}";
                    var existingThumb = await db.ThumbnailPerformance.FirstOrDefaultAsync(x => x.PipelineRunId == request.PipelineRunId && x.Platform == platform && x.ContentType == thumbContentType, cancellationToken);
                    if (existingThumb is null) db.ThumbnailPerformance.Add(new ThumbnailPerformance
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail analytics initialization failed for pipeline run {PipelineRunId}. Continuing with non-thumbnail analytics initialization.", request.PipelineRunId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Analytics initialization materialized entries. Platforms={PlatformCount}; Hooks={HookCount}; Thumbnails={ThumbnailCount}", platforms.Length, hooks.Length, thumbnails.Length);
        _logger.LogInformation("Analytics initialization scope disposed. DbContextHash={DbContextHash}; PipelineRunId={PipelineRunId}", db.GetHashCode(), request.PipelineRunId);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : value.ToUniversalTime();


    private async Task<string[]> GetThumbnailPerformanceColumnsAsync(MediaFactoryDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'thumbnail_performance'";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));

        var normalized = columns.Select(x => x.StartsWith(char.ToUpperInvariant(x[0])) ? x : char.ToUpperInvariant(x[0]) + x[1..]).ToArray();
        return normalized;
    }
}
