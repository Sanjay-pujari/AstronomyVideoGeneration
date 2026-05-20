using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentMasterDataTests
{
    [Fact]
    public void Model_Contains_New_Master_Tables()
    {
        using var db = CreateDb();
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        Assert.Contains("content_categories", tables);
        Assert.Contains("hook_styles", tables);
        Assert.Contains("thumbnail_styles", tables);
        Assert.Contains("narration_styles", tables);
        Assert.Contains("celestial_objects", tables);
        Assert.Contains("astronomy_event_types", tables);
        Assert.Contains("content_generation_plans", tables);
        Assert.Contains("content_pipeline_executions", tables);
        Assert.Contains("content_category_style_settings", tables);
    }

    [Fact]
    public void SeedData_Contains_Content_Category_And_Utc_Timestamp()
    {
        using var db = CreateDb();
        var seeded = db.ContentCategories.Single(x => x.Code == "DailySkyGuide");
        Assert.Equal(TimeSpan.Zero, seeded.CreatedUtc.Offset);
        Assert.Equal(TimeSpan.Zero, (seeded.UpdatedUtc ?? seeded.CreatedUtc).Offset);
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
