using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AnalyticsIntelligenceServiceTests
{
    [Fact]
    public async Task Dashboard_AggregatesAnalyticsCorrectly()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.Equal(4, dashboard.OverallSummary.TotalContentPublished);
        Assert.Equal(3_800, dashboard.OverallSummary.TotalViews);
        Assert.Equal("Instagram", dashboard.OverallSummary.BestPerformingPlatform);
        Assert.Contains(dashboard.PlatformBreakdown, x => x.Platform == "YouTube" && x.ContentCount == 1);
    }

    [Fact]
    public async Task TopContent_RankingUsesPerformanceScore()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var top = await service.GetTopContentAsync(new AnalyticsIntelligenceRequest(Days: 30, Limit: 2), CancellationToken.None);

        Assert.Equal(2, top.Count);
        Assert.Equal("Jupiter reel hook", top.First().Title);
        Assert.True(top.First().PerformanceScore >= top.Last().PerformanceScore);
        Assert.Contains(db.PlatformContentAnalytics, x => x.PerformanceScore.HasValue);
    }

    [Fact]
    public async Task AstronomyObjectExtraction_UsesTitlesSeoHashtagsAndNarrationContext()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.Contains(dashboard.AstronomyIntelligence.Objects, x => x.ObjectName == "Jupiter");
        Assert.Contains(dashboard.AstronomyIntelligence.Objects, x => x.ObjectName == "Orion Nebula");
        Assert.Equal("Jupiter", dashboard.AstronomyIntelligence.TopObjectByViews);
    }

    [Fact]
    public async Task DurationBucketAnalysis_GroupsShortsAndReels()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.Contains(dashboard.ReelIntelligence.DurationBuckets, x => x.Range == "15-30 sec" && x.ContentCount == 1);
        Assert.Contains(dashboard.ReelIntelligence.DurationBuckets, x => x.Range == "30-45 sec" && x.ContentCount == 1);
    }

    [Fact]
    public async Task TrendDetection_FindsViralCandidatesAndUnderperformers()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.NotEmpty(dashboard.Trends.ViralCandidates);
        Assert.NotEmpty(dashboard.Trends.UnderperformingContent);
        Assert.NotNull(dashboard.Trends.FastestGrowingContent);
    }

    [Fact]
    public async Task Insights_AreGeneratedAndStored()
    {
        await using var db = CreateDb();
        var output = Path.Combine(Path.GetTempPath(), "analytics-intelligence-tests", Guid.NewGuid().ToString("N"));
        SeedAnalytics(db, output);
        var service = CreateService(db, output);

        var insights = await service.GetInsightsAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.NotEmpty(insights);
        Assert.True(File.Exists(Path.Combine(output, "analytics-insights.json")));
    }

    [Fact]
    public async Task MissingAnalytics_IsHandledSafely()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.Equal(0, dashboard.OverallSummary.TotalViews);
        Assert.Empty(dashboard.Trends.ViralCandidates);
        Assert.NotEmpty(dashboard.Insights);
    }

    [Fact]
    public async Task TopContent_MissingAnalytics_ReturnsEmptyCollection()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var top = await service.GetTopContentAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.Empty(top);
    }

    [Fact]
    public async Task ChartData_FormatIsFrontendReady()
    {
        await using var db = CreateDb();
        SeedAnalytics(db);
        var service = CreateService(db);

        var dashboard = await service.BuildDashboardAsync(new AnalyticsIntelligenceRequest(Days: 30), CancellationToken.None);

        Assert.NotEmpty(dashboard.Charts.DailyViews);
        Assert.NotEmpty(dashboard.Charts.PlatformComparison);
        Assert.NotEmpty(dashboard.Charts.ObjectPerformance);
        Assert.Contains(dashboard.Charts.DurationPerformance, x => x.Range == "15-30 sec");
        Assert.NotEmpty(dashboard.Charts.EngagementTrends);
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static AnalyticsIntelligenceService CreateService(MediaFactoryDbContext db, string? output = null)
        => new(db,
            Options.Create(new AnalyticsOptions { TopN = 10 }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = output ?? Path.Combine(Path.GetTempPath(), "analytics-intelligence-tests", Guid.NewGuid().ToString("N")) }));

    private static void SeedAnalytics(MediaFactoryDbContext db, string? output = null)
    {
        output ??= Path.Combine(Path.GetTempPath(), "analytics-intelligence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var thumbnail = Path.Combine(output, "thumbnail-2.png");
        File.WriteAllText(Path.Combine(output, "thumbnail-selection.json"), "{\"variants\":[\"A\",\"B\"]}");

        var jupiterRun = new PipelineRun { ContentType = ContentType.SpaceNews, LocationName = "Udaipur", TimeZone = "Asia/Kolkata", Status = PipelineRunStatus.Succeeded, RunDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        var orionRun = new PipelineRun { ContentType = ContentType.TelescopeTargets, LocationName = "Udaipur", TimeZone = "Asia/Kolkata", Status = PipelineRunStatus.Succeeded, RunDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.PipelineRuns.AddRange(jupiterRun, orionRun);
        db.GeneratedScripts.Add(new GeneratedScript
        {
            PipelineRunId = jupiterRun.Id,
            ContentType = ContentType.SpaceNews,
            ScriptDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Jupiter tonight",
            Description = "SEO metadata for Jupiter and Saturn",
            TagsCsv = "Jupiter,Saturn",
            ScriptBody = "Narration context talks about Jupiter near the Moon.",
            HookLine = "Wait for Jupiter"
        });
        db.GeneratedScripts.Add(new GeneratedScript
        {
            PipelineRunId = orionRun.Id,
            ContentType = ContentType.TelescopeTargets,
            ScriptDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Deep sky",
            Description = "Orion Nebula SEO metadata",
            TagsCsv = "M42,Orion Nebula",
            ScriptBody = "Narration context mentions a galaxy cluster.",
            HookLine = "Find Orion"
        });

        db.PlatformContentAnalytics.AddRange(
            new PlatformContentAnalytics { PipelineRunId = jupiterRun.Id, Platform = "Instagram", PlatformContentType = "Reel", PlatformMediaId = "ig-jupiter", PlatformUrl = "https://ig/jupiter", Title = "Jupiter reel hook", Hashtags = "#Jupiter #space", PublishedUtc = DateTimeOffset.UtcNow.AddHours(-4), CollectedUtc = DateTimeOffset.UtcNow.AddHours(-1), Views = 2_000, Likes = 240, Comments = 20, Shares = 80, WatchTimeMinutes = 800, AverageViewDurationSeconds = 24, DurationSeconds = 30, Ctr = 0.12, ThumbnailPath = thumbnail, IsAnalyticsAvailable = true },
            new PlatformContentAnalytics { PipelineRunId = jupiterRun.Id, Platform = "Facebook", PlatformContentType = "Reel", PlatformMediaId = "fb-jupiter", Title = "Jupiter reel mirror", Hashtags = "#Jupiter", PublishedUtc = DateTimeOffset.UtcNow.AddDays(-4), CollectedUtc = DateTimeOffset.UtcNow.AddDays(-3), Views = 900, Likes = 40, Comments = 5, Shares = 4, WatchTimeMinutes = 100, AverageViewDurationSeconds = 18, DurationSeconds = 30, Ctr = 0.04, ThumbnailPath = Path.Combine(output, "thumbnail-1.png"), IsAnalyticsAvailable = true },
            new PlatformContentAnalytics { PipelineRunId = orionRun.Id, Platform = "YouTube", PlatformContentType = "Long", PlatformMediaId = "yt-orion", Title = "Nebula tour", Hashtags = "#M42", PublishedUtc = DateTimeOffset.UtcNow.AddDays(-2), CollectedUtc = DateTimeOffset.UtcNow.AddDays(-1), Views = 800, Likes = 60, Comments = 8, Shares = 12, WatchTimeMinutes = 1_200, AverageViewDurationSeconds = 300, DurationSeconds = 600, Ctr = 0.08, ThumbnailPath = Path.Combine(output, "thumbnail-3.png"), IsAnalyticsAvailable = true },
            new PlatformContentAnalytics { Platform = "Instagram", PlatformContentType = "Short", PlatformMediaId = "ig-low", Title = "Moon quick tip", Hashtags = "#Moon", PublishedUtc = DateTimeOffset.UtcNow.AddDays(-10), CollectedUtc = DateTimeOffset.UtcNow.AddDays(-10), Views = 100, Likes = 1, Comments = 0, Shares = 0, WatchTimeMinutes = 2, AverageViewDurationSeconds = 5, DurationSeconds = 15, IsAnalyticsAvailable = true });
        db.SaveChanges();
    }
}
