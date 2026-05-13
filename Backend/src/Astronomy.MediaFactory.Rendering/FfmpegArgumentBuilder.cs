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
            ? $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1"
            : $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
        var hasMusic = !string.IsNullOrWhiteSpace(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath);
        var audioFilter = hasMusic ? "-filter_complex \"[2:a]volume=0.2[music];[1:a][music]amix=inputs=2:duration=first:dropout_transition=2[aout]\" -map 0:v:0 -map \"[aout]\"" : string.Empty;

        return string.Join(' ',
            "-y",
            "-f concat",
            "-safe 0",
            $"-i \"{concatInputPath}\"",
            $"-i \"{audioPath}\"",
            hasMusic ? $"-stream_loop -1 -i \"{options.BackgroundMusicPath}\"" : string.Empty,
            $"-r {options.FrameRate}",
            $"-vf \"{filter}\"",
            audioFilter,
            "-c:v libx264",
            "-pix_fmt yuv420p",
            "-c:a aac",
            "-b:a 192k",
            hasMusic ? string.Empty : "-map 0:v:0 -map 1:a:0",
            $"\"{outputPath}\"");
    }
}
