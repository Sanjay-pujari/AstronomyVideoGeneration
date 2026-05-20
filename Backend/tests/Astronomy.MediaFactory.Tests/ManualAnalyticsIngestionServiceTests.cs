using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class ManualAnalyticsIngestionServiceTests
{
    [Fact]
    public async Task InitializeForPipelineRunAsync_creates_thumbnail_rows_for_long_and_short()
    {
        var serviceProvider = CreateAnalyticsServiceProvider();
        await using var db = serviceProvider.GetRequiredService<MediaFactoryDbContext>();
        var service = new ManualAnalyticsIngestionService(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ManualAnalyticsIngestionService>.Instance);
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


    private static IServiceProvider CreateAnalyticsServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        return services.BuildServiceProvider();
    }
}
