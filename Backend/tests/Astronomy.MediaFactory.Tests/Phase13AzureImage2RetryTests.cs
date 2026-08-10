using System.Net;
using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13AzureImage2RetryTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientFailureThenSuccessRetriesOnlyFailedPage(HttpStatusCode firstStatus)
    {
        using var handler = new SequenceHandler(firstStatus, HttpStatusCode.OK);
        var result = await Generate(handler);

        Assert.True(result.ProviderSucceeded);
        Assert.Equal(2, result.AttemptCount);
        Assert.True(result.RetryPerformed);
        Assert.Equal(new int?[] { (int)firstStatus, 200 }, result.ProviderStatusCodes);
        Assert.Equal(new string?[] { "request-1", "request-2" }, result.ProviderRequestIds);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task TwoTransientFailuresStopAfterOneRetry()
    {
        using var handler = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);
        var result = await Generate(handler);

        Assert.False(result.ProviderSucceeded);
        Assert.True(result.TransientFailure);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task PermanentFailureDoesNotRetry()
    {
        using var handler = new SequenceHandler(HttpStatusCode.BadRequest);
        var result = await Generate(handler);

        Assert.False(result.ProviderSucceeded);
        Assert.False(result.TransientFailure);
        Assert.False(result.RetryPerformed);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(408)] [InlineData(429)] [InlineData(500)] [InlineData(502)] [InlineData(503)] [InlineData(504)]
    public void RetryClassificationIsBoundedToApprovedStatuses(int status) =>
        Assert.True(AstroPulseGalleryService.IsTransientAzureImageFailure(status));

    private static async Task<AstroPulseGalleryService.AzureImage2GenerationResult> Generate(SequenceHandler handler)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase13-provider-{Guid.NewGuid():N}.png");
        try
        {
            return await AstroPulseGalleryService.GenerateBackgroundWithAzureImage2Async(
                new AzureOpenAIForImageOptions { Endpoint = "https://unit.test", ImageDeployment = "image2", ApiKey = "not-a-secret" },
                "safe prompt", path, AstroPulseGalleryAspect.Landscape, default, new HttpMessageInvoker(handler),
                (_, _) => Task.CompletedTask);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        internal int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(CallCount, statuses.Length - 1)];
            CallCount++;
            var response = new HttpResponseMessage(status);
            response.Headers.Add("x-request-id", $"request-{CallCount}");
            response.Content = status == HttpStatusCode.OK
                ? new StringContent("{\"data\":[{\"b64_json\":\"AQID\"}]}", Encoding.UTF8, "application/json")
                : new StringContent("{\"error\":{\"code\":\"InternalServerError\"}}", Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}
