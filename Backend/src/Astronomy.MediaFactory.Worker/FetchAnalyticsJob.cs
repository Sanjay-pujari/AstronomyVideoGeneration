using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Astronomy.MediaFactory.Worker;

public sealed class FetchAnalyticsJob : IJob
{
    private readonly IPipelineRepository _repository;
    private readonly IYouTubeAnalyticsService _analyticsService;
    private readonly AnalyticsOptions _options;
    private readonly ILogger<FetchAnalyticsJob> _logger;

    public FetchAnalyticsJob(
        IPipelineRepository repository,
        IYouTubeAnalyticsService analyticsService,
        IOptions<AnalyticsOptions> options,
        ILogger<FetchAnalyticsJob> logger)
    {
        _repository = repository;
        _analyticsService = analyticsService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var videos = await _repository.GetPublishedVideosWithYouTubeIdAsync(since, context.CancellationToken);
        var shorts = await _repository.GetShortVideosWithYouTubeIdAsync(since, context.CancellationToken);
        var parentMap = videos.ToDictionary(x => x.Id, x => x.YouTubeVideoId, EqualityComparer<Guid>.Default);

        foreach (var video in videos)
        {
            await SaveSnapshotAsync(videoId: video.YouTubeVideoId!, title: video.Title, isShort: false, contentType: video.ContentType(), parentVideoId: null, hookLine: null, cancellationToken: context.CancellationToken);
        }

        foreach (var shortVideo in shorts)
        {
            parentMap.TryGetValue(shortVideo.ParentVideoId, out var parentYouTubeId);
            await SaveSnapshotAsync(videoId: shortVideo.YouTubeVideoId!, title: null, isShort: true, contentType: ContentType.DailySkyGuide, parentVideoId: parentYouTubeId, hookLine: null, cancellationToken: context.CancellationToken);
        }

        await _repository.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Analytics fetch completed. TopN setting is {TopN}", _options.TopN);
    }

    private async Task SaveSnapshotAsync(string videoId, string? title, bool isShort, ContentType contentType, string? parentVideoId, string? hookLine, CancellationToken cancellationToken)
    {
        var snapshot = await _analyticsService.GetVideoAnalyticsAsync(videoId, cancellationToken);
        if (snapshot is null)
            return;

        await _repository.AddVideoAnalyticsAsync(new VideoAnalytics
        {
            VideoId = snapshot.VideoId,
            Views = snapshot.Views,
            Likes = snapshot.Likes,
            Comments = snapshot.Comments,
            DurationSeconds = snapshot.DurationSeconds,
            AverageViewDurationSeconds = snapshot.AverageViewDurationSeconds,
            CtrPercent = snapshot.CtrPercent,
            RetrievedAt = DateTimeOffset.UtcNow,
            ContentType = contentType,
            IsShort = isShort,
            ParentVideoId = parentVideoId,
            Title = title,
            HookLine = hookLine
        }, cancellationToken);
    }
}

internal static class PublishedVideoContentTypeExtensions
{
    public static ContentType ContentType(this PublishedVideo video)
    {
        if (Enum.TryParse<ContentType>(video.OptimizedTagsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty, true, out var parsed))
            return parsed;

        return ContentType.DailySkyGuide;
    }
}
