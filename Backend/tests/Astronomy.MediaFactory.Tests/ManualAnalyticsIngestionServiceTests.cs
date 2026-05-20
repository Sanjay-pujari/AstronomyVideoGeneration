using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public class ManualAnalyticsIngestionServiceTests
{
    [Fact]
    public async Task InitializeForPipelineRunAsync_creates_thumbnail_rows_for_long_and_short()
    {
        await using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var service = new ManualAnalyticsIngestionService(db, NullLogger<ManualAnalyticsIngestionService>.Instance);
        var runId = Guid.NewGuid();

        await service.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
            runId,
            "en",
            "us",
            DateTimeOffset.UtcNow,
            new[] { "YouTube-Long" },
            new[] { "Hook" },
            new[]
            {
                new AnalyticsThumbnailSeed("thumbnail-long.jpg", "Long"),
                new AnalyticsThumbnailSeed("thumbnail-short.jpg", "Short")
            },
            "LongVideo",
            null,
            null),
            CancellationToken.None);

        var rows = await db.ThumbnailPerformance.Where(x => x.PipelineRunId == runId).OrderBy(x => x.ThumbnailType).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.ThumbnailPath == "thumbnail-long.jpg");
        Assert.Contains(rows, x => x.ThumbnailPath == "thumbnail-short.jpg");
    }
}
