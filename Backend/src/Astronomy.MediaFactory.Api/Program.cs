using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Serilog;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables();
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddMediaFactory(builder.Configuration);
var app = builder.Build();
app.MapGet("/", () => Results.Ok(new { service = "Astronomy.MediaFactory.Api", status = "ok" }));
app.MapGet("/api/pipelines/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAsync(20, ct)));
app.MapGet("/api/pipelines/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) => { var item = await repository.GetAsync(id, ct); return item is null ? Results.NotFound() : Results.Ok(item); });
app.MapPost("/api/pipelines/run", async (RunPipelineRequest request, PipelineOrchestrator orchestrator, CancellationToken ct) => { var result = await orchestrator.RunAsync(request, ct); return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Completed.")); });
app.MapPost("/api/jobs/enqueue", async (EnqueuePipelineJobRequest request, IPipelineJobQueue queue, CancellationToken ct) =>
{
    try
    {
        var job = await queue.EnqueueAsync(request, ct);
        return Results.Ok(job);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
});
app.MapGet("/api/jobs/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentJobsAsync(50, ct)));
app.MapGet("/api/jobs/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetJobAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.Run();
