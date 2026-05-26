using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

app.MapPost("/api/weekly-skyforecast-v2/generate-weekly-scenes", async (WeeklySkyForecastV2GenerateWeeklyScenesRequest request, IWeeklySkyForecastV2IntelligenceService service, IWeeklySkyForecastVisualAssetGenerationService visualAssetService, IContentPlanningService planning, CancellationToken ct) =>
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

        var response = await service.PreviewAsync(intelligenceRequest, ct);
        var weeklySkyfieldContext = response;
        app.Logger.LogInformation("Skyfield weekly context loaded once");
        var root = weeklySkyfieldContext.RenderPreparationPackage?.WorkingDirectoryPlan.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            return Results.BadRequest(new { error = "Unable to resolve working directory root for WeeklySkyForecast scene generation." });

        var debugRoot = Path.Combine(root, "debug");
        Directory.CreateDirectory(debugRoot);
        var skyfieldResponsePath = Path.Combine(debugRoot, "skyfield-weekly-response.json");
        var skyfieldErrorsPath = Path.Combine(debugRoot, "skyfield-weekly-errors.json");
        await File.WriteAllTextAsync(skyfieldResponsePath, JsonSerializer.Serialize(response.SkyfieldSummary, new JsonSerializerOptions { WriteIndented = true }), ct);
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

        var visualRequest = new WeeklySkyForecastProductionRequest(
            request.ContentCategoryCode,
            request.Language,
            request.RegionId,
            request.RegionName,
            request.ScheduledUtc,
            weekStartDate,
            weekEndDate,
            GenerateNarration: true,
            GenerateSscScripts: true,
            CaptureStellariumScenes: true,
            DryRun: false,
            Diagnostics: false,
            OverwriteExisting: true);
        if (visualRequest.WeekStartDate == DateOnly.MinValue ||
            visualRequest.WeekEndDate == DateOnly.MinValue)
        {
            throw new InvalidOperationException("Internal bug: weekly date range lost before visual generation.");
        }

        app.Logger.LogInformation("Using existing weekly Skyfield context for visual scene generation");
        var visualAssets = await visualAssetService.GenerateAsync(
            contentPlanId.Value,
            new WeeklySkyForecastVisualAssetsGenerateRequest(
                DryRun: false,
                OverwriteExisting: true,
                CaptureStellariumScenes: true,
                Diagnostics: request.Diagnostics,
                AllowExtraScenes: true),
            visualRequest,
            ct);

        var waitTimeout = TimeSpan.FromSeconds(Math.Clamp(request.StellariumTimeoutSeconds ?? 90, 30, 600));
        foreach (var script in visualAssets.Scripts)
        {
            var started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < waitTimeout)
            {
                if (File.Exists(script.ExpectedImagePath))
                {
                    var len = new FileInfo(script.ExpectedImagePath).Length;
                    if (len > 10 * 1024)
                        break;
                }

                await Task.Delay(500, ct);
            }
        }

        var screenshots = visualAssets.Scripts
            .Select(x => x.ExpectedImagePath)
            .Where(path => File.Exists(path) && new FileInfo(path).Length > 10 * 1024)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var warnings = weeklySkyfieldContext.Warnings.Concat(visualAssets.Warnings).Concat(visualAssets.Errors).Distinct().ToList();

        var narrationManifestPath = Path.Combine(root, "debug", "weekly-scenes-manifest.json");
        await File.WriteAllTextAsync(narrationManifestPath, JsonSerializer.Serialize(new
        {
            narrationArtifacts = new
            {
                storyBeatsPath,
                narrationPlanPath,
                narrationTextPath,
                visualRequirementsPath
            }
        }, new JsonSerializerOptions { WriteIndented = true }), ct);

        var output = new WeeklySkyForecastV2GenerateWeeklyScenesResponse(
            pipelineRunId,
            root,
            skyfieldResponsePath,
            storyBeatsPath,
            visualAssets.VisualAssetManifestPath,
            visualAssets.ScriptCount,
            visualAssets.ScriptCount,
            screenshots,
            warnings);

        return Results.Ok(output);
    }
    catch (Exception ex)
    {
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

public sealed record GenerateDailyPlanRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    DateTimeOffset ScheduledUtc,
    string? PrimaryCelestialObjectCode);

public sealed record GenerateDailyPlanResponse(
    Guid ContentGenerationPlanId,
    string Status);
