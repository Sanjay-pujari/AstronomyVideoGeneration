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
        var validationResults = new List<StellariumCaptureValidationSummary>();
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
            if (!request.OverwriteExisting && ValidatePng(plan.CapturePath).Passed)
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
                    MarkFailedIfNoUsableOutput(plan.Job, validationWarning, plan, ref failedCount, warnings, null);
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

                var execution = await ExecuteCaptureWithRetryAsync(plan, cancellationToken);
                validationResults.Add(BuildValidationSummary(plan, execution));
                warnings.AddRange(execution.Warnings);
                if (!execution.Success || !execution.Validation.Passed)
                    throw new InvalidOperationException(BuildExecutionError(execution, plan.CapturePath));

                plan.Job.OutputPath = plan.CapturePath;
                plan.Job.MetadataJson = BuildUpdatedMetadataJson(plan, execution);
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
                if (!validationResults.Any(x => x.JobId == plan.Job.Id))
                {
                    var validation = ValidatePng(plan.CapturePath);
                    validationResults.Add(BuildValidationSummary(plan, new CaptureExecutionResult(false, string.Empty, new CaptureProcessResult(false, null, false, ex.Message, string.Empty, string.Empty), validation, 1, [])));
                }
                var failedSummary = validationResults.LastOrDefault(x => x.JobId == plan.Job.Id);
                MarkFailedIfNoUsableOutput(plan.Job, ex.Message, plan, ref failedCount, warnings, failedSummary);
            }
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        var lastValidation = validationResults.LastOrDefault();
        return new StellariumCaptureExecutionResult(
            selectedJobs.Count,
            completedCount,
            failedCount,
            skippedCount,
            capturedFiles,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            validationResults,
            lastValidation?.ValidationStatus,
            lastValidation?.FileSizeBytes,
            lastValidation?.ImageWidth,
            lastValidation?.ImageHeight,
            lastValidation?.RetryCount ?? 0);
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

    private async Task<string> WriteExecutionScriptAsync(CaptureJobPlan plan, int attemptNumber, CancellationToken cancellationToken)
    {
        var options = stellariumOptions.Value;
        var executionDirectory = Path.Combine(Path.GetDirectoryName(plan.CapturePath) ?? ResolveWorkingDirectoryRoot(), ExecutionScriptsDirectory);
        Directory.CreateDirectory(executionDirectory);
        var executionPath = Path.Combine(executionDirectory, $"execute-capture-scene-{plan.Job.SceneNumber}-{plan.Job.Id:D}-attempt-{attemptNumber}.ssc");
        var originalScript = await File.ReadAllTextAsync(plan.SscPath, cancellationToken);
        var screenshotPrefix = Path.GetFileNameWithoutExtension(plan.CapturePath).Replace("\"", "\\\"");
        var screenshotDir = (Path.GetDirectoryName(plan.CapturePath) ?? ".").Replace("\\", "/").Replace("\"", "\\\"");

        var script = new StringBuilder(originalScript.TrimEnd());
        script.AppendLine();
        script.AppendLine($"// Capture execution wrapper generated by {CaptureSource}; original SSC: {EscapeComment(plan.SscPath)}");
        script.AppendLine($"// Stabilization waits: startup={FormatSeconds(options.StartupWaitSeconds)}, scriptExecution={FormatSeconds(options.ScriptExecutionWaitSeconds)}, preCapture={FormatSeconds(options.PreCaptureWaitSeconds)}, postCapture={FormatSeconds(options.PostCaptureWaitSeconds)} seconds.");
        AppendWait(script, options.StartupWaitSeconds);
        AppendWait(script, options.ScriptExecutionWaitSeconds);
        AppendWait(script, options.PreCaptureWaitSeconds);
        script.AppendLine($"core.screenshot(\"{screenshotPrefix}\", false, \"{screenshotDir}\", true, \"png\");");
        AppendWait(script, options.PostCaptureWaitSeconds);
        script.AppendLine("core.quitStellarium();");
        await File.WriteAllTextAsync(executionPath, script.ToString(), cancellationToken);
        return executionPath;
    }

    private async Task<CaptureExecutionResult> ExecuteCaptureWithRetryAsync(CaptureJobPlan plan, CancellationToken cancellationToken)
    {
        var warnings = BuildOrientationWarnings(plan);
        CaptureExecutionResult? lastResult = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(plan.CapturePath))
                File.Delete(plan.CapturePath);

            var executionScriptPath = await WriteExecutionScriptAsync(plan, attempt, cancellationToken);
            var process = await ExecuteStellariumAsync(executionScriptPath, cancellationToken);
            var validation = ValidatePng(plan.CapturePath);
            var attemptWarnings = warnings.Concat(validation.Warnings).ToList();
            lastResult = new CaptureExecutionResult(process.Success && validation.Passed, executionScriptPath, process, validation, attempt, attemptWarnings);
            if (validation.Passed)
                return lastResult;

            if (attempt == 1)
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        return lastResult ?? new CaptureExecutionResult(false, string.Empty, new CaptureProcessResult(false, null, false, "Capture did not run.", string.Empty, string.Empty), ValidatePng(plan.CapturePath), 0, warnings);
    }

    private async Task<CaptureProcessResult> ExecuteStellariumAsync(string executionScriptPath, CancellationToken cancellationToken)
    {
        var options = stellariumOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ExecutablePath) || !File.Exists(options.ExecutablePath))
            return new CaptureProcessResult(false, null, false, $"Stellarium executable was not found at '{options.ExecutablePath}'.", string.Empty, string.Empty);

        var configuredTimeout = options.CaptureTimeoutSeconds > 0 ? options.CaptureTimeoutSeconds : DefaultSafeTimeoutSeconds;
        var waitBudget = options.StartupWaitSeconds + options.ScriptExecutionWaitSeconds + options.PreCaptureWaitSeconds + options.PostCaptureWaitSeconds;
        var timeoutSeconds = Math.Max(5, Math.Max(configuredTimeout, (int)Math.Ceiling(waitBudget + 15)));
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
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return new CaptureProcessResult(false, null, true, $"Stellarium execution timed out after {timeoutSeconds} seconds.", await ReadCompletedAsync(stdoutTask), await ReadCompletedAsync(stderrTask));
        }

        return new CaptureProcessResult(process.ExitCode == 0 || IsInterruptedQuitNoise(process.ExitCode, await ReadCompletedAsync(stderrTask)), process.ExitCode, false, null, await ReadCompletedAsync(stdoutTask), await ReadCompletedAsync(stderrTask));
    }

    private static async Task<string> ReadCompletedAsync(Task<string> task)
    {
        try { return task.IsCompleted ? await task : string.Empty; }
        catch { return string.Empty; }
    }

    private string BuildUpdatedMetadataJson(CaptureJobPlan plan, CaptureExecutionResult execution)
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
        metadata["captureExecutionScriptPath"] = execution.ExecutionScriptPath;
        metadata["captureAttemptCount"] = execution.AttemptCount;
        metadata["retryCount"] = Math.Max(0, execution.AttemptCount - 1);
        metadata["validationResult"] = execution.Validation.Status;
        metadata["validationStatus"] = execution.Validation.Status;
        metadata["fileSizeBytes"] = execution.Validation.FileSizeBytes.HasValue ? JsonValue.Create(execution.Validation.FileSizeBytes.Value) : null;
        metadata["imageWidth"] = execution.Validation.Width.HasValue ? JsonValue.Create(execution.Validation.Width.Value) : null;
        metadata["imageHeight"] = execution.Validation.Height.HasValue ? JsonValue.Create(execution.Validation.Height.Value) : null;
        metadata["captureFileSizeBytes"] = execution.Validation.FileSizeBytes.HasValue ? JsonValue.Create(execution.Validation.FileSizeBytes.Value) : null;
        metadata["stellariumExitCode"] = execution.Process.ExitCode.HasValue ? JsonValue.Create(execution.Process.ExitCode.Value) : null;
        metadata["stellariumTimedOut"] = execution.Process.TimedOut;
        if (execution.Warnings.Count > 0)
            metadata["captureWarnings"] = JsonSerializer.SerializeToNode(execution.Warnings, JsonOptions);
        return metadata.ToJsonString(JsonOptions);
    }

    private static void MarkFailedIfNoUsableOutput(AstronomyAssetProductionJob job, string reason, CaptureJobPlan plan, ref int failedCount, List<string> warnings, StellariumCaptureValidationSummary? validation)
    {
        if (IsUsablePng(job.OutputPath) || IsUsableSsc(plan.SscPath))
        {
            job.Status = AstronomyAssetProductionJobStatuses.Completed;
            job.OutputPath = plan.SscPath;
            job.MetadataJson = AddWarningMetadata(job.MetadataJson, plan, reason, validation);
            job.CompletedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;
            warnings.Add($"Kept StellariumScreenshot job '{job.Id}' Completed because its existing SSC output remains usable; capture is Preferred and non-blocking.");
            return;
        }

        job.Status = AstronomyAssetProductionJobStatuses.Failed;
        job.FailureReason = reason;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        job.MetadataJson = AddWarningMetadata(job.MetadataJson, plan, reason, validation);
        failedCount++;
    }

    private static string AddWarningMetadata(string? metadataJson, CaptureJobPlan plan, string warning, StellariumCaptureValidationSummary? validation)
    {
        var metadata = ParseMetadata(metadataJson);
        metadata["SscPath"] = plan.SscPath;
        metadata["CapturePath"] = plan.CapturePath;
        metadata["sscPath"] = plan.SscPath;
        metadata["capturePath"] = plan.CapturePath;
        metadata["captureExecuted"] = false;
        metadata["captureWarning"] = warning;
        metadata["captureWarningUtc"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["captureSource"] = CaptureSource;
        metadata["captureAttemptCount"] = validation?.CaptureAttemptCount ?? 0;
        metadata["retryCount"] = validation?.RetryCount ?? 0;
        metadata["validationResult"] = validation?.ValidationStatus ?? "Failed";
        metadata["validationStatus"] = validation?.ValidationStatus ?? "Failed";
        metadata["fileSizeBytes"] = validation?.FileSizeBytes is long fileSize ? JsonValue.Create(fileSize) : null;
        metadata["imageWidth"] = validation?.ImageWidth is int width ? JsonValue.Create(width) : null;
        metadata["imageHeight"] = validation?.ImageHeight is int height ? JsonValue.Create(height) : null;
        if (validation?.Warnings.Count > 0)
            metadata["captureWarnings"] = JsonSerializer.SerializeToNode(validation.Warnings, JsonOptions);
        return metadata.ToJsonString(JsonOptions);
    }

    private static string BuildExecutionError(CaptureExecutionResult execution, string capturePath)
    {
        if (!string.IsNullOrWhiteSpace(execution.Process.Error))
            return execution.Process.Error;
        if (execution.Validation.Warnings.Count > 0)
            return $"Capture validation failed for {capturePath}: {string.Join("; ", execution.Validation.Warnings)}";
        return "Capture execution did not produce a usable PNG.";
    }

    private static bool IsInterruptedQuitNoise(int? exitCode, string? stdErr)
    {
        if (exitCode is not null && exitCode == 0)
            return false;
        return !string.IsNullOrWhiteSpace(stdErr)
            && stdErr.Contains("Error: Interrupted", StringComparison.OrdinalIgnoreCase);
    }

    private static StellariumCaptureValidationSummary BuildValidationSummary(CaptureJobPlan plan, CaptureExecutionResult execution)
        => new(
            plan.Job.Id,
            plan.SscPath,
            plan.CapturePath,
            execution.AttemptCount,
            execution.Validation.Status,
            execution.Validation.FileSizeBytes,
            execution.Validation.Width,
            execution.Validation.Height,
            Math.Max(0, execution.AttemptCount - 1),
            execution.Warnings);

    private static PngValidationResult ValidatePng(string? path)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            warnings.Add("Capture path is empty.");
            return new PngValidationResult(false, "Failed", null, null, null, warnings);
        }
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Capture output is not a PNG path: {path}");
        if (!File.Exists(path))
        {
            warnings.Add($"Capture output was not created: {path}");
            return new PngValidationResult(false, "Failed", null, null, null, warnings);
        }

        var fileSize = new FileInfo(path).Length;
        if (fileSize <= 100 * 1024)
            warnings.Add($"Capture PNG is too small: {fileSize} bytes (minimum 102400 bytes).");

        int? width = null;
        int? height = null;
        try
        {
            using (var stream = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[24];
                if (stream.Read(header) < header.Length)
                {
                    warnings.Add("Capture PNG header is incomplete.");
                }
                else if (!HasPngSignature(header))
                {
                    warnings.Add("Capture file could not be opened as PNG: invalid PNG signature.");
                }
                else if (ReadUInt32BigEndian(header[12..16]) != 0x49484452)
                {
                    warnings.Add("Capture file could not be opened as PNG: missing IHDR chunk.");
                }
            }

            if (warnings.All(w => !w.Contains("could not be opened as PNG", StringComparison.OrdinalIgnoreCase) && !w.Contains("header is incomplete", StringComparison.OrdinalIgnoreCase)))
            {
                var imageInfo = SixLabors.ImageSharp.Image.Identify(path);
                if (imageInfo is null)
                {
                    warnings.Add("Capture file could not be opened as PNG: ImageSharp did not identify image dimensions.");
                }
                else
                {
                    width = imageInfo.Width;
                    height = imageInfo.Height;
                    if (width < 1280)
                        warnings.Add($"Capture PNG width is {width}; minimum width is 1280.");
                    if (height < 720)
                        warnings.Add($"Capture PNG height is {height}; minimum height is 720.");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Capture file could not be opened as PNG: {ex.Message}");
        }

        return new PngValidationResult(warnings.Count == 0, warnings.Count == 0 ? "Passed" : "Failed", fileSize, width, height, warnings);
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> header)
        => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes)
        => ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static void AppendWait(StringBuilder script, double seconds)
    {
        if (seconds > 0)
            script.AppendLine($"core.wait({FormatSeconds(seconds)});");
    }

    private static string FormatSeconds(double seconds) => seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> BuildOrientationWarnings(CaptureJobPlan plan)
    {
        var orientation = ReadString(plan.Metadata, "orientation") ?? ReadString(plan.Metadata, "suggestedOrientation");
        if (orientation is not null && orientation.Contains("portrait-9:16", StringComparison.OrdinalIgnoreCase))
            return ["Portrait orientation requested but current capture uses default landscape resolution."];
        return [];
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static bool IsUsablePng(string? path) => ValidatePng(path).Passed;

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
    private sealed record CaptureExecutionResult(bool Success, string ExecutionScriptPath, CaptureProcessResult Process, PngValidationResult Validation, int AttemptCount, IReadOnlyList<string> Warnings);
    private sealed record CaptureProcessResult(bool Success, int? ExitCode, bool TimedOut, string? Error, string StandardOutput, string StandardError);
    private sealed record PngValidationResult(bool Passed, string Status, long? FileSizeBytes, int? Width, int? Height, IReadOnlyList<string> Warnings);
}
