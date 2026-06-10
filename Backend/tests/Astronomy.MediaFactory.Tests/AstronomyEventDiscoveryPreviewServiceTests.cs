using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyEventDiscoveryPreviewServiceTests
{
    [Fact]
    public async Task DiscoverAstronomyEvents_WritesPreviewJsonOnlyUnderEventDiscoveryFolder()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "event-discovery-preview-" + Guid.NewGuid().ToString("N"));
        var service = CreateService(outputRoot);

        var result = await service.DiscoverAstronomyEventsAsync(new AstronomyEventDiscoveryPreviewRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: false,
            OverwriteExisting: true), CancellationToken.None);

        Assert.True(result.EventPreviewGenerated);
        Assert.Equal(2026, result.Year);
        Assert.Equal("IN-RJ-UDAIPUR", result.RegionId);
        Assert.True(result.EventCount > 0);
        Assert.True(result.TopEventCount > 0);
        var expectedPath = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "event-discovery", "2026", "astronomy-event-preview-2026.json");
        Assert.Equal(expectedPath, result.EventPreviewPath);
        Assert.Equal(expectedPath, Assert.Single(result.GeneratedFiles));
        Assert.True(File.Exists(expectedPath));
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans")));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(expectedPath));
        var root = document.RootElement;
        Assert.Equal(2026, root.GetProperty("year").GetInt32());
        Assert.Equal("IN-RJ-UDAIPUR", root.GetProperty("regionId").GetString());
        Assert.Equal("en", root.GetProperty("language").GetString());
        Assert.Equal(result.EventCount, root.GetProperty("eventCount").GetInt32());
        Assert.Contains(root.GetProperty("events").EnumerateArray(), e => e.GetProperty("eventType").GetString() == "MeteorShower");
        Assert.Contains(root.GetProperty("events").EnumerateArray(), e => e.GetProperty("eventType").GetString() == "NamedFullMoon");
    }

    [Fact]
    public async Task DiscoverAstronomyEvents_DryRunDoesNotWriteFile()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "event-discovery-preview-" + Guid.NewGuid().ToString("N"));
        var service = CreateService(outputRoot);

        var result = await service.DiscoverAstronomyEventsAsync(new AstronomyEventDiscoveryPreviewRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: true,
            OverwriteExisting: true), CancellationToken.None);

        Assert.False(result.EventPreviewGenerated);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(result.EventPreviewPath));
        Assert.True(result.EventCount > 0);
    }

    private static AstronomyEventDiscoveryPreviewService CreateService(string outputRoot)
    {
        var rendering = Options.Create(new RenderingOptions { WorkingDirectory = outputRoot });
        var scheduler = Options.Create(new SchedulerOptions());
        return new AstronomyEventDiscoveryPreviewService(rendering, scheduler, TimeProvider.System, NullLogger<AstronomyEventDiscoveryPreviewService>.Instance);
    }
}
