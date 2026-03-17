using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegArgumentBuilder
{
    public string Build(RenderingOptions options, string concatInputPath, string audioPath, string outputPath)
    {
        return string.Join(' ',
            "-y",
            "-f concat",
            "-safe 0",
            $"-i \"{concatInputPath}\"",
            $"-i \"{audioPath}\"",
            $"-r {options.FrameRate}",
            $"-vf \"scale={options.VideoWidth}:{options.VideoHeight}:force_original_aspect_ratio=decrease,pad={options.VideoWidth}:{options.VideoHeight}:(ow-iw)/2:(oh-ih)/2\"",
            "-c:v libx264",
            "-pix_fmt yuv420p",
            "-c:a aac",
            "-b:a 192k",
            "-shortest",
            $"\"{outputPath}\"");
    }
}
