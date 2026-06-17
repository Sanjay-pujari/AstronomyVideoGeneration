using System.Globalization;

namespace Astronomy.MediaFactory.Rendering;

public sealed class SmoothMotionRenderer
{
    public string BuildZoomPanFilter(double durationSeconds, int fps, int outputWidth, int outputHeight, MotionProfile profile)
    {
        var totalFrames = Math.Max(1, (int)Math.Round(Math.Max(0d, durationSeconds) * fps, MidpointRounding.AwayFromZero));
        var denominator = Math.Max(totalFrames, 1);
        var progress = $"on/{denominator.ToString(CultureInfo.InvariantCulture)}.0";
        var eased = profile.Easing switch
        {
            MotionEasingKind.EaseOutCubic => $"1-pow(1-({progress}),3)",
            MotionEasingKind.EaseInOutSine => $"(1-cos(PI*({progress})))/2",
            _ => progress
        };
        var startScale = profile.StartScale.ToString("G17", CultureInfo.InvariantCulture);
        var scaleDelta = (profile.EndScale - profile.StartScale).ToString("G17", CultureInfo.InvariantCulture);
        var zoomExpression = $"{startScale}+({scaleDelta})*({eased})";
        var xPan = BuildPanExpression(profile.PanXStart, profile.PanXEnd, eased, "iw");
        var yPan = BuildPanExpression(profile.PanYStart, profile.PanYEnd, eased, "ih");
        return $"zoompan=z='{zoomExpression}':x='iw/2-(iw/zoom/2)+({xPan})':y='ih/2-(ih/zoom/2)+({yPan})':d={totalFrames}:s={outputWidth}x{outputHeight}";
    }

    private static string BuildPanExpression(double start, double end, string eased, string axis)
    {
        var startText = start.ToString("G17", CultureInfo.InvariantCulture);
        var deltaText = (end - start).ToString("G17", CultureInfo.InvariantCulture);
        return $"({startText}+({deltaText})*({eased}))*{axis}";
    }
}
