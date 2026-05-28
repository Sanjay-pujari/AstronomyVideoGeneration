using System.Net;
using System.Text;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AzureOpenAICinematicImageGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsProviderNotConfigured_WhenImageDeploymentMissing()
    {
        var sut = CreateGenerator(new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called.")), new AzureOpenAIForImageOptions
        {
            Endpoint = "https://example.openai.azure.com",
            ApiKey = "test-key"
        });

        var result = await sut.GenerateAsync(CreateRequest(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "image.png")), CancellationToken.None);

        Assert.False(sut.IsConfigured);
        Assert.False(result.ProviderConfigured);
        Assert.Equal("ProviderNotConfigured", result.GenerationStatus);
        Assert.Contains(result.Warnings, warning => warning.Contains("AzureOpenAIForImage:ImageDeployment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_SavesB64ImageToPlannedImagePath_WhenConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var plannedPath = Path.Combine(tempDirectory, "generated.png");
        var imageBytes = Encoding.ASCII.GetBytes("fake png bytes for generator persistence");
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                {
                  "data": [
                    { "b64_json": "{{Convert.ToBase64String(imageBytes)}}" }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var sut = CreateGenerator(handler, new AzureOpenAIForImageOptions
        {
            Endpoint = "https://example.openai.azure.com",
            ApiKey = "test-key",
            ImageDeployment = "image-test"
        });

        try
        {
            var result = await sut.GenerateAsync(CreateRequest(plannedPath), CancellationToken.None);

            Assert.True(sut.IsConfigured);
            Assert.True(result.ProviderConfigured);
            Assert.Equal("Generated", result.GenerationStatus);
            Assert.Equal(plannedPath, result.ImagePath);
            Assert.True(File.Exists(plannedPath));
            Assert.Equal(imageBytes, await File.ReadAllBytesAsync(plannedPath));
            Assert.NotNull(capturedRequest);
            Assert.Equal("https://example.openai.azure.com/openai/deployments/image-test/images/generations?api-version=2024-10-21", capturedRequest!.RequestUri!.ToString());
            Assert.True(capturedRequest.Headers.Contains("api-key"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static AzureOpenAICinematicImageGenerator CreateGenerator(HttpMessageHandler handler, AzureOpenAIForImageOptions options)
        => new(
            new HttpClient(handler),
            Options.Create(options),
            NullLogger<AzureOpenAICinematicImageGenerator>.Instance);

    private static AICinematicAssetRequest CreateRequest(string plannedPath) => new(
        AssetId: "asset-1",
        SegmentId: "segment-1",
        SegmentType: "OpeningHook",
        EpisodeType: "longform",
        AssetCode: "cinematic_weekly_sky_reveal",
        UsageRole: "cinematic_segment_support",
        EmotionalTone: "awe",
        PacingRole: "cinematic_support",
        StyleProfile: "cinematic_wide_night_sky_reveal",
        Prompt: "Create a cinematic astronomy still image.",
        NegativePrompt: "No labels.",
        TargetWidth: 1920,
        TargetHeight: 1080,
        PlannedImagePath: plannedPath);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
