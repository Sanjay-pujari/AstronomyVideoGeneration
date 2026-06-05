using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyCategoryReadinessServiceTests
{
    [Fact]
    public async Task GetCategoryReadinessAsync_ReturnsExistsActiveAndWarningState()
    {
        await using var db = CreateDb();
        db.ContentCategories.AddRange(
            new ContentCategoryMaster { Code = "RareEventAlert", DisplayName = "Rare Event Alert", Enabled = true },
            new ContentCategoryMaster { Code = "MoonSpecials", DisplayName = "Moon Specials", Enabled = false });
        await db.SaveChangesAsync();

        var service = new AstronomyCategoryReadinessService(db);
        var result = await service.GetCategoryReadinessAsync(["RareEventAlert", "MoonSpecials", "PlanetGrouping"], CancellationToken.None);

        var rare = Assert.Single(result.Categories, c => c.CategoryCode == "RareEventAlert");
        Assert.True(rare.Exists);
        Assert.True(rare.IsActive);
        Assert.True(rare.CanPlan);
        Assert.Null(rare.Warning);
        Assert.Equal("Rare Event Alert", rare.DisplayName);

        var moon = Assert.Single(result.Categories, c => c.CategoryCode == "MoonSpecials");
        Assert.True(moon.Exists);
        Assert.False(moon.IsActive);
        Assert.False(moon.CanPlan);
        Assert.Contains("inactive", moon.Warning);

        var grouping = Assert.Single(result.Categories, c => c.CategoryCode == "PlanetGrouping");
        Assert.False(grouping.Exists);
        Assert.False(grouping.IsActive);
        Assert.False(grouping.CanPlan);
        Assert.Null(grouping.DisplayName);
        Assert.Contains("missing", grouping.Warning);
    }

    [Fact]
    public async Task GetCategoryReadinessAsync_DefaultsToPhase7CategoryAuditList()
    {
        await using var db = CreateDb();
        var service = new AstronomyCategoryReadinessService(db);

        var result = await service.GetCategoryReadinessAsync(null, CancellationToken.None);

        Assert.Equal(AstronomyOpportunityCategoryCodes.Phase7CategoryCodes.ToArray(), result.Categories.Select(c => c.CategoryCode).ToArray());
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
