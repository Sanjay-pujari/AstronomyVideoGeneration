using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class NarrationPreviewRequestTests
{
    [Fact]
    public void DeserializesReturnScenesBoolean()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("false"));

        Assert.NotNull(request);
        Assert.False(request.ReturnScenes);
    }

    [Fact]
    public void DeserializesReturnScenesArrayAsEnabledWhenScenesAreRequested()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("[\"hook\", \"best-time\"]"));

        Assert.NotNull(request);
        Assert.True(request.ReturnScenes);
    }

    [Fact]
    public void DeserializesEmptyReturnScenesArrayAsDisabled()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("[]"));

        Assert.NotNull(request);
        Assert.False(request.ReturnScenes);
    }

    [Fact]
    public async Task NarrationGenerationFallsBackWhenEventNameIsNull()
    {
        var request = new NarrationPreviewRequest(
            PlanId: "plan-1",
            EventType: "meteor_shower",
            EventName: null!,
            ShortTitle: "Fallback Meteor Shower",
            Language: "en",
            RegionId: "us",
            Format: null,
            EventMetadata: null);
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("Fallback Meteor Shower", response.EventName);
        Assert.Contains(response.Scenes, scene => scene.Narration.Contains("Fallback Meteor Shower", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrationGenerationFallsBackWhenEventTypeAndNameAreNull()
    {
        var request = new NarrationPreviewRequest(
            PlanId: "plan-1",
            EventType: null!,
            EventName: null!,
            ShortTitle: null,
            Language: "en",
            RegionId: null!,
            Format: null,
            EventMetadata: null);
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("astronomy event", response.EventType);
        Assert.Equal("this sky event", response.EventName);
        Assert.Equal(string.Empty, response.RegionId);
        Assert.NotEmpty(response.Scenes);
    }

    private static string RequestJson(string returnScenes) => $$"""
        {
          "eventType": "meteor_shower",
          "eventName": "Geminid Meteor Shower",
          "language": "en",
          "regionId": "us",
          "returnScenes": {{returnScenes}}
        }
        """;
}

public sealed class NarrationPreviewPlanHydrationTests
{
    [Fact]
    public async Task NarrationGenerationHydratesPlanAndEventIntelligenceWhenPlanIdIsProvided()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MediaFactoryDbContext(options);
        var planId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
        var intelligence = new Astronomy.MediaFactory.Core.AstronomyEventIntelligence
        {
            EventType = "MeteorShower",
            Title = "Geminids Meteor Shower Peak",
            Summary = "Geminids",
            Language = "en",
            StartUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 12, 14, 6, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            MetadataJson = JsonSerializer.Serialize(new
            {
                shortTitle = "Geminids",
                eventDate = "2026-12-14",
                localPeakTime = "2026-12-14 11:30 +05:30",
                bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST",
                skyDirectionHint = "East to overhead after 10 PM",
                moonInterference = "low"
            })
        };
        var plan = new Astronomy.MediaFactory.Core.ContentGenerationPlan
        {
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            Title = "Generic request title that must be replaced",
            AstronomyEventIntelligence = intelligence,
            AstronomyEventIntelligenceId = intelligence.Id,
            PlannedFormat = "ShortVideo"
        };
        plan.AssignId(planId);
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var request = new NarrationPreviewRequest(planId.ToString("D"), null!, null!, null, "", "", null, null);
        var service = new NarrationGenerationService(db);

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("MeteorShower", response.EventType);
        Assert.Equal("Geminids Meteor Shower Peak", response.EventName);
        Assert.Equal("Geminids", response.ShortTitle);
        Assert.Equal("IN-RJ-UDAIPUR", response.RegionId);
        Assert.Equal("December 14, 2026", response.FormattingDiagnostics.EventDate);
        Assert.Equal("December 14, 2026 11:30", response.FormattingDiagnostics.PeakTime);
        Assert.Equal("December 14, 2026 00:00–05:00 IST", response.FormattingDiagnostics.ViewingWindow);
        Assert.Equal("East to overhead after 10 PM", response.FormattingDiagnostics.Direction);
        Assert.NotNull(response.PlanHydrationDiagnostics);
        Assert.True(response.PlanHydrationDiagnostics.PlanLoaded);
        Assert.True(response.PlanHydrationDiagnostics.EventIntelligenceLoaded);
        Assert.False(response.PlanHydrationDiagnostics.FallbackUsed);
        Assert.Equal("MeteorShower", response.PlanHydrationDiagnostics.ResolvedEventType);
        Assert.Equal("Geminids Meteor Shower Peak", response.PlanHydrationDiagnostics.ResolvedEventName);
        Assert.Equal("IN-RJ-UDAIPUR", response.PlanHydrationDiagnostics.ResolvedRegionId);
    }

    [Fact]
    public async Task NarrationGenerationRejectsMissingPlanIdInsteadOfUsingFallbacks()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MediaFactoryDbContext(options);
        var service = new NarrationGenerationService(db);
        var request = new NarrationPreviewRequest(Guid.NewGuid().ToString("D"), null!, null!, null, "en", "", null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GeneratePreviewAsync(request, CancellationToken.None));
    }
}
