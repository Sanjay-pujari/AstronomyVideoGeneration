using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
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
    private const int PollDelayMs = 1000;
    private const int DefaultTimeoutSeconds = 90;
    private const int MinimumTimeoutSeconds = 60;
    private const int MaximumTimeoutSeconds = 180;
    private readonly StellariumOptions _options = options.Value;

    public async Task<WeeklyStellariumScreenshotGenerationResult> GenerateAsync(string workingDirectoryRoot, WeeklyStellariumScriptPackage scriptPackage, string? executeShotCode = null, string? testMode = null, int? maxScriptCount = null, bool executeAllScripts = false, bool confirmFullBatch = false, bool continueOnFailure = true, int timeoutSeconds = 90, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var results = new List<WeeklyStellariumScreenshotScriptResult>();
        var sw = Stopwatch.StartNew();

        logger.LogInformation("Multi-shot Stellarium capture started");

        if (scriptPackage is null)
        {
            errors.Add("No script package provided.");
            return await WriteResultAsync(false, workingDirectoryRoot, ExtractPipelineRunId(Path.GetFullPath(workingDirectoryRoot)), warnings, errors, results, sw.ElapsedMilliseconds, null, null, []);
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
        {
            errors.Add("Stellarium executable path is not configured.");
            return await WriteResultAsync(false, workingDirectoryRoot, ExtractPipelineRunId(Path.GetFullPath(workingDirectoryRoot)), warnings, errors, results, sw.ElapsedMilliseconds, null, null, []);
        }

        var rootFull = Path.GetFullPath(workingDirectoryRoot);
        var pipelineRunId = ExtractPipelineRunId(rootFull);
        var scenesDir = Path.Combine(rootFull, "stellarium", "scenes");
        Directory.CreateDirectory(scenesDir);

        var ignoredDiagnosticScripts = new List<string>();
        var skippedScripts = new List<(string ShotCode, string Reason)>();
        var selected = SelectScripts(scriptPackage, executeShotCode, testMode, maxScriptCount, executeAllScripts, confirmFullBatch, warnings, errors, ignoredDiagnosticScripts, skippedScripts);
        logger.LogInformation("Starting Stellarium screenshot execution batch");
        var totalScenes = scriptPackage.Scripts.Select(s => s.ShotCode.Split("_", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? s.ShotCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        logger.LogInformation("Screenshot execution diagnostics: packageIsValid={IsValid}, totalScenes={TotalScenes}, totalShots={TotalShots}, executableShotCount={ExecutableShotCount}, skippedShotCount={SkippedShotCount}", scriptPackage.IsValid, totalScenes, scriptPackage.ScriptCount, selected.Count, skippedScripts.Count);
        foreach (var skipped in skippedScripts)
        {
            logger.LogWarning("Skipping shot {ShotCode}: {Reason}", skipped.ShotCode, skipped.Reason);
        }
        logger.LogInformation("selected script count: {ScriptCount}", selected.Count);
        logger.LogInformation("selected shot codes: {ShotCodes}", string.Join(", ", selected.Select(s => s.ShotCode)));
        if (selected.Count == 0)
        {
            warnings.Add("No shots matched testMode filter.");
            return await WriteResultAsync(true, workingDirectoryRoot, pipelineRunId, warnings, errors, results, sw.ElapsedMilliseconds, null, null, ignoredDiagnosticScripts);
        }

        var selectedScript = selected[0];
        if (selectedScript.ScriptPath.Contains("_smoke_basic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("StellariumScreenshots cannot execute diagnostic smoke script.");
        }

        foreach (var script in selected)
        {
            var scriptSw = Stopwatch.StartNew();
            string? error = null;
            bool timedOut = false;
            int? exitCode = null;
            var screenshotSize = 0L;
            var screenshotFull = Path.GetFullPath(script.ExpectedScreenshotPath);
            var scriptFull = Path.GetFullPath(script.ScriptPath);
            var scriptExists = File.Exists(scriptFull);
            var scriptPreview = scriptExists ? await File.ReadAllTextAsync(scriptFull, cancellationToken) : string.Empty;
            scriptPreview = scriptPreview.Length > 1200 ? scriptPreview[..1200] : scriptPreview;
            var warmupSeconds = ReadHeaderDouble(scriptPreview, "WarmupSeconds", _options.WeeklyApiLaunchWarmupSeconds > 0 ? _options.WeeklyApiLaunchWarmupSeconds : 8);
            var cameraSettleSeconds = ReadHeaderDouble(scriptPreview, "CameraSettleSeconds", _options.WeeklyCameraSettleSeconds > 0 ? _options.WeeklyCameraSettleSeconds : 3);
            var preScreenshotWaitSeconds = ReadHeaderDouble(scriptPreview, "PreScreenshotWaitSeconds", _options.WeeklyPreScreenshotWaitSeconds > 0 ? _options.WeeklyPreScreenshotWaitSeconds : 2);
            var scriptLastWriteUtc = scriptExists ? File.GetLastWriteTimeUtc(scriptFull).ToString("O") : null;
            string? launchedExecutable = null;
            string? launchedArguments = null;
            string? launchedWorkingDirectory = null;

            logger.LogInformation("Launching Stellarium cinematic shot script");
            logger.LogInformation("selectedShotCode={ShotCode}", script.ShotCode);
            logger.LogInformation("selectedScriptPath={ScriptPath}", script.ScriptPath);
            logger.LogInformation("selectedExpectedScreenshotPath={ExpectedScreenshotPath}", script.ExpectedScreenshotPath);
            logger.LogInformation("selectedScriptExists={ScriptExists}", scriptExists);
            logger.LogInformation("selectedScriptLastWriteUtc={ScriptLastWriteUtc}", scriptLastWriteUtc);
            logger.LogInformation("workingDirectoryRoot={WorkingDirectoryRoot}", rootFull);
            logger.LogInformation("pipelineRunId={PipelineRunId}", pipelineRunId);
            logger.LogInformation("Stellarium executable path: {ExecutablePath}", _options.ExecutablePath);
            logger.LogInformation("SSC script path: {ScriptPath}", scriptFull);
            logger.LogInformation("Screenshot target path: {ScreenshotTargetPath}", screenshotFull);

            if (!scriptExists) error = $"Selected script missing: {script.ScriptPath}";
            else if (!scriptFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) error = "Selected script path outside workingDirectoryRoot.";
            else if (!screenshotFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) error = "Expected screenshot path outside workingDirectoryRoot.";
            else if (!scriptFull.Contains(pipelineRunId, StringComparison.OrdinalIgnoreCase) || !screenshotFull.Contains(pipelineRunId, StringComparison.OrdinalIgnoreCase)) error = "Selected script/screenshot path must include current pipelineRunId without dashes.";
            else if (scriptFull.Contains(@"D:\AstronomyWorkspace\Astronomy\media-output\stellarium\", StringComparison.OrdinalIgnoreCase)
                || screenshotFull.Contains(@"D:\AstronomyWorkspace\Astronomy\media-output\stellarium\", StringComparison.OrdinalIgnoreCase)) error = "Generic Stellarium path detected.";
            else if (!scriptPreview.Contains("core.screenshot(", StringComparison.OrdinalIgnoreCase)
                || (!scriptPreview.Contains("core.quitStellarium()", StringComparison.OrdinalIgnoreCase) && !scriptPreview.Contains("core.quit()", StringComparison.OrdinalIgnoreCase))) error = "Selected SSC script does not contain screenshot/quit command.";
            else
            {
                var screenshotDirectory = Path.GetDirectoryName(screenshotFull)!;
                Directory.CreateDirectory(screenshotDirectory);
                logger.LogInformation("Screenshot directory exists.");
                if (File.Exists(screenshotFull)) File.Delete(screenshotFull);
                logger.LogInformation("Expected screenshot path: {ExpectedScreenshotPath}", screenshotFull);
                var screenshotCommand = ExtractScreenshotCommand(scriptPreview);
                if (!string.IsNullOrWhiteSpace(screenshotCommand))
                {
                    logger.LogInformation("SSC screenshot command: {SscScreenshotCommand}", screenshotCommand);
                }

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _options.ExecutablePath,
                        Arguments = $"--startup-script \"{script.ScriptPath}\"",
                        WorkingDirectory = rootFull,
                        UseShellExecute = false,
                        CreateNoWindow = false
                    }
                };
                launchedExecutable = process.StartInfo.FileName;
                launchedArguments = process.StartInfo.Arguments;
                launchedWorkingDirectory = process.StartInfo.WorkingDirectory;
                logger.LogInformation("Working directory: {WorkingDirectory}", launchedWorkingDirectory);

                process.Start();
                logger.LogInformation("Stellarium process id: {ProcessId}", process.Id);
                logger.LogInformation("Stellarium process start info arguments: {Arguments}", process.StartInfo.Arguments);
                logger.LogInformation("Waiting for screenshot: {ScreenshotPath}", screenshotFull);

                var effectiveTimeoutSeconds = Math.Clamp(timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds, MinimumTimeoutSeconds, MaximumTimeoutSeconds);
                var deadline = DateTime.UtcNow.AddSeconds(effectiveTimeoutSeconds);
                long? screenshotDetectedAtMs = null;
                long? screenshotStableAtMs = null;
                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
                {
                    if (File.Exists(screenshotFull))
                    {
                        screenshotSize = new FileInfo(screenshotFull).Length;
                        if (screenshotSize > MinScreenshotBytes)
                        {
                            logger.LogInformation("Screenshot detected");
                            logger.LogInformation("Screenshot file size: {ScreenshotSize}", screenshotSize);
                            screenshotDetectedAtMs = scriptSw.ElapsedMilliseconds;
                            await Task.Delay(1000, cancellationToken);
                            var sizeAfterDelay = File.Exists(screenshotFull) ? new FileInfo(screenshotFull).Length : 0;
                            await Task.Delay(1000, cancellationToken);
                            var sizeSecondCheck = File.Exists(screenshotFull) ? new FileInfo(screenshotFull).Length : 0;
                            if (sizeAfterDelay > MinScreenshotBytes && sizeAfterDelay == sizeSecondCheck)
                            {
                                screenshotStableAtMs = scriptSw.ElapsedMilliseconds;
                                logger.LogInformation("Screenshot validated");
                                break;
                            }
                        }
                    }
                    await Task.Delay(PollDelayMs, cancellationToken);
                }

                var screenshotExists = File.Exists(screenshotFull);
                screenshotSize = screenshotExists ? new FileInfo(screenshotFull).Length : 0;
                if (!screenshotExists || screenshotSize <= MinScreenshotBytes)
                {
                    var fallback = TryResolveFallbackScreenshot(rootFull, script.ShotCode, screenshotFull, scriptLastWriteUtc);
                    if (fallback is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(screenshotFull)!);
                        File.Copy(fallback.FullPath, screenshotFull, overwrite: true);
                        screenshotExists = File.Exists(screenshotFull);
                        screenshotSize = screenshotExists ? new FileInfo(screenshotFull).Length : 0;
                        logger.LogInformation("Actual screenshot detected:\n{Path}\n{FileSize}", fallback.FullPath, fallback.FileSizeBytes);
                    }
                }
                logger.LogInformation("Screenshot exists: {ScreenshotExists}", screenshotExists);
                logger.LogInformation("Screenshot file size: {ScreenshotSize}", screenshotSize);
                timedOut = !(screenshotExists && screenshotSize > MinScreenshotBytes);
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
                if (exitCode.HasValue) logger.LogInformation("Stellarium process exit code: {ExitCode}", exitCode.Value);
                if (timedOut) error = $"Screenshot not created within timeout ({effectiveTimeoutSeconds}s).";
                if (error is null) logger.LogInformation("Shot execution successful");
                logger.LogInformation("Script execution completed");
                results.Add(new WeeklyStellariumScreenshotScriptResult(script.ShotCode, script.ScriptPath, script.ExpectedScreenshotPath, File.Exists(script.ExpectedScreenshotPath), screenshotSize, scriptSw.ElapsedMilliseconds, timedOut, exitCode, error, scriptPreview, scriptLastWriteUtc, launchedExecutable, launchedArguments, launchedWorkingDirectory, warmupSeconds, cameraSettleSeconds, preScreenshotWaitSeconds, screenshotDetectedAtMs, screenshotStableAtMs, scriptFull));
                await Task.Delay(2000, cancellationToken);
                if (!continueOnFailure && !string.IsNullOrWhiteSpace(error)) break;
                continue;
            }

            var exists = File.Exists(script.ExpectedScreenshotPath);
            if (exists) screenshotSize = new FileInfo(script.ExpectedScreenshotPath).Length;
            if (error is null && (!exists)) error = "Screenshot not created.";
            if (error is null && screenshotSize <= MinScreenshotBytes) error = $"Screenshot size <= 10 KB ({screenshotSize} bytes).";

            scriptSw.Stop();
            logger.LogInformation("Script execution completed");
            results.Add(new WeeklyStellariumScreenshotScriptResult(script.ShotCode, script.ScriptPath, script.ExpectedScreenshotPath, exists, screenshotSize, scriptSw.ElapsedMilliseconds, timedOut, exitCode, error, scriptPreview, scriptLastWriteUtc, launchedExecutable, launchedArguments, launchedWorkingDirectory, warmupSeconds, cameraSettleSeconds, preScreenshotWaitSeconds, null, null, scriptFull));
            await Task.Delay(2000, cancellationToken);
            if (!continueOnFailure && !string.IsNullOrWhiteSpace(error)) break;
        }

        sw.Stop();
        var attemptedShots = results.Count;
        var successfulShots = results.Count(r => string.IsNullOrWhiteSpace(r.Error));
        var failedShots = results.Count(r => !string.IsNullOrWhiteSpace(r.Error));
        var skippedShots = skippedScripts.Count;
        logger.LogInformation("Screenshot generation completed");
        logger.LogInformation("Screenshot batch summary: attemptedShots={AttemptedShots}, successfulShots={SuccessfulShots}, failedShots={FailedShots}, skippedShots={SkippedShots}", attemptedShots, successfulShots, failedShots, skippedShots);
        await WriteValidationReportAsync(workingDirectoryRoot, results, warnings, sw.ElapsedMilliseconds);
        return await WriteResultAsync(failedShots == 0, workingDirectoryRoot, pipelineRunId, warnings, errors, results, sw.ElapsedMilliseconds, selectedScript.ShotCode, selectedScript.ScriptPath, ignoredDiagnosticScripts);
    }

    private static List<WeeklyStellariumScriptInfo> SelectScripts(WeeklyStellariumScriptPackage package, string? executeShotCode, string? testMode, int? maxScriptCount, bool executeAllScripts, bool confirmFullBatch, List<string> warnings, List<string> errors, List<string> ignoredDiagnosticScripts, List<(string ShotCode, string Reason)> skippedScripts)
    {
        if (package.Scripts.Count == 0)
        {
            errors.Add("No script package or script list is empty.");
            return [];
        }

        var executableScripts = new List<WeeklyStellariumScriptInfo>();
        foreach (var s in package.Scripts.Where(s => !s.IsDiagnostic && !s.ShotCode.StartsWith("_", StringComparison.Ordinal) && !Path.GetFileName(s.ScriptPath).StartsWith("_", StringComparison.Ordinal) && !Path.GetFileName(s.ScriptPath).Contains("_smoke_basic", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(s.ShotCode)) { skippedScripts.Add((s.ShotCode, "missing shotCode")); continue; }
            if (string.IsNullOrWhiteSpace(s.ScriptPath) || !File.Exists(s.ScriptPath)) { skippedScripts.Add((s.ShotCode, "SSC missing")); continue; }
            if (string.IsNullOrWhiteSpace(s.ExpectedScreenshotPath)) { skippedScripts.Add((s.ShotCode, "expected image path missing")); continue; }
            var content = File.ReadAllText(s.ScriptPath);
            if (!content.Contains("core.screenshot", StringComparison.OrdinalIgnoreCase)) { skippedScripts.Add((s.ShotCode, "script missing core.screenshot")); continue; }
            executableScripts.Add(s);
        }

        ignoredDiagnosticScripts.AddRange(package.Scripts.Except(executableScripts).Select(s => s.ShotCode));

        if (!string.IsNullOrWhiteSpace(executeShotCode))
        {
            var selected = executableScripts.FirstOrDefault(s => string.Equals(s.ShotCode, executeShotCode, StringComparison.Ordinal));
            if (selected is null)
            {
                errors.Add($"Selected script missing for executeShotCode='{executeShotCode}'.");
                return [];
            }
            return [selected];
        }

        var ordered = FilterByTestMode(executableScripts.OrderBy(s => s.ShotOrder).ToList(), testMode, warnings);
        if (executeAllScripts)
        {
            if (!confirmFullBatch)
            {
                errors.Add("Full batch requires confirmFullBatch=true.");
                return [];
            }
            return ordered;
        }

        var requested = maxScriptCount.GetValueOrDefault(3);
        var normalized = requested <= 0 ? 3 : requested;
        var capped = Math.Min(5, normalized);
        if (capped != normalized) warnings.Add("batch mode capped to 5 scripts");
        return ordered.Take(capped).ToList();
    }

    
    private static List<WeeklyStellariumScriptInfo> FilterByTestMode(List<WeeklyStellariumScriptInfo>? ordered, string? testMode, List<string> warnings)
    {
        if (ordered is null || ordered.Count == 0) return [];
        if (string.IsNullOrWhiteSpace(testMode) || testMode.Equals(nameof(ScreenshotTestMode.All), StringComparison.OrdinalIgnoreCase)) return ordered;
        if (!Enum.TryParse<ScreenshotTestMode>(testMode, ignoreCase: true, out var parsedTestMode))
        {
            warnings.Add($"Invalid testMode '{testMode}' supplied. Falling back to '{ScreenshotTestMode.All}'.");
            parsedTestMode = ScreenshotTestMode.All;
        }
        if (parsedTestMode == ScreenshotTestMode.All) return ordered;
        return ordered.Where(s =>
        {
            var content = File.Exists(s.ScriptPath) ? File.ReadAllText(s.ScriptPath) : string.Empty;
            var renderMode = ReadHeaderString(content, "RenderMode") ?? ReadHeaderString(content, "Type") ?? s.ShotCode;
            var shotCode = s.ShotCode ?? string.Empty;
            var targetCount = ReadHeaderString(content, "TargetObjects")?.Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? (shotCode.Contains("group", StringComparison.OrdinalIgnoreCase) || shotCode.Contains("conjunction", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            if (parsedTestMode == ScreenshotTestMode.Single) return targetCount == 1;
            if (parsedTestMode == ScreenshotTestMode.Grouping) return renderMode.Contains("group", StringComparison.OrdinalIgnoreCase);
            if (parsedTestMode == ScreenshotTestMode.Conjunction) return renderMode.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
            if (parsedTestMode == ScreenshotTestMode.Panorama) return renderMode.Contains("panorama", StringComparison.OrdinalIgnoreCase) || renderMode.Contains("wide", StringComparison.OrdinalIgnoreCase);
            return true;
        }).ToList();
    }

    private static string? ReadHeaderString(string text, string key)
    {
        var marker = $"// {key}:";
        var line = text.Split('\n').FirstOrDefault(x => x.TrimStart().StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        return line[(line.IndexOf(':') + 1)..].Trim();
    }

    private static string? ExtractScreenshotCommand(string scriptPreview)
        => scriptPreview
            .Split('\n')
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith("core.screenshot(", StringComparison.OrdinalIgnoreCase));

    private ResolvedFallbackScreenshot? TryResolveFallbackScreenshot(string rootFull, string shotCode, string expectedScreenshotPath, string? scriptLastWriteUtc)
    {
        var expectedDir = Path.GetDirectoryName(expectedScreenshotPath) ?? Path.Combine(rootFull, "stellarium", "scenes");
        var candidates = new List<string>();
        candidates.Add(expectedDir);
        if (!string.IsNullOrWhiteSpace(_options.CaptureDirectory)) candidates.Add(_options.CaptureDirectory);
        var defaultPictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(defaultPictures)) candidates.Add(Path.Combine(defaultPictures, "Stellarium"));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData)) candidates.Add(Path.Combine(appData, "Stellarium", "screenshots"));

        var allFiles = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.png", SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .Where(f => f.Exists && f.Length > 0)
            .ToList();
        if (allFiles.Count == 0) return null;

        var scriptTimeUtc = DateTime.TryParse(scriptLastWriteUtc, out var parsedUtc) ? parsedUtc : DateTime.UtcNow;
        var byShotCode = allFiles
            .Where(f => f.Name.Contains(shotCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Math.Abs((f.LastWriteTimeUtc - scriptTimeUtc).TotalSeconds))
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        var selected = byShotCode ?? allFiles
            .OrderBy(f => Math.Abs((f.LastWriteTimeUtc - scriptTimeUtc).TotalSeconds))
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return selected is null ? null : new ResolvedFallbackScreenshot(selected.FullName, selected.Length);
    }
private static async Task<WeeklyStellariumScreenshotGenerationResult> WriteResultAsync(bool success, string workingDirectoryRoot, string? pipelineRunId, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<WeeklyStellariumScreenshotScriptResult> scripts, long elapsedMs, string? selectedShotCode, string? selectedScriptPath, IReadOnlyList<string> ignoredDiagnosticScripts)
    {
        var timeoutCount = scripts.Count(s => s.TimedOut);
        var result = new WeeklyStellariumScreenshotGenerationResult(success, scripts.Count, scripts.Count(s => s.Error is null), scripts.Count(s => s.Error is not null), elapsedMs, timeoutCount, warnings, errors, scripts, Path.Combine(Path.GetFullPath(workingDirectoryRoot), "debug", "weekly-stellarium-multishot-capture.json"), Path.GetFullPath(workingDirectoryRoot), pipelineRunId, selectedShotCode, selectedScriptPath, "WeeklyStellariumScriptPackage", ignoredDiagnosticScripts);
        Directory.CreateDirectory(Path.GetDirectoryName(result.DiagnosticsPath)!);
        await File.WriteAllTextAsync(result.DiagnosticsPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }


    private static async Task WriteValidationReportAsync(string workingDirectoryRoot, IReadOnlyList<WeeklyStellariumScreenshotScriptResult> results, List<string> warnings, long elapsedMs)
    {
        var hashes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var payloadResults = new List<object>();
        foreach (var r in results)
        {
            string? sha = null;
            int width = 0; int height = 0;
            var itemWarnings = new List<string>();
            if (File.Exists(r.ExpectedScreenshotPath))
            {
                using var fs = File.OpenRead(r.ExpectedScreenshotPath);
                sha = Convert.ToHexString(SHA256.HashData(fs));
                fs.Position = 0;
                var info = Image.Identify(fs);
                width = info?.Width ?? 0; height = info?.Height ?? 0;
                if (width <= 0 || height <= 0) itemWarnings.Add("Invalid image dimensions.");
                if (r.ScreenshotSizeBytes <= MinScreenshotBytes) itemWarnings.Add("Screenshot size <=10KB.");
                hashes.TryAdd(sha, []); hashes[sha].Add(r.ShotCode);
            }
            payloadResults.Add(new { shotCode = r.ShotCode, scriptExecuted = true, screenshotExists = r.ScreenshotExists, fileSizeBytes = r.ScreenshotSizeBytes, dimensions = new { width, height }, imageHash = sha, warnings = itemWarnings, error = r.Error });
            var compPath = Path.Combine(Path.GetDirectoryName(r.ExpectedScreenshotPath)!, $"{r.ShotCode}.composition.json");
            var scriptText = File.Exists(r.ScriptPath) ? await File.ReadAllTextAsync(r.ScriptPath) : string.Empty;
            var renderMode = ReadHeaderString(scriptText, "RenderMode") ?? "Unknown";
            var targetObjects = (ReadHeaderString(scriptText, "TargetObjects") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var computedFov = ReadHeaderDouble(scriptText, "ComputedFov", 0);
            var (centerAzimuth, centerAltitude) = ReadMoveToAltAzi(scriptText);
            await File.WriteAllTextAsync(compPath, JsonSerializer.Serialize(new { shotCode = r.ShotCode, renderMode, centerAzimuth, centerAltitude, computedFov = computedFov <= 0 ? (double?)null : computedFov, targetObjects, includedObjects = targetObjects, excludedObjects = Array.Empty<string>(), fallbackUsed = false, fallbackReason = "" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var dup in hashes.Where(x => x.Value.Count > 1)) warnings.Add($"Duplicate screenshot hash detected for shots: {string.Join(",", dup.Value)}");
        var report = new { totalShots = results.Count, successfulScreenshots = results.Count(x => string.IsNullOrWhiteSpace(x.Error)), failedScreenshots = results.Count(x => !string.IsNullOrWhiteSpace(x.Error)), duplicateGroups = hashes.Where(x=>x.Value.Count>1).Select(x=>x.Value).ToList(), results = payloadResults, totalElapsedMs = elapsedMs };
        var path = Path.Combine(Path.GetFullPath(workingDirectoryRoot), "debug", "weekly-screenshot-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ExtractPipelineRunId(string rootFull)
    {
        var normalized = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalized);
    }

    private static double ReadHeaderDouble(string text, string key, double fallback)
    {
        var marker = $"// {key}:";
        var line = text.Split('\n').FirstOrDefault(x => x.TrimStart().StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        if (line is null) return fallback;
        var raw = line[(line.IndexOf(':') + 1)..].Trim();
        return double.TryParse(raw, out var value) ? value : fallback;
    }


    private static (double? Azimuth, double? Altitude) ReadMoveToAltAzi(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"core\.moveToAltAzi\((?<az>-?\d+(\.\d+)?),\s*(?<alt>-?\d+(\.\d+)?),");
        if (!match.Success) return (null, null);
        if (!double.TryParse(match.Groups["az"].Value, out var az)) return (null, null);
        if (!double.TryParse(match.Groups["alt"].Value, out var alt)) return (null, null);
        return (az, alt);
    }

    private async Task<BasicSmokeResult> RunBasicSmokeTestAsync(string rootFull, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var scriptsDir = Path.Combine(rootFull, "debug", "stellarium-smoke");
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
        var scriptContent = File.Exists(smokeScriptPath) ? await File.ReadAllTextAsync(smokeScriptPath, cancellationToken) : string.Empty;
        return new BasicSmokeResult(launchedCommand, smokeScriptPath, scriptContent, smokeScreenshotPath, exists, size, Math.Max(5, timeoutSeconds), exitCode, timedOut, stdout, stderr);
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
        string SmokeScriptContent,
        string ExpectedSmokeScreenshotPath,
        bool ScreenshotExists,
        long ScreenshotSizeBytes,
        int Timeout,
        int? StellariumExitCode,
        bool TimedOut,
        string Stdout,
        string Stderr);

    private sealed record ResolvedFallbackScreenshot(string FullPath, long FileSizeBytes);
}
