using System.Globalization;
using System.Text;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumScriptGenerator : IStellariumScriptGenerator
{
    public async Task<string> GenerateScriptAsync(StellariumSceneCaptureItem scene, StellariumSceneCapturePlan plan, string outputImagePath, string scriptPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var utcDate = scene.CaptureTimeUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
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
        await File.WriteAllTextAsync(scriptPath, script.ToString(), cancellationToken);
        return script.ToString();
    }
}
