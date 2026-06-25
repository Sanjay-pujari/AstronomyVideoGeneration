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

    [Fact]
    public async Task NarrationGenerationNormalizesPlanetConjunctionProductionMetadata()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "PlanetConjunction",
            EventName: "Jupiter Venus Conjunction peaks 2026-06-07 IN-RJ-UDAIPUR minimum angular separation 0.5 degrees consolidated from feed",
            ShortTitle: "Jupiter Venus 2026-06-07 Udaipur +05:30",
            Language: "en",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-06-07",
                localPeakTime = "2026-06-07 19:30 +05:30",
                bestViewingWindowLocal = "2026-06-07 19:00–20:30 IST",
                skyDirectionHint = "western sky after sunset"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var narration = string.Join(" ", response.Scenes.Select(s => s.Narration));

        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
        Assert.NotNull(response.NarrationContextDiagnostics);
        Assert.Equal("Jupiter and Venus Conjunction", response.NarrationContextDiagnostics.DisplayTitle);
        Assert.Equal("Udaipur", response.NarrationContextDiagnostics.DisplayLocation);
        Assert.Equal("PlanetConjunction", response.NarrationContextDiagnostics.Family);
        Assert.DoesNotContain("2026-06-07", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+05:30", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minimum angular separation", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consolidated from", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IN-RJ-UDAIPUR", narration, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("from midnight to 5:00 AM IST", response.FormattingDiagnostics.ViewingWindow);
        Assert.Equal("East to overhead after 10 PM", response.FormattingDiagnostics.Direction);
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "Hook" && scene.Narration == "On December 14, 2026, Geminids Meteor Shower will reach its peak, offering one of the year's best chances to see bright meteors streak across the night sky.");
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "InterestingFact" && scene.Narration == "Unlike most major meteor showers, the Geminids come from asteroid 3200 Phaethon rather than a traditional comet.");
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "BestTime" && scene.Narration == "For observers in Udaipur, the recommended viewing window runs from midnight to 5:00 AM IST on December 14, 2026. Look toward eastern sky toward overhead after 10 PM as the night deepens.");
        Assert.DoesNotContain(response.Scenes, scene => scene.Narration.Contains("Geminids Meteor Shower Peak", StringComparison.Ordinal));
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
        Assert.NotNull(response.NarrationContextDiagnostics);
        Assert.True(response.NarrationContextDiagnostics.NormalizerUsed);
        Assert.Equal("Geminids Meteor Shower", response.NarrationContextDiagnostics.DisplayTitle);
        Assert.Equal("Udaipur", response.NarrationContextDiagnostics.DisplayLocation);
        Assert.Equal("MeteorShower", response.NarrationContextDiagnostics.Family);
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
