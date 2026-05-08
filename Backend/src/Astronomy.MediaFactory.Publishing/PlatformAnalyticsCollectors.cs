using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeAnalyticsCollector : IYouTubeAnalyticsCollector
{
    private readonly IYouTubeAnalyticsService _analyticsService;

    public string Platform => "YouTube";

    public YouTubeAnalyticsCollector(IYouTubeAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    public async Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await _analyticsService.GetVideoAnalyticsAsync(context.PlatformMediaId, cancellationToken);
        if (snapshot is null)
            return FromContext(context, false, "YouTube analytics unavailable or permissions are missing.");

        var analytics = FromContext(context, true, null);
        analytics.Views = snapshot.Views;
        analytics.Likes = snapshot.Likes;
        analytics.Comments = snapshot.Comments;
        analytics.DurationSeconds = snapshot.DurationSeconds > 0 ? snapshot.DurationSeconds : context.DurationSeconds;
        analytics.AverageViewDurationSeconds = snapshot.AverageViewDurationSeconds;
        analytics.Ctr = snapshot.CtrPercent;
        analytics.WatchTimeMinutes = snapshot.EstimatedMinutesWatched;
        analytics.EngagementRate = ComputeEngagementRate(analytics);
        return analytics;
    }

    private static PlatformContentAnalytics FromContext(PlatformAnalyticsCollectionContext context, bool available, string? error) => AnalyticsFactory.FromContext(context, available, error);
    private static double? ComputeEngagementRate(PlatformContentAnalytics analytics) => AnalyticsFactory.ComputeEngagementRate(analytics);
}

public sealed class FacebookAnalyticsCollector : IFacebookAnalyticsCollector
{
    private readonly HttpClient _httpClient;
    private readonly MetaOptions _options;
    private readonly ILogger<FacebookAnalyticsCollector> _logger;

    public string Platform => "Facebook";

    public FacebookAnalyticsCollector(HttpClient httpClient, IOptions<MetaOptions> options, ILogger<FacebookAnalyticsCollector> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken)
    {
        var token = await ReadMetaAccessTokenAsync(_options, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return AnalyticsFactory.FromContext(context, false, "Facebook analytics token is missing.");

        try
        {
            var url = $"https://graph.facebook.com/v19.0/{Uri.EscapeDataString(context.PlatformMediaId)}/insights?metric=total_video_views,total_video_views_unique,total_video_reactions_by_type_total,total_video_stories_by_action_type&access_token={Uri.EscapeDataString(token)}";
            var document = await _httpClient.GetFromJsonAsync<GraphInsightsDocument>(url, cancellationToken);
            var analytics = AnalyticsFactory.FromContext(context, true, null);
            analytics.Views = ReadLong(document, "total_video_views");
            analytics.Reach = ReadLong(document, "total_video_views_unique");
            analytics.Likes = ReadObjectTotal(document, "total_video_reactions_by_type_total");
            analytics.Comments = ReadNestedValue(document, "total_video_stories_by_action_type", "comment");
            analytics.Shares = ReadNestedValue(document, "total_video_stories_by_action_type", "share");
            analytics.EngagementRate = AnalyticsFactory.ComputeEngagementRate(analytics);
            return analytics;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Facebook analytics collection failed for media {MediaId}.", context.PlatformMediaId);
            return AnalyticsFactory.FromContext(context, false, ex.Message);
        }
    }

    private static long? ReadLong(GraphInsightsDocument? doc, string metric)
    {
        var value = doc?.Data?.FirstOrDefault(x => x.Name == metric)?.Values.FirstOrDefault()?.Value;
        return value?.ValueKind == JsonValueKind.Number ? value.Value.GetInt64() : null;
    }

    private static long? ReadObjectTotal(GraphInsightsDocument? doc, string metric)
    {
        var value = doc?.Data?.FirstOrDefault(x => x.Name == metric)?.Values.FirstOrDefault()?.Value;
        return value?.ValueKind == JsonValueKind.Object ? value.Value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).Sum(p => p.Value.GetInt64()) : null;
    }

    private static long? ReadNestedValue(GraphInsightsDocument? doc, string metric, string property)
    {
        var value = doc?.Data?.FirstOrDefault(x => x.Name == metric)?.Values.FirstOrDefault()?.Value;
        return value?.ValueKind == JsonValueKind.Object && value.Value.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number ? prop.GetInt64() : null;
    }

    internal static async Task<string?> ReadMetaAccessTokenAsync(MetaOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.TokenFilePath) || !File.Exists(options.TokenFilePath))
            return null;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(options.TokenFilePath, cancellationToken));
        return FindString(document.RootElement, "page_access_token") ?? FindString(document.RootElement, "access_token");
    }

    internal static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var nested = FindString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        return null;
    }
}

public sealed class InstagramAnalyticsCollector : IInstagramAnalyticsCollector
{
    private readonly HttpClient _httpClient;
    private readonly MetaOptions _options;
    private readonly ILogger<InstagramAnalyticsCollector> _logger;

    public string Platform => "Instagram";

    public InstagramAnalyticsCollector(HttpClient httpClient, IOptions<MetaOptions> options, ILogger<InstagramAnalyticsCollector> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken)
    {
        var token = await FacebookAnalyticsCollector.ReadMetaAccessTokenAsync(_options, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return AnalyticsFactory.FromContext(context, false, "Instagram analytics token is missing.");

        try
        {
            var url = $"https://graph.facebook.com/v19.0/{Uri.EscapeDataString(context.PlatformMediaId)}/insights?metric=impressions,reach,likes,comments,saved,shares,plays&access_token={Uri.EscapeDataString(token)}";
            var document = await _httpClient.GetFromJsonAsync<GraphInsightsDocument>(url, cancellationToken);
            var analytics = AnalyticsFactory.FromContext(context, true, null);
            analytics.Impressions = ReadLong(document, "impressions");
            analytics.Reach = ReadLong(document, "reach");
            analytics.Likes = ReadLong(document, "likes");
            analytics.Comments = ReadLong(document, "comments");
            analytics.Shares = ReadLong(document, "shares");
            analytics.Views = ReadLong(document, "plays");
            analytics.EngagementRate = AnalyticsFactory.ComputeEngagementRate(analytics);
            return analytics;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Instagram analytics collection failed for media {MediaId}.", context.PlatformMediaId);
            return AnalyticsFactory.FromContext(context, false, ex.Message);
        }
    }

    private static long? ReadLong(GraphInsightsDocument? doc, string metric)
    {
        var value = doc?.Data?.FirstOrDefault(x => x.Name == metric)?.Values.FirstOrDefault()?.Value;
        return value?.ValueKind == JsonValueKind.Number ? value.Value.GetInt64() : null;
    }
}

internal static class AnalyticsFactory
{
    public static PlatformContentAnalytics FromContext(PlatformAnalyticsCollectionContext context, bool available, string? error) => new()
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
        LocationName = context.LocationName,
        TargetDate = context.TargetDate,
        ContentCategory = context.ContentCategory,
        ThumbnailPath = context.ThumbnailPath,
        IsAnalyticsAvailable = available,
        LastError = error
    };

    public static double? ComputeEngagementRate(PlatformContentAnalytics analytics)
    {
        var denominator = analytics.Views ?? analytics.Reach ?? analytics.Impressions;
        if (!denominator.HasValue || denominator.Value <= 0)
            return null;
        return ((analytics.Likes ?? 0) + (analytics.Comments ?? 0) + (analytics.Shares ?? 0)) / (double)denominator.Value;
    }
}

internal sealed record GraphInsightsDocument([property: JsonPropertyName("data")] List<GraphInsightMetric>? Data);
internal sealed record GraphInsightMetric([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("values")] List<GraphInsightValue> Values);
internal sealed record GraphInsightValue([property: JsonPropertyName("value")] JsonElement Value);
