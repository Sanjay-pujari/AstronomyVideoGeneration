using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStellariumScriptWriter(IOptions<StellariumOptions> options) : IWeeklyStellariumScriptWriter
{
    private const double DefaultWarmupSeconds = 8;
    private const double DefaultCameraSettleSeconds = 3;
    private const double DefaultPreScreenshotWaitSeconds = 2;
    private readonly StellariumOptions _options = options.Value;

    public async Task<WeeklyStellariumScriptPackage> WriteAsync(WeeklyCinematicShotPackage cinematicShotPackage, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cinematicShotPackage);
        if (string.IsNullOrWhiteSpace(workingDirectoryRoot)) throw new ArgumentException("Working directory root is required.", nameof(workingDirectoryRoot));

        var rootFullPath = Path.GetFullPath(workingDirectoryRoot);
        var scriptsDir = Path.Combine(rootFullPath, "stellarium", "scripts");
        var scenesDir = Path.Combine(rootFullPath, "stellarium", "scenes");
        var debugDir = Path.Combine(rootFullPath, "debug");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(scenesDir);
        Directory.CreateDirectory(debugDir);

        var validationIssues = new List<string>();
        var warnings = new List<string>();
        var scripts = new List<WeeklyStellariumScriptInfo>();

        var shotOrder = 0;
        foreach (var shot in cinematicShotPackage.SceneSequences.SelectMany(x => x.Shots))
        {
            shotOrder++;
            cancellationToken.ThrowIfCancellationRequested();

            var scriptPath = Path.Combine(scriptsDir, $"{shot.ShotCode}.ssc");
            var screenshotPath = Path.Combine(scenesDir, $"{shot.ShotCode}.png");
            var scriptFullPath = Path.GetFullPath(scriptPath);
            var screenshotFullPath = Path.GetFullPath(screenshotPath);

            var shotIssues = new List<string>();
            if (!IsPathUnderRoot(scriptFullPath, rootFullPath)) shotIssues.Add($"Script path is outside working root for shot '{shot.ShotCode}'.");
            if (!IsPathUnderRoot(screenshotFullPath, rootFullPath)) shotIssues.Add($"Screenshot path is outside working root for shot '{shot.ShotCode}'.");
            if (shot.PlannedSscCommands is null || shot.PlannedSscCommands.Count == 0) shotIssues.Add($"Shot '{shot.ShotCode}' has empty command list.");
            if (shot.PlannedSscCommands.Any(IsForbiddenMultiObjectSelect)) shotIssues.Add($"Shot '{shot.ShotCode}' contains invalid multi-object selectObjectByName command.");
            if (shot.PlannedSscCommands.Any(ContainsForbiddenSetFov)) shotIssues.Add($"Shot '{shot.ShotCode}' contains unsupported core.setFov command.");
            if (shot.PlannedSscCommands.Any(ContainsForbiddenManagerReference)) shotIssues.Add($"Shot '{shot.ShotCode}' contains unsupported manager reference not used by DailySkyGuide scripts.");
            if (shot.PlannedSscCommands.Any(c => c.Contains("core.screenshot(", StringComparison.OrdinalIgnoreCase) && c.Contains("\\", StringComparison.Ordinal)))
                warnings.Add($"Shot '{shot.ShotCode}' screenshot command uses backslashes; forward slash path is required for SSC parity.");

            var sceneFolderSscPath = ToSscPath(scenesDir);
            var isValid = shotIssues.Count == 0;
            if (isValid)
            {
                var warmupSeconds = _options.WeeklyApiLaunchWarmupSeconds <= 0 ? DefaultWarmupSeconds : _options.WeeklyApiLaunchWarmupSeconds;
                var cameraSettleSeconds = _options.WeeklyCameraSettleSeconds <= 0 ? DefaultCameraSettleSeconds : _options.WeeklyCameraSettleSeconds;
                var preScreenshotWaitSeconds = _options.WeeklyPreScreenshotWaitSeconds <= 0 ? DefaultPreScreenshotWaitSeconds : _options.WeeklyPreScreenshotWaitSeconds;
                var lines = new List<string>
                {
                    "// WeeklySkyForecast v2 SSC",
                    "// ExecutionMode: ApiLaunched",
                    $"// WarmupSeconds: {warmupSeconds:0.###}",
                    $"// CameraSettleSeconds: {cameraSettleSeconds:0.###}",
                    $"// PreScreenshotWaitSeconds: {preScreenshotWaitSeconds:0.###}",
                    $"// Shot: {shot.ShotCode}",
                    $"// Type: {shot.ShotType}",
                    $"// Duration: {shot.DurationSeconds}s",
                    $"// GeneratedUtc: {DateTime.UtcNow:O}",
                    $"// ExpectedScreenshotPath: {ToSscPath(screenshotFullPath)}",
                    string.Empty
                };

                lines.AddRange(ApplyApiStartupStabilization(shot.PlannedSscCommands, warmupSeconds, cameraSettleSeconds));
                lines.Add($"core.wait({preScreenshotWaitSeconds:0.###});");
                lines.Add($"core.screenshot(\"{EscapeForSscDoubleQuotedString(shot.ShotCode)}\", false, \"{EscapeForSscDoubleQuotedString(sceneFolderSscPath)}\", true, \"png\");");
                lines.Add("core.wait(2.0);");
                lines.Add("core.quitStellarium();");
                await File.WriteAllTextAsync(scriptFullPath, string.Join("\n", lines), Encoding.UTF8, cancellationToken);
            }

            validationIssues.AddRange(shotIssues);
            if (scriptFullPath.Contains(@"D:\AstronomyWorkspace\Astronomy\media-output\stellarium\", StringComparison.OrdinalIgnoreCase)
                || screenshotFullPath.Contains(@"D:\AstronomyWorkspace\Astronomy\media-output\stellarium\", StringComparison.OrdinalIgnoreCase))
            {
                validationIssues.Add("Generic Stellarium path detected.");
            }
            var isDiagnostic = shot.ShotCode.StartsWith("_", StringComparison.Ordinal);
            scripts.Add(new WeeklyStellariumScriptInfo(shot.ShotCode, scriptFullPath, ToSscPath(screenshotFullPath), shot.PlannedSscCommands?.Count ?? 0, isValid, shotOrder, isDiagnostic));
        }

        var diagnosticsPath = Path.Combine(debugDir, "weekly-stellarium-script-package.json");
        var diagnostics = new
        {
            scriptCount = scripts.Count,
            scripts,
            validationIssues,
            warnings
        };

        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        return new WeeklyStellariumScriptPackage(validationIssues.Count == 0, scripts.Count, scripts, validationIssues, warnings, diagnosticsPath);
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        var rootWithSep = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || string.Equals(candidatePath, rootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSscPath(string path) => path.Replace('\\', '/');

    private static string EscapeForSscDoubleQuotedString(string value) => value.Replace("\\", "\\\\");

    private static bool IsForbiddenMultiObjectSelect(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        const string marker = "selectObjectByName('";
        var start = command.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        start += marker.Length;
        var end = command.IndexOf("'", start, StringComparison.Ordinal);
        if (end <= start) return false;
        var selected = command[start..end];
        return selected.Contains(',', StringComparison.Ordinal);
    }

    private static bool ContainsForbiddenSetFov(string command)
        => !string.IsNullOrWhiteSpace(command) && command.Contains("core.setFov(", StringComparison.Ordinal);

    private static bool ContainsForbiddenManagerReference(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.Contains("landscapeMgr.", StringComparison.Ordinal)
            || command.Contains("labelMgr.", StringComparison.Ordinal);
    }

    private static List<string> ApplyApiStartupStabilization(IReadOnlyList<string> plannedCommands, double warmupSeconds, double cameraSettleSeconds)
    {
        var adjusted = new List<string>(plannedCommands.Count + 8);
        var warmupInserted = false;
        var dateLocationSettleInserted = false;
        var cameraSettleInserted = false;
        var zoomSettleInserted = false;

        foreach (var command in plannedCommands)
        {
            adjusted.Add(command);
            if (!warmupInserted && command.Contains("core.clear(\"natural\")", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.Add($"core.wait({warmupSeconds:0.###});");
                warmupInserted = true;
            }

            if (!dateLocationSettleInserted && command.Contains("core.setObserverLocation(", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.Add("core.wait(2.0);");
                dateLocationSettleInserted = true;
            }

            if (!cameraSettleInserted && (command.Contains("moveToAltAzi(", StringComparison.OrdinalIgnoreCase) || command.Contains("moveToSelectedObject(", StringComparison.OrdinalIgnoreCase)))
            {
                adjusted.Add($"core.wait({cameraSettleSeconds:0.###});");
                cameraSettleInserted = true;
            }

            if (!zoomSettleInserted && command.Contains("zoomTo(", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.Add("core.wait(4.0);");
                zoomSettleInserted = true;
            }
        }

        return adjusted;
    }
}
