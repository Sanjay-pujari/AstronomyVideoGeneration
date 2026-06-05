using System.Net;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SkyfieldVisibilityClientTests
{
    [Fact]
    public async Task CalculateAsync_UsesNightPlanRequestContract()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "locationName":"Udaipur",
              "timezone":"Asia/Kolkata",
              "targetDate":"2026-06-05",
              "sunsetLocal":"2026-06-05T19:17:00+05:30",
              "sunriseLocal":"2026-06-06T05:42:00+05:30",
              "nightWindowStartUtc":"2026-06-05T13:47:00Z",
              "nightWindowEndUtc":"2026-06-06T00:12:00Z",
              "visibleObjects":[{"objectName":"Jupiter","objectType":"Planet","isVisible":true,"visibilityReason":"Highest altitude above threshold during night window","bestUtcTime":"2026-06-05T22:00:00+00:00","altitudeDegrees":42.5,"azimuthDegrees":110,"samples":[]}],
              "notVisibleObjects":[]
            }
            """, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://skyfield.test") };
        var client = new SkyfieldVisibilityClient(httpClient, Options.Create(EnabledOptions()), NullLogger<SkyfieldVisibilityClient>.Instance);

        var response = await client.CalculateAsync(new SkyfieldVisibilityRequest(
            "udaipur",
            "Udaipur",
            24.5854,
            73.7125,
            "Asia/Kolkata",
            new DateOnly(2026, 6, 5),
            [new SkyfieldVisibilityCandidateRequest("JUPITER", "Jupiter", "Planet")]), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("JUPITER", response.Objects.Single().ObjectCode);
        Assert.True(response.Objects.Single().Visible);
        Assert.Equal(42.5, response.Objects.Single().MaxAltitudeDegrees);
        Assert.Equal(110, response.Objects.Single().BestViewingAzimuthDegrees);
        Assert.Equal("/visibility/night-plan", handler.RequestUri?.AbsolutePath);
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;
        Assert.Equal("2026-06-05", root.GetProperty("date").GetString());
        Assert.Equal("Udaipur", root.GetProperty("locationName").GetString());
        Assert.False(root.TryGetProperty("targetDate", out _));
        Assert.False(root.TryGetProperty("objectCodes", out _));
        Assert.Equal("Jupiter", root.GetProperty("candidates")[0].GetProperty("objectName").GetString());
        Assert.Equal("Planet", root.GetProperty("candidates")[0].GetProperty("objectType").GetString());
    }

    [Fact]
    public async Task CalculateAsync_ReturnsValidationBodyFor422()
    {
        const string validationBody = "{\"detail\":[{\"loc\":[\"body\",\"date\"],\"msg\":\"Input should use yyyy-MM-dd\"}]}";
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(validationBody, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://skyfield.test") };
        var client = new SkyfieldVisibilityClient(httpClient, Options.Create(EnabledOptions()), NullLogger<SkyfieldVisibilityClient>.Instance);

        var response = await client.CalculateAsync(new SkyfieldVisibilityRequest(
            "udaipur",
            "Udaipur",
            24.5854,
            73.7125,
            "Asia/Kolkata",
            new DateOnly(2026, 6, 5),
            [new SkyfieldVisibilityCandidateRequest("JUPITER", "Jupiter", "Planet")]), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Contains("Skyfield sidecar returned status 422", response.ErrorMessage);
        Assert.Contains("Input should use yyyy-MM-dd", response.ErrorMessage);
    }

    private static SkyfieldSidecarOptions EnabledOptions() => new()
    {
        Enabled = true,
        BaseUrl = "http://skyfield.test",
        TimeoutSeconds = 5
    };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
