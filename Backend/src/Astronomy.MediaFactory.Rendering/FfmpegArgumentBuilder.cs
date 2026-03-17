using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegArgumentBuilder
{
    public string Build(RenderingOptions options, RenderManifest manifest, string concatInputPath, string audioPath, string outputPath)
    {
        var width = manifest.OutputWidth ?? options.VideoWidth;
        var height = manifest.OutputHeight ?? options.VideoHeight;
        var filter = manifest.EnableVerticalCrop
            ? $"scale='if(gt(a,{width}.0/{height}),-2,{width})':'if(gt(a,{width}.0/{height}),{height},-2)',crop={width}:{height}"
            : $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";

        return string.Join(' ',
            "-y",
            "-f concat",
            "-safe 0",
            $"-i \"{concatInputPath}\"",
            $"-i \"{audioPath}\"",
            $"-r {options.FrameRate}",
            $"-vf \"{filter}\"",
            "-c:v libx264",
            "-pix_fmt yuv420p",
            "-c:a aac",
            "-b:a 192k",
            "-shortest",
            $"\"{outputPath}\"");
    }
}
