using Astronomy.MediaFactory.Api;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class JsonEndpointBodyReaderTests
{
    [Fact]
    public async Task PreviewAssetProductionEndpoint_ReturnsBadRequestForMalformedJson()
    {
        var previewService = new StubAssetProducerPreviewService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAstronomyAssetProducerPreviewService>(previewService);

        var app = builder.Build();
        app.MapPost("/api/astronomy-intelligence/preview-asset-production", async (HttpRequest httpRequest, IAstronomyAssetProducerPreviewService previews, ILogger<JsonEndpointBodyReaderTests> logger, CancellationToken ct) =>
        {
            var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<AstronomyAssetProducerPreviewRequest>(httpRequest, "request", logger, ct);
            if (requestBody.HasError)
            {
                return requestBody.ErrorResult!;
            }

            return Results.Ok(await previews.PreviewAssetProductionAsync(requestBody.Value!, ct));
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/api/astronomy-intelligence/preview-asset-production",
            new StringContent("""
                {
                  "regionId": "IN-RJ-UDAIPUR",
                  "maxJobs": 1
                }
                }
                """, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        Assert.Equal("Request body must be a single valid JSON object.", payload!["message"]?.ToString());
        Assert.Equal("request", payload["parameter"]?.ToString());
        Assert.Equal(0, previewService.Calls);

        await app.StopAsync();
    }

    [Fact]
    public async Task PreviewAssetProductionEndpoint_PassesValidJsonToService()
    {
        var previewService = new StubAssetProducerPreviewService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAstronomyAssetProducerPreviewService>(previewService);

        var app = builder.Build();
        app.MapPost("/api/astronomy-intelligence/preview-asset-production", async (HttpRequest httpRequest, IAstronomyAssetProducerPreviewService previews, ILogger<JsonEndpointBodyReaderTests> logger, CancellationToken ct) =>
        {
            var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<AstronomyAssetProducerPreviewRequest>(httpRequest, "request", logger, ct);
            if (requestBody.HasError)
            {
                return requestBody.ErrorResult!;
            }

            return Results.Ok(await previews.PreviewAssetProductionAsync(requestBody.Value!, ct));
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/astronomy-intelligence/preview-asset-production", new
        {
            regionId = "IN-RJ-UDAIPUR",
            maxJobs = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, previewService.Calls);
        Assert.Equal("IN-RJ-UDAIPUR", previewService.LastRequest?.RegionId);
        Assert.Equal(1, previewService.LastRequest?.MaxJobs);

        await app.StopAsync();
    }

    private sealed class StubAssetProducerPreviewService : IAstronomyAssetProducerPreviewService
    {
        public int Calls { get; private set; }
        public AstronomyAssetProducerPreviewRequest? LastRequest { get; private set; }

        public Task<AstronomyAssetProducerPreviewResult> PreviewAssetProductionAsync(AstronomyAssetProducerPreviewRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new AstronomyAssetProducerPreviewResult(0, 0, 0, [], [], []));
        }
    }
}
