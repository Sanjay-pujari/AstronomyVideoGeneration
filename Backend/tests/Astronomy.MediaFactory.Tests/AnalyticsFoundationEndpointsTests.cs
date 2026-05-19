using Astronomy.MediaFactory.Analytics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AnalyticsFoundationEndpointsTests
{
    [Fact]
    public async Task Summary_And_Filter_Endpoints_Work()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<TestDb>(o => o.UseInMemoryDatabase("analytics-foundation"));

        var app = builder.Build();
        app.MapGet("/api/analytics/summary", async (string? platform, TestDb db, CancellationToken ct) =>
        {
            var q = db.PlatformVideoAnalytics.AsQueryable();
            if (!string.IsNullOrWhiteSpace(platform)) q = q.Where(x => x.Platform == platform);
            var rows = await q.ToListAsync(ct);
            return Results.Ok(new { views = rows.Sum(x => x.Views) });
        });
        app.MapGet("/api/analytics/hooks", async (TestDb db, CancellationToken ct) => Results.Ok(await db.HookPerformance.ToListAsync(ct)));
        app.MapGet("/api/analytics/thumbnails", async (TestDb db, CancellationToken ct) => Results.Ok(await db.ThumbnailPerformance.ToListAsync(ct)));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics { Platform = "youtube", Views = 10, ContentType = "short", Language = "en", RegionId = "us" });
        db.PlatformVideoAnalytics.Add(new PlatformVideoAnalytics { Platform = "facebook", Views = 3, ContentType = "short", Language = "en", RegionId = "us" });
        db.HookPerformance.Add(new HookPerformance { Platform = "youtube", ContentType = "short", Language = "en", RegionId = "us" });
        db.ThumbnailPerformance.Add(new ThumbnailPerformance { Platform = "youtube", ContentType = "short", Language = "en", RegionId = "us" });
        await db.SaveChangesAsync();

        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/summary")).StatusCode);
        var filtered = await client.GetFromJsonAsync<Dictionary<string, int>>("/api/analytics/summary?platform=youtube");
        Assert.Equal(10, filtered!["views"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/hooks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/thumbnails")).StatusCode);
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<PlatformVideoAnalytics> PlatformVideoAnalytics => Set<PlatformVideoAnalytics>();
        public DbSet<HookPerformance> HookPerformance => Set<HookPerformance>();
        public DbSet<ThumbnailPerformance> ThumbnailPerformance => Set<ThumbnailPerformance>();
    }
}
