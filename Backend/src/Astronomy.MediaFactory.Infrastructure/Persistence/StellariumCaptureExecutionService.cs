using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumCaptureExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    IOptions<StellariumOptions> stellariumOptions,
    ILogger<StellariumCaptureExecutionService> logger) : IStellariumCaptureExecutionService
{
    private const string StellariumScreenshot = "StellariumScreenshot";
    private const string StellariumCapturesDirectory = "stellarium-captures";
    private const string ExecutionScriptsDirectory = "stellarium-capture-scripts";
    private const string CaptureSource = "Phase8D.3";
    private const int DefaultSafeTimeoutSeconds = 90;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<StellariumCaptureExecutionResult> ExecuteCaptureAsync(StellariumAssetCaptureExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var maxJobs = request.MaxJobs <= 0 ? 1 : request.MaxJobs;
        var warnings = new List<string>();
        var capturedFiles = new List<string>();
        var completedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.AssetType.ToLower() == StellariumScreenshot.ToLower())
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Completed)
            .Where(j => j.OutputPath != null && j.OutputPath.ToLower().EndsWith(".ssc"))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.RegionId))
        {
            var regionId = request.RegionId.Trim();
            query = query.Where(j => j.ContentGenerationPlan != null && j.ContentGenerationPlan.RegionId == regionId);
        }

        if (request.JobIds is { Count: > 0 })
        {
            var jobIds = request.JobIds.Where(id => id != Guid.Empty).ToHashSet();
            query = query.Where(j => jobIds.Contains(j.Id));
        }

        var candidates = await query
            .OrderBy(j => j.Priority)
            .ThenBy(j => j.SceneNumber)
            .ThenBy(j => j.Id)
            .ToListAsync(cancellationToken);

        var selectedJobs = new List<CaptureJobPlan>();
        foreach (var job in candidates)
        {
            var plan = BuildPlan(job, request.RegionId);
            if (!request.OverwriteExisting && IsUsablePng(plan.CapturePath))
            {
                skippedCount++;
                warnings.Add($"Skipped StellariumScreenshot job '{job.Id}' because capture PNG already exists. Set overwriteExisting=true to recapture it.");
                continue;
            }

            selectedJobs.Add(plan);
            if (selectedJobs.Count >= maxJobs)
                break;
        }

        foreach (var plan in selectedJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capturedFiles.Add(plan.CapturePath);

            var validationWarning = ValidatePlan(plan);
            if (validationWarning is not null)
            {
                warnings.Add(validationWarning);
                if (!request.DryRun)
                    MarkFailedIfNoUsableOutput(plan.Job, validationWarning, plan, ref failedCount, warnings);
                continue;
            }

            if (request.DryRun)
                continue;

            plan.Job.StartedUtc = DateTimeOffset.UtcNow;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.CapturePath) ?? ResolveWorkingDirectoryRoot());
                if (request.OverwriteExisting && File.Exists(plan.CapturePath))
                    File.Delete(plan.CapturePath);

                var executionScriptPath = await WriteExecutionScriptAsync(plan, cancellationToken);
                var execution = await ExecuteStellariumAsync(executionScriptPath, plan.CapturePath, cancellationToken);
                if (!execution.Success || !IsUsablePng(plan.CapturePath))
                    throw new InvalidOperationException(BuildExecutionError(execution, plan.CapturePath));

                plan.Job.OutputPath = plan.CapturePath;
                plan.Job.MetadataJson = BuildUpdatedMetadataJson(plan, executionScriptPath, execution);
                plan.Job.Status = AstronomyAssetProductionJobStatuses.Completed;
                plan.Job.CompletedUtc = DateTimeOffset.UtcNow;
                plan.Job.FailureReason = null;
                completedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var warning = $"StellariumScreenshot capture job '{plan.Job.Id}' failed non-blockingly: {ex.Message}";
                warnings.Add(warning);
                logger.LogWarning(ex, "StellariumScreenshot capture job {JobId} failed non-blockingly", plan.Job.Id);
                MarkFailedIfNoUsableOutput(plan.Job, ex.Message, plan, ref failedCount, warnings);
            }
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        return new StellariumCaptureExecutionResult(selectedJobs.Count, completedCount, failedCount, skippedCount, capturedFiles, warnings);
    }

    private CaptureJobPlan BuildPlan(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var eventIntelligenceId = job.AstronomyEventIntelligenceId
            ?? job.ContentGenerationPlan?.AstronomyEventIntelligenceId
            ?? ReadGuid(metadata, "astronomyEventIntelligenceId")
            ?? ReadGuid(metadata, "eventIntelligenceId");
        var regionId = SanitizePathSegment(ReadString(metadata, "regionId"))
            ?? SanitizePathSegment(job.ContentGenerationPlan?.RegionId)
            ?? SanitizePathSegment(requestedRegionId)
            ?? "unknown-region";
        var eventId = eventIntelligenceId ?? job.ContentGenerationPlanId;
        var capturePath = Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventId.ToString("D"), StellariumCapturesDirectory, $"capture-scene-{job.SceneNumber}-{job.Id:D}.png");
        return new CaptureJobPlan(job, job.OutputPath ?? string.Empty, capturePath, metadata);
    }

    private static string? ValidatePlan(CaptureJobPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.SscPath))
            return $"StellariumScreenshot job '{plan.Job.Id}' does not have an SSC OutputPath.";
        if (!plan.SscPath.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase))
            return $"StellariumScreenshot job '{plan.Job.Id}' OutputPath is not an SSC file: {plan.SscPath}";
        if (!File.Exists(plan.SscPath))
            return $"StellariumScreenshot job '{plan.Job.Id}' SSC file does not exist: {plan.SscPath}";
        if (new FileInfo(plan.SscPath).Length <= 0)
            return $"StellariumScreenshot job '{plan.Job.Id}' SSC file is empty: {plan.SscPath}";
        if (!plan.CapturePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return $"StellariumScreenshot job '{plan.Job.Id}' capture path is not a PNG path: {plan.CapturePath}";
        return null;
    }

    private async Task<string> WriteExecutionScriptAsync(CaptureJobPlan plan, CancellationToken cancellationToken)
    {
        var executionDirectory = Path.Combine(Path.GetDirectoryName(plan.CapturePath) ?? ResolveWorkingDirectoryRoot(), ExecutionScriptsDirectory);
        Directory.CreateDirectory(executionDirectory);
        var executionPath = Path.Combine(executionDirectory, $"execute-capture-scene-{plan.Job.SceneNumber}-{plan.Job.Id:D}.ssc");
        var originalScript = await File.ReadAllTextAsync(plan.SscPath, cancellationToken);
        var screenshotPrefix = Path.GetFileNameWithoutExtension(plan.CapturePath).Replace("\"", "\\\"");
        var screenshotDir = (Path.GetDirectoryName(plan.CapturePath) ?? ".").Replace("\\", "/").Replace("\"", "\\\"");

        var script = new StringBuilder(originalScript.TrimEnd());
        script.AppendLine();
        script.AppendLine($"// Capture execution wrapper generated by {CaptureSource}; original SSC: {EscapeComment(plan.SscPath)}");
        script.AppendLine("core.wait(1.0);");
        script.AppendLine($"core.screenshot(\"{screenshotPrefix}\", false, \"{screenshotDir}\", true, \"png\");");
        script.AppendLine("core.wait(2.0);");
        script.AppendLine("core.quitStellarium();");
        await File.WriteAllTextAsync(executionPath, script.ToString(), cancellationToken);
        return executionPath;
    }

    private async Task<CaptureProcessResult> ExecuteStellariumAsync(string executionScriptPath, string capturePath, CancellationToken cancellationToken)
    {
        var options = stellariumOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ExecutablePath) || !File.Exists(options.ExecutablePath))
            return new CaptureProcessResult(false, null, false, $"Stellarium executable was not found at '{options.ExecutablePath}'.", string.Empty, string.Empty);

        var timeoutSeconds = Math.Max(5, options.CaptureTimeoutSeconds > 0 ? options.CaptureTimeoutSeconds : DefaultSafeTimeoutSeconds);
        var workingDirectory = Path.GetFullPath(ResolveWorkingDirectoryRoot());
        Directory.CreateDirectory(workingDirectory);
        var psi = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            Arguments = $"--startup-script \"{Path.GetFullPath(executionScriptPath)}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exited = false;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await WaitForCaptureOrExitAsync(process, capturePath, linked.Token);
            if (!process.HasExited && IsUsablePng(capturePath))
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            else if (!process.HasExited)
            {
                await process.WaitForExitAsync(linked.Token);
            }
            exited = process.HasExited;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return new CaptureProcessResult(false, null, true, $"Stellarium execution timed out after {timeoutSeconds} seconds.", await ReadCompletedAsync(stdoutTask), await ReadCompletedAsync(stderrTask));
        }

        return new CaptureProcessResult(IsUsablePng(capturePath), exited ? process.ExitCode : null, false, null, await ReadCompletedAsync(stdoutTask), await ReadCompletedAsync(stderrTask));
    }

    private static async Task WaitForCaptureOrExitAsync(Process process, string capturePath, CancellationToken cancellationToken)
    {
        while (!process.HasExited && !IsUsablePng(capturePath))
            await Task.Delay(250, cancellationToken);
    }

    private static async Task<string> ReadCompletedAsync(Task<string> task)
    {
        try { return task.IsCompleted ? await task : string.Empty; }
        catch { return string.Empty; }
    }

    private string BuildUpdatedMetadataJson(CaptureJobPlan plan, string executionScriptPath, CaptureProcessResult execution)
    {
        var metadata = ParseMetadata(plan.Job.MetadataJson);
        metadata["SscPath"] = plan.SscPath;
        metadata["CapturePath"] = plan.CapturePath;
        metadata["sscPath"] = plan.SscPath;
        metadata["capturePath"] = plan.CapturePath;
        metadata["sscFile"] = plan.SscPath;
        metadata["captureExecuted"] = true;
        metadata["captureSource"] = CaptureSource;
        metadata["captureCompletedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["captureExecutionScriptPath"] = executionScriptPath;
        metadata["captureFileSizeBytes"] = new FileInfo(plan.CapturePath).Length;
        metadata["stellariumExitCode"] = execution.ExitCode.HasValue ? JsonValue.Create(execution.ExitCode.Value) : null;
        metadata["stellariumTimedOut"] = execution.TimedOut;
        return metadata.ToJsonString(JsonOptions);
    }

    private static void MarkFailedIfNoUsableOutput(AstronomyAssetProductionJob job, string reason, CaptureJobPlan plan, ref int failedCount, List<string> warnings)
    {
        if (IsUsablePng(job.OutputPath) || IsUsableSsc(plan.SscPath))
        {
            job.Status = AstronomyAssetProductionJobStatuses.Completed;
            job.OutputPath = plan.SscPath;
            job.MetadataJson = AddWarningMetadata(job.MetadataJson, plan, reason);
            job.CompletedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;
            warnings.Add($"Kept StellariumScreenshot job '{job.Id}' Completed because its existing SSC output remains usable; capture is Preferred and non-blocking.");
            return;
        }

        job.Status = AstronomyAssetProductionJobStatuses.Failed;
        job.FailureReason = reason;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        job.MetadataJson = AddWarningMetadata(job.MetadataJson, plan, reason);
        failedCount++;
    }

    private static string AddWarningMetadata(string? metadataJson, CaptureJobPlan plan, string warning)
    {
        var metadata = ParseMetadata(metadataJson);
        metadata["SscPath"] = plan.SscPath;
        metadata["CapturePath"] = plan.CapturePath;
        metadata["captureExecuted"] = false;
        metadata["captureWarning"] = warning;
        metadata["captureWarningUtc"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["captureSource"] = CaptureSource;
        return metadata.ToJsonString(JsonOptions);
    }

    private static string BuildExecutionError(CaptureProcessResult execution, string capturePath)
    {
        if (!string.IsNullOrWhiteSpace(execution.Error))
            return execution.Error;
        if (!File.Exists(capturePath))
            return $"Capture output was not created: {capturePath}";
        if (new FileInfo(capturePath).Length <= 0)
            return $"Capture output is empty: {capturePath}";
        if (!capturePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return $"Capture output is not a PNG path: {capturePath}";
        return "Capture execution did not produce a usable PNG.";
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static bool IsUsablePng(string? path)
        => !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path)
            && new FileInfo(path).Length > 0;

    private static bool IsUsableSsc(string? path)
        => !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path)
            && new FileInfo(path).Length > 0;

    private static JsonObject ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return new JsonObject();
        try { return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private static string? ReadString(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return null;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return string.IsNullOrWhiteSpace(text) ? null : text;
        return value.ToJsonString(JsonOptions);
    }

    private static Guid? ReadGuid(JsonObject metadata, string key)
        => Guid.TryParse(ReadString(metadata, key), out var id) ? id : null;

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static string EscapeComment(string value) => value.Replace("\r", " ").Replace("\n", " ");

    private sealed record CaptureJobPlan(AstronomyAssetProductionJob Job, string SscPath, string CapturePath, JsonObject Metadata);
    private sealed record CaptureProcessResult(bool Success, int? ExitCode, bool TimedOut, string? Error, string StandardOutput, string StandardError);
}
