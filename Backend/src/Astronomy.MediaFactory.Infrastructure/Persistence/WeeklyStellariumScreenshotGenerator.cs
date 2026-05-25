using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStellariumScreenshotGenerator(
    IOptions<StellariumOptions> options,
    ILogger<WeeklyStellariumScreenshotGenerator> logger) : IWeeklyStellariumScreenshotGenerator
{
    private const long MinScreenshotBytes = 10 * 1024;
    private const int PollDelayMs = 500;
    private readonly StellariumOptions _options = options.Value;

    public async Task<WeeklyStellariumScreenshotGenerationResult> GenerateAsync(string workingDirectoryRoot, WeeklyStellariumScriptPackage scriptPackage, string? executeShotCode = null, int maxScriptCount = 1, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var results = new List<WeeklyStellariumScreenshotScriptResult>();
        var sw = Stopwatch.StartNew();

        logger.LogInformation("Screenshot generation started");

        if (scriptPackage is null)
        {
            errors.Add("No script package provided.");
            return await WriteResultAsync(false, workingDirectoryRoot, warnings, errors, results, sw.ElapsedMilliseconds);
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
        {
            errors.Add("Stellarium executable path is not configured.");
            return await WriteResultAsync(false, workingDirectoryRoot, warnings, errors, results, sw.ElapsedMilliseconds);
        }

        var rootFull = Path.GetFullPath(workingDirectoryRoot);
        var scenesDir = Path.Combine(rootFull, "stellarium", "scenes");
        Directory.CreateDirectory(scenesDir);

        var smokeResult = await RunBasicSmokeTestAsync(rootFull, timeoutSeconds, cancellationToken);
        if (!smokeResult.ScreenshotExists || smokeResult.TimedOut)
        {
            errors.Add("Basic Stellarium smoke test failed. Cinematic scripts were not executed.");
            await WriteBasicSmokeDiagnosticsAsync(rootFull, smokeResult, cancellationToken);
            return await WriteResultAsync(false, workingDirectoryRoot, warnings, errors, results, sw.ElapsedMilliseconds);
        }
        await WriteBasicSmokeDiagnosticsAsync(rootFull, smokeResult, cancellationToken);

        var selected = SelectScripts(scriptPackage, executeShotCode, maxScriptCount, warnings, errors);
        logger.LogInformation("Script count: {ScriptCount}", selected.Count);
        if (selected.Count == 0)
        {
            errors.Add("No scripts selected for execution.");
            return await WriteResultAsync(false, workingDirectoryRoot, warnings, errors, results, sw.ElapsedMilliseconds);
        }

        foreach (var script in selected)
        {
            var scriptSw = Stopwatch.StartNew();
            string? error = null;
            bool timedOut = false;
            int? exitCode = null;
            var screenshotSize = 0L;
            var screenshotFull = Path.GetFullPath(script.ExpectedScreenshotPath);

            logger.LogInformation("Starting Stellarium script execution");
            logger.LogInformation("Script path: {ScriptPath}", script.ScriptPath);
            logger.LogInformation("Expected screenshot path: {ExpectedScreenshotPath}", script.ExpectedScreenshotPath);

            if (!File.Exists(script.ScriptPath)) error = $"Selected script missing: {script.ScriptPath}";
            else if (!screenshotFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) error = "Expected screenshot path outside workingDirectoryRoot.";
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotFull)!);
                if (File.Exists(screenshotFull)) File.Delete(screenshotFull);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _options.ExecutablePath,
                        Arguments = $"--startup-script \"{script.ScriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    }
                };

                process.Start();
                logger.LogInformation("Stellarium process id: {ProcessId}", process.Id);
                logger.LogInformation("Waiting for screenshot");

                var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
                {
                    if (File.Exists(screenshotFull))
                    {
                        screenshotSize = new FileInfo(screenshotFull).Length;
                        if (screenshotSize > MinScreenshotBytes)
                        {
                            logger.LogInformation("Screenshot detected");
                            logger.LogInformation("Screenshot size: {ScreenshotSize}", screenshotSize);
                            break;
                        }
                    }
                    await Task.Delay(PollDelayMs, cancellationToken);
                }

                timedOut = !(File.Exists(screenshotFull) && new FileInfo(screenshotFull).Length > MinScreenshotBytes);
                if (!process.HasExited)
                {
                    logger.LogInformation("Killing/closing Stellarium");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    if (!timedOut) warnings.Add($"Stellarium had to be killed after screenshot capture for shot {script.ShotCode}.");
                }
                else if (!timedOut)
                {
                    warnings.Add($"Script completed but process did not exit naturally for shot {script.ShotCode}.");
                }

                exitCode = process.HasExited ? process.ExitCode : null;
                if (timedOut) error = $"Screenshot not created within timeout ({Math.Max(5, timeoutSeconds)}s).";
            }

            var exists = File.Exists(script.ExpectedScreenshotPath);
            if (exists) screenshotSize = new FileInfo(script.ExpectedScreenshotPath).Length;
            if (error is null && (!exists)) error = "Screenshot not created.";
            if (error is null && screenshotSize <= MinScreenshotBytes) error = $"Screenshot size <= 10 KB ({screenshotSize} bytes).";

            scriptSw.Stop();
            logger.LogInformation("Script execution completed");
            results.Add(new WeeklyStellariumScreenshotScriptResult(script.ShotCode, script.ScriptPath, script.ExpectedScreenshotPath, exists, screenshotSize, scriptSw.ElapsedMilliseconds, timedOut, exitCode, error));
        }

        sw.Stop();
        logger.LogInformation("Screenshot generation completed");
        return await WriteResultAsync(!results.Any(r => !string.IsNullOrWhiteSpace(r.Error)), workingDirectoryRoot, warnings, errors, results, sw.ElapsedMilliseconds);
    }

    private static List<WeeklyStellariumScriptInfo> SelectScripts(WeeklyStellariumScriptPackage package, string? executeShotCode, int maxScriptCount, List<string> warnings, List<string> errors)
    {
        if (package.Scripts.Count == 0)
        {
            errors.Add("No script package or script list is empty.");
            return [];
        }

        if (!string.IsNullOrWhiteSpace(executeShotCode))
        {
            var selected = package.Scripts.FirstOrDefault(s => string.Equals(s.ShotCode, executeShotCode, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                errors.Add($"Selected script missing for executeShotCode='{executeShotCode}'.");
                return [];
            }
            return [selected];
        }

        var requested = maxScriptCount <= 0 ? 1 : maxScriptCount;
        var capped = Math.Min(3, requested);
        if (capped != requested) warnings.Add("batch mode capped to 3 scripts");
        return package.Scripts.Take(capped).ToList();
    }

    private static async Task<WeeklyStellariumScreenshotGenerationResult> WriteResultAsync(bool success, string workingDirectoryRoot, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<WeeklyStellariumScreenshotScriptResult> scripts, long elapsedMs)
    {
        var timeoutCount = scripts.Count(s => s.TimedOut);
        var result = new WeeklyStellariumScreenshotGenerationResult(success, scripts.Count, scripts.Count(s => s.Error is null), scripts.Count(s => s.Error is not null), elapsedMs, timeoutCount, warnings, errors, scripts, Path.Combine(Path.GetFullPath(workingDirectoryRoot), "debug", "weekly-stellarium-screenshot-generation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(result.DiagnosticsPath)!);
        await File.WriteAllTextAsync(result.DiagnosticsPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    private async Task<BasicSmokeResult> RunBasicSmokeTestAsync(string rootFull, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var scriptsDir = Path.Combine(rootFull, "stellarium", "scripts");
        var scenesDir = Path.Combine(rootFull, "stellarium", "scenes");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(scenesDir);

        var smokeScriptPath = Path.Combine(scriptsDir, "_smoke_basic.ssc");
        var smokeScreenshotPath = Path.Combine(scenesDir, "_smoke_basic.png");
        var smokeScreenshotSscPath = smokeScreenshotPath;
        var smokeScreenshotEscaped = smokeScreenshotSscPath.Replace("\\", "\\\\");
        var forms = new[]
        {
            $"core.screenshot(\"{smokeScreenshotEscaped}\", false, \"png\")",
            $"core.screenshot(\"{smokeScreenshotEscaped}\")",
            $"StelMainView.screenshot(\"{smokeScreenshotEscaped}\")"
        };

        var launchedCommand = string.Empty;
        var stdout = string.Empty;
        var stderr = string.Empty;
        int? exitCode = null;
        var timedOut = false;

        foreach (var screenshotCommand in forms)
        {
            var smokeScript = string.Join("\n", ["core.wait(3)", screenshotCommand, "core.quit()"]);
            await File.WriteAllTextAsync(smokeScriptPath, smokeScript, cancellationToken);
            if (File.Exists(smokeScreenshotPath)) File.Delete(smokeScreenshotPath);

            var psi = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                Arguments = $"--startup-script \"{smokeScriptPath}\"",
                WorkingDirectory = rootFull,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            launchedCommand = $"{psi.FileName} {psi.Arguments}";
            logger.LogInformation("Launching Stellarium smoke test");
            logger.LogInformation("Executable: {Executable}", psi.FileName);
            logger.LogInformation("Arguments: {Arguments}", psi.Arguments);
            logger.LogInformation("WorkingDirectory: {WorkingDirectory}", psi.WorkingDirectory);

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(smokeScreenshotPath) && new FileInfo(smokeScreenshotPath).Length > 0) break;
                if (process.HasExited) break;
                await Task.Delay(PollDelayMs, cancellationToken);
            }

            if (!process.HasExited)
            {
                timedOut = !(File.Exists(smokeScreenshotPath) && new FileInfo(smokeScreenshotPath).Length > 0);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            exitCode = process.HasExited ? process.ExitCode : null;
            stdout = await stdoutTask;
            stderr = await stderrTask;

            if (File.Exists(smokeScreenshotPath) && new FileInfo(smokeScreenshotPath).Length > 0)
            {
                break;
            }
        }

        var exists = File.Exists(smokeScreenshotPath);
        var size = exists ? new FileInfo(smokeScreenshotPath).Length : 0;
        return new BasicSmokeResult(launchedCommand, smokeScriptPath, smokeScreenshotPath, exists, size, Math.Max(5, timeoutSeconds), exitCode, timedOut, stdout, stderr);
    }

    private static async Task WriteBasicSmokeDiagnosticsAsync(string rootFull, BasicSmokeResult result, CancellationToken cancellationToken)
    {
        var debugDir = Path.Combine(rootFull, "debug");
        Directory.CreateDirectory(debugDir);
        var path = Path.Combine(debugDir, "weekly-stellarium-basic-smoke.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private sealed record BasicSmokeResult(
        string LaunchedCommand,
        string SmokeScriptPath,
        string ExpectedSmokeScreenshotPath,
        bool ScreenshotExists,
        long ScreenshotSizeBytes,
        int Timeout,
        int? StellariumExitCode,
        bool TimedOut,
        string Stdout,
        string Stderr);
}
