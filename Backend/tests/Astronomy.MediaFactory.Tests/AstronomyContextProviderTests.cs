using System.Net;
using System.Text;
using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.AstroData.Services;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyContextProviderTests
{
    [Fact]
    public async Task BuildContextAsync_UsesNightPlanVisibleObjects_WhenAvailable()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(new SkyfieldNightPlanResponse
        {
            LocationName = "Udaipur, India",
            Timezone = "Asia/Kolkata",
            VisibleObjects =
            [
                new SkyfieldObjectVisibility
                {
                    ObjectName = "Jupiter",
                    ObjectType = "Planet",
                    IsVisible = true,
                    BestLocalTime = "2026-03-17T20:30:00",
                    BestUtcTime = "2026-03-17T15:00:00Z",
                    DirectionLabel = "SW",
                    AltitudeDegrees = 55,
                    VisibilityReason = "Visible high in the southwest during early evening."
                }
            ]
        }));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);

        Assert.Contains(result.Events, e => e.ObjectName == "Jupiter" && e.Category == "Planet");
        Assert.Contains(result.SceneObservationContexts, s => s.ObjectName == "Jupiter" && s.IsVisible);
    }

    [Fact]
    public async Task BuildContextAsync_UsesObjectSpecificPeakSampleTimes()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(new SkyfieldNightPlanResponse
        {
            LocationName = "Udaipur, India",
            Timezone = "Asia/Kolkata",
            NightWindowStartLocal = "2026-03-17T19:00:00",
            VisibleObjects =
            [
                CreateVisible("Venus", "Planet", "2026-03-17T21:10:00", 42, [Sample("2026-03-17T20:10:00", 20), Sample("2026-03-17T21:10:00", 42)]),
                CreateVisible("Mars", "Planet", "2026-03-17T22:10:00", 51, [Sample("2026-03-17T21:40:00", 35), Sample("2026-03-17T22:10:00", 51)]),
                CreateVisible("Jupiter", "Planet", "2026-03-17T23:10:00", 60, [Sample("2026-03-17T22:30:00", 49), Sample("2026-03-17T23:10:00", 60)])
            ]
        }));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);
        var objectScenes = result.SceneObservationContexts.Where(s => s.SceneType == "Object").ToList();

        Assert.Equal(3, objectScenes.Select(s => s.LocalObservationTime).Distinct().Count());
        Assert.Equal(new DateTime(2026, 3, 17, 21, 10, 0), objectScenes.Single(s => s.ObjectName == "Venus").LocalObservationTime);
        Assert.Equal(new DateTime(2026, 3, 17, 22, 10, 0), objectScenes.Single(s => s.ObjectName == "Mars").LocalObservationTime);
        Assert.Equal(new DateTime(2026, 3, 17, 23, 10, 0), objectScenes.Single(s => s.ObjectName == "Jupiter").LocalObservationTime);
        Assert.Equal(["object-1", "object-2", "object-3"], objectScenes.Select(s => s.SceneId).ToArray());
        Assert.True(objectScenes.SequenceEqual(objectScenes.OrderBy(s => s.LocalObservationTime)));
    }

    [Fact]
    public async Task BuildContextAsync_ShiftsDuplicateTimes_AndAddsNarrationSpecificTime()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(new SkyfieldNightPlanResponse
        {
            LocationName = "Udaipur, India",
            Timezone = "Asia/Kolkata",
            NightWindowStartLocal = "2026-03-17T19:00:00",
            VisibleObjects =
            [
                CreateVisible("Venus", "Planet", "2026-03-17T21:10:00", 42, [Sample("2026-03-17T21:10:00", 42)]),
                CreateVisible("Jupiter", "Planet", "2026-03-17T21:10:00", 41, [Sample("2026-03-17T21:10:00", 41)])
            ]
        }));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);
        var venus = result.SceneObservationContexts.Single(s => s.ObjectName == "Venus");
        var jupiter = result.SceneObservationContexts.Single(s => s.ObjectName == "Jupiter");

        Assert.NotEqual(venus.LocalObservationTime, jupiter.LocalObservationTime);
        Assert.Contains("Best around", venus.NarrationFocus);
        Assert.Contains("Best around", jupiter.NarrationFocus);
    }

    [Fact]
    public async Task BuildContextAsync_UsesMidpointFallback_WhenNoSamplesProvided()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(new SkyfieldNightPlanResponse
        {
            LocationName = "Udaipur, India",
            Timezone = "Asia/Kolkata",
            NightWindowStartLocal = "2026-03-17T19:00:00",
            VisibleObjects =
            [
                new SkyfieldObjectVisibility
                {
                    ObjectName = "Moon",
                    ObjectType = "Moon",
                    IsVisible = true,
                    AltitudeDegrees = 35,
                    VisibilityReason = "Visible through most of the evening.",
                    Samples =
                    [
                        Sample("2026-03-17T20:00:00", 20),
                        Sample("2026-03-17T22:00:00", 25)
                    ]
                }
            ]
        }));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);
        var moonScene = result.SceneObservationContexts.Single(s => s.ObjectName == "Moon");
        Assert.Equal(new DateTime(2026, 3, 17, 21, 0, 0), moonScene.LocalObservationTime);
    }

    [Fact]
    public async Task BuildContextAsync_FallsBackToSafeOverview_WhenNightPlanUnavailable()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(null));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);

        Assert.Single(result.Events);
        Assert.DoesNotContain(result.Events, e => e.ObjectName.Contains("Jupiter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildContextAsync_UsesObservationOptions_WhenRequestLocationAndTimezoneAreEmpty()
    {
        var fake = new FakeSkyfieldSidecarClient(new SkyfieldNightPlanResponse { VisibleObjects = [] });
        var provider = CreateProvider(fake);
        _ = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "", "", CancellationToken.None);
        Assert.Equal("Pune, India", fake.LastNightPlanRequest!.LocationName);
        Assert.Equal("Asia/Kolkata", fake.LastNightPlanRequest.Timezone);
    }

    [Fact]
    public async Task GetDailySkyAsync_ReturnsNull_WhenResponseIsInvalidJson()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("http://localhost:8010") };

        var sut = new SkyfieldSidecarClient(httpClient, NullLogger<SkyfieldSidecarClient>.Instance);

        var result = await sut.GetDailySkyAsync(new SkyfieldDailySkyRequest
        {
            Date = "2026-03-17",
            LocationName = "Udaipur, India",
            Latitude = 24.5854,
            Longitude = 73.7125,
            Timezone = "Asia/Kolkata"
        }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDailySkyAsync_ReturnsNull_WhenRequestContractIsInvalid()
    {
        var wasCalled = false;
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            wasCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("http://localhost:8010") };

        var sut = new SkyfieldSidecarClient(httpClient, NullLogger<SkyfieldSidecarClient>.Instance);

        var result = await sut.GetDailySkyAsync(new SkyfieldDailySkyRequest
        {
            Date = "17-03-2026",
            LocationName = "Udaipur, India",
            Latitude = 24.5854,
            Longitude = 73.7125,
            Timezone = "Asia/Kolkata"
        }, CancellationToken.None);

        Assert.Null(result);
        Assert.False(wasCalled);
    }

    [Fact]
    public async Task GetDailySkyAsync_ReturnsNull_WhenResponseContractIsInvalid()
    {
        var invalidPayload = """
        {
          "date": "2026-03-17",
          "locationName": "Udaipur, India",
          "timezone": "Asia/Kolkata",
          "events": [
            {
              "category": "Planet",
              "objectName": "",
              "visibilityWindow": "19:10-23:30",
              "direction": "SW",
              "observationTool": "Binoculars / telescope",
              "details": "Visible high in the southwest during early evening."
            }
          ],
          "visualIdeas": []
        }
        """;

        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(invalidPayload, Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("http://localhost:8010") };

        var sut = new SkyfieldSidecarClient(httpClient, NullLogger<SkyfieldSidecarClient>.Instance);

        var result = await sut.GetDailySkyAsync(new SkyfieldDailySkyRequest
        {
            Date = "2026-03-17",
            LocationName = "Udaipur, India",
            Latitude = 24.5854,
            Longitude = 73.7125,
            Timezone = "Asia/Kolkata"
        }, CancellationToken.None);

        Assert.Null(result);
    }

    private static AstronomyContextProvider CreateProvider(ISkyfieldSidecarClient skyfieldSidecarClient)
    {
        var options = Options.Create(new AstronomyApiOptions { NasaBaseUrl = "http://localhost", NasaApiKey = "demo" });
        var nasaClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("planetary/apod") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"title\":\"Test APOD\",\"explanation\":\"Summary\",\"url\":\"https://example.com\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }));

        return new AstronomyContextProvider(
            new NasaApodClient(nasaClient, options, NullLogger<NasaApodClient>.Instance),
            new NasaNeoWsClient(nasaClient, options, NullLogger<NasaNeoWsClient>.Instance),
            skyfieldSidecarClient,
            NullLogger<AstronomyContextProvider>.Instance,
            Options.Create(new SkyfieldSidecarOptions { Enabled = true, BaseUrl = "http://localhost:8010" }),
            Options.Create(new ObservationOptions { LocationName = "Pune, India", Timezone = "Asia/Kolkata", Latitude = 18.5204, Longitude = 73.8567 }));
    }

    private sealed class FakeSkyfieldSidecarClient : ISkyfieldSidecarClient
    {
        private readonly SkyfieldNightPlanResponse? _response;
        public SkyfieldNightPlanRequest? LastNightPlanRequest { get; private set; }
        public FakeSkyfieldSidecarClient(SkyfieldNightPlanResponse? response) => _response = response;
        public Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => Task.FromResult<SkyfieldDailySkyResponse?>(null);
        public Task<SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SkyfieldNightPlanRequest request, CancellationToken cancellationToken)
        {
            LastNightPlanRequest = request;
            return Task.FromResult(_response);
        }
    }

    private static SkyfieldObjectVisibility CreateVisible(string objectName, string objectType, string bestLocalTime, double altitudeDegrees, List<SkyfieldVisibilitySample> samples)
        => new()
        {
            ObjectName = objectName,
            ObjectType = objectType,
            IsVisible = true,
            BestLocalTime = bestLocalTime,
            AltitudeDegrees = altitudeDegrees,
            VisibilityReason = $"Best visibility for {objectName}.",
            Samples = samples
        };

    private static SkyfieldVisibilitySample Sample(string localTime, double altitudeDegrees)
        => new()
        {
            LocalTime = localTime,
            UtcTime = $"{localTime}Z",
            AltitudeDegrees = altitudeDegrees,
            AzimuthDegrees = 180,
            DirectionLabel = "S",
            IsVisibleCandidate = altitudeDegrees >= 10
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_handler(request));
    }
}
