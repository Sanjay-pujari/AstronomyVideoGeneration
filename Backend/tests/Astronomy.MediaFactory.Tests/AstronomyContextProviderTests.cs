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
    public async Task BuildContextAsync_UsesSidecarEvents_WhenAvailable()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(new SkyfieldDailySkyResponse
        {
            Date = "2026-03-17",
            LocationName = "Udaipur, India",
            Timezone = "Asia/Kolkata",
            Events =
            [
                new SkyfieldDailySkyEvent
                {
                    Category = "Planet",
                    ObjectName = "Jupiter",
                    VisibilityWindow = "19:10-23:30",
                    Direction = "SW",
                    ObservationTool = "Binoculars / telescope",
                    Details = "Visible high in the southwest during early evening."
                }
            ],
            VisualIdeas = [new SkyfieldVisualIdea { Title = "Jupiter in the southwest", Description = "Show Jupiter position in the southwest sky after dusk." }]
        }));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);

        Assert.Contains(result.Events, e => e.ObjectName == "Jupiter" && e.Category == "Planet");
        Assert.Contains(result.VisualIdeas, v => v.Title == "Jupiter in the southwest");
    }

    [Fact]
    public async Task BuildContextAsync_FallsBackToDemoEvents_WhenSidecarUnavailable()
    {
        var provider = CreateProvider(new FakeSkyfieldSidecarClient(null));

        var result = await provider.BuildContextAsync(new DateOnly(2026, 3, 17), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", CancellationToken.None);

        Assert.Contains(result.Events, e => e.ObjectName == "Jupiter" && e.Category == "Planet");
        Assert.Contains(result.Events, e => e.ObjectName == "Orion Nebula" && e.Category == "Deep Sky");
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
            new NasaApodClient(nasaClient, options),
            new NasaNeoWsClient(nasaClient, options),
            skyfieldSidecarClient,
            NullLogger<AstronomyContextProvider>.Instance);
    }

    private sealed class FakeSkyfieldSidecarClient : ISkyfieldSidecarClient
    {
        private readonly SkyfieldDailySkyResponse? _response;
        public FakeSkyfieldSidecarClient(SkyfieldDailySkyResponse? response) => _response = response;
        public Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => Task.FromResult(_response);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_handler(request));
    }
}
