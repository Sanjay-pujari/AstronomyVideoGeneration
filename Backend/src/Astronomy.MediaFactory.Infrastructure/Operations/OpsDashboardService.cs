using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class OpsDashboardService : IOpsDashboardService
{
    private const int RecentRunsLimit = 20;
    private static readonly string[] PublishingStageNames =
    [
        PipelineStageNames.YouTubeLongPublished,
        PipelineStageNames.YouTubeShortPublished,
        PipelineStageNames.FacebookReelPublished,
        PipelineStageNames.InstagramReelPublished
    ];

    private readonly MediaFactoryDbContext _db;
    private readonly IPipelineSchedulerService _scheduler;
    private readonly ITokenHealthService _tokenHealth;
    private readonly RenderingOptions _renderingOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly StellariumOptions _stellariumOptions;
    private readonly SkyfieldSidecarOptions _skyfieldOptions;
    private readonly AzureBlobOptions _azureBlobOptions;
    private readonly HttpClient _httpClient;

    public OpsDashboardService(
        MediaFactoryDbContext db,
        IPipelineSchedulerService scheduler,
        ITokenHealthService tokenHealth,
        IOptions<RenderingOptions> renderingOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        IOptions<StellariumOptions> stellariumOptions,
        IOptions<SkyfieldSidecarOptions> skyfieldOptions,
        IOptions<AzureBlobOptions> azureBlobOptions,
        HttpClient httpClient)
    {
        _db = db;
        _scheduler = scheduler;
        _tokenHealth = tokenHealth;
        _renderingOptions = renderingOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _stellariumOptions = stellariumOptions.Value;
        _skyfieldOptions = skyfieldOptions.Value;
        _azureBlobOptions = azureBlobOptions.Value;
        _httpClient = httpClient;
    }

    public async Task<OpsDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var recentRuns = await GetRunsAsync(null, "all", cancellationToken);
        if (recentRuns.Count == 0)
            warnings.Add("No recent pipeline runs were found.");

        var scheduler = await BuildSchedulerSummaryAsync(cancellationToken);
        warnings.AddRange(scheduler.Warnings);

        var latestRunId = recentRuns.OrderByDescending(x => x.StartedUtc ?? DateTimeOffset.MinValue).FirstOrDefault()?.RunId;
        var publishSummary = latestRunId.HasValue
            ? await BuildPublishSummaryAsync(latestRunId.Value, cancellationToken)
            : EmptyPublishSummary("No recent pipeline run is available for publish summary.");
        warnings.AddRange(publishSummary.Warnings);

        var tokenSummary = await BuildTokenHealthSummaryAsync(cancellationToken);
        warnings.AddRange(tokenSummary.Warnings);

        var systemSummary = await BuildSystemHealthSummaryAsync(cancellationToken);
        warnings.AddRange(systemSummary.Warnings);

        var failures = await GetFailuresAsync(7, cancellationToken);
        var performance = await BuildPerformanceSummaryAsync(cancellationToken);
        warnings.AddRange(performance.Warnings);

        var analytics = await BuildAnalyticsOpsDataAsync(cancellationToken);
        var analyticsSummary = analytics.Summary;
        var analyticsIntelligence = analytics.Intelligence;

        var diagnostics = BuildDiagnosticsSummary();
        warnings.AddRange(diagnostics.Warnings);

        return new OpsDashboardResponse(
            scheduler,
            recentRuns,
            publishSummary,
            tokenSummary,
            systemSummary,
            failures,
            performance,
            analyticsSummary,
            analyticsIntelligence,
            await BuildRegionBreakdownAsync(cancellationToken),
            diagnostics,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<IReadOnlyCollection<OpsPipelineRunSummary>> GetRunsAsync(DateOnly? date, string? status, CancellationToken cancellationToken)
    {
        var query = _db.PipelineRuns.AsNoTracking().AsQueryable();
        if (date.HasValue)
            query = query.Where(x => x.RunDate == date.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<PipelineRunStatus>(status, ignoreCase: true, out var parsedStatus))
                query = query.Where(x => x.Status == parsedStatus);
            else
                return [];
        }

        var runs = await query
            .OrderByDescending(x => x.StartedUtc ?? x.CreatedUtc)
            .Take(RecentRunsLimit)
            .ToListAsync(cancellationToken);

        return await SummarizeRunsAsync(runs, cancellationToken);
    }

    public async Task<OpsPipelineRunDetail?> GetRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _db.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        var stages = await _db.PipelineStageExecutions.AsNoTracking()
            .Where(x => x.PipelineRunId == pipelineRunId)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(cancellationToken);

        var summary = SummarizeRun(run, stages);
        var publishSummary = await BuildPublishSummaryAsync(pipelineRunId, cancellationToken);
        var warnings = new List<string>();
        if (stages.Count == 0)
            warnings.Add("No stage executions were found for this pipeline run.");
        warnings.AddRange(publishSummary.Warnings);

        return new OpsPipelineRunDetail(summary, publishSummary, stages, warnings);
    }

    public async Task<FailureOpsSummary> GetFailuresAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-days);
        var from24 = now.AddHours(-24);
        var from7 = now.AddDays(-7);

        var failedRuns = await _db.PipelineRuns.AsNoTracking()
            .Where(x => x.Status == PipelineRunStatus.Failed && (x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc) >= from)
            .OrderByDescending(x => x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc)
            .Take(RecentRunsLimit)
            .ToListAsync(cancellationToken);
        var summaries = await SummarizeRunsAsync(failedRuns, cancellationToken);

        var failedStages = await _db.PipelineStageExecutions.AsNoTracking()
            .Where(x => (x.Status == PipelineStageStatuses.Failed || x.Status == PipelineStageStatuses.FailedWithFallback)
                && x.StartedAt >= from7)
            .ToListAsync(cancellationToken);

        var failures24 = await _db.PipelineRuns.AsNoTracking()
            .CountAsync(x => x.Status == PipelineRunStatus.Failed && (x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc) >= from24, cancellationToken);
        var failures7 = await _db.PipelineRuns.AsNoTracking()
            .CountAsync(x => x.Status == PipelineRunStatus.Failed && (x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc) >= from7, cancellationToken);

        var commonStage = failedStages
            .GroupBy(x => x.StageName)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

        return new FailureOpsSummary(failures24, failures7, commonStage, summaries);
    }


    private async Task<IReadOnlyCollection<RegionBreakdownItem>> BuildRegionBreakdownAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-14);
        var runs = await _db.PipelineRuns.AsNoTracking()
            .Where(x => (x.FinishedUtc ?? x.StartedUtc ?? x.CreatedUtc) >= from)
            .ToListAsync(cancellationToken);
        var analytics = await _db.PlatformContentAnalytics.AsNoTracking()
            .Where(x => x.CollectedUtc >= from && x.IsAnalyticsAvailable)
            .ToListAsync(cancellationToken);

        var runGroups = runs.GroupBy(x => ResolveRegionId(x.RegionId, x.LocationName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => new
            {
                LocationName = x.Select(r => r.LocationName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? x.Key,
                Runs = x.Count(),
                Failures = x.Count(r => r.Status == PipelineRunStatus.Failed)
            }, StringComparer.OrdinalIgnoreCase);

        var viewGroups = analytics.GroupBy(x => ResolveRegionId(x.RegionId, x.LocationName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(a => a.Views ?? 0), StringComparer.OrdinalIgnoreCase);

        return runGroups.Keys.Concat(viewGroups.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(regionId =>
            {
                var hasRunData = runGroups.TryGetValue(regionId, out var runData);
                var locationName = hasRunData
                    ? runData!.LocationName
                    : analytics.FirstOrDefault(x => ResolveRegionId(x.RegionId, x.LocationName).Equals(regionId, StringComparison.OrdinalIgnoreCase))?.LocationName ?? regionId;

                return new RegionBreakdownItem(
                    regionId,
                    locationName,
                    hasRunData ? runData!.Runs : 0,
                    viewGroups.GetValueOrDefault(regionId),
                    hasRunData ? runData!.Failures : 0);
            })
            .OrderByDescending(x => x.Runs)
            .ThenByDescending(x => x.Views)
            .ThenBy(x => x.RegionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveRegionId(string? regionId, string? locationName)
    {
        if (!string.IsNullOrWhiteSpace(regionId))
            return regionId;
        return Slugify(locationName ?? "unknown-region");
    }

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "unknown-region";
    }
    private async Task<(AnalyticsDashboardSummary Summary, OpsAnalyticsIntelligenceSummary Intelligence)> BuildAnalyticsOpsDataAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-14);
        var analytics = await _db.PlatformContentAnalytics.AsNoTracking()
            .Where(x => x.CollectedUtc >= from && x.IsAnalyticsAvailable)
            .ToListAsync(cancellationToken);
        if (analytics.Count == 0)
            return (new AnalyticsDashboardSummary([], 0, 0, null, null, null), new OpsAnalyticsIntelligenceSummary([], 0, 0, null, 0));
        var top = analytics
            .OrderByDescending(x => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0))
            .ThenByDescending(x => x.Views ?? 0)
            .Take(10)
            .ToArray();
        var totalEngagement = analytics.Sum(x => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0));
        var bestPlatform = analytics.GroupBy(x => x.Platform)
            .OrderByDescending(x => x.Sum(v => v.Views ?? 0))
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key)
            .FirstOrDefault();
        var bestReel = analytics
            .Where(x => x.PlatformContentType.Contains("reel", StringComparison.OrdinalIgnoreCase) || x.PlatformContentType.Contains("short", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0))
            .ThenByDescending(x => x.Views ?? 0)
            .FirstOrDefault();
        var bestHour = analytics.Where(x => x.PublishedUtc.HasValue)
            .GroupBy(x => x.PublishedUtc!.Value.UtcDateTime.Hour)
            .OrderByDescending(x => x.Sum(v => v.Views ?? 0))
            .Select(x => (int?)x.Key)
            .FirstOrDefault();
        var averageEngagementRate = analytics.Average(x => x.EngagementRate ?? ((x.Views ?? 0) <= 0 ? 0 : (double)((x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0)) / (x.Views ?? 1)));
        var viralCandidates = analytics.Count(x =>
        {
            var engagement = (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0);
            var engagementRate = x.EngagementRate ?? ((x.Views ?? 0) <= 0 ? 0 : (double)engagement / (x.Views ?? 1));
            return engagementRate >= 0.08 && (x.Shares ?? 0) >= Math.Max(3, engagement * 0.15);
        });
        var summary = new AnalyticsDashboardSummary(top, analytics.Sum(x => x.Views ?? 0), totalEngagement, bestPlatform, bestReel, bestHour);
        var intelligence = new OpsAnalyticsIntelligenceSummary(top, totalEngagement, averageEngagementRate, bestPlatform, viralCandidates);
        return (summary, intelligence);
    }

    private async Task<SchedulerOpsSummary> BuildSchedulerSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _scheduler.GetStatusAsync(cancellationToken);
            var next = status.Schedules
                .Where(x => x.Enabled && x.NextPlannedRunUtc.HasValue)
                .OrderBy(x => x.NextPlannedRunUtc)
                .FirstOrDefault()
                ?.NextPlannedRunUtc;
            var last = status.RecentRuns.OrderByDescending(x => x.ActualRunUtc ?? x.PlannedRunUtc).FirstOrDefault();
            var warnings = new List<string>();
            if (status.Schedules.Count == 0)
                warnings.Add("Scheduler has no configured schedules.");
            if (last is null)
                warnings.Add("No scheduler run history was found.");
            return new SchedulerOpsSummary(status.Enabled, status.Schedules.Count, next, last, warnings);
        }
        catch (Exception ex)
        {
            return new SchedulerOpsSummary(false, 0, null, null, [$"Scheduler status unavailable: {ex.Message}"]);
        }
    }

    private async Task<PlatformPublishOpsSummary> BuildPublishSummaryAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var longVideo = await _db.PublishedVideos.AsNoTracking()
            .Where(x => x.PipelineRunId == pipelineRunId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PublishedVideoPublishSummary(x.Status, x.YouTubeVideoId))
            .FirstOrDefaultAsync(cancellationToken);

        var records = await (from record in _db.PlatformPublicationRecords.AsNoTracking()
                             join shortVideo in _db.ShortVideos.AsNoTracking() on record.ParentShortVideoId equals shortVideo.Id
                             join publishedVideo in _db.PublishedVideos.AsNoTracking() on shortVideo.ParentVideoId equals publishedVideo.Id
                             where publishedVideo.PipelineRunId == pipelineRunId
                             orderby record.CreatedUtc descending
                             select record).ToListAsync(cancellationToken);

        var stages = await _db.PipelineStageExecutions.AsNoTracking()
            .Where(x => x.PipelineRunId == pipelineRunId && PublishingStageNames.Contains(x.StageName))
            .ToListAsync(cancellationToken);

        if (longVideo is null && records.Count == 0 && stages.Count == 0)
            warnings.Add("No publishing records or publishing stage diagnostics were found for the selected run.");

        var youtubeLong = longVideo is not null
            ? new PlatformPublishStatus(longVideo.Status, BuildYouTubeUrl(longVideo.YouTubeVideoId))
            : StageStatus(stages, PipelineStageNames.YouTubeLongPublished);

        var youtubeShortRecord = records.FirstOrDefault(x => x.Platform == ShortFormPlatform.YouTubeShorts);
        var facebookRecord = records.FirstOrDefault(x => x.Platform == ShortFormPlatform.Facebook);
        var instagramRecord = records.FirstOrDefault(x => x.Platform == ShortFormPlatform.InstagramReels);

        return new PlatformPublishOpsSummary(
            youtubeLong,
            RecordStatus(youtubeShortRecord, StageStatus(stages, PipelineStageNames.YouTubeShortPublished)),
            RecordStatus(facebookRecord, StageStatus(stages, PipelineStageNames.FacebookReelPublished)),
            RecordStatus(instagramRecord, StageStatus(stages, PipelineStageNames.InstagramReelPublished)),
            warnings);
    }

    private async Task<TokenHealthOpsSummary> BuildTokenHealthSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var results = await _tokenHealth.CheckAllAsync(cancellationToken);
            var youtube = results.FirstOrDefault(x => x.Platform.Contains("YouTube", StringComparison.OrdinalIgnoreCase));
            var meta = results.FirstOrDefault(x => x.Platform.Contains("Meta", StringComparison.OrdinalIgnoreCase));
            var warnings = results.Select(x => x.Warning).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var expiry = results
                .Where(x => !string.IsNullOrWhiteSpace(x.Warning) || x.DaysUntilExpiry is <= 7)
                .OrderBy(x => x.DaysUntilExpiry ?? int.MaxValue)
                .Select(x => string.IsNullOrWhiteSpace(x.Warning) ? $"{x.Platform} token expires in {x.DaysUntilExpiry} days." : x.Warning)
                .FirstOrDefault();
            if (youtube is null)
                warnings.Add("YouTube token health result was not returned.");
            if (meta is null)
                warnings.Add("Meta token health result was not returned.");
            return new TokenHealthOpsSummary(youtube?.IsValid, meta?.IsValid, expiry, warnings);
        }
        catch (Exception ex)
        {
            return new TokenHealthOpsSummary(null, null, null, [$"Token health unavailable: {ex.Message}"]);
        }
    }

    private async Task<SystemHealthOpsSummary> BuildSystemHealthSummaryAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var outputDirectory = _maintenanceOptions.WorkingDirectory;
        var diskFree = TryGetDiskFreeSpace(outputDirectory, warnings);
        var outputSize = TryGetShallowFolderSize(outputDirectory, warnings);
        var ffmpegConfigured = !string.IsNullOrWhiteSpace(_renderingOptions.FfmpegPath);
        var stellariumConfigured = !string.IsNullOrWhiteSpace(_stellariumOptions.ExecutablePath)
            || !string.IsNullOrWhiteSpace(_stellariumOptions.ScriptsDirectory)
            || !string.IsNullOrWhiteSpace(_stellariumOptions.CaptureDirectory);
        var skyfieldReachable = await IsSkyfieldReachableAsync(cancellationToken);
        if (_skyfieldOptions.Enabled && !skyfieldReachable)
            warnings.Add("Skyfield sidecar is enabled but was not reachable within the dashboard timeout.");
        var blobConfigured = !string.IsNullOrWhiteSpace(_azureBlobOptions.ConnectionString)
            || !string.IsNullOrWhiteSpace(_azureBlobOptions.ServiceUri)
            || !string.IsNullOrWhiteSpace(_azureBlobOptions.AccountName)
            || _azureBlobOptions.UseManagedIdentity;

        return new SystemHealthOpsSummary(diskFree, outputSize, ffmpegConfigured, stellariumConfigured, skyfieldReachable, blobConfigured, warnings);
    }

    private async Task<PerformanceOpsSummary> BuildPerformanceSummaryAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var runs = await _db.PipelineRuns.AsNoTracking()
            .Where(x => x.StartedUtc.HasValue && x.FinishedUtc.HasValue && x.StartedUtc >= from)
            .ToListAsync(cancellationToken);
        var stages = await _db.PipelineStageExecutions.AsNoTracking()
            .Where(x => x.StartedAt >= from && x.DurationMs.HasValue)
            .ToListAsync(cancellationToken);

        var warnings = new List<string>();
        if (runs.Count == 0)
            warnings.Add("No completed pipeline runs were found for performance averages.");

        return new PerformanceOpsSummary(
            AverageOrNull(runs.Select(x => (x.FinishedUtc!.Value - x.StartedUtc!.Value).TotalSeconds)),
            AverageOrNull(stages.Where(x => x.StageName.Equals(PipelineStageNames.RenderingCompleted, StringComparison.OrdinalIgnoreCase) || x.StageName.Contains("Render", StringComparison.OrdinalIgnoreCase)).Select(x => x.DurationMs!.Value / 1000d)),
            AverageOrNull(stages.Where(x => PublishingStageNames.Contains(x.StageName)).Select(x => x.DurationMs!.Value / 1000d)),
            warnings);
    }

    private async Task<IReadOnlyCollection<OpsPipelineRunSummary>> SummarizeRunsAsync(IReadOnlyCollection<PipelineRun> runs, CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
            return [];

        var runIds = runs.Select(x => x.Id).ToList();
        var stages = await _db.PipelineStageExecutions.AsNoTracking()
            .Where(x => runIds.Contains(x.PipelineRunId))
            .ToListAsync(cancellationToken);
        var stagesByRun = stages.GroupBy(x => x.PipelineRunId).ToDictionary(x => x.Key, x => x.ToList());
        return runs.Select(run => SummarizeRun(run, stagesByRun.GetValueOrDefault(run.Id) ?? [])).ToList();
    }

    private static OpsPipelineRunSummary SummarizeRun(PipelineRun run, IReadOnlyCollection<PipelineStageExecution> stages)
    {
        var failed = stages
            .Where(x => PipelineStageStatuses.IsFailed(x.Status))
            .OrderByDescending(x => x.FinishedAt ?? x.StartedAt)
            .FirstOrDefault();
        var lastErrorStage = stages
            .Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
            .OrderByDescending(x => x.FinishedAt ?? x.StartedAt)
            .FirstOrDefault();
        var duration = run.StartedUtc.HasValue && run.FinishedUtc.HasValue
            ? (run.FinishedUtc.Value - run.StartedUtc.Value).TotalSeconds
            : (double?)null;
        return new OpsPipelineRunSummary(
            run.Id,
            run.ContentType,
            run.LocationName,
            run.RunDate,
            run.Status,
            run.StartedUtc,
            run.FinishedUtc,
            duration,
            failed?.StageName,
            failed?.ErrorMessage ?? lastErrorStage?.ErrorMessage ?? run.FailureReason);
    }

    private OpsDashboardDiagnostics BuildDiagnosticsSummary()
    {
        var path = Path.Combine(_maintenanceOptions.WorkingDirectory, "ops-dashboard.json");
        if (!File.Exists(path))
            return new OpsDashboardDiagnostics("ops-dashboard.json", false, null, ["ops-dashboard.json was not found; dashboard data was built from database/configuration sources only."]);

        try
        {
            return new OpsDashboardDiagnostics("ops-dashboard.json", true, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), []);
        }
        catch (Exception ex)
        {
            return new OpsDashboardDiagnostics("ops-dashboard.json", false, null, [$"ops-dashboard.json could not be inspected: {ex.Message}"]);
        }
    }

    private static PlatformPublishStatus RecordStatus(PlatformPublicationRecord? record, PlatformPublishStatus fallback)
        => record is null ? fallback : new PlatformPublishStatus(record.Status.ToString(), SanitizeUrl(record.ExternalUrl));

    private static PlatformPublishStatus StageStatus(IReadOnlyCollection<PipelineStageExecution> stages, string stageName)
    {
        var stage = stages.Where(x => x.StageName == stageName).OrderByDescending(x => x.FinishedAt ?? x.StartedAt).FirstOrDefault();
        return stage is null ? new PlatformPublishStatus("Unknown", null) : new PlatformPublishStatus(stage.Status, null);
    }

    private static PlatformPublishOpsSummary EmptyPublishSummary(string warning)
        => new(new PlatformPublishStatus("Unknown", null), new PlatformPublishStatus("Unknown", null), new PlatformPublishStatus("Unknown", null), new PlatformPublishStatus("Unknown", null), [warning]);

    private sealed record PublishedVideoPublishSummary(string Status, string? YouTubeVideoId);

    private static string? BuildYouTubeUrl(string? videoId)
        => string.IsNullOrWhiteSpace(videoId) ? null : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";

    private static string? SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
            return queryIndex >= 0 ? url[..queryIndex] : url;
        }

        return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString();
    }

    private static double? AverageOrNull(IEnumerable<double> values)
    {
        var materialized = values.ToList();
        return materialized.Count == 0 ? null : materialized.Average();
    }

    private static long? TryGetDiskFreeSpace(string outputDirectory, List<string> warnings)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(outputDirectory));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            warnings.Add($"Disk free space unavailable: {ex.Message}");
            return null;
        }
    }

    private static long? TryGetShallowFolderSize(string outputDirectory, List<string> warnings)
    {
        try
        {
            if (!Directory.Exists(outputDirectory))
            {
                warnings.Add("Output folder does not exist.");
                return null;
            }

            var total = 0L;
            foreach (var file in Directory.EnumerateFiles(outputDirectory).Take(500))
                total += new FileInfo(file).Length;
            foreach (var directory in Directory.EnumerateDirectories(outputDirectory).Take(100))
            {
                foreach (var file in Directory.EnumerateFiles(directory).Take(100))
                    total += new FileInfo(file).Length;
            }

            return total;
        }
        catch (Exception ex)
        {
            warnings.Add($"Output folder size unavailable: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> IsSkyfieldReachableAsync(CancellationToken cancellationToken)
    {
        if (!_skyfieldOptions.Enabled || string.IsNullOrWhiteSpace(_skyfieldOptions.BaseUrl) || !Uri.TryCreate(_skyfieldOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
