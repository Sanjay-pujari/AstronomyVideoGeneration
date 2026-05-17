using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegArgumentBuilder
{
    public string Build(RenderingOptions options, RenderManifest manifest, string concatInputPath, string audioPath, string outputPath)
    {
        var isShort = manifest.OutputHeight.GetValueOrDefault() > manifest.OutputWidth.GetValueOrDefault();
        var preset = isShort
            ? VideoEncodingPreset.YouTubeShortProduction()
            : VideoEncodingPreset.YouTubeLongProduction(options.EnableYouTube1440pUpscale);
        var width = preset.Width;
        var height = preset.Height;
        var filter = manifest.EnableVerticalCrop || isShort
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
            $"-r {(isShort ? 30 : options.FrameRate)}",
            $"-vf \"{filter}\"",
            audioFilter,
            $"-c:v {preset.Codec}",
            $"-preset {preset.Preset}",
            $"-crf {preset.Crf}",
            $"-b:v {preset.VideoBitrate}",
            $"-maxrate {preset.MaxVideoBitrate}",
            $"-bufsize {preset.BufferSize}",
            $"-pix_fmt {preset.PixelFormat}",
            "-c:a aac",
            $"-b:a {preset.AudioBitrate}",
            "-movflags +faststart",
            hasMusic ? string.Empty : "-map 0:v:0 -map 1:a:0",
            $"\"{outputPath}\"");
    }
}
