using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using CoreModel = Astronomy.MediaFactory.Core;
using SidecarModel = Astronomy.MediaFactory.AstroData.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastFoundationTests
{
    [Theory]
    [InlineData("IN-RJ-UDAIPUR")]
    [InlineData("in-rj-udaipur")]
    [InlineData("In-Rj-Udaipur")]
    public async Task ContextBuilder_Resolves_RegionId_Case_Insensitively(string inputRegionId)
    {
        var scheduler = Options.Create(new SchedulerOptions
        {
            Regions = new RegionSchedulingOptions
            {
                Items =
                [
                    new RegionScheduleOptions { RegionId = "INDIA-UDAIPUR", DisplayName = "Udaipur", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" }
                ]
            }
        });
        var sidecar = new StubSkyfieldSidecarClient();
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new RegionResolutionService(scheduler), sidecar, NullLogger<WeeklySkyForecastContextBuilder>.Instance);

        var context = await builder.BuildAsync(new CoreModel.WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", inputRegionId, "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), GenerateSscScripts: false, Diagnostics: true), CancellationToken.None);

        Assert.Equal("INDIA-UDAIPUR", context.RegionId);
        Assert.Equal("INDIA-UDAIPUR", sidecar.LastRequest!.RegionId);
    }

    [Fact]
    public async Task ContextBuilder_Unknown_Region_Returns_Clear_Validation_Error()
    {
        var scheduler = Options.Create(new SchedulerOptions { Regions = new RegionSchedulingOptions { Items = [] } });
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new RegionResolutionService(scheduler), new StubSkyfieldSidecarClient(), NullLogger<WeeklySkyForecastContextBuilder>.Instance);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            builder.BuildAsync(new CoreModel.WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "in-rj-udaipur", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), GenerateSscScripts: false, Diagnostics: true), CancellationToken.None));

        Assert.Contains("Region 'IN-RJ-UDAIPUR' is not configured in region settings.", ex.Message, StringComparison.Ordinal);
        var resolutionEx = Assert.IsType<WeeklySkyForecastRegionResolutionException>(ex);
        Assert.Equal("IN-RJ-UDAIPUR", resolutionEx.RequestedRegionId);
        Assert.Empty(resolutionEx.AvailableRegionIds);
    }

    [Fact]
    public async Task ContextBuilder_Uses_Configured_Region_Only_Without_Custom_Dictionary()
    {
        var scheduler = Options.Create(new SchedulerOptions
        {
            Regions = new RegionSchedulingOptions
            {
                Items =
                [
                    new RegionScheduleOptions { RegionId = "INDIA-UDAIPUR", DisplayName = "Udaipur", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" },
                    new RegionScheduleOptions { RegionId = "india-udaipur", DisplayName = "Udaipur Duplicate", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" }
                ]
            }
        });
        var sidecar = new StubSkyfieldSidecarClient();
        var logger = new TestLogger<WeeklySkyForecastContextBuilder>();
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new RegionResolutionService(scheduler), sidecar, logger);

        var context = await builder.BuildAsync(new CoreModel.WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "INDIA-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), GenerateSscScripts: false, Diagnostics: true), CancellationToken.None);

        Assert.Equal("INDIA-UDAIPUR", context.RegionId);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Duplicate region configuration found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkyfieldClient_Calls_WeeklySky_Endpoint()
    {
        var handler = new StubHandler((req, _) =>
        {
            Assert.Equal("/forecast/weekly-sky", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"regionId\":\"IN-RJ-UDAIPUR\",\"locationName\":\"Udaipur\",\"timezone\":\"Asia/Kolkata\",\"weekStartDate\":\"2026-05-22\",\"weekEndDate\":\"2026-05-28\",\"days\":[],\"weeklyHighlights\":[],\"recommendedNights\":[],\"warnings\":[]}", Encoding.UTF8, "application/json")
            };
        });
        var client = new SidecarModel.SkyfieldSidecarClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8010") }, NullLogger<SidecarModel.SkyfieldSidecarClient>.Instance);
        var response = await client.GetWeeklySkyForecastAsync(new SidecarModel.WeeklySkyForecastSkyfieldRequest { RegionId = "IN-RJ-UDAIPUR", LocationName = "Udaipur", Latitude = 24, Longitude = 73, Timezone = "Asia/Kolkata", WeekStartDate = "2026-05-22", Language = "en" }, CancellationToken.None);
        Assert.NotNull(response);
        Assert.True(response!.Success);
    }

    [Fact]
    public async Task SkyfieldClient_Deserializes_BestPlanet_Object_Response()
    {
        var handler = new StubHandler((req, _) =>
        {
            Assert.Equal("/forecast/weekly-sky", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"regionId\":\"IN-RJ-UDAIPUR\",\"locationName\":\"Udaipur\",\"timezone\":\"Asia/Kolkata\",\"weekStartDate\":\"2026-05-22\",\"weekEndDate\":\"2026-05-28\",\"days\":[],\"weeklyHighlights\":[],\"recommendedNights\":[],\"bestPlanetOfWeek\":{\"objectCode\":\"JUPITER\",\"objectName\":\"Jupiter\",\"objectType\":\"Planet\",\"visible\":true,\"visibilityScore\":95,\"photographyScore\":90,\"viewingDirection\":\"SE\",\"reason\":\"Excellent\"},\"bestMoonNight\":{\"date\":\"2026-05-24\",\"score\":87,\"reason\":\"Moon visibility\",\"bestObjects\":[\"MOON\"],\"bestStartUtc\":\"2026-05-24T18:00:00Z\",\"bestEndUtc\":\"2026-05-24T20:00:00Z\"},\"bestPhotographyNight\":{\"date\":\"2026-05-26\",\"score\":92,\"reason\":\"Dark sky window\",\"bestObjects\":[\"JUPITER\"],\"bestStartUtc\":\"2026-05-26T18:00:00Z\",\"bestEndUtc\":\"2026-05-26T20:00:00Z\"},\"warnings\":[]}", Encoding.UTF8, "application/json")
            };
        });
        var client = new SidecarModel.SkyfieldSidecarClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8010") }, NullLogger<SidecarModel.SkyfieldSidecarClient>.Instance);
        var response = await client.GetWeeklySkyForecastAsync(new SidecarModel.WeeklySkyForecastSkyfieldRequest { RegionId = "IN-RJ-UDAIPUR", LocationName = "Udaipur", Latitude = 24, Longitude = 73, Timezone = "Asia/Kolkata", WeekStartDate = "2026-05-22", Language = "en" }, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("JUPITER", response!.BestPlanetOfWeek?.ObjectCode);
    }

    [Fact]
    public async Task Segment_Metadata_Path_Disables_Publishing_And_Analytics()
    {
        var planner = new WeeklySkyForecastSegmentPlanner();
        var context = BuildContext();
        var segments = await planner.BuildAsync(context, CancellationToken.None);
        Assert.True(segments.LongSegments.Count >= 6);
        Assert.True(segments.ShortSegments.Count >= 3);

        var scenePlanner = new LegacyWeeklyVisualAssetGenerator(NullLogger<LegacyWeeklyVisualAssetGenerator>.Instance);
        var scenes = await scenePlanner.BuildAsync(context, segments, CancellationToken.None);
        Assert.All(scenes.Scenes.Where(x => !string.IsNullOrWhiteSpace(x.TargetObjectCode)), x => Assert.Contains(x.TargetObjectCode!, context.DailyForecasts.SelectMany(d => d.VisibleObjects).Where(v => v.Visible).Select(v => v.ObjectCode).Append("Moon")));

        var metadata = await new WeeklySkyForecastMetadataBuilder().BuildAsync(context, segments, CancellationToken.None);
        Assert.Contains("2026-05-22", metadata.WeekRange);
        Assert.Equal("Udaipur", metadata.RegionName);

        var paths = new CategoryOutputPathResolver(Options.Create(new RenderingOptions { WorkingDirectory = "media-output" })).Resolve("WeeklySkyForecast", context.WeekStartDate, "IN-RJ-UDAIPUR", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Contains("media-output", paths.RootDirectory);
        Assert.Contains("weeklyskyforecast/2026-05-22/in-rj-udaipur", paths.RootDirectory.ToLowerInvariant());
    }

    
    [Fact]
    public async Task ScenePlanner_Uses_Object_Specific_And_Recommended_Night_Times()
    {
        var tz = "Asia/Kolkata";
        var jupiterTime = DateTime.Parse("2026-05-25T18:40:00Z");
        var moonTime = DateTime.Parse("2026-05-24T19:10:00Z");
        var recommendedStart = DateTime.Parse("2026-05-26T17:30:00Z");

        var dayMoon = new CoreModel.DailySkyForecastContextItem(new DateOnly(2026, 5, 24), DateTime.UtcNow, DateTime.UtcNow, "Waxing", 30, null, null,
            [new CoreModel.WeeklySkyForecastVisibleObjectItem("Moon", "Moon", "Moon", true, null, null, null, null, 110, moonTime, 80, 75, "E", "Great")], [], recommendedStart, recommendedStart, 80, "Good");
        var dayJupiter = new CoreModel.DailySkyForecastContextItem(new DateOnly(2026, 5, 25), DateTime.UtcNow, DateTime.UtcNow, "Waxing", 40, null, null,
            [new CoreModel.WeeklySkyForecastVisibleObjectItem("Jupiter", "Jupiter", "Planet", true, null, null, null, null, 150, jupiterTime, 90, 88, "SE", "Great")], [], recommendedStart, recommendedStart, 90, "Great");
        var daySummary = new CoreModel.DailySkyForecastContextItem(new DateOnly(2026, 5, 26), DateTime.UtcNow, DateTime.UtcNow, "Waxing", 50, null, null,
            [new CoreModel.WeeklySkyForecastVisibleObjectItem("Saturn", "Saturn", "Planet", true, null, null, null, null, 175, DateTime.Parse("2026-05-26T20:00:00Z"), 70, 70, "S", "Good")], [], recommendedStart, recommendedStart.AddHours(2), 86, "Great");

        var context = new CoreModel.WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, tz, new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "en",
            [dayMoon, dayJupiter, daySummary],
            [new CoreModel.WeeklySkyForecastHighlightItem(1, "best_moon_night", "Best Moon", "Desc", new DateOnly(2026, 5, 24), moonTime, "Moon", 90, "moon_closeup")],
            [new CoreModel.RecommendedObservationNight(new DateOnly(2026, 5, 25), 90, "Best Jupiter", ["Jupiter"], jupiterTime, jupiterTime.AddHours(1)), new CoreModel.RecommendedObservationNight(new DateOnly(2026, 5, 26), 88, "Best summary", ["Saturn"], recommendedStart, recommendedStart.AddHours(1))],
            "Jupiter", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 26), []);

        var planner = new LegacyWeeklyVisualAssetGenerator(NullLogger<LegacyWeeklyVisualAssetGenerator>.Instance);
        var segments = await new WeeklySkyForecastSegmentPlanner().BuildAsync(context, CancellationToken.None);
        var scenes = await planner.BuildAsync(context, segments, CancellationToken.None);

        Assert.Equal(moonTime, scenes.Scenes.Single(x => x.SceneCode == "BestMoonNight").CaptureTimeUtc);
        Assert.Equal(jupiterTime, scenes.Scenes.Single(x => x.SceneCode == "BestPlanetOfWeek").CaptureTimeUtc);
        Assert.Equal(recommendedStart, scenes.Scenes.Single(x => x.SceneCode == "WeeklySummaryMap").CaptureTimeUtc);
    }

    [Fact]
    public async Task ContextBuilder_Uses_Skyfield_BestMoonNight_Source_Of_Truth()
    {
        var scheduler = Options.Create(new SchedulerOptions { Regions = new RegionSchedulingOptions { Items = [new RegionScheduleOptions { RegionId = "INDIA-UDAIPUR", DisplayName = "Udaipur", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" }] } });
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new RegionResolutionService(scheduler), new StubSkyfieldSidecarClientWithMoonNight(), NullLogger<WeeklySkyForecastContextBuilder>.Instance);
        var context = await builder.BuildAsync(new CoreModel.WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "INDIA-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), GenerateSscScripts: false, Diagnostics: true), CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 5, 27), context.BestMoonNight);
    }

    [Fact]
    public async Task ContextBuilder_Maps_BestPlanet_And_BestNights_From_Sidecar_Objects()
    {
        var scheduler = Options.Create(new SchedulerOptions { Regions = new RegionSchedulingOptions { Items = [new RegionScheduleOptions { RegionId = "INDIA-UDAIPUR", DisplayName = "Udaipur", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" }] } });
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new RegionResolutionService(scheduler), new StubSkyfieldSidecarClientWithBestObjects(), NullLogger<WeeklySkyForecastContextBuilder>.Instance);
        var context = await builder.BuildAsync(new CoreModel.WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "INDIA-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), GenerateSscScripts: false, Diagnostics: true), CancellationToken.None);

        Assert.Equal("JUPITER", context.BestPlanetOfWeek);
        Assert.Equal(new DateOnly(2026, 5, 27), context.BestMoonNight);
        Assert.Equal(new DateOnly(2026, 5, 28), context.BestPhotographyNight);
    }
private static CoreModel.WeeklySkyForecastContext BuildContext()
    {
        var day = new CoreModel.DailySkyForecastContextItem(new DateOnly(2026, 5, 22), DateTime.UtcNow, DateTime.UtcNow, "Waxing", 20, null, null,
            [new CoreModel.WeeklySkyForecastVisibleObjectItem("JUP", "Jupiter", "Planet", true, null, null, null, null, 200, DateTime.UtcNow, 90, 85, "SE", "Great")],
            [], DateTime.UtcNow, DateTime.UtcNow, 80, "Good");
        return new CoreModel.WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "en", [day, day, day, day, day, day, day], [new CoreModel.WeeklySkyForecastHighlightItem(1, "BestNight", "Title", "Desc", new DateOnly(2026, 5, 23), DateTime.UtcNow, "JUP", 90, "WeeklyHighlight")], [new CoreModel.RecommendedObservationNight(new DateOnly(2026, 5, 23), 90, "Reason", ["JUP"], DateTime.UtcNow, DateTime.UtcNow)], "JUP", new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24), []);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> cb) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(cb(request, cancellationToken));
    }

    private sealed class StubSkyfieldSidecarClient : SidecarModel.ISkyfieldSidecarClient
    {
        public SidecarModel.WeeklySkyForecastSkyfieldRequest? LastRequest { get; private set; }
        public Task<SidecarModel.SkyfieldDailySkyResponse?> GetDailySkyAsync(SidecarModel.SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SidecarModel.SkyfieldNightPlanRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(SidecarModel.WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<SidecarModel.WeeklySkyForecastSkyfieldResponse?>(new SidecarModel.WeeklySkyForecastSkyfieldResponse
            {
                Success = true,
                RegionId = request.RegionId,
                LocationName = request.LocationName,
                Timezone = request.Timezone,
                WeekStartDate = request.WeekStartDate,
                WeekEndDate = "2026-05-28",
                Days = [],
                WeeklyHighlights = [],
                RecommendedNights = [],
                Warnings = []
            });
        }
    }

    
    private sealed class StubSkyfieldSidecarClientWithMoonNight : SidecarModel.ISkyfieldSidecarClient
    {
        public Task<SidecarModel.SkyfieldDailySkyResponse?> GetDailySkyAsync(SidecarModel.SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SidecarModel.SkyfieldNightPlanRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(SidecarModel.WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
            => Task.FromResult<SidecarModel.WeeklySkyForecastSkyfieldResponse?>(new SidecarModel.WeeklySkyForecastSkyfieldResponse
            {
                Success = true,
                RegionId = request.RegionId,
                LocationName = request.LocationName,
                Timezone = request.Timezone,
                WeekStartDate = request.WeekStartDate,
                WeekEndDate = "2026-05-28",
                Days = [new SidecarModel.DailySkyForecastItem { Date = "2026-05-24" }],
                WeeklyHighlights = [new SidecarModel.WeeklyHighlightItem { Order = 1, HighlightType = "best_moon_night", Date = "2026-05-24" }],
                RecommendedNights = [],
                BestMoonNight = new SidecarModel.RecommendedObservationNight { Date = "2026-05-27", BestStartUtc = DateTime.Parse("2026-05-27T18:00:00Z"), BestEndUtc = DateTime.Parse("2026-05-27T20:00:00Z") },
                Warnings = []
            });
    }

    private sealed class StubSkyfieldSidecarClientWithBestObjects : SidecarModel.ISkyfieldSidecarClient
    {
        public Task<SidecarModel.SkyfieldDailySkyResponse?> GetDailySkyAsync(SidecarModel.SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SidecarModel.SkyfieldNightPlanRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SidecarModel.WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(SidecarModel.WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
            => Task.FromResult<SidecarModel.WeeklySkyForecastSkyfieldResponse?>(new SidecarModel.WeeklySkyForecastSkyfieldResponse
            {
                Success = true,
                RegionId = request.RegionId,
                LocationName = request.LocationName,
                Timezone = request.Timezone,
                WeekStartDate = request.WeekStartDate,
                WeekEndDate = "2026-05-28",
                Days = [],
                WeeklyHighlights = [],
                RecommendedNights = [],
                BestPlanetOfWeek = new SidecarModel.VisibleObjectForecastItem { ObjectCode = "JUPITER", ObjectName = "Jupiter", ObjectType = "Planet", Visible = true },
                BestMoonNight = new SidecarModel.RecommendedObservationNight { Date = "2026-05-27", BestStartUtc = DateTime.Parse("2026-05-27T18:00:00Z"), BestEndUtc = DateTime.Parse("2026-05-27T20:00:00Z") },
                BestPhotographyNight = new SidecarModel.RecommendedObservationNight { Date = "2026-05-28", BestStartUtc = DateTime.Parse("2026-05-28T18:00:00Z"), BestEndUtc = DateTime.Parse("2026-05-28T20:00:00Z") },
                Warnings = []
            });
    }

private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
