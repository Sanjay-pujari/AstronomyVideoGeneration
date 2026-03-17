using System.Net;
using System.Text;
using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AzureOpenAiContentGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsValidatedModelResponse_WhenJsonIsValid()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"title\":\"Sky Highlights\",\"description\":\"Tonight's best events\",\"tags\":[\"astronomy\",\"planets\"],\"estimatedDurationSeconds\":780,\"scriptBody\":\"Look to the southwest for Jupiter.\"}"
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
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
                Content = new StringContent("""
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"title\":\"Sky Highlights\",\"description\":\"Tonight's best events\",\"tags\":[\"astronomy\"],\"estimatedDurationSeconds\":780,\"scriptBody\":\"Look to the southwest for Jupiter.\",\"extra\":true}"
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handler);
        var result = await sut.GenerateAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.StartsWith("What to See in the Sky", result.Title);
        Assert.Equal(900, result.EstimatedDurationSeconds);
        Assert.Contains(ContentType.DailySkyGuide.ToString(), result.Tags);
    }



    [Fact]
    public async Task GenerateShortAsync_ReturnsShortPayload_WhenJsonIsValid()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"hook\":\"Watch this nebula rise tonight!\",\"shortScript\":\"Step outside right after sunset and look west for a glowing patch.\",\"title\":\"Nebula in 60 Seconds\",\"tags\":[\"shorts\",\"astronomy\"]}"
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handler);
        var result = await sut.GenerateShortAsync(ContentType.DailySkyGuide, BuildContext(), CancellationToken.None);

        Assert.Equal("Nebula in 60 Seconds", result.Title);
        Assert.Contains("shorts", result.Tags);
        Assert.InRange(result.EstimatedDurationSeconds, 30, 60);
    }

    private static AzureOpenAiContentGenerationService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new AzureOpenAiOptions
        {
            Endpoint = "https://example.openai.azure.com",
            ApiKey = "test-key",
            ChatDeployment = "gpt-test"
        });

        return new AzureOpenAiContentGenerationService(
            client,
            options,
            new StubPromptBuilder(),
            NullLogger<AzureOpenAiContentGenerationService>.Instance);
    }

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
        public string Build(ContentType contentType, AstronomyContext context) => "prompt";
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
