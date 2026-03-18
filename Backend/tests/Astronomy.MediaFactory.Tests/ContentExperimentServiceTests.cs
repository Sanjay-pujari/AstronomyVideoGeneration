using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentExperimentServiceTests
{
    [Fact]
    public async Task InitializeExperimentsAsync_CreatesTitleThumbnailAndCtaExperiments()
    {
        await using var db = CreateDb();
        var service = new EfContentExperimentService(db);
        var video = new PublishedVideo { Title = "Primary", OptimizedTitle = "Primary" };
        db.PublishedVideos.Add(video);
        await db.SaveChangesAsync();

        await service.InitializeExperimentsAsync(
            video,
            new OptimizedVideoMetadata
            {
                PrimaryTitle = "Primary",
                AlternateTitles = ["Alt One", "Alt Two"],
                ThumbnailTextSuggestions = ["Big Jupiter", "Best Tonight"]
            },
            new ThumbnailPlan
            {
                PrimaryThumbnailText = "Big Jupiter",
                Variants =
                [
                    new ThumbnailVariantOption { LayoutType = ThumbnailLayoutType.TopBanner, Text = "Big Jupiter" },
                    new ThumbnailVariantOption { LayoutType = ThumbnailLayoutType.CenteredTitleOverlay, Text = "Best Tonight" }
                ]
            },
            new MonetizationPlan
            {
                FinalDescription = "desc",
                PinnedCommentText = "Check the pinned gear links.",
                AffiliateLinks = []
            },
            CancellationToken.None);

        var experiments = await db.ContentExperiments.Include(x => x.Variants).OrderBy(x => x.ExperimentType).ToListAsync();
        Assert.Equal(3, experiments.Count);
        Assert.Contains(experiments, x => x.ExperimentType == ContentExperimentType.Title && x.Variants.Count == 3);
        Assert.Contains(experiments, x => x.ExperimentType == ContentExperimentType.Thumbnail && x.Variants.Count == 2);
        Assert.Contains(experiments, x => x.ExperimentType == ContentExperimentType.CTA && x.Variants.Count >= 1);
        Assert.NotNull(video.TitleExperimentId);
        Assert.NotNull(video.SelectedTitleVariantId);
        Assert.NotNull(video.ThumbnailExperimentId);
    }

    [Fact]
    public async Task EvaluateRecentExperiments_SelectsWinnerByCtrWhenAvailable()
    {
        await using var db = CreateDb();
        var service = new EfContentExperimentService(db);
        var video = new PublishedVideo { Title = "Alpha", OptimizedTitle = "Alpha", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        db.PublishedVideos.Add(video);

        var experiment = BuildExperiment(video.Id, ContentExperimentType.Title, ContentVariantType.TitleText, DateTimeOffset.UtcNow.AddDays(-2), ["Alpha", "Beta"]);
        video.TitleExperimentId = experiment.Id;
        video.SelectedTitleVariantId = experiment.SelectedVariantId;
        db.ContentExperiments.Add(experiment);
        db.VideoAnalytics.AddRange(
            new VideoAnalytics { PublishedVideoId = video.Id, TitleVariantId = experiment.Variants[0].Id, TitleExperimentId = experiment.Id, Views = 250, CtrPercent = 4.1, Likes = 15, Comments = 2, DurationSeconds = 100, AverageViewDurationSeconds = 52, RetrievedAt = DateTimeOffset.UtcNow.AddHours(-20) },
            new VideoAnalytics { PublishedVideoId = video.Id, TitleVariantId = experiment.Variants[1].Id, TitleExperimentId = experiment.Id, Views = 200, CtrPercent = 5.2, Likes = 11, Comments = 1, DurationSeconds = 100, AverageViewDurationSeconds = 49, RetrievedAt = DateTimeOffset.UtcNow.AddHours(-10) });
        await db.SaveChangesAsync();

        await service.EvaluateRecentExperimentsAsync(CancellationToken.None);

        var stored = await db.ContentExperiments.Include(x => x.Variants).SingleAsync();
        var winner = stored.Variants.Single(x => x.IsWinner);
        Assert.Equal(ContentExperimentStatus.Completed, stored.Status);
        Assert.Equal("Beta", winner.Value);
        Assert.Equal(winner.Id, stored.SelectedVariantId);
        Assert.Equal("Beta", video.Title);
    }


    [Fact]
    public async Task EvaluateRecentExperiments_RotatesToUntestedVariantAfterInterval()
    {
        await using var db = CreateDb();
        var service = new EfContentExperimentService(db);
        var video = new PublishedVideo { Title = "Alpha", OptimizedTitle = "Alpha", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        db.PublishedVideos.Add(video);

        var experiment = BuildExperiment(video.Id, ContentExperimentType.Title, ContentVariantType.TitleText, DateTimeOffset.UtcNow.AddDays(-2), ["Alpha", "Beta", "Gamma"]);
        experiment.SelectedVariantId = experiment.Variants[0].Id;
        video.TitleExperimentId = experiment.Id;
        video.SelectedTitleVariantId = experiment.SelectedVariantId;
        db.ContentExperiments.Add(experiment);
        db.VideoAnalytics.Add(new VideoAnalytics
        {
            PublishedVideoId = video.Id,
            TitleVariantId = experiment.Variants[0].Id,
            TitleExperimentId = experiment.Id,
            Views = 250,
            CtrPercent = 4.8,
            Likes = 20,
            Comments = 4,
            DurationSeconds = 100,
            AverageViewDurationSeconds = 60,
            RetrievedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        await service.EvaluateRecentExperimentsAsync(CancellationToken.None);

        var stored = await db.ContentExperiments.SingleAsync();
        Assert.Equal(ContentExperimentStatus.Running, stored.Status);
        Assert.Equal(experiment.Variants[1].Id, stored.SelectedVariantId);

        var refreshedVideo = await db.PublishedVideos.SingleAsync();
        Assert.Equal(experiment.Variants[1].Id, refreshedVideo.SelectedTitleVariantId);
        Assert.Equal("Beta", refreshedVideo.Title);
    }

    [Fact]
    public async Task GetFeedbackSnapshotAsync_IncludesStructuredInsightsForRecentWinners()
    {
        await using var db = CreateDb();
        var service = new EfContentExperimentService(db);
        var video = new PublishedVideo { Title = "Alpha", OptimizedTitle = "Alpha", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        db.PublishedVideos.Add(video);

        var experiment = BuildExperiment(video.Id, ContentExperimentType.Title, ContentVariantType.TitleText, DateTimeOffset.UtcNow.AddDays(-2), ["Alpha Tonight", "Beta Tonight"]);
        experiment.Status = ContentExperimentStatus.Completed;
        experiment.CompletedAt = DateTimeOffset.UtcNow.AddHours(-1);
        experiment.SelectedVariantId = experiment.Variants[1].Id;
        experiment.Variants[1].IsWinner = true;
        experiment.Variants[1].Views = 320;
        experiment.Variants[1].Ctr = 5.4;
        experiment.Variants[1].EngagementScore = 61.2;
        db.ContentExperiments.Add(experiment);
        await db.SaveChangesAsync();

        var snapshot = await service.GetFeedbackSnapshotAsync(CancellationToken.None);

        var insight = Assert.Single(snapshot.Insights);
        Assert.Equal(ContentExperimentType.Title, insight.ExperimentType);
        Assert.Equal("Beta Tonight", insight.WinningValue);
        Assert.Equal(320, insight.Metrics.Views);
        Assert.Equal(5.4, insight.Metrics.Ctr);
    }

    [Fact]
    public async Task EvaluateRecentExperiments_FallsBackToFirstVariantWhenAnalyticsMissing()
    {
        await using var db = CreateDb();
        var service = new EfContentExperimentService(db);
        var video = new PublishedVideo { Title = "Alpha", OptimizedTitle = "Alpha", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) };
        db.PublishedVideos.Add(video);

        var experiment = BuildExperiment(video.Id, ContentExperimentType.Thumbnail, ContentVariantType.ThumbnailTextAndLayout, DateTimeOffset.UtcNow.AddDays(-3), ["TopBanner: Alpha", "Overlay: Beta"]);
        video.ThumbnailExperimentId = experiment.Id;
        video.SelectedThumbnailVariantId = experiment.SelectedVariantId;
        db.ContentExperiments.Add(experiment);
        await db.SaveChangesAsync();

        await service.EvaluateRecentExperimentsAsync(CancellationToken.None);

        var stored = await db.ContentExperiments.Include(x => x.Variants).SingleAsync();
        Assert.Equal(ContentExperimentStatus.Completed, stored.Status);
        Assert.Equal(stored.Variants[0].Id, stored.SelectedVariantId);
        Assert.True(stored.Variants[0].IsWinner);
    }

    private static MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private static ContentExperiment BuildExperiment(Guid videoId, ContentExperimentType experimentType, ContentVariantType variantType, DateTimeOffset createdAt, IReadOnlyCollection<string> values)
    {
        var experiment = new ContentExperiment
        {
            VideoId = videoId,
            ExperimentType = experimentType,
            Status = ContentExperimentStatus.Running,
            CreatedAt = createdAt,
            Variants = []
        };

        foreach (var value in values)
        {
            experiment.Variants.Add(new ContentVariant
            {
                ContentExperimentId = experiment.Id,
                VariantType = variantType,
                Value = value
            });
        }

        experiment.SelectedVariantId = experiment.Variants[0].Id;
        return experiment;
    }
}
