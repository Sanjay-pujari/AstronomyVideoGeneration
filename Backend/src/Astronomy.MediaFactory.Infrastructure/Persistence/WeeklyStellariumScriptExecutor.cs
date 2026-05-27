using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStellariumScriptExecutor(
    IOptions<StellariumOptions> options,
    ILogger<WeeklyStellariumScriptExecutor> logger) : IWeeklyStellariumScriptExecutor, IStellariumScriptExecutionService
{
    private const string ExecutorName = nameof(StellariumImageCaptureExecutor);
    private const int PollDelayMilliseconds = 500;
    private readonly StellariumOptions _options = options.Value;

    public async Task<WeeklyStellariumScriptExecutionResult> ExecuteAsync(string workingDirectoryRoot, string scriptPath, string expectedScreenshotPath, int timeoutSeconds = 45, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var timedOut = false;
        int? exitCode = null;
        var processStarted = false;
        int? processId = null;

        var effectiveTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 180;
        logger.LogInformation("Starting Stellarium execution");

        if (!File.Exists(scriptPath)) errors.Add($"Script file does not exist: {scriptPath}");
        var rootFull = Path.GetFullPath(workingDirectoryRoot);
        var scriptFull = Path.GetFullPath(scriptPath);
        var screenshotFull = Path.GetFullPath(expectedScreenshotPath);
        var screenshotDirectory = Path.GetDirectoryName(screenshotFull) ?? string.Empty;
        var pipelineRunFolderName = Path.GetFileName(rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var pipelineRunIdRaw = Guid.TryParseExact(pipelineRunFolderName, "N", out var parsedPipelineRunId)
            ? parsedPipelineRunId.ToString()
            : pipelineRunFolderName;

        logger.LogInformation("workingDirectoryRoot: {WorkingDirectoryRoot}", rootFull);
        logger.LogInformation("scriptPath: {ScriptPath}", scriptFull);
        logger.LogInformation("expectedScreenshotPath: {ExpectedScreenshotPath}", screenshotFull);
        logger.LogInformation("screenshotDirectory: {ScreenshotDirectory}", screenshotDirectory);
        logger.LogInformation("pipelineRunId raw: {PipelineRunIdRaw}", pipelineRunIdRaw);
        logger.LogInformation("pipelineRunFolderName: {PipelineRunFolderName}", pipelineRunFolderName);

        if (!scriptFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !screenshotFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Script/screenshot path mismatch: both paths must share the same workingDirectoryRoot. root='{rootFull}', script='{scriptFull}', screenshot='{screenshotFull}'.");
        }

        if (!screenshotFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Expected screenshot path must be under working directory root. Root='{rootFull}', screenshot='{screenshotFull}'");
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
        {
            errors.Add($"Stellarium executable was not found at '{_options.ExecutablePath}'.");
        }

        if (!errors.Any())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotFull)!);
            if (File.Exists(screenshotFull)) File.Delete(screenshotFull);

            var psi = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                Arguments = $"--startup-script \"{scriptFull}\"",
                WorkingDirectory = rootFull,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            logger.LogInformation("executorName={ExecutorName}", ExecutorName);
            logger.LogInformation("stellariumExePath={StellariumExePath}", psi.FileName);
            logger.LogInformation("scriptPath={ScriptPath}", scriptFull);
            logger.LogInformation("arguments={Arguments}", psi.Arguments);
            logger.LogInformation("workingDirectory={WorkingDirectory}", psi.WorkingDirectory);
            logger.LogInformation("expectedScreenshotPath={ExpectedScreenshotPath}", screenshotFull);
            logger.LogInformation("timeoutSeconds={TimeoutSeconds}", effectiveTimeoutSeconds);

            using var process = new Process { StartInfo = psi };
            process.Start();
            processStarted = true;
            processId = process.Id;
            logger.LogInformation("Stellarium process started");
            logger.LogInformation("Waiting for screenshot");

            var stabilizationResult = await WaitForScreenshotStabilizationAsync(screenshotFull, effectiveTimeoutSeconds, cancellationToken);
            timedOut = !stabilizationResult.Completed;
            if (stabilizationResult.FileExists)
            {
                logger.LogInformation("Screenshot file size: {ScreenshotFileSize}", stabilizationResult.LastObservedSizeBytes);
            }

            if (timedOut)
            {
                logger.LogWarning("Timeout reached while waiting for screenshot stabilization");
            }
            else
            {
                logger.LogInformation("Screenshot write completion confirmed");
                logger.LogInformation("Screenshot stabilization duration (ms): {ScreenshotStabilizationDurationMs}", stabilizationResult.StabilizationDurationMs);
            }

            await process.WaitForExitAsync(cancellationToken);
            exitCode = process.ExitCode;
            logger.LogInformation("Stellarium exited with code {ExitCode}", exitCode);
        }

        var screenshotExists = File.Exists(screenshotFull);
        var screenshotSize = screenshotExists ? new FileInfo(screenshotFull).Length : 0;
        if (!screenshotExists) errors.Add("Expected screenshot was not created.");
        else if (screenshotSize <= 0) errors.Add($"Screenshot exists but file size is zero bytes: {screenshotSize} bytes.");
        if (timedOut) errors.Add($"Stellarium execution timed out after {effectiveTimeoutSeconds} seconds.");

        stopwatch.Stop();
        var debugDir = Path.Combine(rootFull, "debug");
        Directory.CreateDirectory(debugDir);
        var diagnosticsPath = Path.Combine(debugDir, "weekly-stellarium-execution-smoke-test.json");
        var groupedDiagnosticsPath = Path.Combine(debugDir, "grouped-stellarium-execution-report.json");
        var result = new WeeklyStellariumScriptExecutionResult(scriptPath, expectedScreenshotPath, screenshotExists, screenshotSize, stopwatch.ElapsedMilliseconds, timedOut, exitCode, errors, warnings, diagnosticsPath, errors.Count == 0);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var groupedReport = new
        {
            scriptPath,
            stellariumExecutablePath = _options.ExecutablePath,
            processStarted,
            processId,
            expectedScreenshotPath,
            screenshotExists,
            screenshotFileSize = screenshotSize,
            timeoutSeconds = effectiveTimeoutSeconds,
            constellationLinesEnabled = true,
            constellationLabelsEnabled = true,
            objectLabelsEnabled = true,
            targetObjects = Array.Empty<string>(),
            errors
        };
        await File.WriteAllTextAsync(groupedDiagnosticsPath, JsonSerializer.Serialize(groupedReport, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        logger.LogInformation("Execution completed");
        return result;
    }
    private static async Task<ScreenshotStabilizationResult> WaitForScreenshotStabilizationAsync(string screenshotPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var stabilizationStart = Stopwatch.StartNew();
        long lastSize = -1;
        var consecutiveStableChecks = 0;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(screenshotPath))
            {
                var size = new FileInfo(screenshotPath).Length;
                if (size > 0)
                {
                    if (size == lastSize)
                    {
                        consecutiveStableChecks++;
                        if (consecutiveStableChecks >= 2)
                        {
                            stabilizationStart.Stop();
                            return new ScreenshotStabilizationResult(true, true, size, stabilizationStart.ElapsedMilliseconds);
                        }
                    }
                    else
                    {
                        lastSize = size;
                        consecutiveStableChecks = 0;
                    }
                }
            }

            await Task.Delay(PollDelayMilliseconds, cancellationToken);
        }

        stabilizationStart.Stop();
        var exists = File.Exists(screenshotPath);
        var finalSize = exists ? new FileInfo(screenshotPath).Length : 0;
        return new ScreenshotStabilizationResult(false, exists, finalSize, stabilizationStart.ElapsedMilliseconds);
    }

    private sealed record ScreenshotStabilizationResult(bool Completed, bool FileExists, long LastObservedSizeBytes, long StabilizationDurationMs);

}
