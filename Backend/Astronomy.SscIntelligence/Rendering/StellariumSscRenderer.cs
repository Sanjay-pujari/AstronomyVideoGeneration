using Astronomy.SscIntelligence.Contracts;
using System.Globalization;
using System.Text;

namespace Astronomy.SscIntelligence.Rendering;

public sealed class StellariumSscRenderer : IStellariumSscRenderer
{
    public SscRenderResult Render(SscRenderRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("core.clear(\"natural\");");
        sb.AppendLine("core.setGuiVisible(false);");
        sb.AppendLine();
        sb.AppendLine($"core.setDate(\"{request.ObservationUtc:yyyy-MM-ddTHH:mm:ss}Z\", \"utc\");");
        sb.AppendLine("core.wait(2);");
        sb.AppendLine();
        sb.AppendLine($"core.setObserverLocation({Fmt(request.Longitude)}, {Fmt(request.Latitude)}, {Fmt(request.ElevationMeters)}, 0, \"{Escape(request.LocationName)}\", \"Earth\");");
        sb.AppendLine("core.wait(2);");
        sb.AppendLine();
        sb.AppendLine("ConstellationMgr.setFlagLines(true);");
        sb.AppendLine("ConstellationMgr.setFlagLabels(true);");
        sb.AppendLine();
        sb.AppendLine($"core.moveToAltAzi(\"{Fmt(request.CameraAltitudeDeg)}d\", \"{Fmt(request.CameraAzimuthDeg)}d\", 1);");
        sb.AppendLine($"StelMovementMgr.zoomTo({Fmt(request.FovDeg)}, 2);");
        sb.AppendLine();
        sb.AppendLine("core.wait(3);");
        sb.AppendLine();
        sb.AppendLine("core.wait(2);");
        sb.Append("core.quitStellarium();");

        return new SscRenderResult(sb.ToString());
    }

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
