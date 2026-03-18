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

public sealed class ExperimentApiEndpointsTests
{
    [Fact]
    public async Task ExperimentEndpoints_ReturnExpectedPayloads()
    {
        var service = new StubExperimentService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IContentExperimentService>(service);

        var app = builder.Build();
        app.MapGet("/api/experiments/recent", async (IContentExperimentService experimentService, CancellationToken ct) => Results.Ok(await experimentService.GetRecentExperimentsAsync(20, ct)));
        app.MapGet("/api/experiments/{id:guid}", async (Guid id, IContentExperimentService experimentService, CancellationToken ct) =>
        {
            var experiment = await experimentService.GetExperimentAsync(id, ct);
            return experiment is null ? Results.NotFound() : Results.Ok(experiment);
        });
        app.MapGet("/api/experiments/top-performing", async (IContentExperimentService experimentService, CancellationToken ct) => Results.Ok(await experimentService.GetTopPerformingExperimentsAsync(10, ct)));

        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/experiments/recent")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/experiments/{service.Experiment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/experiments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/experiments/top-performing")).StatusCode);

        var recent = await client.GetFromJsonAsync<List<ContentExperiment>>("/api/experiments/recent");
        Assert.Single(recent!);
        await app.StopAsync();
    }

    private sealed class StubExperimentService : IContentExperimentService
    {
        public ContentExperiment Experiment { get; } = new()
        {
            VideoId = Guid.NewGuid(),
            ExperimentType = ContentExperimentType.Title,
            Status = ContentExperimentStatus.Completed,
            Variants =
            [
                new ContentVariant { ContentExperimentId = Guid.NewGuid(), VariantType = ContentVariantType.TitleText, Value = "Alpha", Views = 100 },
                new ContentVariant { ContentExperimentId = Guid.NewGuid(), VariantType = ContentVariantType.TitleText, Value = "Beta", Views = 120, IsWinner = true }
            ]
        };

        public Task InitializeExperimentsAsync(PublishedVideo publishedVideo, OptimizedVideoMetadata metadata, ThumbnailPlan thumbnailPlan, MonetizationPlan? monetizationPlan, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ExperimentVariantAssignment> ResolveAssignmentsAsync(Guid videoId, CancellationToken cancellationToken) => Task.FromResult(new ExperimentVariantAssignment());
        public Task EvaluateRecentExperimentsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<ContentExperiment>> GetRecentExperimentsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ContentExperiment>>([Experiment]);
        public Task<ContentExperiment?> GetExperimentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == Experiment.Id ? Experiment : null);
        public Task<IReadOnlyCollection<ContentExperiment>> GetTopPerformingExperimentsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ContentExperiment>>([Experiment]);
        public Task<ExperimentFeedbackSnapshot> GetFeedbackSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new ExperimentFeedbackSnapshot());
    }
}
