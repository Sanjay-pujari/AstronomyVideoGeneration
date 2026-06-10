using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.SscIntelligence;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Narrative;
using Astronomy.SscIntelligence.Spatial;
using Astronomy.SscIntelligence.Resolution;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EventScoring;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Api;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddMediaFactorySecureConfiguration(builder.Environment);

var telemetryOptions = new TelemetryOptions();
builder.Configuration.GetSection(TelemetryOptions.SectionName).Bind(telemetryOptions);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
if (!string.IsNullOrWhiteSpace(telemetryOptions.ApplicationInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

const string DevelopmentCorsPolicy = "DevelopmentCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevelopmentCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
                builder.Environment.IsDevelopment()
                && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.Ordinal)))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddMediaFactory(builder.Configuration);
builder.Services.AddSscIntelligence();
builder.Services.AddSingleton<ISkyfieldTemporalResolver, SkyfieldTemporalResolver>();

var app = builder.Build();

app.Logger.LogInformation("Starting Astronomy.MediaFactory.Api in {Environment}", app.Environment.EnvironmentName);
var renderingOptions = app.Services.GetRequiredService<IOptions<RenderingOptions>>().Value;
var ffmpegConfigured = !string.IsNullOrWhiteSpace(renderingOptions.FfmpegPath);
var ffprobeConfigured = !string.IsNullOrWhiteSpace(renderingOptions.FfprobePath);
var ffmpegExists = ffmpegConfigured && File.Exists(renderingOptions.FfmpegPath);
var ffprobeExists = ffprobeConfigured && File.Exists(renderingOptions.FfprobePath);
if (ffmpegExists)
{
    app.Logger.LogInformation("FFmpeg executable resolved: {Path}", renderingOptions.FfmpegPath);
}
else
{
    app.Logger.LogWarning("FFmpeg executable is not available. configured={Configured}; path={Path}", ffmpegConfigured, renderingOptions.FfmpegPath);
}
if (ffprobeExists)
{
    app.Logger.LogInformation("FFprobe executable resolved: {Path}", renderingOptions.FfprobePath);
}
else
{
    app.Logger.LogWarning("FFprobe executable is not available. configured={Configured}; path={Path}", ffprobeConfigured, renderingOptions.FfprobePath);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(DevelopmentCorsPolicy);

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { service = "Astronomy.MediaFactory.Api", status = "ok" }));
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (ctx, _) => await ctx.Response.WriteAsJsonAsync(new { status = "live" })
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
        };

        await ctx.Response.WriteAsJsonAsync(payload);
    }
});
app.MapGet("/api/system/rendering/diagnostics", () =>
{
    var current = app.Services.GetRequiredService<IOptions<RenderingOptions>>().Value;
    var isFfmpegConfigured = !string.IsNullOrWhiteSpace(current.FfmpegPath);
    var isFfprobeConfigured = !string.IsNullOrWhiteSpace(current.FfprobePath);
    var isFfmpegExists = isFfmpegConfigured && File.Exists(current.FfmpegPath);
    var isFfprobeExists = isFfprobeConfigured && File.Exists(current.FfprobePath);
    var ffmpegVersion = GetVersion(current.FfmpegPath, isFfmpegExists);
    var ffprobeVersion = GetVersion(current.FfprobePath, isFfprobeExists);
    var writable = IsWritable(current.WorkingDirectory);

    return Results.Ok(new
    {
        ffmpegConfigured = isFfmpegConfigured,
        ffmpegExists = isFfmpegExists,
        ffprobeConfigured = isFfprobeConfigured,
        ffprobeExists = isFfprobeExists,
        ffmpegVersion = ffmpegVersion,
        ffprobeVersion = ffprobeVersion,
        currentUser = Environment.UserName,
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        workingDirectoryWritable = writable
    });
});

app.MapPost("/api/assets/celestial/extract-pack", async (ICelestialAssetPackExtractor extractor, CancellationToken ct) =>
{
    var report = await extractor.ExtractAsync(ct);
    return Results.Ok(report);
});

static object? GetVersion(string? executablePath, bool executableExists)
{
    if (!executableExists || string.IsNullOrWhiteSpace(executablePath))
    {
        return new { executed = false, exitCode = -1, stdout = "", stderr = "Executable not found." };
    }

    var psi = new ProcessStartInfo(executablePath, "-version")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = Process.Start(psi);
    if (process is null)
    {
        return new { executed = false, exitCode = -1, stdout = "", stderr = "Unable to start process." };
    }

    process.WaitForExit();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    return new { executed = true, exitCode = process.ExitCode, stdout, stderr };
}

static bool IsWritable(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return false;
    try
    {
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
        return true;
    }
    catch
    {
        return false;
    }
}

app.MapGet("/api/events/upcoming", async (int? days, string? regionId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var upcoming = await events.GetUpcomingAsync(days, ct);
    return Results.Ok(string.IsNullOrWhiteSpace(regionId) ? upcoming : upcoming.Where(e => e.GlobalVisibility || e.RegionId == regionId || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase))).ToArray());
});
app.MapPost("/api/alerts/subscribe", async (AlertSubscribeRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        var created = await alerts.SubscribeAsync(request, ct);
        return Results.Created($"/api/alerts/preferences/{created.SubscriberId}", created);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/alerts/preferences/{subscriberId:guid}", async (Guid subscriberId, ISkyAlertService alerts, CancellationToken ct) =>
{
    var item = await alerts.GetPreferencesAsync(subscriberId, ct);
    return item is null ? Results.NotFound(new { message = "Alert subscriber was not found." }) : Results.Ok(item);
});
app.MapPut("/api/alerts/preferences/{subscriberId:guid}", async (Guid subscriberId, AlertPreferenceUpdateRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        var item = await alerts.UpdatePreferencesAsync(subscriberId, request, ct);
        return item is null ? Results.NotFound(new { message = "Alert subscriber was not found." }) : Results.Ok(item);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/alerts/upcoming", async (string? regionId, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await alerts.GetUpcomingAsync(regionId, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapPost("/api/alerts/test", async (AlertTestRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await alerts.CreateTestAlertAsync(request, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});
app.MapPost("/api/alerts/unsubscribe/{subscriberId:guid}", async (Guid subscriberId, ISkyAlertService alerts, CancellationToken ct) =>
    await alerts.UnsubscribeAsync(subscriberId, ct) ? Results.Ok(new { subscriberId, isActive = false }) : Results.NotFound(new { message = "Alert subscriber was not found." }));

app.MapGet("/api/events/top", async (int? days, string? regionId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var top = await events.GetTopAsync(days, ct);
    return Results.Ok(string.IsNullOrWhiteSpace(regionId) ? top : top.Where(e => e.GlobalVisibility || e.RegionId == regionId || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase))).ToArray());
});
app.MapGet("/api/events/{eventId}", async (string eventId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var item = await events.GetByIdAsync(eventId, ct);
    return item is null ? Results.NotFound(new { message = $"Astronomy event '{eventId}' was not found." }) : Results.Ok(item);
});
app.MapPost("/api/events/refresh", async (int? days, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
    Results.Ok(await events.RefreshAsync(days, ct)));

app.MapPost("/api/astronomy-intelligence/detect-events", async (AstronomyEventDetectionRequest request, IAstronomyEventDetectionService detection, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy intelligence detect-events request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await detection.DetectEventsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-content-opportunities", async (AstronomyContentOpportunityRequest request, IAstronomyContentOpportunityService opportunities, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy content opportunity generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await opportunities.GenerateAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-question-answers", async (QuestionAnswerGenerationRequest request, IQuestionEngine questionEngine, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question answer generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await questionEngine.GenerateQuestionAnswersAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/validate-question-answer-set", async (QuestionAnswerValidationRequest request, IQuestionEngine questionEngine, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question answer validation request received for {RegionId}. EventId={EventId}; Language={Language}", request.RegionId, request.EventId, request.Language);
    try
    {
        return Results.Ok(await questionEngine.ValidateQuestionAnswerSetAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-question-scene-plan", async (QuestionScenePlanRequest request, IQuestionScenePlanner scenePlanner, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question scene plan generation request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        return Results.Ok(await scenePlanner.GenerateQuestionScenePlanAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/enrich-question-scene-plan", async (QuestionSceneIntentEnrichmentRequest request, IQuestionSceneIntentEnricher enricher, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question scene intent enrichment request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        return Results.Ok(await enricher.EnrichQuestionScenePlanAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-question-driven-narration", async (QuestionDrivenNarrationRequest request, IQuestionDrivenNarrationGenerator narrationGenerator, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question-driven narration generation request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        return Results.Ok(await narrationGenerator.GenerateQuestionDrivenNarrationAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/astronomy-intelligence/generate-hero-assets", async (HeroAssetStoryGenerationRequest request, IHeroAssetIntelligenceEngine heroAssetEngine, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Hero hook intelligence generation request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        var response = await heroAssetEngine.GenerateHeroAssetsAsync(request, ct);
        return Results.Ok(new
        {
            response.SelectedHook,
            response.AlternativeHooks,
            response.HookScores,
            response.HeroStory,
            response.HeroBlueprint,
            response.PlatformVariants,
            response.ReviewScores,
            response.Warnings,
            response.PhaseRequested,
            response.PhaseExecuted,
            response.StoryExecuted,
            response.BlueprintExecuted,
            response.ImageGenerationExecuted,
            heroSceneSelectorExecuted = response.HeroSceneSelectorExecuted,
            heroSceneManifestGenerated = response.HeroSceneManifestGenerated,
            heroCompositionModelGenerated = response.HeroCompositionModelGenerated,
            layoutValidationGenerated = response.LayoutValidationGenerated,
            duplicateBlocksDetected = response.DuplicateBlocksDetected,
            textOverlapDetected = response.TextOverlapDetected,
            objectsVisible = response.ObjectsVisible,
            response.GeneratedFiles
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-video-assembly", async (VideoAssemblyGenerationRequest request, IVideoAssemblyIntelligenceService videoAssembly, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Video assembly generation request received for {RegionId}. EventId={EventId}; Phase={Phase}; DryRun={DryRun}", request.RegionId, request.EventId, request.Phase, request.DryRun);
    try
    {
        var response = await videoAssembly.GenerateVideoAssemblyAsync(request, ct);
        if (string.Equals(response.PhaseExecuted, "FullPipeline", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.OutputMode,
                response.ShortForm,
                response.LongForm,
                response.GeneratedFiles
            });
        }

        if (string.Equals(response.PhaseExecuted, "Script", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                response.VideoNarrationScriptGenerated,
                response.VideoNarrationScriptPath,
                response.TotalEstimatedDurationSeconds,
                response.TtsReady,
                response.GeneratedFiles
            });
        }

        if (string.Equals(response.PhaseExecuted, "LongFormTts", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                response.TtsAudioGenerated,
                response.TtsTimingsGenerated,
                response.AudioFilePath,
                response.TimingsFilePath,
                response.ActualDurationSeconds,
                response.TtsProvider,
                response.AudioValidationPassed
            });
        }


        if (string.Equals(response.PhaseExecuted, "LongFormAssembly", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                VideoLongAssemblyPlanGenerated = response.VideoAssemblyPlanGenerated,
                VideoLongAssemblyPlanPath = response.VideoAssemblyPlanPath,
                ScenePresentationProfileUsed = response.ScenePresentationProfileUsed.ToString(),
                SectionCount = response.SegmentCount,
                response.TotalDurationSeconds,
                BackgroundMusicPlanned = response.BackgroundMusicPlanned,
                ReadyForRender = response.ReadyForRender
            });
        }

        if (string.Equals(response.PhaseExecuted, "Assembly", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                response.VideoAssemblyPlanGenerated,
                response.VideoAssemblyPlanPath,
                response.ReadyForRender,
                response.SegmentCount,
                response.TotalDurationSeconds,
                response.GeneratedFiles
            });
        }


        if (string.Equals(response.PhaseExecuted, "LongFormRender", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                response.VideoRendered,
                response.FinalVideoPath,
                response.FinalVideoDurationSeconds,
                response.OutputResolution,
                response.AudioTrackPresent,
                response.BackgroundMusicApplied,
                ScenePresentationProfileUsed = response.ScenePresentationProfileUsed.ToString(),
                response.RenderSucceeded
            });
        }

        if (string.Equals(response.PhaseExecuted, "Render", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                response.PhaseRequested,
                response.PhaseExecuted,
                response.VideoRendered,
                response.FinalVideoPath,
                response.FinalVideoDurationSeconds,
                response.OutputResolution,
                response.AudioTrackPresent,
                response.BackgroundMusicRequested,
                response.BackgroundMusicApplied,
                response.BackgroundMusicSourcePath,
                response.MusicLevelPercent,
                response.RequestedMusicLevelPercent,
                response.EffectiveMusicLevelPercent,
                response.MusicVolumeMultiplier,
                response.DuckMusicUnderNarration,
                response.FfmpegAudioFilter,
                response.MusicMixApplied,
                response.RenderSucceeded,
                response.VideoRenderValidationPath,
                response.RenderPolishScore,
                response.VideoFinalReadinessScore,
                response.GeneratedFiles
            });
        }

        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/astronomy-intelligence/generate-thumbnail-assets", async (ThumbnailAssetGenerationRequest request, IThumbnailAssetIntelligenceService thumbnailAssets, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Thumbnail asset generation request received for {RegionId}. EventId={EventId}; Phase={Phase}; DryRun={DryRun}", request.RegionId, request.EventId, request.Phase, request.DryRun);
    try
    {
        var response = await thumbnailAssets.GenerateThumbnailAssetsAsync(request, ct);
        return Results.Ok(new
        {
            response.PhaseRequested,
            response.PhaseExecuted,
            response.ThumbnailCompositionGenerated,
            response.ThumbnailCompositionPath,
            response.ThumbnailCompositionReadinessScore,
            response.ThumbnailSceneManifestGenerated,
            response.ThumbnailSceneManifestPath,
            response.PrimaryScene,
            response.SecondaryScene,
            response.SupportScene,
            response.ThumbnailLayoutValidationGenerated,
            response.ThumbnailLayoutValidationPath,
            response.HookVisible,
            response.VisualFocusVisible,
            response.TextElementCount,
            response.ThumbnailReadabilityScore,
            response.ThumbnailClickabilityScore,
            response.ThumbnailCuriosityScore,
            response.ThumbnailVisualSourceMode,
            response.SourceSceneUsed,
            response.ApprovedSceneFoundationUsed,
            response.IndependentPlanetRedrawUsed,
            response.ArtificialGlowRemoved,
            response.VisualSourceQualityScore,
            response.PhotoCinematicRendererUsed,
            response.OldThumbnailRendererBypassed,
            response.SceneTextLabelsRemoved,
            response.TextBoxesRemoved,
            response.VenusRenderedAsStarPoint,
            response.JupiterRenderedAsPlanet,
            response.RequestedRenderer,
            response.ActualRendererUsed,
            response.RendererSelectionReason,
            response.OldRendererBypassed,
            response.PhotoCinematicRendererEntered,
            response.PhotoCinematicRendererCompleted,
            response.OutputWriteSource,
            response.OutputOverwriteDetected,
            response.Warnings,
            response.GeneratedFiles
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/astronomy-intelligence/generate-question-driven-visuals", async (QuestionDrivenVisualGenerationRequest request, IQuestionDrivenVisualComposer composer, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy question-driven visual generation request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        return Results.Ok(await composer.GenerateQuestionDrivenVisualsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-editorial-astronomy-infographics", async (QuestionDrivenVisualGenerationRequest request, IEditorialAstronomyInfographicComposer composer, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Editorial astronomy infographic generation request received for {RegionId}. EventId={EventId}; DryRun={DryRun}", request.RegionId, request.EventId, request.DryRun);
    try
    {
        return Results.Ok(await composer.GenerateEditorialAstronomyInfographicsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-video-plans", async (AstronomyVideoPlanningRequest request, IAstronomyVideoPlanningService planning, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy video planning request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await planning.GenerateVideoPlansAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-asset-plans", async (AstronomyAssetPlanningRequest request, IAstronomyAssetPlanningService planning, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy asset planning request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await planning.GenerateAssetPlansAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-narration-scripts", async (NarrationPlanningRequest request, INarrationPlanningService narration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy narration planning request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await narration.GenerateNarrationScriptsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-director-narration", async (DirectorNarrationRequest request, IDirectorNarrationService directorNarration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy director narration request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await directorNarration.GenerateDirectorNarrationAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-final-narration", async (FinalNarrationRequest request, IFinalNarrationService finalNarration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy final narration request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await finalNarration.GenerateFinalNarrationAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/polish-final-narration", async (PolishedNarrationRequest request, IPolishedNarrationService polishedNarration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy polished narration request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await polishedNarration.PolishFinalNarrationAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/resolve-astronomy-visual-asset-strategy", async (AstronomyVisualAssetStrategyRequest request, IAstronomyVisualAssetStrategyService strategy, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy visual asset strategy request received for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await strategy.ResolveAstronomyVisualAssetStrategyAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-infographic-layout-blueprint", async (InfographicLayoutBlueprintRequest request, IInfographicLayoutBlueprintGenerator generator, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Infographic layout blueprint request received for EventId={EventId}, RegionId={RegionId}, DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await generator.GenerateInfographicLayoutBlueprintAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-production-visuals", async (ProductionVisualGenerationRequest request, IProductionVisualComposerService composer, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy production visual generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await composer.GenerateProductionVisualsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-scene-editorial-preview", async (SceneEditorialPreviewRequest request, ISceneEditorialPreviewService previews, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy scene editorial preview request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await previews.GenerateSceneEditorialPreviewAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-tts-packages", async (TtsPackagePlanningRequest request, ITtsPackagePlanningService ttsPackages, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy TTS package planning request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await ttsPackages.GenerateTtsPackagesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/validate-tts-packages", async (TtsPackageValidationRequest request, ITtsPackageValidationService validation, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy TTS package SSML validation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await validation.ValidateTtsPackagesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/repair-tts-alignment", async (TtsAlignmentRepairRequest request, ITtsAlignmentRepairService repair, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy TTS package SSML/text alignment repair request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await repair.RepairTtsAlignmentAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/normalize-tts-alignment", async (TtsAlignmentRepairRequest request, ITtsAlignmentRepairService repair, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy TTS package SSML/text alignment normalization request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await repair.RepairTtsAlignmentAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-tts-audio", async (TtsAudioGenerationRequest request, ITtsAudioGenerationService audioGeneration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy TTS audio generation request received for {RegionId}. DryRun={DryRun}. MaxPlans={MaxPlans}", request.RegionId, request.DryRun, request.MaxPlans);
    try
    {
        return Results.Ok(await audioGeneration.GenerateTtsAudioAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-tts-audio-bulk", async (TtsAudioBulkGenerationRequest request, ITtsAudioGenerationService audioGeneration, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy bulk TTS audio generation request received for {RegionId}. DryRun={DryRun}. MaxPlans={MaxPlans}. OverwriteExisting={OverwriteExisting}", request.RegionId, request.DryRun, request.MaxPlans, request.OverwriteExisting);
    try
    {
        return Results.Ok(await audioGeneration.GenerateTtsAudioBulkAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-director-timelines", async (DirectorTimelineRequest request, IDirectorTimelineService timelines, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy director timeline generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await timelines.GenerateDirectorTimelinesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-scene-assembly-plans", async (SceneAssemblyPlanRequest request, ISceneAssemblyPlanService assemblyPlans, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy scene assembly plan generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await assemblyPlans.GenerateSceneAssemblyPlansAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-visual-assets", async (VisualAssetGenerationRequest request, IVisualAssetGenerationService visualAssets, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy visual asset generation request received for {RegionId}. DryRun={DryRun}. MaxPlans={MaxPlans}. OverwriteExisting={OverwriteExisting}", request.RegionId, request.DryRun, request.MaxPlans, request.OverwriteExisting);
    try
    {
        return Results.Ok(await visualAssets.GenerateVisualAssetsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-render-recipes", async (RenderRecipeRequest request, IRenderRecipeGenerator generator, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy render recipe generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await generator.GenerateRenderRecipesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/generate-render-capabilities", async (RenderCapabilityMatrixRequest request, IRenderCapabilityMatrixService matrix, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy render capability matrix generation request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await matrix.GenerateRenderCapabilitiesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/render-scenes", async (SceneRenderingRequest request, ISceneRenderer renderer, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy scene rendering request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await renderer.RenderScenesAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/create-asset-production-jobs", async (AstronomyAssetProductionJobRequest request, IAstronomyAssetProductionJobService jobs, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy asset production job DTO request received for {RegionId}. DryRun={DryRun}", request.RegionId, request.DryRun);
    try
    {
        return Results.Ok(await jobs.CreateAssetProductionJobsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/execute-required-assets", async (AssetExecutionRequest request, IAssetExecutionService execution, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Required astronomy asset execution request received for {RegionId}. DryRun={DryRun} MaxJobs={MaxJobs}", request.RegionId, request.DryRun, request.MaxJobs);
    try
    {
        return Results.Ok(await execution.ExecuteRequiredAssetsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/astronomy-intelligence/execute-preferred-assets", async (AssetExecutionRequest request, ISkyMapCardExecutionService skyMapExecution, IConstellationGuideExecutionService constellationGuideExecution, IStellariumScreenshotExecutionService stellariumScreenshotExecution, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Preferred astronomy asset execution request received for {RegionId}. DryRun={DryRun} MaxJobs={MaxJobs}", request.RegionId, request.DryRun, request.MaxJobs);
    try
    {
        var requestedTypes = request.AssetTypes is { Count: > 0 }
            ? request.AssetTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizePreferredAssetType).ToHashSet(StringComparer.Ordinal)
            : null;
        var executeSkyMap = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("SkyMapCard"));
        var executeConstellationGuide = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("ConstellationGuide"));
        var executeStellariumScreenshot = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("StellariumScreenshot"));

        var results = new List<AssetExecutionResult>();
        if (executeSkyMap)
            results.Add(await skyMapExecution.ExecutePreferredAssetsAsync(request with { AssetTypes = ["SkyMapCard"] }, ct));
        if (executeConstellationGuide)
            results.Add(await constellationGuideExecution.ExecutePreferredAssetsAsync(request with { AssetTypes = ["ConstellationGuide"] }, ct));
        if (executeStellariumScreenshot)
            results.Add(await stellariumScreenshotExecution.ExecutePreferredAssetsAsync(request with { AssetTypes = ["StellariumScreenshot"] }, ct));

        if (results.Count > 0)
        {
            return Results.Ok(new AssetExecutionResult(
                results.Sum(result => result.JobCount),
                results.Sum(result => result.CompletedCount),
                results.Sum(result => result.FailedCount),
                results.Sum(result => result.SkippedCount),
                results.SelectMany(result => result.GeneratedFiles).ToList(),
                results.SelectMany(result => result.Warnings).ToList()));
        }

        return Results.Ok(new AssetExecutionResult(0, 0, 0, 0, [], ["No supported preferred asset type was requested; supported types are SkyMapCard, ConstellationGuide, and StellariumScreenshot."]));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/astronomy-intelligence/execute-optional-assets", async (AssetExecutionRequest request, INasaAssetExecutionService nasaAssetExecution, IAiImagePromptExecutionService aiImagePromptExecution, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Optional astronomy asset execution request received for {RegionId}. DryRun={DryRun} MaxJobs={MaxJobs} EnableExternalLookup={EnableExternalLookup} EnableExternalGeneration={EnableExternalGeneration}", request.RegionId, request.DryRun, request.MaxJobs, request.EnableExternalLookup, request.EnableExternalGeneration);
    try
    {
        var requestedTypes = request.AssetTypes is { Count: > 0 }
            ? request.AssetTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizePreferredAssetType).ToHashSet(StringComparer.Ordinal)
            : null;
        var executeNasaAsset = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("NasaAsset"));
        var executeAiHeroImage = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("AiHeroImage"));
        var executeAiCinematicImage = requestedTypes is null || requestedTypes.Contains(NormalizePreferredAssetType("AiCinematicImage"));

        var results = new List<AssetExecutionResult>();
        if (executeNasaAsset)
            results.Add(await nasaAssetExecution.ExecuteOptionalAssetsAsync(request with { AssetTypes = ["NasaAsset"] }, ct));

        var aiAssetTypes = new List<string>();
        if (executeAiHeroImage)
            aiAssetTypes.Add("AiHeroImage");
        if (executeAiCinematicImage)
            aiAssetTypes.Add("AiCinematicImage");
        if (aiAssetTypes.Count > 0)
            results.Add(await aiImagePromptExecution.ExecuteOptionalAssetsAsync(request with { AssetTypes = aiAssetTypes }, ct));

        if (results.Count > 0)
        {
            return Results.Ok(new AssetExecutionResult(
                results.Sum(result => result.JobCount),
                results.Sum(result => result.CompletedCount),
                results.Sum(result => result.FailedCount),
                results.Sum(result => result.SkippedCount),
                results.SelectMany(result => result.GeneratedFiles).ToList(),
                results.SelectMany(result => result.Warnings).ToList()));
        }

        return Results.Ok(new AssetExecutionResult(0, 0, 0, 0, [], ["No supported optional asset type was requested; supported types are NasaAsset, AiHeroImage, and AiCinematicImage."]));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/astronomy-intelligence/preview-stellarium-capture", async (HttpRequest httpRequest, IStellariumCapturePreviewService previews, ILogger<Program> logger, CancellationToken ct) =>
{
    var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<StellariumCapturePreviewRequest>(httpRequest, "request", logger, ct);
    if (requestBody.HasError)
    {
        return requestBody.ErrorResult!;
    }

    var request = requestBody.Value!;
    logger.LogInformation("Stellarium capture preview request received for {RegionId}. MaxJobs={MaxJobs}", request.RegionId, request.MaxJobs);
    try
    {
        return Results.Ok(await previews.PreviewCaptureAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.Accepts<StellariumCapturePreviewRequest>("application/json");


app.MapPost("/api/astronomy-intelligence/execute-stellarium-capture", async (HttpRequest httpRequest, IStellariumCaptureExecutionService execution, ILogger<Program> logger, CancellationToken ct) =>
{
    var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<StellariumAssetCaptureExecutionRequest>(httpRequest, "request", logger, ct);
    if (requestBody.HasError)
    {
        return requestBody.ErrorResult!;
    }

    var request = requestBody.Value!;
    logger.LogInformation("Stellarium capture execution request received for {RegionId}. DryRun={DryRun} MaxJobs={MaxJobs}", request.RegionId, request.DryRun, request.MaxJobs);
    try
    {
        return Results.Ok(await execution.ExecuteCaptureAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.Accepts<StellariumAssetCaptureExecutionRequest>("application/json");

app.MapPost("/api/astronomy-intelligence/discover-astronomy-events", async (HttpRequest httpRequest, IAstronomyEventDiscoveryPreviewService previews, ILogger<Program> logger, CancellationToken ct) =>
{
    var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<AstronomyEventDiscoveryPreviewRequest>(httpRequest, "request", logger, ct);
    if (requestBody.HasError)
    {
        return requestBody.ErrorResult!;
    }

    var request = requestBody.Value!;
    logger.LogInformation("Astronomy event discovery preview request received for {RegionId} in {Year}. DryRun={DryRun}", request.RegionId, request.Year, request.DryRun);
    try
    {
        return Results.Ok(await previews.DiscoverAstronomyEventsAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.Accepts<AstronomyEventDiscoveryPreviewRequest>("application/json");

app.MapPost("/api/astronomy-intelligence/preview-asset-production", async (HttpRequest httpRequest, IAstronomyAssetProducerPreviewService previews, ILogger<Program> logger, CancellationToken ct) =>
{
    var requestBody = await JsonEndpointBodyReader.ReadRequiredAsync<AstronomyAssetProducerPreviewRequest>(httpRequest, "request", logger, ct);
    if (requestBody.HasError)
    {
        return requestBody.ErrorResult!;
    }

    var request = requestBody.Value!;
    logger.LogInformation("Astronomy asset production preview request received for {RegionId}. MaxJobs={MaxJobs}", request.RegionId, request.MaxJobs);
    try
    {
        return Results.Ok(await previews.PreviewAssetProductionAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.Accepts<AstronomyAssetProducerPreviewRequest>("application/json");

app.MapGet("/api/astronomy-intelligence/category-readiness", async (IAstronomyCategoryReadinessService readiness, CancellationToken ct) =>
    Results.Ok(await readiness.GetCategoryReadinessAsync(AstronomyOpportunityCategoryCodes.Phase7CategoryCodes, ct)));

app.MapGet("/api/astronomy-intelligence/production-summary", async (string? regionId, DateTimeOffset? startUtc, DateTimeOffset? endUtc, IAstronomyProductionMonitoringService monitoring, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Astronomy production summary requested for {RegionId} from {StartUtc} to {EndUtc}.", regionId, startUtc, endUtc);
    try
    {
        return Results.Ok(await monitoring.GetProductionSummaryAsync(new AstronomyProductionMonitoringRequest(regionId, startUtc, endUtc), ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});



app.MapGet("/api/content-master/categories", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ContentCategories.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/hook-styles", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.HookStyles.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/thumbnail-styles", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ThumbnailStyles.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/narration-styles", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.NarrationStyles.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/celestial-objects", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.CelestialObjects.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
app.MapGet("/api/content-master/event-types", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.AstronomyEventTypes.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct)));
app.MapGet("/api/content-master/category-style-settings", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ContentCategoryStyleSettings.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/variety-rules", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ContentVarietyRules.AsNoTracking().OrderBy(x => x.ContentCategoryCode).ThenBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-master/idea-templates", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ContentIdeaTemplates.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.ContentCategoryCode).ThenByDescending(x => x.Priority).ToListAsync(ct)));

app.MapPost("/api/content-planning/generate-plan", async (GenerateContentPlanRequest request, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await planning.GeneratePlanAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        if (ex is WeeklySkyForecastRegionResolutionException regionEx)
        {
            return Results.BadRequest(new
            {
                requestedRegionId = regionEx.RequestedRegionId,
                availableRegionIds = regionEx.AvailableRegionIds,
                message = regionEx.Message
            });
        }
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapPost("/api/content-planning/run-category-preparation", async (ManualCategoryPreparationRequest request, IManualCategoryPreparationOrchestrator orchestrator, CancellationToken ct) =>
{
    var response = await orchestrator.RunAsync(request, ct);
    return response.Success ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/content-planning/run-category-production-preview", async (CategoryProductionPreviewRequest request, ICategoryProductionRunner runner, CancellationToken ct) =>
{
    var response = await runner.RunAsync(request, ct);
    return response.Success ? Results.Ok(response) : Results.BadRequest(response);
});


app.MapPost("/api/content-planning/weekly-skyforecast-v2/intelligence-preview", async (WeeklySkyForecastV2IntelligenceRequest request, IWeeklySkyForecastV2IntelligenceService service, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        var contentPlanId = request.ContentGenerationPlanId;
        if (!contentPlanId.HasValue)
        {
            var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
            contentPlanId = plan.ContentGenerationPlanId;
        }

        var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
        var requestWithRun = request with { PipelineRunId = pipelineRunId, ContentGenerationPlanId = contentPlanId };
        var response = await service.PreviewAsync(requestWithRun, ct);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapPost("/api/content-planning/weekly-skyforecast-v2/phase-diagnostics", async (WeeklySkyForecastV2PhaseDiagnosticsRequest request, IWeeklySkyForecastV2IntelligenceService service, IWeeklyCinematicShotExpansionEngine cinematicEngine, IWeeklyStellariumScriptWriter stellariumScriptWriter, IWeeklyStellariumScriptExecutor stellariumScriptExecutor, IWeeklyStellariumScreenshotGenerator stellariumScreenshotGenerator, IWeeklyMotionRenderManifestBuilder motionManifestBuilder, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        if (!Enum.TryParse<WeeklySkyForecastV2DiagnosticsPhase>(request.Phase, ignoreCase: true, out var phase))
        {
            return Results.BadRequest(new
            {
                error = "Invalid phase.",
                allowedPhases = Enum.GetNames<WeeklySkyForecastV2DiagnosticsPhase>()
            });
        }

        var contentPlanId = request.ContentGenerationPlanId;
        if (!contentPlanId.HasValue)
        {
            var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
            contentPlanId = plan.ContentGenerationPlanId;
        }

        var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
        var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(
            request.ContentCategoryCode,
            request.Language,
            request.RegionId,
            request.RegionName,
            request.ScheduledUtc,
            request.WeekStartDate,
            Diagnostics: request.Diagnostics,
            PipelineRunId: pipelineRunId,
            ContentGenerationPlanId: contentPlanId);
        var response = await service.PreviewAsync(intelligenceRequest, ct);
        var weeklySkyfieldContext = response;
        app.Logger.LogInformation("Skyfield weekly context loaded once");
        var root = weeklySkyfieldContext.RenderPreparationPackage?.WorkingDirectoryPlan.RootPath;
        var debugFiles = new Dictionary<string, string?>
        {
            ["astronomyEvents"] = root is null ? null : Path.Combine(root, "debug", "weekly-astronomy-events.json"),
            ["storyBeats"] = root is null ? null : Path.Combine(root, "debug", "weekly-story-beats.json"),
            ["storyboard"] = root is null ? null : Path.Combine(root, "debug", "weekly-storyboard.json"),
            ["visualSources"] = root is null ? null : Path.Combine(root, "debug", "weekly-visual-sources.json"),
            ["stellariumBlueprints"] = root is null ? null : Path.Combine(root, "debug", "weekly-stellarium-blueprints.json"),
            ["cinematicShots"] = root is null ? null : Path.Combine(root, "debug", "weekly-cinematic-shot-timeline.json"),
            ["stellariumScripts"] = root is null ? null : Path.Combine(root, "debug", "weekly-stellarium-script-package.json"),
            ["motionRenderManifest"] = root is null ? null : Path.Combine(root, "debug", "weekly-motion-render-manifest.json"),
            ["narrationSceneSync"] = root is null ? null : Path.Combine(root, "debug", "weekly-narration-scene-sync.json"),
            ["cinematicTimeline"] = root is null ? null : Path.Combine(root, "debug", "weekly-cinematic-timeline.json"),
            ["thumbnailStoryboard"] = root is null ? null : Path.Combine(root, "debug", "weekly-thumbnail-storyboard.json"),
            ["shortsPlan"] = root is null ? null : Path.Combine(root, "debug", "weekly-shorts-plan.json")
        };


        WeeklyCinematicShotPackage? cachedCinematicPackage = null;
        WeeklyStellariumScriptPackage? cachedStellariumScripts = null;
        WeeklyCinematicShotPackage? FilterForSingleShot(WeeklyCinematicShotPackage? package, string? executeShotCode)
        {
            if (package is null || string.IsNullOrWhiteSpace(executeShotCode)) return package;
            var filteredSequences = package.SceneSequences
                .Select(sequence =>
                {
                    var matchingShots = sequence.Shots
                        .Where(shot => string.Equals(shot.ShotCode, executeShotCode, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    return matchingShots.Count == 0
                        ? null
                        : sequence with
                        {
                            Shots = matchingShots,
                            DurationSeconds = matchingShots.Sum(shot => Math.Max(1, shot.DurationSeconds))
                        };
                })
                .Where(sequence => sequence is not null)
                .Select(sequence => sequence!)
                .ToList();
            if (filteredSequences.Count == 0) return package with
            {
                SceneSequences = [],
                TotalScenes = 0,
                TotalShots = 0,
                EstimatedDurationSeconds = 0,
                ValidationIssues = package.ValidationIssues.Concat([$"No shot matched executeShotCode '{executeShotCode}'."]).ToList()
            };

            var filteredSceneCodes = filteredSequences.Select(sequence => sequence.SceneCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return package with
            {
                SceneSequences = filteredSequences,
                TotalScenes = filteredSequences.Count,
                TotalShots = filteredSequences.Sum(sequence => sequence.Shots.Count),
                EstimatedDurationSeconds = filteredSequences.Sum(sequence => Math.Max(1, sequence.DurationSeconds)),
                DynamicFovCalculations = package.DynamicFovCalculations.Where(calc => filteredSceneCodes.Contains(calc.SceneCode)).ToList()
            };
        }
        WeeklyCinematicShotPackage? GetCinematicShotPackage()
        {
            if (cachedCinematicPackage is not null) return cachedCinematicPackage;
            if (response.Storyboard is null || response.StellariumBlueprintPackage is null || response.EventExtractionResult is null || root is null) return null;
            var expanded = cinematicEngine.Expand(response.Storyboard, response.StellariumBlueprintPackage, response.EventExtractionResult, response.Region, root, pipelineRunId.ToString("N"));
            cachedCinematicPackage = FilterForSingleShot(expanded, request.ExecuteShotCode);
            return cachedCinematicPackage;
        }
        async Task<WeeklyStellariumScriptPackage?> GetStellariumScriptsAsync()
        {
            if (cachedStellariumScripts is not null) return cachedStellariumScripts;
            var cinematicPackage = GetCinematicShotPackage();
            if (cinematicPackage is null || root is null) return null;
            cachedStellariumScripts = await stellariumScriptWriter.WriteAsync(cinematicPackage, root, ct);
            return cachedStellariumScripts;
        }

        var selectedTestModeRaw = string.IsNullOrWhiteSpace(request.TestMode) ? nameof(ScreenshotTestMode.All) : request.TestMode;
        var executeAllScripts = request.ExecuteAllScripts ?? false;
        var confirmFullBatch = request.ConfirmFullBatch ?? false;
        var continueOnFailure = request.ContinueOnFailure ?? true;
        var maxScriptCount = request.MaxScriptCount ?? 3;
        var warnings = new List<string>();
        if (!Enum.TryParse<ScreenshotTestMode>(selectedTestModeRaw, true, out var parsedTestMode))
        {
            parsedTestMode = ScreenshotTestMode.All;
            warnings.Add($"Invalid testMode '{selectedTestModeRaw}' supplied. Falling back to '{ScreenshotTestMode.All}'.");
        }

        if (root is not null)
        {
            var diagSnapshotPath = Path.Combine(root, "debug", "diagnostics-request.json");
            Directory.CreateDirectory(Path.GetDirectoryName(diagSnapshotPath)!);
            await File.WriteAllTextAsync(diagSnapshotPath, JsonSerializer.Serialize(new
            {
                request,
                parsedDefaults = new { selectedTestMode = parsedTestMode.ToString(), executeAllScripts, confirmFullBatch, continueOnFailure, maxScriptCount },
                selectedPhase = phase.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
        }

        async Task<object?> ExecuteStellariumScreenshotsAsync(WeeklyStellariumScriptPackage shots)
        {
            var totalShotsAvailable = shots?.Scripts?.Count ?? 0;
            Console.WriteLine($"entering diagnostics phase; request.phase={request.Phase}; request.testMode={parsedTestMode}; totalShotsAvailable={totalShotsAvailable}");
            return await stellariumScreenshotGenerator.GenerateAsync(root!, shots, request.ExecuteShotCode, parsedTestMode.ToString(), maxScriptCount, executeAllScripts, confirmFullBatch, continueOnFailure, request.StellariumTimeoutSeconds ?? 90, ct);
        }
        async Task<object?> LoadCompositionAsync(string? workingRoot, string? shotCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workingRoot) || string.IsNullOrWhiteSpace(shotCode)) return null;
            var compositionPath = Path.Combine(workingRoot, "stellarium", "scenes", $"{shotCode}.composition.json");
            if (!File.Exists(compositionPath)) return null;
            return JsonSerializer.Deserialize<object>(await File.ReadAllTextAsync(compositionPath, cancellationToken));
        }
        async Task<object?> ExecuteStellariumSmokeTestAsync()
        {
            var cinematicPackage = GetCinematicShotPackage();
            if (cinematicPackage is null) return null;
            var shots = await stellariumScriptWriter.WriteAsync(cinematicPackage, root!, ct);
            var selected = string.IsNullOrWhiteSpace(request.ExecuteShotCode)
                ? shots.Scripts.FirstOrDefault()
                : shots.Scripts.FirstOrDefault(x => string.Equals(x.ShotCode, request.ExecuteShotCode, StringComparison.OrdinalIgnoreCase));
            selected ??= shots.Scripts.FirstOrDefault(x => string.Equals(x.ShotCode, "s1_wide_sky_reveal_01", StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                return null;
            }

            return await stellariumScriptExecutor.ExecuteAsync(root!, selected.ScriptPath, selected.ExpectedScreenshotPath, request.StellariumTimeoutSeconds ?? 45, ct);
        }

        Console.WriteLine("entering diagnostics phase");
        Console.WriteLine($"phase selected={phase}");
        Console.WriteLine($"request values: executeShotCode={request.ExecuteShotCode}, testMode={parsedTestMode}, executeAllScripts={executeAllScripts}, confirmFullBatch={confirmFullBatch}, continueOnFailure={continueOnFailure}, maxScriptCount={maxScriptCount}");
        Console.WriteLine("service execution start");
        object result;
        try
        {
            result = phase switch
            {
            WeeklySkyForecastV2DiagnosticsPhase.AstronomyEvents => new
            {
                response.EventExtractionResult,
                response.EventIntelligence
            },
            WeeklySkyForecastV2DiagnosticsPhase.StoryBeats => new
            {
                storyboard = response.Storyboard,
                segments = response.Storyboard?.OrderedSegments,
                transitions = response.Storyboard?.Transitions,
                pacingAnalysis = response.Storyboard?.PacingAnalysis,
                heroEvent = response.Storyboard?.SelectedPrimaryEvent,
                emotionalArc = response.Storyboard?.EmotionalArc,
                response.EventExtractionResult,
                response.EventIntelligence,
                response.WeeklyStoryArc
            },
            WeeklySkyForecastV2DiagnosticsPhase.VisualSources => new
            {
                response.EventExtractionResult,
                response.EventIntelligence,
                response.WeeklyStoryArc,
                response.EditorialStoryPackage
            },
            WeeklySkyForecastV2DiagnosticsPhase.StellariumBlueprints => new
            {
                storyboard = response.Storyboard,
                stellariumBlueprintPackage = response.StellariumBlueprintPackage,
                sceneBlueprints = response.StellariumBlueprintPackage?.SceneBlueprints,
                validation = response.StellariumBlueprintPackage?.ValidationIssues,
                debugFiles
            },
            WeeklySkyForecastV2DiagnosticsPhase.CinematicShots => new
            {
                storyboard = response.Storyboard,
                stellariumBlueprintPackage = response.StellariumBlueprintPackage,
                cinematicShotPackage = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()
                    : null,
                sceneSequences = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()?.SceneSequences
                    : null,
                totalShots = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()?.TotalShots ?? 0
                    : 0,
                dynamicFovCalculations = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()?.DynamicFovCalculations
                    : null,
                validation = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()?.ValidationIssues ?? ["missing prerequisites"]
                    : ["missing prerequisites"],
                debugFiles
            },
            WeeklySkyForecastV2DiagnosticsPhase.StellariumScripts => new
            {
                storyboard = response.Storyboard,
                stellariumBlueprintPackage = response.StellariumBlueprintPackage,
                cinematicShotPackage = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()
                    : null,
                stellariumScripts = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? await stellariumScriptWriter.WriteAsync(
                        GetCinematicShotPackage()!,
                        root,
                        ct)
                    : null,
                debugFiles
            },
            WeeklySkyForecastV2DiagnosticsPhase.StellariumScreenshots => new
            {
                selectedShot = request.ExecuteShotCode,
                composition = await LoadCompositionAsync(root, request.ExecuteShotCode, ct),
                sscPath = (root is not null && !string.IsNullOrWhiteSpace(request.ExecuteShotCode))
                    ? Path.Combine(root, "stellarium", "scripts", $"{request.ExecuteShotCode}.ssc")
                    : null,
                screenshotPath = (root is not null && !string.IsNullOrWhiteSpace(request.ExecuteShotCode))
                    ? Path.Combine(root, "stellarium", "scenes", $"{request.ExecuteShotCode}.png")
                    : null,
                executionResult = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? await ExecuteStellariumScreenshotsAsync((await GetStellariumScriptsAsync())!)
                    : null
            },
            WeeklySkyForecastV2DiagnosticsPhase.StellariumBasicSmoke or WeeklySkyForecastV2DiagnosticsPhase.StellariumExecutionSmokeTest => new
            {
                storyboard = response.Storyboard,
                stellariumBlueprintPackage = response.StellariumBlueprintPackage,
                cinematicShotPackage = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? GetCinematicShotPackage()
                    : null,
                stellariumScripts = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? await stellariumScriptWriter.WriteAsync(GetCinematicShotPackage()!, root, ct)
                    : null,
                execution = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? await ExecuteStellariumSmokeTestAsync()
                    : null,
                debugFiles
            },
            WeeklySkyForecastV2DiagnosticsPhase.MotionRenderPlan => new
            {
                storyboard = response.Storyboard,
                stellariumBlueprintPackage = response.StellariumBlueprintPackage,
                cinematicShotPackage = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? cinematicEngine.Expand(response.Storyboard, response.StellariumBlueprintPackage, response.EventExtractionResult, response.Region, root, pipelineRunId.ToString("N"))
                    : null,
                motionRenderPlan = (response.Storyboard is not null && response.StellariumBlueprintPackage is not null && response.EventExtractionResult is not null && root is not null)
                    ? await motionManifestBuilder.BuildAsync(
                        cinematicEngine.Expand(response.Storyboard, response.StellariumBlueprintPackage, response.EventExtractionResult, response.Region, root, pipelineRunId.ToString("N")),
                        root,
                        pipelineRunId.ToString("N"),
                        request.RenderPreviewClips,
                        request.PreviewClipCount <= 0 ? 1 : Math.Min(1, request.PreviewClipCount),
                        ct)
                    : null,
                debugFiles
            },
            WeeklySkyForecastV2DiagnosticsPhase.NarrationSceneSync => new
            {
                response.NarrationPlan,
                response.GeneratedNarrationPackage,
                response.NarrationQuality,
                response.VisualRequirementPackage,
                response.HybridScenePlanPackage
            },
            WeeklySkyForecastV2DiagnosticsPhase.CinematicTimeline => new
            {
                response.SceneChoreographyPackage,
                response.CinematicChoreographyPackage,
                response.RenderExecutionPackage
            },
            WeeklySkyForecastV2DiagnosticsPhase.ThumbnailStoryboard => new
            {
                response.EditorialStoryPackage.ThumbnailDirection,
                response.CinematicStoryBlueprint
            },
            WeeklySkyForecastV2DiagnosticsPhase.ShortsPlan => new
            {
                response.EditorialStoryPackage.ShortsCandidates,
                response.WeeklyStoryArc.SuggestedShorts
            },
                _ => response
            };
        }
        catch (Exception ex)
        {
            return Results.Ok(new { phase = phase.ToString(), errorStage = "DiagnosticsPhaseRouting", error = ex.Message, stackTrace = ex.StackTrace, warnings });
        }
        Console.WriteLine("service execution completed");

        return Results.Ok(new
        {
            contentGenerationPlanId = contentPlanId,
            pipelineRunId,
            workingDirectoryRoot = root,
            phase = phase.ToString(),
            skyfieldCallCount = 1,
            region = response.Region,
            result,
            debugFiles,
            validation = new
            {
                response.ReadyForRenderPreparation,
                response.ReadyForSceneRendering,
                response.ReadyForRendering,
                response.ExecutionValidation,
                response.PreviewStability,
                response.Phase5FoundationStatus
            },
            warnings = response.Warnings,
            errors = response.StepResults
                .Where(x => !string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.ErrorMessage ?? x.Message ?? $"{x.StepName} did not complete successfully.")
                .ToArray()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/weekly-skyforecast-v2/runs/{pipelineRunId:guid}/generate-audio", async (Guid pipelineRunId, WeeklySkyForecastAudioGenerationRequest request, IWeeklySkyForecastAudioGenerationService service, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var response = await service.GenerateAsync(pipelineRunId, request, ct);
        return Results.Ok(response);
    }
    catch (FileNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_GENERATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, audioGenerationReady = false, errors = new[] { ex.Message } });
    }
    catch (DirectoryNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_GENERATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.NotFound(new { pipelineRunId, audioGenerationReady = false, errors = new[] { ex.Message } });
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_GENERATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, audioGenerationReady = false, errors = new[] { ex.Message } });
    }
});


app.MapPost("/api/weekly-skyforecast-v2/runs/{pipelineRunId:guid}/reconcile-timeline-from-audio", async (Guid pipelineRunId, WeeklyAudioDrivenTimelineReconciliationRequest request, IWeeklyAudioDrivenTimelineReconciliationService service, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var response = await service.ReconcileAsync(pipelineRunId, request, ct);
        return Results.Ok(response);
    }
    catch (FileNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_DRIVEN_TIMELINE_RECONCILIATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, audioDrivenTimelineReady = false, errors = new[] { ex.Message } });
    }
    catch (DirectoryNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_DRIVEN_TIMELINE_RECONCILIATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.NotFound(new { pipelineRunId, audioDrivenTimelineReady = false, errors = new[] { ex.Message } });
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError(ex, "WEEKLY_AUDIO_DRIVEN_TIMELINE_RECONCILIATION_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, audioDrivenTimelineReady = false, errors = new[] { ex.Message } });
    }
});

app.MapPost("/api/weekly-skyforecast-v2/runs/{pipelineRunId:guid}/render-video", async (Guid pipelineRunId, WeeklyExistingRunRenderRequest request, IWeeklyExistingRunVideoRenderer renderer, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var response = await renderer.RenderAsync(pipelineRunId, request, ct);
        return Results.Ok(response);
    }
    catch (FileNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_RENDER_EXISTING_RUN_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, renderVideoReady = false, errors = new[] { ex.Message } });
    }
    catch (DirectoryNotFoundException ex)
    {
        logger.LogError(ex, "WEEKLY_RENDER_EXISTING_RUN_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.NotFound(new { pipelineRunId, renderVideoReady = false, errors = new[] { ex.Message } });
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError(ex, "WEEKLY_RENDER_EXISTING_RUN_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        return Results.BadRequest(new { pipelineRunId, renderVideoReady = false, errors = new[] { ex.Message } });
    }
});

app.MapPost("/api/content-planning/weekly-skyforecast-v2/render-scenes", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastSceneRenderingOrchestrator orchestrator, IContentPlanningService planning, CancellationToken ct) =>
{
    var contentPlanId = request.ContentGenerationPlanId;
    if (!contentPlanId.HasValue)
    {
        var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
        contentPlanId = plan.ContentGenerationPlanId;
    }

    var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
    var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(
        request.ContentCategoryCode,
        request.Language,
        request.RegionId,
        request.RegionName,
        request.ScheduledUtc,
        request.WeekStartDate,
        Diagnostics: request.Diagnostics,
        PipelineRunId: pipelineRunId,
        ContentGenerationPlanId: contentPlanId);
    var result = await orchestrator.RunAsync(intelligenceRequest, contentPlanId, ct);
    return Results.Ok(result);
});

app.MapPost("/api/weekly-skyforecast-v2/generate-weekly-scenes", async (WeeklySkyForecastV2GenerateWeeklyScenesRequest request, IWeeklySkyForecastV2IntelligenceService service, IContentPlanningService planning, WeeklyEpisodeArchitectureService episodeArchitectureService, WeeklySegmentClassificationService segmentClassificationService, WeeklySegmentDiversificationService segmentDiversificationService, WeeklyVisualAssetPlanningService visualAssetPlanningService, WeeklyAICinematicAssetGenerationService aiCinematicAssetGenerationService, WeeklyAssetExpansionService assetExpansionService, IOptions<WeeklySkyForecastAICinematicAssetsOptions> aiCinematicOptionsAccessor, IOptions<WeeklySkyForecastAssetExpansionOptions> assetExpansionOptionsAccessor, IWeeklySkySceneComposer sceneComposer, ISscIntelligenceService sscIntelligenceService, Astronomy.SscIntelligence.SceneIntent.ISceneIntentResolver sceneIntentResolver, Astronomy.SscIntelligence.Storytelling.IAstronomicalSceneScorer astronomicalSceneScorer, IStellariumScriptExecutionService sharedStellariumExecutor, ISkyfieldTemporalResolver temporalResolver, IAstronomicalSpatialCompositionEngine spatialCompositionEngine, INarrativeSceneSplitter narrativeSceneSplitter, WeeklyAssetRealizationService assetRealizationService, WeeklyNarrationVisualTimelineComposer narrationVisualTimelineComposer, IWeeklyEventPriorityScoringEngine eventPriorityScoringEngine, IWeeklyNarrationEngineV2 narrationEngineV2, IWeeklyTimelineCompositionEngine timelineCompositionEngine, IWeeklyFfmpegRenderPreparationEngine ffmpegRenderPreparationEngine, IWeeklySkyForecastContextBuilderV2 contextBuilder, CancellationToken ct) =>
{
    try
    {
        app.Logger.LogInformation("WeeklySkyForecast generate-weekly-scenes request after HTTP binding: {@Request}", request);
        var contentPlanId = request.ContentGenerationPlanId;
        if (!contentPlanId.HasValue)
        {
            var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
            contentPlanId = plan.ContentGenerationPlanId;
        }

        var pipelineRunId = request.PipelineRunId ?? contentPlanId!.Value;
        var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(
            request.ContentCategoryCode,
            request.Language,
            request.RegionId,
            request.RegionName,
            request.ScheduledUtc,
            request.WeekStartDate,
            Diagnostics: request.Diagnostics,
            PipelineRunId: pipelineRunId,
            ContentGenerationPlanId: contentPlanId);

        using var skyfieldTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        skyfieldTimeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
        var weeklyForecast = await contextBuilder.BuildAsync(new WeeklySkyForecastV2OrchestrationContext(
            contentPlanId.Value,
            pipelineRunId,
            null,
            intelligenceRequest,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow), skyfieldTimeoutCts.Token);
        var orchestrationContext = new WeeklySkyForecastV2OrchestrationContext(
            contentPlanId.Value,
            pipelineRunId,
            null,
            intelligenceRequest,
            null,
            weeklyForecast,
            null,
            null,
            null,
            null,
            DateTime.UtcNow,
            SkyfieldWeeklyForecastCalls: 1,
            RegionResolveCalls: 1,
            ContextReusedAcrossPhases: true);

        var response = await service.PreviewAsync(orchestrationContext, ct);
        var weeklySkyfieldContext = response;
        app.Logger.LogInformation("Skyfield weekly context loaded once and reused for intelligence preview");
        var root = weeklySkyfieldContext.RenderPreparationPackage?.WorkingDirectoryPlan.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            return Results.BadRequest(new { error = "Unable to resolve working directory root for WeeklySkyForecast scene generation." });

        var debugRoot = Path.Combine(root, "debug");
        Directory.CreateDirectory(debugRoot);
        var skyfieldResponsePath = Path.Combine(debugRoot, "skyfield-weekly-response.json");
        var skyfieldFullResponsePath = Path.Combine(debugRoot, "skyfield-weekly-full-response.json");
        var skyfieldErrorsPath = Path.Combine(debugRoot, "skyfield-weekly-errors.json");
        var skyfieldFullResponseJson = WeeklySkyForecastPreparationDiagnostics.GetJson("WeeklySkyForecast.SkyfieldWeeklyResponse");
        if (!string.IsNullOrWhiteSpace(skyfieldFullResponseJson))
        {
            await File.WriteAllTextAsync(skyfieldResponsePath, skyfieldFullResponseJson, ct);
            await File.WriteAllTextAsync(skyfieldFullResponsePath, skyfieldFullResponseJson, ct);
        }
        else
        {
            await File.WriteAllTextAsync(skyfieldResponsePath, JsonSerializer.Serialize(response.EventExtractionResult, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
        await File.WriteAllTextAsync(skyfieldErrorsPath, "[]", ct);

        var narrationDirectory = Path.Combine(root, "narration");
        Directory.CreateDirectory(narrationDirectory);
        app.Logger.LogInformation("Persisting narration artifacts");
        var storyBeatsPath = Path.Combine(narrationDirectory, "weekly-story-beats.json");
        var narrationPlanPath = Path.Combine(narrationDirectory, "weekly-narration-plan.json");
        var narrationTextPath = Path.Combine(narrationDirectory, "weekly-narration-text.txt");
        var visualRequirementsPath = Path.Combine(narrationDirectory, "weekly-visual-requirements.json");
        await File.WriteAllTextAsync(storyBeatsPath, JsonSerializer.Serialize(weeklySkyfieldContext.NarrativeAbstractionPackage, new JsonSerializerOptions { WriteIndented = true }), ct);
        await File.WriteAllTextAsync(narrationPlanPath, JsonSerializer.Serialize(weeklySkyfieldContext.NarrationPlan, new JsonSerializerOptions { WriteIndented = true }), ct);
        await File.WriteAllTextAsync(narrationTextPath, weeklySkyfieldContext.GeneratedNarrationPackage?.LongFormNarration.FullNarration ?? string.Empty, ct);
        await File.WriteAllTextAsync(visualRequirementsPath, JsonSerializer.Serialize(weeklySkyfieldContext.VisualRequirementPackage, new JsonSerializerOptions { WriteIndented = true }), ct);
        app.Logger.LogInformation("Narration artifacts persisted");

        var weekStartDate = request.WeekStartDate ?? DateOnly.FromDateTime(request.ScheduledUtc.UtcDateTime);
        var weekEndDate = weekStartDate.AddDays(6);

        var episodeArchitecture = await episodeArchitectureService.BuildAndPersistAsync(
            pipelineRunId,
            request.RegionId,
            request.Language,
            weekStartDate,
            root,
            ct);

        app.Logger.LogInformation(
            "WeeklySkyForecast generate-weekly-scenes before orchestration visual generation: weekStartDate={WeekStartDate}, weekEndDate={WeekEndDate}, scheduledUtc={ScheduledUtc}, dryRun={DryRun}, generateSscScripts={GenerateSscScripts}, captureStellariumScenes={CaptureStellariumScenes}, diagnostics={Diagnostics}, flags={Flags}",
            weekStartDate,
            weekEndDate,
            request.ScheduledUtc,
            false,
            true,
            true,
            request.Diagnostics,
            new
            {
                request.ContentCategoryCode,
                request.Language,
                request.RegionId,
                request.RegionName,
                request.StellariumTimeoutSeconds,
                request.MaxScriptCount,
                request.ContinueOnFailure,
                pipelineRunId,
                contentPlanId
            });

        var scenePlansDirectory = Path.Combine(root, "scene-plans");
        var compositionDirectory = Path.Combine(root, "composition");
        var scriptsDirectory = Path.Combine(root, "stellarium", "scripts");
        var scenesDirectory = Path.Combine(root, "stellarium", "scenes");
        var manifestsDirectory = Path.Combine(root, "manifests");
        var renderDirectory = Path.Combine(root, "render");
        Directory.CreateDirectory(scenePlansDirectory);
        Directory.CreateDirectory(compositionDirectory);
        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(scenesDirectory);
        Directory.CreateDirectory(manifestsDirectory);
        Directory.CreateDirectory(renderDirectory);
        var orchestrationStageTimeout = TimeSpan.FromSeconds(60);

        async Task<T> ExecuteOrchestrationStageAsync<T>(string stageName, Func<CancellationToken, Task<T>> action, TimeSpan? stageTimeoutOverride = null)
        {
            var stageTimeout = stageTimeoutOverride ?? orchestrationStageTimeout;
            app.Logger.LogInformation("[INF] Starting {StageName}", stageName);
            var stageStopwatch = Stopwatch.StartNew();
            using var stageTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stageTimeoutCts.CancelAfter(stageTimeout);
            try
            {
                var result = await action(stageTimeoutCts.Token);
                stageStopwatch.Stop();
                app.Logger.LogInformation("[INF] Completed {StageName} in {ElapsedMs}ms", stageName, stageStopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException oce) when (!ct.IsCancellationRequested && stageTimeoutCts.IsCancellationRequested)
            {
                stageStopwatch.Stop();
                app.Logger.LogWarning(oce, "[WRN] Timeout after {ElapsedMs}ms for stage {StageName}. TimeoutMs={TimeoutMs}", stageStopwatch.ElapsedMilliseconds, stageName, stageTimeout.TotalMilliseconds);
                throw new TimeoutException($"WeeklySkyForecast orchestration stage timed out after {stageTimeout.TotalSeconds:0}s: {stageName}", oce);
            }
        }

        async Task ExecuteOrchestrationStageAsyncNonGeneric(string stageName, Func<CancellationToken, Task> action)
        {
            await ExecuteOrchestrationStageAsync<object?>(stageName, async stageCt =>
            {
                await action(stageCt);
                return null;
            });
        }

        var weeklyScenePlan = await ExecuteOrchestrationStageAsync("Building weekly scene plan", _ =>
        {
            var scenePlan = weeklySkyfieldContext.HybridScenePlanPackage;
            if (scenePlan is null)
                throw new InvalidOperationException("Weekly scene plan package was not generated.");
            return Task.FromResult(scenePlan);
        });

        var weeklyFocusPlan = BuildWeeklyFocusObjectPlan(
            weekStartDate,
            request.RegionId,
            request.Language,
            weeklySkyfieldContext,
            weeklySkyfieldContext.GeneratedNarrationPackage?.LongFormNarration.FullNarration ?? string.Empty);
        if (!weeklyFocusPlan.FocusObjects.Contains("JUPITER", StringComparer.OrdinalIgnoreCase)
            && weeklyFocusPlan.FocusObjects.Contains("SATURN", StringComparer.OrdinalIgnoreCase)
            && File.Exists(narrationTextPath))
        {
            var sanitizedNarration = Regex.Replace(await File.ReadAllTextAsync(narrationTextPath, ct), @"\bJupiter\b", "Saturn", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            sanitizedNarration = Regex.Replace(sanitizedNarration, @"\bJUPITER\b", "SATURN", RegexOptions.CultureInvariant);
            await File.WriteAllTextAsync(narrationTextPath, sanitizedNarration, ct);
        }
        weeklyScenePlan = AlignWeeklyScenePlanWithFocusObjects(weeklyScenePlan, weeklyFocusPlan);
        var weeklyStellariumSceneRequirements = BuildWeeklyStellariumSceneRequirements(weeklyFocusPlan, weeklyScenePlan);
        var episodeDirectory = Path.Combine(root, "episode");
        var stellariumDirectory = Path.Combine(root, "stellarium");
        Directory.CreateDirectory(episodeDirectory);
        Directory.CreateDirectory(stellariumDirectory);
        var weeklyFocusObjectPlanPath = Path.Combine(episodeDirectory, "weekly-focus-object-plan.json");
        var weeklyStellariumSceneRequirementsPath = Path.Combine(episodeDirectory, "weekly-stellarium-scene-requirements.json");
        var visualNarrationCoverageReportPath = Path.Combine(episodeDirectory, "weekly-visual-narration-coverage-report.json");
        var sscSceneManifestPath = Path.Combine(stellariumDirectory, "ssc-scene-manifest.json");
        var framingDirectory = Path.Combine(stellariumDirectory, "framing");
        Directory.CreateDirectory(framingDirectory);
        var weeklyDynamicFramingPlanPath = Path.Combine(framingDirectory, "weekly-dynamic-framing-plan.json");
        var weeklyFramingValidationReportPath = Path.Combine(framingDirectory, "weekly-framing-validation-report.json");
        var sscPropagationValidationReportPath = Path.Combine(stellariumDirectory, "ssc-propagation-validation-report.json");
        var sscCameraLockValidationReportPath = Path.Combine(stellariumDirectory, "ssc-camera-lock-validation-report.json");
        await ExecuteOrchestrationStageAsyncNonGeneric("Persisting weekly focus object plan", stageCt =>
            File.WriteAllTextAsync(weeklyFocusObjectPlanPath, JsonSerializer.Serialize(weeklyFocusPlan, new JsonSerializerOptions { WriteIndented = true }), stageCt));
        await ExecuteOrchestrationStageAsyncNonGeneric("Persisting weekly Stellarium scene requirements", stageCt =>
            File.WriteAllTextAsync(weeklyStellariumSceneRequirementsPath, JsonSerializer.Serialize(weeklyStellariumSceneRequirements, new JsonSerializerOptions { WriteIndented = true }), stageCt));

        var scenePlanPath = Path.Combine(scenePlansDirectory, "weekly-scene-plan.json");
        await ExecuteOrchestrationStageAsyncNonGeneric("Persisting weekly scene plan", stageCt =>
            File.WriteAllTextAsync(scenePlanPath, JsonSerializer.Serialize(weeklyScenePlan, new JsonSerializerOptions { WriteIndented = true }), stageCt));

        var segmentClassification = await ExecuteOrchestrationStageAsync("Classifying weekly episode segments", stageCt =>
            segmentClassificationService.ClassifyAndPersistAsync(
                episodeArchitecture,
                weeklySkyfieldContext,
                weeklyScenePlan,
                root,
                stageCt));

        var shotTimeline = await ExecuteOrchestrationStageAsync("Building cinematic shot timeline", _ =>
        {
            var shots = weeklyScenePlan.ScenePlans
                .OrderBy(x => x.SceneOrder)
                .Select((scene, idx) => new WeeklyCinematicShot(
                    scene.SceneCode,
                    scene.SceneType,
                    scene.RenderIntent,
                    scene.ObjectCodes,
                    scene.ObjectCodes.FirstOrDefault(),
                    scene.TargetDate,
                    TimeOnly.FromDateTime((scene.BestTimeUtc ?? request.ScheduledUtc.UtcDateTime).ToUniversalTime()),
                    scene.DurationSeconds,
                    "W",
                    scene.SceneType.Contains("wide", StringComparison.OrdinalIgnoreCase) ? 82d : 45d,
                    scene.SceneType.Contains("wide", StringComparison.OrdinalIgnoreCase) ? 82d : 45d,
                    scene.SceneType.Contains("wide", StringComparison.OrdinalIgnoreCase) ? 82d : 45d,
                    new WeeklyCameraMovementPlan("static", scene.CinematicMotion, "none", null, null, false),
                    new WeeklyShotTransitionPlan(scene.TransitionIn, scene.TransitionIn, "in"),
                    new WeeklyShotTransitionPlan(scene.TransitionOut, scene.TransitionOut, "out"),
                    scene.CinematicMotion,
                    Path.Combine(scenesDirectory, $"{scene.SceneCode}.png"),
                    string.Empty,
                    Path.Combine(scriptsDirectory, $"{scene.SceneCode}.ssc"),
                    [],
                    new WeeklyShotNarrationSync(scene.SceneCode, idx * 6, (idx + 1) * 6, scene.VisualStrategy, scene.ObjectCodes.FirstOrDefault(), [])))
                .ToList();
            var timeline = new WeeklyCinematicShotPackage(true, "weekly-v2", pipelineRunId.ToString(), weeklyScenePlan.ScenePlans.Count, shots.Count, shots.Sum(x => x.DurationSeconds),
                shots.Select(s => new WeeklyCinematicSceneSequence(s.ShotCode, s.ShotCode, s.ShotType, s.ShotCode, s.DurationSeconds, [s], s.ShotPurpose, s.TransitionIn, s.TransitionOut)).ToList(), [], [], []);
            return Task.FromResult(timeline);
        });
        var shots = shotTimeline.SceneSequences.SelectMany(sequence => sequence.Shots).ToList();
        var shotTimelinePath = Path.Combine(scenePlansDirectory, "weekly-cinematic-shot-timeline.json");
        await ExecuteOrchestrationStageAsyncNonGeneric("Persisting cinematic shot timeline", stageCt =>
            File.WriteAllTextAsync(shotTimelinePath, JsonSerializer.Serialize(shotTimeline, new JsonSerializerOptions { WriteIndented = true }), stageCt));

        var compositionPackage = await ExecuteOrchestrationStageAsync("Generating compositions", _ =>
        {
            var dailyForecastCount = weeklySkyfieldContext.SkyfieldSummary?.DailyForecastCount ?? 0;
            var geometryCount = (weeklySkyfieldContext.EventExtractionResult?.ExtractedEvents ?? [])
                .SelectMany(e => e.Objects ?? [])
                .Count(o => o.AltitudeDegrees.HasValue && o.AzimuthDegrees.HasValue);
            if (dailyForecastCount > 0 && geometryCount == 0)
                throw new InvalidOperationException("Skyfield geometry missing from persisted weekly response");
            var package = sceneComposer.Compose(shotTimeline, weeklySkyfieldContext.EventExtractionResult ?? throw new InvalidOperationException("Missing event extraction result."), root);
            return Task.FromResult(package);
        });
        var compositionPaths = new List<string>();
        var scriptPaths = new List<string>();
        app.Logger.LogInformation("WEEKLY_SCENE_PLAN_COUNT={Count}", weeklyScenePlan.ScenePlans.Count);
        app.Logger.LogInformation("CINEMATIC_TIMELINE_SCENE_COUNT={Count}", shots.Count);
        app.Logger.LogInformation("COMPOSITION_SCENE_COUNT={Count}", compositionPackage.Entries.Count);
        var generatedScripts = new List<(string ScriptPath, string ScriptContent)>();
        var cinematicQualityReports = new List<object>();
        var cinematicAttentionGuidanceReports = new List<object>();
        var allFramePlans = new List<CinematicSceneFramePlan>();
        var finalRenderSceneDescriptors = new List<FinalRenderSceneDescriptor>();
        var multiObjectSceneResolutionReports = new List<WeeklyMultiObjectSceneResolutionReport>();
        var scriptSourceSceneCodes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var generatedSplitMetadataBySceneCode = new Dictionary<string, GeneratedSplitSceneMetadata>(StringComparer.OrdinalIgnoreCase);
        var dynamicFramingEngine = new WeeklyDynamicMultiObjectFramingEngine();
        var dynamicFramingPlans = new List<WeeklyDynamicFramingPlan>();
        static string ResolveCinematicCompositionMode(string? sceneCode, string? framingMode)
        {
            if (!string.IsNullOrWhiteSpace(sceneCode))
            {
                if (sceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) return "MoonHero";
                if (sceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
                if (sceneCode.Equals("best_night_wide_scene", StringComparison.OrdinalIgnoreCase)) return "WideOrientation";
                if (sceneCode.Equals("viewing_tip_wide_scene", StringComparison.OrdinalIgnoreCase)) return "WideOrientation";
                if (sceneCode.Equals("thumbnail_story_scene", StringComparison.OrdinalIgnoreCase)) return "HorizonEpic";
            }

            if (!string.IsNullOrWhiteSpace(framingMode))
            {
                if (framingMode.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
                if (framingMode.Equals("OrientationWide", StringComparison.OrdinalIgnoreCase)) return "WideOrientation";
                if (framingMode.Equals("HeroObject", StringComparison.OrdinalIgnoreCase)) return "MoonHero";
            }

            return "DeepSkyIsolation";
        }

        static (double OffsetAz, double OffsetAlt, double TargetX, double TargetY, string Reason, List<string> Warnings) ComputeSubjectOffset(
            string compositionMode,
            IReadOnlyList<SkyObjectPosition> visibleObjects,
            double cameraAz,
            double cameraAlt)
        {
            var warnings = new List<string>();
            var targetX = 0.50d;
            var targetY = 0.50d;
            var azOffset = 0d;
            var altOffset = 0d;
            var reason = "No offset policy matched; preserving baseline camera.";
            if (compositionMode == "MoonHero")
            {
                targetX = 0.58d; targetY = 0.42d; azOffset = -3.5d; altOffset = 4d;
                reason = "MoonHero framing";
            }
            else if (compositionMode == "PlanetGrouping")
            {
                targetX = 0.50d; targetY = 0.62d; azOffset = 0d; altOffset = -3d;
                reason = "PlanetGrouping framing";
            }
            else if (compositionMode == "WideOrientation")
            {
                targetX = 0.50d; targetY = 0.65d; azOffset = 0d; altOffset = -1.5d;
                reason = "WideOrientation framing";
            }
            if (targetX < 0.20d || targetX > 0.80d || targetY < 0.20d || targetY > 0.80d)
            {
                warnings.Add("Requested subject placement exceeded safe frame bounds; clamped.");
                targetX = Math.Clamp(targetX, 0.20d, 0.80d);
                targetY = Math.Clamp(targetY, 0.20d, 0.80d);
            }
            return (cameraAz + azOffset, Math.Clamp(cameraAlt + altOffset, -5d, 85d), targetX, targetY, reason, warnings);
        }

        static (string attentionMode, string overlayDensity, string labelPriority, bool suppressPeripheralLabels, bool highlightPrimarySubject, string[] attentionWarnings, string reason) BuildAttentionPolicy(string compositionMode, string primarySubject)
        {
            if (compositionMode == "PlanetGrouping")
                return ("GroupFocus", "medium", "primary+secondary", false, true, Array.Empty<string>(), "PlanetGrouping policy.");
            if (compositionMode == "WideOrientation")
                return ("ContextualSky", "medium-high", "balanced", false, false, Array.Empty<string>(), "WideOrientation policy.");
            return ("PrimarySubject", "low-medium", string.IsNullOrWhiteSpace(primarySubject) ? "primary" : primarySubject, true, true, Array.Empty<string>(), "MoonHero policy.");
        }
        WeeklyScenePlan? ResolveRenderSceneArtifactScenePlan(string sceneCode, string? sourceSceneCode, IReadOnlyDictionary<string, WeeklyScenePlan> scenePlanIndex)
        {
            if (scenePlanIndex.TryGetValue(sceneCode, out var direct))
            {
                app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, sourceSceneCode, "ScenePlan", "SceneCode");
                return direct;
            }

            if (!string.IsNullOrWhiteSpace(sourceSceneCode) && scenePlanIndex.TryGetValue(sourceSceneCode, out var source))
            {
                app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, sourceSceneCode, "ScenePlan", "SourceSceneCode");
                return source;
            }

            if (generatedSplitMetadataBySceneCode.TryGetValue(sceneCode, out var split) && scenePlanIndex.TryGetValue(split.SourceSceneCode, out var splitSource))
            {
                app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, split.SourceSceneCode, "ScenePlan", "DerivedFallback");
                return splitSource;
            }

            app.Logger.LogError("SSC_ARTIFACT_RESOLUTION_FAILED sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} availableSceneCodes={AvailableSceneCodes}", sceneCode, sourceSceneCode, "ScenePlan", string.Join(',', scenePlanIndex.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            return null;
        }

        WeeklySceneCompositionEntry? ResolveRenderSceneArtifactComposition(string sceneCode, string? sourceSceneCode)
        {
            var direct = compositionPackage.Entries.FirstOrDefault(x => x.ShotCode.Equals(sceneCode, StringComparison.OrdinalIgnoreCase));
            if (direct is not null)
            {
                app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, sourceSceneCode, "Composition", "SceneCode");
                return direct;
            }

            if (!string.IsNullOrWhiteSpace(sourceSceneCode))
            {
                var source = compositionPackage.Entries.FirstOrDefault(x => x.ShotCode.Equals(sourceSceneCode, StringComparison.OrdinalIgnoreCase));
                if (source is not null)
                {
                    app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, sourceSceneCode, "Composition", "SourceSceneCode");
                    return source;
                }
            }

            if (generatedSplitMetadataBySceneCode.TryGetValue(sceneCode, out var split))
            {
                var splitSource = compositionPackage.Entries.FirstOrDefault(x => x.ShotCode.Equals(split.SourceSceneCode, StringComparison.OrdinalIgnoreCase));
                if (splitSource is not null)
                {
                    app.Logger.LogInformation("SSC_ARTIFACT_RESOLUTION sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} matchedBy={MatchedBy}", sceneCode, split.SourceSceneCode, "Composition", "DerivedFallback");
                    return splitSource;
                }
            }

            app.Logger.LogError("SSC_ARTIFACT_RESOLUTION_FAILED sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} artifactType={ArtifactType} availableSceneCodes={AvailableSceneCodes}", sceneCode, sourceSceneCode, "Composition", string.Join(',', compositionPackage.Entries.Select(x => x.ShotCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            return null;
        }
        await ExecuteOrchestrationStageAsync("Persisting composition files", async stageCt =>
        {
            foreach (var shot in shots)
            {
                var composition = ResolveRenderSceneArtifactComposition(shot.ShotCode, null)
                    ?? throw new InvalidOperationException($"Unable to resolve composition for scene '{shot.ShotCode}'.");
                var compositionPath = Path.Combine(compositionDirectory, $"{shot.ShotCode}.composition.json");
                await File.WriteAllTextAsync(compositionPath, JsonSerializer.Serialize(composition, new JsonSerializerOptions { WriteIndented = true }), stageCt);
                compositionPaths.Add(compositionPath);
            }
            return true;
        });

        (List<string> TargetObjects, string PrimaryObject) ResolveManifestTargets(string sceneCode, IReadOnlyList<string> defaultTargets)
        {
            var focusObjects = new HashSet<string>(weeklyFocusPlan.FocusObjects, StringComparer.OrdinalIgnoreCase);
            if (sceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase))
            {
                var planetTargets = new[] { "VENUS", "SATURN" }
                    .Where(focusObjects.Contains)
                    .ToList();
                if (planetTargets.Count > 0)
                    return (planetTargets, planetTargets.First());
            }

            if (sceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase))
            {
                var moonTargets = new[] { "MOON" }
                    .Where(focusObjects.Contains)
                    .ToList();
                if (moonTargets.Count > 0)
                    return (moonTargets, "MOON");
            }

            var deduped = defaultTargets
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeWeeklyObjectCode)
                .Where(x => x is not null)
                .Select(x => x!)
                .Where(code => !code.Equals("JUPITER", StringComparison.OrdinalIgnoreCase) || focusObjects.Contains("JUPITER"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (deduped, deduped.FirstOrDefault() ?? string.Empty);
        }

        var renderSceneManifestDescriptors = weeklyScenePlan.StellariumNeeds
            .Select(x =>
            {
                var resolvedTargets = ResolveManifestTargets(x.SceneCode, x.ObjectCodes);
                if (x.SceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)
                    && resolvedTargets.TargetObjects.Contains("MOON", StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Manifest corruption: western_planet_grouping_scene contains MOON");
                }

                if (x.SceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase)
                    && (resolvedTargets.TargetObjects.Count != 1
                        || !resolvedTargets.TargetObjects[0].Equals("MOON", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Manifest corruption: moon_hero_scene target objects invalid");
                }

                return new
                {
                    SourceSceneCode = string.IsNullOrWhiteSpace(x.SourceSceneCode) ? x.SceneCode : x.SourceSceneCode,
                    SceneCode = x.SceneCode,
                    RenderEngine = "Stellarium",
                    TargetObjects = resolvedTargets.TargetObjects,
                    PrimaryObject = resolvedTargets.PrimaryObject,
                    ExpectedSscScriptPath = Path.Combine(scriptsDirectory, $"{x.SceneCode}.ssc"),
                    ExpectedOutputImagePath = Path.Combine(scenesDirectory, $"{x.SceneCode}.png")
                };
            }).ToList();
        app.Logger.LogInformation("PRODUCTION_RENDER_FLOW_SOURCE source=ScenePlan+Timeline+Composition+InMemorySplit");
        app.Logger.LogInformation("STELLARIUM_RENDER_SCENE_COUNT={Count}", renderSceneManifestDescriptors.Count);
        if (request.Diagnostics)
        {
            var renderSceneManifestPath = Path.Combine(manifestsDirectory, "render-scene-manifest.json");
            await File.WriteAllTextAsync(renderSceneManifestPath, JsonSerializer.Serialize(new { StellariumScenes = renderSceneManifestDescriptors }, new JsonSerializerOptions { WriteIndented = true }), ct);
            app.Logger.LogInformation("DEBUG_RENDER_SCENE_MANIFEST_PATH={Path}", renderSceneManifestPath);
        }

        var stellariumNeedsByScene = weeklyScenePlan.StellariumNeeds
            .ToDictionary(x => x.SceneCode, StringComparer.OrdinalIgnoreCase);
        var scenePlansByCode = weeklyScenePlan.ScenePlans
            .ToDictionary(x => x.SceneCode, StringComparer.OrdinalIgnoreCase);
        var stellariumShots = renderSceneManifestDescriptors
            .Select(renderScene => weeklyScenePlan.StellariumNeeds.First(need => need.SceneCode.Equals(renderScene.SceneCode, StringComparison.OrdinalIgnoreCase)))
            .Select(need =>
            {
                var matchingShot = shots.FirstOrDefault(shot => shot.ShotCode.Equals(need.SceneCode, StringComparison.OrdinalIgnoreCase));
                if (matchingShot is not null)
                {
                    return matchingShot;
                }

                WeeklyScenePlan? fallbackScenePlan = null;
                var matchedBy = "Failed";

                if (scenePlansByCode.TryGetValue(need.SceneCode, out var directScenePlan))
                {
                    fallbackScenePlan = directScenePlan;
                    matchedBy = "SceneCode";
                }
                else if (!string.IsNullOrWhiteSpace(need.SourceSceneCode) && scenePlansByCode.TryGetValue(need.SourceSceneCode, out var sourceScenePlan))
                {
                    fallbackScenePlan = sourceScenePlan with { SceneCode = need.SceneCode };
                    matchedBy = "SourceSceneCode";
                }
                else
                {
                    fallbackScenePlan = DynamicSplitScenePlanResolver.Resolve(need.SceneCode, scenePlansByCode, generatedSplitMetadataBySceneCode, out var sourceSceneCode, out var metadataSource);
                    if (fallbackScenePlan is not null)
                    {
                        matchedBy = "DerivedDynamicSplit";
                        if (!string.IsNullOrWhiteSpace(sourceSceneCode))
                        {
                            app.Logger.LogInformation("DYNAMIC_SPLIT_SCENE_RESOLVED sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} metadataSource={MetadataSource}", need.SceneCode, sourceSceneCode, metadataSource);
                        }
                    }
                }

                app.Logger.LogInformation("STELLARIUM_NEED_SCENEPLAN_RESOLUTION needSceneCode={NeedSceneCode} sourceSceneCode={SourceSceneCode} matchedBy={MatchedBy}", need.SceneCode, need.SourceSceneCode, matchedBy);

                if (fallbackScenePlan is null)
                {
                    var availableSceneCodes = string.Join(", ", scenePlansByCode.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                    throw new InvalidOperationException($"Stellarium need '{need.SceneCode}' has no matching scene plan. Available original scene codes: [{availableSceneCodes}]");
                }

                return new WeeklyCinematicShot(
                    need.SceneCode,
                    fallbackScenePlan.SceneType,
                    fallbackScenePlan.RenderIntent,
                    need.ObjectCodes,
                    need.ObjectCodes.FirstOrDefault(),
                    need.TargetDate,
                    TimeOnly.FromDateTime((need.BestTimeUtc ?? request.ScheduledUtc.UtcDateTime).ToUniversalTime()),
                    fallbackScenePlan.DurationSeconds,
                    "W",
                    need.FieldOfViewDegrees,
                    need.FieldOfViewDegrees,
                    need.FieldOfViewDegrees,
                    new WeeklyCameraMovementPlan("static", fallbackScenePlan.CinematicMotion, "none", null, null, false),
                    new WeeklyShotTransitionPlan(fallbackScenePlan.TransitionIn, fallbackScenePlan.TransitionIn, "in"),
                    new WeeklyShotTransitionPlan(fallbackScenePlan.TransitionOut, fallbackScenePlan.TransitionOut, "out"),
                    fallbackScenePlan.CinematicMotion,
                    Path.Combine(scenesDirectory, $"{need.SceneCode}.png"),
                    string.Empty,
                    Path.Combine(scriptsDirectory, $"{need.SceneCode}.ssc"),
                    [],
                    new WeeklyShotNarrationSync(need.SceneCode, 0, fallbackScenePlan.DurationSeconds, fallbackScenePlan.VisualStrategy, need.ObjectCodes.FirstOrDefault(), []));
            })
            .ToList();

        app.Logger.LogInformation("Generating SSC scripts for {SceneCount} Stellarium scenes", stellariumShots.Count);

        await ExecuteOrchestrationStageAsync("Generating SSC scripts", async stageCt =>
        {
            var skyObjectsByCode = (weeklySkyfieldContext.EventExtractionResult?.ExtractedEvents ?? [])
                .SelectMany(e => e.Objects)
                .GroupBy(o => o.ObjectCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var latitude = weeklySkyfieldContext.StellariumBlueprintPackage?.Latitude ?? 24.5854d;
            var longitude = weeklySkyfieldContext.StellariumBlueprintPackage?.Longitude ?? 73.7125d;
            var locationName = weeklySkyfieldContext.Region;
            var timezone = weeklySkyfieldContext.StellariumBlueprintPackage?.Timezone ?? "UTC";
            const double elevationMeters = 600d;
            var defaultRules = new VisibilityRules
            {
                MinimumObjectAltitudeDeg = 10d,
                TwilightSunAltitudeThresholdDeg = -12d,
                MaximumMagnitude = 6d,
                MaximumGroupSpreadDeg = 70d
            };
            foreach (var shot in stellariumShots)
            {
                var stellariumNeed = stellariumNeedsByScene.TryGetValue(shot.ShotCode, out var resolvedNeed)
                    ? resolvedNeed
                    : throw new InvalidOperationException($"Missing Stellarium need for scene '{shot.ShotCode}'.");
                var composition = ResolveRenderSceneArtifactComposition(shot.ShotCode, stellariumNeed.SourceSceneCode)
                    ?? throw new InvalidOperationException($"Unable to resolve composition for SSC scene '{shot.ShotCode}'.");
                var scenePlan = ResolveRenderSceneArtifactScenePlan(shot.ShotCode, stellariumNeed.SourceSceneCode, scenePlansByCode)
                    ?? DynamicSplitScenePlanResolver.Resolve(shot.ShotCode, scenePlansByCode, generatedSplitMetadataBySceneCode, out _, out _);
                var sceneSpecificCodes = ResolveSceneSpecificObjectCodes(shot, composition, scenePlan, weeklySkyfieldContext);
                var resolvedNeedTargets = ResolveManifestTargets(shot.ShotCode, stellariumNeed.ObjectCodes);
                if (resolvedNeedTargets.TargetObjects.Count > 0)
                {
                    sceneSpecificCodes = resolvedNeedTargets.TargetObjects;
                }
                app.Logger.LogInformation("SSC_INPUT_TARGET_OBJECTS sceneCode={SceneCode} targetObjects={TargetObjects}", shot.ShotCode, string.Join(",", sceneSpecificCodes));
                var usedFallback = sceneSpecificCodes.Count == 0;
                if (usedFallback)
                {
                    app.Logger.LogWarning("SCENE_OBJECT_MAPPING_FALLBACK_USED sceneCode={SceneCode}", shot.ShotCode);
                    sceneSpecificCodes = composition.IncludedObjects
                        .Concat(composition.TargetObjects)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                var scheduledUtcFallback = request.ScheduledUtc == default || request.ScheduledUtc == DateTimeOffset.MinValue
                    ? (DateTime?)null
                    : request.ScheduledUtc.UtcDateTime;
                var planBestTimeUtc = scenePlan?.BestTimeUtc;
                var observationUtc = ResolveSceneObservationUtc(
                    shot.ShotCode,
                    shot.DateLocal,
                    shot.TimeLocal,
                    timezone,
                    sceneSpecificCodes,
                    weeklySkyfieldContext.EventExtractionResult?.ExtractedEvents ?? [],
                    planBestTimeUtc,
                    scheduledUtcFallback,
                    app.Logger);
                var selectedObservationLocal = ConvertUtcToLocal(observationUtc, timezone);

                app.Logger.LogInformation(
                    "WeeklySkyForecast V2 pre-resolution context sceneCode={SceneCode} selectedObservationUtc={SelectedObservationUtc} localTime={LocalTime} region={Region} objectNames={ObjectNames}",
                    shot.ShotCode,
                    observationUtc,
                    selectedObservationLocal,
                    weeklySkyfieldContext.Region,
                    string.Join(",", sceneSpecificCodes));
                app.Logger.LogInformation(
                    "SKYFIELD_OBJECT_RESOLUTION_TRACE sceneCode={SceneCode} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal} requestedTargetObjects={RequestedTargetObjects}",
                    shot.ShotCode,
                    observationUtc,
                    selectedObservationLocal,
                    string.Join(",", sceneSpecificCodes));

                var distinctSceneSpecificCodes = sceneSpecificCodes
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var multiObjectResolution = distinctSceneSpecificCodes.Count > 1
                    ? await ResolveMultiObjectSceneAsync(
                        shot.ShotCode,
                        distinctSceneSpecificCodes,
                        observationUtc,
                        shot.DateLocal,
                        timezone,
                        skyObjectsByCode,
                        weeklySkyfieldContext,
                        composition,
                        scenePlan,
                        shot,
                        app.Logger,
                        stageCt)
                    : null;
                if (multiObjectResolution is not null)
                {
                    observationUtc = multiObjectResolution.SelectedObservationUtc;
                    selectedObservationLocal = multiObjectResolution.SelectedObservationLocal;
                    multiObjectSceneResolutionReports.Add(multiObjectResolution.Report);
                }

                var skyPositions = multiObjectResolution?.ResolvedObjects ?? distinctSceneSpecificCodes
                    .Select(code =>
                    {
                        skyObjectsByCode.TryGetValue(code, out var obj);
                        var resolution = ResolveWeeklySkyObjectPosition(code, observationUtc, selectedObservationLocal, shot.DateLocal, composition, obj, weeklySkyfieldContext, distinctSceneSpecificCodes, temporalResolver, app.Logger, shot.ShotCode);
                        var objectName = obj?.ObjectName ?? code;
                        var objectType = ResolveObjectType(obj?.ObjectName ?? code);
                        var isPrimaryTarget = distinctSceneSpecificCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
                            || composition.TargetObjects.Contains(code, StringComparer.OrdinalIgnoreCase)
                            || (scenePlan?.ObjectCodes?.Contains(code, StringComparer.OrdinalIgnoreCase) ?? false);
                        var source = $"{ResolveObjectSource(code, composition, scenePlan, shot, weeklySkyfieldContext)}|{resolution.Source}";
                        app.Logger.LogInformation(
                            "SKYFIELD_OBJECT_RESOLUTION_TRACE object={Object} normalized={Normalized} dateKey={DateKey} timeKey={TimeKey} collection={Collection} matchFound={MatchFound} candidateNames={CandidateNames} candidateTimes={CandidateTimes} topLevelKeys={TopLevelKeys} availableDates={AvailableDates} selectedDateCollections={SelectedDateCollections} selectedDateObjectNames={SelectedDateObjectNames}",
                            resolution.RequestedName,
                            resolution.NormalizedName,
                            resolution.DateKey,
                            resolution.TimeKey,
                            resolution.CollectionSearched,
                            resolution.MatchFound,
                            resolution.CandidateNames,
                            resolution.CandidateTimes,
                            resolution.TopLevelKeys,
                            resolution.AvailableDates,
                            resolution.SelectedDateCollections,
                            resolution.SelectedDateObjectNames);
                        var weight = ResolveObjectWeight(objectName, objectType, isPrimaryTarget);
                        return new WeeklySceneObjectSelection(
                            new SkyObjectPosition(
                                Name: objectName,
                                AltitudeDeg: resolution.AltitudeDeg,
                                AzimuthDeg: resolution.AzimuthDeg,
                                Magnitude: resolution.Magnitude,
                                ObjectType: objectType,
                                Weight: weight),
                            source);
                    })
                    .ToList();
                var missingRequestedObjects = skyPositions
                    .Where(x => x.Source.Contains("source=fallback", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Position.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (missingRequestedObjects.Count > 0)
                {
                    app.Logger.LogError("SKYFIELD_OBJECTS_MISSING sceneCode={SceneCode} missingObjects={MissingObjects}", shot.ShotCode, string.Join(",", missingRequestedObjects));
                }
                if (skyPositions.Count > 0 && missingRequestedObjects.Count == skyPositions.Count)
                {
                    app.Logger.LogError("SSC_SKIPPED_ALL_TARGETS_MISSING sceneCode={SceneCode} requestedObjects={RequestedObjects}", shot.ShotCode, string.Join(",", sceneSpecificCodes));
                    continue;
                }

                var hasRealAltAz = skyPositions.Any(x => !x.Source.Contains("source=fallback", StringComparison.OrdinalIgnoreCase));
                if (!hasRealAltAz)
                {
                    app.Logger.LogError("SSC_SKIPPED_NO_REAL_GEOMETRY sceneCode={SceneCode} requestedObjects={RequestedObjects}", shot.ShotCode, string.Join(",", sceneSpecificCodes));
                    continue;
                }

                var sceneRequirement = new WeeklySceneRequirement(
                    shot.ShotCode,
                    stellariumNeed.SourceSceneCode,
                    ResolveDynamicEventType(scenePlan?.SceneType ?? shot.ShotType, shot.ShotCode, sceneSpecificCodes),
                    sceneSpecificCodes,
                    observationUtc,
                    selectedObservationLocal,
                    scenePlan?.SceneType ?? shot.ShotType,
                    shot.ShotPurpose);
                var dynamicPlan = await dynamicFramingEngine.BuildFramingPlanAsync(
                    sceneRequirement,
                    weeklySkyfieldContext,
                    weeklyFocusPlan,
                    skyPositions,
                    stageCt);
                dynamicFramingPlans.Add(dynamicPlan);
                app.Logger.LogInformation("DYNAMIC_FRAMING_PLAN_CREATED sceneCode={SceneCode} eventType={EventType} framingMode={FramingMode} splitRequired={SplitRequired} targetObjects={TargetObjects} resolvedObjects={ResolvedObjects}",
                    dynamicPlan.SceneCode,
                    dynamicPlan.EventType,
                    dynamicPlan.FramingMode,
                    dynamicPlan.SplitRequired,
                    string.Join(",", dynamicPlan.RequestedObjects),
                    string.Join(",", dynamicPlan.ResolvedObjects));
                if (dynamicPlan.SplitRequired)
                    app.Logger.LogInformation("DYNAMIC_FRAMING_SPLIT_REQUIRED sceneCode={SceneCode} clusterCount={ClusterCount}", dynamicPlan.SceneCode, dynamicPlan.Clusters.Count);
                ValidateWeeklyDynamicFramingPlan(dynamicPlan);

                IReadOnlyList<WeeklyDynamicSceneContract> dynamicScenes = dynamicPlan.SplitRequired ? dynamicPlan.Clusters : new[] { dynamicPlan.ToSceneContract() };
                foreach (var dynamicScene in dynamicScenes)
                {
                    ValidateWeeklyDynamicSceneContract(dynamicScene);
                    app.Logger.LogInformation("DYNAMIC_FRAMING_TARGET_LOCK_PASSED sceneCode={SceneCode} targetObjects={TargetObjects} cameraTargets={CameraTargets}", dynamicScene.SceneCode, string.Join(",", dynamicScene.TargetObjects), string.Join(",", dynamicScene.CameraTargetObjects));
                    app.Logger.LogInformation("SSC_GENERATOR_USING_DYNAMIC_FRAMING_PLAN sceneCode={SceneCode}", dynamicScene.SceneCode);

                    var dynamicScriptPath = Path.Combine(scriptsDirectory, $"{dynamicScene.SceneCode}.ssc");
                    var dynamicImagePath = Path.Combine(scenesDirectory, $"{dynamicScene.SceneCode}.png");
                    var dynamicSsc = BuildWeeklyDynamicFramingSsc(dynamicScene, observationUtc, selectedObservationLocal, longitude, latitude, elevationMeters, locationName, scenesDirectory);
                    generatedScripts.Add((dynamicScriptPath, dynamicSsc));
                    generatedSplitMetadataBySceneCode[dynamicScene.SceneCode] = new GeneratedSplitSceneMetadata(
                        dynamicScene.SceneCode,
                        dynamicScene.ParentSceneCode ?? shot.ShotCode,
                        dynamicScene.TargetObjects,
                        dynamicScene.PrimaryObject,
                        dynamicScene.FramingMode,
                        ResolveDynamicEventType(scenePlan?.SceneType ?? shot.ShotType, dynamicScene.SceneCode, dynamicScene.TargetObjects),
                        shot.DurationSeconds,
                        stellariumNeed.TargetDate,
                        observationUtc,
                        dynamicScriptPath,
                        dynamicImagePath);
                    if (!scriptSourceSceneCodes.TryGetValue(dynamicScene.SceneCode, out var dynamicSources))
                    {
                        dynamicSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        scriptSourceSceneCodes[dynamicScene.SceneCode] = dynamicSources;
                    }
                    dynamicSources.Add(shot.ShotCode);
                    app.Logger.LogInformation("DYNAMIC_FRAMING_SCENE_CREATED sceneCode={SceneCode} parentSceneCode={ParentSceneCode} targetObjects={TargetObjects} cameraAz={CameraAz} cameraAlt={CameraAlt} fov={Fov} inheritedFromParent={InheritedFromParent}",
                        dynamicScene.SceneCode,
                        dynamicScene.ParentSceneCode ?? string.Empty,
                        string.Join(",", dynamicScene.TargetObjects),
                        dynamicScene.CameraAzimuth,
                        dynamicScene.CameraAltitude,
                        dynamicScene.Fov,
                        dynamicScene.InheritedFromParent);

                    var dynamicFrameVariants = new[]
                    {
                        (FrameType: CinematicFrameType.HorizonContext, Name: "horizon_context", FovScale: 1.15d, MinFov: 18d, MaxFov: Math.Min(WeeklyFramingOptions.Default.AbsoluteMaxSingleFrameFov, Math.Max(dynamicScene.Fov, 60d)), PreserveHorizon: dynamicScene.IncludeHorizon, Purpose: "Dynamic geometry-driven horizon/context frame."),
                        (FrameType: CinematicFrameType.BalancedStoryFrame, Name: "balanced_story_frame", FovScale: 1.00d, MinFov: 18d, MaxFov: WeeklyFramingOptions.Default.AbsoluteMaxSingleFrameFov, PreserveHorizon: dynamicScene.IncludeHorizon, Purpose: "Dynamic geometry-driven balanced target frame."),
                        (FrameType: CinematicFrameType.DetailFocus, Name: "detail_focus", FovScale: 0.78d, MinFov: 18d, MaxFov: Math.Min(dynamicScene.Fov, 75d), PreserveHorizon: false, Purpose: "Dynamic geometry-driven detail frame.")
                    };
                    var dynamicFramePlans = new List<CinematicFramePlan>();
                    var dynamicFrameScriptDir = Path.Combine(scriptsDirectory, dynamicScene.SceneCode);
                    var dynamicFrameSceneDir = Path.Combine(scenesDirectory, dynamicScene.SceneCode);
                    Directory.CreateDirectory(dynamicFrameScriptDir);
                    Directory.CreateDirectory(dynamicFrameSceneDir);
                    for (var frameIndex = 0; frameIndex < dynamicFrameVariants.Length; frameIndex++)
                    {
                        var variant = dynamicFrameVariants[frameIndex];
                        var frameFov = Math.Clamp(dynamicScene.Fov * variant.FovScale, variant.MinFov, variant.MaxFov);
                        var frameOutputScriptName = $"{frameIndex + 1:00}_{variant.Name}.ssc";
                        var frameOutputImageName = $"{frameIndex + 1:00}_{variant.Name}.png";
                        var frameScriptPath = Path.Combine(dynamicFrameScriptDir, frameOutputScriptName);
                        var frameImagePath = Path.Combine(dynamicFrameSceneDir, frameOutputImageName);
                        var framePlan = new CinematicFramePlan(
                            $"{dynamicScene.SceneCode}_{frameIndex + 1:00}_{variant.Name}",
                            dynamicScene.ParentSceneCode ?? shot.ShotCode,
                            dynamicScene.SceneCode,
                            variant.FrameType,
                            frameIndex + 1,
                            dynamicScene.TargetObjects,
                            dynamicScene.PrimaryObject,
                            dynamicScene.CameraAzimuth,
                            dynamicScene.CameraAltitude,
                            frameFov,
                            variant.PreserveHorizon,
                            true,
                            true,
                            0.50d,
                            variant.PreserveHorizon ? 0.62d : 0.50d,
                            variant.Purpose,
                            dynamicScene.SplitRequired ? "Dynamic split scene with target-locked camera." : "Dynamic single-frame scene with target-locked camera.",
                            frameOutputScriptName,
                            frameOutputImageName,
                            frameScriptPath,
                            frameImagePath,
                            Path.Combine("stellarium", "scripts", dynamicScene.SceneCode, frameOutputScriptName),
                            Path.Combine("stellarium", "scenes", dynamicScene.SceneCode, frameOutputImageName),
                            []);
                        dynamicFramePlans.Add(framePlan);
                        var frameSsc = BuildWeeklyDynamicFramingSsc(dynamicScene with { Fov = frameFov }, observationUtc, selectedObservationLocal, longitude, latitude, elevationMeters, locationName, Path.GetDirectoryName(frameImagePath) ?? scenesDirectory, Path.GetFileNameWithoutExtension(frameImagePath));
                        generatedScripts.Add((frameScriptPath, frameSsc));
                        finalRenderSceneDescriptors.Add(new FinalRenderSceneDescriptor(
                            dynamicScene.SceneCode,
                            framePlan.FrameId,
                            false,
                            string.Empty,
                            "source=weekly-dynamic-framing-plan",
                            framePlan.ScriptPath,
                            framePlan.ImagePath,
                            true));
                    }
                    allFramePlans.Add(new CinematicSceneFramePlan(dynamicScene.SceneCode, dynamicScene.ParentSceneCode ?? shot.ShotCode, dynamicFramePlans));
                }
                continue;

                var compositionObjectsForSplit = skyPositions.Select(x => x.Position).ToList();
                var spatialComposition = spatialCompositionEngine.Analyze(compositionObjectsForSplit);
                var splitProbeSceneIntent = sceneIntentResolver.Resolve(shot.ShotCode, shot.ShotPurpose);
                var splitProbeSsc = sscIntelligenceService.Generate(new SscIntelligenceRequest(
                    observationUtc,
                    longitude,
                    latitude,
                    elevationMeters,
                    locationName,
                    skyPositions.Select(x => x.Position).ToList(),
                    defaultRules,
                    null,
                    "Asia/Kolkata",
                    null,
                    null,
                    splitProbeSceneIntent,
                    shot.ShotCode,
                    shot.ShotPurpose,
                    sceneSpecificCodes.ToList()),
                    scenesDirectory,
                    shot.ShotCode);
                var splitResult = narrativeSceneSplitter.Split(shot.ShotCode, shot.ShotPurpose, request.Language, weeklySkyfieldContext.Region, observationUtc, selectedObservationLocal, null, compositionObjectsForSplit, spatialComposition, splitProbeSsc.NightWindow, splitProbeSsc.RequiresSplit);
                if (multiObjectResolution is not null && splitProbeSsc.RequiresSplit)
                {
                    ReplaceMultiObjectSceneResolutionReport(multiObjectSceneResolutionReports, shot.ShotCode, true, true);
                }
                app.Logger.LogInformation("NARRATIVE_SCENE_SPLIT originalSceneCode={OriginalSceneCode} splitApplied={SplitApplied} reason={Reason} originalObjects={OriginalObjects} generatedScenes={GeneratedScenes} totalSceneCount={TotalSceneCount}", shot.ShotCode, splitResult.SplitApplied, splitResult.Reason, string.Join(',', compositionObjectsForSplit.Select(x=>x.Name)), string.Join('|', splitResult.Scenes.Select(scn=>$"{scn.SceneCode}:{scn.SceneRole}:{scn.SceneIntent}:[{string.Join(',', scn.TargetObjects.Select(o=>o.Name))}]")), splitResult.Scenes.Count);
                var screenshotPrefix = shot.ShotCode;
                var expectedScreenshotPath = Path.Combine(scenesDirectory, $"{screenshotPrefix}.png");
                var sceneIntent = sceneIntentResolver.Resolve(shot.ShotCode, shot.ShotPurpose);
                                app.Logger.LogInformation(
                    "WeeklySkyForecast V2 scene object mapping sceneCode={SceneCode} selectedObjectCount={SelectedObjectCount} selectedObjectNames={SelectedObjectNames} fallbackUsed={FallbackUsed}",
                    shot.ShotCode,
                    skyPositions.Count,
                    string.Join(",", skyPositions.Select(x => x.Position.Name)),
                    usedFallback);
                foreach (var selected in skyPositions)
                {
                    if (selected.Source.Contains("fallback", StringComparison.OrdinalIgnoreCase))
                    {
                        app.Logger.LogWarning("WeeklySkyForecast V2 scene object fallback used sceneCode={SceneCode} objectName={ObjectName} source={Source} selectedObservationUtc={SelectedObservationUtc} region={Region}", shot.ShotCode, selected.Position.Name, selected.Source, observationUtc, weeklySkyfieldContext.Region);
                    }
                    app.Logger.LogInformation(
                        "WeeklySkyForecast V2 scene object detail sceneCode={SceneCode} objectName={ObjectName} alt={Altitude} az={Azimuth} magnitude={Magnitude} source={Source}",
                        shot.ShotCode,
                        selected.Position.Name,
                        selected.Position.AltitudeDeg,
                        selected.Position.AzimuthDeg,
                        selected.Position.Magnitude,
                        selected.Source);
                    if (selected.Source.Contains("source=skyfield.exact", StringComparison.OrdinalIgnoreCase))
                    {
                        app.Logger.LogInformation(
                            "WeeklySkyForecast V2 post-resolution exact objectName={ObjectName} source=skyfield.exact altitude={Altitude} azimuth={Azimuth} magnitude={Magnitude}",
                            selected.Position.Name,
                            selected.Position.AltitudeDeg,
                            selected.Position.AzimuthDeg,
                            selected.Position.Magnitude);
                    }
                    else if (selected.Source.Contains("source=skyfield.nearest-time", StringComparison.OrdinalIgnoreCase))
                    {
                        app.Logger.LogInformation(
                            "WeeklySkyForecast V2 post-resolution nearest-time objectName={ObjectName} source=skyfield.nearest-time altitude={Altitude} azimuth={Azimuth} magnitude={Magnitude} trace={Trace}",
                            selected.Position.Name,
                            selected.Position.AltitudeDeg,
                            selected.Position.AzimuthDeg,
                            selected.Position.Magnitude,
                            selected.Source);
                    }
                }
                if (splitResult.SplitApplied)
                {
                    foreach (var splitScene in splitResult.Scenes.Take(3))
                    {
                        var splitObjectCodes = splitScene.SourceCluster
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (splitObjectCodes.Count == 0)
                        {
                            splitObjectCodes = splitScene.TargetObjects
                                .Select(x => x.Name)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                        }
                        var splitPrimaryObject = splitObjectCodes.FirstOrDefault() ?? splitScene.TargetObjects.FirstOrDefault()?.Name;
                        var splitSceneCode = multiObjectResolution is not null && splitObjectCodes.Count == 1 && !splitScene.SceneCode.Equals(shot.ShotCode, StringComparison.OrdinalIgnoreCase)
                            ? ResolveMultiObjectSplitSceneCode(shot.ShotCode, splitPrimaryObject ?? splitObjectCodes[0])
                            : splitScene.SceneCode;
                        var splitPrefix = splitSceneCode;
                        var splitSkyPositions = splitObjectCodes
                            .Select(code =>
                            {
                                skyObjectsByCode.TryGetValue(code, out var obj);
                                var resolution = ResolveWeeklySkyObjectPosition(code, observationUtc, selectedObservationLocal, shot.DateLocal, composition, obj, weeklySkyfieldContext, splitObjectCodes, temporalResolver, app.Logger, splitSceneCode);
                                var objectName = obj?.ObjectName ?? code;
                                var objectType = ResolveObjectType(obj?.ObjectName ?? code);
                                var source = $"{ResolveObjectSource(code, composition, scenePlan, shot, weeklySkyfieldContext)}|{resolution.Source}";
                                var weight = ResolveObjectWeight(objectName, objectType, true);
                                return new WeeklySceneObjectSelection(
                                    new SkyObjectPosition(
                                        Name: objectName,
                                        AltitudeDeg: resolution.AltitudeDeg,
                                        AzimuthDeg: resolution.AzimuthDeg,
                                        Magnitude: resolution.Magnitude,
                                        ObjectType: objectType,
                                        Weight: weight),
                                    source);
                            })
                            .ToList();
                        app.Logger.LogInformation("SSC_INPUT_TARGET_OBJECTS sceneCode={SceneCode} targetObjects={TargetObjects}", splitSceneCode, string.Join(",", splitObjectCodes));
                        var splitFallbackCount = splitSkyPositions.Count(x => x.Source.Contains("source=fallback", StringComparison.OrdinalIgnoreCase));
                        if (splitSkyPositions.Count > 0 && splitFallbackCount == splitSkyPositions.Count)
                        {
                            throw new InvalidOperationException($"DeferredHydrationFailure: split scene '{splitSceneCode}' could not resolve real geometry for target objects [{string.Join(",", splitObjectCodes)}].");
                        }
                        var splitDistinctAltAzCount = splitSkyPositions
                            .Select(x => $"{Math.Round(x.Position.AltitudeDeg, 3)}|{Math.Round(x.Position.AzimuthDeg, 3)}")
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();
                        if (splitDistinctAltAzCount == 1 && splitObjectCodes.Count > 1)
                        {
                            throw new InvalidOperationException($"Identical fallback geometry detected for split scene '{splitSceneCode}'.");
                        }
                        var splitResultSsc = sscIntelligenceService.Generate(new SscIntelligenceRequest(
                            observationUtc,longitude,latitude,elevationMeters,locationName,splitSkyPositions.Select(x => x.Position).ToList(),defaultRules,null,"Asia/Kolkata",null,null,splitScene.SceneIntent,splitSceneCode,shot.ShotPurpose,splitObjectCodes),
                            scenesDirectory,splitPrefix);
                        if (splitFallbackCount > 0)
                        {
                            throw new InvalidOperationException($"FallbackGeometryForbidden: split scene '{splitSceneCode}' contains fallback geometry.");
                        }
                        if (Math.Abs(splitResultSsc.CameraAltitudeDeg - 30d) < 0.0001d && Math.Abs(splitResultSsc.CameraAzimuthDeg - 270d) < 0.0001d)
                        {
                            throw new InvalidOperationException($"FallbackCameraForbidden: split scene '{splitSceneCode}' produced fallback camera alt/az.");
                        }
                        var splitScriptPath = Path.Combine(scriptsDirectory, $"{splitSceneCode}.ssc");
                        var splitHeader = string.Join(Environment.NewLine, new[] {"// Source: NarrativeSceneSplitter",$"// SourceSceneCode: {shot.ShotCode}",$"// Region: {weeklySkyfieldContext.Region}",$"// TargetDate: {stellariumNeed.TargetDate:yyyy-MM-dd}",$"// SelectedObservationUtc: {observationUtc:O}",$"// ScreenshotDirectory: {scenesDirectory.Replace('\\', '/')}",string.Empty});
                        generatedScripts.Add((splitScriptPath, splitHeader + splitResultSsc.SscScript));
                        var splitExpectedOutputImagePath = Path.Combine(scenesDirectory, $"{splitSceneCode}.png");
                        generatedSplitMetadataBySceneCode[splitSceneCode] = new GeneratedSplitSceneMetadata(
                            splitSceneCode,
                            splitScene.SourceSceneCode,
                            splitObjectCodes,
                            splitPrimaryObject,
                            splitScene.SceneRole.ToString(),
                            splitScene.SceneIntent.ToString(),
                            shot.DurationSeconds,
                            stellariumNeed.TargetDate,
                            splitScene.SelectedObservationUtc,
                            splitScriptPath,
                            splitExpectedOutputImagePath);
                        app.Logger.LogInformation(
                            "FINAL_RENDER_SCENE_DESCRIPTOR sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} targetObjects={TargetObjects} resolvedObjects={ResolvedObjects} fallbackUsed={FallbackUsed} cameraAlt={CameraAlt} cameraAz={CameraAz} fov={Fov} primaryObject={PrimaryObject} isDynamicSplitScene={IsDynamicSplitScene}",
                            splitSceneCode,
                            splitScene.SourceSceneCode,
                            string.Join(",", splitObjectCodes),
                            string.Join(",", splitSkyPositions.Select(x => x.Position.Name)),
                            splitFallbackCount > 0,
                            splitResultSsc.CameraAltitudeDeg,
                            splitResultSsc.CameraAzimuthDeg,
                            splitResultSsc.FovDeg,
                            splitPrimaryObject,
                            true);
                        if (!scriptSourceSceneCodes.TryGetValue(splitSceneCode, out var splitSources))
                        {
                            splitSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            scriptSourceSceneCodes[splitSceneCode] = splitSources;
                        }
                        splitSources.Add(shot.ShotCode);

                        var splitFrameVariants = new[]
                        {
                            (FrameType: CinematicFrameType.HorizonContext, Name: "horizon_context", FovScale: 1.20d, MinFov: 20d, MaxFov: 60d, PreserveHorizon: true, Purpose: "Horizon/context frame for split planet coverage."),
                            (FrameType: CinematicFrameType.BalancedStoryFrame, Name: "balanced_story_frame", FovScale: 1.00d, MinFov: 18d, MaxFov: 55d, PreserveHorizon: true, Purpose: "Narrative-balanced split planet frame."),
                            (FrameType: CinematicFrameType.DetailFocus, Name: "detail_focus", FovScale: 0.75d, MinFov: 18d, MaxFov: 45d, PreserveHorizon: false, Purpose: "Closer labeled split planet frame.")
                        };
                        var splitFramePlans = new List<CinematicFramePlan>();
                        var splitFrameScriptDir = Path.Combine(scriptsDirectory, splitSceneCode);
                        var splitFrameSceneDir = Path.Combine(scenesDirectory, splitSceneCode);
                        Directory.CreateDirectory(splitFrameScriptDir);
                        Directory.CreateDirectory(splitFrameSceneDir);
                        for (var frameIndex = 0; frameIndex < splitFrameVariants.Length; frameIndex++)
                        {
                            var variant = splitFrameVariants[frameIndex];
                            var frameFov = Math.Clamp(splitResultSsc.FovDeg * variant.FovScale, variant.MinFov, variant.MaxFov);
                            var frameOutputScriptName = $"{frameIndex + 1:00}_{variant.Name}.ssc";
                            var frameOutputImageName = $"{frameIndex + 1:00}_{variant.Name}.png";
                            var frameScriptPath = Path.Combine(splitFrameScriptDir, frameOutputScriptName);
                            var frameImagePath = Path.Combine(splitFrameSceneDir, frameOutputImageName);
                            var framePlan = new CinematicFramePlan(
                                $"{splitSceneCode}_{frameIndex + 1:00}_{variant.Name}",
                                shot.ShotCode,
                                splitSceneCode,
                                variant.FrameType,
                                frameIndex + 1,
                                splitObjectCodes,
                                splitPrimaryObject,
                                splitResultSsc.CameraAzimuthDeg,
                                splitResultSsc.CameraAltitudeDeg,
                                frameFov,
                                variant.PreserveHorizon,
                                true,
                                true,
                                0.50d,
                                variant.PreserveHorizon ? 0.62d : 0.50d,
                                variant.Purpose,
                                "Split-scene visual support for an impossible single-frame planet grouping.",
                                frameOutputScriptName,
                                frameOutputImageName,
                                frameScriptPath,
                                frameImagePath,
                                Path.Combine("stellarium", "scripts", splitSceneCode, frameOutputScriptName),
                                Path.Combine("stellarium", "scenes", splitSceneCode, frameOutputImageName),
                                []);
                            splitFramePlans.Add(framePlan);

                            var frameScreenshotDirectory = (Path.GetDirectoryName(frameImagePath) ?? scenesDirectory).Replace("\\", "/");
                            var frameScreenshotFileName = Path.GetFileNameWithoutExtension(frameImagePath);
                            var frameSsc = Regex.Replace(
                                splitHeader + splitResultSsc.SscScript,
                                @"core\.moveToAltAzi\([^\)]*\);",
                                $"core.moveToAltAzi(\"{splitResultSsc.CameraAltitudeDeg.ToString("0.###", CultureInfo.InvariantCulture)}d\", \"{splitResultSsc.CameraAzimuthDeg.ToString("0.###", CultureInfo.InvariantCulture)}d\", 1);",
                                RegexOptions.CultureInvariant);
                            frameSsc = Regex.Replace(
                                frameSsc,
                                @"StelMovementMgr\.zoomTo\([^\)]*\);",
                                $"StelMovementMgr.zoomTo({frameFov.ToString("0.###", CultureInfo.InvariantCulture)}, 2);",
                                RegexOptions.CultureInvariant);
                            frameSsc = Regex.Replace(
                                frameSsc,
                                @"core\.screenshot\([^\)]*\);",
                                $"core.screenshot(\"{frameScreenshotFileName.Replace("\"", "\\\"")}\", false, \"{frameScreenshotDirectory.Replace("\"", "\\\"")}\", true, \"png\");",
                                RegexOptions.CultureInvariant);
                            generatedScripts.Add((frameScriptPath, frameSsc));
                            finalRenderSceneDescriptors.Add(new FinalRenderSceneDescriptor(
                                splitSceneCode,
                                framePlan.FrameId,
                                false,
                                string.Empty,
                                string.Join("|", splitSkyPositions.Select(x => x.Source)),
                                framePlan.ScriptPath,
                                framePlan.ImagePath,
                                true));
                        }
                        allFramePlans.Add(new CinematicSceneFramePlan(splitSceneCode, shot.ShotCode, splitFramePlans));
                    }
                    continue;
                }

                var sscResult = splitProbeSsc;
                if (sscResult.RequiresSplit)
                {
                    app.Logger.LogWarning("WeeklySkyForecast V2 scene {SceneId} requires split, fallback to single SSC with computed center/FOV. reason=requiresSplit", shot.ShotCode);
                }

                var syntheticFallbackUsed = skyPositions.Any(x => x.Source.Contains("source=fallback", StringComparison.OrdinalIgnoreCase));
                if (syntheticFallbackUsed)
                {
                    app.Logger.LogWarning("SSC_SKIPPED_FALLBACK_GEOMETRY sceneCode={SceneCode} requestedObjects={RequestedObjects}", shot.ShotCode, string.Join(",", sceneSpecificCodes));
                    continue;
                }
                var identicalGeometry = skyPositions
                    .Select(x => $"{Math.Round(x.Position.AltitudeDeg, 3)}|{Math.Round(x.Position.AzimuthDeg, 3)}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() <= 1
                    && skyPositions.Count > 1;
                if (identicalGeometry)
                {
                    app.Logger.LogCritical(
                        "WeeklySkyForecast V2 identical scene geometry detected sceneCode={SceneCode} selectedObjectCount={SelectedObjectCount} selectedObservationUtc={SelectedObservationUtc}",
                        shot.ShotCode,
                        skyPositions.Count,
                        observationUtc);
                }

                if (!syntheticFallbackUsed)
                {
                    var sceneScore = astronomicalSceneScorer.Score(
                        shot.ShotCode,
                        shot.ShotPurpose,
                        sceneIntent.ToString(),
                        sscResult.VisibleObjects,
                        sscResult.NightWindow);

                    app.Logger.LogInformation(
                        "WeeklySkyForecast V2 storytelling diagnostics sceneCode={SceneCode} eventType={EventType} significanceScore={SignificanceScore} closestPair={ClosestPair} closestPairSeparation={ClosestPairSeparation} maxSpread={MaxSpread} brightestObject={BrightestObject} recommendedPrimaryTargets={RecommendedPrimaryTargets} reason={Reason}",
                        shot.ShotCode,
                        sceneScore.EventType,
                        sceneScore.Score,
                        sceneScore.AngularRelationships.ClosestPair is null ? "" : $"{sceneScore.AngularRelationships.ClosestPair.ObjectA}-{sceneScore.AngularRelationships.ClosestPair.ObjectB}",
                        sceneScore.AngularRelationships.ClosestPair?.SeparationDeg,
                        sceneScore.AngularRelationships.MaxSpreadDeg,
                        sceneScore.AngularRelationships.BrightestObject?.Name,
                        string.Join(",", sceneScore.RecommendedPrimaryTargets),
                        sceneScore.Reason);
                }
                else
                {
                    app.Logger.LogWarning(
                        "WeeklySkyForecast V2 storytelling diagnostics skipped due to fallback geometry sceneCode={SceneCode} selectedObservationUtc={SelectedObservationUtc}",
                        shot.ShotCode,
                        observationUtc);
                }
                app.Logger.LogInformation(
                    "WeeklySkyForecast V2 SSC intelligence sceneCode={SceneCode} sceneIntent={SceneIntent} primaryTargets={PrimaryTargets} secondaryTargets={SecondaryTargets} contextTargets={ContextTargets} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal} isNight={IsNight} sunAltitudeDeg={SunAltitudeDeg} screenshotDirectory={ScreenshotDirectory} screenshotPrefix={ScreenshotPrefix} expectedScreenshotPath={ExpectedScreenshotPath} visibleObjectCount={VisibleObjectCount} removedObjectCount={RemovedObjectCount} rawCameraAltitude={RawCameraAltitude} adjustedCameraAltitude={CameraAltitude} cameraAzimuth={CameraAzimuth} fov={Fov} compositionBiasReason={CompositionBiasReason} requiresSplit={RequiresSplit}",
                    shot.ShotCode,
                    sceneIntent,
                    string.Join(",", sscResult.PrimaryTargets),
                    string.Join(",", sscResult.SecondaryTargets),
                    string.Join(",", sscResult.ContextTargets),
                    sscResult.NightWindow.BestObservationUtc,
                    sscResult.NightWindow.BestObservationLocalTime,
                    sscResult.NightWindow.IsNight,
                    sscResult.NightWindow.SunAltitudeDeg,
                    scenesDirectory,
                    screenshotPrefix,
                    expectedScreenshotPath,
                    sscResult.VisibleObjects.Count,
                    sscResult.RemovedObjects.Count,
                    sscResult.RawCameraAltitudeDeg,
                    sscResult.CameraAltitudeDeg,
                    sscResult.CameraAzimuthDeg,
                    sscResult.FovDeg,
                    sscResult.CompositionBiasReason,
                    sscResult.RequiresSplit);
                if (Math.Abs(sscResult.CameraAltitudeDeg - 30d) < 0.0001d && Math.Abs(sscResult.CameraAzimuthDeg - 270d) < 0.0001d)
                {
                    app.Logger.LogError("SSC_SKIPPED_FALLBACK_CAMERA sceneCode={SceneCode} cameraAlt={CameraAlt} cameraAz={CameraAz}", shot.ShotCode, sscResult.CameraAltitudeDeg, sscResult.CameraAzimuthDeg);
                    continue;
                }
                var framingMode = sscResult.CinematicQualityReport?.CameraPlan.FramingMode;
                var compositionMode = ResolveCinematicCompositionMode(shot.ShotCode, framingMode);
                app.Logger.LogInformation("CINEMATIC_COMPOSITION_MODE_RESOLVED sceneCode={SceneCode} framingMode={FramingMode} compositionMode={CompositionMode}", shot.ShotCode, framingMode, compositionMode);
                var subjectOffset = ComputeSubjectOffset(compositionMode, skyPositions.Select(x => x.Position).ToList(), sscResult.CameraAzimuthDeg, sscResult.CameraAltitudeDeg);
                var attentionPolicy = BuildAttentionPolicy(compositionMode, sceneSpecificCodes.FirstOrDefault() ?? string.Empty);
                app.Logger.LogInformation("SUBJECT_OFFSET_COMPOSITION sceneCode={SceneCode} intent={Intent} primarySubject={PrimarySubject} originalCameraAz={OriginalCameraAz} originalCameraAlt={OriginalCameraAlt} offsetCameraAz={OffsetCameraAz} offsetCameraAlt={OffsetCameraAlt} targetScreenX={TargetScreenX} targetScreenY={TargetScreenY} offsetReason={OffsetReason} safetyWarnings={SafetyWarnings}",
                    shot.ShotCode, sceneIntent, sceneSpecificCodes.FirstOrDefault() ?? string.Empty, sscResult.CameraAzimuthDeg, sscResult.CameraAltitudeDeg, subjectOffset.OffsetAz, subjectOffset.OffsetAlt, subjectOffset.TargetX, subjectOffset.TargetY, subjectOffset.Reason, string.Join("|", subjectOffset.Warnings));
                app.Logger.LogInformation("ATTENTION_GUIDANCE_POLICY sceneCode={SceneCode} attentionMode={AttentionMode} overlayDensity={OverlayDensity} labelPriority={LabelPriority} suppressPeripheralLabels={SuppressPeripheralLabels} highlightPrimarySubject={HighlightPrimarySubject} reason={Reason}",
                    shot.ShotCode, attentionPolicy.attentionMode, attentionPolicy.overlayDensity, attentionPolicy.labelPriority, attentionPolicy.suppressPeripheralLabels, attentionPolicy.highlightPrimarySubject, attentionPolicy.reason);
                List<(CinematicFrameType frameType, string name, double fovScale, double? minFov, double? maxFov, double? subjectX, double? subjectY, bool preserveHorizon, bool preserveLabels, bool preserveLines, string purpose)> variants = compositionMode switch
                {
                    "MoonHero" =>
                    [
                        (CinematicFrameType.EstablishingWide, "establishing_wide", 1.35d, 15d, 75d, 0.55d, 0.45d, false, true, true, "Wider contextual moon framing."),
                        (CinematicFrameType.BalancedStoryFrame, "balanced_story_frame", 1.00d, 15d, 75d, subjectOffset.TargetX, subjectOffset.TargetY, false, true, true, "Primary narrative-balanced frame."),
                        (CinematicFrameType.HeroCloseup, "hero_closeup", 0.65d, 18d, 75d, 0.58d, 0.42d, false, false, false, "Hero close-up emphasizing moon.")
                    ],
                    "PlanetGrouping" =>
                    [
                        (CinematicFrameType.HorizonContext, "horizon_context", 1.25d, 15d, 65d, 0.50d, 0.62d, true, true, true, "Horizon anchored contextual grouping."),
                        (CinematicFrameType.BalancedStoryFrame, "balanced_story_frame", 1.00d, 15d, 75d, subjectOffset.TargetX, subjectOffset.TargetY, false, true, true, "Primary narrative-balanced grouping frame."),
                        (CinematicFrameType.AlignmentWide, "alignment_wide", 1.15d, 15d, 60d, 0.50d, 0.55d, false, true, true, "Slightly wider alignment context.")
                    ],
                    _ =>
                    [
                        (CinematicFrameType.EstablishingWide, "establishing_wide", 1.20d, 15d, 75d, subjectOffset.TargetX, subjectOffset.TargetY, false, true, true, "Wide contextual frame."),
                        (CinematicFrameType.EducationalContext, "educational_context", 1.00d, 15d, 75d, subjectOffset.TargetX, subjectOffset.TargetY, true, true, true, "Educational contextual frame.")
                    ]
                };
                app.Logger.LogInformation("CINEMATIC_FRAME_PLANNER_START sceneCode={SceneCode}", shot.ShotCode);
                var framePlans = new List<CinematicFramePlan>();
                var frameScriptDir = Path.Combine(scriptsDirectory, shot.ShotCode);
                var frameSceneDir = Path.Combine(scenesDirectory, shot.ShotCode);
                Directory.CreateDirectory(frameScriptDir);
                Directory.CreateDirectory(frameSceneDir);
                for (var i = 0; i < variants.Count; i++)
                {
                    var v = variants[i];
                    var rawFov = sscResult.FovDeg * v.fovScale;
                    var boundedMax = v.maxFov ?? 75d;
                    var fov = Math.Clamp(rawFov, v.minFov ?? 15d, Math.Min(75d, boundedMax));
                    var framePlan = new CinematicFramePlan(
                        $"{shot.ShotCode}_{i + 1:00}_{v.name}",
                        stellariumNeed.SourceSceneCode ?? shot.ShotCode,
                        shot.ShotCode,
                        v.frameType,
                        i + 1,
                        sceneSpecificCodes,
                        sceneSpecificCodes.FirstOrDefault(),
                        subjectOffset.OffsetAz,
                        subjectOffset.OffsetAlt,
                        fov,
                        v.preserveHorizon,
                        v.preserveLabels,
                        v.preserveLines,
                        v.subjectX ?? subjectOffset.TargetX,
                        v.subjectY ?? subjectOffset.TargetY,
                        v.purpose,
                        "WeeklySkyForecastV2",
                        $"{i + 1:00}_{v.name}.ssc",
                        $"{i + 1:00}_{v.name}.png",
                        Path.Combine(frameScriptDir, $"{i + 1:00}_{v.name}.ssc"),
                        Path.Combine(frameSceneDir, $"{i + 1:00}_{v.name}.png"),
                        Path.Combine("stellarium", "scripts", shot.ShotCode, $"{i + 1:00}_{v.name}.ssc"),
                        Path.Combine("stellarium", "scenes", shot.ShotCode, $"{i + 1:00}_{v.name}.png"),
                        subjectOffset.Warnings);
                    framePlans.Add(framePlan);
                    app.Logger.LogInformation("CINEMATIC_FRAME_PLAN_CREATED sceneCode={SceneCode} frameType={FrameType} frameIndex={FrameIndex} cameraAz={CameraAz} cameraAlt={CameraAlt} fov={Fov} outputImageName={OutputImageName} safetyWarnings={SafetyWarnings}", shot.ShotCode, framePlan.FrameType, framePlan.FrameIndex, framePlan.CameraAzimuth, framePlan.CameraAltitude, framePlan.Fov, framePlan.OutputImageName, string.Join("|", framePlan.SafetyWarnings));
                }
                allFramePlans.Add(new CinematicSceneFramePlan(shot.ShotCode, stellariumNeed.SourceSceneCode ?? shot.ShotCode, framePlans));
                var primaryFrame = framePlans.First(x => x.FrameType == CinematicFrameType.BalancedStoryFrame);
                app.Logger.LogInformation("PRIMARY_FRAME_SELECTED sceneCode={SceneCode} frameType={FrameType} frameIndex={FrameIndex} cameraAz={CameraAz} cameraAlt={CameraAlt} fov={Fov} outputImageName={OutputImageName} safetyWarnings={SafetyWarnings}",
                    shot.ShotCode, primaryFrame.FrameType, primaryFrame.FrameIndex, primaryFrame.CameraAzimuth, primaryFrame.CameraAltitude, primaryFrame.Fov, primaryFrame.OutputImageName, string.Join("|", primaryFrame.SafetyWarnings));
                foreach (var frame in framePlans)
                {
                    var scriptPath = frame.ScriptPath;
                    var frameImagePath = frame.ImagePath;
                    var frameScreenshotDirectory = (Path.GetDirectoryName(frameImagePath) ?? scenesDirectory).Replace("\\", "/");
                    var frameScreenshotFileName = Path.GetFileNameWithoutExtension(frameImagePath);
                    var frameSsc = sscResult.SscScript
                        .Replace($"core.moveToAltAzi({sscResult.CameraAltitudeDeg.ToString("0.###", CultureInfo.InvariantCulture)}, {sscResult.CameraAzimuthDeg.ToString("0.###", CultureInfo.InvariantCulture)}", $"core.moveToAltAzi({frame.CameraAltitude.ToString("0.###", CultureInfo.InvariantCulture)}, {frame.CameraAzimuth.ToString("0.###", CultureInfo.InvariantCulture)}", StringComparison.Ordinal)
                        .Replace($"StelMovementMgr.zoomTo({sscResult.FovDeg.ToString("0.###", CultureInfo.InvariantCulture)}", $"StelMovementMgr.zoomTo({frame.Fov.ToString("0.###", CultureInfo.InvariantCulture)}", StringComparison.Ordinal);
                    frameSsc = Regex.Replace(
                        frameSsc,
                        @"core\.screenshot\([^\)]*\);",
                        $"core.screenshot(\"{frameScreenshotFileName.Replace("\"", "\\\"")}\", false, \"{frameScreenshotDirectory.Replace("\"", "\\\"")}\", true, \"png\");",
                        RegexOptions.CultureInvariant);
                    generatedScripts.Add((scriptPath, frameSsc));
                    finalRenderSceneDescriptors.Add(new FinalRenderSceneDescriptor(
                        shot.ShotCode,
                        frame.FrameId,
                        frame.FrameGenerationUsedFallback,
                        frame.FallbackReason ?? string.Empty,
                        string.Join("|", skyPositions.Select(x => x.Source)),
                        frame.ScriptPath,
                        frame.ImagePath,
                        true));
                    app.Logger.LogInformation("FRAME_SSC_GENERATED sceneCode={SceneCode} frameType={FrameType} frameIndex={FrameIndex} cameraAz={CameraAz} cameraAlt={CameraAlt} fov={Fov} outputImageName={OutputImageName} scriptPath={ScriptPath} imagePath={ImagePath} safetyWarnings={SafetyWarnings}",
                        shot.ShotCode, frame.FrameType, frame.FrameIndex, frame.CameraAzimuth, frame.CameraAltitude, frame.Fov, frame.OutputImageName, frame.ScriptPath, frame.ImagePath, string.Join("|", frame.SafetyWarnings));
                    app.Logger.LogInformation("FRAME_SCRIPT_PATH_RESOLVED sceneCode={SceneCode} frameIndex={FrameIndex} frameType={FrameType} scriptPath={ScriptPath} imagePath={ImagePath}",
                        shot.ShotCode, frame.FrameIndex, frame.FrameType, frame.ScriptPath, frame.ImagePath);
                }
                app.Logger.LogInformation(
                    "FINAL_RENDER_SCENE_DESCRIPTOR sceneCode={SceneCode} sourceSceneCode={SourceSceneCode} targetObjects={TargetObjects} resolvedObjects={ResolvedObjects} fallbackUsed={FallbackUsed} cameraAlt={CameraAlt} cameraAz={CameraAz} fov={Fov} primaryObject={PrimaryObject} isDynamicSplitScene={IsDynamicSplitScene}",
                    shot.ShotCode,
                    stellariumNeed.SourceSceneCode ?? string.Empty,
                    string.Join(",", sceneSpecificCodes),
                    string.Join(",", skyPositions.Select(x => x.Position.Name)),
                    syntheticFallbackUsed,
                    sscResult.CameraAltitudeDeg,
                    sscResult.CameraAzimuthDeg,
                    sscResult.FovDeg,
                    sceneSpecificCodes.FirstOrDefault() ?? string.Empty,
                    stellariumNeed.IsDynamicSplitScene);
                cinematicQualityReports.Add(new
                {
                    sceneCode = shot.ShotCode,
                    subjectOffset = new { offsetCameraAzimuth = subjectOffset.OffsetAz, offsetCameraAltitude = subjectOffset.OffsetAlt, subjectScreenPlacement = new { x = subjectOffset.TargetX, y = subjectOffset.TargetY }, offsetReason = subjectOffset.Reason },
                    attentionGuidance = attentionPolicy,
                    finalCameraAfterOffset = new { cameraAzimuth = subjectOffset.OffsetAz, cameraAltitude = subjectOffset.OffsetAlt, fov = sscResult.FovDeg },
                    safetyWarnings = subjectOffset.Warnings
                });
                cinematicAttentionGuidanceReports.Add(attentionPolicy);
                if (!scriptSourceSceneCodes.TryGetValue(shot.ShotCode, out var originalSources))
                {
                    originalSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    scriptSourceSceneCodes[shot.ShotCode] = originalSources;
                }
                originalSources.Add(shot.ShotCode);
            }
            return true;
        });
        var dynamicFramingValidationReport = BuildWeeklyDynamicFramingValidationReport(dynamicFramingPlans);
        await File.WriteAllTextAsync(weeklyDynamicFramingPlanPath, JsonSerializer.Serialize(new
        {
            dynamicFramingReady = dynamicFramingValidationReport.DynamicFramingReady,
            generatedUtc = DateTime.UtcNow,
            scenes = dynamicFramingPlans.SelectMany(plan => plan.SplitRequired ? plan.Clusters : (IReadOnlyList<WeeklyDynamicSceneContract>)new[] { plan.ToSceneContract() })
        }, new JsonSerializerOptions { WriteIndented = true }), ct);
        await File.WriteAllTextAsync(weeklyFramingValidationReportPath, JsonSerializer.Serialize(dynamicFramingValidationReport, new JsonSerializerOptions { WriteIndented = true }), ct);
        app.Logger.LogInformation("DYNAMIC_FRAMING_REPORTS_WRITTEN planPath={PlanPath} validationPath={ValidationPath} dynamicFramingReady={DynamicFramingReady} allCameraTargetsLocked={AllCameraTargetsLocked} allTargetLabelsEnabled={AllTargetLabelsEnabled}", weeklyDynamicFramingPlanPath, weeklyFramingValidationReportPath, dynamicFramingValidationReport.DynamicFramingReady, dynamicFramingValidationReport.AllCameraTargetsLocked, dynamicFramingValidationReport.AllTargetLabelsEnabled);

        var dynamicFramingScenesFromPlan = await ReadWeeklyDynamicScenesFromPlanFileAsync(weeklyDynamicFramingPlanPath, ct);
        var dynamicFramingScenesByCode = dynamicFramingScenesFromPlan
            .GroupBy(x => x.SceneCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var qualityDirectory = Path.Combine(root, "cinematic");
        Directory.CreateDirectory(qualityDirectory);
        var framePlanPath = Path.Combine(qualityDirectory, "cinematic-frame-plan.json");
        var qualityPath = Path.Combine(qualityDirectory, "cinematic-quality-report.json");
        await File.WriteAllTextAsync(framePlanPath, JsonSerializer.Serialize(allFramePlans, new JsonSerializerOptions { WriteIndented = true }), ct);
        try
        {
            await File.WriteAllTextAsync(qualityPath, JsonSerializer.Serialize(new
            {
                framePlans = allFramePlans,
                finalRenderSceneDescriptors,
                selectedPrimaryFrame = "BalancedStoryFrame",
                frameCount = allFramePlans.Sum(x => x.FramePlans.Count),
                sceneQuality = cinematicQualityReports,
                attentionGuidance = cinematicAttentionGuidanceReports
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "CINEMATIC_QUALITY_REPORT_WRITE_FAILED path={Path}", qualityPath);
            throw new InvalidOperationException($"CINEMATIC_QUALITY_REPORT_WRITE_FAILED path='{qualityPath}' exception='{ex.Message}'", ex);
        }

        var finalScenes = allFramePlans
            .SelectMany(plan => plan.FramePlans.Select(frame => new WeeklySscSceneFinalizer.FinalSscScene(
                SceneCode: $"{plan.RenderSceneCode}/{frame.OutputScriptName}",
                ScriptPath: frame.ScriptPath,
                ScreenshotPath: frame.ImagePath,
                SourceSceneCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { plan.SourceSceneCode, plan.RenderSceneCode })))
            .OrderBy(x => x.SceneCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await ExecuteOrchestrationStageAsync("Persisting SSC scripts", async stageCt =>
        {
            var finalScriptsByPath = generatedScripts
                .GroupBy(x => x.ScriptPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().ScriptContent, StringComparer.OrdinalIgnoreCase);

            app.Logger.LogInformation(
                "FINAL_STELLARIUM_SCENE_LIST sceneCodes={SceneCodes}",
                string.Join(",", finalScenes.Select(x => x.SceneCode)));
            foreach (var finalScene in finalScenes)
            {
                var renderSceneCode = ResolveWeeklyRenderSceneCodeFromFinalSceneCode(finalScene.SceneCode);
                if (!dynamicFramingScenesByCode.TryGetValue(renderSceneCode, out var propagatedScene))
                    throw new InvalidOperationException($"SSC propagation failed for {finalScene.SceneCode}: dynamic framing metadata was not propagated.");
                ValidateWeeklySscPropagationScene(finalScene.SceneCode, propagatedScene);

                var scriptContent = finalScriptsByPath[finalScene.ScriptPath];
                await File.WriteAllTextAsync(finalScene.ScriptPath, scriptContent, stageCt);
                scriptPaths.Add(finalScene.ScriptPath);
                app.Logger.LogInformation("FINAL_SSC_SCRIPT_PATHS sceneCode={SceneCode} path={ScriptPath}", finalScene.SceneCode, finalScene.ScriptPath);
            }
            return true;
        });
        await ExecuteOrchestrationStageAsync("Validating SSC scripts", _ =>
        {
            foreach (var scriptPath in scriptPaths)
            {
                var info = new FileInfo(scriptPath);
                if (!info.Exists || info.Length == 0)
                    throw new InvalidOperationException($"SSC script validation failed for '{scriptPath}'.");
                var scriptContent = File.ReadAllText(scriptPath);
                var requiredSnippets = new[]
                {
                    "core.screenshot(",
                    "core.quitStellarium();",
                    "ConstellationMgr.setFlagLines(true);",
                    "ConstellationMgr.setFlagLabels(true);",
                    "SolarSystem.setFlagLabels(true);",
                    "StelMovementMgr.setFlagTracking(true);",
                    "StelMovementMgr.zoomTo",
                    "core.moveToSelectedObject("
                };
                foreach (var snippet in requiredSnippets)
                {
                    if (!scriptContent.Contains(snippet, StringComparison.Ordinal))
                        throw new InvalidOperationException($"SSC script validation failed for '{scriptPath}'; missing required token '{snippet}'.");
                }
            }
            return Task.FromResult(true);
        });

        var screenshots = new List<string>();
        var primaryScreenshots = new List<string>();
        var executionCandidates = finalScenes
            .Select(scene =>
            {
                var renderSceneCode = ResolveWeeklyRenderSceneCodeFromFinalSceneCode(scene.SceneCode);
                if (!dynamicFramingScenesByCode.TryGetValue(renderSceneCode, out var propagatedScene))
                    throw new InvalidOperationException($"SSC propagation failed for {scene.SceneCode}: dynamic framing metadata was not propagated.");
                ValidateWeeklySscPropagationScene(scene.SceneCode, propagatedScene);
                return new
                {
                    SceneCode = scene.SceneCode,
                    ScriptPath = scene.ScriptPath,
                    ScreenshotDirectory = scenesDirectory,
                    ScreenshotPath = scene.ScreenshotPath,
                    Objects = propagatedScene.TargetObjects,
                    ResolvedObjects = propagatedScene.ResolvedObjects,
                    PrimaryObject = propagatedScene.PrimaryObject,
                    CameraTargetObjects = propagatedScene.CameraTargetObjects,
                    RequiredLabels = propagatedScene.LabelObjects,
                    CameraAzimuth = propagatedScene.CameraAzimuth,
                    CameraAltitude = propagatedScene.CameraAltitude,
                    Fov = propagatedScene.Fov
                };
            })
            .ToList();

        var orderedScreenshotExecutionQueue = executionCandidates
            .OrderBy(item => item.SceneCode.Equals("hero_western_grouping_scene", StringComparison.OrdinalIgnoreCase) ? 0
                : item.SceneCode.Equals("best_night_wide_scene", StringComparison.OrdinalIgnoreCase) ? 1
                : 2)
            .ThenBy(item => item.SceneCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var generatedScriptsByPath = generatedScripts
            .GroupBy(x => x.ScriptPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().ScriptContent, StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedScreenshotExecutionQueue)
        {
            if (!generatedScriptsByPath.ContainsKey(item.ScriptPath))
            {
                app.Logger.LogError("FRAME_SCRIPT_PATH_NOT_GENERATED requestedScriptPath={RequestedScriptPath} availableScriptPaths={AvailableScriptPaths}", item.ScriptPath, string.Join(",", generatedScriptsByPath.Keys));
                throw new InvalidOperationException($"Frame SSC script path was not generated: {item.ScriptPath}. Available script paths: {string.Join(", ", generatedScriptsByPath.Keys)}");
            }

            var scriptContent = await File.ReadAllTextAsync(item.ScriptPath, ct);
            var screenshotCommandMatch = Regex.Match(scriptContent, @"core\.screenshot\([^\)]*\);");
            app.Logger.LogInformation("FRAME_SCREENSHOT_COMMAND_PATH sceneCode={SceneCode} command={Command}", item.SceneCode, screenshotCommandMatch.Success ? screenshotCommandMatch.Value : "MISSING");
            if (!scriptContent.Contains("core.quitStellarium();", StringComparison.Ordinal))
                throw new InvalidOperationException($"Generated SSC script missing core.quitStellarium();: {item.ScriptPath}");

            var timeoutSeconds = 180;
            var executablePath = Environment.GetEnvironmentVariable("STELLARIUM_PATH")
                ?? Environment.GetEnvironmentVariable("StellariumPath")
                ?? "stellarium";
            var scriptPathForArgs = item.ScriptPath.Replace("\\", "/");
            var arguments = $"--startup-script \"{scriptPathForArgs}\"";

            app.Logger.LogInformation("USING_SHARED_STELLARIUM_EXECUTOR_FOR_WEEKLY_SKYFORECAST");
            app.Logger.LogInformation("sceneCode={SceneCode}", item.SceneCode);
            app.Logger.LogInformation("SSC_CAPTURE_QUEUE_DYNAMIC_METADATA sceneCode={SceneCode} objects={Objects} primaryObject={PrimaryObject} requiredLabels={RequiredLabels} cameraAzimuth={CameraAzimuth} cameraAltitude={CameraAltitude} fov={Fov}", item.SceneCode, string.Join(",", item.Objects), item.PrimaryObject, string.Join(",", item.RequiredLabels), item.CameraAzimuth, item.CameraAltitude, item.Fov);
            app.Logger.LogInformation("scriptPath={ScriptPath}", item.ScriptPath);
            app.Logger.LogInformation("screenshotPath={ScreenshotPath}", item.ScreenshotPath);
            app.Logger.LogInformation("exePath={ExePath}", executablePath);
            app.Logger.LogInformation("arguments={Arguments}", arguments);
            var frameScreenshotTargetPath = item.ScreenshotPath;
            app.Logger.LogInformation("FRAME_SCREENSHOT_TARGET_RESOLVED sceneCode={SceneCode} targetPath={TargetPath}", item.SceneCode, frameScreenshotTargetPath);
            var frameScreenshotDirectory = Path.GetDirectoryName(frameScreenshotTargetPath) ?? scenesDirectory;
            Directory.CreateDirectory(frameScreenshotDirectory);
            app.Logger.LogInformation("FRAME_SCREENSHOT_DIRECTORY_CREATED sceneCode={SceneCode} directoryPath={DirectoryPath}", item.SceneCode, frameScreenshotDirectory);
            var flatTempCaptureDirectory = Path.Combine(scenesDirectory, "__frames_temp__");
            Directory.CreateDirectory(flatTempCaptureDirectory);
            var flatTempCapturePath = Path.Combine(flatTempCaptureDirectory, Path.GetFileName(frameScreenshotTargetPath));

            await sharedStellariumExecutor.ExecuteAsync(
                workingDirectoryRoot: root,
                scriptPath: item.ScriptPath,
                expectedScreenshotPath: frameScreenshotTargetPath,
                timeoutSeconds: timeoutSeconds,
                cancellationToken: ct);

            var screenshotExistsAfterCapture = File.Exists(frameScreenshotTargetPath) && new FileInfo(frameScreenshotTargetPath).Length > 0;
            app.Logger.LogInformation("FRAME_SCREENSHOT_FILE_EXISTS_AFTER_CAPTURE sceneCode={SceneCode} exists={Exists}", item.SceneCode, screenshotExistsAfterCapture);
            var movedIfRequired = false;
            if (!screenshotExistsAfterCapture && File.Exists(flatTempCapturePath) && new FileInfo(flatTempCapturePath).Length > 0)
            {
                Directory.CreateDirectory(frameScreenshotDirectory);
                File.Copy(flatTempCapturePath, frameScreenshotTargetPath, overwrite: true);
                movedIfRequired = true;
                screenshotExistsAfterCapture = File.Exists(frameScreenshotTargetPath) && new FileInfo(frameScreenshotTargetPath).Length > 0;
            }
            app.Logger.LogInformation("FRAME_SCREENSHOT_MOVED_IF_REQUIRED sceneCode={SceneCode} moved={Moved} tempPath={TempPath} targetPath={TargetPath}", item.SceneCode, movedIfRequired, flatTempCapturePath, frameScreenshotTargetPath);
            if (!File.Exists(frameScreenshotTargetPath))
            {
                app.Logger.LogError("FRAME_SCREENSHOT_CAPTURE_FAILED sceneCode={SceneCode} screenshotPath={ScreenshotPath}", item.SceneCode, frameScreenshotTargetPath);
                throw new InvalidOperationException($"Expected screenshot was not generated: {frameScreenshotTargetPath}");
            }

            screenshots.Add(frameScreenshotTargetPath);
            app.Logger.LogInformation("FRAME_SCREENSHOT_CAPTURED sceneCode={SceneCode} outputImageName={OutputImageName}", item.SceneCode, Path.GetFileName(item.ScreenshotPath));
        }
        foreach (var plan in allFramePlans)
        {
            var balanced = plan.FramePlans.FirstOrDefault(x => x.FrameType == CinematicFrameType.BalancedStoryFrame);
            if (balanced is null) continue;
            var source = Path.Combine(scenesDirectory, plan.RenderSceneCode, balanced.OutputImageName);
            var target = Path.Combine(scenesDirectory, $"{plan.RenderSceneCode}.png");
            if (File.Exists(source))
            {
                File.Copy(source, target, overwrite: true);
                primaryScreenshots.Add(target);
            }
        }
        app.Logger.LogInformation("CINEMATIC_FRAME_PLANNER_COMPLETE generatedFramePlans={GeneratedFramePlans}", allFramePlans.Sum(x => x.FramePlans.Count));

        var warnings = weeklySkyfieldContext.Warnings.Concat(compositionPackage.Errors).Distinct().ToList();
        warnings.Add("primaryScreenshots are compatibility-only; frameScreenshots are the production image source.");
        if (screenshots.Count < finalScenes.Count)
        {
            warnings.Add($"Only {screenshots.Count} screenshots were detected out of {finalScenes.Count} planned Stellarium shots.");
        }

        var narrationManifestPath = Path.Combine(manifestsDirectory, "weekly-scenes-manifest.json");
        var framePlansBySceneCode = allFramePlans
            .ToDictionary(x => x.RenderSceneCode, x => x.FramePlans, StringComparer.OrdinalIgnoreCase);
        var sscManifestEntries = finalScenes.Select(scene =>
        {
            var screenshotPath = scene.ScreenshotPath;
            var renderSceneCode = ResolveWeeklyRenderSceneCodeFromFinalSceneCode(scene.SceneCode);
            if (!dynamicFramingScenesByCode.TryGetValue(renderSceneCode, out var propagatedScene))
                throw new InvalidOperationException($"SSC propagation failed for {scene.SceneCode}: dynamic framing metadata was not propagated.");
            ValidateWeeklySscPropagationScene(scene.SceneCode, propagatedScene);
            var objects = propagatedScene.TargetObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var resolvedObjects = propagatedScene.ResolvedObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var cameraTargetObjects = propagatedScene.CameraTargetObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var visualAnchorObjects = propagatedScene.VisualAnchorObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var requiredLabels = propagatedScene.LabelObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            return new
            {
                sceneCode = scene.SceneCode,
                sceneType = "FinalStellariumScene",
                objects,
                targetObjects = objects,
                resolvedObjects,
                primaryObject = NormalizeWeeklyObjectCode(propagatedScene.PrimaryObject) ?? propagatedScene.PrimaryObject,
                cameraTargetObjects,
                visualAnchorObjects,
                requiredLabels,
                cameraAzimuth = propagatedScene.CameraAzimuth,
                cameraAltitude = propagatedScene.CameraAltitude,
                fov = propagatedScene.Fov,
                framingMode = propagatedScene.FramingMode,
                includeHorizon = propagatedScene.IncludeHorizon,
                sourceSceneCodes = scene.SourceSceneCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                sscPath = scene.ScriptPath,
                screenshotPath,
                screenshotExists = File.Exists(screenshotPath) && new FileInfo(screenshotPath).Length > 10 * 1024,
                expectedObjectLabels = requiredLabels.Select(ToWeeklyObjectDisplayName).ToArray(),
                dynamicFramingPlanPath = weeklyDynamicFramingPlanPath
            };
        }).ToList();
        var sscPropagationValidationReport = BuildWeeklySscPropagationValidationReport(finalScenes, dynamicFramingScenesByCode, weeklyDynamicFramingPlanPath);
        await File.WriteAllTextAsync(sscPropagationValidationReportPath, JsonSerializer.Serialize(sscPropagationValidationReport, new JsonSerializerOptions { WriteIndented = true }), ct);
        var sscCameraLockValidationReport = BuildWeeklySscCameraLockValidationReport(finalScenes, dynamicFramingScenesByCode);
        await File.WriteAllTextAsync(sscCameraLockValidationReportPath, JsonSerializer.Serialize(sscCameraLockValidationReport, new JsonSerializerOptions { WriteIndented = true }), ct);
        await File.WriteAllTextAsync(sscSceneManifestPath, JsonSerializer.Serialize(new
        {
            pipelineRunId,
            weeklyFocusObjectPlanPath,
            weeklyStellariumSceneRequirementsPath,
            generatedAtUtc = DateTime.UtcNow,
            stellariumScenes = sscManifestEntries,
            groupingSplitRequired = multiObjectSceneResolutionReports.Any(x => x.GroupingSplitRequired),
            groupingSingleFrameAvailable = !multiObjectSceneResolutionReports.Any(x => x.GroupingSplitRequired),
            groupingSplitScenes = multiObjectSceneResolutionReports.SelectMany(x => x.SplitScenes ?? Array.Empty<WeeklyMultiObjectSplitSceneManifestEntry>()).ToList(),
            multiObjectResolutionPassed = multiObjectSceneResolutionReports.Count == 0 || multiObjectSceneResolutionReports.All(x => x.MultiObjectResolutionPassed),
            visualCoveragePassed = multiObjectSceneResolutionReports.Count == 0 || multiObjectSceneResolutionReports.All(x => x.AllObjectsVisuallySupported),
            multiObjectSceneResolutionPassed = multiObjectSceneResolutionReports.Count == 0 || multiObjectSceneResolutionReports.All(x => x.MultiObjectResolutionPassed),
            multiObjectScenesRequested = multiObjectSceneResolutionReports.Count,
            multiObjectScenesResolved = multiObjectSceneResolutionReports.Count(x => x.MultiObjectResolutionPassed),
            multiObjectScenesFailed = multiObjectSceneResolutionReports.Count(x => !x.MultiObjectResolutionPassed),
            multiObjectSceneResolutions = multiObjectSceneResolutionReports,
            scriptsGenerated = scriptPaths.Count,
            screenshotsGenerated = screenshots.Count,
            sscPropagationReady = sscPropagationValidationReport.sscPropagationReady,
            sscPropagationValidationReportPath,
            emptyObjectSceneCount = sscPropagationValidationReport.emptyObjectSceneCount,
            emptyRequiredLabelSceneCount = sscPropagationValidationReport.emptyRequiredLabelSceneCount,
            cameraTargetMismatchCount = sscPropagationValidationReport.cameraTargetMismatchCount,
            sscCameraLockReady = sscCameraLockValidationReport.sscCameraLockReady,
            sscCameraLockValidationReportPath,
            objectFirstCameraLockSceneCount = sscCameraLockValidationReport.objectFirstCameraLockSceneCount,
            altAzOnlySceneCount = sscCameraLockValidationReport.altAzOnlySceneCount,
            fallbackUsedSceneCount = sscCameraLockValidationReport.fallbackUsedSceneCount
        }, new JsonSerializerOptions { WriteIndented = true }), ct);
        if (!sscCameraLockValidationReport.sscCameraLockReady)
            throw new InvalidOperationException($"SSC camera lock validation failed: object-first camera lock was not propagated. Report: {sscCameraLockValidationReportPath}");
        if (!sscPropagationValidationReport.sscPropagationReady)
            throw new InvalidOperationException($"SSC propagation failed: dynamic framing metadata was not propagated. Report: {sscPropagationValidationReportPath}");

        var visualNarrationCoverage = BuildWeeklyVisualNarrationCoverageReport(
            weeklyFocusPlan,
            weeklyStellariumSceneRequirements,
            allFramePlans,
            screenshots,
            scriptPaths,
            sscManifestEntries.Select(x => x.sceneCode).ToList(),
            warnings,
            multiObjectSceneResolutionReports);
        if (visualNarrationCoverage.GroupingSplitRequired && File.Exists(narrationTextPath))
        {
            var narrationText = await File.ReadAllTextAsync(narrationTextPath, ct);
            narrationText = Regex.Replace(
                narrationText,
                @"Venus\s+and\s+Saturn[^\.]{0,120}\b(?:same|single)\s+(?:window|frame)[^\.]*\.",
                "Venus and Saturn highlight opposite parts of the sky this week, so watch Venus toward one horizon while Saturn appears in another direction.",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            await File.WriteAllTextAsync(narrationTextPath, narrationText, ct);
        }
        await File.WriteAllTextAsync(visualNarrationCoverageReportPath, JsonSerializer.Serialize(visualNarrationCoverage, new JsonSerializerOptions { WriteIndented = true }), ct);
        if (!visualNarrationCoverage.VisualNarrationAligned)
        {
            throw new InvalidOperationException($"Weekly visual/narration coverage is not aligned. Missing: {string.Join(",", visualNarrationCoverage.ObjectsMentionedButNotVisible.Concat(visualNarrationCoverage.MissingScenes))}");
        }

        app.Logger.LogInformation("temporal match summary: resolvedCandidates={Count}", generatedScripts.Count);
        app.Logger.LogInformation("spatial composition summary: compositionScenes={Count}", compositionPackage.Entries.Count);
        app.Logger.LogInformation("render split summary: stellariumScenes={Count}", finalScenes.Count);
        app.Logger.LogInformation("final SSC path summary: {Paths}", string.Join(",", scriptPaths));
        app.Logger.LogInformation("SSC_SCRIPT_COUNT={Count}", scriptPaths.Count);
        app.Logger.LogInformation("SCREENSHOT_COUNT={Count}", screenshots.Count);

        var executionSummary = new
        {
            plannedSceneCount = shots.Count,
            plannedStellariumSceneCount = finalScenes.Count,
            compositionFileCount = compositionPaths.Count,
            sscScriptCount = scriptPaths.Count,
            screenshotCount = screenshots.Count,
            screenshotMissingCount = Math.Max(0, finalScenes.Count - screenshots.Count)
        };

        const long minimumScreenshotBytes = 10 * 1024;
        var seenFrameHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screenshotPath in screenshots)
        {
            var info = new FileInfo(screenshotPath);
            if (!info.Exists)
                throw new InvalidOperationException($"Final image validation failed: file does not exist '{screenshotPath}'.");
            if (info.Length <= minimumScreenshotBytes)
                throw new InvalidOperationException($"Final image validation failed: file '{screenshotPath}' is too small ({info.Length} bytes).");

            using var imageStream = File.OpenRead(screenshotPath);
            var imageInfo = Image.Identify(imageStream);
            if (imageInfo is null || imageInfo.Width <= 0 || imageInfo.Height <= 0)
                throw new InvalidOperationException($"Final image validation failed: invalid image dimensions for '{screenshotPath}'.");

            using var hashStream = File.OpenRead(screenshotPath);
            var hash = Convert.ToHexString(SHA256.HashData(hashStream));
            if (seenFrameHashes.TryGetValue(hash, out var originalPath))
                throw new InvalidOperationException($"Final image validation failed: duplicate frame detected between '{originalPath}' and '{screenshotPath}'.");
            seenFrameHashes[hash] = screenshotPath;
        }

        var generatedFramePlans = ((IEnumerable<CinematicSceneFramePlan>?)allFramePlans ?? Array.Empty<CinematicSceneFramePlan>())
            .SelectMany(x => x.FramePlans ?? Array.Empty<CinematicFramePlan>())
            .ToList();
        var framePlanLookupForSelectedImageReport = generatedFramePlans
            .Where(x => !string.IsNullOrWhiteSpace(x.FrameId))
            .GroupBy(x => x.FrameId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var imageSequencePlanPath = Path.Combine(qualityDirectory, "image-sequence-plan.json");
        var imageSequenceSummaryPath = Path.Combine(qualityDirectory, "image-sequence-summary.json");
        app.Logger.LogInformation("SELECTED_IMAGE_SEQUENCE_BUILD_START pipelineRunId={PipelineRunId}", pipelineRunId);
        app.Logger.LogInformation("IMAGE_SEQUENCE_SELECTION_START pipelineRunId={PipelineRunId} frameScreenshots={FrameScreenshotCount} framePlans={FramePlanCount}", pipelineRunId, screenshots.Count, generatedFramePlans.Count);
        app.Logger.LogInformation("IMAGE_SEQUENCE_VALIDATION_START pipelineRunId={PipelineRunId} expectedImageCount={ExpectedImageCount} productionImageSource={ProductionImageSource}", pipelineRunId, 6, "frameScreenshots");
        var imageSequencePlan = BuildWeeklyImageSequencePlan(
            pipelineRunId,
            request.ContentCategoryCode,
            request.RegionId,
            request.Language,
            weekStartDate,
            allFramePlans,
            screenshots,
            primaryScreenshots,
            app.Logger);
        app.Logger.LogInformation("IMAGE_SEQUENCE_PLAN_ENRICHED path={Path} validationStatus={ValidationStatus} selectedImageCount={SelectedImageCount} totalDurationSeconds={TotalDurationSeconds} productionReady={ProductionReady} productionImageSource={ProductionImageSource}", imageSequencePlanPath, imageSequencePlan.ValidationStatus, imageSequencePlan.SelectedImageCount, imageSequencePlan.TotalDurationSeconds, imageSequencePlan.ProductionReady, imageSequencePlan.ProductionImageSource);
        var selectedImageSequenceItems = (imageSequencePlan.Sequences ?? Array.Empty<ImageSequenceItem>()).ToList();
        await File.WriteAllTextAsync(imageSequencePlanPath, JsonSerializer.Serialize(imageSequencePlan, new JsonSerializerOptions { WriteIndented = true }), ct);
        await File.WriteAllTextAsync(imageSequenceSummaryPath, JsonSerializer.Serialize(new
        {
            imageSequencePlan.PipelineRunId,
            imageSequencePlan.ContentCategoryCode,
            imageSequencePlan.RegionId,
            imageSequencePlan.Language,
            imageSequencePlan.WeekStartDate,
            productionReady = imageSequencePlan.ProductionReady,
            validationStatus = imageSequencePlan.ValidationStatus,
            selectedImageCount = imageSequencePlan.SelectedImageCount,
            totalDurationSeconds = imageSequencePlan.TotalDurationSeconds,
            estimatedImageSequenceDurationSeconds = imageSequencePlan.EstimatedDurationSeconds,
            productionImageSource = imageSequencePlan.ProductionImageSource,
            validationWarnings = imageSequencePlan.ValidationWarnings ?? Array.Empty<string>(),
            primaryScreenshotsDeprecated = imageSequencePlan.PrimaryScreenshotsDeprecated,
            imageSequencePlanPath,
            sequence = selectedImageSequenceItems.Select(x => new
            {
                x.SequenceIndex,
                x.RenderSceneCode,
                x.FrameType,
                x.ImagePath,
                x.SuggestedDurationSeconds,
                x.ImageValidation,
                x.SequenceRole,
                x.TransitionIntent,
                x.MotionIntentForFutureVideo,
                x.IsProductionSelected
            })
        }, new JsonSerializerOptions { WriteIndented = true }), ct);
        app.Logger.LogInformation("IMAGE_SEQUENCE_SUMMARY_ENRICHED path={Path} validationStatus={ValidationStatus} productionReady={ProductionReady} validationWarnings={ValidationWarnings}", imageSequenceSummaryPath, imageSequencePlan.ValidationStatus, imageSequencePlan.ProductionReady, string.Join("|", imageSequencePlan.ValidationWarnings ?? Array.Empty<string>()));
        var imageSequenceProductionValidationReportPath = Path.Combine(renderDirectory, "image-sequence-production-validation-report.json");
        var selectedStellariumImageSequenceReportPath = ResolveSelectedImageSequenceReportPath(null, root);
        var imageSequenceDurationDeltaSeconds = imageSequencePlan.TotalDurationSeconds - imageSequencePlan.ExpectedImageCount * 5;
        var imageSequenceExpectedDurationSeconds = imageSequencePlan.ExpectedImageCount * 5;
        var imageSequenceDurationToleranceSeconds = imageSequenceExpectedDurationSeconds <= 30 ? 1d : Math.Max(1d, imageSequenceExpectedDurationSeconds * 0.03d);
        await File.WriteAllTextAsync(imageSequenceProductionValidationReportPath, JsonSerializer.Serialize(new
        {
            imageSequenceValidationReady = imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase),
            validationStatus = imageSequencePlan.ValidationStatus,
            selectedImageCount = imageSequencePlan.SelectedImageCount,
            expectedImageCount = imageSequencePlan.ExpectedImageCount,
            totalDurationSeconds = imageSequencePlan.TotalDurationSeconds,
            expectedDurationSeconds = imageSequenceExpectedDurationSeconds,
            durationDeltaSeconds = imageSequenceDurationDeltaSeconds,
            withinDurationTolerance = Math.Abs(imageSequenceDurationDeltaSeconds) <= imageSequenceDurationToleranceSeconds,
            duplicateImagesDetected = imageSequencePlan.DuplicateImagesDetected,
            productionImageSource = ResolveSelectedImageSource(imageSequencePlan.ProductionImageSource, null),
            warnings = imageSequencePlan.ValidationWarnings ?? Array.Empty<string>(),
            errors = imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? Array.Empty<string>() : imageSequencePlan.ValidationWarnings ?? Array.Empty<string>()
        }, new JsonSerializerOptions { WriteIndented = true }), ct);
        await WriteSelectedImageSequenceReportAsync(
            selectedStellariumImageSequenceReportPath,
            imageSequencePlan,
            selectedImageSequenceItems,
            framePlanLookupForSelectedImageReport,
            root,
            imageSequenceExpectedDurationSeconds,
            imageSequenceDurationDeltaSeconds,
            imageSequenceDurationToleranceSeconds,
            app.Logger,
            ct);
        app.Logger.LogInformation("IMAGE_SEQUENCE_PRODUCTION_VALIDATION_REPORT_WRITTEN path={Path} selectedReportPath={SelectedReportPath}", imageSequenceProductionValidationReportPath, selectedStellariumImageSequenceReportPath);
        app.Logger.LogInformation("IMAGE_SEQUENCE_PLAN_WRITTEN path={Path} summaryPath={SummaryPath} selectedImageCount={SelectedImageCount} estimatedDurationSeconds={EstimatedDurationSeconds}", imageSequencePlanPath, imageSequenceSummaryPath, imageSequencePlan.TotalImages, imageSequencePlan.EstimatedDurationSeconds);
        app.Logger.LogInformation("IMAGE_SEQUENCE_VALIDATION_COMPLETE selectedImageCount={SelectedImageCount} estimatedDurationSeconds={EstimatedDurationSeconds}", imageSequencePlan.TotalImages, imageSequencePlan.EstimatedDurationSeconds);
        app.Logger.LogInformation("SELECTED_IMAGE_SEQUENCE_BUILD_COMPLETE pipelineRunId={PipelineRunId} selectedImageCount={SelectedImageCount} validationStatus={ValidationStatus}", pipelineRunId, imageSequencePlan.TotalImages, imageSequencePlan.ValidationStatus);

        var finalScriptPathSet = scriptPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var producedRenderSceneDescriptors = finalRenderSceneDescriptors
            .Where(x => x.ProducedSscScript && finalScriptPathSet.Contains(x.ScriptPath))
            .ToList();

        foreach (var descriptor in producedRenderSceneDescriptors)
        {
            app.Logger.LogInformation(
                "FINAL_FALLBACK_VALIDATION_SOURCE renderSceneCode={RenderSceneCode} frameId={FrameId} fallbackUsed={FallbackUsed} fallbackReason={FallbackReason} geometrySource={GeometrySource} scriptPath={ScriptPath} imagePath={ImagePath}",
                descriptor.RenderSceneCode,
                descriptor.FrameId,
                descriptor.FallbackUsed,
                descriptor.FallbackReason,
                descriptor.GeometrySource,
                descriptor.ScriptPath,
                descriptor.ImagePath);
        }

        var fallbackFramePlans = generatedFramePlans
            .Where(x => x.FrameGenerationUsedFallback)
            .Select(x => new { SceneCode = x.RenderSceneCode, x.FrameId, FallbackReason = x.FallbackReason ?? string.Empty })
            .ToList();
        var fallbackDescriptors = producedRenderSceneDescriptors
            .Where(x => x.FallbackUsed)
            .Select(x => new { SceneCode = x.RenderSceneCode, x.FrameId, FallbackReason = x.FallbackReason ?? string.Empty })
            .ToList();
        var offendingFallbackFrames = fallbackFramePlans.Concat(fallbackDescriptors)
            .GroupBy(x => $"{x.SceneCode}|{x.FrameId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.SceneCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FrameId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fallbackUsed = fallbackFramePlans.Count > 0 || fallbackDescriptors.Count > 0;

        var allSelectedImagesValid = selectedImageSequenceItems.All(x => x.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase));
        app.Logger.LogInformation("IMAGE_SEQUENCE_FINAL_VALIDATION ScenePlan={ScenePlan} Timeline={Timeline} Composition={Composition} RenderScenes={RenderScenes} FramePlans={FramePlans} SscScripts={SscScripts} FrameScreenshots={FrameScreenshots} SelectedImages={SelectedImages} ImageSequenceDuration={ImageSequenceDuration} AllSelectedImagesValid={AllSelectedImagesValid} DuplicateImages={DuplicateImages} ProductionImageSource={ProductionImageSource} fallbackUsed={FallbackUsed}", weeklyScenePlan.ScenePlans.Count, shots.Count, compositionPaths.Count, allFramePlans.Count, generatedFramePlans.Count, scriptPaths.Count, screenshots.Count, imageSequencePlan.TotalImages, imageSequencePlan.EstimatedDurationSeconds, allSelectedImagesValid, imageSequencePlan.DuplicateImagesDetected, imageSequencePlan.ProductionImageSource, fallbackUsed);

        var finalImageSequenceWithinDurationTolerance = Math.Abs(imageSequencePlan.TotalDurationSeconds - imageSequenceExpectedDurationSeconds) <= imageSequenceDurationToleranceSeconds;
        if (!imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) || !allSelectedImagesValid || imageSequencePlan.DuplicateImagesDetected)
        {
            var offendingFramesText = offendingFallbackFrames.Count == 0
                ? string.Empty
                : Environment.NewLine
                    + "OffendingFrames:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        offendingFallbackFrames.Select(x => string.Join(
                            Environment.NewLine,
                            $"- sceneCode={x.SceneCode}",
                            $"- frameId={x.FrameId}",
                            $"- fallbackReason={x.FallbackReason}")));
            var invalidImagesText = selectedImageSequenceItems.Any(x => !x.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase))
                ? Environment.NewLine
                    + "InvalidImages:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        selectedImageSequenceItems
                            .Where(x => !x.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase))
                            .Select(x => $"- sequenceIndex={x.SequenceIndex} sceneCode={x.RenderSceneCode} frameId={x.FrameId} imagePath={x.ImagePath} validationStatus={x.ValidationStatus} warnings={string.Join("|", x.ValidationWarnings ?? Array.Empty<string>())}"))
                : string.Empty;
            throw new InvalidOperationException($"Final validation failed: ScenePlan={weeklyScenePlan.ScenePlans.Count}, Timeline={shots.Count}, Composition={compositionPaths.Count}, RenderScenes={allFramePlans.Count}, FramePlans={generatedFramePlans.Count}, SscScripts={scriptPaths.Count}, FrameScreenshots={screenshots.Count}, SelectedImages={imageSequencePlan.TotalImages}, ImageSequenceDuration={imageSequencePlan.EstimatedDurationSeconds}, AllSelectedImagesValid={allSelectedImagesValid}, DuplicateImages={imageSequencePlan.DuplicateImagesDetected}, ProductionImageSource={imageSequencePlan.ProductionImageSource}, fallbackUsed={(fallbackUsed ? "true" : "false")}{invalidImagesText}{offendingFramesText}");
        }

        if (!finalImageSequenceWithinDurationTolerance)
        {
            warnings.Add($"Selected image sequence duration {imageSequencePlan.TotalDurationSeconds}s differs from expected {imageSequenceExpectedDurationSeconds}s by {Math.Abs(imageSequenceDurationDeltaSeconds)}s; tolerated as post-asset warning.");
        }
        app.Logger.LogInformation("IMAGE_PIPELINE_LOCKED_PRODUCTION_READY pipelineRunId={PipelineRunId} selectedImageCount={SelectedImageCount} estimatedDurationSeconds={EstimatedDurationSeconds} productionImageSource={ProductionImageSource}", pipelineRunId, imageSequencePlan.TotalImages, imageSequencePlan.EstimatedDurationSeconds, imageSequencePlan.ProductionImageSource);

        var segmentDiversification = await ExecuteOrchestrationStageAsync("Diversifying weekly episode segments", stageCt =>
            segmentDiversificationService.DiversifyAndPersistAsync(
                segmentClassification.Plan,
                episodeArchitecture,
                weeklyScenePlan,
                imageSequencePlan,
                allFramePlans,
                weeklySkyfieldContext,
                root,
                stageCt));

        var visualAssetPlanning = await ExecuteOrchestrationStageAsync("Planning weekly visual assets", stageCt =>
            visualAssetPlanningService.PlanAndPersistAsync(
                segmentDiversification.Plan,
                segmentClassification.Plan,
                episodeArchitecture,
                weeklyScenePlan,
                imageSequencePlan,
                allFramePlans,
                weeklySkyfieldContext,
                root,
                stageCt));

        var assetExpansion = await ExecuteOrchestrationStageAsync("Expanding weekly segment asset packages", stageCt =>
            assetExpansionService.ExpandAndPersistAsync(
                episodeArchitecture,
                segmentClassification.Plan,
                segmentDiversification.Plan,
                visualAssetPlanning.Plan,
                weeklyScenePlan,
                imageSequencePlan,
                allFramePlans,
                weeklySkyfieldContext,
                null,
                root,
                stageCt));

        var assetExpansionOptions = assetExpansionOptionsAccessor.Value;
        var expandedStellariumExecution = await ExecuteOrchestrationStageAsync("Executing expanded Stellarium scene requirements", stageCt =>
            ExecuteExpandedStellariumScenesAsync(
                root,
                assetExpansion.Plan,
                assetExpansion.RenderScenePlanPath,
                assetExpansionOptions,
                weeklySkyfieldContext,
                sscIntelligenceService,
                sceneIntentResolver,
                sharedStellariumExecutor,
                temporalResolver,
                request.ScheduledUtc.UtcDateTime,
                app.Logger,
                stageCt),
            TimeSpan.FromSeconds(Math.Max(1, assetExpansionOptions.ExpandedExecutionTimeoutSeconds)));
        warnings.AddRange(expandedStellariumExecution.Warnings);
        warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var aiCinematicOptions = aiCinematicOptionsAccessor.Value;
        var aiCinematicAssets = await ExecuteOrchestrationStageAsync("Generating planned AI cinematic still assets", stageCt =>
            aiCinematicAssetGenerationService.GenerateAndPersistAsync(
                visualAssetPlanning.Plan,
                visualAssetPlanning.BalanceReport,
                segmentDiversification.Plan,
                episodeArchitecture,
                weeklySkyfieldContext,
                root,
                stageCt,
                request.ContinueOnFailure && aiCinematicOptions.ContinueOnFailure),
            TimeSpan.FromSeconds(Math.Max(1, aiCinematicOptions.EffectiveMaxGenerationSeconds)));

        var visualBalanceHealthyAfterAICinematicAssets = visualAssetPlanning.BalanceReport.VisualBalanceHealthy;

        var nullCollectionsDetected = 0;
        IReadOnlyList<T> NormalizeEndpointAssetCollection<T>(string provider, IReadOnlyList<T>? source)
        {
            if (source is not null) return source;
            nullCollectionsDetected++;
            app.Logger.LogWarning("NULL_ASSET_COLLECTION_NORMALIZED provider={Provider}", provider);
            return [];
        }

        var aiCinematicImagePaths = aiCinematicAssets.AICinematicImagePaths?.Count > 0
            ? NormalizeEndpointAssetCollection("AICinematic", aiCinematicAssets.AICinematicImagePaths)
            : await CollectProductionReadyAICinematicImagePathsAsync(aiCinematicAssets.ResultsPath, app.Logger, ct);
        aiCinematicImagePaths = NormalizeEndpointAssetCollection("AICinematic", aiCinematicImagePaths);
        var frameScreenshots = NormalizeEndpointAssetCollection("FrameScreenshots", screenshots);
        var expandedFrameScreenshots = NormalizeEndpointAssetCollection("ExpandedFrameScreenshots", expandedStellariumExecution.ExpandedFrameScreenshots);
        if (aiCinematicImagePaths.Count == 0)
            warnings.Add("AI cinematic assets missing; continued with non-AI visual assets.");
        warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var aiCinematicGenerationReportPath = await WriteAICinematicGenerationReportAsync(root, aiCinematicAssets, aiCinematicImagePaths, ct);
        app.Logger.LogInformation("AI_CINEMATIC_GENERATION_REPORT_WRITTEN path={Path}", aiCinematicGenerationReportPath);
        app.Logger.LogInformation("ASSET_PROVIDER_RESULT provider=AICinematic assetCount={AssetCount} isNull=False", aiCinematicImagePaths.Count);
        var stellariumProductionFrameScreenshots = frameScreenshots.Concat(expandedFrameScreenshots).ToList();
        var allProductionImageAssets = BuildAllProductionImageAssets(
            frameScreenshots,
            expandedFrameScreenshots,
            aiCinematicImagePaths,
            [],
            [],
            app.Logger);

        var assetRealization = await ExecuteOrchestrationStageAsync("Realizing weekly production asset manifest", stageCt =>
            assetRealizationService.RealizeAndPersistAsync(
                new WeeklyAssetRealizationInput(
                    pipelineRunId,
                    request.RegionId,
                    request.Language,
                    weekStartDate,
                    weekEndDate,
                    root,
                    storyBeatsPath,
                    narrationTextPath,
                    episodeArchitecture.LongFormPlan,
                    episodeArchitecture.ShortFormPlan,
                    segmentClassification.Plan,
                    visualAssetPlanning.Plan,
                    visualAssetPlanning.PlanPath,
                    frameScreenshots,
                    expandedFrameScreenshots,
                    aiCinematicImagePaths,
                    allProductionImageAssets,
                    weeklyForecast),
                stageCt));
        warnings.AddRange(assetRealization.RealizationReport.Warnings);
        warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var failedAssetPaths = new HashSet<string>(assetRealization.FailedAssetPaths ?? [], StringComparer.OrdinalIgnoreCase);
        var nasaImagePaths = NormalizeEndpointAssetCollection("NASA", assetRealization.NasaImagePaths);
        var jwstImagePaths = NormalizeEndpointAssetCollection("JWST", assetRealization.JwstImagePaths);
        var motionGraphicPaths = NormalizeEndpointAssetCollection("MotionGraphics", assetRealization.MotionGraphicPaths);
        var educationalOverlayPaths = NormalizeEndpointAssetCollection("EducationalOverlay", assetRealization.EducationalOverlayPaths);
        var realizedAllProductionImageAssets = allProductionImageAssets
            .Concat(nasaImagePaths)
            .Concat(jwstImagePaths)
            .Concat(motionGraphicPaths)
            .Concat(educationalOverlayPaths)
            .Where(path => !failedAssetPaths.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assetProviderSummary = new WeeklySkyForecastAssetProviderSummary(
            frameScreenshots.Count,
            expandedFrameScreenshots.Count,
            nasaImagePaths.Count,
            jwstImagePaths.Count,
            motionGraphicPaths.Count,
            educationalOverlayPaths.Count,
            aiCinematicImagePaths.Count,
            nullCollectionsDetected >= 0);

        var narrationVisualTimeline = await ExecuteOrchestrationStageAsync("Composing narration visual timeline", stageCt =>
            narrationVisualTimelineComposer.ComposeAndPersistAsync(
                new WeeklyNarrationVisualTimelineInput(
                    root,
                    assetRealization.WeeklyProductionAssetManifestPath,
                    assetRealization.WeeklyAssetRealizationReportPath,
                    assetRealization.WeeklyVideoReadinessReportPath,
                    episodeArchitecture.WeeklyEpisodePlanPath,
                    episodeArchitecture.WeeklyLongformPlanPath,
                    episodeArchitecture.WeeklyShortformPlanPath,
                    storyBeatsPath,
                    assetRealization.Manifest,
                    assetRealization.RealizationReport,
                    assetRealization.VideoReadinessReport,
                    episodeArchitecture.LongFormPlan,
                    episodeArchitecture.ShortFormPlan,
                    weeklySkyfieldContext.GeneratedNarrationPackage,
                    realizedAllProductionImageAssets,
                    frameScreenshots,
                    expandedFrameScreenshots,
                    aiCinematicImagePaths),
                stageCt));

        await File.WriteAllTextAsync(narrationManifestPath, JsonSerializer.Serialize(new
        {
            pipelineRunId,
            workingDirectoryRoot = root,
            skyfieldResponsePath,
            narrationArtifacts = new
            {
                storyBeatsPath,
                narrationPlanPath,
                narrationTextPath,
                visualRequirementsPath
            },
            scenePlanPath,
            shotTimelinePath,
            compositionFiles = compositionPaths,
            sscScripts = sscManifestEntries,
            screenshotOutputs = screenshots,
            expandedFrameScreenshots,
            allProductionFrameScreenshots = stellariumProductionFrameScreenshots,
            aiCinematicImagePaths,
            aiCinematicGenerationReportPath,
            nasaImagePaths,
            jwstImagePaths,
            motionGraphicPaths,
            educationalOverlayPaths,
            allProductionImageAssets = realizedAllProductionImageAssets,
            assetProviderSummary,
            imageSequencePlanPath,
            episodeArchitecture = new
            {
                weeklyEpisodePlanPath = episodeArchitecture.WeeklyEpisodePlanPath,
                weeklyLongformPlanPath = episodeArchitecture.WeeklyLongformPlanPath,
                weeklyShortformPlanPath = episodeArchitecture.WeeklyShortformPlanPath,
                longformTargetDurationSeconds = episodeArchitecture.LongFormPlan.TotalTargetDurationSeconds,
                shortformTargetDurationSeconds = episodeArchitecture.ShortFormPlan.TotalTargetDurationSeconds,
                episodeArchitectureReady = episodeArchitecture.EpisodeArchitectureReady
            },
            segmentClassification = new
            {
                weeklySegmentClassificationPlanPath = segmentClassification.Path,
                segmentClassificationReady = segmentClassification.Plan.SegmentClassificationReady,
                classifiedLongformSegmentCount = segmentClassification.Plan.ClassifiedLongformSegmentCount,
                classifiedShortformSegmentCount = segmentClassification.Plan.ClassifiedShortformSegmentCount,
                heroEventSegmentType = segmentClassification.Plan.HeroEventSegmentType,
                heroEventObjects = segmentClassification.Plan.HeroEventObjects
            },
            segmentDiversification = new
            {
                weeklySegmentDiversificationPlanPath = segmentDiversification.Path,
                segmentDiversificationReady = segmentDiversification.Plan.SegmentDiversificationReady,
                diversifiedLongformSegmentCount = segmentDiversification.Plan.DiversifiedLongformSegmentCount,
                diversifiedShortformSegmentCount = segmentDiversification.Plan.DiversifiedShortformSegmentCount,
                assetExpansionRequired = segmentDiversification.Plan.AssetExpansionRequired || aiCinematicAssets.MissingRequiredAICinematicAssetCount > 0,
                highestRetentionRiskScore = segmentDiversification.Plan.HighestRetentionRiskScore,
                highestRepetitionRiskScore = segmentDiversification.Plan.HighestRepetitionRiskScore
            },
            visualAssetPlanning = new
            {
                weeklyVisualAssetPlanPath = visualAssetPlanning.PlanPath,
                weeklyVisualBalanceReportPath = visualAssetPlanning.BalanceReportPath,
                visualAssetPlanningReady = visualAssetPlanning.Plan.VisualAssetPlanningReady,
                plannedVisualAssetCount = visualAssetPlanning.Plan.PlannedVisualAssetCount,
                plannedMotionGraphicsCount = visualAssetPlanning.Plan.PlannedMotionGraphicsCount,
                plannedEducationalOverlayCount = visualAssetPlanning.Plan.PlannedEducationalOverlayCount,
                plannedAICinematicCount = visualAssetPlanning.Plan.PlannedAICinematicCount,
                plannedNASAAssetCount = visualAssetPlanning.Plan.PlannedNASAAssetCount,
                plannedJWSTAssetCount = visualAssetPlanning.Plan.PlannedJWSTAssetCount,
                visualBalanceHealthy = visualBalanceHealthyAfterAICinematicAssets
            },
            aiCinematicAssets = new
            {
                aiCinematicAssetPlanPath = aiCinematicAssets.PlanPath,
                aiCinematicAssetResultsPath = aiCinematicAssets.ResultsPath,
                aiCinematicAssetRealizationReportPath = aiCinematicAssets.RealizationReportPath,
                aiCinematicAssetGenerationReady = aiCinematicAssets.GenerationReady,
                plannedAICinematicAssetCount = aiCinematicAssets.PlannedCount,
                selectedAICinematicAssetCount = aiCinematicAssets.SelectedCount,
                generatedAICinematicAssetCount = aiCinematicAssets.GeneratedCount,
                deferredAICinematicAssetCount = aiCinematicAssets.DeferredCount,
                failedAICinematicAssetCount = aiCinematicAssets.FailedCount,
                skippedExistingValidAICinematicAssetCount = aiCinematicAssets.SkippedExistingValidCount,
                productionReadyAICinematicAssetCount = aiCinematicAssets.ProductionReadyCount,
                aiCinematicGenerationPartial = aiCinematicAssets.Partial,
                aiCinematicMaxAssetsPerRun = aiCinematicAssets.MaxAssetsPerRun,
                aiCinematicProviderConfigured = aiCinematicAssets.ProviderConfigured,
                azureImageDeploymentUsed = aiCinematicAssets.AzureImageDeploymentUsed,
                aiCinematicCandidateCount = aiCinematicAssets.AICinematicCandidateCount,
                requiredAICinematicAssetCount = aiCinematicAssets.RequiredAICinematicAssetCount,
                optionalAICinematicCandidateCount = aiCinematicAssets.OptionalAICinematicCandidateCount,
                selectedRequiredAICinematicAssetCount = aiCinematicAssets.SelectedRequiredAICinematicAssetCount,
                generatedRequiredAICinematicAssetCount = aiCinematicAssets.GeneratedRequiredAICinematicAssetCount,
                productionReadyRequiredAICinematicAssetCount = aiCinematicAssets.ProductionReadyRequiredAICinematicAssetCount,
                missingRequiredAICinematicAssetCount = aiCinematicAssets.MissingRequiredAICinematicAssetCount,
                generatedOptionalAICinematicAssetCount = aiCinematicAssets.GeneratedOptionalAICinematicAssetCount,
                deferredOptionalAICinematicAssetCount = aiCinematicAssets.DeferredOptionalAICinematicAssetCount,
                aiCinematicRequiredPackageReady = aiCinematicAssets.AICinematicRequiredPackageReady,
                aiCinematicImagePaths,
                remainingAICinematicGap = aiCinematicAssets.RemainingGap
            },
            narrationVisualTimeline = new
            {
                weeklyNarrationVisualTimelinePath = narrationVisualTimeline.WeeklyNarrationVisualTimelinePath,
                weeklyTimelineValidationReportPath = narrationVisualTimeline.WeeklyTimelineValidationReportPath,
                narrationVisualTimelineReady = narrationVisualTimeline.NarrationVisualTimelineReady,
                longformTimelineReadyForTest = narrationVisualTimeline.ValidationReport.LongformTimelineReadyForTest,
                shortformTimelineReadyForTest = narrationVisualTimeline.ValidationReport.ShortformTimelineReadyForTest,
                longformTimelineReadyForFinalVideo = narrationVisualTimeline.ValidationReport.LongformTimelineReadyForFinalVideo,
                shortformTimelineReadyForFinalVideo = narrationVisualTimeline.ValidationReport.ShortformTimelineReadyForFinalVideo,
                totalTimelineShotCount = narrationVisualTimeline.ValidationReport.TotalShotCount,
                totalTimelineDurationSeconds = narrationVisualTimeline.ValidationReport.TotalTimelineDurationSeconds,
                timelineValidationStatus = narrationVisualTimeline.ValidationReport.TimelineValidationStatus
            },
            assetRealization = new
            {
                weeklyProductionAssetManifestPath = assetRealization.WeeklyProductionAssetManifestPath,
                weeklyAssetRealizationReportPath = assetRealization.WeeklyAssetRealizationReportPath,
                weeklyVideoReadinessReportPath = assetRealization.WeeklyVideoReadinessReportPath,
                assetRealizationReady = assetRealization.AssetRealizationReady,
                totalProductionImageAssetCount = assetRealization.Manifest.TotalProductionImageAssetCount,
                stellariumBaseAssetCount = assetRealization.Manifest.StellariumBaseAssetCount,
                expandedStellariumAssetCount = assetRealization.Manifest.ExpandedStellariumAssetCount,
                aiCinematicImageCount = assetRealization.Manifest.AICinematicAssetCount,
                nasaImageCount = assetRealization.NasaImageCount,
                nasaAssetPlanPath = assetRealization.NasaAssetPlanPath,
                nasaAssetResultsPath = assetRealization.NasaAssetResultsPath,
                nasaAssetRealizationReportPath = assetRealization.NasaAssetRealizationReportPath,
                failedNASAAssetCount = assetRealization.FailedNASAAssetCount,
                plannedNASAAssetCount = assetRealization.PlannedNASAAssetCount,
                generatedNASAAssetCount = assetRealization.GeneratedNASAAssetCount,
                productionReadyNASAAssetCount = assetRealization.ProductionReadyNASAAssetCount,
                nasaImagePaths,
                nasaProviderConfigured = assetRealization.NasaProviderConfigured,
                jwstAssetPlanPath = assetRealization.JwstAssetPlanPath,
                jwstAssetResultsPath = assetRealization.JwstAssetResultsPath,
                jwstAssetRealizationReportPath = assetRealization.JwstAssetRealizationReportPath,
                plannedJWSTAssetCount = assetRealization.PlannedJWSTAssetCount,
                generatedJWSTAssetCount = assetRealization.GeneratedJWSTAssetCount,
                productionReadyJWSTAssetCount = assetRealization.ProductionReadyJWSTAssetCount,
                failedJWSTAssetCount = assetRealization.FailedJWSTAssetCount,
                jwstImagePaths,
                jwstImageCount = assetRealization.JwstImageCount,
                motionGraphicsImageCount = assetRealization.Manifest.MotionGraphicsAssetCount,
                educationalOverlayImageCount = assetRealization.Manifest.EducationalOverlayAssetCount,
                plannedMotionGraphicCount = assetRealization.PlannedMotionGraphicCount,
                generatedMotionGraphicCount = assetRealization.GeneratedMotionGraphicCount,
                productionReadyMotionGraphicCount = assetRealization.ProductionReadyMotionGraphicCount,
                motionGraphicPaths,
                plannedEducationalOverlayCount = assetRealization.PlannedEducationalOverlayCount,
                generatedEducationalOverlayCount = assetRealization.GeneratedEducationalOverlayCount,
                productionReadyEducationalOverlayCount = assetRealization.ProductionReadyEducationalOverlayCount,
                educationalOverlayPaths,
                testVideoPipelineReady = assetRealization.VideoReadinessReport.TestVideoPipelineReady,
                finalVideoPipelineReady = assetRealization.VideoReadinessReport.FinalVideoPipelineReady,
                readySegmentCountForTest = assetRealization.VideoReadinessReport.ReadySegmentCountForTest,
                readySegmentCountForFinal = assetRealization.VideoReadinessReport.ReadySegmentCountForFinal,
                notReadySegmentCount = assetRealization.VideoReadinessReport.NotReadySegments.Count,
                assetQualityReportPath = assetRealization.AssetQualityReportPath,
                assetQualityDetailsPath = assetRealization.AssetQualityDetailsPath,
                totalValidatedAssets = assetRealization.TotalValidatedAssets,
                productionReadyAssetCount = assetRealization.ProductionReadyAssetCount,
                productionWarningAssetCount = assetRealization.ProductionWarningAssetCount,
                productionFailedAssetCount = assetRealization.ProductionFailedAssetCount,
                qualityGatePassed = assetRealization.QualityGatePassed,
                failedAssetPaths = assetRealization.FailedAssetPaths
            },
            assetExpansion = new
            {
                weeklyAssetExpansionPlanPath = assetExpansion.PlanPath,
                weeklySegmentCoverageReportPath = assetExpansion.CoverageReportPath,
                weeklyExpandedRenderScenePlanPath = assetExpansion.RenderScenePlanPath,
                assetExpansionPlanningReady = assetExpansion.Plan.AssetExpansionPlanningReady,
                longformVisualPackageCount = assetExpansion.Plan.LongformVisualPackageCount,
                shortformVisualPackageCount = assetExpansion.Plan.ShortformVisualPackageCount,
                expandedRenderSceneRequirementCount = assetExpansion.Plan.ExpandedRenderSceneRequirementCount,
                uniqueAstronomySceneRequirementCount = assetExpansion.Plan.UniqueAstronomySceneRequirementCount,
                readyForVideoPlanningSegmentCount = assetExpansion.Plan.ReadyForVideoPlanningSegmentCount,
                needsAssetGenerationSegmentCount = assetExpansion.Plan.NeedsAssetGenerationSegmentCount,
                assetExpansionPlanningMode = assetExpansion.Plan.AssetExpansionPlanningMode,
                weeklyExpandedStellariumExecutionReportPath = expandedStellariumExecution.ReportPath,
                expandedStellariumExecutionReady = expandedStellariumExecution.Ready,
                expandedNightGeometryReady = expandedStellariumExecution.ExpandedNightGeometryReady,
                expandedSelectedObservationUtc = expandedStellariumExecution.ExpandedSelectedObservationUtc,
                expandedSelectedObservationLocal = expandedStellariumExecution.ExpandedSelectedObservationLocal,
                expandedSelectedSunAltitudeDeg = expandedStellariumExecution.ExpandedSelectedSunAltitudeDeg,
                expandedNightValidationStatus = expandedStellariumExecution.ExpandedNightValidationStatus,
                failedExpandedAssetReasons = expandedStellariumExecution.FailedExpandedAssetReasons,
                expandedStellariumExecutionPartial = expandedStellariumExecution.Partial,
                expandedStellariumExecutionTimedOut = expandedStellariumExecution.TimedOut,
                expandedStellariumMaxScenesPerRun = expandedStellariumExecution.MaxExpandedScenesPerRun,
                expandedStellariumMaxFramesPerScene = expandedStellariumExecution.MaxFramesPerExpandedScene,
                executedExpandedSceneCount = expandedStellariumExecution.ExecutedExpandedSceneCount,
                skippedExpandedSceneCount = expandedStellariumExecution.SkippedExpandedSceneCount,
                generatedExpandedSscScriptCount = expandedStellariumExecution.GeneratedExpandedSscScriptCount,
                generatedExpandedScreenshotCount = expandedStellariumExecution.GeneratedExpandedScreenshotCount,
                totalGeneratedSscScriptsIncludingExpanded = scriptPaths.Count + expandedStellariumExecution.GeneratedExpandedSscScriptCount,
                totalGeneratedScreenshotsIncludingExpanded = screenshots.Count + expandedStellariumExecution.GeneratedExpandedScreenshotCount,
                assetExpansionExecutionMode = expandedStellariumExecution.Mode
            },
            selectedImageCount = imageSequencePlan.TotalImages,
            estimatedImageSequenceDurationSeconds = imageSequencePlan.EstimatedDurationSeconds,
            imagePipelineProductionReady = imageSequencePlan.ProductionReady,
            imageSequenceValidationStatus = imageSequencePlan.ValidationStatus,
            allSelectedImagesValid,
            duplicateImagesDetected = imageSequencePlan.DuplicateImagesDetected,
            primaryScreenshotsDeprecated = imageSequencePlan.PrimaryScreenshotsDeprecated,
            productionImageSource = imageSequencePlan.ProductionImageSource,
            warnings,
            executionSummary,
            sscPropagationReady = sscPropagationValidationReport.sscPropagationReady,
            sscPropagationValidationReportPath,
            emptyObjectSceneCount = sscPropagationValidationReport.emptyObjectSceneCount,
            emptyRequiredLabelSceneCount = sscPropagationValidationReport.emptyRequiredLabelSceneCount,
            cameraTargetMismatchCount = sscPropagationValidationReport.cameraTargetMismatchCount
        }, new JsonSerializerOptions { WriteIndented = true }), ct);

        var eventPriorityScoring = await ExecuteOrchestrationStageAsync("Scoring weekly event priorities", stageCt =>
            eventPriorityScoringEngine.ScoreAndPersistAsync(
                new WeeklyEventPriorityScoringInput(
                    pipelineRunId,
                    root,
                    skyfieldResponsePath,
                    storyBeatsPath,
                    narrationManifestPath,
                    segmentClassification.Path,
                    visualAssetPlanning.PlanPath,
                    assetRealization.WeeklyProductionAssetManifestPath,
                    narrationVisualTimeline.WeeklyNarrationVisualTimelinePath,
                    weeklySkyfieldContext.EventExtractionResult ?? throw new InvalidOperationException("Missing event extraction result for priority scoring.")),
                stageCt));

        var narrationEngine = await ExecuteOrchestrationStageAsync("Generating narration engine v2 artifacts", stageCt =>
            narrationEngineV2.GenerateAndPersistAsync(
                new WeeklyNarrationEngineV2Input(
                    pipelineRunId,
                    root,
                    request.Language,
                    request.RegionName,
                    weekStartDate,
                    episodeArchitecture.WeeklyEpisodePlanPath,
                    episodeArchitecture.WeeklyLongformPlanPath,
                    episodeArchitecture.WeeklyShortformPlanPath,
                    segmentClassification.Path,
                    eventPriorityScoring.WeeklyEventPriorityReportPath,
                    eventPriorityScoring.HeroEventSelectionPath,
                    assetRealization.WeeklyProductionAssetManifestPath,
                    narrationVisualTimeline.WeeklyNarrationVisualTimelinePath,
                    storyBeatsPath,
                    episodeArchitecture.LongFormPlan,
                    episodeArchitecture.ShortFormPlan,
                    segmentClassification.Plan,
                    eventPriorityScoring.Report,
                    eventPriorityScoring.HeroEventSelection,
                    assetRealization.Manifest,
                    narrationVisualTimeline.Timeline),
                stageCt));

        var timelineComposition = await ExecuteOrchestrationStageAsync("Finalizing weekly render timeline composition", stageCt =>
            timelineCompositionEngine.ComposeAndPersistAsync(
                new WeeklyTimelineCompositionInput(
                    pipelineRunId,
                    root,
                    narrationEngine.LongformNarrationPath,
                    narrationEngine.ShortformNarrationPath,
                    narrationEngine.NarrationAssetMapPath,
                    narrationEngine.NarrationTimelineMapPath,
                    narrationEngine.EditorialReviewReportPath,
                    eventPriorityScoring.WeeklyEventPriorityReportPath,
                    eventPriorityScoring.HeroEventSelectionPath,
                    assetRealization.WeeklyProductionAssetManifestPath,
                    assetRealization.AssetQualityReportPath,
                    assetRealization.WeeklyVideoReadinessReportPath,
                    narrationEngine.LongformNarration,
                    narrationEngine.ShortformNarration,
                    narrationEngine.NarrationAssetMap,
                    narrationEngine.NarrationTimelineMap,
                    narrationEngine.EditorialReviewReport,
                    eventPriorityScoring.Report,
                    eventPriorityScoring.HeroEventSelection,
                    assetRealization.Manifest,
                    realizedAllProductionImageAssets),
                stageCt));

        var ffmpegRendererPreparation = await ExecuteOrchestrationStageAsync("Preparing weekly FFmpeg renderer contract", stageCt =>
            ffmpegRenderPreparationEngine.PrepareAndPersistAsync(
                new WeeklyFfmpegRenderPreparationInput(
                    pipelineRunId,
                    root,
                    weekStartDate,
                    request.RegionId,
                    request.Language,
                    timelineComposition.FinalRenderTimelinePath,
                    timelineComposition.FinalRenderShotListPath,
                    timelineComposition.TimelineTransitionPlanPath,
                    timelineComposition.SegmentTimelineReportPath,
                    timelineComposition.RetentionMarkerTimelinePath,
                    assetRealization.WeeklyProductionAssetManifestPath,
                    assetRealization.AssetQualityReportPath,
                    narrationEngine.LongformNarrationPath,
                    narrationEngine.ShortformNarrationPath),
                stageCt));
        await EnrichWeeklyRenderInputManifestWithSscPropagationAsync(
            ffmpegRendererPreparation.RenderInputManifestPath,
            weeklyDynamicFramingPlanPath,
            sscSceneManifestPath,
            sscPropagationValidationReportPath,
            sscManifestEntries,
            sscPropagationValidationReport,
            ct);

        app.Logger.LogInformation("GENERATE_WEEKLY_SCENES_RESPONSE_BUILD_START pipelineRunId={PipelineRunId}", pipelineRunId);
        var weeklyScenesReady = imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase)
            && assetRealization.DynamicSceneNormalizationReady
            && assetRealization.NullSourceAssetsAfterNormalization == 0;
        var output = new WeeklySkyForecastV2GenerateWeeklyScenesResponse(
            pipelineRunId,
            root,
            skyfieldResponsePath,
            storyBeatsPath,
            narrationManifestPath,
            primaryScreenshots.Count,
            scriptPaths.Count,
            screenshots.Count,
            screenshots,
            warnings,
            allFramePlans.Sum(x => x.FramePlans.Count),
            screenshots,
            primaryScreenshots,
            framePlanPath,
            qualityPath,
            imageSequencePlanPath,
            imageSequencePlan.TotalImages,
            imageSequencePlan.EstimatedDurationSeconds,
            imageSequencePlan.ProductionReady,
            imageSequencePlan.ValidationStatus,
            allSelectedImagesValid,
            imageSequencePlan.DuplicateImagesDetected,
            imageSequencePlan.PrimaryScreenshotsDeprecated,
            imageSequencePlan.ProductionImageSource,
            episodeArchitecture.WeeklyEpisodePlanPath,
            episodeArchitecture.WeeklyLongformPlanPath,
            episodeArchitecture.WeeklyShortformPlanPath,
            episodeArchitecture.LongFormPlan.TotalTargetDurationSeconds,
            episodeArchitecture.ShortFormPlan.TotalTargetDurationSeconds,
            episodeArchitecture.EpisodeArchitectureReady,
            segmentClassification.Path,
            segmentClassification.Plan.SegmentClassificationReady,
            segmentClassification.Plan.ClassifiedLongformSegmentCount,
            segmentClassification.Plan.ClassifiedShortformSegmentCount,
            segmentClassification.Plan.HeroEventSegmentType,
            segmentClassification.Plan.HeroEventObjects,
            segmentDiversification.Path,
            segmentDiversification.Plan.SegmentDiversificationReady,
            segmentDiversification.Plan.DiversifiedLongformSegmentCount,
            segmentDiversification.Plan.DiversifiedShortformSegmentCount,
            segmentDiversification.Plan.AssetExpansionRequired || aiCinematicAssets.MissingRequiredAICinematicAssetCount > 0,
            segmentDiversification.Plan.HighestRetentionRiskScore,
            segmentDiversification.Plan.HighestRepetitionRiskScore,
            visualAssetPlanning.PlanPath,
            visualAssetPlanning.BalanceReportPath,
            visualAssetPlanning.Plan.VisualAssetPlanningReady,
            visualAssetPlanning.Plan.PlannedVisualAssetCount,
            visualAssetPlanning.Plan.PlannedMotionGraphicsCount,
            visualAssetPlanning.Plan.PlannedEducationalOverlayCount,
            visualAssetPlanning.Plan.PlannedAICinematicCount,
            visualAssetPlanning.Plan.PlannedNASAAssetCount,
            visualAssetPlanning.Plan.PlannedJWSTAssetCount,
            visualBalanceHealthyAfterAICinematicAssets,
            aiCinematicAssets.PlanPath,
            aiCinematicAssets.ResultsPath,
            aiCinematicAssets.RealizationReportPath,
            aiCinematicAssets.GenerationReady,
            aiCinematicAssets.PlannedCount,
            aiCinematicAssets.SelectedCount,
            aiCinematicAssets.GeneratedCount,
            aiCinematicAssets.DeferredCount,
            aiCinematicAssets.FailedCount,
            aiCinematicAssets.SkippedExistingValidCount,
            aiCinematicAssets.ProductionReadyCount,
            aiCinematicAssets.Partial,
            aiCinematicAssets.MaxAssetsPerRun,
            aiCinematicAssets.ProviderConfigured,
            aiCinematicAssets.AzureImageDeploymentUsed,
            aiCinematicAssets.AICinematicCandidateCount,
            aiCinematicAssets.RequiredAICinematicAssetCount,
            aiCinematicAssets.OptionalAICinematicCandidateCount,
            aiCinematicAssets.SelectedRequiredAICinematicAssetCount,
            aiCinematicAssets.GeneratedRequiredAICinematicAssetCount,
            aiCinematicAssets.ProductionReadyRequiredAICinematicAssetCount,
            aiCinematicAssets.MissingRequiredAICinematicAssetCount,
            aiCinematicAssets.GeneratedOptionalAICinematicAssetCount,
            aiCinematicAssets.DeferredOptionalAICinematicAssetCount,
            aiCinematicAssets.AICinematicRequiredPackageReady,
            assetExpansion.PlanPath,
            assetExpansion.CoverageReportPath,
            assetExpansion.RenderScenePlanPath,
            assetExpansion.Plan.AssetExpansionPlanningReady,
            assetExpansion.Plan.LongformVisualPackageCount,
            assetExpansion.Plan.ShortformVisualPackageCount,
            assetExpansion.Plan.ExpandedRenderSceneRequirementCount,
            assetExpansion.Plan.UniqueAstronomySceneRequirementCount,
            assetExpansion.Plan.ReadyForVideoPlanningSegmentCount,
            assetExpansion.Plan.NeedsAssetGenerationSegmentCount,
            assetExpansion.Plan.AssetExpansionPlanningMode,
            expandedStellariumExecution.ReportPath,
            expandedStellariumExecution.Ready,
            expandedStellariumExecution.ExpandedNightGeometryReady,
            expandedStellariumExecution.ExpandedSelectedObservationUtc,
            expandedStellariumExecution.ExpandedSelectedObservationLocal,
            expandedStellariumExecution.ExpandedSelectedSunAltitudeDeg,
            expandedStellariumExecution.ExpandedNightValidationStatus,
            expandedStellariumExecution.FailedExpandedAssetReasons,
            expandedStellariumExecution.Partial,
            expandedStellariumExecution.TimedOut,
            expandedStellariumExecution.MaxExpandedScenesPerRun,
            expandedStellariumExecution.MaxFramesPerExpandedScene,
            expandedStellariumExecution.ExecutedExpandedSceneCount,
            expandedStellariumExecution.SkippedExpandedSceneCount,
            expandedStellariumExecution.GeneratedExpandedSscScriptCount,
            expandedStellariumExecution.GeneratedExpandedScreenshotCount,
            scriptPaths.Count + expandedStellariumExecution.GeneratedExpandedSscScriptCount,
            screenshots.Count + expandedStellariumExecution.GeneratedExpandedScreenshotCount,
            expandedStellariumExecution.Mode,
            expandedFrameScreenshots,
            stellariumProductionFrameScreenshots,
            aiCinematicImagePaths,
            realizedAllProductionImageAssets,
            assetRealization.WeeklyProductionAssetManifestPath,
            assetRealization.WeeklyAssetRealizationReportPath,
            assetRealization.WeeklyVideoReadinessReportPath,
            assetRealization.AssetRealizationReady,
            assetRealization.Manifest.TotalProductionImageAssetCount,
            assetRealization.Manifest.StellariumBaseAssetCount,
            assetRealization.Manifest.ExpandedStellariumAssetCount,
            assetRealization.Manifest.AICinematicAssetCount,
            assetRealization.NasaImageCount,
            assetRealization.NasaAssetPlanPath,
            assetRealization.NasaAssetResultsPath,
            assetRealization.NasaAssetRealizationReportPath,
            assetRealization.PlannedNASAAssetCount,
            assetRealization.GeneratedNASAAssetCount,
            assetRealization.ProductionReadyNASAAssetCount,
            assetRealization.FailedNASAAssetCount,
            nasaImagePaths,
            assetRealization.JwstAssetPlanPath,
            assetRealization.JwstAssetResultsPath,
            assetRealization.JwstAssetRealizationReportPath,
            assetRealization.PlannedJWSTAssetCount,
            assetRealization.GeneratedJWSTAssetCount,
            assetRealization.ProductionReadyJWSTAssetCount,
            assetRealization.FailedJWSTAssetCount,
            jwstImagePaths,
            assetRealization.NasaProviderConfigured,
            assetRealization.JwstImageCount,
            assetRealization.Manifest.MotionGraphicsAssetCount,
            assetRealization.Manifest.EducationalOverlayAssetCount,
            assetRealization.PlannedMotionGraphicCount,
            assetRealization.GeneratedMotionGraphicCount,
            assetRealization.ProductionReadyMotionGraphicCount,
            motionGraphicPaths,
            assetRealization.GeneratedEducationalOverlayCount,
            assetRealization.ProductionReadyEducationalOverlayCount,
            educationalOverlayPaths,
            assetRealization.VideoReadinessReport.TestVideoPipelineReady,
            assetRealization.VideoReadinessReport.FinalVideoPipelineReady,
            assetRealization.VideoReadinessReport.ReadySegmentCountForTest,
            assetRealization.VideoReadinessReport.ReadySegmentCountForFinal,
            assetRealization.VideoReadinessReport.NotReadySegments.Count,
            narrationVisualTimeline.WeeklyNarrationVisualTimelinePath,
            narrationVisualTimeline.WeeklyTimelineValidationReportPath,
            narrationVisualTimeline.NarrationVisualTimelineReady,
            narrationVisualTimeline.ValidationReport.LongformTimelineReadyForTest,
            narrationVisualTimeline.ValidationReport.ShortformTimelineReadyForTest,
            narrationVisualTimeline.ValidationReport.LongformTimelineReadyForFinalVideo,
            narrationVisualTimeline.ValidationReport.ShortformTimelineReadyForFinalVideo,
            narrationVisualTimeline.ValidationReport.TotalShotCount,
            narrationVisualTimeline.ValidationReport.TotalTimelineDurationSeconds,
            narrationVisualTimeline.ValidationReport.TimelineValidationStatus,
            assetRealization.AssetQualityReportPath,
            assetRealization.TotalValidatedAssets,
            assetRealization.ProductionReadyAssetCount,
            assetRealization.ProductionWarningAssetCount,
            assetRealization.ProductionFailedAssetCount,
            assetRealization.QualityGatePassed,
            assetRealization.FailedAssetPaths,
            eventPriorityScoring.WeeklyEventPriorityReportPath,
            eventPriorityScoring.HeroEventSelectionPath,
            eventPriorityScoring.ThumbnailCandidateReportPath,
            eventPriorityScoring.OpeningHookCandidateReportPath,
            eventPriorityScoring.HighestPriorityEventCode,
            eventPriorityScoring.HighestPriorityEventScore,
            eventPriorityScoring.HeroEventClassification,
            eventPriorityScoring.TopThreeEventCodes,
            eventPriorityScoring.EventPriorityScoringReady,
            narrationEngine.LongformNarrationPath,
            narrationEngine.ShortformNarrationPath,
            narrationEngine.NarrationAssetMapPath,
            narrationEngine.NarrationTimelineMapPath,
            narrationEngine.WeeklyNarrationReportPath,
            narrationEngine.LongformNarrationReady,
            narrationEngine.ShortformNarrationReady,
            narrationEngine.NarrationAssetMappingReady,
            narrationEngine.NarrationTimelineReady,
            narrationEngine.TotalLongformNarrationSeconds,
            narrationEngine.TotalShortformNarrationSeconds,
            narrationEngine.NarrationEditorialRefinementReady,
            narrationEngine.DocumentaryNarrationReady,
            narrationEngine.VisualVarietyPassed,
            narrationEngine.RepeatedAssetSequenceCount,
            narrationEngine.InternalMetadataLeakCount,
            narrationEngine.EditorialReviewReportPath,
            timelineComposition.TimelineCompositionReady,
            timelineComposition.FinalRenderTimelinePath,
            timelineComposition.FinalRenderShotListPath,
            timelineComposition.TimelineTransitionPlanPath,
            timelineComposition.SegmentTimelineReportPath,
            timelineComposition.RetentionMarkerTimelinePath,
            timelineComposition.FinalTimelineValidationReportPath,
            timelineComposition.LongformFinalTimelineReady,
            timelineComposition.ShortformFinalTimelineReady,
            timelineComposition.LongformActualDurationSeconds,
            timelineComposition.ShortformActualDurationSeconds,
            timelineComposition.LongformFinalShotCount,
            timelineComposition.ShortformFinalShotCount,
            timelineComposition.TotalFinalShotCount,
            timelineComposition.ValidationReport.AssetValidationPassed,
            timelineComposition.ValidationReport.NarrationValidationPassed,
            timelineComposition.ValidationReport.DurationValidationPassed,
            timelineComposition.ValidationReport.GapValidationPassed,
            timelineComposition.ValidationReport.OverlapValidationPassed,
            timelineComposition.ValidationReport.VisualVarietyPassed,
            timelineComposition.ValidationReport.HeroEventRulePassed,
            timelineComposition.ValidationReport.AstrophotographyRulePassed,
            timelineComposition.ValidationReport.SummaryRulePassed,
            timelineComposition.ValidationReport.ShortformRulePassed,
            ffmpegRendererPreparation.ValidationReport.RendererPreparationReady,
            ffmpegRendererPreparation.WeeklyRenderContractPath,
            ffmpegRendererPreparation.RenderInputManifestPath,
            ffmpegRendererPreparation.FfmpegFilterGraphPlanPath,
            ffmpegRendererPreparation.TransitionExecutionPlanPath,
            ffmpegRendererPreparation.MotionEffectExecutionPlanPath,
            ffmpegRendererPreparation.AudioAlignmentPlanPath,
            ffmpegRendererPreparation.RendererValidationReportPath,
            ffmpegRendererPreparation.ValidationReport.LongformRenderContractReady,
            ffmpegRendererPreparation.ValidationReport.ShortformRenderContractReady,
            ffmpegRendererPreparation.ValidationReport.AllTimelineAssetsFound,
            ffmpegRendererPreparation.ValidationReport.AllTimelineAssetsReadable,
            ffmpegRendererPreparation.ValidationReport.DurationConsistencyPassed,
            ffmpegRendererPreparation.ValidationReport.ResolutionPlanPassed,
            ffmpegRendererPreparation.ValidationReport.TransitionPlanPassed,
            ffmpegRendererPreparation.ValidationReport.AudioAlignmentPlanReady,
            File.Exists(weeklyFocusObjectPlanPath),
            File.Exists(weeklyStellariumSceneRequirementsPath),
            File.Exists(visualNarrationCoverageReportPath),
            visualNarrationCoverage.VisualNarrationAligned,
            weeklyFocusPlan.FocusObjects,
            (weeklyFocusPlan.FocusGroupings ?? Array.Empty<WeeklyFocusGrouping>()).Select(x => x.GroupingCode).ToList(),
            visualNarrationCoverage.MoonSceneCount,
            visualNarrationCoverage.VenusSceneCount,
            visualNarrationCoverage.SaturnSceneCount,
            visualNarrationCoverage.GroupingSceneCount,
            weeklyFocusObjectPlanPath,
            weeklyStellariumSceneRequirementsPath,
            visualNarrationCoverageReportPath,
            sscSceneManifestPath,
            sscPropagationValidationReport.sscPropagationReady,
            sscPropagationValidationReportPath,
            sscPropagationValidationReport.emptyObjectSceneCount,
            sscPropagationValidationReport.emptyRequiredLabelSceneCount,
            sscPropagationValidationReport.cameraTargetMismatchCount,
            sscCameraLockValidationReport.sscCameraLockReady,
            sscCameraLockValidationReportPath,
            sscCameraLockValidationReport.objectFirstCameraLockSceneCount,
            sscCameraLockValidationReport.altAzOnlySceneCount,
            sscCameraLockValidationReport.fallbackUsedSceneCount,
            selectedStellariumImageSequenceReportPath,
            imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase),
            aiCinematicGenerationReportPath,
            assetRealization.PostAssetDynamicSceneNormalizationReportPath,
            assetRealization.DynamicSceneNormalizationReady,
            assetRealization.NullSourceAssetsAfterNormalization,
            weeklyScenesReady,
            assetProviderSummary);

        app.Logger.LogInformation("GENERATE_WEEKLY_SCENES_RESPONSE_BUILD_COMPLETE pipelineRunId={PipelineRunId} weeklyScenesReady={WeeklyScenesReady} dynamicSceneNormalizationReady={DynamicSceneNormalizationReady} nullSourceAssetsAfterNormalization={NullSourceAssetsAfterNormalization}", pipelineRunId, weeklyScenesReady, assetRealization.DynamicSceneNormalizationReady, assetRealization.NullSourceAssetsAfterNormalization);
        return Results.Ok(output);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "WEEKLY_POST_ASSET_NULL_SOURCE_FAILURE stage={Stage} field={Field} sceneCode={SceneCode} assetPath={AssetPath} pipelineRunId={PipelineRunId}", "GENERATE_WEEKLY_SCENES", ex is ArgumentNullException argumentNullException ? argumentNullException.ParamName ?? "unknown" : "unknown", "unknown", "unknown", request.PipelineRunId);
        return Results.BadRequest(new { error = ex.Message });
    }
});


app.MapPost("/api/content-planning/weekly-skyforecast-v2/compose-timeline", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastTimelineCompositionOrchestrator orchestrator, IContentPlanningService planning, CancellationToken ct) =>
{
    var contentPlanId = request.ContentGenerationPlanId;
    if (!contentPlanId.HasValue)
    {
        var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
        contentPlanId = plan.ContentGenerationPlanId;
    }

    var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
    var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(
        request.ContentCategoryCode,
        request.Language,
        request.RegionId,
        request.RegionName,
        request.ScheduledUtc,
        request.WeekStartDate,
        Diagnostics: request.Diagnostics,
        PipelineRunId: pipelineRunId,
        ContentGenerationPlanId: contentPlanId);
    var result = await orchestrator.RunAsync(intelligenceRequest, contentPlanId, ct);
    return Results.Ok(result);
});


app.MapPost("/api/content-planning/weekly-skyforecast-v2/render-final-media", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastFinalMediaOrchestrator finalMediaOrchestrator, IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator, IWeeklySkyForecastSceneRenderingOrchestrator sceneOrchestrator, IWeeklySkyForecastV2IntelligenceService intelligenceService, IContentPlanningService planning, IWeeklySkyForecastContextBuilderV2 contextBuilder, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("WeeklySkyForecastRenderFinalMediaEndpoint");
    var contentPlanId = request.ContentGenerationPlanId;
    if (!contentPlanId.HasValue)
    {
        var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
        contentPlanId = plan.ContentGenerationPlanId;
    }

    var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
    var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, request.WeekStartDate, Diagnostics: request.Diagnostics, PipelineRunId: pipelineRunId, ContentGenerationPlanId: contentPlanId);
    using var skyfieldTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    skyfieldTimeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
    var weeklyForecast = await contextBuilder.BuildAsync(new WeeklySkyForecastV2OrchestrationContext(contentPlanId.Value, pipelineRunId, null, intelligenceRequest, null, null, null, null, null, null, DateTime.UtcNow), skyfieldTimeoutCts.Token);
    var orchestrationContext = new WeeklySkyForecastV2OrchestrationContext(
        contentPlanId.Value,
        pipelineRunId,
        null,
        intelligenceRequest,
        null,
        weeklyForecast,
        null,
        null,
        null,
        null,
        DateTime.UtcNow,
        SkyfieldWeeklyForecastCalls: 1,
        RegionResolveCalls: 1,
        ContextReusedAcrossPhases: true);
    logger.LogInformation("Starting intelligence preview");
    var intelligence = await intelligenceService.PreviewAsync(orchestrationContext, ct);
    logger.LogInformation("Completed intelligence preview");

    var phaseContext = orchestrationContext with
    {
        IntelligencePreviewCalls = 1,
        IntelligencePreviewResult = intelligence,
        RenderPreparationPackage = intelligence.RenderPreparationPackage
    };

    logger.LogInformation("Starting Phase 6A render preparation");
    logger.LogInformation("Completed Phase 6A render preparation");

    logger.LogInformation("Starting Phase 6B scene rendering");
    string? lastStartedSceneCode = null;
    string? lastCompletedSceneCode = null;
    string? failedSceneCode = null;
    var sceneRenderingStarted = true;
    var sceneRenderingCompleted = false;
    var sceneRendering = await sceneOrchestrator.RunAsync(phaseContext, ct);
    sceneRenderingCompleted = true;
    lastStartedSceneCode = sceneRendering.SceneRenderResults.LastOrDefault()?.SceneCode;
    lastCompletedSceneCode = sceneRendering.SceneRenderResults.LastOrDefault(x => string.Equals(x.Status, "Rendered", StringComparison.OrdinalIgnoreCase))?.SceneCode;
    failedSceneCode = sceneRendering.SceneRenderResults.FirstOrDefault(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase))?.SceneCode;
    logger.LogInformation("Completed Phase 6B scene rendering");

    var phaseContextB = phaseContext with { SceneRenderingPackage = sceneRendering };
    logger.LogInformation("Starting Phase 6C timeline composition");
    var timeline = await timelineOrchestrator.RunAsync(phaseContextB, ct);
    logger.LogInformation("Completed Phase 6C timeline composition");

    var phaseContextC = phaseContextB with { TimelineCompositionPackage = timeline };
    logger.LogInformation("Starting Phase 6D final media realization");
    var finalMedia = await finalMediaOrchestrator.RunAsync(phaseContextC, ct);
    logger.LogInformation("Completed Phase 6D final media realization");

    var finalDirectory = Path.GetDirectoryName(timeline.LongFormTimelineResult.OutputPath);
    var workingDirectoryRoot = string.IsNullOrWhiteSpace(finalDirectory)
        ? finalDirectory
        : Path.GetDirectoryName(finalDirectory);

    return Results.Ok(new
    {
        contentGenerationPlanId = contentPlanId,
        pipelineRunId,
        workingDirectoryRoot,
        diagnostics = new
        {
            skyfieldWeeklyForecastCalls = 1,
            regionResolveCalls = 1,
            contextReusedAcrossPhases = true,
            intelligencePreviewCalls = 1,
            warning = 1 > 1 ? "WeeklySkyForecast sidecar called multiple times in one pipeline run." : null,
            sceneRenderingStarted,
            sceneRenderingCompleted,
            lastStartedSceneCode,
            lastCompletedSceneCode,
            failedSceneCode,
            ffmpegProcessTimedOut = sceneRendering.SceneRenderingValidation.BlockingIssues.Any(x => x.Contains("Scene render timeout:", StringComparison.OrdinalIgnoreCase)),
            ffmpegExitCode = 0,
            ffmpegStdErr = sceneRendering.SceneRenderingValidation.BlockingIssues.FirstOrDefault(x => x.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
        },
        timelineCompositionPackage = timeline,
        finalMediaPackage = finalMedia
    });
});

app.MapPost("/api/content-planning/weekly-skyforecast-v2/run-through-timeline", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastV2IntelligenceService intelligenceService, IWeeklySkyForecastSceneRenderingOrchestrator sceneOrchestrator, IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator, IContentPlanningService planning, CancellationToken ct) =>
{
    var contentPlanId = request.ContentGenerationPlanId;
    if (!contentPlanId.HasValue)
    {
        var plan = await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime, GeneratedByAi: true), ct);
        contentPlanId = plan.ContentGenerationPlanId;
    }

    var pipelineRunId = request.PipelineRunId ?? contentPlanId.Value;
    var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, request.WeekStartDate, Diagnostics: request.Diagnostics, PipelineRunId: pipelineRunId, ContentGenerationPlanId: contentPlanId);

    var intelligence = await intelligenceService.PreviewAsync(intelligenceRequest, ct);
    var renderPreparation = intelligence.RenderPreparationPackage ?? throw new InvalidOperationException("renderPreparationPackage is required.");
    var sceneRendering = await sceneOrchestrator.RunAsync(intelligenceRequest, contentPlanId, ct);
    var timeline = await timelineOrchestrator.RunAsync(intelligenceRequest, contentPlanId, ct);

    return Results.Ok(new
    {
        contentGenerationPlanId = contentPlanId,
        pipelineRunId,
        workingDirectoryRoot = renderPreparation.WorkingDirectoryPlan.RootPath,
        singleContentPlanContextUsed = true,
        singlePipelineRunIdUsed = timeline.TimelineCompositionValidation.SinglePipelineRunIdUsed,
        mixedPipelineRunIdsDetected = !timeline.TimelineCompositionValidation.SinglePipelineRunIdUsed,
        sceneRenderingPackage = sceneRendering,
        timelineCompositionPackage = timeline
    });
});
app.MapPost("/api/content-planning/run-weekly-skyforecast-preparation", async (WeeklySkyForecastProductionRequest request, IWeeklySkyForecastPreparationOrchestrator orchestrator, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        logger.LogInformation("WeeklySkyForecast preparation raw request payload: {@Request}", request);
        logger.LogInformation("WeeklySkyForecast preparation parsed WeekStartDate={WeekStartDate}, WeekEndDate={WeekEndDate}", request.WeekStartDate, request.WeekEndDate);
        if (request.WeekStartDate == DateOnly.MinValue || request.WeekEndDate == DateOnly.MinValue)
            return Results.BadRequest(new { message = "weekStartDate and weekEndDate are required and cannot be DateOnly.MinValue." });
        var safeRequest = request with { PublishToYouTube = false, PublishToFacebook = false, PublishToInstagram = false };
        var response = await orchestrator.RunAsync(safeRequest, ct);
        return Results.Ok(response);
    }
    catch (KeyNotFoundException ex)
    {
        if (ex is WeeklySkyForecastRegionResolutionException regionEx)
        {
            return Results.BadRequest(new
            {
                requestedRegionId = regionEx.RequestedRegionId,
                availableRegionIds = regionEx.AvailableRegionIds,
                message = regionEx.Message
            });
        }
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/content-planning/generate-daily-plan", async (GenerateDailyPlanRequest request, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        var plan = await planning.GenerateDailyPlanAsync(
            request.ContentCategoryCode,
            request.Language,
            request.RegionId,
            request.ScheduledUtc,
            request.PrimaryCelestialObjectCode,
            ct);

        return Results.Ok(new GenerateDailyPlanResponse(plan.Id, plan.Status));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/content-planning/categories/{categoryCode}/requirements", async (string categoryCode, ICategoryRequirementResolver resolver, CancellationToken ct) =>
    Results.Ok(await resolver.ResolveAsync(categoryCode, ct)));

app.MapGet("/api/content-planning/plans", async (string? status, IContentPlanningService planning, CancellationToken ct) =>
    Results.Ok(await planning.GetPendingPlansAsync(status, ct)));
app.MapGet("/api/content-planning/plans/{id:guid}", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    var plan = await planning.GetPlanByIdAsync(id, ct);
    return plan is null ? Results.NotFound() : Results.Ok(plan);
});
app.MapGet("/api/content-planning/plans/{id:guid}/visual-strategy-preview", async (Guid id, IContentPlanningService planning, IVisualStrategyResolver resolver, CancellationToken ct) =>
{
    var plan = await planning.GetPlanByIdAsync(id, ct);
    if (plan is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(await resolver.ResolveAsync(plan, ct));
});

app.MapGet("/api/content-planning/plans/{id:guid}/pipeline-request-preview", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        var preview = await planning.BuildPipelineRequestPreviewAsync(id, ct);
        return Results.Ok(new
        {
            pipelineRequest = preview.PipelineRequest,
            assetAwareMetadata = preview.AssetAwareMetadata,
            warnings = preview.Warnings
        });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/content-planning/plans/{id:guid}/daily-sky-context-preview", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await planning.BuildDailySkyGuideContextPreviewAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});


app.MapGet("/api/content-planning/plans/{id:guid}/astronomy-visibility-preview", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await planning.BuildAstronomyVisibilityPreviewAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});
app.MapGet("/api/content-planning/plans/{id:guid}/stellarium-scene-plan-preview", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await planning.BuildStellariumScenePlanPreviewAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/content-planning/plans/{id:guid}/ssc-script-preview", async (Guid id, IContentPlanningService planning, IStellariumScriptGenerator scriptGenerator, CancellationToken ct) =>
{
    try
    {
        var scenePlan = await planning.BuildStellariumScenePlanPreviewAsync(id, ct);
        var scripts = new List<StellariumScriptGenerationResult>();
        foreach (var scene in scenePlan.Scenes.OrderBy(x => x.SortOrder))
        {
            scripts.Add(await scriptGenerator.GenerateAsync(scenePlan, scene, ct));
        }

        return Results.Ok(new { scenePlan.ContentGenerationPlanId, scenePlan.ContentCategoryCode, ScriptCount = scripts.Count, Scripts = scripts, scenePlan.Warnings });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/content-planning/plans/{id:guid}/ssc-script-preview/{sceneCode}", async (Guid id, string sceneCode, IContentPlanningService planning, IStellariumScriptGenerator scriptGenerator, CancellationToken ct) =>
{
    try
    {
        var scenePlan = await planning.BuildStellariumScenePlanPreviewAsync(id, ct);
        var scene = scenePlan.Scenes.FirstOrDefault(x => string.Equals(x.SceneCode, sceneCode, StringComparison.OrdinalIgnoreCase));
        if (scene is null)
        {
            return Results.NotFound(new { message = $"Scene code '{sceneCode}' was not found for plan '{id}'." });
        }

        var script = await scriptGenerator.GenerateAsync(scenePlan, scene, ct);
        return Results.Ok(script);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});
app.MapGet("/api/content-planning/plans/{id:guid}/visual-assets-preview", async (Guid id, IDailySkyGuideVisualAssetPackager packager, CancellationToken ct) =>
{
    var package = await packager.BuildPackageAsync(id, ct);
    return Results.Ok(package);
});
app.MapPost("/api/content-planning/plans/{id:guid}/capture-stellarium-scenes", async (Guid id, StellariumCaptureExecutionApiRequest apiRequest, IContentPlanningService planning, IStellariumImageCaptureExecutor executor, CancellationToken ct) =>
{
    try
    {
        var plan = await planning.GetPlanByIdAsync(id, ct);
        if (plan is null)
        {
            return Results.NotFound(new { message = $"Content generation plan '{id}' was not found." });
        }

        if (plan.Status is not ("Planned" or "ReadyForManualRun" or "InProgress"))
        {
            return Results.BadRequest(new { message = "Plan status must be Planned, ReadyForManualRun, or InProgress to capture Stellarium scenes." });
        }

        var scenePlan = await planning.BuildStellariumScenePlanPreviewAsync(id, ct);
        var request = new StellariumCaptureExecutionRequest(id, apiRequest.DryRun, apiRequest.OverwriteExisting, apiRequest.Diagnostics);
        var response = await executor.CaptureAsync(scenePlan, request, ct);
        return Results.Ok(response);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/content-planning/plans/{id:guid}/stellarium-capture-diagnostics", async (Guid id, IStellariumImageCaptureExecutor executor, CancellationToken ct) =>
{
    var response = await executor.GetDiagnosticsAsync(id, ct);
    return Results.Ok(response);
});

app.MapPost("/api/content-planning/weekly-skyforecast/{contentGenerationPlanId:guid}/generate-visual-assets", async (Guid contentGenerationPlanId, WeeklySkyForecastVisualAssetsGenerateRequest request, IWeeklySkyForecastVisualAssetGenerationService service, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await service.GenerateAsync(contentGenerationPlanId, request, productionRequest: null, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/content-planning/weekly-skyforecast/{contentGenerationPlanId:guid}/render-segments", async (Guid contentGenerationPlanId, WeeklySkyForecastSegmentVideoRenderRequest request, IWeeklySkyForecastSegmentVideoRenderer service, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await service.RenderAsync(contentGenerationPlanId, request, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/content-planning/plans/{id:guid}/asset-aware-manual-run-package", async (Guid id, IAssetAwareManualRunPreparationService preparation, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await preparation.PrepareAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/content-planning/plans/{id:guid}/daily-skyguide-asset-context", async (Guid id, IDailySkyGuideAssetAwareContextService contextService, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await contextService.BuildAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapGet("/api/content-planning/plans/{id:guid}/daily-skyguide-composition-plan", async (Guid id, IDailySkyGuideAssetAwareCompositionPlanner planner, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await planner.BuildAsync(id, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});


app.MapPost("/api/content-planning/plans/{id:guid}/generate-preview-video", async (Guid id, AssetAwarePreviewVideoRequest request, IDailySkyGuidePreviewVideoGenerator generator, CancellationToken ct) =>
{
    var response = await generator.GenerateAsync(id, request, ct);
    return response.Success ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapGet("/api/content-planning/plans/{id:guid}/preview-video-info", async (Guid id, IDailySkyGuidePreviewVideoGenerator generator, CancellationToken ct) =>
{
    var response = await generator.GetPreviewInfoAsync(id, ct);
    return response.Success ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/content-planning/plans/{id:guid}/prepare-manual-run", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        var response = await planning.PrepareManualRunAsync(id, ct);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapPost("/api/content-planning/plans/{id:guid}/mark-ready", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    var updated = await planning.MarkPlanReadyForManualRunAsync(id, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
app.MapPost("/api/content-planning/plans/{id:guid}/start-manual-execution", async (Guid id, IContentPlanningService planning, CancellationToken ct) =>
{
    try
    {
        var response = await planning.StartManualExecutionAsync(id, ct);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapPost("/api/content-planning/executions/{executionId:guid}/complete", async (Guid executionId, CompleteContentPlanningExecutionRequest request, IContentPlanningService planning, CancellationToken ct) =>
{
    var execution = await planning.CompleteExecutionAsync(executionId, request, ct);
    return execution is null ? Results.NotFound() : Results.Ok(execution);
});
app.MapPost("/api/content-planning/executions/{executionId:guid}/fail", async (Guid executionId, FailContentPlanningExecutionRequest request, IContentPlanningService planning, CancellationToken ct) =>
{
    var execution = await planning.FailExecutionAsync(executionId, request, ct);
    return execution is null ? Results.NotFound() : Results.Ok(execution);
});
app.MapGet("/api/content-planning/executions", async (string? status, IContentPlanningService planning, CancellationToken ct) =>
    Results.Ok(await planning.GetExecutionsAsync(status, ct)));
app.MapGet("/api/content-planning/executions/{executionId:guid}", async (Guid executionId, IContentPlanningService planning, CancellationToken ct) =>
{
    var execution = await planning.GetExecutionByIdAsync(executionId, ct);
    return execution is null ? Results.NotFound() : Results.Ok(execution);
});

app.MapGet("/api/content-categories/settings", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ContentCategorySettings.AsNoTracking().OrderBy(x => x.Priority).ToListAsync(ct)));
app.MapGet("/api/content-categories/settings/{pipelineType}", async (ContentPipelineType pipelineType, IContentCategorySettingsService svc, CancellationToken ct) =>
{
    var settings = await svc.GetSettingsAsync(pipelineType, ct);
    return settings is null ? Results.NotFound() : Results.Ok(settings);
});
app.MapPut("/api/content-categories/settings/{pipelineType}", async (ContentPipelineType pipelineType, ContentCategorySettings incoming, MediaFactoryDbContext db, CancellationToken ct) =>
{
    var current = await db.ContentCategorySettings.FirstOrDefaultAsync(x => x.PipelineType == pipelineType, ct);
    if (current is null) return Results.NotFound();
    incoming.PipelineType = pipelineType;
    db.Entry(current).CurrentValues.SetValues(incoming);
    current.Touch();
    await db.SaveChangesAsync(ct);
    return Results.Ok(current);
});
app.MapPost("/api/content-pipelines/run/{pipelineType}", async (ContentPipelineType pipelineType, ContentPipelineRunRequest request, IEnumerable<IContentCategoryPipeline> pipelines, IContentCategorySettingsService settingsService, CancellationToken ct) =>
{
    if (!await settingsService.IsEnabledAsync(pipelineType, ct)) return Results.BadRequest(new { message = "Content category is disabled." });
    var pipeline = pipelines.FirstOrDefault(x => x.PipelineType == pipelineType);
    if (pipeline is null) return Results.NotFound(new { message = $"Pipeline '{pipelineType}' is not wired yet." });
    var result = await pipeline.RunAsync(request, ct);
    return Results.Ok(result);
});

app.MapGet("/api/pipelines/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAsync(20, ct)));
app.MapGet("/api/pipelines/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.MapGet("/api/scripts/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentScriptsAsync(20, ct)));
app.MapGet("/api/scheduler/status", async (IPipelineSchedulerService scheduler, CancellationToken ct) => Results.Ok(await scheduler.GetStatusAsync(ct)));
app.MapGet("/api/scheduler/event-plan", async (string regionId, DateOnly date, IPipelineSchedulerService scheduler, CancellationToken ct) =>
    Results.Ok(await scheduler.GetEventPlanAsync(regionId, date, ct)));
app.MapGet("/api/regions", async (IPipelineSchedulerService scheduler, CancellationToken ct) => Results.Ok(await scheduler.GetRegionsAsync(ct)));
app.MapPost("/api/regions/{regionId}/run-now", async (string regionId, bool? force, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var result = await scheduler.RunRegionNowAsync(regionId, force ?? false, ct);
    return result.Status == "NotFound" ? Results.NotFound(new { message = result.Reason }) : Results.Ok(result);
});
app.MapPost("/api/regions/{regionId}/enable", async (string regionId, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.EnableRegionAsync(regionId, ct);
    return updated ? Results.Ok(new { regionId, enabled = true }) : Results.NotFound(new { message = $"Region '{regionId}' was not found." });
});
app.MapPost("/api/regions/{regionId}/disable", async (string regionId, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.DisableRegionAsync(regionId, ct);
    return updated ? Results.Ok(new { regionId, enabled = false }) : Results.NotFound(new { message = $"Region '{regionId}' was not found." });
});
app.MapGet("/api/tokenhealth", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckAllAsync(ct)));
app.MapGet("/api/tokenhealth/youtube", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckYouTubeAsync(ct)));
app.MapGet("/api/tokenhealth/meta", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckMetaAsync(ct)));
app.MapPost("/api/scheduler/run-now/{scheduleName}", async (string scheduleName, bool? force, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var result = await scheduler.RunNowAsync(scheduleName, force ?? false, ct);
    return result.Status == "NotFound" ? Results.NotFound(new { message = result.Reason }) : Results.Ok(result);
});
app.MapPost("/api/scheduler/enable/{scheduleName}", async (string scheduleName, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.EnableScheduleAsync(scheduleName, ct);
    return updated ? Results.Ok(new { scheduleName, enabled = true }) : Results.NotFound(new { message = $"Schedule '{scheduleName}' was not found." });
});
app.MapPost("/api/scheduler/disable/{scheduleName}", async (string scheduleName, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.DisableScheduleAsync(scheduleName, ct);
    return updated ? Results.Ok(new { scheduleName, enabled = false }) : Results.NotFound(new { message = $"Schedule '{scheduleName}' was not found." });
});

app.MapPost("/api/events/{eventId}/generate", async (string eventId, string? regionId, ContentType? contentType, RunPipelineRequest request, IAstronomyEventDiscoveryService events, IPipelineRepository repository, PipelineOrchestrator orchestrator, CancellationToken ct) =>
{
    var astronomyEvent = await events.GetByIdAsync(eventId, ct);
    if (astronomyEvent is null)
        return Results.NotFound(new { message = $"Astronomy event '{eventId}' was not found." });

    var duplicateRegionId = string.IsNullOrWhiteSpace(regionId) ? (string.IsNullOrWhiteSpace(request.RegionId) ? request.LocationName : request.RegionId) : regionId;
    var requestedContentType = contentType ?? ContentType.SpecialEventGuide;
    var statuses = new[] { PipelineRunStatus.Queued, PipelineRunStatus.Running, PipelineRunStatus.Succeeded, PipelineRunStatus.CompletedWithPublishErrors };
    if (await repository.HasSpecialEventRunAsync(eventId, request.Date, duplicateRegionId, requestedContentType, statuses, ct))
        return Results.Conflict(new { message = "Special event video already exists for event/date/region/contentType.", eventId, targetDate = request.Date, regionId = duplicateRegionId, contentType = requestedContentType });

    var specialRequest = request with
    {
        ContentType = requestedContentType,
        RegionId = duplicateRegionId,
        EventId = astronomyEvent.EventId,
        EventType = astronomyEvent.EventType,
        EventTitle = astronomyEvent.Title,
        EventDescription = astronomyEvent.Description,
        UseTopicPlanner = false
    };
    var result = await orchestrator.RunAsync(specialRequest, ct);
    return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Special event guide completed."));
});
app.MapGet("/api/events/generated", async (int? take, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetGeneratedSpecialEventRunsAsync(Math.Clamp(take ?? 50, 1, 200), ct)));


app.MapGet("/api/ai-optimization/hooks/{pipelineRunId:guid}", async (Guid pipelineRunId, MediaFactoryDbContext db, CancellationToken ct) =>
{
    var rows = await db.HookOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.FinalScore).ToListAsync(ct);
    return Results.Ok(rows);
});

app.MapGet("/api/ai-optimization/trends/{date}", async (DateOnly date, MediaFactoryDbContext db, CancellationToken ct) =>
{
    var rows = await db.TrendSignals.Where(x => x.SignalDate == date).OrderByDescending(x => x.Score).ToListAsync(ct);
    return Results.Ok(rows);
});

app.MapGet("/api/ai-optimization/publishing/{pipelineRunId:guid}", async (Guid pipelineRunId, MediaFactoryDbContext db, CancellationToken ct) =>
{
    var rows = await db.PublishingOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
    return Results.Ok(rows);
});


app.MapGet("/api/analytics/summary", async (string? platform, MediaFactoryDbContext db, CancellationToken ct) =>
{
    var query = db.PlatformVideoAnalytics.AsQueryable();
    if (!string.IsNullOrWhiteSpace(platform)) query = query.Where(x => x.Platform == platform);
    var rows = await query.ToListAsync(ct);
    return Results.Ok(new {
        impressions = rows.Sum(x => x.Impressions),
        views = rows.Sum(x => x.Views),
        ctr = rows.Count == 0 ? 0d : rows.Average(x => x.Ctr),
        averageWatchDuration = rows.Count == 0 ? 0d : rows.Average(x => x.AverageWatchDuration),
        watchTimeMinutes = rows.Sum(x => x.WatchTimeMinutes),
        likes = rows.Sum(x => x.Likes),
        comments = rows.Sum(x => x.Comments),
        shares = rows.Sum(x => x.Shares),
        subscribersGained = rows.Sum(x => x.SubscribersGained)
    });
});

app.MapGet("/api/analytics/videos/{pipelineRunId:guid}", async (Guid pipelineRunId, MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.PlatformContentAnalytics.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CollectedUtc).ToListAsync(ct)));

app.MapGet("/api/analytics/platforms", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.PlatformVideoAnalytics.Select(x => x.Platform).Distinct().OrderBy(x => x).ToListAsync(ct)));

app.MapGet("/api/analytics/hooks", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.HookPerformance.OrderByDescending(x => x.Views).ToListAsync(ct)));

app.MapGet("/api/analytics/thumbnails", async (MediaFactoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.ThumbnailPerformance.OrderByDescending(x => x.Ctr).ToListAsync(ct)));


app.MapPost("/api/ai-optimization/run/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? force, IPipelineRepository repository, MediaFactoryDbContext db, IAIOptimizationPipelineService aiPipeline, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null) return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });

    var outputDirectory = run.OutputFolder;
    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} output folder was not found.", outputDirectory });

    var scripts = await db.GeneratedScripts.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
    var script = scripts.FirstOrDefault();
    var selectedHook = script?.HookLine ?? script?.Title ?? run.EventTitle ?? "Astronomy tonight";
    var selectedTitle = script?.OptimizedTitle ?? script?.Title ?? run.EventTitle ?? "Astronomy tonight";
    var objects = Array.Empty<string>();
    var longThumb = File.Exists(Path.Combine(outputDirectory, "thumbnail.jpg")) ? Path.Combine(outputDirectory, "thumbnail.jpg") : null;
    var shortThumb = File.Exists(Path.Combine(outputDirectory, "thumbnail-short.jpg")) ? Path.Combine(outputDirectory, "thumbnail-short.jpg") : null;

    var recordsSkipped = 0;
    var warnings = new List<string>();
    if (force == true)
    {
        var existingHook = await db.HookOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        var existingPub = await db.PublishingOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        var existingThumb = await db.ThumbnailOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        db.HookOptimizationResults.RemoveRange(existingHook);
        db.PublishingOptimizationResults.RemoveRange(existingPub);
        db.ThumbnailOptimizationResults.RemoveRange(existingThumb);
        await db.SaveChangesAsync(ct);
    }
    else
    {
        recordsSkipped += await db.HookOptimizationResults.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
        recordsSkipped += await db.PublishingOptimizationResults.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
        recordsSkipped += await db.ThumbnailOptimizationResults.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    }

    var aiResult = await aiPipeline.RunForPipelineAsync(new AIOptimizationPipelineRequest(
        pipelineRunId,
        outputDirectory,
        run.Language,
        run.RegionId,
        DateOnly.FromDateTime(run.CreatedUtc.UtcDateTime),
        run.LocationName,
        selectedHook,
        selectedTitle,
        objects,
        longThumb,
        shortThumb,
        run.EventType ?? run.ContentType.ToString()), ct);

    return Results.Ok(new { pipelineRunId, aiOptimizationExecuted = aiResult.Executed, hookRecordsCreated = aiResult.HookRecordsCreated, analyticsInitialized = false, recordsSkipped, warnings, outputReportPath = Path.Combine(outputDirectory, "ai-hook-optimization-report.json") });
});

app.MapPost("/api/analytics/initialize/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? force, IPipelineRepository repository, MediaFactoryDbContext db, IAnalyticsIngestionService analytics, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null) return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });

    var outputDirectory = run.OutputFolder;
    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} output folder was not found.", outputDirectory });

    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AnalyticsInitialization");
    logger.LogInformation("Analytics initialization START for pipeline run {PipelineRunId}", pipelineRunId);
    var script = await db.GeneratedScripts.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
    var longThumb = Path.Combine(outputDirectory, "thumbnail-long.jpg");
    var fallbackLongThumb = Path.Combine(outputDirectory, "thumbnail.jpg");
    var shortThumb = Path.Combine(outputDirectory, "thumbnail-short.jpg");
    var thumbs = new List<AnalyticsThumbnailSeed>();
    if (File.Exists(longThumb)) thumbs.Add(new AnalyticsThumbnailSeed(longThumb, "Long"));
    else if (File.Exists(fallbackLongThumb)) thumbs.Add(new AnalyticsThumbnailSeed(fallbackLongThumb, "Long"));
    if (File.Exists(shortThumb)) thumbs.Add(new AnalyticsThumbnailSeed(shortThumb, "Short"));
    var platforms = new[] { "YouTube-Long", "YouTube-Short", "Facebook-Long", "Facebook-Reel", "Instagram-Reel" };
    var hooks = new[] { script?.HookLine, run.EventTitle }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    var recordsSkipped = 0;
    if (force == true)
    {
        var existingVideoRows = await db.PlatformVideoAnalytics.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        var existingHookRows = await db.HookPerformance.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        var existingThumbRows = await db.ThumbnailPerformance.Where(x => x.PipelineRunId == pipelineRunId).ToListAsync(ct);
        db.PlatformVideoAnalytics.RemoveRange(existingVideoRows.Where(x => x.Views == 0 && x.Impressions == 0 && x.Likes == 0 && x.Comments == 0 && x.Shares == 0 && x.WatchTimeMinutes == 0));
        db.HookPerformance.RemoveRange(existingHookRows.Where(x => x.Views == 0 && x.Impressions == 0 && x.Likes == 0 && x.Comments == 0 && x.Shares == 0 && x.WatchTimeMinutes == 0));
        db.ThumbnailPerformance.RemoveRange(existingThumbRows.Where(x => x.Views == 0 && x.Impressions == 0 && x.Likes == 0 && x.Comments == 0 && x.Shares == 0 && x.WatchTimeMinutes == 0));
        await db.SaveChangesAsync(ct);
    }
    else
    {
        recordsSkipped += await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
        recordsSkipped += await db.HookPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
        recordsSkipped += await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    }

    var beforeVideoCount = await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforePlatformContentCount = await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforeHookCount = await db.HookPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforeThumbCount = await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);

    await analytics.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
        pipelineRunId, run.Language, run.RegionId, DateTimeOffset.UtcNow, platforms,
        hooks.Length > 0 ? hooks : ["Astronomy tonight"], thumbs,
        run.ContentType.ToString(), run.YouTubeVideoId, null), ct);

    var afterVideoCount = await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var afterHookCount = await db.HookPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var afterThumbCount = await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var afterPlatformContentCount = await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var platformRowsCreated = afterPlatformContentCount - beforePlatformContentCount;
    var videoAnalyticsRowsCreated = afterVideoCount - beforeVideoCount;
    var hookRowsCreated = afterHookCount - beforeHookCount;
    var thumbnailRowsCreated = afterThumbCount - beforeThumbCount;
    var warnings = new List<string>();
    var zeroReasons = new List<string>();
    if (platformRowsCreated == 0) zeroReasons.Add(beforePlatformContentCount > 0 ? "platform_content_analytics: Skipped because records already existed" : "platform_content_analytics: No rows were created");
    if (hookRowsCreated == 0) zeroReasons.Add(beforeHookCount > 0 ? "hook_performance: Skipped because records already existed" : "hook_performance: No rows were created");
    if (thumbnailRowsCreated == 0) zeroReasons.Add(thumbs.Count == 0 ? "thumbnail_performance: Skipped because thumbnail files missing" : (beforeThumbCount > 0 ? "thumbnail_performance: Skipped because records already existed" : "thumbnail_performance: No rows were created"));
    if (platformRowsCreated + hookRowsCreated + thumbnailRowsCreated + videoAnalyticsRowsCreated == 0)
    {
        warnings.AddRange(zeroReasons);
        logger.LogWarning("No analytics rows were created for pipeline run {PipelineRunId}", pipelineRunId);
    }
    logger.LogInformation("Analytics rows created for pipeline run {PipelineRunId}: platform={PlatformRowsCreated}, hook={HookRowsCreated}, thumbnail={ThumbnailRowsCreated}", pipelineRunId, platformRowsCreated, hookRowsCreated, thumbnailRowsCreated);
    logger.LogInformation("SaveChanges completed for analytics initialization of pipeline run {PipelineRunId}", pipelineRunId);

    var reportPath = Path.Combine(outputDirectory, "analytics-initialization-report.json");
    var thumbnailPerformanceTableValidated = thumbnailRowsCreated > 0 || beforeThumbCount > 0 || thumbs.Count == 0;
    var schemaMismatchDetected = thumbnailRowsCreated == 0 && thumbs.Count > 0 && beforeThumbCount == 0;
    var migrationApplied = !schemaMismatchDetected;
    await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(new
    {
        pipelineRunId,
        canonicalVideoAnalyticsTable = "platform_content_analytics",
        tablesTargeted = new[] { "platform_content_analytics", "hook_performance", "thumbnail_performance", "platform_video_analytics" },
        recordsCreated = new { platformRowsCreated, hookRowsCreated, thumbnailRowsCreated, videoAnalyticsRowsCreated },
        recordsSkipped = new { platformRowsSkipped = beforePlatformContentCount > 0 ? beforePlatformContentCount : 0, hookRowsSkipped = beforeHookCount > 0 ? beforeHookCount : 0, thumbnailRowsSkipped = beforeThumbCount > 0 ? beforeThumbCount : 0, videoAnalyticsRowsSkipped = beforeVideoCount > 0 ? beforeVideoCount : 0 },
        thumbnailPathsDetected = thumbs.Select(x => x.ThumbnailPath).ToArray(),
        missingThumbnailPaths = new[] { longThumb, shortThumb }.Where(x => !File.Exists(x)).ToArray(),
        platformRowsCreated,
        hookRowsCreated,
        thumbnailRowsCreated,
        videoAnalyticsRowsCreated,
        thumbnailPerformanceTableValidated,
        schemaMismatchDetected,
        migrationApplied,
        saveChangesSucceeded = true,
        warnings,
        reasons = zeroReasons,
        errors = Array.Empty<string>()
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);

    return Results.Ok(new { pipelineRunId, analyticsInitialized = true, canonicalVideoAnalyticsTable = "platform_content_analytics", platformRowsCreated, hookRowsCreated, thumbnailRowsCreated, videoAnalyticsRowsCreated, recordsSkipped, warnings, reasons = zeroReasons, outputReportPath = reportPath });
});

app.MapPost("/api/intelligence/backfill/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? force, HttpContext http, CancellationToken ct) =>
{
    var repository = http.RequestServices.GetRequiredService<IPipelineRepository>();
    var db = http.RequestServices.GetRequiredService<MediaFactoryDbContext>();
    var analytics = http.RequestServices.GetRequiredService<IAnalyticsIngestionService>();
    var aiPipeline = http.RequestServices.GetRequiredService<IAIOptimizationPipelineService>();
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null) return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });
    var outputDirectory = run.OutputFolder ?? string.Empty;
    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} output folder was not found.", outputDirectory });

    var aiResult = await aiPipeline.RunForPipelineAsync(new AIOptimizationPipelineRequest(
        pipelineRunId, outputDirectory, run.Language, run.RegionId, DateOnly.FromDateTime(run.CreatedUtc.UtcDateTime), run.LocationName, run.EventTitle ?? "Astronomy tonight", run.EventTitle ?? "Astronomy tonight", Array.Empty<string>(), null, null, run.EventType ?? run.ContentType.ToString()), ct);

    var script = await db.GeneratedScripts.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
    var longThumb = Path.Combine(outputDirectory, "thumbnail-long.jpg");
    var fallbackLongThumb = Path.Combine(outputDirectory, "thumbnail.jpg");
    var shortThumb = Path.Combine(outputDirectory, "thumbnail-short.jpg");
    var thumbs = new List<AnalyticsThumbnailSeed>();
    if (File.Exists(longThumb)) thumbs.Add(new AnalyticsThumbnailSeed(longThumb, "Long"));
    else if (File.Exists(fallbackLongThumb)) thumbs.Add(new AnalyticsThumbnailSeed(fallbackLongThumb, "Long"));
    if (File.Exists(shortThumb)) thumbs.Add(new AnalyticsThumbnailSeed(shortThumb, "Short"));
    var hooks = new[] { script?.HookLine, run.EventTitle }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var platforms = new[] { "YouTube-Long", "YouTube-Short", "Facebook-Long", "Facebook-Reel", "Instagram-Reel" };
    var beforeVideoCount = await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforePlatformContentCount = await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforeHookCount = await db.HookPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    var beforeThumbCount = await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct);
    await analytics.InitializeForPipelineRunAsync(new AnalyticsPipelineInitializationRequest(
        pipelineRunId, run.Language, run.RegionId, DateTimeOffset.UtcNow, platforms, hooks.Length > 0 ? hooks : ["Astronomy tonight"], thumbs, run.ContentType.ToString(), run.YouTubeVideoId, null), ct);
    var videoAnalyticsRowsCreated = await db.PlatformVideoAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct) - beforeVideoCount;
    var platformRowsCreated = await db.PlatformContentAnalytics.CountAsync(x => x.PipelineRunId == pipelineRunId, ct) - beforePlatformContentCount;
    var hookRowsCreated = await db.HookPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct) - beforeHookCount;
    var thumbnailRowsCreated = await db.ThumbnailPerformance.CountAsync(x => x.PipelineRunId == pipelineRunId, ct) - beforeThumbCount;
    var warnings = aiResult.Errors.ToList();
    var zeroReasons = new List<string>();
    if (platformRowsCreated == 0) zeroReasons.Add(beforePlatformContentCount > 0 ? "platform_content_analytics: Skipped because records already existed" : "platform_content_analytics: No rows were created");
    if (hookRowsCreated == 0) zeroReasons.Add(beforeHookCount > 0 ? "hook_performance: Skipped because records already existed" : "hook_performance: No rows were created");
    if (thumbnailRowsCreated == 0) zeroReasons.Add(thumbs.Count == 0 ? "thumbnail_performance: Skipped because thumbnail files missing" : (beforeThumbCount > 0 ? "thumbnail_performance: Skipped because records already existed" : "thumbnail_performance: No rows were created"));
    if (platformRowsCreated + hookRowsCreated + thumbnailRowsCreated + videoAnalyticsRowsCreated == 0) warnings.AddRange(zeroReasons);
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "analytics-initialization-report.json"), System.Text.Json.JsonSerializer.Serialize(new { pipelineRunId, canonicalVideoAnalyticsTable = "platform_content_analytics", tablesTargeted = new[] { "platform_content_analytics", "hook_performance", "thumbnail_performance", "platform_video_analytics" }, recordsCreated = new { platformRowsCreated, hookRowsCreated, thumbnailRowsCreated, videoAnalyticsRowsCreated }, recordsSkipped = new { platformRowsSkipped = beforePlatformContentCount > 0 ? beforePlatformContentCount : 0, hookRowsSkipped = beforeHookCount > 0 ? beforeHookCount : 0, thumbnailRowsSkipped = beforeThumbCount > 0 ? beforeThumbCount : 0, videoAnalyticsRowsSkipped = beforeVideoCount > 0 ? beforeVideoCount : 0 }, thumbnailPathsDetected = thumbs.Select(x => x.ThumbnailPath).ToArray(), missingThumbnailPaths = new[] { longThumb, shortThumb }.Where(x => !File.Exists(x)).ToArray(), saveChangesSucceeded = true, warnings, reasons = zeroReasons, errors = Array.Empty<string>() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
    var reportPath = Path.Combine(outputDirectory, "pipeline-intelligence-hook-report.json");
    await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(new { pipelineRunId, aiOptimizationExecuted = aiResult.Executed, hookRecordsCreated = aiResult.HookRecordsCreated, analyticsInitialized = true, canonicalVideoAnalyticsTable = "platform_content_analytics", analytics = new { platformRowsCreated, hookRowsCreated, thumbnailRowsCreated, videoAnalyticsRowsCreated }, recordsSkipped = beforePlatformContentCount + beforeHookCount + beforeThumbCount + beforeVideoCount, warnings, reasons = zeroReasons, outputReportPath = reportPath }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
    return Results.Ok(new { pipelineRunId, aiOptimizationExecuted = aiResult.Executed, hookRecordsCreated = aiResult.HookRecordsCreated, analyticsInitialized = true, canonicalVideoAnalyticsTable = "platform_content_analytics", platformRowsCreated, hookRowsCreated, thumbnailRowsCreated, videoAnalyticsRowsCreated, recordsSkipped = beforePlatformContentCount + beforeHookCount + beforeThumbCount + beforeVideoCount, warnings, reasons = zeroReasons, outputReportPath = reportPath });
});
app.MapPost("/api/pipelines/run", async (RunPipelineRequest request, PipelineOrchestrator orchestrator, IPipelineRecoveryService recoveryService, ILogger<Program> logger, CancellationToken ct) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["contentType"] = request.ContentType,
        ["runDate"] = request.Date,
        ["regionId"] = request.RegionId ?? "",
        ["publishToYouTube"] = request.PublishToYouTube
    });
    var result = await orchestrator.RunAsync(request, ct);
    if (result.Status is PipelineRunStatus.PublishFailed or PipelineRunStatus.CompletedWithPublishErrors)
    {
        var status = await recoveryService.GetStatusAsync(result.Id, ct);
        var failedStages = status?.Stages
            .Where(s => s.Status.Equals(PersistentStageStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                && (s.StageName.Equals(PipelineStageNames.YouTubeLongPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.YouTubeShortPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.FacebookLongPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.FacebookReelPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.InstagramReelPublished, StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.StageName)
            .ToArray() ?? [];

        return Results.Ok(new RunPipelineExecutionResponse(
            result.Id,
            result.Status,
            "Succeeded",
            "Failed",
            failedStages,
            $"/api/pipeline/resume/{result.Id}",
            $"/api/pipeline/retry-publish/{result.Id}?platform=youtube",
            "Generation completed, but one or more publish stages failed."));
    }

    return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Completed."));
});

app.MapGet("/api/pipeline/status/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? includeInternal, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.GetStatusAsync(pipelineRunId, ct, includeInternal ?? false);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});

app.MapGet("/api/pipeline/{runId:guid}/thumbnail-publish-status", async (Guid runId, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(runId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {runId} was not found." });
    }

    var outputDirectory = run.OutputFolder;
    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
    {
        return Results.NotFound(new { message = $"Pipeline run {runId} output folder was not found.", outputDirectory });
    }

    async Task<JsonNode?> ReadJsonAsync(string fileName)
    {
        var path = Path.Combine(outputDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonNode.Parse(await File.ReadAllTextAsync(path, ct));
    }

    var report = await ReadJsonAsync("platform-thumbnail-resolution-report.json");
    var assetsReport = await ReadJsonAsync("platform-publishing-assets-report.json");
    var youtubeLong = await ReadJsonAsync("youtube-publish-result-long.json");
    var youtubeShort = await ReadJsonAsync("youtube-publish-result-short.json");
    var facebookLong = await ReadJsonAsync("facebook-long-publish-result.json");
    var facebook = await ReadJsonAsync("facebook-reel-publish-result.json");
    var instagram = await ReadJsonAsync("instagram-reel-publish-result.json");
    var facebookThumb = await ReadJsonAsync("facebook-thumbnail-upload-diagnostics.json");
    var instagramThumb = await ReadJsonAsync("instagram-thumbnail-upload-diagnostics.json");
    var youtubeThumb = await ReadJsonAsync("youtube-thumbnail-upload-diagnostics.json");
    var youtubeShortThumb = await ReadJsonAsync("youtube-short-thumbnail-upload-diagnostics.json");

    return Results.Ok(new
    {
        runId,
        localGeneratedThumbnails = new
        {
            longPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-long.jpg"),
            shortPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-short.jpg")
        },
        perPlatformResolution = report,
        publishingAssets = assetsReport,
        publishResults = new { youtubeLong, youtubeShort, facebookLong, facebook, instagram },
        thumbnailDiagnostics = new { youtube = youtubeThumb, youtubeShort = youtubeShortThumb, facebook = facebookThumb, instagram = instagramThumb }
    });
});

app.MapPost("/api/weekly-skyforecast-v2/run-end-to-end", async (WeeklySkyForecastV2EndToEndRunRequest request, HttpContext httpContext, ILogger<Program> logger, CancellationToken ct) =>
{
    var pipelineRunId = Guid.NewGuid();
    var reports = new WeeklySkyForecastV2EndToEndReports(null, null, null, null, null, null);
    var warnings = new List<string>();
    var errors = new List<string>();
    WeeklySkyForecastV2GenerateWeeklyScenesResponse? sceneResponse = null;
    WeeklySkyForecastAudioGenerationResponse? audioResponse = null;
    WeeklyVisualIntentBuildResponse? visualIntentResponse = null;
    WeeklyAudioDrivenTimelineReconciliationResponse? timelineResponse = null;
    WeeklyExistingRunRenderResponse? renderResponse = null;

    try
    {
        var weekStartDate = DateOnly.Parse(request.WeekStartDate, CultureInfo.InvariantCulture);
        var scheduledUtc = new DateTimeOffset(weekStartDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var baseUri = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
        using var client = new HttpClient { BaseAddress = baseUri, Timeout = Timeout.InfiniteTimeSpan };

        var sceneRequest = new WeeklySkyForecastV2GenerateWeeklyScenesRequest(
            "WeeklySkyForecast",
            request.Language,
            request.RegionId,
            request.LocationName,
            scheduledUtc,
            weekStartDate,
            Diagnostics: true,
            ContinueOnFailure: false,
            PipelineRunId: pipelineRunId);
        var sceneResult = await PostJsonStageAsync<WeeklySkyForecastV2GenerateWeeklyScenesResponse>(client, "/api/weekly-skyforecast-v2/generate-weekly-scenes", sceneRequest, "generateWeeklyScenes", ct);
        if (!sceneResult.Success)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "generateWeeklyScenes", reports, warnings, sceneResult.Errors));
        sceneResponse = sceneResult.Value!;
        pipelineRunId = sceneResponse.PipelineRunId;
        warnings.AddRange(sceneResponse.Warnings ?? []);
        reports = reports with { SceneGenerationReportPath = sceneResponse.WeeklyVideoReadinessReportPath ?? sceneResponse.ScenePlanPath };
        if (!sceneResponse.WeeklyScenesReady)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "generateWeeklyScenes", reports, warnings, ["Weekly scene generation completed but WeeklyScenesReady was false."]));

        if (request.GenerateAudio)
        {
            var audioRequest = new WeeklySkyForecastAudioGenerationRequest(
                request.GenerateLongform,
                request.GenerateShortform,
                request.OverwriteExisting,
                DryRun: false,
                VoiceName: null,
                AudioFormat: "mp3",
                Language: request.Language);
            var audioResult = await PostJsonStageAsync<WeeklySkyForecastAudioGenerationResponse>(client, $"/api/weekly-skyforecast-v2/runs/{pipelineRunId}/generate-audio", audioRequest, "generateAudio", ct);
            if (!audioResult.Success)
                return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "generateAudio", reports, warnings, audioResult.Errors));
            audioResponse = audioResult.Value!;
            warnings.AddRange(audioResponse.Warnings ?? []);
            reports = reports with { AudioGenerationReportPath = audioResponse.AudioGenerationReportPath };
            if (!audioResponse.AudioGenerationReady)
                return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "generateAudio", reports, warnings, audioResponse.Errors.Count > 0 ? audioResponse.Errors : ["Audio generation completed but AudioGenerationReady was false."]));
        }

        var visualResult = await PostJsonStageAsync<WeeklyVisualIntentBuildResponse>(client, $"/api/weekly-skyforecast-v2/runs/{pipelineRunId}/build-visual-intent-plan", new { }, "buildVisualIntentPlan", ct);
        if (!visualResult.Success)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "buildVisualIntentPlan", reports, warnings, visualResult.Errors));
        visualIntentResponse = visualResult.Value!;
        warnings.AddRange(visualIntentResponse.Warnings ?? []);
        var renderSafeValidationReportPath = Path.Combine(visualIntentResponse.ResolvedPipelineRunRoot, "render", "visual-intent-render-safe-validation-report.json");
        reports = reports with
        {
            VisualIntentValidationReportPath = visualIntentResponse.VisualIntentValidationReportPath,
            VisualIntentRenderSafeValidationReportPath = renderSafeValidationReportPath
        };
        if (!visualIntentResponse.VisualIntentReady || !visualIntentResponse.RenderSafeShotPlanReady)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "buildVisualIntentPlan", reports, warnings, visualIntentResponse.Errors.Count > 0 ? visualIntentResponse.Errors : ["Visual intent or render-safe shot plan was not ready."]));

        var timelineRequest = new WeeklyAudioDrivenTimelineReconciliationRequest(
            request.GenerateLongform,
            request.GenerateShortform,
            OverwriteExisting: true,
            DryRun: false);
        var timelineResult = await PostJsonStageAsync<WeeklyAudioDrivenTimelineReconciliationResponse>(client, $"/api/weekly-skyforecast-v2/runs/{pipelineRunId}/reconcile-timeline-from-audio", timelineRequest, "reconcileAudioDrivenTimeline", ct);
        if (!timelineResult.Success)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "reconcileAudioDrivenTimeline", reports, warnings, timelineResult.Errors));
        timelineResponse = timelineResult.Value!;
        warnings.AddRange(timelineResponse.Warnings ?? []);
        if (!timelineResponse.AudioDrivenTimelineReady)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "reconcileAudioDrivenTimeline", reports, warnings, timelineResponse.Errors.Count > 0 ? timelineResponse.Errors : ["Audio-driven timeline reconciliation completed but AudioDrivenTimelineReady was false."]));

        var renderRequest = new WeeklyExistingRunRenderRequest(
            request.GenerateLongform,
            request.GenerateShortform,
            OverwriteExisting: true,
            DryRun: false,
            DebugStoryboard: false,
            AllowSilent: !request.GenerateAudio,
            UseStagedRendering: true,
            UseAudioDrivenTimeline: true,
            MergeAudio: true,
            UseVisualIntentPlan: true);
        var renderResult = await PostJsonStageAsync<WeeklyExistingRunRenderResponse>(client, $"/api/weekly-skyforecast-v2/runs/{pipelineRunId}/render-video", renderRequest, "renderVideo", ct);
        if (!renderResult.Success)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "renderVideo", reports, warnings, renderResult.Errors));
        renderResponse = renderResult.Value!;
        warnings.AddRange(renderResponse.Warnings ?? []);
        reports = reports with
        {
            RenderQualityReportPath = renderResponse.RenderQualityReportPath,
            FinalRenderReportPath = renderResponse.FinalRenderReportPath
        };
        if (!renderResponse.RenderVideoReady || !renderResponse.FinalVideoRenderReady)
            return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "renderVideo", reports, warnings, renderResponse.Errors.Count > 0 ? renderResponse.Errors : ["Render completed but final video was not ready."]));

        var shortformVisualClipCount = Math.Max(1, renderResponse.ShortformClipCount);
        var shortformSmartCropRatioPassed = renderResponse.ShortformSmartCropLayoutCount >= Math.Ceiling(shortformVisualClipCount * 0.80d);
        var shortformFullFrameCoveragePassed = shortformSmartCropRatioPassed && renderResponse.ShortformContainLayoutCount <= 1 && renderResponse.ShortformCroppedTextRiskCount == 0;
        var shortformVisualProfessionalReady = shortformFullFrameCoveragePassed
            && visualIntentResponse.MotionGraphicStandaloneShotCount == 0
            && visualIntentResponse.EducationalOverlayStandaloneShotCount == 0
            && renderResponse.ShortformVerticalLayoutPassed
            && renderResponse.ShortformSafeAreaPassed;
        var longformVisualProfessionalReady = renderResponse.LongformPacingPassed
            && renderResponse.MaxLongformShotDurationSeconds <= 12
            && renderResponse.LongformSameFamilyConsecutiveMax <= 2;

        return Results.Ok(new WeeklySkyForecastV2EndToEndRunResponse(
            pipelineRunId,
            true,
            request.RegionId,
            request.LocationName,
            request.WeekStartDate,
            true,
            audioResponse?.AudioGenerationReady ?? !request.GenerateAudio,
            visualIntentResponse.VisualIntentReady,
            visualIntentResponse.RenderSafeShotPlanReady,
            timelineResponse.AudioDrivenTimelineReady,
            renderResponse.RenderVideoReady,
            renderResponse.AudioVideoMergeReady,
            renderResponse.LongformFinalVideoPath,
            renderResponse.ShortformFinalVideoPath,
            shortformVisualProfessionalReady,
            longformVisualProfessionalReady,
            reports,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            errors,
            null,
            renderResponse.ShortformSmartCropLayoutCount,
            renderResponse.ShortformContainLayoutCount,
            renderResponse.ShortformCroppedTextRiskCount,
            shortformFullFrameCoveragePassed,
            visualIntentResponse.MotionGraphicStandaloneShotCount,
            visualIntentResponse.EducationalOverlayStandaloneShotCount));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "WEEKLY_END_TO_END_FAILED pipelineRunId={PipelineRunId}", pipelineRunId);
        errors.Add(ex.Message);
        return Results.BadRequest(BuildWeeklyEndToEndFailure(request, pipelineRunId, "unhandledException", reports, warnings, errors));
    }
});

app.MapPost("/api/weekly-skyforecast-v2/runs/{pipelineRunId:guid}/build-visual-intent-plan", async (Guid pipelineRunId, IWeeklyVisualIntentEngine visualIntentEngine, CancellationToken ct) =>
{
    try
    {
        var result = await visualIntentEngine.BuildAsync(pipelineRunId, ct);
        return result.Errors.Count > 0 ? Results.BadRequest(result) : Results.Ok(result);
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { pipelineRunId, message = ex.Message });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { pipelineRunId, message = ex.Message });
    }
});

app.MapPost("/api/pipeline/resume/{pipelineRunId:guid}", async (Guid pipelineRunId, string? forceStage, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.ResumeAsync(pipelineRunId, forceStage, ct);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});
app.MapPost("/api/pipeline/retry-publish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? platform, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.RetryPublishAsync(pipelineRunId, platform ?? "all", ct);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});
app.MapPost("/api/youtubepublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IContentPublishService publishService, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });
    }

    var results = await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct);
    return results.Count == 0 ? Results.BadRequest(new { message = "No publishing result was produced." }) : Results.Ok(results);
});
app.MapPost("/api/metapublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IMetaPublishService publishService, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });
    }

    if (run.Status != PipelineRunStatus.Succeeded)
    {
        return Results.BadRequest(new { message = $"Pipeline run {pipelineRunId} is not completed." });
    }

    var results = await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct);
    return results.Count == 0 ? Results.BadRequest(new { message = "No Meta publishing result was produced." }) : Results.Ok(results);
});
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

app.MapGet("/api/platform-publications/recent", async (int? take, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentPlatformPublicationRecordsAsync(take ?? 20, ct)));
app.MapGet("/api/platform-publications/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetPlatformPublicationRecordAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.MapGet("/api/platform-publications/by-short/{shortId:guid}", async (Guid shortId, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetPlatformPublicationRecordsByShortIdAsync(shortId, ct)));

app.MapGet("/api/ops/dashboard", async (IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetDashboardAsync(ct)));
app.MapGet("/api/ops/runs", async (DateOnly? date, string? status, IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetRunsAsync(date, status ?? "all", ct)));
app.MapGet("/api/ops/run/{pipelineRunId:guid}", async (Guid pipelineRunId, IOpsDashboardService dashboardService, CancellationToken ct) =>
{
    var run = await dashboardService.GetRunAsync(pipelineRunId, ct);
    return run is null ? Results.NotFound() : Results.Ok(run);
});
app.MapGet("/api/ops/failures", async (int? days, IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetFailuresAsync(days ?? 7, ct)));
app.MapGet("/api/ops/summary", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetSummaryAsync(ct)));
app.MapGet("/api/ops/pipelines/recent", async (int? take, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetRecentPipelinesAsync(take ?? 20, ct)));
app.MapGet("/api/ops/pipelines/{id:guid}/stages", async (Guid id, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetPipelineStagesAsync(id, ct)));
app.MapGet("/api/ops/failures/recent", async (int? take, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetRecentFailuresAsync(take ?? 20, ct)));
app.MapGet("/api/ops/jobs/summary", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetJobSummaryAsync(ct)));

app.MapPost("/api/ops/runs/{id:guid}/replay", async (Guid id, ReplayPipelineRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.ReplayRunAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/retry-publish", async (Guid id, RetryPublishRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RetryPublishAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/retry-archive", async (Guid id, RetryArchiveRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RetryArchiveAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/regenerate-shorts", async (Guid id, RegenerateShortsRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RegenerateShortsAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/rerun-metadata", async (Guid id, RerunMetadataOptimizationRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RerunMetadataOptimizationAsync(id, request, ct)));
app.MapPost("/api/ops/jobs/{id:guid}/requeue", async (Guid id, RequeueJobRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RequeueJobAsync(id, request, ct)));
app.MapPost("/api/ops/jobs/recover-stale", async (RecoverStaleJobsRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RecoverStaleJobsAsync(request, ct)));
app.MapPost("/api/ops/maintenance/cleanup", async (CleanupMaintenanceRequest request, IMaintenanceService maintenanceService, CancellationToken ct) =>
    await ExecuteOpsAsync(() => maintenanceService.CleanupAsync(request, ct)));

app.MapGet("/api/topics/recommended", async (DateOnly? date, ContentType? contentType, string? locationName, string? timeZone, ITopicSelectionService topicSelectionService, CancellationToken ct) =>
{
    var request = new TopicSelectionRequest
    {
        Date = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
        ContentType = contentType,
        LocationName = string.IsNullOrWhiteSpace(locationName) ? "Udaipur, India" : locationName,
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "Asia/Kolkata" : timeZone,
        MaxCandidates = 8
    };

    var plan = await topicSelectionService.BuildPlanAsync(request, ct);
    return Results.Ok(plan);
});
app.MapPost("/api/topics/plan", async (TopicSelectionRequest request, ITopicSelectionService topicSelectionService, CancellationToken ct) =>
    Results.Ok(await topicSelectionService.BuildPlanAsync(request, ct)));
app.MapPost("/api/prompts/feedback-preview", async (PromptFeedbackRequest request, IPromptFeedbackService promptFeedbackService, CancellationToken ct) =>
    Results.Ok(await promptFeedbackService.BuildContextAsync(request, ct)));
app.MapGet("/api/experiments/recent", async (int? take, IContentExperimentService experimentService, CancellationToken ct) =>
    Results.Ok(await experimentService.GetRecentExperimentsAsync(take ?? 20, ct)));
app.MapGet("/api/experiments/{id:guid}", async (Guid id, IContentExperimentService experimentService, CancellationToken ct) =>
{
    var experiment = await experimentService.GetExperimentAsync(id, ct);
    return experiment is null ? Results.NotFound() : Results.Ok(experiment);
});
app.MapGet("/api/experiments/top-performing", async (int? take, IContentExperimentService experimentService, CancellationToken ct) =>
    Results.Ok(await experimentService.GetTopPerformingExperimentsAsync(take ?? 10, ct)));

app.MapGet("/api/analytics/recent", async (int? days, string? platform, string? location, string? contentType, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPlatformContentAnalyticsAsync(new PlatformAnalyticsQuery(days ?? 14, platform, location, contentType, 100), ct)));
app.MapGet("/api/analytics/platform/{platform}", async (string platform, int? days, string? location, string? contentType, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPlatformContentAnalyticsAsync(new PlatformAnalyticsQuery(days ?? 14, platform, location, contentType, 100), ct)));
app.MapGet("/api/analytics/run/{pipelineRunId:guid}", async (Guid pipelineRunId, IPipelineRepository repository, CancellationToken ct) =>
{
    var items = await repository.GetPlatformContentAnalyticsByRunAsync(pipelineRunId, ct);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
});
app.MapPost("/api/analytics/collect-now", async (Guid? pipelineRunId, IAnalyticsCollectionService collectionService, CancellationToken ct) =>
{
    if (pipelineRunId.HasValue)
        await collectionService.CollectForPipelineRunAsync(pipelineRunId.Value, ct);
    else
        await collectionService.CollectRecentAnalyticsAsync(ct);
    return Results.Accepted();
});
app.MapGet("/api/analytics/dashboard", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.BuildDashboardAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/top-content", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetTopContentAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/insights", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetInsightsAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/platform-summary", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetPlatformSummaryAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/content-performance", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetContentPerformanceAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));


app.MapGet("/api/optimization/plan", async (string location, string? platform, IOptimizationService optimizationService, CancellationToken ct) =>
    Results.Ok(await optimizationService.BuildPlanAsync(location, platform ?? "YouTube", ct)));
app.MapPost("/api/optimization/apply-preview", async (OptimizationApplyPreviewRequest request, IOptimizationService optimizationService, CancellationToken ct) =>
{
    var plan = request.Plan ?? await optimizationService.BuildPlanAsync(request.Request.LocationName, request.Platform, ct);
    var result = await optimizationService.ApplyPlanAsync(request.Request, plan, ct);
    var changed = GetChangedFields(request.Request, result);
    return Results.Ok(new OptimizationApplyResult { OriginalRequest = request.Request, ResultRequest = result, Plan = plan, ChangedFields = changed, Mode = "Preview" });
});

app.MapGet("/api/ai-optimization/recommendations", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GetRecommendationsAsync(ct)));
app.MapPost("/api/ai-optimization/generate-now", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GenerateNowAsync(ct)));
app.MapGet("/api/ai-optimization/pending-approval", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GetPendingApprovalAsync(ct)));
app.MapPost("/api/ai-optimization/apply-approved", async Task<IResult> (AIOptimizationApplyRequest request, IAIOptimizationService service, CancellationToken ct) =>
{
    var result = await service.ApplyApprovedAsync(request, ct);
    return result.Applied ? Results.Ok(result) : Results.BadRequest(result);
});
app.MapPost("/api/ai-optimization/reject", async (AIOptimizationApplyRequest request, IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.RejectAsync(request, ct)));

app.MapGet("/api/analytics/dashboard-summary", async (int? days, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetAnalyticsDashboardSummaryAsync(days ?? 14, ct)));
app.MapGet("/api/analytics/top-performing", async (int? topN, IAnalyticsAggregationService aggregationService, CancellationToken ct) =>
{
    var take = topN.GetValueOrDefault(10);
    var summary = await aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, take, ct);
    return Results.Ok(summary);
});
app.MapGet("/api/analytics/youtube/{videoId}", async (string videoId, IPipelineRepository repository, CancellationToken ct) =>
{
    var items = await repository.GetAnalyticsByVideoIdAsync(videoId, ct);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
});

app.MapPost("/api/assets/celestial/refresh", async (ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
    Results.Ok(await ingestion.RefreshAsync(ct)));
app.MapGet("/api/assets/celestial/status", async (ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
    Results.Ok(await ingestion.GetStatusAsync(ct)));
app.MapGet("/api/assets/celestial/{objectKey}", async (string objectKey, ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
{
    var status = await ingestion.GetObjectAsync(objectKey, ct);
    return status is null ? Results.NotFound(new { message = $"Celestial object '{objectKey}' is not configured." }) : Results.Ok(status);
});

app.Run();


static async Task<WeeklyEndToEndStageResult<T>> PostJsonStageAsync<T>(HttpClient client, string path, object payload, string stage, CancellationToken cancellationToken)
{
    using var response = await client.PostAsJsonAsync(path, payload, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return new WeeklyEndToEndStageResult<T>(false, default, ExtractWeeklyStageErrors(body, stage));
    }

    try
    {
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return value is null
            ? new WeeklyEndToEndStageResult<T>(false, default, [$"{stage} returned an empty response."])
            : new WeeklyEndToEndStageResult<T>(true, value, []);
    }
    catch (JsonException ex)
    {
        return new WeeklyEndToEndStageResult<T>(false, default, [$"{stage} response could not be parsed: {ex.Message}", body]);
    }
}

static IReadOnlyList<string> ExtractWeeklyStageErrors(string body, string stage)
{
    if (string.IsNullOrWhiteSpace(body)) return [$"{stage} failed with an empty error response."];
    try
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var errors = new List<string>();
        if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            errors.AddRange(errorsElement.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : x.GetRawText()).Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        if (root.TryGetProperty("error", out var errorElement)) errors.Add(errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() ?? string.Empty : errorElement.GetRawText());
        if (root.TryGetProperty("message", out var messageElement)) errors.Add(messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() ?? string.Empty : messageElement.GetRawText());
        return errors.Count > 0 ? errors : [$"{stage} failed: {body}"];
    }
    catch (JsonException)
    {
        return [$"{stage} failed: {body}"];
    }
}

static WeeklySkyForecastV2EndToEndRunResponse BuildWeeklyEndToEndFailure(
    WeeklySkyForecastV2EndToEndRunRequest request,
    Guid pipelineRunId,
    string failedStage,
    WeeklySkyForecastV2EndToEndReports reports,
    IReadOnlyList<string> warnings,
    IReadOnlyList<string> errors)
    => new(
        pipelineRunId,
        false,
        request.RegionId,
        request.LocationName,
        request.WeekStartDate,
        !string.IsNullOrWhiteSpace(reports.SceneGenerationReportPath),
        !string.IsNullOrWhiteSpace(reports.AudioGenerationReportPath),
        !string.IsNullOrWhiteSpace(reports.VisualIntentValidationReportPath),
        !string.IsNullOrWhiteSpace(reports.VisualIntentRenderSafeValidationReportPath),
        false,
        false,
        false,
        string.Empty,
        string.Empty,
        false,
        false,
        reports,
        warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        failedStage,
        0,
        0,
        0,
        false,
        0,
        0);

static AnalyticsIntelligenceRequest BuildAnalyticsIntelligenceRequest(int? days, string? platform, string? contentType, string? location, int? limit)
    => new(days ?? 14, platform, contentType, location, limit ?? 10);

static async Task<IResult> ExecuteOpsAsync<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}

static IReadOnlyCollection<string> GetChangedFields(RunPipelineRequest original, RunPipelineRequest result)
{
    var changed = new List<string>();
    if (original.Date != result.Date) changed.Add(nameof(RunPipelineRequest.Date));
    if (original.ContentType != result.ContentType) changed.Add(nameof(RunPipelineRequest.ContentType));
    if (original.LocationName != result.LocationName) changed.Add(nameof(RunPipelineRequest.LocationName));
    if (original.TimeZone != result.TimeZone) changed.Add(nameof(RunPipelineRequest.TimeZone));
    if (original.PublishToYouTube != result.PublishToYouTube) changed.Add(nameof(RunPipelineRequest.PublishToYouTube));
    if (original.UseTopicPlanner != result.UseTopicPlanner) changed.Add(nameof(RunPipelineRequest.UseTopicPlanner));
    if (original.Latitude != result.Latitude) changed.Add(nameof(RunPipelineRequest.Latitude));
    if (original.Longitude != result.Longitude) changed.Add(nameof(RunPipelineRequest.Longitude));
    if (original.OverrideTimezone != result.OverrideTimezone) changed.Add(nameof(RunPipelineRequest.OverrideTimezone));
    if (original.OverrideLocationName != result.OverrideLocationName) changed.Add(nameof(RunPipelineRequest.OverrideLocationName));
    if (original.TargetDate != result.TargetDate) changed.Add(nameof(RunPipelineRequest.TargetDate));
    if (original.RegionId != result.RegionId) changed.Add(nameof(RunPipelineRequest.RegionId));
    return changed;
}


static DateTime ResolveSceneObservationUtc(string sceneCode, DateOnly sceneDateLocal, TimeOnly shotTimeLocal, string timezoneId, IReadOnlyList<string> targetObjectCodes, IReadOnlyList<WeeklyAstronomyEvent> extractedEvents, DateTime? planBestTimeUtc, DateTime? scheduledUtcFallback, Microsoft.Extensions.Logging.ILogger logger)
{
    if (TryResolveFromSkyfieldEvent(sceneCode, timezoneId, extractedEvents, targetObjectCodes, out var skyfieldResolved, out var bestDateLocal, out var bestTimeLocal))
    {
        var local = ConvertUtcToLocal(skyfieldResolved, timezoneId);
        logger.LogInformation("SSC_TIME_RESOLUTION sceneCode={SceneCode} source=SkyfieldBestTime bestDateLocal={BestDateLocal} bestTimeLocal={BestTimeLocal} timezone={Timezone} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal}", sceneCode, bestDateLocal, bestTimeLocal, timezoneId, skyfieldResolved, local);
        return skyfieldResolved;
    }

    if (planBestTimeUtc.HasValue && planBestTimeUtc.Value != default)
    {
        var resolved = DateTime.SpecifyKind(planBestTimeUtc.Value, DateTimeKind.Utc);
        var local = ConvertUtcToLocal(resolved, timezoneId);
        logger.LogInformation("SSC_TIME_RESOLUTION sceneCode={SceneCode} source=ScenePlanBestTime bestDateLocal={BestDateLocal} bestTimeLocal={BestTimeLocal} timezone={Timezone} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal}", sceneCode, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), timezoneId, resolved, local);
        return resolved;
    }

    logger.LogError("SSC_TIME_RESOLUTION sceneCode={SceneCode} source=FallbackRejected bestDateLocal={BestDateLocal} bestTimeLocal={BestTimeLocal} timezone={Timezone} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal}", sceneCode, sceneDateLocal, shotTimeLocal, timezoneId, default(DateTime), default(DateTime));
    throw new InvalidOperationException($"No valid SSC time source for scene '{sceneCode}'.");
}

static bool TryResolveFromSkyfieldEvent(string sceneCode, string timezoneId, IReadOnlyList<WeeklyAstronomyEvent> extractedEvents, IReadOnlyList<string> targetObjectCodes, out DateTime selectedObservationUtc, out DateOnly bestDateLocal, out TimeOnly bestTimeLocal)
{
    selectedObservationUtc = default;
    bestDateLocal = default;
    bestTimeLocal = default;
    var targetSet = new HashSet<string>(targetObjectCodes.Select(NormalizeWeeklyObjectName), StringComparer.OrdinalIgnoreCase);
    var candidates = extractedEvents
        .Where(e => e.BestDateLocal.HasValue && e.BestTimeLocal.HasValue)
        .Where(e => e.Objects.Any(o => targetSet.Contains(NormalizeWeeklyObjectName(o.ObjectCode)) || targetSet.Contains(NormalizeWeeklyObjectName(o.ObjectName))))
        .ToList();

    WeeklyAstronomyEvent? selected = null;
    if (sceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase))
    {
        selected = candidates.FirstOrDefault(e => targetSet.Contains("venus") && targetSet.Contains("jupiter") && e.Objects.Any(o => NormalizeWeeklyObjectName(o.ObjectCode)=="venus") && e.Objects.Any(o => NormalizeWeeklyObjectName(o.ObjectCode)=="jupiter"));
    }
    else if (sceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase))
    {
        selected = candidates.FirstOrDefault(e => e.Objects.Any(o => NormalizeWeeklyObjectName(o.ObjectCode)=="moon" || NormalizeWeeklyObjectName(o.ObjectName)=="moon"));
    }

    selected ??= candidates
        .OrderByDescending(e => e.ImportanceScore)
        .ThenByDescending(e => e.VisibilityScore)
        .ThenByDescending(e => e.RarityScore)
        .FirstOrDefault();
    if (selected is null) return false;

    bestDateLocal = selected.BestDateLocal!.Value;
    bestTimeLocal = selected.BestTimeLocal!.Value;
    var tz = string.IsNullOrWhiteSpace(timezoneId) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    var local = DateTime.SpecifyKind(bestDateLocal.ToDateTime(bestTimeLocal), DateTimeKind.Unspecified);
    selectedObservationUtc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
    return true;
}

static DateTime ConvertUtcToLocal(DateTime utc, string timezoneId)
{
    try
    {
        var tz = string.IsNullOrWhiteSpace(timezoneId) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
    }
    catch
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }
}

static string ResolveObjectType(string objectName)
{
    var key = objectName.Trim().ToLowerInvariant();
    return key switch
    {
        "moon" => "Moon",
        "venus" or "jupiter" or "saturn" or "mars" or "mercury" => "Planet",
        _ => "StarOrDeepSky"
    };
}

static double ResolveObjectWeight(string objectName, string objectType, bool isPrimaryTarget)
{
    var key = objectName.Trim().ToLowerInvariant();
    if (key == "moon") return isPrimaryTarget ? 3.2d : 1.1d;
    if (key == "venus") return isPrimaryTarget ? 2.7d : 1.05d;
    if (key == "jupiter") return isPrimaryTarget ? 2.4d : 1.0d;
    if (key is "saturn" or "mars" or "mercury") return isPrimaryTarget ? 2.2d : 1.0d;
    if (objectType.Equals("starordeepsky", StringComparison.OrdinalIgnoreCase) && key is "sirius" or "canopus" or "arcturus" or "vega" or "capella" or "rigel" or "procyon" or "betelgeuse" or "achernar" or "aldebaran" or "antares" or "spica" or "pollux") return isPrimaryTarget ? 1.3d : 0.85d;
    if (key.Contains("constellation", StringComparison.OrdinalIgnoreCase) || key.Contains("nebula", StringComparison.OrdinalIgnoreCase)) return isPrimaryTarget ? 0.8d : 0.45d;
    return isPrimaryTarget ? 1.1d : 0.8d;
}

static List<string> ResolveSceneSpecificObjectCodes(dynamic shot, dynamic composition, WeeklyScenePlan? scenePlan, WeeklySkyForecastV2IntelligenceResponse weeklyContext)
{
    var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var targetObjects = (composition.TargetObjects as IReadOnlyList<string> ?? Array.Empty<string>())
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Select(c => c.Trim())
        .ToList();
    foreach (var c in targetObjects) codes.Add(c);
    var includedObjects = (composition.IncludedObjects as IReadOnlyList<string> ?? Array.Empty<string>())
        .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals("SKY", StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Trim())
        .ToList();
    if (scenePlan is not null)
    {
        foreach (var c in scenePlan.ObjectCodes ?? Array.Empty<string>()) if (!string.IsNullOrWhiteSpace(c)) codes.Add(c.Trim());
    }
    else
    {
        foreach (var c in includedObjects) codes.Add(c);
    }

    var sceneKeywords = string.Join(" ", new[] { shot.ShotCode as string, shot.ShotPurpose as string, scenePlan?.CompositionDescription, scenePlan?.VisualCode })
        .Split([' ', '-', '_', ',', '.', ';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(k => k.Length >= 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var ev in weeklyContext.EventExtractionResult?.ExtractedEvents ?? [])
    {
        var sameDate = ev.BestDateLocal is null || ev.BestDateLocal == shot.DateLocal;
        if (!sameDate) continue;
        foreach (var o in ev.Objects ?? [])
        {
            if (sceneKeywords.Contains(o.ObjectCode) || sceneKeywords.Contains(o.ObjectName) || targetObjects.Contains(o.ObjectCode, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(o.ObjectCode)) codes.Add(o.ObjectCode.Trim());
            }
        }
    }

    return codes.ToList();
}

static string ResolveObjectSource(string code, dynamic composition, WeeklyScenePlan? scenePlan, dynamic shot, WeeklySkyForecastV2IntelligenceResponse weeklyContext)
{
    if ((composition.TargetObjects as IReadOnlyList<string>)?.Contains(code, StringComparer.OrdinalIgnoreCase) ?? false) return "scene.targetObjects";
    if ((scenePlan?.ObjectCodes?.Contains(code, StringComparer.OrdinalIgnoreCase) ?? false)) return "scenePlan.objectCodes";
    if ((composition.IncludedObjects as IReadOnlyList<string>)?.Contains(code, StringComparer.OrdinalIgnoreCase) ?? false) return "composition.objects";
    return "skyfield.scene-date-match";
}

static Task<WeeklyMultiObjectSceneResolutionResult> ResolveMultiObjectSceneAsync(
    string sceneCode,
    IReadOnlyList<string> targetObjects,
    DateTime preferredObservationUtc,
    DateOnly sceneDateLocal,
    string timezoneId,
    IReadOnlyDictionary<string, WeeklyAstronomyEventObject> skyObjectsByCode,
    WeeklySkyForecastV2IntelligenceResponse weeklyContext,
    dynamic composition,
    WeeklyScenePlan? scenePlan,
    dynamic shot,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    var requestedObjects = targetObjects.Select(NormalizeWeeklyObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var requiredSet = requestedObjects.Select(NormalizeWeeklyObjectName).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    logger.LogInformation("MULTI_OBJECT_RESOLUTION_START sceneCode={SceneCode} targetObjects={TargetObjects} preferredObservationUtc={PreferredObservationUtc} sceneDateLocal={SceneDateLocal}", sceneCode, string.Join(",", requestedObjects), preferredObservationUtc, sceneDateLocal);

    var extractedEvents = weeklyContext.EventExtractionResult?.ExtractedEvents ?? [];
    var flattenedObjects = Astronomy.MediaFactory.Api.WeeklySkyfieldObjectHydration.BuildFlattenedTemporalObjects(
            extractedEvents,
            e => ResolveEventUtc(e),
            name => NormalizeWeeklyObjectName(name),
            logger,
            sceneCode)
        .Where(candidate => candidate.AltitudeDegrees > 5d)
        .ToList();

    var candidateTimestampsInspected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var candidate in flattenedObjects)
    {
        candidateTimestampsInspected.Add(DateTime.SpecifyKind(candidate.SnapshotUtc, DateTimeKind.Utc).ToString("O"));
    }

    var preferredLocal = ConvertUtcToLocal(DateTime.SpecifyKind(preferredObservationUtc, DateTimeKind.Utc), timezoneId);
    var sharedCandidate = FindBestSetBasedMultiObjectCandidate(flattenedObjects, requestedObjects, requiredSet, preferredObservationUtc, preferredLocal, sceneDateLocal, timezoneId, logger, sceneCode);

    if (sharedCandidate is null)
    {
        var resolvedObjects = flattenedObjects
            .Where(x => requiredSet.Contains(x.NormalizedName))
            .Select(x => NormalizeWeeklyObjectCode(x.DisplayName) ?? x.NormalizedName.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingObjects = requestedObjects.Where(x => !resolvedObjects.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        var topCandidateBuckets = BuildTopMultiObjectBucketFailureSummary(flattenedObjects, requiredSet, preferredObservationUtc, timezoneId, sceneDateLocal);
        logger.LogError(
            "MULTI_OBJECT_RESOLUTION_FAILED sceneCode={SceneCode} requestedObjects={RequestedObjects} resolvedObjects={ResolvedObjects} missingObjects={MissingObjects} candidateTimestampCount={CandidateTimestampCount} topCandidateBuckets={TopCandidateBuckets}",
            sceneCode,
            string.Join(",", requestedObjects),
            string.Join(",", resolvedObjects),
            string.Join(",", missingObjects),
            candidateTimestampsInspected.Count,
            string.Join(" | ", topCandidateBuckets));
        throw new InvalidOperationException($"Multi-object scene resolution failed for '{sceneCode}'. requestedObjects=[{string.Join(",", requestedObjects)}]; resolvedObjects=[{string.Join(",", resolvedObjects)}]; missingObjects=[{string.Join(",", missingObjects)}]; candidateTimestampCount={candidateTimestampsInspected.Count}; topCandidateBuckets=[{string.Join(" | ", topCandidateBuckets)}]");
    }

    var selectedObservationUtc = DateTime.SpecifyKind(sharedCandidate.AnchorUtc, DateTimeKind.Utc);
    var selectedObservationLocal = ConvertUtcToLocal(selectedObservationUtc, timezoneId);
    var resolvedSelections = sharedCandidate.Objects.Select(resolved =>
    {
        skyObjectsByCode.TryGetValue(resolved.RequestedCode, out var directObject);
        var objectName = directObject?.ObjectName ?? ToWeeklyObjectDisplayName(resolved.RequestedCode);
        var objectType = ResolveObjectType(objectName);
        var isPrimaryTarget = requestedObjects.Contains(resolved.RequestedCode, StringComparer.OrdinalIgnoreCase)
            || composition.TargetObjects.Contains(resolved.RequestedCode, StringComparer.OrdinalIgnoreCase)
            || (scenePlan?.ObjectCodes?.Contains(resolved.RequestedCode, StringComparer.OrdinalIgnoreCase) ?? false);
        var source = $"{ResolveObjectSource(resolved.RequestedCode, composition, scenePlan, shot, weeklyContext)}|source={sharedCandidate.MatchMode};selectedObservationUtc={selectedObservationUtc:O};matchedTimeUtc={resolved.Candidate.SnapshotUtc:O};deltaMinutes={Math.Abs((resolved.Candidate.SnapshotUtc - selectedObservationUtc).TotalMinutes):0.###}";
        logger.LogInformation("MULTI_OBJECT_RESOLUTION_OBJECT_MATCH sceneCode={SceneCode} requestedObject={RequestedObject} resolvedObject={ResolvedObject} selectedObservationUtc={SelectedObservationUtc} matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, resolved.RequestedCode, objectName, selectedObservationUtc, resolved.Candidate.SnapshotUtc, resolved.Candidate.AltitudeDegrees, resolved.Candidate.AzimuthDegrees);
        return new WeeklySceneObjectSelection(new SkyObjectPosition(objectName, resolved.Candidate.AltitudeDegrees, resolved.Candidate.AzimuthDegrees, resolved.Candidate.Magnitude ?? directObject?.Magnitude ?? 5.5d, objectType, ResolveObjectWeight(objectName, objectType, isPrimaryTarget)), source);
    }).ToList();

    var resolvedCodes = sharedCandidate.Objects.Select(x => x.RequestedCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var missingRequiredObjects = requestedObjects.Where(x => !resolvedCodes.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
    if (resolvedCodes.Count < requestedObjects.Count || missingRequiredObjects.Count > 0)
    {
        var topCandidateBuckets = BuildTopMultiObjectBucketFailureSummary(flattenedObjects, requiredSet, preferredObservationUtc, timezoneId, sceneDateLocal);
        logger.LogError("MULTI_OBJECT_RESOLUTION_FAILED sceneCode={SceneCode} requestedObjects={RequestedObjects} resolvedObjects={ResolvedObjects} missingObjects={MissingObjects} candidateTimestampCount={CandidateTimestampCount} topCandidateBuckets={TopCandidateBuckets}", sceneCode, string.Join(",", requestedObjects), string.Join(",", resolvedCodes), string.Join(",", missingRequiredObjects), candidateTimestampsInspected.Count, string.Join(" | ", topCandidateBuckets));
        throw new InvalidOperationException($"Multi-object scene resolution failed for '{sceneCode}'. requestedObjects=[{string.Join(",", requestedObjects)}]; resolvedObjects=[{string.Join(",", resolvedCodes)}]; missingObjects=[{string.Join(",", missingRequiredObjects)}]; candidateTimestampCount={candidateTimestampsInspected.Count}; topCandidateBuckets=[{string.Join(" | ", topCandidateBuckets)}]");
    }

    var selectedBucketObjectNames = sharedCandidate.BucketObjectNames.Count > 0
        ? sharedCandidate.BucketObjectNames.Select(ToWeeklyObjectDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        : sharedCandidate.Objects.Select(x => x.DisplayName.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    var angularSpread = EstimateMultiObjectAngularSpreadDeg(sharedCandidate.Objects);
    var groupingSplitRequired = angularSpread > 80d;
    IReadOnlyList<WeeklyMultiObjectSplitSceneManifestEntry> splitScenes = groupingSplitRequired
        ? resolvedCodes.Select(code => new WeeklyMultiObjectSplitSceneManifestEntry(ResolveMultiObjectSplitSceneCode(sceneCode, code), new[] { code }, new[] { code })).ToList()
        : Array.Empty<WeeklyMultiObjectSplitSceneManifestEntry>();
    var report = new WeeklyMultiObjectSceneResolutionReport(sceneCode, requestedObjects, resolvedCodes, [], selectedObservationUtc, selectedObservationLocal, true, candidateTimestampsInspected.ToList(), groupingSplitRequired, true, candidateTimestampsInspected.Count, selectedBucketObjectNames, angularSpread, !groupingSplitRequired, splitScenes);
    logger.LogInformation("MULTI_OBJECT_RESOLUTION_SUCCESS sceneCode={SceneCode} targetObjects={TargetObjects} resolvedObjects={ResolvedObjects} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal} matchMode={MatchMode}", sceneCode, string.Join(",", requestedObjects), string.Join(",", resolvedCodes), selectedObservationUtc, selectedObservationLocal, sharedCandidate.MatchMode);
    return Task.FromResult(new WeeklyMultiObjectSceneResolutionResult(sceneCode, requestedObjects, resolvedSelections, selectedObservationUtc, selectedObservationLocal, report));
}

static WeeklyMultiObjectResolutionCandidate? FindBestSetBasedMultiObjectCandidate(
    IReadOnlyList<WeeklySkyfieldObjectHydration.SkyfieldFlattenedTemporalObject> flattenedObjects,
    IReadOnlyList<string> requestedObjects,
    HashSet<string> requiredSet,
    DateTime preferredObservationUtc,
    DateTime preferredObservationLocal,
    DateOnly sceneDateLocal,
    string timezoneId,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode)
{
    if (flattenedObjects.Count == 0 || requiredSet.Count == 0) return null;

    var exact = FindBestSetBasedMultiObjectCandidateForTolerance(flattenedObjects, requestedObjects, requiredSet, preferredObservationUtc, preferredObservationLocal, sceneDateLocal, timezoneId, TimeSpan.Zero, "skyfield.set-exact", logger, sceneCode, roundAnchorToMinute: false);
    if (exact is not null) return exact;

    var roundedMinute = FindBestSetBasedMultiObjectCandidateForTolerance(flattenedObjects, requestedObjects, requiredSet, preferredObservationUtc, preferredObservationLocal, sceneDateLocal, timezoneId, TimeSpan.Zero, "skyfield.set-rounded-minute", logger, sceneCode, roundAnchorToMinute: true);
    if (roundedMinute is not null) return roundedMinute;

    foreach (var minutes in new[] { 5d, 15d, 30d, 60d })
    {
        var candidate = FindBestSetBasedMultiObjectCandidateForTolerance(flattenedObjects, requestedObjects, requiredSet, preferredObservationUtc, preferredObservationLocal, sceneDateLocal, timezoneId, TimeSpan.FromMinutes(minutes), $"skyfield.set-nearest-{minutes:0}m", logger, sceneCode, roundAnchorToMinute: false);
        if (candidate is not null) return candidate;
    }

    return null;
}

static WeeklyMultiObjectResolutionCandidate? FindBestSetBasedMultiObjectCandidateForTolerance(
    IReadOnlyList<WeeklySkyfieldObjectHydration.SkyfieldFlattenedTemporalObject> flattenedObjects,
    IReadOnlyList<string> requestedObjects,
    HashSet<string> requiredSet,
    DateTime preferredObservationUtc,
    DateTime preferredObservationLocal,
    DateOnly sceneDateLocal,
    string timezoneId,
    TimeSpan tolerance,
    string matchMode,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode,
    bool roundAnchorToMinute)
{
    var anchors = flattenedObjects
        .Select(x => DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc))
        .Select(x => roundAnchorToMinute ? new DateTime(x.Year, x.Month, x.Day, x.Hour, x.Minute, 0, DateTimeKind.Utc) : x)
        .Distinct()
        .OrderBy(x => Math.Abs((x - preferredObservationUtc).TotalMinutes))
        .ToList();

    var matches = new List<(WeeklyMultiObjectResolutionCandidate Candidate, double Score, IReadOnlyList<string> BucketObjects)>();
    foreach (var anchor in anchors)
    {
        var bucket = flattenedObjects
            .Select(x => new
            {
                Object = x,
                Utc = DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc),
                BucketUtc = roundAnchorToMinute ? new DateTime(x.SnapshotUtc.Year, x.SnapshotUtc.Month, x.SnapshotUtc.Day, x.SnapshotUtc.Hour, x.SnapshotUtc.Minute, 0, DateTimeKind.Utc) : DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc)
            })
            .Where(x => roundAnchorToMinute ? x.BucketUtc == anchor : Math.Abs((x.Utc - anchor).TotalMinutes) <= tolerance.TotalMinutes)
            .Select(x => x.Object)
            .ToList();

        var bucketObjectNames = bucket.Select(x => x.NormalizedName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var containsAllRequired = requiredSet.All(required => bucketObjectNames.Contains(required, StringComparer.OrdinalIgnoreCase));
        logger.LogInformation(
            "MULTI_OBJECT_BUCKET_CANDIDATE sceneCode={SceneCode} bucketUtc={BucketUtc} bucketObjects={BucketObjects} requiredObjects={RequiredObjects} containsAllRequired={ContainsAllRequired}",
            sceneCode,
            anchor,
            string.Join(",", bucketObjectNames.Select(ToWeeklyObjectDisplayName)),
            string.Join(",", requestedObjects),
            containsAllRequired);

        if (!containsAllRequired) continue;
        var candidateLocal = ConvertUtcToLocal(anchor, timezoneId);
        if (!IsCompatibleMultiObjectLocalWindow(candidateLocal, preferredObservationLocal, sceneDateLocal) || !IsEveningNightLocal(candidateLocal)) continue;

        var selectedObjects = new List<WeeklyResolvedTemporalObject>();
        foreach (var requestedObject in requestedObjects)
        {
            var requiredName = NormalizeWeeklyObjectName(requestedObject);
            var selected = bucket
                .Where(x => x.NormalizedName.Equals(requiredName, StringComparison.OrdinalIgnoreCase) && x.AltitudeDegrees > 5d)
                .OrderBy(x => Math.Abs((DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc) - anchor).TotalMinutes))
                .ThenByDescending(x => x.AltitudeDegrees)
                .FirstOrDefault();
            if (selected is null) break;
            selectedObjects.Add(new WeeklyResolvedTemporalObject(
                requestedObject,
                selected.NormalizedName,
                ToWeeklyObjectDisplayName(requestedObject),
                new SkyfieldTemporalCandidate(selected.NormalizedName.ToUpperInvariant(), DateTime.SpecifyKind(selected.SnapshotUtc, DateTimeKind.Utc), selected.AltitudeDegrees, selected.AzimuthDegrees, selected.Magnitude)));
        }

        if (selectedObjects.Count != requestedObjects.Count) continue;
        var maxDelta = selectedObjects.Max(x => Math.Abs((DateTime.SpecifyKind(x.Candidate.SnapshotUtc, DateTimeKind.Utc) - anchor).TotalMinutes));
        var preferredDelta = Math.Abs((anchor - preferredObservationUtc).TotalMinutes);
        var avgAltitude = selectedObjects.Average(x => x.Candidate.AltitudeDegrees);
        var angularSpread = EstimateMultiObjectAngularSpreadDeg(selectedObjects);
        var localNightBonus = IsEveningNightLocal(candidateLocal) ? 100d : 0d;
        var score = localNightBonus + avgAltitude - angularSpread - preferredDelta / 10d - maxDelta;
        logger.LogInformation(
            "MULTI_OBJECT_RESOLUTION_CANDIDATE_TIMESTAMP sceneCode={SceneCode} anchorUtc={AnchorUtc} toleranceMinutes={ToleranceMinutes} objectCount={ObjectCount} maxDeltaMinutes={MaxDeltaMinutes} preferredDeltaMinutes={PreferredDeltaMinutes} averageAltitude={AverageAltitude} angularSpread={AngularSpread} matchMode={MatchMode}",
            sceneCode,
            anchor,
            tolerance.TotalMinutes,
            selectedObjects.Count,
            maxDelta,
            preferredDelta,
            avgAltitude,
            angularSpread,
            matchMode);
        matches.Add((new WeeklyMultiObjectResolutionCandidate(anchor, selectedObjects, maxDelta, preferredDelta, matchMode, bucketObjectNames), score, bucketObjectNames));
    }

    return matches.OrderByDescending(x => x.Score).ThenBy(x => x.Candidate.PreferredDeltaMinutes).FirstOrDefault().Candidate;
}

static double EstimateMultiObjectAngularSpreadDeg(IReadOnlyList<WeeklyResolvedTemporalObject> objects)
{
    if (objects.Count < 2) return 0d;
    var max = 0d;
    for (var i = 0; i < objects.Count; i++)
    {
        for (var j = i + 1; j < objects.Count; j++)
        {
            var a = objects[i].Candidate;
            var b = objects[j].Candidate;
            var altDelta = a.AltitudeDegrees - b.AltitudeDegrees;
            var azDelta = Math.Abs(a.AzimuthDegrees - b.AzimuthDegrees);
            if (azDelta > 180d) azDelta = 360d - azDelta;
            var spread = Math.Sqrt(altDelta * altDelta + azDelta * azDelta);
            if (spread > max) max = spread;
        }
    }
    return max;
}

static IReadOnlyList<string> BuildTopMultiObjectBucketFailureSummary(
    IReadOnlyList<WeeklySkyfieldObjectHydration.SkyfieldFlattenedTemporalObject> flattenedObjects,
    HashSet<string> requiredSet,
    DateTime preferredObservationUtc,
    string timezoneId,
    DateOnly sceneDateLocal)
{
    return flattenedObjects
        .GroupBy(x => DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc))
        .Select(g => new
        {
            BucketUtc = g.Key,
            Names = g.Select(x => x.NormalizedName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RequiredMatches = g.Select(x => x.NormalizedName).Distinct(StringComparer.OrdinalIgnoreCase).Count(x => requiredSet.Contains(x)),
            Night = IsEveningNightLocal(ConvertUtcToLocal(g.Key, timezoneId)) && DateOnly.FromDateTime(ConvertUtcToLocal(g.Key, timezoneId)) == sceneDateLocal
        })
        .OrderByDescending(x => x.RequiredMatches)
        .ThenBy(x => Math.Abs((x.BucketUtc - preferredObservationUtc).TotalMinutes))
        .Take(5)
        .Select(x => $"{x.BucketUtc:O}:objects={string.Join(',', x.Names.Select(ToWeeklyObjectDisplayName))};requiredMatches={x.RequiredMatches};night={x.Night}")
        .ToList();
}

static WeeklyMultiObjectResolutionCandidate? FindBestSharedMultiObjectCandidate(
    IReadOnlyDictionary<string, List<SkyfieldTemporalCandidate>> candidatesByObject,
    IReadOnlyList<string> requestedObjects,
    DateTime preferredObservationUtc,
    DateTime preferredObservationLocal,
    DateOnly sceneDateLocal,
    string timezoneId,
    double toleranceMinutes,
    string matchMode,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode)
{
    var anchors = candidatesByObject.Values.SelectMany(x => x).Select(x => DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc)).Distinct().OrderBy(x => Math.Abs((x - preferredObservationUtc).TotalMinutes)).ToList();
    var matches = new List<WeeklyMultiObjectResolutionCandidate>();
    foreach (var anchor in anchors)
    {
        var selectedObjects = new List<WeeklyResolvedTemporalObject>();
        var validAnchor = true;
        foreach (var requestedObject in requestedObjects)
        {
            if (!candidatesByObject.TryGetValue(requestedObject, out var candidates) || candidates.Count == 0) { validAnchor = false; break; }
            var candidate = candidates.Select(x => new { Candidate = x, Delta = Math.Abs((DateTime.SpecifyKind(x.SnapshotUtc, DateTimeKind.Utc) - anchor).TotalMinutes) })
                .Where(x => toleranceMinutes <= 0d ? x.Delta == 0d : x.Delta <= toleranceMinutes)
                .OrderBy(x => x.Delta).ThenByDescending(x => x.Candidate.AltitudeDegrees).FirstOrDefault();
            if (candidate is null) { validAnchor = false; break; }
            var candidateLocal = ConvertUtcToLocal(DateTime.SpecifyKind(candidate.Candidate.SnapshotUtc, DateTimeKind.Utc), timezoneId);
            if (!IsCompatibleMultiObjectLocalWindow(candidateLocal, preferredObservationLocal, sceneDateLocal)) { validAnchor = false; break; }
            selectedObjects.Add(new WeeklyResolvedTemporalObject(requestedObject, NormalizeWeeklyObjectName(requestedObject), ToWeeklyObjectDisplayName(requestedObject), candidate.Candidate));
        }
        if (!validAnchor || selectedObjects.Count != requestedObjects.Count) continue;
        var maxDelta = selectedObjects.Max(x => Math.Abs((DateTime.SpecifyKind(x.Candidate.SnapshotUtc, DateTimeKind.Utc) - anchor).TotalMinutes));
        var preferredDelta = Math.Abs((anchor - preferredObservationUtc).TotalMinutes);
        logger.LogInformation("MULTI_OBJECT_RESOLUTION_CANDIDATE_TIMESTAMP sceneCode={SceneCode} anchorUtc={AnchorUtc} toleranceMinutes={ToleranceMinutes} objectCount={ObjectCount} maxDeltaMinutes={MaxDeltaMinutes} preferredDeltaMinutes={PreferredDeltaMinutes} matchMode={MatchMode}", sceneCode, anchor, toleranceMinutes, selectedObjects.Count, maxDelta, preferredDelta, matchMode);
        matches.Add(new WeeklyMultiObjectResolutionCandidate(anchor, selectedObjects, maxDelta, preferredDelta, matchMode, selectedObjects.Select(x => x.NormalizedName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
    }
    return matches.OrderBy(x => x.MaxDeltaMinutes).ThenBy(x => x.PreferredDeltaMinutes).ThenByDescending(x => x.Objects.Min(o => o.Candidate.AltitudeDegrees)).FirstOrDefault();
}

static bool IsCompatibleMultiObjectLocalWindow(DateTime candidateLocal, DateTime preferredObservationLocal, DateOnly sceneDateLocal)
{
    if (DateOnly.FromDateTime(candidateLocal) == sceneDateLocal) return true;
    if (DateOnly.FromDateTime(candidateLocal) == DateOnly.FromDateTime(preferredObservationLocal) && IsEveningNightLocal(candidateLocal)) return true;
    if (IsEveningNightLocal(candidateLocal) && IsEveningNightLocal(preferredObservationLocal)) return Math.Abs((candidateLocal - preferredObservationLocal).TotalHours) <= 12d;
    return false;
}

static void ReplaceMultiObjectSceneResolutionReport(List<WeeklyMultiObjectSceneResolutionReport> reports, string sceneCode, bool groupingSplitRequired, bool allObjectsVisuallySupported)
{
    var index = reports.FindIndex(x => x.SceneCode.Equals(sceneCode, StringComparison.OrdinalIgnoreCase));
    if (index < 0) return;
    var existing = reports[index];
    IReadOnlyList<WeeklyMultiObjectSplitSceneManifestEntry>? splitScenes = groupingSplitRequired && (existing.SplitScenes is null || existing.SplitScenes.Count == 0)
        ? existing.ResolvedObjects.Select(code => new WeeklyMultiObjectSplitSceneManifestEntry(ResolveMultiObjectSplitSceneCode(existing.SceneCode, code), new[] { code }, new[] { code })).ToList()
        : existing.SplitScenes;
    reports[index] = existing with
    {
        GroupingSplitRequired = groupingSplitRequired,
        AllObjectsVisuallySupported = allObjectsVisuallySupported,
        GroupingSingleFrameAvailable = !groupingSplitRequired,
        SplitScenes = splitScenes
    };
}

static WeeklyObjectPositionResolution ResolveWeeklySkyObjectPosition(
    string objectCodeOrName,
    DateTime sceneObservationUtc,
    DateTime sceneObservationLocal,
    DateOnly sceneDateLocal,
    dynamic composition,
    WeeklyAstronomyEventObject? directObject,
    WeeklySkyForecastV2IntelligenceResponse weeklyContext,
    IReadOnlyCollection<string> requestedTargetObjects,
    ISkyfieldTemporalResolver temporalResolver,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode)
{
    var targetAliases = ResolveWeeklyObjectAliases(objectCodeOrName, directObject?.ObjectName);
    var normalizedRequestedName = NormalizeWeeklyObjectName(directObject?.ObjectName ?? objectCodeOrName);
    var selectedDateKey = sceneDateLocal.ToString("yyyy-MM-dd");
    var selectedTimeKey = sceneObservationUtc.ToString("HH:mm");
    var extractedEvents = weeklyContext.EventExtractionResult?.ExtractedEvents ?? [];
    var topLevelKeys = string.Join(",", typeof(WeeklyAstronomyEventExtractionResult).GetProperties().Select(p => p.Name));
    var availableDates = string.Join(",", extractedEvents.Where(e => e.BestDateLocal.HasValue).Select(e => e.BestDateLocal!.Value.ToString("yyyy-MM-dd")).Distinct().OrderBy(x => x));
    var selectedDateEvents = extractedEvents.Where(e => e.BestDateLocal.HasValue && e.BestDateLocal.Value == sceneDateLocal).ToList();
    var selectedDateCollections = "ExtractedEvents.Objects";
    var selectedDateObjectNames = string.Join(",", selectedDateEvents.SelectMany(e => e.Objects ?? []).Select(o => o.ObjectCode ?? o.ObjectName ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
    var selectedDateTimestamps = string.Join(",", selectedDateEvents.Select(e => ResolveEventUtc(e)).Where(x => x.HasValue).Select(x => x!.Value.ToString("O")).Distinct().OrderBy(x => x));
    var candidates = Astronomy.MediaFactory.Api.WeeklySkyfieldObjectHydration.BuildTemporalCandidates(
        extractedEvents,
        targetAliases,
        e => ResolveEventUtc(e),
        name => NormalizeWeeklyObjectName(name),
        (code, name, aliases) => MatchesWeeklyObjectAliases(code, name, aliases),
        logger,
        sceneCode,
        directObject?.ObjectName ?? objectCodeOrName).ToList();
    logger.LogInformation(
        "TEMPORAL_RESOLVER_ENTER sceneCode={SceneCode} object={Object} requestedUtc={RequestedUtc} candidateCount={CandidateCount} toleranceMinutes={ToleranceMinutes}",
        sceneCode,
        directObject?.ObjectName ?? objectCodeOrName,
        sceneObservationUtc,
        candidates.Count,
        SkyfieldTemporalResolver.DefaultMaximumDeltaMinutes);

    var toleranceMinutes = SkyfieldTemporalResolver.DefaultMaximumDeltaMinutes;
    foreach (var candidate in candidates.OrderBy(c => c.SnapshotUtc))
    {
        var deltaMinutes = Math.Abs((candidate.SnapshotUtc - sceneObservationUtc).TotalMinutes);
        var withinTolerance = deltaMinutes <= toleranceMinutes;
        logger.LogInformation(
            "TEMPORAL_RESOLVER_CANDIDATE sceneCode={SceneCode} object={Object} requestedUtc={RequestedUtc} candidateUtc={CandidateUtc} deltaMinutes={DeltaMinutes} withinTolerance={WithinTolerance} selected=False",
            sceneCode,
            directObject?.ObjectName ?? objectCodeOrName,
            sceneObservationUtc,
            candidate.SnapshotUtc,
            deltaMinutes,
            withinTolerance);
    }

    var temporal = temporalResolver.Resolve(
        directObject?.ObjectName ?? objectCodeOrName,
        sceneObservationUtc,
        candidates,
        toleranceMinutes);

    if (temporal.MatchFound && temporal.AltitudeDegrees.HasValue && temporal.AzimuthDegrees.HasValue)
    {
        logger.LogInformation(
            "TEMPORAL_RESOLVER_MATCH sceneCode={SceneCode} object={Object} requestedUtc={RequestedUtc} candidateUtc={CandidateUtc} deltaMinutes={DeltaMinutes} withinTolerance=True selected=True source={Source}",
            sceneCode,
            directObject?.ObjectName ?? objectCodeOrName,
            temporal.RequestedTimeUtc,
            temporal.MatchedTimeUtc,
            temporal.DeltaMinutes,
            temporal.Source);
        var source = temporal.ExactMatch ? "source=skyfield.exact" : "source=skyfield.nearest-time";
        var temporalTrace = $"{source};requestedTimeUtc={temporal.RequestedTimeUtc:O};matchedTimeUtc={temporal.MatchedTimeUtc:O};deltaMinutes={temporal.DeltaMinutes?.ToString("0.###") ?? "0"}";
        logger.LogInformation("TEMPORAL_RESOLVER_EXIT sceneCode={SceneCode} object={Object} result=resolved source={Source}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, temporal.Source);
        return new WeeklyObjectPositionResolution(
            temporal.AltitudeDegrees.Value,
            temporal.AzimuthDegrees.Value,
            temporal.Magnitude ?? 5.5d,
            temporalTrace,
            directObject?.ObjectName ?? objectCodeOrName,
            normalizedRequestedName,
            selectedDateKey,
            selectedTimeKey,
            "EventExtractionResult.ExtractedEvents[].Objects",
            true,
            string.Empty,
            selectedDateTimestamps,
            topLevelKeys,
            availableDates,
            selectedDateCollections,
            selectedDateObjectNames,
            temporal.MatchedTimeUtc);
    }

    logger.LogWarning(
        "TEMPORAL_RESOLVER_REJECT sceneCode={SceneCode} object={Object} requestedUtc={RequestedUtc} candidateUtc={CandidateUtc} deltaMinutes={DeltaMinutes} withinTolerance={WithinTolerance} selected=False reason={Reason}",
        sceneCode,
        directObject?.ObjectName ?? objectCodeOrName,
        temporal.RequestedTimeUtc,
        temporal.MatchedTimeUtc,
        temporal.DeltaMinutes,
        temporal.DeltaMinutes.HasValue && temporal.DeltaMinutes.Value <= toleranceMinutes,
        temporal.RejectionReason ?? "no-match");
    logger.LogInformation("TEMPORAL_RESOLVER_EXIT sceneCode={SceneCode} object={Object} result=fallback source={Source}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, temporal.Source);

    var candidateNames = string.Join(",", extractedEvents.SelectMany(e => e.Objects ?? []).Select(o => o.ObjectCode ?? o.ObjectName ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
    var candidateTimes = string.Join(",", extractedEvents.Select(e => ResolveEventUtc(e)).Where(x => x.HasValue).Select(x => x!.Value.ToString("O")).Distinct().OrderBy(x => x));
    if (!string.IsNullOrWhiteSpace(candidateNames) && candidates.Count == 0)
    {
        logger.LogCritical(
            "OBJECT_HYDRATION_CRITICAL_EMPTY_CANDIDATES sceneCode={SceneCode} object={Object} rawObjectNames={RawObjectNames} selectedDateObjectNames={SelectedDateObjectNames} selectedDateTimestamps={SelectedDateTimestamps}",
            sceneCode,
            directObject?.ObjectName ?? objectCodeOrName,
            candidateNames,
            selectedDateObjectNames,
            selectedDateTimestamps);
    }
    var fallbackResolution = new WeeklyObjectPositionResolution(
        (double)composition.CenterAltitude,
        (double)composition.CenterAzimuth,
        4d,
        $"source=fallback;requestedTimeUtc={sceneObservationUtc:O};matchedTimeUtc={(temporal.MatchedTimeUtc.HasValue ? temporal.MatchedTimeUtc.Value.ToString("O") : "null")};deltaMinutes={(temporal.DeltaMinutes?.ToString("0.###") ?? "null")}",
        directObject?.ObjectName ?? objectCodeOrName,
        normalizedRequestedName,
        selectedDateKey,
        selectedTimeKey,
        "EventExtractionResult.ExtractedEvents[].Objects",
        false,
        candidateNames,
        candidateTimes,
        topLevelKeys,
        availableDates,
        selectedDateCollections,
        selectedDateObjectNames);
    if (temporal.MatchedTimeUtc.HasValue && temporal.DeltaMinutes.HasValue && temporal.DeltaMinutes.Value <= toleranceMinutes)
    {
        logger.LogCritical(
            "TEMPORAL_RESOLVER_CRITICAL_WITHIN_TOLERANCE_FALLBACK sceneCode={SceneCode} object={Object} requestedUtc={RequestedUtc} candidateUtc={CandidateUtc} deltaMinutes={DeltaMinutes}",
            sceneCode,
            directObject?.ObjectName ?? objectCodeOrName,
            temporal.RequestedTimeUtc,
            temporal.MatchedTimeUtc,
            temporal.DeltaMinutes);
    }

    return fallbackResolution;
}


static WeeklyObjectPositionResolution ResolveExpandedWeeklySkyObjectPosition(
    string objectCodeOrName,
    DateTime sceneObservationUtc,
    DateTime sceneObservationLocal,
    DateOnly sceneDateLocal,
    string timezoneId,
    dynamic composition,
    WeeklyAstronomyEventObject? directObject,
    WeeklySkyForecastV2IntelligenceResponse weeklyContext,
    IReadOnlyCollection<string> requestedTargetObjects,
    ISkyfieldTemporalResolver temporalResolver,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode)
{
    logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_START sceneCode={SceneCode} object={Object} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, sceneObservationUtc, sceneObservationLocal);

    var targetAliases = ResolveWeeklyObjectAliases(objectCodeOrName, directObject?.ObjectName);
    var normalizedRequestedName = NormalizeWeeklyObjectName(directObject?.ObjectName ?? objectCodeOrName);
    var extractedEvents = weeklyContext.EventExtractionResult?.ExtractedEvents ?? [];
    var candidates = Astronomy.MediaFactory.Api.WeeklySkyfieldObjectHydration.BuildTemporalCandidates(
        extractedEvents,
        targetAliases,
        e => ResolveEventUtc(e),
        name => NormalizeWeeklyObjectName(name),
        (code, name, aliases) => MatchesWeeklyObjectAliases(code, name, aliases),
        logger,
        sceneCode,
        directObject?.ObjectName ?? objectCodeOrName).ToList();

    var topLevelKeys = string.Join(",", typeof(WeeklyAstronomyEventExtractionResult).GetProperties().Select(p => p.Name));
    var availableDates = string.Join(",", extractedEvents.Where(e => e.BestDateLocal.HasValue).Select(e => e.BestDateLocal!.Value.ToString("yyyy-MM-dd")).Distinct().OrderBy(x => x));
    var selectedDateEvents = extractedEvents.Where(e => e.BestDateLocal.HasValue && e.BestDateLocal.Value == sceneDateLocal).ToList();
    var selectedDateObjectNames = string.Join(",", selectedDateEvents.SelectMany(e => e.Objects ?? []).Select(o => o.ObjectCode ?? o.ObjectName ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
    var selectedDateTimestamps = string.Join(",", selectedDateEvents.Select(e => ResolveEventUtc(e)).Where(x => x.HasValue).Select(x => x!.Value.ToString("O")).Distinct().OrderBy(x => x));
    var candidateNames = string.Join(",", extractedEvents.SelectMany(e => e.Objects ?? []).Select(o => o.ObjectCode ?? o.ObjectName ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
    var candidateTimes = string.Join(",", extractedEvents.Select(e => ResolveEventUtc(e)).Where(x => x.HasValue).Select(x => x!.Value.ToString("O")).Distinct().OrderBy(x => x));

    WeeklyObjectPositionResolution ToResolution(SkyfieldTemporalCandidate candidate, string source)
    {
        var delta = Math.Abs((candidate.SnapshotUtc - sceneObservationUtc).TotalMinutes);
        return new WeeklyObjectPositionResolution(
            candidate.AltitudeDegrees,
            candidate.AzimuthDegrees,
            candidate.Magnitude ?? directObject?.Magnitude ?? 5.5d,
            $"source={source};requestedTimeUtc={sceneObservationUtc:O};matchedTimeUtc={candidate.SnapshotUtc:O};deltaMinutes={delta:0.###}",
            directObject?.ObjectName ?? objectCodeOrName,
            normalizedRequestedName,
            sceneDateLocal.ToString("yyyy-MM-dd"),
            sceneObservationUtc.ToString("HH:mm"),
            "EventExtractionResult.ExtractedEvents[].Objects",
            true,
            candidateNames,
            candidateTimes,
            topLevelKeys,
            availableDates,
            "ExtractedEvents.Objects",
            selectedDateObjectNames,
            candidate.SnapshotUtc);
    }

    var exact = candidates.FirstOrDefault(candidate => candidate.SnapshotUtc == sceneObservationUtc);
    if (exact is not null)
    {
        logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_SUCCESS sceneCode={SceneCode} object={Object} source=skyfield.exact matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, exact.SnapshotUtc, exact.AltitudeDegrees, exact.AzimuthDegrees);
        return ToResolution(exact, "skyfield.exact");
    }

    logger.LogInformation("EXPANDED_GEOMETRY_EXACT_MISS sceneCode={SceneCode} object={Object} selectedObservationUtc={SelectedObservationUtc} candidateCount={CandidateCount}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, sceneObservationUtc, candidates.Count);

    var sameLocalDate = candidates
        .Where(candidate => DateOnly.FromDateTime(ConvertUtcToLocal(DateTime.SpecifyKind(candidate.SnapshotUtc, DateTimeKind.Utc), timezoneId)) == sceneDateLocal)
        .OrderBy(candidate => Math.Abs((candidate.SnapshotUtc - sceneObservationUtc).TotalMinutes))
        .FirstOrDefault();
    if (sameLocalDate is not null)
    {
        logger.LogInformation("EXPANDED_GEOMETRY_WEEKLY_FALLBACK_SELECTED sceneCode={SceneCode} object={Object} fallback=same-local-date-nearest matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, sameLocalDate.SnapshotUtc, sameLocalDate.AltitudeDegrees, sameLocalDate.AzimuthDegrees);
        logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_SUCCESS sceneCode={SceneCode} object={Object} source=skyfield.same-local-date-nearest", sceneCode, directObject?.ObjectName ?? objectCodeOrName);
        return ToResolution(sameLocalDate, "skyfield.same-local-date-nearest");
    }

    var isMoon = targetAliases.Contains("moon") || objectCodeOrName.Equals("MOON", StringComparison.OrdinalIgnoreCase) || (directObject?.ObjectName?.Contains("moon", StringComparison.OrdinalIgnoreCase) ?? false);
    if (isMoon)
    {
        var moonNight = candidates
            .Where(candidate =>
            {
                var local = ConvertUtcToLocal(DateTime.SpecifyKind(candidate.SnapshotUtc, DateTimeKind.Utc), timezoneId);
                return candidate.AltitudeDegrees > 0d && (local.Hour >= 18 || local.Hour <= 5);
            })
            .OrderByDescending(candidate => candidate.AltitudeDegrees)
            .FirstOrDefault();
        if (moonNight is not null)
        {
            logger.LogInformation("EXPANDED_GEOMETRY_WEEKLY_FALLBACK_SELECTED sceneCode={SceneCode} object={Object} fallback=moon-best-nighttime matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, moonNight.SnapshotUtc, moonNight.AltitudeDegrees, moonNight.AzimuthDegrees);
            logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_SUCCESS sceneCode={SceneCode} object={Object} source=skyfield.moon-best-nighttime", sceneCode, directObject?.ObjectName ?? objectCodeOrName);
            return ToResolution(moonNight, "skyfield.moon-best-nighttime");
        }
    }

    var bestAltitude = candidates.OrderByDescending(candidate => candidate.AltitudeDegrees).FirstOrDefault();
    if (bestAltitude is not null && bestAltitude.AltitudeDegrees > 0d)
    {
        logger.LogInformation("EXPANDED_GEOMETRY_WEEKLY_FALLBACK_SELECTED sceneCode={SceneCode} object={Object} fallback=same-week-best-altitude matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, bestAltitude.SnapshotUtc, bestAltitude.AltitudeDegrees, bestAltitude.AzimuthDegrees);
        logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_SUCCESS sceneCode={SceneCode} object={Object} source=skyfield.same-week-best-altitude", sceneCode, directObject?.ObjectName ?? objectCodeOrName);
        return ToResolution(bestAltitude, "skyfield.same-week-best-altitude");
    }

    var visible = candidates.Where(candidate => candidate.AltitudeDegrees > 15d).OrderByDescending(candidate => candidate.AltitudeDegrees).FirstOrDefault();
    if (visible is not null)
    {
        logger.LogInformation("EXPANDED_GEOMETRY_WEEKLY_FALLBACK_SELECTED sceneCode={SceneCode} object={Object} fallback=weekly-visible-altitude-over-15 matchedTimeUtc={MatchedTimeUtc} altitude={Altitude} azimuth={Azimuth}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, visible.SnapshotUtc, visible.AltitudeDegrees, visible.AzimuthDegrees);
        logger.LogInformation("EXPANDED_GEOMETRY_RESOLUTION_SUCCESS sceneCode={SceneCode} object={Object} source=skyfield.weekly-visible-altitude-over-15", sceneCode, directObject?.ObjectName ?? objectCodeOrName);
        return ToResolution(visible, "skyfield.weekly-visible-altitude-over-15");
    }

    logger.LogWarning("EXPANDED_GEOMETRY_RESOLUTION_FAILED sceneCode={SceneCode} object={Object} selectedObservationUtc={SelectedObservationUtc} candidateCount={CandidateCount}", sceneCode, directObject?.ObjectName ?? objectCodeOrName, sceneObservationUtc, candidates.Count);
    return ResolveWeeklySkyObjectPosition(objectCodeOrName, sceneObservationUtc, sceneObservationLocal, sceneDateLocal, composition, directObject, weeklyContext, requestedTargetObjects, temporalResolver, logger, sceneCode);
}

static HashSet<string> ResolveWeeklyObjectAliases(string objectCodeOrName, string? directName)
{
    var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    void add(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        aliases.Add(NormalizeWeeklyObjectName(s));
    }

    add(objectCodeOrName);
    add(directName);

    var baseName = NormalizeWeeklyObjectName(directName ?? objectCodeOrName);
    foreach (var alias in baseName switch
    {
        "moon" => ["moon", "luna"],
        "venus" => ["venus"],
        "jupiter" => ["jupiter"],
        "saturn" => ["saturn"],
        "mars" => ["mars"],
        "mercury" => ["mercury"],
        "sirius" => ["sirius", "alpha canis majoris"],
        "canopus" => ["canopus", "alpha carinae"],
        "arcturus" => ["arcturus", "alpha bootis"],
        "vega" => ["vega", "alpha lyrae"],
        "capella" => ["capella", "alpha aurigae"],
        "rigel" => ["rigel", "beta orionis"],
        "procyon" => ["procyon", "alpha canis minoris"],
        "betelgeuse" => ["betelgeuse", "alpha orionis"],
        "achernar" => ["achernar", "alpha eridani"],
        "aldebaran" => ["aldebaran", "alpha tauri"],
        "antares" => ["antares", "alpha scorpii"],
        "spica" => ["spica", "alpha virginis"],
        "pollux" => ["pollux", "beta geminorum"],
        _ => Array.Empty<string>()
    })
    {
        add(alias);
    }

    return aliases;
}

static bool MatchesWeeklyObjectAliases(string? objectCode, string? objectName, HashSet<string> targetAliases)
{
    var code = NormalizeWeeklyObjectName(objectCode);
    var name = NormalizeWeeklyObjectName(objectName);
    return targetAliases.Contains(code) || targetAliases.Contains(name);
}

static string NormalizeWeeklyObjectName(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    var normalized = value.Trim().ToLowerInvariant()
        .Replace("_", "")
        .Replace("-", "")
        .Replace(" ", "");
    return normalized switch
    {
        "solarsystemmoon" => "moon",
        _ => normalized
    };
}

static DateTime? ResolveEventUtc(WeeklyAstronomyEvent weeklyEvent)
{
    if (!weeklyEvent.BestDateLocal.HasValue || !weeklyEvent.BestTimeLocal.HasValue) return null;
    var local = weeklyEvent.BestDateLocal.Value.ToDateTime(weeklyEvent.BestTimeLocal.Value, DateTimeKind.Utc);
    return DateTime.SpecifyKind(local, DateTimeKind.Utc);
}

static ImageSequencePlan BuildWeeklyImageSequencePlan(
    Guid pipelineRunId,
    string contentCategoryCode,
    string regionId,
    string language,
    DateOnly weekStartDate,
    IReadOnlyList<CinematicSceneFramePlan> sceneFramePlans,
    IReadOnlyList<string> frameScreenshots,
    IReadOnlyList<string> primaryScreenshots,
    Microsoft.Extensions.Logging.ILogger logger)
{
    const int expectedImageCount = 6;
    const int expectedDurationSeconds = 30;
    const long minimumProductionImageBytes = 50 * 1024;
    const string productionImageSource = "frameScreenshots";

    var candidateSceneFramePlans = sceneFramePlans ?? Array.Empty<CinematicSceneFramePlan>();
    var candidateImages = frameScreenshots ?? Array.Empty<string>();
    var candidatePrimaryScreenshots = primaryScreenshots ?? Array.Empty<string>();
    var candidateFramePlans = candidateSceneFramePlans
        .SelectMany(scene => scene.FramePlans ?? Array.Empty<CinematicFramePlan>())
        .Where(frame => frame is not null)
        .ToList();
    var screenshotPathSet = candidateImages
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var primaryScreenshotPathSet = candidatePrimaryScreenshots
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var framePlanLookup = candidateFramePlans
        .Where(frame => !string.IsNullOrWhiteSpace(frame.RenderSceneCode))
        .GroupBy(frame => $"{frame.RenderSceneCode}|{frame.FrameType}", StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    var frameIdLookup = candidateFramePlans
        .Where(frame => !string.IsNullOrWhiteSpace(frame.FrameId))
        .GroupBy(frame => frame.FrameId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    var preferredOrder = new (string RenderSceneCode, CinematicFrameType FrameType)[]
    {
        ("moon_hero_scene", CinematicFrameType.EstablishingWide),
        ("moon_hero_scene", CinematicFrameType.BalancedStoryFrame),
        ("moon_hero_scene", CinematicFrameType.HeroCloseup),
        ("western_planet_grouping_scene", CinematicFrameType.HorizonContext),
        ("western_planet_grouping_scene", CinematicFrameType.BalancedStoryFrame),
        ("western_planet_grouping_scene", CinematicFrameType.AlignmentWide)
    };
    var deterministicOrder = preferredOrder
        .Where(x => framePlanLookup.ContainsKey($"{x.RenderSceneCode}|{x.FrameType}"))
        .Concat(candidateFramePlans
            .OrderBy(frame => frame.FrameType == CinematicFrameType.BalancedStoryFrame ? 0 : frame.FrameType == CinematicFrameType.HorizonContext ? 1 : 2)
            .ThenBy(frame => frame.RenderSceneCode, StringComparer.OrdinalIgnoreCase)
            .Select(frame => (frame.RenderSceneCode, frame.FrameType)))
        .Distinct()
        .Take(expectedImageCount)
        .ToArray();
    if (deterministicOrder.Length < expectedImageCount)
        logger.LogWarning("IMAGE_SEQUENCE_DYNAMIC_SELECTION_PARTIAL selected={SelectedCount} expected={ExpectedCount}", deterministicOrder.Length, expectedImageCount);

    var sequenceItems = new List<ImageSequenceItem>();
    var planValidationWarnings = new List<string>();
    for (var i = 0; i < deterministicOrder.Length; i++)
    {
        var (orderedRenderSceneCode, frameType) = deterministicOrder[i];
        var key = $"{orderedRenderSceneCode}|{frameType}";
        if (!framePlanLookup.TryGetValue(key, out var framePlan))
            throw new InvalidOperationException($"IMAGE_SEQUENCE_FRAME_PLAN_MISSING renderSceneCode='{orderedRenderSceneCode}' frameType='{frameType}'.");

        logger.LogInformation(
            "IMAGE_SEQUENCE_ITEM_SELECTED sequenceIndex={SequenceIndex} sourceSceneCode={SourceSceneCode} renderSceneCode={RenderSceneCode} frameId={FrameId} frameType={FrameType} imagePath={ImagePath}",
            i + 1,
            framePlan.SourceSceneCode,
            framePlan.RenderSceneCode,
            framePlan.FrameId,
            framePlan.FrameType,
            framePlan.ImagePath);

        var validationWarnings = new List<string>();
        var imagePath = framePlan.ImagePath ?? string.Empty;
        var sourceSceneCode = ResolveImageSequenceSourceSceneCode(framePlan.SourceSceneCode, imagePath, framePlan.RenderSceneCode);
        var renderSceneCode = string.IsNullOrWhiteSpace(framePlan.RenderSceneCode) ? sourceSceneCode : framePlan.RenderSceneCode;
        var frameId = string.IsNullOrWhiteSpace(framePlan.FrameId) ? $"{renderSceneCode}_{framePlan.FrameType}" : framePlan.FrameId;
        var targetObjects = framePlan.TargetObjects ?? Array.Empty<string>();
        var safetyWarnings = framePlan.SafetyWarnings ?? Array.Empty<string>();
        var suggestedDurationSeconds = Math.Max(1, ResolveImageSequenceDuration(renderSceneCode, framePlan.FrameType));
        if (string.IsNullOrWhiteSpace(imagePath))
            validationWarnings.Add("imagePath is required.");
        if (string.IsNullOrWhiteSpace(sourceSceneCode))
            validationWarnings.Add("sourceSceneCode is required.");
        if (string.IsNullOrWhiteSpace(renderSceneCode))
            validationWarnings.Add("sceneCode is required.");
        var imageInfo = string.IsNullOrWhiteSpace(imagePath) ? null : new FileInfo(imagePath);
        var imageExists = imageInfo?.Exists == true;
        var fileSizeBytes = imageExists ? imageInfo!.Length : 0;
        var extension = Path.GetExtension(imagePath);
        var width = 0;
        var height = 0;

        if (!imageExists)
            validationWarnings.Add("imagePath does not exist.");
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            validationWarnings.Add($"imagePath extension must be .png but was '{extension}'.");
        if (fileSizeBytes <= minimumProductionImageBytes)
            validationWarnings.Add($"image file size must be greater than {minimumProductionImageBytes} bytes but was {fileSizeBytes}.");
        if (!frameIdLookup.TryGetValue(frameId, out var frameIdPlan))
            validationWarnings.Add($"frameId '{frameId}' does not exist in cinematic-frame-plan.");
        else if (!string.Equals(frameIdPlan.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            validationWarnings.Add($"imagePath '{imagePath}' does not match cinematic-frame-plan imagePath '{frameIdPlan.ImagePath}'.");
        if (!screenshotPathSet.Contains(imagePath))
            validationWarnings.Add("imagePath was not selected from frameScreenshots.");
        if (primaryScreenshotPathSet.Contains(imagePath))
            validationWarnings.Add("imagePath resolves to primaryScreenshots, which are compatibility-only and not a production source.");

        if (imageExists)
        {
            try
            {
                using var imageStream = File.OpenRead(imagePath);
                var identifiedImage = Image.Identify(imageStream);
                if (identifiedImage is null)
                {
                    validationWarnings.Add("image could not be opened/read by ImageSharp.");
                }
                else
                {
                    width = identifiedImage.Width;
                    height = identifiedImage.Height;
                    if (width < 1280)
                        validationWarnings.Add($"image width must be at least 1280 but was {width}.");
                    if (height < 720)
                        validationWarnings.Add($"image height must be at least 720 but was {height}.");
                }
            }
            catch (Exception ex)
            {
                validationWarnings.Add($"image could not be opened/read: {ex.Message}");
            }
        }

        var imageValidationStatus = validationWarnings.Count == 0 ? "Passed" : "Failed";
        if (validationWarnings.Count > 0)
            planValidationWarnings.AddRange(validationWarnings.Select(w => $"sequenceIndex={i + 1}; frameId={frameId}; imagePath={imagePath}; {w}"));

        var imageValidation = new ImageSequenceImageValidation(
            ImageExists: imageExists,
            FileSizeBytes: fileSizeBytes,
            Width: width,
            Height: height,
            ValidationStatus: imageValidationStatus,
            ValidationWarnings: validationWarnings,
            PerceptualHash: null);

        logger.LogInformation(
            "IMAGE_SEQUENCE_IMAGE_VALIDATED sequenceIndex={SequenceIndex} frameId={FrameId} imagePath={ImagePath} fileSizeBytes={FileSizeBytes} width={Width} height={Height} validationStatus={ValidationStatus}",
            i + 1,
            frameId,
            imagePath,
            fileSizeBytes,
            width,
            height,
            imageValidationStatus);

        sequenceItems.Add(new ImageSequenceItem(
            SequenceIndex: i + 1,
            SourceSceneCode: sourceSceneCode,
            RenderSceneCode: renderSceneCode,
            FrameId: frameId,
            FrameType: framePlan.FrameType.ToString(),
            ImagePath: imagePath,
            VisualPurpose: framePlan.VisualPurpose,
            NarrationUse: ResolveImageSequenceNarrationUse(framePlan),
            SuggestedDurationSeconds: suggestedDurationSeconds,
            TransitionIntent: ResolveImageSequenceTransitionIntent(i + 1),
            MotionIntentForFutureVideo: ResolveImageSequenceMotionIntent(framePlan.FrameType),
            ImportanceScore: ResolveImageSequenceImportanceScore(renderSceneCode, framePlan.FrameType),
            SelectionReason: "Selected by deterministic Phase 5 image-sequence order from generated frameScreenshots; no primaryScreenshots were used as production source.",
            Warnings: safetyWarnings,
            ImageExists: imageExists,
            FileSizeBytes: fileSizeBytes,
            Width: width,
            Height: height,
            ValidationStatus: imageValidationStatus,
            ValidationWarnings: validationWarnings,
            ImageValidation: imageValidation,
            SequenceRole: ResolveImageSequenceRole(i + 1, framePlan.FrameType),
            IsProductionSelected: true,
            PerceptualHash: null));
    }

    var duplicateWarnings = DetectImageSequenceStructuralDuplicates(sequenceItems);
    var duplicateImagePaths = sequenceItems
        .GroupBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var duplicateFrameIds = sequenceItems
        .GroupBy(x => x.FrameId, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var duplicateImagesDetected = duplicateWarnings.Count > 0;
    planValidationWarnings.AddRange(duplicateWarnings);
    foreach (var item in sequenceItems)
    {
        logger.LogInformation(
            "IMAGE_SEQUENCE_DUPLICATE_CHECK sequenceIndex={SequenceIndex} frameId={FrameId} imagePath={ImagePath} fileSizeBytes={FileSizeBytes} width={Width} height={Height} validationStatus={ValidationStatus} duplicateImagePath={DuplicateImagePath} duplicateFrameId={DuplicateFrameId}",
            item.SequenceIndex,
            item.FrameId,
            item.ImagePath,
            item.FileSizeBytes,
            item.Width,
            item.Height,
            item.ValidationStatus,
            duplicateImagePaths.Contains(item.ImagePath),
            duplicateFrameIds.Contains(item.FrameId));
    }

    var totalDurationSeconds = sequenceItems.Sum(x => x.SuggestedDurationSeconds);
    var durationDeltaSeconds = totalDurationSeconds - expectedDurationSeconds;
    var durationToleranceSeconds = expectedDurationSeconds <= 30
        ? 1d
        : Math.Max(1d, expectedDurationSeconds * 0.03d);
    var withinDurationTolerance = Math.Abs(durationDeltaSeconds) <= durationToleranceSeconds;
    if (durationDeltaSeconds != 0 && withinDurationTolerance)
    {
        planValidationWarnings.Add($"Image sequence duration differs by {Math.Abs(durationDeltaSeconds)} second{(Math.Abs(durationDeltaSeconds) == 1 ? string.Empty : "s")} but is within tolerance.");
    }
    var allImagesValid = sequenceItems.All(x => x.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase));
    var validationStatus = allImagesValid
        && !duplicateImagesDetected
        && sequenceItems.Count == expectedImageCount
        && withinDurationTolerance
        && productionImageSource.Equals("frameScreenshots", StringComparison.OrdinalIgnoreCase)
            ? "Passed"
            : "Failed";
    var productionReady = validationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase);

    if (!validationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Image sequence production validation failed: selectedImageCount={sequenceItems.Count}, expectedImageCount={expectedImageCount}, totalDurationSeconds={totalDurationSeconds}, expectedDurationSeconds={expectedDurationSeconds}, validationStatus={validationStatus}, duplicateImagesDetected={duplicateImagesDetected}, productionImageSource={productionImageSource}. Details: {string.Join(" | ", planValidationWarnings)}");
    }

    return new ImageSequencePlan(
        pipelineRunId,
        contentCategoryCode,
        regionId,
        language,
        weekStartDate,
        sequenceItems.Count,
        totalDurationSeconds,
        sequenceItems,
        ValidationStatus: validationStatus,
        SelectedImageCount: sequenceItems.Count,
        ExpectedImageCount: expectedImageCount,
        TotalDurationSeconds: totalDurationSeconds,
        ProductionReady: productionReady,
        ProductionImageSource: productionImageSource,
        PrimaryScreenshotsDeprecated: true,
        DuplicateImagesDetected: duplicateImagesDetected,
        ValidationWarnings: planValidationWarnings);
}

static string ResolveImageSequenceSourceSceneCode(string? sourceSceneCode, string? imagePath, string? renderSceneCode)
{
    if (!string.IsNullOrWhiteSpace(sourceSceneCode))
        return sourceSceneCode;

    var derivedSceneCode = DeriveStellariumSceneCodeFromImagePath(imagePath);
    if (!string.IsNullOrWhiteSpace(derivedSceneCode))
        return derivedSceneCode;

    return string.IsNullOrWhiteSpace(renderSceneCode) ? "unknown_scene" : renderSceneCode;
}

static string? DeriveStellariumSceneCodeFromImagePath(string? imagePath)
{
    if (string.IsNullOrWhiteSpace(imagePath))
        return null;

    var normalizedPath = imagePath.Replace('\\', '/');
    var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < segments.Length - 2; i++)
    {
        if (segments[i].Equals("stellarium", StringComparison.OrdinalIgnoreCase)
            && segments[i + 1].Equals("scenes", StringComparison.OrdinalIgnoreCase))
        {
            return segments[i + 2];
        }
    }

    return null;
}

static string ResolveSelectedImageSource(string? source, string? imagePath)
{
    if (!string.IsNullOrWhiteSpace(source))
        return source;

    return "frameScreenshots";
}

static string ResolveSelectedImageSequenceReportPath(string? selectedReportPath, string runRoot)
{
    if (!string.IsNullOrWhiteSpace(selectedReportPath))
        return selectedReportPath;

    return Path.Combine(runRoot, "render", "selected-stellarium-image-sequence-report.json");
}

static async Task WriteSelectedImageSequenceReportAsync(
    string? selectedReportPath,
    ImageSequencePlan imageSequencePlan,
    IReadOnlyList<ImageSequenceItem>? selectedImages,
    IReadOnlyDictionary<string, CinematicFramePlan>? framePlanLookup,
    string runRoot,
    int expectedDurationSeconds,
    int durationDeltaSeconds,
    double durationToleranceSeconds,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken ct)
{
    var resolvedSelectedReportPath = ResolveSelectedImageSequenceReportPath(selectedReportPath, runRoot);
    string? nullFieldName = null;

    try
    {
        logger.LogInformation("SELECTED_IMAGE_SEQUENCE_REPORT_BUILD_START path={Path} selectedImageCount={SelectedImageCount} validationStatus={ValidationStatus}", resolvedSelectedReportPath, imageSequencePlan.SelectedImageCount, imageSequencePlan.ValidationStatus);

        var safeSelectedImages = selectedImages?.Where(x => x is not null).ToList() ?? [];
        var safeFramePlanLookup = framePlanLookup ?? new Dictionary<string, CinematicFramePlan>(StringComparer.OrdinalIgnoreCase);
        var productionImageSource = ResolveSelectedImageSource(imageSequencePlan.ProductionImageSource, null);
        var reportWarnings = (imageSequencePlan.ValidationWarnings ?? Array.Empty<string>()).ToList();
        var validationPassed = imageSequencePlan.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase);

        var images = safeSelectedImages.Select(x =>
        {
            var imagePath = x.ImagePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                nullFieldName = "imagePath";
                reportWarnings.Add($"sequenceIndex={x.SequenceIndex}; imagePath is missing; skipped selected image.");
                return null;
            }

            var source = ResolveSelectedImageSource(productionImageSource, imagePath);
            var sourceType = source;
            var sourceSceneCode = ResolveImageSequenceSourceSceneCode(x.SourceSceneCode, imagePath, x.RenderSceneCode);
            var sceneCode = string.IsNullOrWhiteSpace(x.RenderSceneCode)
                ? (DeriveStellariumSceneCodeFromImagePath(imagePath) ?? sourceSceneCode)
                : x.RenderSceneCode;
            var parentSceneCode = WeeklyPostAssetDynamicSceneNormalizer.InferParentSceneCode(sceneCode);
            var frameType = ResolveSelectedImageReportFrameType(x.FrameType, imagePath);
            var selectedFramePlan = !string.IsNullOrWhiteSpace(x.FrameId) && safeFramePlanLookup.TryGetValue(x.FrameId, out var foundFramePlan)
                ? foundFramePlan
                : null;
            var targetObjects = (selectedFramePlan?.TargetObjects ?? Array.Empty<string>())
                .Select(o => NormalizeWeeklyObjectCode(o) ?? o)
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var requiredLabels = Array.Empty<string>();

            return new
            {
                source,
                productionImageSource,
                sourceType,
                sourceSceneCode,
                sceneCode,
                parentSceneCode,
                frameType,
                imagePath,
                path = imagePath,
                targetObjects,
                requiredLabels,
                durationSeconds = Math.Max(1, x.SuggestedDurationSeconds)
            };
        }).Where(x => x is not null).ToArray();

        await File.WriteAllTextAsync(resolvedSelectedReportPath, JsonSerializer.Serialize(new
        {
            selectedImageCount = imageSequencePlan.SelectedImageCount,
            expectedImageCount = imageSequencePlan.ExpectedImageCount,
            productionImageSource,
            validationStatus = imageSequencePlan.ValidationStatus,
            imageSequenceValidationReady = validationPassed,
            totalDurationSeconds = imageSequencePlan.TotalDurationSeconds,
            expectedDurationSeconds,
            durationDeltaSeconds,
            withinDurationTolerance = Math.Abs(durationDeltaSeconds) <= durationToleranceSeconds,
            images,
            warnings = reportWarnings,
            errors = validationPassed ? Array.Empty<string>() : reportWarnings.ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }), ct);

        logger.LogInformation("SELECTED_IMAGE_SEQUENCE_REPORT_BUILD_COMPLETE path={Path} selectedImageCount={SelectedImageCount} validationStatus={ValidationStatus}", resolvedSelectedReportPath, images.Length, imageSequencePlan.ValidationStatus);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "WEEKLY_POST_ASSET_NULL_SOURCE_FAILURE stage={Stage} field={Field} sceneCode={SceneCode} assetPath={AssetPath} pipelineRunId={PipelineRunId}", "SELECTED_IMAGE_SEQUENCE_REPORT_BUILD", nullFieldName ?? "unknown", "unknown", resolvedSelectedReportPath, imageSequencePlan.PipelineRunId);
        logger.LogError(ex, "SELECTED_IMAGE_SEQUENCE_REPORT_BUILD_FAILED path={Path} nullFieldName={NullFieldName}", resolvedSelectedReportPath, nullFieldName ?? "unknown");
        throw;
    }
}


static WeeklyFocusObjectPlan BuildWeeklyFocusObjectPlan(DateOnly weekStartDate, string regionId, string language, WeeklySkyForecastV2IntelligenceResponse context, string narrationText)
{
    var focusObjects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    var skyfieldObjects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    var events = context.EventExtractionResult?.ExtractedEvents ?? [];
    foreach (var ev in events)
    {
        var isRequiredVisualEvent = context.EventExtractionResult?.SelectedPrimaryEvent?.EventId.Equals(ev.EventId, StringComparison.OrdinalIgnoreCase) == true
            || ev.EventType is WeeklyAstronomyEventType.HeroObject or WeeklyAstronomyEventType.Conjunction or WeeklyAstronomyEventType.Grouping or WeeklyAstronomyEventType.BestViewingWindow
            || ev.ImportanceScore >= 70d;
        foreach (var obj in ev.Objects ?? [])
        {
            var code = NormalizeWeeklyObjectCode(obj.ObjectCode) ?? NormalizeWeeklyObjectCode(obj.ObjectName);
            if (code is not null)
            {
                skyfieldObjects.Add(code);
                if (isRequiredVisualEvent) focusObjects.Add(code);
            }
        }
        var primary = NormalizeWeeklyObjectCode(ev.PrimaryObject);
        if (primary is not null)
        {
            skyfieldObjects.Add(primary);
            if (isRequiredVisualEvent) focusObjects.Add(primary);
        }
    }

    foreach (var intelligence in context.EventIntelligence ?? [])
    {
        var purpose = $"{intelligence.EventType} {intelligence.RecommendedVisualStrategy} {intelligence.RecommendedScenePurpose}";
        var visuallyRequired = purpose.Contains("hero", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("planet", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("moon", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("best", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("astro", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("short", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
        if (!visuallyRequired) continue;
        foreach (var code in intelligence.ObjectCodes.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!))
            focusObjects.Add(code);
    }

    foreach (var code in ExtractWeeklyObjectsFromText(narrationText))
    {
        if (skyfieldObjects.Contains(code) && IsRequiredNarrationObject(code, context))
            focusObjects.Add(code);
    }

    foreach (var contextOnly in new[] { "JUPITER", "MARS", "MERCURY" })
    {
        if (!IsRequiredNarrationObject(contextOnly, context))
            focusObjects.Remove(contextOnly);
    }

    var objects = focusObjects.ToList();
    var groupings = new List<WeeklyFocusGrouping>();
    if (objects.Contains("VENUS", StringComparer.OrdinalIgnoreCase) && objects.Contains("SATURN", StringComparer.OrdinalIgnoreCase))
        groupings.Add(new WeeklyFocusGrouping("venus_saturn_planet_grouping", ["VENUS", "SATURN"], "narration+skyfield", "PlanetHighlights"));
    if (objects.Contains("MOON", StringComparer.OrdinalIgnoreCase) && objects.Contains("VENUS", StringComparer.OrdinalIgnoreCase) && objects.Contains("SATURN", StringComparer.OrdinalIgnoreCase))
        groupings.Add(new WeeklyFocusGrouping("moon_venus_saturn_hero_grouping", ["MOON", "VENUS", "SATURN"], "narration+skyfield", "HeroEvent"));

    var hero = context.EventExtractionResult?.SelectedPrimaryEvent?.Title
        ?? context.NarrativeAbstractionPackage?.HeroNarrative.Title
        ?? string.Join(" + ", objects.Select(ToWeeklyObjectDisplayName));

    var requiredScenes = new List<WeeklyRequiredVisualScene>();
    if (objects.Contains("MOON", StringComparer.OrdinalIgnoreCase))
        requiredScenes.Add(new WeeklyRequiredVisualScene("moon_hero_scene", "Moon Context Scene", ["MOON"], "MoonHighlights", 2, 3, false, true, true, "Moon label required; use night observation time."));
    if (objects.Contains("VENUS", StringComparer.OrdinalIgnoreCase) && objects.Contains("SATURN", StringComparer.OrdinalIgnoreCase))
        requiredScenes.Add(new WeeklyRequiredVisualScene("western_planet_grouping_scene", "Planet Grouping Scene", ["VENUS", "SATURN"], "PlanetHighlights", 2, 3, true, true, true, "Venus and Saturn labels required; do not include Jupiter unless sourced."));
    var heroObjects = new[] { "MOON", "VENUS", "SATURN" }.Where(objects.Contains).ToList();
    if (heroObjects.Count >= 2)
        requiredScenes.Add(new WeeklyRequiredVisualScene("hero_grouping_scene", "Hero Grouping Scene", heroObjects, "HeroEvent", 3, 3, true, true, true, "Must show all visible hero objects if possible; missing objects go to coverage report."));
    if (heroObjects.Count > 0)
        requiredScenes.Add(new WeeklyRequiredVisualScene("best_observation_direction_scene", "Best Observation Direction Scene", heroObjects, "WhereToLookDirection", 1, 2, true, true, true, "Include horizon and azimuth reference."));
    var astroObjects = heroObjects.Count >= 2 ? heroObjects : new[] { "MOON" }.Where(objects.Contains).ToList();
    if (astroObjects.Count > 0)
        requiredScenes.Add(new WeeklyRequiredVisualScene("astrophotography_target_scene", "Astrophotography Target Scene", astroObjects, "AstrophotographyTip", 1, 2, true, true, true, "Use hero grouping or Moon plus nearby planet."));

    return new WeeklyFocusObjectPlan(weekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), regionId, language, hero, objects, groupings, requiredScenes);
}

static WeeklyHybridScenePlanPackage AlignWeeklyScenePlanWithFocusObjects(WeeklyHybridScenePlanPackage plan, WeeklyFocusObjectPlan focusPlan)
{
    var focus = new HashSet<string>(focusPlan.FocusObjects, StringComparer.OrdinalIgnoreCase);
    var planetTargets = new[] { "VENUS", "SATURN" }.Where(focus.Contains).ToArray();
    var heroTargets = new[] { "MOON", "VENUS", "SATURN" }.Where(focus.Contains).ToArray();
    IReadOnlyList<string> CleanTargets(IReadOnlyList<string> source)
        => source.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Where(x => !x.Equals("JUPITER", StringComparison.OrdinalIgnoreCase) || focus.Contains("JUPITER")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    var scenes = plan.ScenePlans.Select(scene =>
    {
        if (scene.SceneCode.Equals("hero_western_grouping_scene", StringComparison.OrdinalIgnoreCase) && heroTargets.Length > 0)
            return scene with { ObjectCodes = heroTargets, RequiredAssets = heroTargets, SceneType = "HeroEvent" };
        if (scene.SceneCode.Equals("best_night_wide_scene", StringComparison.OrdinalIgnoreCase) && heroTargets.Length > 0)
            return scene with { ObjectCodes = heroTargets, RequiredAssets = heroTargets, SceneType = "WhereToLookDirection" };
        if (scene.SceneCode.Equals("moon_jupiter_hero_scene", StringComparison.OrdinalIgnoreCase))
            return scene with { SceneCode = "moon_hero_scene", VisualCode = "moon_hero", ObjectCodes = ["MOON"], RequiredAssets = ["MOON"], SceneType = "MoonHighlights", VisualSourceType = "Stellarium", RequiresStellarium = true };
        return scene with { ObjectCodes = CleanTargets(scene.ObjectCodes), RequiredAssets = CleanTargets(scene.RequiredAssets) };
    }).ToList();

    var mappings = plan.SegmentSceneMappings.Select(m =>
        m.SceneCode.Equals("moon_jupiter_hero_scene", StringComparison.OrdinalIgnoreCase)
            ? m with { SceneCode = "moon_hero_scene" }
            : m).ToList();

    var stellariumNeeds = new List<WeeklyStellariumNeed>();
    var moonSource = scenes.FirstOrDefault(x => x.SceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase));
    if (moonSource is not null && focus.Contains("MOON"))
        stellariumNeeds.Add(new WeeklyStellariumNeed("moon_hero_scene", moonSource.TargetDate, moonSource.BestTimeUtc, plan.StellariumNeeds.FirstOrDefault()?.LocationRegionId ?? string.Empty, ["MOON"], "MoonHighlights", 55, "StillFrameOrSlowPanReference", "MoonHighlights", moonSource.SceneCode, true));
    var groupingSource = scenes.FirstOrDefault(x => x.SceneCode.Equals("hero_western_grouping_scene", StringComparison.OrdinalIgnoreCase)) ?? scenes.FirstOrDefault();
    if (groupingSource is not null && planetTargets.Length >= 2)
        stellariumNeeds.Add(new WeeklyStellariumNeed("western_planet_grouping_scene", groupingSource.TargetDate, groupingSource.BestTimeUtc, plan.StellariumNeeds.FirstOrDefault()?.LocationRegionId ?? string.Empty, planetTargets, "PlanetHighlights", 65, "StillFrameOrSlowPanReference", "PlanetHighlights", groupingSource.SceneCode, true));

    return plan with { ScenePlans = scenes, SegmentSceneMappings = mappings, StellariumNeeds = stellariumNeeds, SceneWarnings = plan.SceneWarnings.Concat(["Weekly focus object alignment applied before SSC generation."]).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
}

static string ResolveMultiObjectSplitSceneCode(string sourceSceneCode, string objectCodeOrName)
{
    var normalizedObject = NormalizeWeeklyObjectCode(objectCodeOrName) ?? objectCodeOrName;
    var objectSlug = new string(normalizedObject.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
    if (string.IsNullOrWhiteSpace(objectSlug)) objectSlug = "object";
    var normalizedScene = sourceSceneCode.Trim().ToLowerInvariant();
    return normalizedScene.EndsWith($"_{objectSlug}", StringComparison.OrdinalIgnoreCase)
        ? normalizedScene
        : $"{normalizedScene}_{objectSlug}";
}

static WeeklyStellariumSceneRequirementsDocument BuildWeeklyStellariumSceneRequirements(WeeklyFocusObjectPlan focusPlan, WeeklyHybridScenePlanPackage scenePlan)
{
    var labels = focusPlan.FocusObjects.Select(ToWeeklyObjectDisplayName).Select(x => $"{x} label").ToList();
    var requirements = new[]
    {
        "use night time only",
        "use correct observation local time",
        "set observer location correctly",
        "enable object labels",
        "enable constellation lines",
        "enable horizon/azimuth reference where useful",
        "set camera direction based on object azimuth",
        "set zoom/FOV so target objects are visible",
        "capture output PNG with meaningful name"
    };
    return new WeeklyStellariumSceneRequirementsDocument(focusPlan.WeekStartDate, focusPlan.RegionId, focusPlan.Language, focusPlan.RequiredVisualScenes, requirements, labels);
}

static WeeklyVisualNarrationCoverageReport BuildWeeklyVisualNarrationCoverageReport(
    WeeklyFocusObjectPlan focusPlan,
    WeeklyStellariumSceneRequirementsDocument requirements,
    IReadOnlyList<CinematicSceneFramePlan> framePlans,
    IReadOnlyList<string> screenshots,
    IReadOnlyList<string> scriptPaths,
    IReadOnlyList<string> generatedSceneCodes,
    IReadOnlyList<string> warnings,
    IReadOnlyList<WeeklyMultiObjectSceneResolutionReport> multiObjectSceneResolutionReports)
{
    var supported = framePlans.SelectMany(x => x.FramePlans).SelectMany(x => x.TargetObjects).Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var mentioned = focusPlan.FocusObjects.ToList();
    var missingObjects = mentioned.Where(x => !supported.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
    var requiredGenerated = requirements.RequiredScenes.Where(r => r.Objects.All(o => supported.Contains(o, StringComparer.OrdinalIgnoreCase))).Select(r => r.SceneCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var missingScenes = requirements.RequiredScenes.Where(r => !requiredGenerated.Contains(r.SceneCode, StringComparer.OrdinalIgnoreCase) && r.Objects.Any(o => mentioned.Contains(o, StringComparer.OrdinalIgnoreCase))).Select(r => r.SceneCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var moonSceneCount = CountScenesForObject(framePlans, "MOON");
    var venusSceneCount = CountScenesForObject(framePlans, "VENUS");
    var saturnSceneCount = CountScenesForObject(framePlans, "SATURN");
    var groupingSceneCount = framePlans.Count(scene => scene.FramePlans.Any(frame => frame.TargetObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2));
    var multiObjectScenesRequested = multiObjectSceneResolutionReports.Count;
    var multiObjectScenesResolved = multiObjectSceneResolutionReports.Count(x => x.MultiObjectResolutionPassed);
    var multiObjectScenesFailed = multiObjectSceneResolutionReports.Count(x => !x.MultiObjectResolutionPassed);
    var multiObjectSceneResolutionPassed = multiObjectScenesFailed == 0;
    var groupingSplitRequired = multiObjectSceneResolutionReports.Any(x => x.GroupingSplitRequired);
    var groupingSingleFrameAvailable = !groupingSplitRequired && groupingSceneCount > 0;
    var splitGroupingVisuallySupported = groupingSplitRequired
        && multiObjectSceneResolutionReports.Where(x => x.GroupingSplitRequired).All(report => report.ResolvedObjects.All(obj => supported.Contains(obj, StringComparer.OrdinalIgnoreCase)));
    var errors = new List<string>();
    if (missingObjects.Count > 0) errors.Add($"Narration mentions unsupported objects: {string.Join(",", missingObjects)}");
    if (mentioned.Contains("VENUS", StringComparer.OrdinalIgnoreCase) && venusSceneCount == 0) errors.Add("Venus is mentioned but no Venus scene was generated.");
    if (mentioned.Contains("SATURN", StringComparer.OrdinalIgnoreCase) && saturnSceneCount == 0) errors.Add("Saturn is mentioned but no Saturn scene was generated.");
    if (focusPlan.FocusGroupings.Count > 0 && groupingSceneCount == 0 && !splitGroupingVisuallySupported) errors.Add("Narration grouping mentioned but no grouping scene or split-scene support was generated.");
    var allObjectsVisuallySupported = missingObjects.Count == 0 && multiObjectSceneResolutionPassed && (!groupingSplitRequired || splitGroupingVisuallySupported);
    if (!multiObjectSceneResolutionPassed) errors.Add($"Multi-object scene resolution failed for: {string.Join(",", multiObjectSceneResolutionReports.Where(x => !x.MultiObjectResolutionPassed).Select(x => x.SceneCode))}");
    var moonRequirementSatisfied = !mentioned.Contains("MOON", StringComparer.OrdinalIgnoreCase) || moonSceneCount > 0;
    var aligned = errors.Count == 0 && missingObjects.Count == 0 && moonRequirementSatisfied && multiObjectSceneResolutionPassed && (!groupingSplitRequired || splitGroupingVisuallySupported);
    return new WeeklyVisualNarrationCoverageReport(aligned, allObjectsVisuallySupported, groupingSplitRequired, mentioned, supported, missingObjects, requiredGenerated, missingScenes, moonSceneCount, venusSceneCount, saturnSceneCount, groupingSceneCount, scriptPaths.Count, screenshots.Count, multiObjectSceneResolutionPassed, multiObjectScenesRequested, multiObjectScenesResolved, multiObjectScenesFailed, multiObjectSceneResolutionReports, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors, groupingSingleFrameAvailable, groupingSplitRequired);
}

static int CountScenesForObject(IReadOnlyList<CinematicSceneFramePlan> framePlans, string objectCode)
    => framePlans.Count(scene => scene.FramePlans.Any(frame => frame.TargetObjects.Select(NormalizeWeeklyObjectCode).Any(code => code?.Equals(objectCode, StringComparison.OrdinalIgnoreCase) == true)));

static IReadOnlyList<string> ExtractWeeklyObjectsFromText(string text)
{
    var found = new List<string>();
    foreach (var candidate in new[] { "MOON", "VENUS", "SATURN", "JUPITER", "MARS", "MERCURY" })
    {
        if (Regex.IsMatch(text ?? string.Empty, $@"\b{candidate}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            found.Add(candidate);
    }
    return found;
}

static bool IsRequiredNarrationObject(string objectCode, WeeklySkyForecastV2IntelligenceResponse context)
{
    var normalized = NormalizeWeeklyObjectCode(objectCode);
    if (normalized is null) return false;
    var selected = context.EventExtractionResult?.SelectedPrimaryEvent;
    if (selected is not null)
    {
        if (selected.Objects.Any(o => string.Equals(NormalizeWeeklyObjectCode(o.ObjectCode) ?? NormalizeWeeklyObjectCode(o.ObjectName), normalized, StringComparison.OrdinalIgnoreCase))) return true;
        if (string.Equals(NormalizeWeeklyObjectCode(selected.PrimaryObject), normalized, StringComparison.OrdinalIgnoreCase)) return true;
    }

    return (context.EventIntelligence ?? []).Any(item =>
    {
        if (!item.ObjectCodes.Select(NormalizeWeeklyObjectCode).Any(code => string.Equals(code, normalized, StringComparison.OrdinalIgnoreCase))) return false;
        var purpose = $"{item.EventType} {item.RecommendedVisualStrategy} {item.RecommendedScenePurpose}";
        return purpose.Contains("hero", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("planet", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("moon", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("best", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("astro", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("short", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
    });
}

static string? NormalizeWeeklyObjectCode(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    var normalized = value.Trim().Replace(" ", "_").Replace("-", "_").ToUpperInvariant();
    return normalized switch
    {
        "LUNA" or "THE_MOON" or "MOON" => "MOON",
        "VENUS" => "VENUS",
        "SATURN" => "SATURN",
        "JUPITER" => "JUPITER",
        "MARS" => "MARS",
        "MERCURY" => "MERCURY",
        _ => normalized.Contains("MOON", StringComparison.OrdinalIgnoreCase) ? "MOON" : normalized
    };
}

static string ToWeeklyObjectDisplayName(string code) => (NormalizeWeeklyObjectCode(code) ?? code).ToUpperInvariant() switch
{
    "MOON" => "Moon",
    "VENUS" => "Venus",
    "SATURN" => "Saturn",
    "JUPITER" => "Jupiter",
    "MARS" => "Mars",
    "MERCURY" => "Mercury",
    var other => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(other.ToLowerInvariant().Replace('_', ' '))
};

static string ResolveSelectedImageReportFrameType(string? frameType, string? imagePath)
{
    var fileName = string.IsNullOrWhiteSpace(imagePath) ? string.Empty : Path.GetFileNameWithoutExtension(imagePath) ?? string.Empty;
    var normalizedFileName = Regex.Replace(fileName, @"^\d+_", string.Empty, RegexOptions.CultureInvariant);
    if (!string.IsNullOrWhiteSpace(normalizedFileName)) return normalizedFileName;

    var resolvedFrameType = string.IsNullOrWhiteSpace(frameType) ? null : frameType;

    return resolvedFrameType switch
    {
        nameof(CinematicFrameType.HorizonContext) => "horizon_context",
        nameof(CinematicFrameType.EstablishingWide) => "horizon_context",
        nameof(CinematicFrameType.BalancedStoryFrame) => "balanced_story_frame",
        nameof(CinematicFrameType.DetailFocus) => "detail_focus",
        nameof(CinematicFrameType.HeroCloseup) => "detail_focus",
        nameof(CinematicFrameType.AlignmentWide) => "horizon_context",
        null => "stellarium_frame_screenshot",
        _ => Regex.Replace(resolvedFrameType, "([a-z0-9])([A-Z])", "$1_$2", RegexOptions.CultureInvariant).ToLowerInvariant()
    };
}

static IReadOnlyList<string> DetectImageSequenceStructuralDuplicates(IReadOnlyList<ImageSequenceItem> sequenceItems)
{
    var duplicateWarnings = new List<string>();

    duplicateWarnings.AddRange(sequenceItems
        .GroupBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => $"Duplicate imagePath detected: imagePath={g.Key}; sequenceIndexes={string.Join(",", g.Select(x => x.SequenceIndex))}; frameIds={string.Join(",", g.Select(x => x.FrameId))}."));

    duplicateWarnings.AddRange(sequenceItems
        .GroupBy(x => x.FrameId, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => $"Duplicate frameId detected: frameId={g.Key}; sequenceIndexes={string.Join(",", g.Select(x => x.SequenceIndex))}; imagePaths={string.Join(",", g.Select(x => x.ImagePath))}."));

    return duplicateWarnings;
}

static string ResolveImageSequenceRole(int sequenceIndex, CinematicFrameType frameType) => frameType switch
{
    CinematicFrameType.EstablishingWide => sequenceIndex == 1 ? "opening_establishing_production_frame" : "establishing_production_frame",
    CinematicFrameType.HeroCloseup => "hero_emphasis_production_frame",
    CinematicFrameType.HorizonContext => "horizon_context_production_frame",
    CinematicFrameType.AlignmentWide => "alignment_context_production_frame",
    CinematicFrameType.BalancedStoryFrame => "story_bridge_production_frame",
    _ => "production_frame"
};

static int ResolveImageSequenceDuration(string renderSceneCode, CinematicFrameType frameType)
{
    if (renderSceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase))
    {
        return frameType switch
        {
            CinematicFrameType.EstablishingWide => 4,
            CinematicFrameType.BalancedStoryFrame => 5,
            CinematicFrameType.HeroCloseup => 5,
            _ => 4
        };
    }

    if (renderSceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase))
    {
        return frameType switch
        {
            CinematicFrameType.HorizonContext => 5,
            CinematicFrameType.BalancedStoryFrame => 6,
            CinematicFrameType.AlignmentWide => 5,
            _ => 5
        };
    }

    return 5;
}

static string ResolveImageSequenceMotionIntent(CinematicFrameType frameType) => frameType switch
{
    CinematicFrameType.EstablishingWide => "slow_push_in",
    CinematicFrameType.BalancedStoryFrame => "gentle_hold",
    CinematicFrameType.HeroCloseup => "micro_zoom_in",
    CinematicFrameType.HorizonContext => "slow_tilt_up",
    CinematicFrameType.AlignmentWide => "slow_pan_across_group",
    _ => "gentle_hold"
};

static string ResolveImageSequenceTransitionIntent(int sequenceIndex) => sequenceIndex switch
{
    1 => "soft_push",
    2 => "cinematic_zoom",
    3 => "crossfade",
    4 => "soft_push",
    5 => "wide_reveal",
    _ => "final_hold"
};

static double ResolveImageSequenceImportanceScore(string renderSceneCode, CinematicFrameType frameType)
{
    if (renderSceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase) && frameType == CinematicFrameType.HeroCloseup) return 0.96d;
    if (renderSceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase) && frameType == CinematicFrameType.BalancedStoryFrame) return 0.95d;
    if (frameType == CinematicFrameType.BalancedStoryFrame) return 0.92d;
    if (frameType == CinematicFrameType.EstablishingWide || frameType == CinematicFrameType.AlignmentWide) return 0.90d;
    return 0.88d;
}

static string ResolveImageSequenceNarrationUse(CinematicFramePlan framePlan)
{
    if (framePlan.RenderSceneCode.Equals("moon_hero_scene", StringComparison.OrdinalIgnoreCase))
    {
        return framePlan.FrameType switch
        {
            CinematicFrameType.EstablishingWide => "Opening moon hero narration segment; establishes the week sky-viewing context.",
            CinematicFrameType.BalancedStoryFrame => "Moon hero story beat; supports the main lunar viewing guidance.",
            CinematicFrameType.HeroCloseup => "Moon hero emphasis beat; reinforces the Moon as the emotional subject.",
            _ => "Moon hero narration segment."
        };
    }

    if (framePlan.RenderSceneCode.Equals("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase))
    {
        return framePlan.FrameType switch
        {
            CinematicFrameType.HorizonContext => "Western horizon setup narration segment; orients viewers after sunset.",
            CinematicFrameType.BalancedStoryFrame => "Planet grouping story beat; supports Jupiter and Venus viewing guidance.",
            CinematicFrameType.AlignmentWide => "Wide alignment narration segment; closes the grouping with spatial context.",
            _ => "Western planet grouping narration segment."
        };
    }

    return framePlan.NarrationUse;
}

static async Task<string> WriteAICinematicGenerationReportAsync(
    string root,
    AICinematicAssetGenerationSummary aiCinematicAssets,
    IReadOnlyList<string>? aiCinematicImagePaths,
    CancellationToken cancellationToken)
{
    var assetsDirectory = Path.Combine(root, "assets");
    Directory.CreateDirectory(assetsDirectory);
    var safeAICinematicImagePaths = aiCinematicImagePaths ?? [];
    var warnings = safeAICinematicImagePaths.Count == 0
        ? new[] { "AI cinematic images were not generated; continuing with Stellarium/NASA/JWST/motion graphics assets." }
        : Array.Empty<string>();
    var reportPath = Path.Combine(assetsDirectory, "ai-cinematic-generation-report.json");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        aiCinematicDirectionsGenerated = true,
        aiCinematicImagesGenerated = safeAICinematicImagePaths.Count > 0,
        aiCinematicAssetCount = safeAICinematicImagePaths.Count,
        aiCinematicRequiredForSuccess = false,
        status = safeAICinematicImagePaths.Count > 0 ? "Generated" : "SkippedOrMissingButNonFatal",
        warnings,
        errors = Array.Empty<string>()
    }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    return reportPath;
}

static async Task<IReadOnlyList<string>> CollectProductionReadyAICinematicImagePathsAsync(string resultsPath, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(resultsPath) || !File.Exists(resultsPath))
    {
        return [];
    }

    try
    {
        await using var stream = File.OpenRead(resultsPath);
        var results = await JsonSerializer.DeserializeAsync<IReadOnlyList<AICinematicAssetResult>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? [];

        var imagePaths = results
            .Where(result => result.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase) || result.GenerationStatus.Equals("SkippedExistingValid", StringComparison.OrdinalIgnoreCase))
            .Where(result => result.ProductionReady)
            .Where(result => !string.IsNullOrWhiteSpace(result.ImagePath))
            .GroupBy(result => result.ImagePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        foreach (var result in imagePaths)
        {
            logger.LogInformation("AI_CINEMATIC_PRODUCTION_IMAGE_COLLECTED assetCode={AssetCode} imagePath={ImagePath}", result.AssetCode, result.ImagePath);
        }

        return imagePaths.Select(result => result.ImagePath).ToList();
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "AI_CINEMATIC_PRODUCTION_IMAGE_COLLECTION_FAILED resultsPath={ResultsPath}", resultsPath);
        return [];
    }
}

static IReadOnlyList<string> BuildAllProductionImageAssets(
    IReadOnlyList<string> frameScreenshots,
    IReadOnlyList<string> expandedFrameScreenshots,
    IReadOnlyList<string> aiCinematicImagePaths,
    IReadOnlyList<string> nasaOrJwstImagePaths,
    IReadOnlyList<string> motionGraphicsImagePaths,
    Microsoft.Extensions.Logging.ILogger logger)
{
    var safeFrameScreenshots = frameScreenshots ?? [];
    var safeExpandedFrameScreenshots = expandedFrameScreenshots ?? [];
    var safeAICinematicImagePaths = aiCinematicImagePaths ?? [];
    var safeNasaOrJwstImagePaths = nasaOrJwstImagePaths ?? [];
    var safeMotionGraphicsImagePaths = motionGraphicsImagePaths ?? [];

    var allProductionImageAssets = safeFrameScreenshots
        .Concat(safeExpandedFrameScreenshots)
        .Concat(safeAICinematicImagePaths)
        .Concat(safeNasaOrJwstImagePaths)
        .Concat(safeMotionGraphicsImagePaths)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    logger.LogInformation(
        "PRODUCTION_IMAGE_ASSET_LIST_BUILT stellarium={StellariumCount} expanded={ExpandedCount} ai={AICinematicCount}",
        safeFrameScreenshots.Count,
        safeExpandedFrameScreenshots.Count,
        safeAICinematicImagePaths.Count);

    return allProductionImageAssets;
}

static async Task<ExpandedStellariumExecutionSummary> ExecuteExpandedStellariumScenesAsync(
    string root,
    WeeklyAssetExpansionPlan assetExpansionPlan,
    string expandedRenderScenePlanPath,
    WeeklySkyForecastAssetExpansionOptions options,
    WeeklySkyForecastV2IntelligenceResponse weeklyContext,
    ISscIntelligenceService sscIntelligenceService,
    Astronomy.SscIntelligence.SceneIntent.ISceneIntentResolver sceneIntentResolver,
    IStellariumScriptExecutionService sharedStellariumExecutor,
    ISkyfieldTemporalResolver temporalResolver,
    DateTime scheduledUtcFallback,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var episodeDirectory = Path.Combine(root, "episode");
    Directory.CreateDirectory(episodeDirectory);
    var reportPath = Path.Combine(episodeDirectory, "weekly-expanded-stellarium-execution-report.json");
    var renderScenePlanRequirements = await ReadExpandedRenderSceneRequirementsAsync(expandedRenderScenePlanPath, assetExpansionPlan.ExpandedRenderSceneRequirements, cancellationToken);
    var mode = string.IsNullOrWhiteSpace(options.Mode) ? AssetExpansionPolicy.PlanningOnlyMode : options.Mode;
    var requestedCount = renderScenePlanRequirements.Count;
    var expandedScenes = new List<ExpandedStellariumSceneExecution>();
    var skippedScenes = new List<ExpandedStellariumSkippedScene>();
    var warnings = new List<string>();
    var generatedScripts = new List<string>();
    var generatedScreenshots = new List<string>();
    var timedOut = false;
    var partialExecution = false;

    logger.LogInformation("EXPANDED_STELLARIUM_EXECUTION_START mode={Mode} requestedExpandedSceneCount={RequestedCount} maxExpandedScenesPerRun={MaxExpandedScenesPerRun} maxFramesPerExpandedScene={MaxFramesPerExpandedScene} expandedExecutionTimeoutSeconds={ExpandedExecutionTimeoutSeconds}", mode, requestedCount, options.MaxExpandedScenesPerRun, options.MaxFramesPerExpandedScene, options.ExpandedExecutionTimeoutSeconds);

    if (!string.Equals(mode, AssetExpansionPolicy.ExecuteExpandedScenesMode, StringComparison.OrdinalIgnoreCase))
    {
        warnings.Add($"Expanded Stellarium execution skipped because mode is {mode}.");
        await WriteExpandedStellariumExecutionReportAsync(reportPath, requestedCount, expandedScenes, skippedScenes, warnings, partialExecution, timedOut, generatedScripts.Count, generatedScreenshots.Count, CancellationToken.None);
        logger.LogInformation("EXPANDED_STELLARIUM_EXECUTION_REPORT_WRITTEN path={Path}", reportPath);
        logger.LogInformation("EXPANDED_STELLARIUM_EXECUTION_COMPLETE executedExpandedSceneCount=0 skippedExpandedSceneCount={SkippedCount} generatedExpandedSscScriptCount=0 generatedExpandedScreenshotCount=0 partialExecution={PartialExecution} timedOut={TimedOut}", skippedScenes.Count, partialExecution, timedOut);
        return new ExpandedStellariumExecutionSummary(reportPath, false, partialExecution, timedOut, options.MaxExpandedScenesPerRun, options.MaxFramesPerExpandedScene, mode, 0, skippedScenes.Count, 0, 0, [], warnings, false, null, null, null, "Skipped", []);
    }

    try
    {
        var scriptsExpandedRoot = Path.Combine(root, "stellarium", "scripts", "expanded");
        var scenesExpandedRoot = Path.Combine(root, "stellarium", "scenes", "expanded");
        Directory.CreateDirectory(scriptsExpandedRoot);
        Directory.CreateDirectory(scenesExpandedRoot);

        var adapter = new ExpandedRenderSceneRequirementToStellariumSceneAdapter();
        var latitude = weeklyContext.StellariumBlueprintPackage?.Latitude ?? 24.5854d;
        var longitude = weeklyContext.StellariumBlueprintPackage?.Longitude ?? 73.7125d;
        var timezone = weeklyContext.StellariumBlueprintPackage?.Timezone ?? "UTC";
        var locationName = weeklyContext.Region;
        const double elevationMeters = 600d;
        var defaultRules = new VisibilityRules
        {
            MinimumObjectAltitudeDeg = 10d,
            TwilightSunAltitudeThresholdDeg = -12d,
            MaximumMagnitude = 6d,
            MaximumGroupSpreadDeg = 70d
        };
        var skyObjectsByCode = (weeklyContext.EventExtractionResult?.ExtractedEvents ?? [])
            .SelectMany(e => e.Objects)
            .GroupBy(o => o.ObjectCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var expandedRequirementExecutionCandidates = renderScenePlanRequirements
            .Select(requirement => new
            {
                Requirement = requirement,
                ResolvedFrameTypes = ResolveExpandedFrameTypes(requirement.RequiredFrameTypes, requirement.SourceSegmentType, requirement.VisualRole)
            })
            .ToList();

        var selectedRequirements = expandedRequirementExecutionCandidates
            .OrderBy(x => x.Requirement.Priority)
            .Where(x => IsExecutableExpandedRequirement(x.Requirement, x.ResolvedFrameTypes))
            .Take(Math.Max(0, options.MaxExpandedScenesPerRun))
            .Select(x => x.Requirement)
            .ToList();

        foreach (var skipped in expandedRequirementExecutionCandidates.Where(x => !selectedRequirements.Contains(x.Requirement)))
        {
            var reason = ResolveExpandedSkipReason(skipped.Requirement, skipped.ResolvedFrameTypes);
            LogExpandedRequirementSkipped(logger, skipped.Requirement, skipped.ResolvedFrameTypes, reason);
            skippedScenes.Add(new ExpandedStellariumSkippedScene(skipped.Requirement.RenderSceneCode, skipped.Requirement.SourceSegmentType, skipped.Requirement.TargetObjects, reason, skipped.Requirement.Warnings));
        }

        foreach (var requirement in selectedRequirements)
        {
            var sceneScripts = new List<string>();
            var sceneScreenshots = new List<string>();
            var currentRenderSceneCode = requirement.RenderSceneCode;
            var currentSourceSegmentType = requirement.SourceSegmentType;
            var currentTargetObjects = requirement.TargetObjects;

            try
            {
                logger.LogInformation("EXPANDED_REQUIREMENT_SELECTED renderSceneCode={RenderSceneCode} sourceSegmentType={SourceSegmentType} targetObjects={TargetObjects}", requirement.RenderSceneCode, requirement.SourceSegmentType, string.Join(',', requirement.TargetObjects));
                var adapted = adapter.Adapt(requirement, scheduledUtcFallback);
                currentRenderSceneCode = adapted.RenderSceneCode;
                currentSourceSegmentType = adapted.SourceSegmentType;
                currentTargetObjects = adapted.TargetObjects;
                logger.LogInformation("EXPANDED_REQUIREMENT_ADAPTED_TO_SCENE renderSceneCode={RenderSceneCode} visualRole={VisualRole} selectedObservationUtc={SelectedObservationUtc}", adapted.RenderSceneCode, adapted.VisualRole, adapted.PreferredObservationUtc);

                var observationUtc = DateTime.SpecifyKind(adapted.PreferredObservationUtc, DateTimeKind.Utc);
                var selectedObservationLocal = ConvertUtcToLocal(observationUtc, timezone);
                var sceneDateLocal = DateOnly.FromDateTime(selectedObservationLocal);
                var nightGeometry = ResolveExpandedNightGeometry(adapted, weeklyContext, skyObjectsByCode, observationUtc, timezone, latitude, longitude, logger);
                if (!nightGeometry.Ready)
                {
                    var warning = $"NoNightGeometry: expanded scene '{adapted.RenderSceneCode}' has no valid night geometry.";
                    warnings.Add(warning);
                    logger.LogWarning("EXPANDED_NIGHT_GEOMETRY_FAILED renderSceneCode={RenderSceneCode} sourceSegmentType={SourceSegmentType} targetObjects={TargetObjects} reason=NoNightGeometry", adapted.RenderSceneCode, adapted.SourceSegmentType, string.Join(',', adapted.TargetObjects));
                    skippedScenes.Add(new ExpandedStellariumSkippedScene(adapted.RenderSceneCode, adapted.SourceSegmentType, adapted.TargetObjects, "NoNightGeometry", requirement.Warnings.Concat([warning]).ToList()));
                    continue;
                }

                observationUtc = DateTime.SpecifyKind(nightGeometry.SelectedObservationUtc!.Value, DateTimeKind.Utc);
                selectedObservationLocal = nightGeometry.SelectedObservationLocal!.Value;
                sceneDateLocal = DateOnly.FromDateTime(selectedObservationLocal);
                if (!string.IsNullOrWhiteSpace(nightGeometry.SelectedTargetObject) && !adapted.TargetObjects.Contains(nightGeometry.SelectedTargetObject, StringComparer.OrdinalIgnoreCase))
                {
                    adapted = adapted with { TargetObjects = [nightGeometry.SelectedTargetObject] };
                    currentTargetObjects = adapted.TargetObjects;
                }
                logger.LogInformation("EXPANDED_SSC_SELECTED_TIME renderSceneCode={RenderSceneCode} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal} selectedSunAltitudeDeg={SelectedSunAltitudeDeg} nightValidationStatus={NightValidationStatus}", adapted.RenderSceneCode, observationUtc, selectedObservationLocal, nightGeometry.SelectedSunAltitudeDeg, nightGeometry.ValidationStatus);
                var compositionFallback = new
                {
                    CenterAltitude = 30d,
                    CenterAzimuth = 270d,
                    TargetObjects = adapted.TargetObjects,
                    IncludedObjects = adapted.TargetObjects
                };

                var resolvedObservationUtc = observationUtc;
                var skyPositions = adapted.TargetObjects
                    .Select(code =>
                    {
                        skyObjectsByCode.TryGetValue(code, out var obj);
                        var resolution = ResolveExpandedWeeklySkyObjectPosition(code, observationUtc, selectedObservationLocal, sceneDateLocal, timezone, compositionFallback, obj, weeklyContext, adapted.TargetObjects, temporalResolver, logger, adapted.RenderSceneCode);
                        if (resolution.MatchFound && resolution.MatchedTimeUtc.HasValue)
                        {
                            resolvedObservationUtc = resolution.MatchedTimeUtc.Value;
                        }
                        var objectName = obj?.ObjectName ?? code;
                        var objectType = ResolveObjectType(objectName);
                        var weight = ResolveObjectWeight(objectName, objectType, adapted.TargetObjects.FirstOrDefault()?.Equals(code, StringComparison.OrdinalIgnoreCase) == true);
                        return new WeeklySceneObjectSelection(
                            new SkyObjectPosition(objectName, resolution.AltitudeDeg, resolution.AzimuthDeg, resolution.Magnitude, objectType, weight),
                            $"expandedRequirement|{resolution.Source}");
                    })
                    .ToList();
                observationUtc = DateTime.SpecifyKind(resolvedObservationUtc, DateTimeKind.Utc);

                var missingGeometryObjects = skyPositions
                    .Where(x => x.Source.Contains("source=fallback", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Position.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (missingGeometryObjects.Count > 0)
                {
                    var warning = $"SkippedMissingGeometry: expanded scene '{adapted.RenderSceneCode}' missing geometry for [{string.Join(',', missingGeometryObjects)}].";
                    warnings.Add(warning);
                    logger.LogInformation("EXPANDED_REQUIREMENT_SKIPPED renderSceneCode={RenderSceneCode} renderEngine={RenderEngine} geometryAvailable={GeometryAvailable} targetObjects={TargetObjects} requiredFrameTypes={RequiredFrameTypes} productionStatus={ProductionStatus} reason=SkippedMissingGeometry skipReason=SkippedMissingGeometry missingObjects={MissingObjects}", adapted.RenderSceneCode, requirement.RenderEngine, requirement.GeometryAvailable, string.Join(',', adapted.TargetObjects), string.Join(',', ResolveExpandedFrameTypeNames(adapted.RequiredFrameTypes, adapted.SourceSegmentType, adapted.VisualRole)), requirement.ProductionStatus, string.Join(',', missingGeometryObjects));
                    skippedScenes.Add(new ExpandedStellariumSkippedScene(adapted.RenderSceneCode, adapted.SourceSegmentType, adapted.TargetObjects, "SkippedMissingGeometry", requirement.Warnings.Concat([warning]).ToList()));
                    continue;
                }

                var sceneIntent = sceneIntentResolver.Resolve(adapted.RenderSceneCode, adapted.DesiredCameraIntent);
                var sceneDirectory = Path.Combine(scenesExpandedRoot, adapted.RenderSceneCode);
                var scriptDirectory = Path.Combine(scriptsExpandedRoot, adapted.RenderSceneCode);
                Directory.CreateDirectory(sceneDirectory);
                Directory.CreateDirectory(scriptDirectory);
                var sscResult = sscIntelligenceService.Generate(new SscIntelligenceRequest(
                    observationUtc,
                    longitude,
                    latitude,
                    elevationMeters,
                    locationName,
                    skyPositions.Select(x => x.Position).ToList(),
                    defaultRules,
                    nightGeometry.SelectedSunAltitudeDeg,
                    timezone,
                    null,
                    null,
                    sceneIntent,
                    adapted.RenderSceneCode,
                    adapted.DesiredCameraIntent,
                    adapted.TargetObjects),
                    sceneDirectory,
                    adapted.RenderSceneCode);

                if (Math.Abs(sscResult.CameraAltitudeDeg - 30d) < 0.0001d && Math.Abs(sscResult.CameraAzimuthDeg - 270d) < 0.0001d)
                {
                    var warning = $"SkippedMissingGeometry: expanded scene '{adapted.RenderSceneCode}' produced fallback camera geometry.";
                    warnings.Add(warning);
                    logger.LogInformation("EXPANDED_REQUIREMENT_SKIPPED renderSceneCode={RenderSceneCode} renderEngine={RenderEngine} geometryAvailable={GeometryAvailable} targetObjects={TargetObjects} requiredFrameTypes={RequiredFrameTypes} productionStatus={ProductionStatus} reason=SkippedMissingGeometry skipReason=SkippedMissingGeometry", adapted.RenderSceneCode, requirement.RenderEngine, requirement.GeometryAvailable, string.Join(',', adapted.TargetObjects), string.Join(',', ResolveExpandedFrameTypeNames(adapted.RequiredFrameTypes, adapted.SourceSegmentType, adapted.VisualRole)), requirement.ProductionStatus);
                    skippedScenes.Add(new ExpandedStellariumSkippedScene(adapted.RenderSceneCode, adapted.SourceSegmentType, adapted.TargetObjects, "SkippedMissingGeometry", requirement.Warnings.Concat([warning]).ToList()));
                    continue;
                }

                var frameTypes = ResolveExpandedFrameTypes(adapted.RequiredFrameTypes, adapted.SourceSegmentType, adapted.VisualRole)
                    .Take(Math.Max(1, options.MaxFramesPerExpandedScene))
                    .ToList();
                var frameIndex = 0;
                foreach (var frameType in frameTypes)
                {
                    frameIndex++;
                    var variant = ResolveExpandedFrameVariant(frameType);
                    var fov = Math.Clamp(sscResult.FovDeg * variant.FovScale, variant.MinFov, variant.MaxFov);
                    var scriptName = $"{frameIndex:00}_{variant.Name}.ssc";
                    var imageName = $"{frameIndex:00}_{variant.Name}.png";
                    var scriptPath = ResolveExpandedOutputPath(scriptDirectory, scriptName);
                    var imagePath = ResolveExpandedOutputPath(sceneDirectory, imageName);
                    var frameSsc = sscResult.SscScript
                        .Replace($"core.moveToAltAzi({sscResult.CameraAltitudeDeg.ToString("0.###", CultureInfo.InvariantCulture)}, {sscResult.CameraAzimuthDeg.ToString("0.###", CultureInfo.InvariantCulture)}", $"core.moveToAltAzi({sscResult.CameraAltitudeDeg.ToString("0.###", CultureInfo.InvariantCulture)}, {sscResult.CameraAzimuthDeg.ToString("0.###", CultureInfo.InvariantCulture)}", StringComparison.Ordinal)
                        .Replace($"StelMovementMgr.zoomTo({sscResult.FovDeg.ToString("0.###", CultureInfo.InvariantCulture)}", $"StelMovementMgr.zoomTo({fov.ToString("0.###", CultureInfo.InvariantCulture)}", StringComparison.Ordinal);
                    frameSsc = Regex.Replace(
                        frameSsc,
                        @"core\.screenshot\([^\)]*\);",
                        $"core.screenshot(\"{Path.GetFileNameWithoutExtension(imagePath).Replace("\"", "\\\"")}\", false, \"{sceneDirectory.Replace("\\", "/").Replace("\"", "\\\"")}\", true, \"png\");",
                        RegexOptions.CultureInvariant);
                    frameSsc = string.Join(Environment.NewLine,
                        $"// ExpandedSelectedObservationUtc: {observationUtc:O}",
                        $"// ExpandedSelectedObservationLocal: {selectedObservationLocal:O}",
                        $"// ExpandedSelectedSunAltitudeDeg: {nightGeometry.SelectedSunAltitudeDeg?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null"}",
                        $"// ExpandedNightValidationStatus: {nightGeometry.ValidationStatus}",
                        frameSsc);
                    ValidateExpandedSscScript(frameSsc, scriptPath);
                    await File.WriteAllTextAsync(scriptPath, frameSsc, cancellationToken);
                    sceneScripts.Add(scriptPath);
                    generatedScripts.Add(scriptPath);
                    logger.LogInformation("EXPANDED_SSC_GENERATED renderSceneCode={RenderSceneCode} frameType={FrameType} scriptPath={ScriptPath}", adapted.RenderSceneCode, frameType, scriptPath);

                    await sharedStellariumExecutor.ExecuteAsync(root, scriptPath, imagePath, 180, cancellationToken);
                    if (!File.Exists(imagePath) || new FileInfo(imagePath).Length == 0)
                        throw new InvalidOperationException($"Expected expanded screenshot was not generated: {imagePath}");
                    if (!ValidateExpandedScreenshotNightImage(imagePath))
                    {
                        logger.LogWarning("EXPANDED_SCREENSHOT_NIGHT_VALIDATION_FAILED renderSceneCode={RenderSceneCode} frameType={FrameType} screenshotPath={ScreenshotPath} reason=DaylightSkyDetected", adapted.RenderSceneCode, frameType, imagePath);
                        throw new InvalidOperationException("DaylightSkyDetected");
                    }
                    logger.LogInformation("EXPANDED_SCREENSHOT_NIGHT_VALIDATION_PASSED renderSceneCode={RenderSceneCode} frameType={FrameType} screenshotPath={ScreenshotPath}", adapted.RenderSceneCode, frameType, imagePath);
                    sceneScreenshots.Add(imagePath);
                    generatedScreenshots.Add(imagePath);
                    logger.LogInformation("EXPANDED_SCREENSHOT_CAPTURED renderSceneCode={RenderSceneCode} frameType={FrameType} screenshotPath={ScreenshotPath}", adapted.RenderSceneCode, frameType, imagePath);
                }

                expandedScenes.Add(new ExpandedStellariumSceneExecution(adapted.RenderSceneCode, adapted.SourceSegmentType, adapted.TargetObjects, sceneScripts, sceneScreenshots, "Executed", true, observationUtc, selectedObservationLocal, nightGeometry.SelectedSunAltitudeDeg, nightGeometry.ValidationStatus));
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                partialExecution = true;
                var warning = $"Expanded Stellarium execution timed out while processing scene '{currentRenderSceneCode}': {ex.Message}";
                warnings.Add(warning);
                logger.LogWarning(ex, "EXPANDED_STELLARIUM_EXECUTION_TIMED_OUT renderSceneCode={RenderSceneCode} generatedExpandedScreenshotCount={GeneratedExpandedScreenshotCount}", currentRenderSceneCode, generatedScreenshots.Count);
                if (sceneScripts.Count > 0 || sceneScreenshots.Count > 0)
                {
                    expandedScenes.Add(new ExpandedStellariumSceneExecution(currentRenderSceneCode, currentSourceSegmentType, currentTargetObjects, sceneScripts, sceneScreenshots, "Partial", sceneScreenshots.Count > 0, null, null, null, "Partial"));
                }
                if (!options.ContinueOnFailure)
                    throw;
                break;
            }
            catch (Exception ex) when (options.ContinueOnFailure)
            {
                partialExecution = true;
                var warning = $"Expanded scene '{requirement.RenderSceneCode}' failed: {ex.Message}";
                warnings.Add(warning);
                logger.LogWarning(ex, "EXPANDED_REQUIREMENT_SKIPPED renderSceneCode={RenderSceneCode} renderEngine={RenderEngine} geometryAvailable={GeometryAvailable} targetObjects={TargetObjects} requiredFrameTypes={RequiredFrameTypes} productionStatus={ProductionStatus} reason=ExecutionFailed skipReason=ExecutionFailed", requirement.RenderSceneCode, requirement.RenderEngine, requirement.GeometryAvailable, string.Join(',', requirement.TargetObjects), string.Join(',', ResolveExpandedFrameTypeNames(requirement.RequiredFrameTypes, requirement.SourceSegmentType, requirement.VisualRole)), requirement.ProductionStatus);
                if (sceneScripts.Count > 0 || sceneScreenshots.Count > 0)
                {
                    expandedScenes.Add(new ExpandedStellariumSceneExecution(currentRenderSceneCode, currentSourceSegmentType, currentTargetObjects, sceneScripts, sceneScreenshots, "Partial", sceneScreenshots.Count > 0, null, null, null, "Partial"));
                }
                skippedScenes.Add(new ExpandedStellariumSkippedScene(requirement.RenderSceneCode, requirement.SourceSegmentType, requirement.TargetObjects, "ExecutionFailed", requirement.Warnings.Concat([warning]).ToList()));
            }
        }
    }
    catch (OperationCanceledException ex) when (options.ContinueOnFailure)
    {
        timedOut = true;
        partialExecution = true;
        var warning = $"Expanded Stellarium execution timed out: {ex.Message}";
        warnings.Add(warning);
        logger.LogWarning(ex, "EXPANDED_STELLARIUM_EXECUTION_TIMED_OUT generatedExpandedScreenshotCount={GeneratedExpandedScreenshotCount}", generatedScreenshots.Count);
    }
    finally
    {
        partialExecution = partialExecution || timedOut;
        await WriteExpandedStellariumExecutionReportAsync(reportPath, requestedCount, expandedScenes, skippedScenes, warnings, partialExecution, timedOut, generatedScripts.Count, generatedScreenshots.Count, CancellationToken.None);
        logger.LogInformation("EXPANDED_STELLARIUM_EXECUTION_REPORT_WRITTEN path={Path}", reportPath);
    }

    var failedExpandedAssetReasons = warnings.Where(x => x.Contains("NoNightGeometry", StringComparison.OrdinalIgnoreCase) || x.Contains("Daylight", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var selectedNightScene = expandedScenes.FirstOrDefault(x => x.SelectedObservationUtc.HasValue);
    var expandedNightGeometryReady = expandedScenes.Count > 0 && expandedScenes.All(x => x.SelectedSunAltitudeDeg.HasValue && x.SelectedSunAltitudeDeg.Value <= ResolveExpandedRequiredSunAltitudeThreshold(x.RenderSceneCode, x.SourceSegmentType, x.TargetObjects, []));
    var expandedNightValidationStatus = expandedNightGeometryReady ? "Passed" : (failedExpandedAssetReasons.Count > 0 ? "Failed" : "NotValidated");
    var ready = generatedScreenshots.Count > 0 && generatedScripts.Count > 0 && expandedNightGeometryReady;
    logger.LogInformation("EXPANDED_STELLARIUM_EXECUTION_COMPLETE executedExpandedSceneCount={ExecutedCount} skippedExpandedSceneCount={SkippedCount} generatedExpandedSscScriptCount={ScriptCount} generatedExpandedScreenshotCount={ScreenshotCount} partialExecution={PartialExecution} timedOut={TimedOut}", expandedScenes.Count, skippedScenes.Count, generatedScripts.Count, generatedScreenshots.Count, partialExecution, timedOut);

    return new ExpandedStellariumExecutionSummary(reportPath, ready, partialExecution, timedOut, options.MaxExpandedScenesPerRun, options.MaxFramesPerExpandedScene, mode, expandedScenes.Count, skippedScenes.Count, generatedScripts.Count, generatedScreenshots.Count, generatedScreenshots, warnings, expandedNightGeometryReady, selectedNightScene?.SelectedObservationUtc, selectedNightScene?.SelectedObservationLocal, selectedNightScene?.SelectedSunAltitudeDeg, expandedNightValidationStatus, failedExpandedAssetReasons);
}


static ExpandedNightGeometrySelection ResolveExpandedNightGeometry(
    ExpandedStellariumSceneRequirement requirement,
    WeeklySkyForecastV2IntelligenceResponse weeklyContext,
    IReadOnlyDictionary<string, WeeklyAstronomyEventObject> skyObjectsByCode,
    DateTime preferredObservationUtc,
    string timezone,
    double latitude,
    double longitude,
    Microsoft.Extensions.Logging.ILogger logger)
{
    var threshold = ResolveExpandedRequiredSunAltitudeThreshold(requirement.RenderSceneCode, requirement.SourceSegmentType, requirement.TargetObjects, requirement.RequiredFrameTypes);
    var preferredThreshold = -12d;
    logger.LogInformation("EXPANDED_NIGHT_GEOMETRY_START renderSceneCode={RenderSceneCode} sourceSegmentType={SourceSegmentType} targetObjects={TargetObjects} preferredObservationUtc={PreferredObservationUtc} requiredSunAltitudeDeg={RequiredSunAltitudeDeg}", requirement.RenderSceneCode, requirement.SourceSegmentType, string.Join(',', requirement.TargetObjects), preferredObservationUtc, threshold);

    var extractedEvents = weeklyContext.EventExtractionResult?.ExtractedEvents ?? [];
    var candidatesByTarget = new List<ExpandedNightGeometryCandidate>();
    foreach (var target in requirement.TargetObjects.Where(x => !string.IsNullOrWhiteSpace(x)))
    {
        skyObjectsByCode.TryGetValue(target, out var obj);
        var aliases = ResolveWeeklyObjectAliases(target, obj?.ObjectName);
        var objectCandidates = WeeklySkyfieldObjectHydration.BuildTemporalCandidates(
            extractedEvents,
            aliases,
            e => ResolveEventUtc(e),
            name => NormalizeWeeklyObjectName(name),
            (code, name, candidateAliases) => MatchesWeeklyObjectAliases(code, name, candidateAliases),
            logger,
            requirement.RenderSceneCode,
            obj?.ObjectName ?? target);
        foreach (var candidate in objectCandidates)
        {
            var utc = DateTime.SpecifyKind(candidate.SnapshotUtc, DateTimeKind.Utc);
            var local = ConvertUtcToLocal(utc, timezone);
            var sunAltitude = CalculateSolarAltitudeDeg(utc, latitude, longitude);
            var expandedCandidate = new ExpandedNightGeometryCandidate(target, utc, local, candidate.AltitudeDegrees, candidate.AzimuthDegrees, sunAltitude);
            candidatesByTarget.Add(expandedCandidate);
            logger.LogInformation("EXPANDED_NIGHT_GEOMETRY_CANDIDATE renderSceneCode={RenderSceneCode} targetObject={TargetObject} candidateUtc={CandidateUtc} candidateLocal={CandidateLocal} objectAltitudeDeg={ObjectAltitudeDeg} objectAzimuthDeg={ObjectAzimuthDeg} sunAltitudeDeg={SunAltitudeDeg}", requirement.RenderSceneCode, target, utc, local, candidate.AltitudeDegrees, candidate.AzimuthDegrees, sunAltitude);
            if (sunAltitude > -6d)
                logger.LogInformation("EXPANDED_NIGHT_GEOMETRY_REJECTED_DAYLIGHT renderSceneCode={RenderSceneCode} targetObject={TargetObject} candidateUtc={CandidateUtc} sunAltitudeDeg={SunAltitudeDeg}", requirement.RenderSceneCode, target, utc, sunAltitude);
        }
    }

    ExpandedNightGeometryCandidate? selected;
    if (IsExpandedAstrophotographyScene(requirement.RenderSceneCode, requirement.SourceSegmentType) && requirement.TargetObjects.Any(IsMoonTarget))
    {
        var moonCandidates = candidatesByTarget.Where(x => IsMoonTarget(x.TargetObject)).ToList();
        selected = SelectExpandedNightCandidate(moonCandidates, preferredObservationUtc, timezone, -12d, requirement, requireMoonNight: true);
        if (selected is null)
        {
            var planetCandidates = BuildExpandedPlanetNightCandidates(extractedEvents, preferredObservationUtc, timezone, latitude, longitude, logger, requirement.RenderSceneCode);
            selected = SelectExpandedNightCandidate(planetCandidates, preferredObservationUtc, timezone, -12d, requirement, requireMoonNight: false);
        }
    }
    else
    {
        selected = SelectExpandedNightCandidate(candidatesByTarget, preferredObservationUtc, timezone, preferredThreshold, requirement, requireMoonNight: false)
            ?? (threshold > preferredThreshold ? SelectExpandedNightCandidate(candidatesByTarget, preferredObservationUtc, timezone, threshold, requirement, requireMoonNight: false) : null);
    }

    if (selected is null)
        return new ExpandedNightGeometrySelection(false, null, null, null, null, "NoNightGeometry");

    logger.LogInformation("EXPANDED_NIGHT_GEOMETRY_SELECTED renderSceneCode={RenderSceneCode} targetObject={TargetObject} selectedObservationUtc={SelectedObservationUtc} selectedObservationLocal={SelectedObservationLocal} selectedSunAltitudeDeg={SelectedSunAltitudeDeg} objectAltitudeDeg={ObjectAltitudeDeg}", requirement.RenderSceneCode, selected.TargetObject, selected.ObservationUtc, selected.ObservationLocal, selected.SunAltitudeDeg, selected.ObjectAltitudeDeg);
    return new ExpandedNightGeometrySelection(true, selected.ObservationUtc, selected.ObservationLocal, selected.SunAltitudeDeg, selected.TargetObject, "Passed");
}

static ExpandedNightGeometryCandidate? SelectExpandedNightCandidate(
    IReadOnlyList<ExpandedNightGeometryCandidate> candidates,
    DateTime preferredObservationUtc,
    string timezone,
    double threshold,
    ExpandedStellariumSceneRequirement requirement,
    bool requireMoonNight)
{
    var valid = candidates
        .Where(x => x.SunAltitudeDeg <= threshold && x.ObjectAltitudeDeg > 0d)
        .Where(x => !requireMoonNight || (IsMoonTarget(x.TargetObject) && x.ObjectAltitudeDeg > 15d && IsEveningNightLocal(x.ObservationLocal)))
        .ToList();
    if (valid.Count == 0) return null;

    var exact = valid.FirstOrDefault(x => x.ObservationUtc == preferredObservationUtc);
    if (exact is not null) return exact;

    var preferredLocalDate = DateOnly.FromDateTime(ConvertUtcToLocal(preferredObservationUtc, timezone));
    var sameDate = valid
        .Where(x => DateOnly.FromDateTime(x.ObservationLocal) == preferredLocalDate)
        .OrderBy(x => Math.Abs((x.ObservationUtc - preferredObservationUtc).TotalMinutes))
        .FirstOrDefault();
    if (sameDate is not null) return sameDate;

    var bestForTarget = valid
        .Where(x => requirement.TargetObjects.Contains(x.TargetObject, StringComparer.OrdinalIgnoreCase))
        .OrderByDescending(x => x.ObjectAltitudeDeg)
        .FirstOrDefault();
    if (bestForTarget is not null) return bestForTarget;

    return valid.OrderByDescending(x => x.ObjectAltitudeDeg).FirstOrDefault();
}

static IReadOnlyList<ExpandedNightGeometryCandidate> BuildExpandedPlanetNightCandidates(
    IReadOnlyList<WeeklyAstronomyEvent> extractedEvents,
    DateTime preferredObservationUtc,
    string timezone,
    double latitude,
    double longitude,
    Microsoft.Extensions.Logging.ILogger logger,
    string sceneCode)
{
    var planets = new[] { "venus", "jupiter", "saturn", "mars", "mercury" };
    var results = new List<ExpandedNightGeometryCandidate>();
    foreach (var planet in planets)
    {
        var aliases = ResolveWeeklyObjectAliases(planet, planet);
        foreach (var candidate in WeeklySkyfieldObjectHydration.BuildTemporalCandidates(extractedEvents, aliases, e => ResolveEventUtc(e), name => NormalizeWeeklyObjectName(name), (code, name, candidateAliases) => MatchesWeeklyObjectAliases(code, name, candidateAliases), logger, sceneCode, planet))
        {
            var utc = DateTime.SpecifyKind(candidate.SnapshotUtc, DateTimeKind.Utc);
            var local = ConvertUtcToLocal(utc, timezone);
            results.Add(new ExpandedNightGeometryCandidate(planet, utc, local, candidate.AltitudeDegrees, candidate.AzimuthDegrees, CalculateSolarAltitudeDeg(utc, latitude, longitude)));
        }
    }
    return results;
}

static double ResolveExpandedRequiredSunAltitudeThreshold(string renderSceneCode, string sourceSegmentType, IReadOnlyList<string> targetObjects, IReadOnlyList<string> frameTypes)
{
    if (IsExpandedAstrophotographyScene(renderSceneCode, sourceSegmentType)) return -12d;
    var text = string.Join(' ', new[] { renderSceneCode, sourceSegmentType }.Concat(targetObjects).Concat(frameTypes)).ToLowerInvariant();
    return text.Contains("horizon", StringComparison.OrdinalIgnoreCase) || text.Contains("planet", StringComparison.OrdinalIgnoreCase) || targetObjects.Any(IsPlanetTarget) ? -6d : -12d;
}

static bool IsExpandedAstrophotographyScene(string renderSceneCode, string sourceSegmentType)
    => renderSceneCode.Contains("astrophotography_target_scene", StringComparison.OrdinalIgnoreCase)
       || sourceSegmentType.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase)
       || sourceSegmentType.Contains("astro", StringComparison.OrdinalIgnoreCase);

static bool IsMoonTarget(string value) => NormalizeWeeklyObjectName(value) is "moon" or "luna";
static bool IsPlanetTarget(string value) => NormalizeWeeklyObjectName(value) is "venus" or "jupiter" or "saturn" or "mars" or "mercury";
static bool IsEveningNightLocal(DateTime local) => local.Hour >= 18 || local.Hour <= 5;

static double CalculateSolarAltitudeDeg(DateTime utc, double latitudeDeg, double longitudeDeg)
{
    utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    var day = utc.DayOfYear;
    var hour = utc.Hour + utc.Minute / 60d + utc.Second / 3600d;
    var gamma = 2d * Math.PI / 365d * (day - 1 + (hour - 12d) / 24d);
    var decl = 0.006918d - 0.399912d * Math.Cos(gamma) + 0.070257d * Math.Sin(gamma) - 0.006758d * Math.Cos(2d * gamma) + 0.000907d * Math.Sin(2d * gamma) - 0.002697d * Math.Cos(3d * gamma) + 0.00148d * Math.Sin(3d * gamma);
    var eqtime = 229.18d * (0.000075d + 0.001868d * Math.Cos(gamma) - 0.032077d * Math.Sin(gamma) - 0.014615d * Math.Cos(2d * gamma) - 0.040849d * Math.Sin(2d * gamma));
    var trueSolarMinutes = (hour * 60d + eqtime + 4d * longitudeDeg) % 1440d;
    if (trueSolarMinutes < 0d) trueSolarMinutes += 1440d;
    var hourAngleDeg = trueSolarMinutes / 4d - 180d;
    var lat = latitudeDeg * Math.PI / 180d;
    var ha = hourAngleDeg * Math.PI / 180d;
    var cosZenith = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
    cosZenith = Math.Clamp(cosZenith, -1d, 1d);
    return 90d - Math.Acos(cosZenith) * 180d / Math.PI;
}

static bool ValidateExpandedScreenshotNightImage(string imagePath)
{
    try
    {
        using var image = Image.Load<Rgba32>(imagePath);
        var xStep = Math.Max(1, image.Width / 96);
        var yStep = Math.Max(1, image.Height / 54);
        var count = 0;
        var brightBlue = 0;
        var dark = 0;
        var lumaSum = 0d;
        for (var y = 0; y < image.Height; y += yStep)
        {
            for (var x = 0; x < image.Width; x += xStep)
            {
                var p = image[x, y];
                var luma = 0.2126d * p.R + 0.7152d * p.G + 0.0722d * p.B;
                lumaSum += luma;
                if (luma < 95d) dark++;
                if (p.B > p.R + 25 && p.B > p.G + 5 && luma > 85d) brightBlue++;
                count++;
            }
        }
        if (count == 0) return false;
        var mean = lumaSum / count;
        var darkRatio = (double)dark / count;
        var blueRatio = (double)brightBlue / count;
        return mean < 145d && darkRatio > 0.25d && blueRatio < 0.35d;
    }
    catch
    {
        return false;
    }
}

static async Task<IReadOnlyList<ExpandedRenderSceneRequirement>> ReadExpandedRenderSceneRequirementsAsync(
    string expandedRenderScenePlanPath,
    IReadOnlyList<ExpandedRenderSceneRequirement> fallbackRequirements,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(expandedRenderScenePlanPath) || !File.Exists(expandedRenderScenePlanPath))
        return fallbackRequirements;

    await using var stream = File.OpenRead(expandedRenderScenePlanPath);
    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    if (!document.RootElement.TryGetProperty("requirements", out var requirementsElement))
        return fallbackRequirements;

    return requirementsElement.Deserialize<List<ExpandedRenderSceneRequirement>>(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? fallbackRequirements;
}

static bool IsExecutableExpandedRequirement(ExpandedRenderSceneRequirement requirement, IReadOnlyList<CinematicFrameType> resolvedFrameTypes) =>
    string.Equals(requirement.RenderEngine, "Stellarium", StringComparison.OrdinalIgnoreCase)
    && requirement.GeometryAvailable
    && requirement.TargetObjects.Any(x => !string.IsNullOrWhiteSpace(x))
    && IsAllowedExpandedProductionStatus(requirement.ProductionStatus);

static bool IsAllowedExpandedProductionStatus(string productionStatus) =>
    string.Equals(productionStatus, "RequirementReadyForPlanningOnly", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "Planned", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "Required", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "ReadyForExecution", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "ReadyForRender", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "Executable", StringComparison.OrdinalIgnoreCase)
    || string.Equals(productionStatus, "NeedsAssetGeneration", StringComparison.OrdinalIgnoreCase);

static string ResolveExpandedSkipReason(ExpandedRenderSceneRequirement requirement, IReadOnlyList<CinematicFrameType> resolvedFrameTypes)
{
    if (!string.Equals(requirement.RenderEngine, "Stellarium", StringComparison.OrdinalIgnoreCase)) return "SkippedNonStellarium";
    if (!requirement.GeometryAvailable) return "SkippedMissingGeometry";
    if (!requirement.TargetObjects.Any(x => !string.IsNullOrWhiteSpace(x))) return "SkippedNoTargetObjects";
    if (!IsAllowedExpandedProductionStatus(requirement.ProductionStatus)) return "SkippedNotExecutable";
    if (resolvedFrameTypes.Count == 0) return "SkippedNoFrameTypes";
    return "SkippedByRunLimit";
}

static void LogExpandedRequirementSkipped(Microsoft.Extensions.Logging.ILogger logger, ExpandedRenderSceneRequirement requirement, IReadOnlyList<CinematicFrameType> resolvedFrameTypes, string skipReason)
{
    logger.LogInformation(
        "EXPANDED_REQUIREMENT_SKIPPED renderSceneCode={RenderSceneCode} renderEngine={RenderEngine} geometryAvailable={GeometryAvailable} targetObjects={TargetObjects} requiredFrameTypes={RequiredFrameTypes} productionStatus={ProductionStatus} reason={Reason} skipReason={SkipReason}",
        requirement.RenderSceneCode,
        requirement.RenderEngine,
        requirement.GeometryAvailable,
        string.Join(',', requirement.TargetObjects.Where(x => !string.IsNullOrWhiteSpace(x))),
        string.Join(',', FormatExpandedFrameTypeNames(resolvedFrameTypes)),
        requirement.ProductionStatus,
        skipReason,
        skipReason);
}

static IReadOnlyList<string> ResolveExpandedFrameTypeNames(IReadOnlyList<string> requestedFrameTypes, string sourceSegmentType, string visualRole) =>
    FormatExpandedFrameTypeNames(ResolveExpandedFrameTypes(requestedFrameTypes, sourceSegmentType, visualRole));

static IReadOnlyList<string> FormatExpandedFrameTypeNames(IReadOnlyList<CinematicFrameType> frameTypes) =>
    frameTypes.Select(x => x.ToString()).ToList();

static IReadOnlyList<CinematicFrameType> ResolveExpandedFrameTypes(IReadOnlyList<string> requestedFrameTypes, string sourceSegmentType, string visualRole)
{
    var parsed = requestedFrameTypes
        .Select(x => Enum.TryParse<CinematicFrameType>(x, ignoreCase: true, out var parsedType) ? parsedType : (CinematicFrameType?)null)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .Distinct()
        .ToList();
    if (parsed.Count > 0) return parsed;

    return ResolveExpandedFallbackFrameTypes(sourceSegmentType, visualRole);
}

static IReadOnlyList<CinematicFrameType> ResolveExpandedFallbackFrameTypes(string sourceSegmentType, string visualRole)
{
    var segmentType = string.IsNullOrWhiteSpace(sourceSegmentType) ? visualRole : sourceSegmentType;
    if (string.IsNullOrWhiteSpace(segmentType)) segmentType = visualRole;

    return segmentType?.Trim().ToLowerInvariant() switch
    {
        "heroevent" => [CinematicFrameType.EstablishingWide, CinematicFrameType.BalancedStoryFrame, CinematicFrameType.HeroCloseup],
        "moonhighlights" => [CinematicFrameType.EstablishingWide, CinematicFrameType.BalancedStoryFrame, CinematicFrameType.HeroCloseup],
        "planethighlights" => [CinematicFrameType.HorizonContext, CinematicFrameType.BalancedStoryFrame, CinematicFrameType.AlignmentWide],
        "weeklyskyoverview" => [CinematicFrameType.EstablishingWide, CinematicFrameType.BalancedStoryFrame],
        "bestobservationwindow" => [CinematicFrameType.HorizonContext, CinematicFrameType.DirectionGuide],
        "astrophotographytip" => [CinematicFrameType.BalancedStoryFrame, CinematicFrameType.HeroCloseup],
        "wheretolook" => [CinematicFrameType.HorizonContext, CinematicFrameType.DirectionGuide],
        _ => [CinematicFrameType.EstablishingWide, CinematicFrameType.BalancedStoryFrame]
    };
}

static (string Name, double FovScale, double MinFov, double MaxFov) ResolveExpandedFrameVariant(CinematicFrameType frameType) => frameType switch
{
    CinematicFrameType.EstablishingWide => ("establishing_wide", 1.30d, 15d, 75d),
    CinematicFrameType.BalancedStoryFrame => ("balanced_story_frame", 1.00d, 15d, 75d),
    CinematicFrameType.HeroCloseup => ("hero_closeup", 0.65d, 18d, 75d),
    CinematicFrameType.HorizonContext => ("horizon_context", 1.25d, 15d, 65d),
    CinematicFrameType.AlignmentWide => ("alignment_wide", 1.15d, 15d, 60d),
    CinematicFrameType.DirectionGuide => ("direction_guide", 1.10d, 15d, 70d),
    _ => (frameType.ToString().ToLowerInvariant(), 1.00d, 15d, 75d)
};



static string ResolveExpandedOutputPath(string directory, string fileName)
{
    var path = Path.Combine(directory, fileName);
    if (!File.Exists(path)) return path;

    var extension = Path.GetExtension(fileName);
    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
    for (var suffix = 2; suffix < 10_000; suffix++)
    {
        var candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffix:00}{extension}");
        if (!File.Exists(candidate)) return candidate;
    }

    throw new IOException($"Unable to allocate a unique expanded Stellarium output path in '{directory}' for '{fileName}'.");
}

static void ValidateExpandedSscScript(string scriptContent, string scriptPath)
{
    var requiredSnippets = new[]
    {
        "core.screenshot(",
        "core.quitStellarium();",
        "ConstellationMgr.setFlagLines(true);",
        "ConstellationMgr.setFlagLabels(true);",
        "SolarSystem.setFlagLabels(true);",
        "core.moveToAltAzi",
        "StelMovementMgr.zoomTo"
    };
    foreach (var snippet in requiredSnippets)
    {
        if (!scriptContent.Contains(snippet, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expanded SSC script validation failed for '{scriptPath}'; missing required token '{snippet}'.");
    }
}

static async Task WriteExpandedStellariumExecutionReportAsync(
    string reportPath,
    int requestedCount,
    IReadOnlyList<ExpandedStellariumSceneExecution> expandedScenes,
    IReadOnlyList<ExpandedStellariumSkippedScene> skippedScenes,
    IReadOnlyList<string> warnings,
    bool partialExecution,
    bool timedOut,
    int generatedExpandedSscScriptCount,
    int generatedExpandedScreenshotCount,
    CancellationToken cancellationToken)
{
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        requestedExpandedSceneCount = requestedCount,
        executedExpandedSceneCount = expandedScenes.Count,
        skippedExpandedSceneCount = skippedScenes.Count,
        generatedExpandedSscScriptCount,
        generatedExpandedScreenshotCount,
        partialExecution,
        timedOut,
        expandedScenes,
        skippedScenes,
        warnings
    }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}

static string ResolveDynamicEventType(string? sceneType, string sceneCode, IReadOnlyList<string> targetObjects)
{
    var haystack = $"{sceneType} {sceneCode}";
    if (haystack.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "MeteorShower";
    if (haystack.Contains("parade", StringComparison.OrdinalIgnoreCase)) return "PlanetParade";
    if (haystack.Contains("conjunction", StringComparison.OrdinalIgnoreCase)) return "Conjunction";
    if (targetObjects.Any(x => string.Equals(x, "MOON", StringComparison.OrdinalIgnoreCase)) && targetObjects.Count == 1) return "MoonEvent";
    if (targetObjects.Count >= 3) return "MultiObjectGrouping";
    if (targetObjects.Count == 2) return "Conjunction";
    return "SingleObject";
}

static void ValidateWeeklyDynamicFramingPlan(WeeklyDynamicFramingPlan plan)
{
    if (plan.RequestedObjects.Count == 0) throw new InvalidOperationException($"Dynamic framing failed: {plan.SceneCode} has no targetObjects.");
    if (!plan.RequestedObjects.All(x => plan.ResolvedObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
        throw new InvalidOperationException($"Dynamic framing failed: {plan.SceneCode} did not resolve all target objects. requested=[{string.Join(',', plan.RequestedObjects)}] resolved=[{string.Join(',', plan.ResolvedObjects)}]");
    foreach (var scene in plan.SplitRequired ? plan.Clusters : (IReadOnlyList<WeeklyDynamicSceneContract>)new[] { plan.ToSceneContract() })
        ValidateWeeklyDynamicSceneContract(scene);
}

static void ValidateWeeklyDynamicSceneContract(WeeklyDynamicSceneContract scene)
{
    if (scene.TargetObjects.Count == 0) throw new InvalidOperationException($"Dynamic scene target lock failed: {scene.SceneCode} has no target objects.");
    if (!scene.TargetObjects.All(x => scene.ResolvedObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
        throw new InvalidOperationException($"Dynamic scene target lock failed: {scene.SceneCode} resolvedObjects does not include all targetObjects.");
    if (string.IsNullOrWhiteSpace(scene.PrimaryObject) || !scene.TargetObjects.Contains(scene.PrimaryObject, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Dynamic scene target lock failed: {scene.SceneCode} primaryObject '{scene.PrimaryObject}' is not in targetObjects [{string.Join(',', scene.TargetObjects)}].");
    if (!scene.TargetObjects.All(x => scene.CameraTargetObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
        throw new InvalidOperationException($"Dynamic scene target lock failed: {scene.SceneCode} cameraTargetObjects does not contain targetObjects.");
    if (!scene.TargetObjects.All(x => scene.LabelObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
        throw new InvalidOperationException($"Dynamic scene label lock failed: {scene.SceneCode} labelObjects does not contain targetObjects.");
    if (double.IsNaN(scene.CameraAzimuth) || double.IsNaN(scene.CameraAltitude) || double.IsNaN(scene.Fov))
        throw new InvalidOperationException($"Dynamic scene camera lock failed: {scene.SceneCode} camera values are invalid.");
    if (!string.IsNullOrWhiteSpace(scene.ParentSceneCode) && scene.TargetObjects.Count == 1)
    {
        var suffix = NormalizeWeeklyObjectCode(scene.TargetObjects[0])?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(suffix) && !scene.SceneCode.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Split scene target lock failed: {scene.SceneCode} expected suffix '{suffix}'.");
    }
}

static string ResolveWeeklyRenderSceneCodeFromFinalSceneCode(string sceneCode)
{
    var slashIndex = sceneCode.IndexOf('/');
    return slashIndex > 0 ? sceneCode[..slashIndex] : sceneCode;
}

static void ValidateWeeklySscPropagationScene(string sceneCode, WeeklyDynamicSceneContract scene)
{
    var objectTargeted = scene.TargetObjects.Count > 0;
    if (!objectTargeted) return;
    var failed = scene.TargetObjects.Count == 0
        || !scene.TargetObjects.All(x => scene.ResolvedObjects.Contains(x, StringComparer.OrdinalIgnoreCase))
        || string.IsNullOrWhiteSpace(scene.PrimaryObject)
        || scene.CameraTargetObjects.Count == 0
        || scene.LabelObjects.Count == 0
        || double.IsNaN(scene.CameraAzimuth)
        || double.IsNaN(scene.CameraAltitude)
        || double.IsNaN(scene.Fov)
        || scene.Fov <= 0;
    if (failed)
        throw new InvalidOperationException($"SSC propagation failed for {sceneCode}: dynamic framing metadata was not propagated.");
}

static async Task<IReadOnlyList<WeeklyDynamicSceneContract>> ReadWeeklyDynamicScenesFromPlanFileAsync(string weeklyDynamicFramingPlanPath, CancellationToken cancellationToken)
{
    await using var stream = File.OpenRead(weeklyDynamicFramingPlanPath);
    var document = await JsonSerializer.DeserializeAsync<WeeklyDynamicFramingPlanDocument>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
    return document?.Scenes ?? Array.Empty<WeeklyDynamicSceneContract>();
}

static async Task EnrichWeeklyRenderInputManifestWithSscPropagationAsync(string renderInputManifestPath, string weeklyDynamicFramingPlanPath, string sscSceneManifestPath, string sscPropagationValidationReportPath, object sscScenes, WeeklySscPropagationValidationReport propagationReport, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(renderInputManifestPath) || !File.Exists(renderInputManifestPath)) return;
    var manifestText = await File.ReadAllTextAsync(renderInputManifestPath, cancellationToken);
    var manifestNode = JsonNode.Parse(manifestText) as JsonObject ?? new JsonObject();
    manifestNode["sscPropagation"] = new JsonObject
    {
        ["sourceOfTruth"] = weeklyDynamicFramingPlanPath,
        ["sscSceneManifestPath"] = sscSceneManifestPath,
        ["sscPropagationValidationReportPath"] = sscPropagationValidationReportPath,
        ["sscPropagationReady"] = propagationReport.sscPropagationReady,
        ["emptyObjectSceneCount"] = propagationReport.emptyObjectSceneCount,
        ["emptyRequiredLabelSceneCount"] = propagationReport.emptyRequiredLabelSceneCount,
        ["cameraTargetMismatchCount"] = propagationReport.cameraTargetMismatchCount,
        ["scenes"] = JsonSerializer.SerializeToNode(sscScenes)
    };
    await File.WriteAllTextAsync(renderInputManifestPath, manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}

static WeeklySscCameraLockValidationReport BuildWeeklySscCameraLockValidationReport(IReadOnlyList<WeeklySscSceneFinalizer.FinalSscScene> finalScenes, IReadOnlyDictionary<string, WeeklyDynamicSceneContract> dynamicFramingScenesByCode)
{
    var warnings = new List<string>();
    var errors = new List<string>();
    var scenes = new List<WeeklySscCameraLockSceneValidation>();
    var objectFirstCameraLockSceneCount = 0;
    var altAzOnlySceneCount = 0;
    var trackingEnabledSceneCount = 0;
    var objectCenteredSceneCount = 0;
    var fallbackUsedSceneCount = 0;

    foreach (var finalScene in finalScenes)
    {
        var renderSceneCode = ResolveWeeklyRenderSceneCodeFromFinalSceneCode(finalScene.SceneCode);
        if (!dynamicFramingScenesByCode.TryGetValue(renderSceneCode, out var dynamicScene))
        {
            errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: dynamic framing scene metadata was not found.");
            scenes.Add(new WeeklySscCameraLockSceneValidation(finalScene.SceneCode, string.Empty, string.Empty, false, false, false, false, false, false));
            continue;
        }

        var targetObject = NormalizeWeeklyObjectCode(dynamicScene.PrimaryObject) ?? dynamicScene.PrimaryObject;
        var displayName = ToWeeklyObjectDisplayName(dynamicScene.PrimaryObject);
        var expectedSelect = $"core.selectObjectByName(\"{displayName.Replace("\"", "\\\"")}\", true)";
        var scriptContent = File.Exists(finalScene.ScriptPath) ? File.ReadAllText(finalScene.ScriptPath) : string.Empty;
        var hasScript = !string.IsNullOrWhiteSpace(scriptContent);
        var hasSelection = scriptContent.Contains(expectedSelect, StringComparison.Ordinal);
        var hasTracking = scriptContent.Contains("StelMovementMgr.setFlagTracking(true)", StringComparison.Ordinal);
        var hasZoom = scriptContent.Contains("StelMovementMgr.zoomTo(", StringComparison.Ordinal);
        var hasObjectCentering = scriptContent.Contains("core.moveToSelectedObject(", StringComparison.Ordinal)
            || scriptContent.Contains("StelMovementMgr.moveToObject(", StringComparison.Ordinal);
        var hasScreenshotPath = scriptContent.Contains("core.screenshot(", StringComparison.Ordinal)
            && scriptContent.Contains(Path.GetFileNameWithoutExtension(finalScene.ScreenshotPath), StringComparison.Ordinal);
        var hasRequiredObjectComment = scriptContent.Contains($"ObjectFirstCameraLockTarget: {displayName}", StringComparison.Ordinal)
            || scriptContent.Contains($"TargetObjectDisplayName: {displayName}", StringComparison.Ordinal);
        var fallbackUsed = scriptContent.Contains("FallbackUsed: true", StringComparison.OrdinalIgnoreCase)
            || scriptContent.Contains("fallbackUsed=true", StringComparison.OrdinalIgnoreCase);
        var usesAltAzExecutable = scriptContent.Contains("core.moveToAltAzi(", StringComparison.Ordinal);
        var cameraLockValid = hasScript && hasSelection && hasTracking && hasZoom && hasObjectCentering && hasRequiredObjectComment && hasScreenshotPath && !fallbackUsed && !usesAltAzExecutable;

        if (fallbackUsed) fallbackUsedSceneCount++;
        if (hasTracking) trackingEnabledSceneCount++;
        if (hasObjectCentering) objectCenteredSceneCount++;
        if (cameraLockValid) objectFirstCameraLockSceneCount++;
        if (usesAltAzExecutable && !(hasSelection && hasObjectCentering)) altAzOnlySceneCount++;

        if (!hasScript) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: script is missing or empty at {finalScene.ScriptPath}.");
        if (!hasSelection) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing {expectedSelect}.");
        if (!hasTracking) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing StelMovementMgr.setFlagTracking(true).");
        if (!hasZoom) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing StelMovementMgr.zoomTo(fov).");
        if (!hasObjectCentering) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing selected-object centering command.");
        if (!hasRequiredObjectComment) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing required object name comment for {displayName}.");
        if (!hasScreenshotPath) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: missing screenshot path/name.");
        if (fallbackUsed) warnings.Add($"SSC camera lock fallback used for {finalScene.SceneCode}.");
        if (usesAltAzExecutable) errors.Add($"SSC camera lock failed for {finalScene.SceneCode}: script still uses executable moveToAltAzi instead of object-first lock.");

        scenes.Add(new WeeklySscCameraLockSceneValidation(
            finalScene.SceneCode,
            targetObject,
            displayName,
            hasSelection,
            hasTracking,
            hasObjectCentering,
            hasZoom,
            hasScreenshotPath,
            cameraLockValid));
    }

    return new WeeklySscCameraLockValidationReport(
        sscCameraLockReady: errors.Count == 0 && finalScenes.Count > 0,
        objectFirstCameraLockSceneCount: objectFirstCameraLockSceneCount,
        altAzOnlySceneCount: altAzOnlySceneCount,
        trackingEnabledSceneCount: trackingEnabledSceneCount,
        objectCenteredSceneCount: objectCenteredSceneCount,
        fallbackUsedSceneCount: fallbackUsedSceneCount,
        scenes: scenes,
        warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        errors: errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
}

static WeeklySscPropagationValidationReport BuildWeeklySscPropagationValidationReport(IReadOnlyList<WeeklySscSceneFinalizer.FinalSscScene> finalScenes, IReadOnlyDictionary<string, WeeklyDynamicSceneContract> dynamicFramingScenesByCode, string weeklyDynamicFramingPlanPath)
{
    var warnings = new List<string>();
    var errors = new List<string>();
    var scenes = new List<WeeklySscPropagationSceneReport>();
    foreach (var finalScene in finalScenes)
    {
        var renderSceneCode = ResolveWeeklyRenderSceneCodeFromFinalSceneCode(finalScene.SceneCode);
        if (!dynamicFramingScenesByCode.TryGetValue(renderSceneCode, out var dynamicScene))
        {
            errors.Add($"SSC propagation failed for {finalScene.SceneCode}: dynamic framing metadata was not propagated.");
            scenes.Add(new WeeklySscPropagationSceneReport(finalScene.SceneCode, [], string.Empty, [], [], double.NaN, double.NaN, 0, false));
            continue;
        }

        var objects = dynamicScene.TargetObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var requiredLabels = dynamicScene.LabelObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var cameraTargets = dynamicScene.CameraTargetObjects.Select(NormalizeWeeklyObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var primaryObject = NormalizeWeeklyObjectCode(dynamicScene.PrimaryObject) ?? dynamicScene.PrimaryObject;
        var screenshotExists = File.Exists(finalScene.ScreenshotPath) && new FileInfo(finalScene.ScreenshotPath).Length > 10 * 1024;
        var objectMetadataMatches = objects.Length > 0
            && dynamicScene.TargetObjects.All(x => dynamicScene.ResolvedObjects.Contains(x, StringComparer.OrdinalIgnoreCase))
            && cameraTargets.Length > 0
            && requiredLabels.Length > 0
            && !string.IsNullOrWhiteSpace(primaryObject)
            && !double.IsNaN(dynamicScene.CameraAzimuth)
            && !double.IsNaN(dynamicScene.CameraAltitude)
            && dynamicScene.Fov > 0;
        if (!screenshotExists) warnings.Add($"Screenshot missing or too small for {finalScene.SceneCode}.");
        if (!objectMetadataMatches) errors.Add($"SSC propagation failed for {finalScene.SceneCode}: dynamic framing metadata was not propagated.");
        scenes.Add(new WeeklySscPropagationSceneReport(finalScene.SceneCode, objects, primaryObject, requiredLabels, cameraTargets, dynamicScene.CameraAzimuth, dynamicScene.CameraAltitude, dynamicScene.Fov, screenshotExists && objectMetadataMatches));
    }

    var emptyObjectSceneCount = scenes.Count(x => x.objects.Count == 0);
    var emptyRequiredLabelSceneCount = scenes.Count(x => x.requiredLabels.Count == 0);
    var cameraTargetMismatchCount = scenes.Count(x => x.objects.Any() && !x.objects.All(o => x.cameraTargetObjects.Contains(o, StringComparer.OrdinalIgnoreCase)));
    if (emptyObjectSceneCount > 0 || emptyRequiredLabelSceneCount > 0 || cameraTargetMismatchCount > 0)
        errors.Add($"SSC propagation metadata counts failed: emptyObjectSceneCount={emptyObjectSceneCount}, emptyRequiredLabelSceneCount={emptyRequiredLabelSceneCount}, cameraTargetMismatchCount={cameraTargetMismatchCount}.");

    return new WeeklySscPropagationValidationReport(
        sscPropagationReady: errors.Count == 0 && scenes.Count > 0,
        sceneCount: scenes.Count,
        metadataPropagatedSceneCount: scenes.Count(x => x.propagationValid),
        emptyObjectSceneCount: emptyObjectSceneCount,
        emptyRequiredLabelSceneCount: emptyRequiredLabelSceneCount,
        cameraTargetMismatchCount: cameraTargetMismatchCount,
        dynamicFramingPlanPath: weeklyDynamicFramingPlanPath,
        scenes: scenes,
        warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        errors: errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
}

static WeeklyDynamicFramingValidationReport BuildWeeklyDynamicFramingValidationReport(IReadOnlyList<WeeklyDynamicFramingPlan> plans)
{
    var warnings = plans.SelectMany(x => x.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var errors = plans.SelectMany(x => x.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    foreach (var plan in plans)
    {
        try { ValidateWeeklyDynamicFramingPlan(plan); }
        catch (Exception ex) { errors.Add(ex.Message); }
    }
    var scenes = plans.SelectMany(plan => plan.SplitRequired ? plan.Clusters : (IReadOnlyList<WeeklyDynamicSceneContract>)new[] { plan.ToSceneContract() }).ToList();
    var allResolved = plans.All(x => x.RequestedObjects.All(o => x.ResolvedObjects.Contains(o, StringComparer.OrdinalIgnoreCase)));
    var allCameraLocked = scenes.All(x => x.TargetObjects.Count > 0 && x.TargetObjects.All(o => x.CameraTargetObjects.Contains(o, StringComparer.OrdinalIgnoreCase)) && !x.InheritedFromParent);
    var allLabels = scenes.All(x => x.TargetObjects.All(o => x.LabelObjects.Contains(o, StringComparer.OrdinalIgnoreCase)));
    return new WeeklyDynamicFramingValidationReport(
        DynamicFramingReady: errors.Count == 0 && scenes.Count > 0,
        SingleFrameSceneCount: plans.Count(x => !x.SplitRequired),
        SplitSceneCount: plans.Count(x => x.SplitRequired),
        ClusterSceneCount: scenes.Count(x => !string.IsNullOrWhiteSpace(x.ParentSceneCode)),
        AllTargetObjectsResolved: allResolved,
        AllCameraTargetsLocked: allCameraLocked,
        AllTargetLabelsEnabled: allLabels,
        CoverageValidationPassed: errors.Count == 0,
        Warnings: warnings,
        Errors: errors);
}

static string BuildWeeklyDynamicFramingSsc(WeeklyDynamicSceneContract scene, DateTime observationUtc, DateTime observationLocal, double longitude, double latitude, double elevationMeters, string locationName, string screenshotDirectory, string? screenshotFileName = null)
{
    ValidateWeeklyDynamicSceneContract(scene);
    var safeDirectory = screenshotDirectory.Replace("\\", "/").Replace("\"", "\\\"");
    var safeName = (screenshotFileName ?? scene.SceneCode).Replace("\"", "\\\"");
    var labels = scene.LabelObjects.Select(ToWeeklyObjectDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var targetDisplayName = ToWeeklyObjectDisplayName(scene.PrimaryObject);
    var escapedTarget = targetDisplayName.Replace("\"", "\\\"");
    var cameraAltitude = scene.CameraAltitude.ToString("0.###", CultureInfo.InvariantCulture);
    var cameraAzimuth = scene.CameraAzimuth.ToString("0.###", CultureInfo.InvariantCulture);
    var fov = scene.Fov.ToString("0.###", CultureInfo.InvariantCulture);
    return string.Join("\n", new[]
    {
        "// WeeklySkyForecast dynamic framing SSC",
        "// CameraLockMode: object-first-selected-target",
        $"// SceneCode: {scene.SceneCode}",
        $"// ParentSceneCode: {scene.ParentSceneCode ?? string.Empty}",
        $"// SelectedObservationUtc: {DateTime.SpecifyKind(observationUtc, DateTimeKind.Utc):O}",
        $"// SelectedObservationLocal: {observationLocal:O}",
        $"// TargetObjects: {string.Join(',', scene.TargetObjects)}",
        $"// TargetObjectDisplayName: {targetDisplayName}",
        $"// ObjectFirstCameraLockTarget: {targetDisplayName}",
        $"// ResolvedObjects: {string.Join(',', scene.ResolvedObjects)}",
        $"// PrimaryObject: {scene.PrimaryObject}",
        $"// PrimaryObjectDisplayName: {targetDisplayName}",
        $"// CameraTargetObjects: {string.Join(',', scene.CameraTargetObjects)}",
        $"// VisualAnchorObjects: {string.Join(',', scene.VisualAnchorObjects)}",
        $"// RequiredLabels: {string.Join(',', scene.LabelObjects)}",
        $"// RequiredLabelDisplayNames: {string.Join(',', labels)}",
        $"// CameraAltAzMetadataOnly: altitudeDeg={cameraAltitude}; azimuthDeg={cameraAzimuth}",
        $"// FramingMode: {scene.FramingMode}",
        "// FallbackUsed: false",
        "core.clear(\"natural\");",
        "core.setGuiVisible(false);",
        $"core.setDate(\"{DateTime.SpecifyKind(observationUtc, DateTimeKind.Utc):yyyy-MM-ddTHH:mm:ss}\", \"utc\");",
        "core.wait(2);",
        $"core.setObserverLocation({longitude.ToString(CultureInfo.InvariantCulture)}, {latitude.ToString(CultureInfo.InvariantCulture)}, {elevationMeters.ToString(CultureInfo.InvariantCulture)}, 0, \"{locationName.Replace("\"", "\\\"")}\", \"Earth\");",
        "core.wait(2);",
        "LandscapeMgr.setFlagAtmosphere(false);",
        "LandscapeMgr.setFlagLandscape(false);",
        "ConstellationMgr.setFlagLines(true);",
        "ConstellationMgr.setFlagLabels(true);",
        "SolarSystem.setFlagLabels(true);",
        "SolarSystem.setFlagMarkers(true);",
        $"var targetName = \"{escapedTarget}\";",
        $"core.selectObjectByName(\"{escapedTarget}\", true);",
        "core.wait(2);",
        "StelMovementMgr.setFlagTracking(true);",
        "core.wait(1);",
        $"StelMovementMgr.zoomTo({fov}, 1);",
        "core.wait(2);",
        "core.moveToSelectedObject(2);",
        "core.wait(4);",
        $"core.screenshot(\"{safeName}\", false, \"{safeDirectory}\", true, \"png\");",
        "core.wait(1);",
        "core.quitStellarium();"
    });
}

static string NormalizePreferredAssetType(string? assetType)
    => (assetType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();



sealed record WeeklyEndToEndStageResult<T>(bool Success, T? Value, IReadOnlyList<string> Errors);

public sealed record WeeklySkyForecastV2EndToEndRunRequest(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    string Language,
    string WeekStartDate,
    bool GenerateLongform = true,
    bool GenerateShortform = true,
    bool GenerateAudio = true,
    bool MergeAudio = true,
    bool OverwriteExisting = true,
    bool PublishToYouTube = false,
    bool PublishToFacebook = false,
    bool PublishToInstagram = false);

public sealed record WeeklySkyForecastV2EndToEndReports(
    string? SceneGenerationReportPath = null,
    string? AudioGenerationReportPath = null,
    string? VisualIntentValidationReportPath = null,
    string? VisualIntentRenderSafeValidationReportPath = null,
    string? RenderQualityReportPath = null,
    string? FinalRenderReportPath = null);

public sealed record WeeklySkyForecastV2EndToEndRunResponse(
    Guid PipelineRunId,
    bool EndToEndReady,
    string RegionId,
    string LocationName,
    string WeekStartDate,
    bool ScenesGenerated,
    bool AudioGenerated,
    bool VisualIntentReady,
    bool RenderSafeShotPlanReady,
    bool AudioDrivenTimelineReady,
    bool RenderVideoReady,
    bool AudioVideoMergeReady,
    string LongformFinalVideoPath,
    string ShortformFinalVideoPath,
    bool ShortformVisualProfessionalReady,
    bool LongformVisualProfessionalReady,
    WeeklySkyForecastV2EndToEndReports Reports,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? FailedStage,
    int ShortformSmartCropLayoutCount,
    int ShortformContainLayoutCount,
    int ShortformCroppedTextRiskCount,
    bool ShortformFullFrameCoveragePassed,
    int MotionGraphicsStandaloneClipCount,
    int EducationalOverlayStandaloneClipCount);

sealed record WeeklyFocusObjectPlan(
    string WeekStartDate,
    string RegionId,
    string Language,
    string HeroEvent,
    IReadOnlyList<string> FocusObjects,
    IReadOnlyList<WeeklyFocusGrouping> FocusGroupings,
    IReadOnlyList<WeeklyRequiredVisualScene> RequiredVisualScenes);

sealed record WeeklyFocusGrouping(string GroupingCode, IReadOnlyList<string> Objects, string Source, string Purpose);

sealed record WeeklyRequiredVisualScene(
    string SceneCode,
    string SceneName,
    IReadOnlyList<string> Objects,
    string Purpose,
    int ScreenshotMin,
    int ScreenshotMax,
    bool IncludeHorizon,
    bool IncludeLabels,
    bool IncludeConstellationLines,
    string Notes);

sealed record WeeklyStellariumSceneRequirementsDocument(
    string WeekStartDate,
    string RegionId,
    string Language,
    IReadOnlyList<WeeklyRequiredVisualScene> RequiredScenes,
    IReadOnlyList<string> SscScriptRequirements,
    IReadOnlyList<string> RequiredLabels);

sealed record WeeklyVisualNarrationCoverageReport(
    bool VisualNarrationAligned,
    bool AllObjectsVisuallySupported,
    bool GroupingSplitRequired,
    IReadOnlyList<string> ObjectsMentionedInNarration,
    IReadOnlyList<string> ObjectsVisuallySupported,
    IReadOnlyList<string> ObjectsMentionedButNotVisible,
    IReadOnlyList<string> RequiredScenesGenerated,
    IReadOnlyList<string> MissingScenes,
    int MoonSceneCount,
    int VenusSceneCount,
    int SaturnSceneCount,
    int GroupingSceneCount,
    int SscScriptsGenerated,
    int ScreenshotsGenerated,
    bool MultiObjectSceneResolutionPassed,
    int MultiObjectScenesRequested,
    int MultiObjectScenesResolved,
    int MultiObjectScenesFailed,
    IReadOnlyList<WeeklyMultiObjectSceneResolutionReport> MultiObjectScenes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool GroupingSingleFrameAvailable,
    bool GroupingNarrationShouldUseSplitLanguage);

sealed record WeeklyMultiObjectSceneResolutionReport(
    string SceneCode,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> ResolvedObjects,
    IReadOnlyList<string> MissingObjects,
    DateTime SelectedObservationUtc,
    DateTime SelectedObservationLocal,
    bool MultiObjectResolutionPassed,
    IReadOnlyList<string> CandidateTimestampsInspected,
    bool GroupingSplitRequired,
    bool AllObjectsVisuallySupported,
    int CandidateTimestampCount,
    IReadOnlyList<string> SelectedBucketObjectNames,
    double AngularSpreadDegrees = 0d,
    bool GroupingSingleFrameAvailable = true,
    IReadOnlyList<WeeklyMultiObjectSplitSceneManifestEntry>? SplitScenes = null);

sealed record WeeklyMultiObjectSplitSceneManifestEntry(
    string SceneCode,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> ResolvedObjects);

sealed record WeeklyMultiObjectSceneResolutionResult(
    string SceneCode,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<WeeklySceneObjectSelection> ResolvedObjects,
    DateTime SelectedObservationUtc,
    DateTime SelectedObservationLocal,
    WeeklyMultiObjectSceneResolutionReport Report);

sealed record WeeklyResolvedTemporalObject(string RequestedCode, string NormalizedName, string DisplayName, SkyfieldTemporalCandidate Candidate);

sealed record WeeklyMultiObjectResolutionCandidate(DateTime AnchorUtc, IReadOnlyList<WeeklyResolvedTemporalObject> Objects, double MaxDeltaMinutes, double PreferredDeltaMinutes, string MatchMode, IReadOnlyList<string> BucketObjectNames);


sealed record ExpandedNightGeometrySelection(bool Ready, DateTime? SelectedObservationUtc, DateTime? SelectedObservationLocal, double? SelectedSunAltitudeDeg, string? SelectedTargetObject, string ValidationStatus);
sealed record ExpandedNightGeometryCandidate(string TargetObject, DateTime ObservationUtc, DateTime ObservationLocal, double ObjectAltitudeDeg, double ObjectAzimuthDeg, double SunAltitudeDeg);

sealed record ExpandedStellariumExecutionSummary(
    string ReportPath,
    bool Ready,
    bool Partial,
    bool TimedOut,
    int MaxExpandedScenesPerRun,
    int MaxFramesPerExpandedScene,
    string Mode,
    int ExecutedExpandedSceneCount,
    int SkippedExpandedSceneCount,
    int GeneratedExpandedSscScriptCount,
    int GeneratedExpandedScreenshotCount,
    IReadOnlyList<string> ExpandedFrameScreenshots,
    IReadOnlyList<string> Warnings,
    bool ExpandedNightGeometryReady,
    DateTime? ExpandedSelectedObservationUtc,
    DateTime? ExpandedSelectedObservationLocal,
    double? ExpandedSelectedSunAltitudeDeg,
    string ExpandedNightValidationStatus,
    IReadOnlyList<string> FailedExpandedAssetReasons);

sealed record ExpandedStellariumSceneExecution(
    string RenderSceneCode,
    string SourceSegmentType,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> GeneratedScripts,
    IReadOnlyList<string> GeneratedScreenshots,
    string ExecutionStatus,
    bool ProductionReady,
    DateTime? SelectedObservationUtc,
    DateTime? SelectedObservationLocal,
    double? SelectedSunAltitudeDeg,
    string NightValidationStatus);

sealed record ExpandedStellariumSkippedScene(
    string RenderSceneCode,
    string SourceSegmentType,
    IReadOnlyList<string> TargetObjects,
    string SceneStatus,
    IReadOnlyList<string> Warnings);


public sealed record GenerateDailyPlanRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    DateTimeOffset ScheduledUtc,
    string? PrimaryCelestialObjectCode);

public sealed record GenerateDailyPlanResponse(
    Guid ContentGenerationPlanId,
    string Status);

sealed record WeeklySceneObjectSelection(SkyObjectPosition Position, string Source);
sealed record WeeklyObjectPositionResolution(double AltitudeDeg, double AzimuthDeg, double Magnitude, string Source, string RequestedName, string NormalizedName, string DateKey, string TimeKey, string CollectionSearched, bool MatchFound, string CandidateNames, string CandidateTimes, string TopLevelKeys, string AvailableDates, string SelectedDateCollections, string SelectedDateObjectNames, DateTime? MatchedTimeUtc = null);


public sealed record FinalRenderSceneDescriptor(
    string RenderSceneCode,
    string FrameId,
    bool FallbackUsed,
    string FallbackReason,
    string GeometrySource,
    string ScriptPath,
    string ImagePath,
    bool ProducedSscScript);

sealed record WeeklySceneRequirement(
    string SceneCode,
    string? ParentSceneCode,
    string EventType,
    IReadOnlyList<string> RequestedObjects,
    DateTime PreferredObservationUtc,
    DateTime PreferredObservationLocal,
    string SegmentClassification,
    string VisualRequirement);

sealed record WeeklyFramingOptions(
    double SingleObjectFovMin = 18d,
    double SingleObjectFovMax = 45d,
    double TightGroupingFovMin = 25d,
    double TightGroupingFovMax = 55d,
    double WideGroupingFovMin = 55d,
    double WideGroupingFovMax = 85d,
    double PlanetParadeFovMin = 80d,
    double PlanetParadeFovMax = 115d,
    double AbsoluteMaxSingleFrameFov = 120d,
    double SplitThresholdDegrees = 85d,
    double HardSplitThresholdDegrees = 120d)
{
    public static WeeklyFramingOptions Default { get; } = new();
}

sealed record WeeklyDynamicFramingPlan(
    string SceneCode,
    string EventType,
    IReadOnlyList<string> RequestedObjects,
    IReadOnlyList<string> ResolvedObjects,
    string FramingMode,
    bool SingleFramePossible,
    bool SplitRequired,
    IReadOnlyList<WeeklyDynamicSceneContract> Clusters,
    DateTime SelectedObservationUtc,
    DateTime SelectedObservationLocal,
    WeeklyDynamicCameraPlan CameraPlan,
    WeeklyDynamicLabelPlan LabelPlan,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public WeeklyDynamicSceneContract ToSceneContract() => new(
        SceneCode,
        null,
        RequestedObjects,
        ResolvedObjects,
        CameraPlan.PrimaryObject,
        CameraPlan.CameraTargetObjects,
        CameraPlan.VisualAnchorObjects,
        LabelPlan.LabelObjects,
        CameraPlan.CameraAzimuth,
        CameraPlan.CameraAltitude,
        CameraPlan.Fov,
        FramingMode,
        false,
        SplitRequired,
        CameraPlan.IncludeHorizon);
}


sealed record WeeklyDynamicFramingPlanDocument(bool DynamicFramingReady, DateTime GeneratedUtc, IReadOnlyList<WeeklyDynamicSceneContract> Scenes);
sealed record WeeklySscPropagationSceneReport(string sceneCode, IReadOnlyList<string> objects, string primaryObject, IReadOnlyList<string> requiredLabels, IReadOnlyList<string> cameraTargetObjects, double cameraAzimuth, double cameraAltitude, double fov, bool propagationValid);
sealed record WeeklySscPropagationValidationReport(bool sscPropagationReady, int sceneCount, int metadataPropagatedSceneCount, int emptyObjectSceneCount, int emptyRequiredLabelSceneCount, int cameraTargetMismatchCount, string dynamicFramingPlanPath, IReadOnlyList<WeeklySscPropagationSceneReport> scenes, IReadOnlyList<string> warnings, IReadOnlyList<string> errors);
sealed record WeeklySscCameraLockSceneValidation(string sceneCode, string targetObject, string displayName, bool selectObjectCommandPresent, bool trackingEnabled, bool moveToSelectedObjectPresent, bool zoomToPresent, bool screenshotCommandPresent, bool cameraLockValid);
sealed record WeeklySscCameraLockValidationReport(bool sscCameraLockReady, int objectFirstCameraLockSceneCount, int altAzOnlySceneCount, int trackingEnabledSceneCount, int objectCenteredSceneCount, int fallbackUsedSceneCount, IReadOnlyList<WeeklySscCameraLockSceneValidation> scenes, IReadOnlyList<string> warnings, IReadOnlyList<string> errors);

sealed record WeeklyDynamicCameraPlan(string PrimaryObject, IReadOnlyList<string> CameraTargetObjects, IReadOnlyList<string> VisualAnchorObjects, double CameraAzimuth, double CameraAltitude, double Fov, bool IncludeHorizon);
sealed record WeeklyDynamicLabelPlan(IReadOnlyList<string> LabelObjects, bool SuppressPeripheralLabels, IReadOnlyList<string> LabelPriority);
sealed record WeeklyDynamicSceneContract(
    string SceneCode,
    string? ParentSceneCode,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> ResolvedObjects,
    string PrimaryObject,
    IReadOnlyList<string> CameraTargetObjects,
    IReadOnlyList<string> VisualAnchorObjects,
    IReadOnlyList<string> LabelObjects,
    double CameraAzimuth,
    double CameraAltitude,
    double Fov,
    string FramingMode,
    bool InheritedFromParent,
    bool SplitRequired,
    bool IncludeHorizon);
sealed record WeeklyDynamicFramingValidationReport(bool DynamicFramingReady, int SingleFrameSceneCount, int SplitSceneCount, int ClusterSceneCount, bool AllTargetObjectsResolved, bool AllCameraTargetsLocked, bool AllTargetLabelsEnabled, bool CoverageValidationPassed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

sealed class WeeklyDynamicMultiObjectFramingEngine
{
    private readonly WeeklyFramingOptions _options;
    public WeeklyDynamicMultiObjectFramingEngine(WeeklyFramingOptions? options = null) => _options = options ?? WeeklyFramingOptions.Default;

    public Task<WeeklyDynamicFramingPlan> BuildFramingPlanAsync(WeeklySceneRequirement sceneRequirement, WeeklySkyForecastV2IntelligenceResponse skyfieldContext, WeeklyFocusObjectPlan focusObjectPlan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selections = ResolveSelectionsFromContext(sceneRequirement, skyfieldContext);
        return BuildFramingPlanAsync(sceneRequirement, skyfieldContext, focusObjectPlan, selections, cancellationToken);
    }

    public Task<WeeklyDynamicFramingPlan> BuildFramingPlanAsync(WeeklySceneRequirement sceneRequirement, WeeklySkyForecastV2IntelligenceResponse skyfieldContext, WeeklyFocusObjectPlan focusObjectPlan, IReadOnlyList<WeeklySceneObjectSelection> resolvedSelections, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = sceneRequirement.RequestedObjects.Select(NormalizeObjectCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resolved = resolvedSelections
            .Select(x => NormalizeObjectCode(x.Position.Name) ?? x.Position.Name.ToUpperInvariant())
            .Where(x => requested.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var errors = new List<string>();
        var warnings = new List<string>();
        if (requested.Count == 0) errors.Add("No requested objects supplied for dynamic framing.");
        var missing = requested.Where(x => !resolved.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) errors.Add($"Requested objects were not resolved from Skyfield geometry: {string.Join(',', missing)}");
        var targets = requested
            .Select(code => resolvedSelections.FirstOrDefault(x => string.Equals(NormalizeObjectCode(x.Position.Name), code, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        if (targets.Count == 0 && resolvedSelections.Count > 0)
        {
            targets = resolvedSelections.ToList();
            warnings.Add("Dynamic framing used resolved selections because requested/resolved object code matching was incomplete.");
        }
        var geometry = AnalyzeGeometry(targets.Select(x => x.Position).ToList());
        var eventType = NormalizeEventType(sceneRequirement.EventType, requested);
        var framingMode = ClassifyFramingMode(eventType, targets.Count, geometry.MaxAngularSeparation, geometry.RequiredFov, geometry.LabelCollisionRisk);
        var splitRequired = framingMode == "ClusterSplit" || geometry.RequiredFov > _options.AbsoluteMaxSingleFrameFov || geometry.MaxAngularSeparation > _options.SplitThresholdDegrees;
        if (eventType == "PlanetParade" && geometry.RequiredFov <= _options.PlanetParadeFovMax) splitRequired = false;
        if (eventType == "MeteorShower") splitRequired = false;
        var singleFramePossible = !splitRequired && geometry.RequiredFov <= _options.AbsoluteMaxSingleFrameFov && !geometry.LabelCollisionRisk;
        var fov = ComputeFov(framingMode, geometry.RequiredFov, eventType);
        var primary = SelectPrimaryObject(targets, requested, focusObjectPlan.FocusObjects);
        var camera = new WeeklyDynamicCameraPlan(primary, resolved, resolved, geometry.CenterAzimuth, geometry.CenterAltitude, fov, geometry.IncludeHorizon || eventType == "PlanetParade");
        var label = new WeeklyDynamicLabelPlan(resolved, false, resolved);
        IReadOnlyList<WeeklyDynamicSceneContract> clusters = splitRequired ? BuildClusters(sceneRequirement.SceneCode, targets, sceneRequirement.PreferredObservationUtc, sceneRequirement.PreferredObservationLocal, focusObjectPlan.FocusObjects) : Array.Empty<WeeklyDynamicSceneContract>();
        return Task.FromResult(new WeeklyDynamicFramingPlan(sceneRequirement.SceneCode, eventType, requested, resolved, splitRequired ? "ClusterSplit" : framingMode, singleFramePossible, splitRequired, clusters, sceneRequirement.PreferredObservationUtc, sceneRequirement.PreferredObservationLocal, camera, label, warnings, errors));
    }

    private IReadOnlyList<WeeklySceneObjectSelection> ResolveSelectionsFromContext(WeeklySceneRequirement req, WeeklySkyForecastV2IntelligenceResponse context)
    {
        var requested = req.RequestedObjects.Select(NormalizeObjectCode).Where(x => x is not null).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (context.EventExtractionResult?.ExtractedEvents ?? [])
            .SelectMany(e => e.Objects)
            .Where(o => requested.Contains(NormalizeObjectCode(o.ObjectCode) ?? o.ObjectCode))
            .Where(o => (o.AltitudeDegrees ?? -90d) > 5d && o.AzimuthDegrees.HasValue)
            .GroupBy(o => NormalizeObjectCode(o.ObjectCode) ?? o.ObjectCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(o => o.VisibilityScore).First())
            .Select(o => new WeeklySceneObjectSelection(new SkyObjectPosition(o.ObjectName, o.AltitudeDegrees ?? 0d, o.AzimuthDegrees ?? 0d, o.Magnitude ?? 99d, "Planet", o.VisibilityScore), "source=weekly-dynamic-framing-context"))
            .ToList();
    }

    private IReadOnlyList<WeeklyDynamicSceneContract> BuildClusters(string parentSceneCode, IReadOnlyList<WeeklySceneObjectSelection> targets, DateTime selectedUtc, DateTime selectedLocal, IReadOnlyList<string> focusObjects)
    {
        var remaining = targets.ToList();
        var clusters = new List<List<WeeklySceneObjectSelection>>();
        while (remaining.Count > 0)
        {
            var seed = remaining.OrderBy(x => NormalizeAzimuth(x.Position.AzimuthDeg)).First();
            var cluster = remaining.Where(x => AngularDistance(seed.Position, x.Position) <= 55d).ToList();
            if (cluster.Count == 0) cluster.Add(seed);
            clusters.Add(cluster);
            foreach (var item in cluster) remaining.Remove(item);
        }
        return clusters.Select(cluster =>
        {
            var codes = cluster.Select(x => NormalizeObjectCode(x.Position.Name) ?? x.Position.Name.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var geo = AnalyzeGeometry(cluster.Select(x => x.Position).ToList());
            var mode = codes.Count == 1 ? "SingleObject" : geo.MaxAngularSeparation <= 15d ? "TightGrouping" : "WideGrouping";
            var fov = ComputeFov(mode, geo.RequiredFov, "MultiObjectGrouping");
            var primary = SelectPrimaryObject(cluster, codes, focusObjects);
            var sceneCode = ResolveClusterSceneCode(parentSceneCode, codes, geo.CenterAzimuth);
            return new WeeklyDynamicSceneContract(sceneCode, parentSceneCode, codes, codes, primary, codes, codes, codes, geo.CenterAzimuth, geo.CenterAltitude, fov, mode, false, true, geo.IncludeHorizon);
        }).ToList();
    }

    private string ResolveClusterSceneCode(string parent, IReadOnlyList<string> codes, double azimuth)
    {
        if (codes.Count == 1) return $"{parent}_{codes[0].ToLowerInvariant()}";
        var direction = azimuth switch { >= 45 and < 135 => "east", >= 135 and < 225 => "south", >= 225 and < 315 => "west", _ => "north" };
        return $"{parent}_{direction}_cluster";
    }

    private string ClassifyFramingMode(string eventType, int count, double maxSep, double requiredFov, bool labelCollisionRisk)
    {
        if (eventType == "MeteorShower") return "RadiantFocus";
        if (count <= 1) return "SingleObject";
        if (labelCollisionRisk || maxSep > _options.SplitThresholdDegrees || requiredFov > _options.AbsoluteMaxSingleFrameFov) return "ClusterSplit";
        if (maxSep <= 15d) return "TightGrouping";
        if (maxSep <= 55d) return "WideGrouping";
        if (maxSep <= 100d && (eventType == "PlanetParade" || eventType == "MultiObjectGrouping")) return "UltraWideHorizon";
        return "ClusterSplit";
    }

    private double ComputeFov(string mode, double requiredFov, string eventType)
    {
        var padded = requiredFov + 10d;
        return mode switch
        {
            "SingleObject" => Math.Clamp(padded, _options.SingleObjectFovMin, _options.SingleObjectFovMax),
            "TightGrouping" => Math.Clamp(padded, _options.TightGroupingFovMin, _options.TightGroupingFovMax),
            "WideGrouping" => Math.Clamp(padded, _options.WideGroupingFovMin, _options.WideGroupingFovMax),
            "UltraWideHorizon" => Math.Clamp(padded, eventType == "PlanetParade" ? _options.PlanetParadeFovMin : _options.WideGroupingFovMin, eventType == "PlanetParade" ? _options.PlanetParadeFovMax : _options.AbsoluteMaxSingleFrameFov),
            "RadiantFocus" => Math.Clamp(padded, _options.TightGroupingFovMin, _options.WideGroupingFovMax),
            _ => Math.Clamp(padded, _options.SingleObjectFovMin, _options.AbsoluteMaxSingleFrameFov)
        };
    }

    private static string SelectPrimaryObject(IReadOnlyList<WeeklySceneObjectSelection> targets, IReadOnlyList<string> requested, IReadOnlyList<string> focusObjects)
    {
        var ranked = targets.Select(x => new { Code = NormalizeObjectCode(x.Position.Name) ?? x.Position.Name.ToUpperInvariant(), x.Position.Magnitude, x.Position.AltitudeDeg }).ToList();
        return ranked.OrderByDescending(x => focusObjects.Contains(x.Code, StringComparer.OrdinalIgnoreCase)).ThenBy(x => x.Magnitude).ThenByDescending(x => x.AltitudeDeg).FirstOrDefault()?.Code
            ?? requested.FirstOrDefault()
            ?? string.Empty;
    }

    private static string NormalizeEventType(string eventType, IReadOnlyList<string> requested)
    {
        if (eventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase)) return "MeteorShower";
        if (eventType.Contains("Parade", StringComparison.OrdinalIgnoreCase)) return "PlanetParade";
        if (eventType.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return "MoonEvent";
        if (eventType.Contains("Conjunction", StringComparison.OrdinalIgnoreCase)) return "Conjunction";
        if (requested.Count > 2) return "MultiObjectGrouping";
        return requested.Count == 1 ? "SingleObject" : "Conjunction";
    }

    private static WeeklyGeometryAnalysis AnalyzeGeometry(IReadOnlyList<SkyObjectPosition> objects)
    {
        if (objects.Count == 0) return new(270d, 35d, 0d, 0d, 0d, 18d, false, false);
        var az = objects.Select(x => NormalizeAzimuth(x.AzimuthDeg)).ToList();
        var alt = objects.Select(x => x.AltitudeDeg).ToList();
        var maxPair = 0d;
        for (var i = 0; i < objects.Count; i++)
        for (var j = i + 1; j < objects.Count; j++)
            maxPair = Math.Max(maxPair, AngularDistance(objects[i], objects[j]));
        var azSpread = ComputeAzSpread(az);
        var altSpread = alt.Count > 1 ? alt.Max() - alt.Min() : 0d;
        var requiredFov = Math.Max(maxPair, Math.Sqrt(azSpread * azSpread + altSpread * altSpread));
        var labelRisk = objects.Count > 4 && maxPair < 12d || objects.Count > 6;
        var includeHorizon = alt.Min() < 18d;
        return new(ComputeCircularMeanAzimuth(az), Math.Clamp((alt.Min() + alt.Max()) / 2d + (includeHorizon ? 2d : 0d), 5d, 85d), azSpread, altSpread, maxPair, Math.Max(18d, requiredFov), includeHorizon, labelRisk);
    }

    private static double AngularDistance(SkyObjectPosition a, SkyObjectPosition b)
    {
        var alt1 = ToRad(a.AltitudeDeg); var alt2 = ToRad(b.AltitudeDeg);
        var az1 = ToRad(a.AzimuthDeg); var az2 = ToRad(b.AzimuthDeg);
        var cos = Math.Sin(alt1) * Math.Sin(alt2) + Math.Cos(alt1) * Math.Cos(alt2) * Math.Cos(az1 - az2);
        return Math.Acos(Math.Clamp(cos, -1d, 1d)) * 180d / Math.PI;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180d;
    private static double NormalizeAzimuth(double value) { var result = value % 360d; return result < 0 ? result + 360d : result; }
    private static double ComputeCircularMeanAzimuth(IReadOnlyList<double> azimuths)
    {
        var sin = azimuths.Sum(a => Math.Sin(ToRad(a)));
        var cos = azimuths.Sum(a => Math.Cos(ToRad(a)));
        if (Math.Abs(sin) < 0.000001d && Math.Abs(cos) < 0.000001d) return azimuths[0];
        return NormalizeAzimuth(Math.Atan2(sin / azimuths.Count, cos / azimuths.Count) * 180d / Math.PI);
    }
    private static double ComputeAzSpread(IReadOnlyList<double> azimuths)
    {
        if (azimuths.Count <= 1) return 0d;
        var sorted = azimuths.OrderBy(x => x).ToList();
        var maxGap = -1d;
        var idx = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            var b = sorted[(i + 1) % sorted.Count] + (i + 1 == sorted.Count ? 360d : 0d);
            if (b - a > maxGap) { maxGap = b - a; idx = i; }
        }
        var start = sorted[(idx + 1) % sorted.Count];
        var end = sorted[idx] + (idx < sorted.Count - 1 ? 0d : 360d);
        return Math.Max(0d, end - start);
    }
    private static string? NormalizeObjectCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Replace(" ", "_").Replace("-", "_").ToUpperInvariant();
        return normalized switch
        {
            "LUNA" or "THE_MOON" or "MOON" => "MOON",
            _ => normalized.Contains("MOON", StringComparison.OrdinalIgnoreCase) ? "MOON" : normalized
        };
    }

    private sealed record WeeklyGeometryAnalysis(double CenterAzimuth, double CenterAltitude, double AzimuthSpread, double AltitudeSpread, double MaxAngularSeparation, double RequiredFov, bool IncludeHorizon, bool LabelCollisionRisk);
}
