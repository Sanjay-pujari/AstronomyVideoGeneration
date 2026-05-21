using System.Globalization;
using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumScriptGenerator(IOptions<StellariumOptions> options) : IStellariumScriptGenerator
{
    private readonly StellariumOptions _options = options.Value;

    public async Task<StellariumScriptGenerationResult> GenerateAsync(StellariumSceneCapturePlan plan, StellariumSceneCaptureItem scene, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var scriptPath = BuildScriptPath(plan.ContentGenerationPlanId, scene);
        var outputImagePath = BuildImagePath(plan.ContentGenerationPlanId, scene);

        var utcDate = scene.CaptureTimeUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var screenshotPrefix = Path.GetFileNameWithoutExtension(outputImagePath).Replace("\"", "\\\"");
        var screenshotDir = (Path.GetDirectoryName(outputImagePath) ?? ".").Replace("\\", "/").Replace("\"", "\\\"");
        var locationName = (plan.LocationName ?? "Earth").Replace("\"", "\\\"");
        var targetObject = (scene.TargetObjectCode ?? string.Empty).Replace("\"", "\\\"");
        var fov = (scene.FieldOfViewDegrees ?? 60d).ToString(CultureInfo.InvariantCulture);

        var script = new StringBuilder();
        script.AppendLine("core.clear(\"natural\");");
        script.AppendLine($"core.setDate(\"{utcDate}\", \"utc\");");
        script.AppendLine($"core.setObserverLocation({plan.Longitude.ToString(CultureInfo.InvariantCulture)}, {plan.Latitude.ToString(CultureInfo.InvariantCulture)}, 0, 0, \"{locationName}\", \"Earth\");");
        script.AppendLine("core.wait(2.0);");
        script.AppendLine($"ConstellationMgr.setFlagLines({scene.ShowConstellationLines.ToString().ToLowerInvariant()});");
        script.AppendLine($"ConstellationMgr.setFlagLabels({scene.ShowConstellationLabels.ToString().ToLowerInvariant()});");
        script.AppendLine($"StelSkyDrawer.setFlagLuminanceAdaptation({scene.ShowPlanetLabels.ToString().ToLowerInvariant()});");
        script.AppendLine($"GridLinesMgr.setFlagAzimuthalGrid({scene.ShowAzimuthGrid.ToString().ToLowerInvariant()});");
        script.AppendLine($"GridLinesMgr.setFlagEquatorGrid({scene.ShowEquatorialGrid.ToString().ToLowerInvariant()});");
        script.AppendLine($"StelMovementMgr.zoomTo({fov}, 1.5);");
        if (!string.IsNullOrWhiteSpace(targetObject))
        {
            script.AppendLine($"core.selectObjectByName(\"{targetObject}\", true);");
            script.AppendLine("core.wait(1.5);");
            script.AppendLine("core.moveToSelectedObject(2.0);");
        }

        script.AppendLine("core.wait(3.0);");
        script.AppendLine($"core.screenshot(\"{screenshotPrefix}\", false, \"{screenshotDir}\", true, \"png\");");
        script.AppendLine("core.wait(2.0);");
        script.AppendLine("core.quitStellarium();");

        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outputImagePath)!);
        var content = script.ToString();
        await File.WriteAllTextAsync(scriptPath, content, cancellationToken);

        return new StellariumScriptGenerationResult(plan.ContentGenerationPlanId, scene.SceneCode, scene.SceneType, scriptPath, outputImagePath, true, content, warnings, null);
    }

    private string BuildScriptPath(Guid planId, StellariumSceneCaptureItem scene)
    {
        var root = string.IsNullOrWhiteSpace(_options.ScriptsDirectory)
            ? Path.Combine(string.IsNullOrWhiteSpace(_options.OutputRoot) ? "outputs" : _options.OutputRoot, "stellarium-scripts")
            : _options.ScriptsDirectory;
        return Path.Combine(root, "content-plans", planId.ToString(), $"{scene.SortOrder:D2}_{scene.SceneCode}.ssc");
    }

    private string BuildImagePath(Guid planId, StellariumSceneCaptureItem scene)
    {
        var root = string.IsNullOrWhiteSpace(_options.CaptureDirectory)
            ? Path.Combine(string.IsNullOrWhiteSpace(_options.OutputRoot) ? "outputs" : _options.OutputRoot)
            : _options.CaptureDirectory;
        return Path.Combine(root, "content-plans", planId.ToString(), "stellarium-scenes", $"{scene.SortOrder:D2}_{scene.SceneCode}_{scene.OutputImageRole}.png");
    }
}
