using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Tests;

public class ContentCategorySettingsServiceTests
{
    [Fact]
    public async Task Seeds_All_Pipeline_Types_And_DailySkyGuide_Enabled()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new MediaFactoryDbContext(options);
        var svc = new ContentCategorySettingsService(db);

        var daily = await svc.GetSettingsAsync(ContentPipelineType.DailySkyGuide);
        var all = await db.ContentCategorySettings.ToListAsync();

        Assert.Equal(Enum.GetValues<ContentPipelineType>().Length, all.Count);
        Assert.NotNull(daily);
        Assert.True(daily!.Enabled);
    }

    [Fact]
    public async Task Disabled_Category_Does_Not_Run()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new MediaFactoryDbContext(options);
        var svc = new ContentCategorySettingsService(db);
        _ = await svc.GetSettingsAsync(ContentPipelineType.RareEventAlert);
        Assert.False(await svc.IsEnabledAsync(ContentPipelineType.RareEventAlert));
    }
}
