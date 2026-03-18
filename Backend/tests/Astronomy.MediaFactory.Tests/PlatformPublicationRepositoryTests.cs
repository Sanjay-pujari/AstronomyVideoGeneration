using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformPublicationRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsAndQueriesPlatformPublicationRecords()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase($"platform-publications-{Guid.NewGuid()}")
            .Options;

        await using var db = new MediaFactoryDbContext(options);
        var repo = new EfPipelineRepository(db);

        var record = new PlatformPublicationRecord
        {
            ParentShortVideoId = Guid.NewGuid(),
            Platform = ShortFormPlatform.YouTubeShorts,
            ExternalPostId = "yt-123",
            ExternalUrl = "https://youtube.com/shorts/yt-123",
            Status = PlatformPublicationStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow
        };

        await repo.AddPlatformPublicationRecordAsync(record, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        var fetched = await repo.GetPlatformPublicationRecordAsync(record.Id, CancellationToken.None);
        var byShort = await repo.GetPlatformPublicationRecordsByShortIdAsync(record.ParentShortVideoId, CancellationToken.None);
        var recent = await repo.GetRecentPlatformPublicationRecordsAsync(10, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Single(byShort);
        Assert.Single(recent);
        Assert.Equal("yt-123", fetched!.ExternalPostId);
    }
}
