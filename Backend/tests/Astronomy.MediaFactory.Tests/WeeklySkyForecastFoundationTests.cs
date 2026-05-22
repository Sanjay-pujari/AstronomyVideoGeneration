using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
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
                    new RegionScheduleOptions { RegionId = "IN-RJ-UDAIPUR", DisplayName = "Udaipur", Latitude = 24.58, Longitude = 73.68, Timezone = "Asia/Kolkata", Language = "en" }
                ]
            }
        });
        var sidecar = new StubSkyfieldSidecarClient();
        var builder = new WeeklySkyForecastContextBuilder(scheduler, sidecar, NullLogger<WeeklySkyForecastContextBuilder>.Instance);

        var context = await builder.BuildAsync(new WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", inputRegionId, "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), false, false, false, true), CancellationToken.None);

        Assert.Equal("IN-RJ-UDAIPUR", context.RegionId);
        Assert.Equal("IN-RJ-UDAIPUR", sidecar.LastRequest!.RegionId);
    }

    [Fact]
    public async Task ContextBuilder_Unknown_Region_Returns_Clear_Validation_Error()
    {
        var scheduler = Options.Create(new SchedulerOptions { Regions = new RegionSchedulingOptions { Items = [] } });
        var builder = new WeeklySkyForecastContextBuilder(scheduler, new StubSkyfieldSidecarClient(), NullLogger<WeeklySkyForecastContextBuilder>.Instance);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            builder.BuildAsync(new WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "in-rj-udaipur", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), false, false, false, true), CancellationToken.None));

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
        var builder = new WeeklySkyForecastContextBuilder(scheduler, sidecar, logger);

        var context = await builder.BuildAsync(new WeeklySkyForecastProductionRequest("WeeklySkyForecast", "en", "INDIA-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), false, false, false, true), CancellationToken.None);

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
        var client = new SkyfieldSidecarClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8010") }, NullLogger<SkyfieldSidecarClient>.Instance);
        var response = await client.GetWeeklySkyForecastAsync(new WeeklySkyForecastSkyfieldRequest { RegionId = "IN-RJ-UDAIPUR", LocationName = "Udaipur", Latitude = 24, Longitude = 73, Timezone = "Asia/Kolkata", WeekStartDate = "2026-05-22", Language = "en" }, CancellationToken.None);
        Assert.NotNull(response);
        Assert.True(response!.Success);
    }

    [Fact]
    public async Task Segment_Metadata_Path_Disables_Publishing_And_Analytics()
    {
        var planner = new WeeklySkyForecastSegmentPlanner();
        var context = BuildContext();
        var segments = await planner.BuildAsync(context, CancellationToken.None);
        Assert.True(segments.LongSegments.Count >= 6);
        Assert.True(segments.ShortSegments.Count >= 3);

        var scenePlanner = new WeeklySkyForecastSscScenePlanner();
        var scenes = await scenePlanner.BuildAsync(context, segments, CancellationToken.None);
        Assert.All(scenes.Scenes.Where(x => !string.IsNullOrWhiteSpace(x.TargetObjectCode)), x => Assert.Contains(x.TargetObjectCode!, context.DailyForecasts.SelectMany(d => d.VisibleObjects).Where(v => v.Visible).Select(v => v.ObjectCode).Append("Moon")));

        var metadata = await new WeeklySkyForecastMetadataBuilder().BuildAsync(context, segments, CancellationToken.None);
        Assert.Contains("2026-05-22", metadata.WeekRange);
        Assert.Equal("Udaipur", metadata.RegionName);

        var paths = new CategoryOutputPathResolver(Options.Create(new RenderingOptions { WorkingDirectory = "media-output" })).Resolve("WeeklySkyForecast", context.WeekStartDate, "IN-RJ-UDAIPUR", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Contains("media-output", paths.RootDirectory);
        Assert.Contains("weeklyskyforecast/2026-05-22/in-rj-udaipur", paths.RootDirectory.ToLowerInvariant());
    }

    private static WeeklySkyForecastContext BuildContext()
    {
        var day = new DailySkyForecastContextItem(new DateOnly(2026, 5, 22), DateTime.UtcNow, DateTime.UtcNow, "Waxing", 20, null, null,
            [new WeeklySkyForecastVisibleObjectItem("JUP", "Jupiter", "Planet", true, null, null, null, null, DateTime.UtcNow, 90, 85, "SE", "Great")],
            [], DateTime.UtcNow, DateTime.UtcNow, 80, "Good");
        return new WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "en", [day, day, day, day, day, day, day], [new WeeklySkyForecastHighlightItem(1, "BestNight", "Title", "Desc", new DateOnly(2026, 5, 23), DateTime.UtcNow, "JUP", 90, "WeeklyHighlight")], [new RecommendedObservationNight(new DateOnly(2026, 5, 23), 90, "Reason", ["JUP"], DateTime.UtcNow, DateTime.UtcNow)], "JUP", new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24), []);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> cb) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(cb(request, cancellationToken));
    }

    private sealed class StubSkyfieldSidecarClient : ISkyfieldSidecarClient
    {
        public WeeklySkyForecastSkyfieldRequest? LastRequest { get; private set; }
        public Task<SkyfieldComputationResponse?> ComputeAsync(SkyfieldComputationRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SunMoonComputationResponse?> ComputeSunMoonAsync(SunMoonComputationRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<WeeklySkyForecastSkyfieldResponse?>(new WeeklySkyForecastSkyfieldResponse
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
