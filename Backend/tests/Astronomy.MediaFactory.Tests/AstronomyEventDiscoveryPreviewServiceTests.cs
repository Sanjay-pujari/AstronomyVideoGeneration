using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
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


    [Fact]
    public async Task VerifyAstronomyEvents_WritesVerifiedJsonAndDeduplicatesFullMoons()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "event-verification-" + Guid.NewGuid().ToString("N"));
        var previewService = CreateService(outputRoot);
        await previewService.DiscoverAstronomyEventsAsync(new AstronomyEventDiscoveryPreviewRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: false,
            OverwriteExisting: true), CancellationToken.None);

        var verificationService = CreateVerificationService(outputRoot);
        var result = await verificationService.VerifyAstronomyEventsAsync(new AstronomyEventVerificationRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: false,
            OverwriteExisting: true), CancellationToken.None);

        Assert.True(result.EventVerificationGenerated);
        Assert.Equal(2026, result.Year);
        Assert.Equal("IN-RJ-UDAIPUR", result.RegionId);
        Assert.True(result.InputEventCount > result.VerifiedEventCount);
        Assert.True(result.DeduplicatedCount > 0);
        Assert.True(result.HighPriorityCount > 0);
        Assert.True(result.ManualReviewCount > 0);
        Assert.True(result.AutoGenerateAllowedCount > 0);
        var expectedPath = Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "event-discovery", "2026", "astronomy-event-verified-2026.json");
        Assert.Equal(expectedPath, result.EventVerificationPath);
        Assert.Equal(expectedPath, Assert.Single(result.GeneratedFiles));
        Assert.True(File.Exists(expectedPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(expectedPath));
        var root = document.RootElement;
        Assert.Equal(result.InputEventCount, root.GetProperty("inputEventCount").GetInt32());
        Assert.Equal(result.VerifiedEventCount, root.GetProperty("verifiedEventCount").GetInt32());
        Assert.Equal(result.DeduplicatedCount, root.GetProperty("deduplicatedCount").GetInt32());
        Assert.Equal(result.HighPriorityCount, root.GetProperty("highPriorityCount").GetInt32());
        Assert.Equal(result.ManualReviewCount, root.GetProperty("manualReviewCount").GetInt32());
        Assert.Equal(result.AutoGenerateAllowedCount, root.GetProperty("autoGenerateAllowedCount").GetInt32());
        Assert.Equal(result.SkyfieldVerifiedCount, root.GetProperty("skyfieldVerifiedCount").GetInt32());
        Assert.Equal(result.PlanetPairingComputedCount, root.GetProperty("planetPairingComputedCount").GetInt32());
        Assert.Equal(result.MoonPhaseVerifiedCount, root.GetProperty("moonPhaseVerifiedCount").GetInt32());
        Assert.Equal(result.MeteorMoonlightAdjustedCount, root.GetProperty("meteorMoonlightAdjustedCount").GetInt32());

        var summary = root.GetProperty("verificationSummary");
        Assert.Equal(result.InputEventCount, summary.GetProperty("inputEventCount").GetInt32());
        Assert.Equal(result.SkyfieldVerifiedCount, summary.GetProperty("skyfieldVerifiedCount").GetInt32());
        Assert.Equal(result.PlanetPairingComputedCount, summary.GetProperty("planetPairingComputedCount").GetInt32());
        Assert.Equal(result.MoonPhaseVerifiedCount, summary.GetProperty("moonPhaseVerifiedCount").GetInt32());
        Assert.Equal(result.MeteorMoonlightAdjustedCount, summary.GetProperty("meteorMoonlightAdjustedCount").GetInt32());

        var events = root.GetProperty("events").EnumerateArray().ToArray();
        Assert.DoesNotContain(events, e => e.GetProperty("eventType").GetString() == "FullMoon");
        var namedFullMoon = events.First(e => e.GetProperty("eventType").GetString() == "NamedFullMoon");
        Assert.Contains(namedFullMoon.GetProperty("aliases").EnumerateArray(), a => a.GetString() == "Full Moon");
        Assert.Contains(namedFullMoon.GetProperty("verificationStatus").GetString(), new[] { "Approximate", "Verified" });
        if (namedFullMoon.GetProperty("verificationStatus").GetString() == "Verified")
        {
            Assert.Equal("Skyfield", namedFullMoon.GetProperty("verificationSource").GetString());
        }
        Assert.Equal("Medium", namedFullMoon.GetProperty("publishPriority").GetString());
        Assert.Contains(events, e => e.GetProperty("verificationStatus").GetString() == "NeedsManualReview" && e.GetProperty("sourceType").GetString() == "ManualSeed");
        Assert.All(root.GetProperty("topEvents").EnumerateArray(), e =>
        {
            Assert.True(e.GetProperty("contentWorthinessScore").GetInt32() >= 85);
            Assert.Equal("High", e.GetProperty("publishPriority").GetString());
            Assert.True(e.GetProperty("autoGenerateAllowed").GetBoolean());
            Assert.NotEqual("NeedsManualReview", e.GetProperty("verificationStatus").GetString());
            Assert.NotEqual("ManualSeed", e.GetProperty("sourceType").GetString());
        });

        Assert.All(events.Where(e => e.GetProperty("eventType").GetString()!.Contains("Meteor")), e =>
        {
            Assert.True(e.TryGetProperty("moonIlluminationPercent", out _));
            Assert.True(e.TryGetProperty("moonInterference", out _));
            Assert.True(e.TryGetProperty("bestViewingWindowLocal", out _));
            Assert.True(e.TryGetProperty("radiantVisibilityNote", out _));
        });
    }

    [Fact]
    public async Task VerifyAstronomyEvents_DryRunDoesNotWriteFile()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "event-verification-" + Guid.NewGuid().ToString("N"));
        var previewService = CreateService(outputRoot);
        await previewService.DiscoverAstronomyEventsAsync(new AstronomyEventDiscoveryPreviewRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: false,
            OverwriteExisting: true), CancellationToken.None);

        var verificationService = CreateVerificationService(outputRoot);
        var result = await verificationService.VerifyAstronomyEventsAsync(new AstronomyEventVerificationRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            DryRun: true,
            OverwriteExisting: true), CancellationToken.None);

        Assert.False(result.EventVerificationGenerated);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(result.EventVerificationPath));
        Assert.True(result.InputEventCount > 0);
        Assert.True(result.DeduplicatedCount > 0);
    }

    private static AstronomyEventDiscoveryPreviewService CreateService(string outputRoot)
    {
        var rendering = Options.Create(new RenderingOptions { WorkingDirectory = outputRoot });
        var scheduler = Options.Create(new SchedulerOptions());
        return new AstronomyEventDiscoveryPreviewService(rendering, scheduler, TimeProvider.System, NullLogger<AstronomyEventDiscoveryPreviewService>.Instance);
    }

    private static AstronomyEventVerificationService CreateVerificationService(string outputRoot)
    {
        var rendering = Options.Create(new RenderingOptions { WorkingDirectory = outputRoot });
        return new AstronomyEventVerificationService(rendering, TimeProvider.System, new EmptySkyfieldAccuracyProvider(), NullLogger<AstronomyEventVerificationService>.Instance);
    }
    private sealed class EmptySkyfieldAccuracyProvider : ISkyfieldAccuracyProvider
    {
        public Task<SkyfieldAccuracyResult> ComputeYearlyAccuracyAsync(int year, RegionScheduleOptions region, IReadOnlyList<AstronomyEventPreviewItem> events, CancellationToken cancellationToken) =>
            Task.FromResult(new SkyfieldAccuracyResult());
    }
}
