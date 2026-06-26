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
        Assert.Equal("the Jupiter–Venus conjunction", response.NarrationContextDiagnostics.DisplayTitle);
        Assert.Equal("Udaipur", response.NarrationContextDiagnostics.DisplayLocation);
        Assert.Equal("PlanetConjunction", response.NarrationContextDiagnostics.Family);
        Assert.NotNull(response.NarrationContextDiagnostics.ObservationContextDiagnostics);
        Assert.Equal("June 7, 2026 19:30", response.NarrationContextDiagnostics.ObservationContextDiagnostics.GeometricPeakTime);
        Assert.Equal("from 7:00 PM to 8:30 PM IST", response.NarrationContextDiagnostics.ObservationContextDiagnostics.ObservationWindow);
        Assert.Equal("western sky after sunset", response.NarrationContextDiagnostics.ObservationContextDiagnostics.ObservationDirection);
        Assert.Equal("metadata.bestViewingWindowLocal", response.NarrationContextDiagnostics.ObservationContextDiagnostics.WindowSource);
        Assert.Equal("metadata.skyDirectionHint", response.NarrationContextDiagnostics.ObservationContextDiagnostics.DirectionSource);
        Assert.False(response.NarrationContextDiagnostics.ObservationContextDiagnostics.FallbackUsed);
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "BestTime" && scene.Narration == "For observers in Udaipur, the best viewing window runs from 7:00 PM to 8:30 PM IST. Look toward western sky after sunset while both planets are above the horizon.");
        Assert.DoesNotContain("2026-06-07", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+05:30", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minimum angular separation", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consolidated from", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IN-RJ-UDAIPUR", narration, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task NarrationGenerationLocalizesHindiMeteorDirectionAndRejectsEnglishDirectionLeakage()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "MeteorShower",
            EventName: "Geminids Meteor Shower Peak",
            ShortTitle: "Geminids",
            Language: "hi",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-12-14",
                localPeakTime = "2026-12-14 11:30 +05:30",
                bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST",
                skyDirectionHint = "East to overhead after 10 PM",
                moonInterference = "low"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var narration = string.Join(" ", response.Scenes.Select(s => s.Narration));

        Assert.Equal("रात 10 बजे के बाद पूर्वी आकाश से सिर के ऊपर तक", response.FormattingDiagnostics.Direction);
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "BestTime" && scene.Narration == "उदयपुर में देखने का सुझाया समय 14 दिसंबर 2026 की रात 12 बजे से सुबह 5 बजे तक है। रात 10 बजे के बाद पूर्वी आकाश से सिर के ऊपर तक देखें।");
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "Hook" && scene.Narration.Contains("जेमिनिड्स", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Scenes, scene => scene.Narration.Contains("Geminids", StringComparison.Ordinal));
        Assert.DoesNotContain(" to ", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("after", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("before", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PM", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AM", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("East", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overhead", narration, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
    }


    [Fact]
    public async Task NarrationGenerationHindiNamedFullMoonAcceptsLocalPeakTimeDevanagariDate()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "NamedFullMoon",
            EventName: "Wolf Moon",
            ShortTitle: "Wolf Moon",
            Language: "hi",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-01-02",
                localPeakTime = "2026-01-03 15:32 +0530"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var hook = Assert.Single(response.Scenes.Where(scene => scene.ScenePurpose == "Hook"));

        Assert.Contains("3 जनवरी 2026", hook.Narration, StringComparison.Ordinal);
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
        Assert.Contains(hook.Validation.Warnings, warning => warning.Contains("\"requestedLanguage\":\"hi\"", StringComparison.Ordinal)
            && warning.Contains("\"resolvedLanguage\":\"hi\"", StringComparison.Ordinal)
            && warning.Contains("3 जनवरी 2026", StringComparison.Ordinal)
            && warning.Contains("०३ जनवरी २०२६", StringComparison.Ordinal)
            && warning.Contains("\"dateValidationPassed\":true", StringComparison.Ordinal)
            && warning.Contains("\"dateSourceUsed\":\"localPeakTime\"", StringComparison.Ordinal));
    }


    [Fact]
    public async Task NarrationGenerationAllowsRawShortTitleInsideApprovedMeteorDisplayNames()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "MeteorShower",
            EventName: "Geminids Meteor Shower Peak",
            ShortTitle: "Geminids",
            Language: "en",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-12-14",
                bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST",
                skyDirectionHint = "East to overhead after 10 PM"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var narration = string.Join(" ", response.Scenes.Select(scene => scene.Narration));

        Assert.Contains("the Geminids meteor shower", narration, StringComparison.Ordinal);
        Assert.Contains("the Geminids", narration, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw short title appears.", response.Validation.Errors);
        Assert.DoesNotContain("Geminids Meteor Shower Peak", narration, StringComparison.Ordinal);
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
    }

    [Fact]
    public async Task NarrationGenerationHindiBestTimeDoesNotPrependDateWhenViewingWindowAlreadyIncludesDate()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "MeteorShower",
            EventName: "Geminids Meteor Shower Peak",
            ShortTitle: "Geminids",
            Language: "hi",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-12-14",
                bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST",
                skyDirectionHint = "East to overhead after 10 PM"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var bestTime = Assert.Single(response.Scenes.Where(scene => scene.ScenePurpose == "BestTime"));

        Assert.Equal("14 दिसंबर 2026 की रात 12 बजे से सुबह 5 बजे तक", response.FormattingDiagnostics.ViewingWindow);
        Assert.Equal("उदयपुर में देखने का सुझाया समय 14 दिसंबर 2026 की रात 12 बजे से सुबह 5 बजे तक है। रात 10 बजे के बाद पूर्वी आकाश से सिर के ऊपर तक देखें।", bestTime.Narration);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(bestTime.Narration, "14 दिसंबर 2026"));
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
    }

    [Fact]
    public async Task NarrationGenerationHindiBestTimeValidationRejectsRepeatedDatePhrase()
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: "MeteorShower",
            EventName: "Geminids Meteor Shower Peak",
            ShortTitle: "Geminids",
            Language: "hi",
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(new
            {
                eventDate = "2026-12-14",
                bestViewingWindowLocal = "2026-12-14 2026-12-14 00:00–05:00 IST",
                skyDirectionHint = "East to overhead after 10 PM"
            }));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.False(response.Validation.IsValid);
        Assert.Contains("BestTime contains the same Hindi date phrase twice.", response.Validation.Errors);
    }

    [Theory]
    [MemberData(nameof(Phase14FamilyLanguageCases))]
    public async Task NarrationGenerationPhase14FamilyLevelNarrationValidatesForEnglishAndHindi(string eventType, string eventName, string shortTitle, string language, object metadata)
    {
        var request = new NarrationPreviewRequest(
            PlanId: null,
            EventType: eventType,
            EventName: eventName,
            ShortTitle: shortTitle,
            Language: language,
            RegionId: "IN-RJ-UDAIPUR",
            Format: "ShortVideo",
            EventMetadata: JsonSerializer.SerializeToElement(metadata));
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);
        var bestTime = Assert.Single(response.Scenes.Where(scene => scene.ScenePurpose == "BestTime"));
        var fact = Assert.Single(response.Scenes.Where(scene => scene.ScenePurpose == "InterestingFact"));
        var narration = string.Join(" ", response.Scenes.Select(scene => scene.Narration));

        Assert.True(bestTime.Validation.IsValid, string.Join("; ", bestTime.Validation.Errors));
        Assert.True(fact.Validation.IsValid, string.Join("; ", fact.Validation.Errors));
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
        if (language == "hi")
        {
            Assert.DoesNotContain("northeast after midnight", narration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("eastern sky", narration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("open sky", narration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("after 10 PM", narration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("early evening", narration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("before sunrise", narration, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static IEnumerable<object[]> Phase14FamilyLanguageCases()
    {
        yield return ["MeteorShower", "Geminids Meteor Shower Peak", "Geminids", "en", new { eventDate = "2026-12-14", bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST", skyDirectionHint = "East to overhead after 10 PM" }];
        yield return ["MeteorShower", "Geminids Meteor Shower Peak", "Geminids", "hi", new { eventDate = "2026-12-14", bestViewingWindowLocal = "2026-12-14 00:00–05:00 IST", skyDirectionHint = "East to overhead after 10 PM" }];
        yield return ["MeteorShower", "Perseids Meteor Shower Peak", "Perseids", "en", new { eventDate = "2026-08-12", bestViewingWindowLocal = "2026-08-12 00:00–05:00 IST", skyDirectionHint = "Northeast after midnight" }];
        yield return ["MeteorShower", "Perseids Meteor Shower Peak", "Perseids", "hi", new { eventDate = "2026-08-12", bestViewingWindowLocal = "2026-08-12 00:00–05:00 IST", skyDirectionHint = "Northeast after midnight" }];
        yield return ["PlanetConjunction", "Jupiter Venus Conjunction", "Jupiter Venus", "en", new { eventDate = "2026-06-07", localPeakTime = "2026-06-07 19:30 +05:30", bestViewingWindowLocal = "2026-06-07 19:00–20:30 IST", skyDirectionHint = "western sky after sunset" }];
        yield return ["PlanetConjunction", "Jupiter Venus Conjunction", "Jupiter Venus", "hi", new { eventDate = "2026-06-07", localPeakTime = "2026-06-07 19:30 +05:30", bestViewingWindowLocal = "2026-06-07 19:00–20:30 IST", skyDirectionHint = "western sky after sunset" }];
        yield return ["PlanetConjunction", "Mars Jupiter Conjunction", "Mars Jupiter", "en", new { eventDate = "2026-01-11", localPeakTime = "2026-01-11 05:30 +05:30", bestViewingWindowLocal = "2026-01-11 04:30–06:00 IST", skyDirectionHint = "eastern horizon before sunrise" }];
        yield return ["PlanetConjunction", "Mars Jupiter Conjunction", "Mars Jupiter", "hi", new { eventDate = "2026-01-11", localPeakTime = "2026-01-11 05:30 +05:30", bestViewingWindowLocal = "2026-01-11 04:30–06:00 IST", skyDirectionHint = "eastern horizon before sunrise" }];
        yield return ["PlanetGrouping", "Mercury Venus Mars planet grouping", "Mercury Venus Mars", "en", new { eventDate = "2026-02-20", bestViewingWindowLocal = "2026-02-20 19:00–20:00 IST", skyDirectionHint = "western sky after sunset" }];
        yield return ["PlanetGrouping", "Mercury Venus Mars planet grouping", "Mercury Venus Mars", "hi", new { eventDate = "2026-02-20", bestViewingWindowLocal = "2026-02-20 19:00–20:00 IST", skyDirectionHint = "western sky after sunset" }];
        yield return ["NamedFullMoon", "Wolf Moon", "Wolf Moon", "en", new { eventDate = "2026-01-03", bestViewingWindowLocal = "2026-01-03 18:00–23:00 IST", skyDirectionHint = "eastern sky near moonrise" }];
        yield return ["NamedFullMoon", "Wolf Moon", "Wolf Moon", "hi", new { eventDate = "2026-01-03", bestViewingWindowLocal = "2026-01-03 18:00–23:00 IST", skyDirectionHint = "eastern sky near moonrise" }];
        yield return ["NamedFullMoon", "Strawberry Moon", "Strawberry Moon", "en", new { eventDate = "2026-06-29", bestViewingWindowLocal = "2026-06-29 19:00–23:30 IST", skyDirectionHint = "eastern sky near moonrise" }];
        yield return ["NamedFullMoon", "Strawberry Moon", "Strawberry Moon", "hi", new { eventDate = "2026-06-29", bestViewingWindowLocal = "2026-06-29 19:00–23:30 IST", skyDirectionHint = "eastern sky near moonrise" }];
        yield return ["SolarEclipse", "Total Solar Eclipse", "Total Solar Eclipse", "en", new { eventDate = "2026-08-12", bestViewingWindowLocal = "2026-08-12 17:00–19:00 IST", skyDirectionHint = "toward the Sun" }];
        yield return ["SolarEclipse", "Total Solar Eclipse", "Total Solar Eclipse", "hi", new { eventDate = "2026-08-12", bestViewingWindowLocal = "2026-08-12 17:00–19:00 IST", skyDirectionHint = "toward the Sun" }];
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
        Assert.NotNull(response.NarrationContextDiagnostics.ObservationContextDiagnostics);
        Assert.Equal("from midnight to 5:00 AM IST", response.NarrationContextDiagnostics.ObservationContextDiagnostics.ObservationWindow);
        Assert.Equal("East to overhead after 10 PM", response.NarrationContextDiagnostics.ObservationContextDiagnostics.ObservationDirection);
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

    [Fact]
    public async Task NarrationGenerationKeepsRequestedSolarEclipseMetadataWhenPlanHydrationConflicts()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MediaFactoryDbContext(options);
        var planId = Guid.Parse("58a48363-60bf-421a-98d7-135c163de821");
        var intelligence = new Astronomy.MediaFactory.Core.AstronomyEventIntelligence
        {
            EventType = "NamedFullMoon",
            Title = "Wolf Moon",
            Summary = "Wolf Moon",
            Language = "en",
            StartUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 8, 12, 17, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            MetadataJson = JsonSerializer.Serialize(new
            {
                shortTitle = "Wolf Moon",
                eventDate = "2026-08-12",
                localPeakTime = "2026-08-12 22:30 +05:30",
                bestViewingWindowLocal = "2026-08-12 17:00–19:00 IST",
                skyDirectionHint = "toward the Sun"
            })
        };
        var plan = new Astronomy.MediaFactory.Core.ContentGenerationPlan
        {
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            Title = "Stale full moon plan title",
            AstronomyEventIntelligence = intelligence,
            AstronomyEventIntelligenceId = intelligence.Id,
            PlannedFormat = "ShortVideo"
        };
        plan.AssignId(planId);
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var request = new NarrationPreviewRequest(
            planId.ToString("D"),
            "SolarEclipse",
            "Total Solar Eclipse",
            "total solar eclipse",
            "en",
            "IN-RJ-UDAIPUR",
            "ShortVideo",
            null);
        var service = new NarrationGenerationService(db);

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("SolarEclipse", response.EventType);
        Assert.Equal("Total Solar Eclipse", response.EventName);
        Assert.Equal("total solar eclipse", response.ShortTitle);
        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors));
        Assert.NotNull(response.NarrationContextDiagnostics);
        Assert.Equal("SolarEclipse", response.NarrationContextDiagnostics.Family);
        Assert.Contains(response.Scenes, scene => scene.ScenePurpose == "Hook" && scene.Narration.Contains("the total solar eclipse", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Raw internal event title appears.", response.Validation.Errors);
        Assert.DoesNotContain("Raw short title appears.", response.Validation.Errors);
        Assert.NotNull(response.PlanHydrationDiagnostics);
        Assert.True(response.PlanHydrationDiagnostics.EventMetadataConflictDetected);
        Assert.Equal("NamedFullMoon", response.PlanHydrationDiagnostics.HydratedEventType);
        Assert.Equal("SolarEclipse", response.PlanHydrationDiagnostics.FinalResolvedEventType);
        Assert.Equal("Wolf Moon", response.PlanHydrationDiagnostics.HydratedShortTitle);
        Assert.Equal("total solar eclipse", response.PlanHydrationDiagnostics.FinalShortTitle);
        Assert.Equal("CurrentEventLockWins", response.PlanHydrationDiagnostics.ConflictResolution);
    }
}
