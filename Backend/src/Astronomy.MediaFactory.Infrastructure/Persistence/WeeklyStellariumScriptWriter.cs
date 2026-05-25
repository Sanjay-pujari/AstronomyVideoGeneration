using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStellariumScriptWriter : IWeeklyStellariumScriptWriter
{
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

        foreach (var shot in cinematicShotPackage.SceneSequences.SelectMany(x => x.Shots))
        {
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

            var expectedScreenshotPath = ToForwardSlashes(screenshotFullPath);
            var isValid = shotIssues.Count == 0;
            if (isValid)
            {
                var lines = new List<string>
                {
                    "// WeeklySkyForecast v2 SSC",
                    $"// Shot: {shot.ShotCode}",
                    $"// Type: {shot.ShotType}",
                    $"// Duration: {shot.DurationSeconds}s",
                    $"// GeneratedUtc: {DateTime.UtcNow:O}",
                    $"// ExpectedScreenshotPath: {expectedScreenshotPath}",
                    string.Empty
                };

                lines.AddRange(shot.PlannedSscCommands);
                lines.Add($"core.screenshot('{expectedScreenshotPath}', false, 'png')");
                await File.WriteAllTextAsync(scriptFullPath, string.Join("\n", lines), Encoding.UTF8, cancellationToken);
            }

            validationIssues.AddRange(shotIssues);
            scripts.Add(new WeeklyStellariumScriptInfo(shot.ShotCode, scriptFullPath, expectedScreenshotPath, shot.PlannedSscCommands?.Count ?? 0, isValid));
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

    private static string ToForwardSlashes(string path) => path.Replace('\\', '/');

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
}
