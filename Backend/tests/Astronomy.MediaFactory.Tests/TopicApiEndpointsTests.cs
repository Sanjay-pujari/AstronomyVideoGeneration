using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class TopicApiEndpointsTests
{
    [Fact]
    public async Task RecommendedEndpoint_ReturnsPlanShape()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITopicSelectionService>(new FakeTopicSelectionService());
        var app = builder.Build();

        app.MapGet("/api/topics/recommended", async (ITopicSelectionService topicSelectionService, CancellationToken ct) =>
            Results.Ok(await topicSelectionService.BuildPlanAsync(new TopicSelectionRequest { Date = DateOnly.FromDateTime(DateTime.UtcNow), LocationName = "Pune" }, ct)));

        await app.StartAsync();
        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/topics/recommended");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TopicSelectionPlan>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.PrimaryLongForm);
        Assert.NotEmpty(payload.RankedOpportunities);

        await app.StopAsync();
    }

    private sealed class FakeTopicSelectionService : ITopicSelectionService
    {
        public Task<TopicSelectionPlan> BuildPlanAsync(TopicSelectionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new TopicSelectionPlan
            {
                PrimaryLongForm = new ContentOpportunity
                {
                    TitleCandidate = "Best thing to watch tonight",
                    ContentType = ContentType.DailySkyGuide,
                    EventType = "moon phase",
                    ObjectName = "Moon + Jupiter",
                    Date = request.Date,
                    PriorityScore = 0.9,
                    ObservabilityScore = 0.9,
                    TimelinessScore = 0.95,
                    EducationalValueScore = 0.8,
                    GrowthPotentialScore = 0.75,
                    Rationale = "High timeliness and visibility",
                    IsLongFormCandidate = true,
                    IsShortCandidate = true
                },
                RankedOpportunities =
                [
                    new ContentOpportunity
                    {
                        TitleCandidate = "Best thing to watch tonight",
                        ContentType = ContentType.DailySkyGuide,
                        EventType = "moon phase",
                        ObjectName = "Moon + Jupiter",
                        Date = request.Date,
                        PriorityScore = 0.9,
                        ObservabilityScore = 0.9,
                        TimelinessScore = 0.95,
                        EducationalValueScore = 0.8,
                        GrowthPotentialScore = 0.75,
                        Rationale = "High timeliness and visibility",
                        IsLongFormCandidate = true,
                        IsShortCandidate = true
                    }
                ]
            });
    }
}
