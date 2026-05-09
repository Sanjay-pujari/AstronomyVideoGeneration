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
    private readonly IContentExperimentService _contentExperimentService;
    private readonly AnalyticsOptions _options;
    private readonly ILogger<FetchAnalyticsJob> _logger;

    public FetchAnalyticsJob(
        IPipelineRepository repository,
        IYouTubeAnalyticsService analyticsService,
        IContentExperimentService contentExperimentService,
        IOptions<AnalyticsOptions> options,
        ILogger<FetchAnalyticsJob> logger)
    {
        _repository = repository;
        _analyticsService = analyticsService;
        _contentExperimentService = contentExperimentService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var videos = await _repository.GetPublishedVideosWithYouTubeIdAsync(since, context.CancellationToken);
        var shorts = await _repository.GetShortVideosWithYouTubeIdAsync(since, context.CancellationToken);
        var parentMap = videos.ToDictionary(x => x.Id, x => x.YouTubeVideoId, EqualityComparer<Guid>.Default);
        var parentEventMap = videos.ToDictionary(x => x.Id, x => (EventId: x.EventId, EventType: x.EventType, EventTitle: x.EventTitle), EqualityComparer<Guid>.Default);

        foreach (var video in videos)
        {
            var assignments = await _contentExperimentService.ResolveAssignmentsAsync(video.Id, context.CancellationToken);
            await SaveSnapshotAsync(
                videoId: video.YouTubeVideoId!,
                title: video.Title,
                isShort: false,
                contentType: video.ResolveContentType(),
                parentVideoId: null,
                hookLine: null,
                publishedVideoId: video.Id,
                assignments: assignments,
                eventId: video.EventId,
                eventType: video.EventType,
                eventTitle: video.EventTitle,
                cancellationToken: context.CancellationToken);
        }

        foreach (var shortVideo in shorts)
        {
            parentMap.TryGetValue(shortVideo.ParentVideoId, out var parentYouTubeId);
            parentEventMap.TryGetValue(shortVideo.ParentVideoId, out var parentEvent);
            await SaveSnapshotAsync(
                videoId: shortVideo.YouTubeVideoId!,
                title: null,
                isShort: true,
                contentType: ContentType.DailySkyGuide,
                parentVideoId: parentYouTubeId,
                hookLine: null,
                publishedVideoId: null,
                assignments: new ExperimentVariantAssignment(),
                eventId: parentEvent.EventId,
                eventType: parentEvent.EventType,
                eventTitle: parentEvent.EventTitle,
                cancellationToken: context.CancellationToken);
        }

        await _repository.SaveChangesAsync(context.CancellationToken);
        await _contentExperimentService.EvaluateRecentExperimentsAsync(context.CancellationToken);
        _logger.LogInformation("Analytics fetch completed. TopN setting is {TopN}", _options.TopN);
    }

    private async Task SaveSnapshotAsync(string videoId, string? title, bool isShort, ContentType contentType, string? parentVideoId, string? hookLine, Guid? publishedVideoId, ExperimentVariantAssignment assignments, string? eventId, string? eventType, string? eventTitle, CancellationToken cancellationToken)
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
            HookLine = hookLine,
            PublishedVideoId = publishedVideoId,
            TitleExperimentId = assignments.TitleExperimentId,
            TitleVariantId = assignments.TitleVariantId,
            ThumbnailExperimentId = assignments.ThumbnailExperimentId,
            ThumbnailVariantId = assignments.ThumbnailVariantId,
            CtaExperimentId = assignments.CtaExperimentId,
            CtaVariantId = assignments.CtaVariantId,
            EventId = eventId,
            EventType = eventType,
            EventTitle = eventTitle
        }, cancellationToken);
    }
}

internal static class PublishedVideoContentTypeExtensions
{
    public static ContentType ResolveContentType(this PublishedVideo video)
    {
        if (Enum.TryParse<ContentType>(video.OptimizedTagsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty, true, out var parsed))
            return parsed;

        return ContentType.DailySkyGuide;
    }
}
