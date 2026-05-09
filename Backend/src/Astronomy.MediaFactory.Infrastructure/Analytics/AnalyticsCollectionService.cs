using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class AnalyticsCollectionService : IAnalyticsCollectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly MediaFactoryDbContext _db;
    private readonly IPipelineRepository _repository;
    private readonly IEnumerable<IPlatformAnalyticsCollector> _collectors;
    private readonly AnalyticsOptions _options;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<AnalyticsCollectionService> _logger;

    public AnalyticsCollectionService(
        MediaFactoryDbContext db,
        IPipelineRepository repository,
        IEnumerable<IPlatformAnalyticsCollector> collectors,
        IOptions<AnalyticsOptions> options,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<AnalyticsCollectionService> logger)
    {
        _db = db;
        _repository = repository;
        _collectors = collectors;
        _options = options.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
    }

    public async Task CollectRecentAnalyticsAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(_options.CollectForRecentDays, 1, 365));
        var runs = await _db.PipelineRuns.AsNoTracking()
            .Where(x => (x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc) >= from)
            .OrderByDescending(x => x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc)
            .ToListAsync(cancellationToken);

        foreach (var run in runs)
            await CollectForPipelineRunAsync(run.Id, cancellationToken);
    }

    public async Task CollectForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var contexts = await BuildContextsAsync(pipelineRunId, cancellationToken);
        var reports = new List<AnalyticsCollectionReport>();
        foreach (var group in contexts.GroupBy(x => x.Platform, StringComparer.OrdinalIgnoreCase))
        {
            var collector = _collectors.FirstOrDefault(x => x.Platform.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            var success = 0;
            var failures = 0;
            var warnings = new List<string>();
            if (collector is null)
            {
                warnings.Add($"No analytics collector registered for {group.Key}.");
                failures += group.Count();
            }
            else
            {
                foreach (var context in group)
                {
                    try
                    {
                        var analytics = await collector.CollectAsync(context, cancellationToken);
                        analytics.RegionId = context.RegionId;
                        analytics.Language = context.Language;
                        analytics.CtaVariant = context.CtaVariant;
                        analytics.AffiliateBlockEnabled = context.AffiliateBlockEnabled;
                        analytics.LocationName ??= context.LocationName;
                        await _repository.UpsertPlatformContentAnalyticsAsync(analytics, cancellationToken);
                        if (analytics.IsAnalyticsAvailable) success++; else failures++;
                        if (!string.IsNullOrWhiteSpace(analytics.LastError)) warnings.Add($"{context.PlatformMediaId}: {analytics.LastError}");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures++;
                        warnings.Add($"{context.PlatformMediaId}: {ex.Message}");
                        var failed = new PlatformContentAnalytics
                        {
                            PipelineRunId = context.PipelineRunId,
                            Platform = context.Platform,
                            PlatformContentType = context.PlatformContentType,
                            PlatformMediaId = context.PlatformMediaId,
                            PlatformUrl = context.PlatformUrl,
                            Title = context.Title,
                            PublishedUtc = context.PublishedUtc,
                            CollectedUtc = DateTimeOffset.UtcNow,
                            DurationSeconds = context.DurationSeconds,
                            Hashtags = context.Hashtags,
                            RegionId = context.RegionId,
                            Language = context.Language,
                            CtaVariant = context.CtaVariant,
                            AffiliateBlockEnabled = context.AffiliateBlockEnabled,
                            LocationName = context.LocationName,
                            TargetDate = context.TargetDate,
                            ContentCategory = context.ContentCategory,
                            ThumbnailPath = context.ThumbnailPath,
                            IsAnalyticsAvailable = false,
                            LastError = ex.Message
                        };
                        await _repository.UpsertPlatformContentAnalyticsAsync(failed, cancellationToken);
                        _logger.LogWarning(ex, "Analytics collection failed for {Platform} media {MediaId}.", context.Platform, context.PlatformMediaId);
                    }
                }
            }

            reports.Add(new AnalyticsCollectionReport(group.Key, group.Count(), success, failures, DateTimeOffset.UtcNow, warnings.Distinct().ToArray()));
        }

        await _repository.SaveChangesAsync(cancellationToken);
        await WriteReportAsync(pipelineRunId, reports, cancellationToken);
    }

    private async Task<List<PlatformAnalyticsCollectionContext>> BuildContextsAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _db.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pipelineRunId, cancellationToken);
        if (run is null)
            return [];

        var outputDirectory = !string.IsNullOrWhiteSpace(run.OutputFolder)
            ? run.OutputFolder!
            : Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
        var scripts = await _db.GeneratedScripts.AsNoTracking().Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
        var script = scripts.FirstOrDefault();
        var publishedVideos = await _db.PublishedVideos.AsNoTracking().Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(cancellationToken);
        var shorts = await (from shortVideo in _db.ShortVideos.AsNoTracking()
                            join publishedVideo in _db.PublishedVideos.AsNoTracking() on shortVideo.ParentVideoId equals publishedVideo.Id
                            where publishedVideo.PipelineRunId == pipelineRunId
                            select new { Short = shortVideo, Parent = publishedVideo }).ToListAsync(cancellationToken);
        var platformRecords = await (from record in _db.PlatformPublicationRecords.AsNoTracking()
                                     join shortVideo in _db.ShortVideos.AsNoTracking() on record.ParentShortVideoId equals shortVideo.Id
                                     join publishedVideo in _db.PublishedVideos.AsNoTracking() on shortVideo.ParentVideoId equals publishedVideo.Id
                                     where publishedVideo.PipelineRunId == pipelineRunId && record.Status == PlatformPublicationStatus.Published
                                     select new { Record = record, Short = shortVideo, Parent = publishedVideo }).ToListAsync(cancellationToken);
        var contexts = new List<PlatformAnalyticsCollectionContext>();

        foreach (var video in publishedVideos.Where(x => !string.IsNullOrWhiteSpace(x.YouTubeVideoId)))
            contexts.Add(Build(run, "YouTube", "LongVideo", video.YouTubeVideoId!, BuildYouTubeUrl(video.YouTubeVideoId), video.Title, video.CreatedAt, null, script, video.ThumbnailPath, outputDirectory));

        foreach (var item in shorts.Where(x => !string.IsNullOrWhiteSpace(x.Short.YouTubeVideoId)))
            contexts.Add(Build(run, "YouTube", "Short", item.Short.YouTubeVideoId!, BuildYouTubeUrl(item.Short.YouTubeVideoId), item.Parent.Title, item.Short.CreatedAt, item.Short.Duration, script, item.Parent.ThumbnailPath, outputDirectory));

        AddJsonContext(contexts, run, outputDirectory, "youtube-publish-result-long.json", "YouTube", "LongVideo", script);
        AddJsonContext(contexts, run, outputDirectory, "youtube-publish-result-short.json", "YouTube", "Short", script);
        AddJsonContext(contexts, run, outputDirectory, "facebook-reel-publish-result.json", "Facebook", "Reel", script);
        AddJsonContext(contexts, run, outputDirectory, "instagram-reel-publish-result.json", "Instagram", "Reel", script);

        foreach (var item in platformRecords.Where(x => !string.IsNullOrWhiteSpace(x.Record.ExternalPostId)))
        {
            var platform = item.Record.Platform == ShortFormPlatform.InstagramReels ? "Instagram" : item.Record.Platform == ShortFormPlatform.Facebook ? "Facebook" : "YouTube";
            contexts.Add(Build(run, platform, item.Record.Platform == ShortFormPlatform.YouTubeShorts ? "Short" : "Reel", item.Record.ExternalPostId!, item.Record.ExternalUrl, item.Parent.Title, item.Record.PublishedAt, item.Short.Duration, script, item.Parent.ThumbnailPath, outputDirectory));
        }

        return contexts
            .Where(x => !string.IsNullOrWhiteSpace(x.PlatformMediaId))
            .GroupBy(x => new { x.Platform, x.PlatformContentType, x.PlatformMediaId })
            .Select(x => x.First())
            .ToList();
    }

    private static PlatformAnalyticsCollectionContext Build(PipelineRun run, string platform, string contentType, string mediaId, string? url, string? title, DateTimeOffset? publishedUtc, int? duration, GeneratedScript? script, string? thumbnailPath, string? outputDirectory)
    {
        var growthMetadata = ReadGrowthMetadata(outputDirectory);
        return new(run.Id, platform, contentType, mediaId, url, title ?? script?.Title, publishedUtc, duration ?? script?.EstimatedDurationSeconds, script?.OptimizedHashtagsCsv ?? script?.TagsCsv, run.RegionId, run.Language, run.LocationName, run.RunDate, run.ContentType, thumbnailPath, outputDirectory, growthMetadata?.CtaVariant, growthMetadata?.AffiliateBlockEnabled);
    }

    private static void AddJsonContext(List<PlatformAnalyticsCollectionContext> contexts, PipelineRun run, string outputDirectory, string fileName, string platform, string contentType, GeneratedScript? script)
    {
        var path = Path.Combine(outputDirectory, fileName);
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var id = ReadString(root, "videoId") ?? ReadString(root, "mediaId") ?? ReadString(root, "postId") ?? ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(id)) return;
            var url = ReadString(root, "videoUrl") ?? ReadString(root, "permalink") ?? ReadString(root, "permalinkUrl") ?? ReadString(root, "url");
            var published = ReadDate(root, "publishedUtc") ?? ReadDate(root, "timestamp");
            contexts.Add(Build(run, platform, contentType, id, url, ReadString(root, "title") ?? script?.Title, published, null, script, null, outputDirectory));
        }
        catch (JsonException)
        {
            // Invalid publish diagnostics should not affect pipeline or analytics scheduling.
        }
    }

    private static GrowthMetadata? ReadGrowthMetadata(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, "growth-metadata.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GrowthMetadata>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                var nested = ReadString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
        => DateTimeOffset.TryParse(ReadString(element, name), out var value) ? value : null;

    private static string? BuildYouTubeUrl(string? videoId) => string.IsNullOrWhiteSpace(videoId) ? null : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";

    private async Task WriteReportAsync(Guid pipelineRunId, IReadOnlyCollection<AnalyticsCollectionReport> reports, CancellationToken cancellationToken)
    {
        var run = await _db.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pipelineRunId, cancellationToken);
        var outputDirectory = run?.OutputFolder ?? _maintenanceOptions.WorkingDirectory;
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "analytics-collection-report.json"), JsonSerializer.Serialize(reports, JsonOptions), cancellationToken);
    }
}
