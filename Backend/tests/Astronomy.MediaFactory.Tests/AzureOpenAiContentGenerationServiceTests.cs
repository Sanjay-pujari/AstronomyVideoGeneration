using System.Net;
using System.Text;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ContentType = Astronomy.MediaFactory.Contracts.ContentType;

namespace Astronomy.MediaFactory.Tests;

public sealed class AzureOpenAiContentGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsValidatedModelResponse_WhenJsonIsValid()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = BuildSuccessResponse("{\"title\":\"Sky Highlights\",\"description\":\"Tonight's best events\",\"tags\":[\"astronomy\",\"planets\"],\"estimatedDurationSeconds\":780,\"scriptBody\":\"Look to the southwest for Jupiter.\"}")
            });

        var sut = CreateService(handler);
        var result = await sut.GenerateAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.Equal("Sky Highlights", result.Title);
        Assert.Equal("Tonight's best events", result.Description);
        Assert.Equal(780, result.EstimatedDurationSeconds);
        Assert.Contains("planets", result.Tags);
        Assert.Contains("Jupiter", result.ScriptBody);
    }

    [Fact]
    public async Task GenerateAsync_FallsBack_WhenModelReturnsUnexpectedProperties()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = BuildSuccessResponse("{\"title\":\"Sky Highlights\",\"description\":\"Tonight's best events\",\"tags\":[\"astronomy\"],\"estimatedDurationSeconds\":780,\"scriptBody\":\"Look to the southwest for Jupiter.\",\"extra\":true}")
            });

        var sut = CreateService(handler);
        var result = await sut.GenerateAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.StartsWith("What to See in the Sky", result.Title);
        Assert.Equal(900, result.EstimatedDurationSeconds);
        Assert.Contains(ContentType.DailySkyGuide.ToString(), result.Tags);
    }

    [Fact]
    public async Task GenerateAsync_UsesManagedIdentityBearerToken_WhenConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = BuildSuccessResponse("{\"title\":\"Sky Highlights\",\"description\":\"Tonight\",\"tags\":[\"astronomy\"],\"estimatedDurationSeconds\":60,\"scriptBody\":\"Observe Jupiter.\"}")
            };
        });

        var sut = CreateService(
            handler,
            new AzureOpenAiOptions
            {
                Endpoint = "https://example.openai.azure.com",
                ChatDeployment = "gpt-test",
                UseManagedIdentity = true,
                ManagedIdentityClientId = "client-id"
            },
            new StubTokenCredential("managed-token"));

        await sut.GenerateAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("managed-token", capturedRequest.Headers.Authorization?.Parameter);
        Assert.False(capturedRequest.Headers.Contains("api-key"));
    }

    [Fact]
    public async Task GenerateShortAsync_ReturnsShortPayload_WhenJsonIsValid()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = BuildSuccessResponse("{\"hook\":\"Watch this nebula rise tonight!\",\"shortScript\":\"Step outside right after sunset and look west for a glowing patch.\",\"title\":\"Nebula in 60 Seconds\",\"tags\":[\"shorts\",\"astronomy\"],\"sceneNarrationSegments\":[{\"sceneId\":\"sky-overview\",\"sceneTitle\":\"Sky overview\",\"visualTarget\":\"whole sky\",\"narrationText\":\"overview\"},{\"sceneId\":\"moon\",\"sceneTitle\":\"Moon focus\",\"visualTarget\":\"moon\",\"narrationText\":\"moon narration\"},{\"sceneId\":\"jupiter\",\"sceneTitle\":\"Jupiter focus\",\"visualTarget\":\"jupiter\",\"narrationText\":\"jupiter narration\"},{\"sceneId\":\"planet-secondary\",\"sceneTitle\":\"Secondary planet\",\"visualTarget\":\"mars\",\"narrationText\":\"mars narration\"},{\"sceneId\":\"constellation\",\"sceneTitle\":\"Constellation\",\"visualTarget\":\"orion\",\"narrationText\":\"orion narration\"}]}")
            });

        var sut = CreateService(handler);
        var result = await sut.GenerateShortAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.Equal("Nebula in 60 Seconds", result.Title);
        Assert.Contains("shorts", result.Tags);
        Assert.InRange(result.EstimatedDurationSeconds, 30, 60);
        Assert.Equal(5, result.SceneNarrationSegments.Count);
        Assert.Contains(result.SceneNarrationSegments, s => s.SceneId == "moon" && s.NarrationText.Contains("moon narration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SceneNarrationSegments, s => s.SceneId == "jupiter" && s.NarrationText.Contains("jupiter narration", StringComparison.OrdinalIgnoreCase));
    }

    private static AzureOpenAiContentGenerationService CreateService(
        HttpMessageHandler handler,
        AzureOpenAiOptions? options = null,
        TokenCredential? credential = null)
    {
        var client = new HttpClient(handler);
        var resolvedOptions = Options.Create(options ?? new AzureOpenAiOptions
        {
            Endpoint = "https://example.openai.azure.com",
            ApiKey = "test-key",
            ChatDeployment = "gpt-test"
        });

        return new AzureOpenAiContentGenerationService(
            client,
            resolvedOptions,
            new StubPromptBuilder(),
            NullLogger<AzureOpenAiContentGenerationService>.Instance,
            credential);
    }

    private static StringContent BuildSuccessResponse(string content)
        => new(
            $$"""
            {
              "choices": [
                {
                  "message": {
                    "content": "{{content}}"
                  }
                }
              ]
            }
            """,
            Encoding.UTF8,
            "application/json");

    private static AstronomyContext BuildContext() => new()
    {
        Date = new DateOnly(2026, 3, 16),
        LocationName = "Udaipur, India",
        Events =
        [
            new AstronomyEventModel
            {
                Category = "Planet",
                ObjectName = "Jupiter",
                VisibilityWindow = "during evening",
                Direction = "southwest",
                ObservationTool = "a small telescope",
                Details = "its cloud bands are visible",
                Score = 0.95
            }
        ]
    };

    private sealed class StubPromptBuilder : IPromptBuilder
    {
        public string Build(ContentType contentType, AstronomyContext context, PromptFeedbackContext? feedbackContext = null) => "prompt";
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class StubTokenCredential(string token) : TokenCredential
    {
        private readonly AccessToken _accessToken = new(token, DateTimeOffset.UtcNow.AddMinutes(10));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => _accessToken;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(_accessToken);
    }
}
