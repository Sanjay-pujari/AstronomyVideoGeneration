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
            var rootFull = Path.GetFullPath(workingDirectoryRoot);
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
                    await Task.Delay(500, cancellationToken);
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
}
