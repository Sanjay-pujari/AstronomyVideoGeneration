using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStellariumScriptExecutor(
    IOptions<StellariumOptions> options,
    ILogger<WeeklyStellariumScriptExecutor> logger) : IWeeklyStellariumScriptExecutor
{
    private readonly StellariumOptions _options = options.Value;

    public async Task<WeeklyStellariumScriptExecutionResult> ExecuteAsync(string workingDirectoryRoot, string scriptPath, string expectedScreenshotPath, int timeoutSeconds = 45, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var timedOut = false;
        int? exitCode = null;

        logger.LogInformation("Starting Stellarium execution");
        logger.LogInformation("Script path: {ScriptPath}", scriptPath);
        logger.LogInformation("Expected screenshot path: {ExpectedScreenshotPath}", expectedScreenshotPath);

        if (!File.Exists(scriptPath)) errors.Add($"Script file does not exist: {scriptPath}");
        var rootFull = Path.GetFullPath(workingDirectoryRoot);
        var screenshotFull = Path.GetFullPath(expectedScreenshotPath);
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
                Arguments = $"--startup-script \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            logger.LogInformation("Stellarium process started");
            logger.LogInformation("Waiting for screenshot");

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(screenshotFull) && new FileInfo(screenshotFull).Length > 10 * 1024)
                {
                    logger.LogInformation("Screenshot detected");
                    break;
                }

                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                    break;
                }

                await Task.Delay(500, cancellationToken);
            }

            if (!process.HasExited)
            {
                timedOut = !(File.Exists(screenshotFull) && new FileInfo(screenshotFull).Length > 10 * 1024);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                logger.LogInformation("Process killed/closed");
            }

            if (exitCode is null && process.HasExited) exitCode = process.ExitCode;
        }

        var screenshotExists = File.Exists(screenshotFull);
        var screenshotSize = screenshotExists ? new FileInfo(screenshotFull).Length : 0;
        if (!screenshotExists) errors.Add("Expected screenshot was not created.");
        else if (screenshotSize <= 10 * 1024) errors.Add($"Screenshot exists but file size is too small: {screenshotSize} bytes.");
        if (timedOut) errors.Add($"Stellarium execution timed out after {Math.Max(5, timeoutSeconds)} seconds.");

        stopwatch.Stop();
        var debugDir = Path.Combine(rootFull, "debug");
        Directory.CreateDirectory(debugDir);
        var diagnosticsPath = Path.Combine(debugDir, "weekly-stellarium-execution-smoke-test.json");
        var result = new WeeklyStellariumScriptExecutionResult(scriptPath, expectedScreenshotPath, screenshotExists, screenshotSize, stopwatch.ElapsedMilliseconds, timedOut, exitCode, errors, warnings, diagnosticsPath, errors.Count == 0);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        logger.LogInformation("Execution completed");
        return result;
    }
}
