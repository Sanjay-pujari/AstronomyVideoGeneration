using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegArgumentBuilder
{
    public string Build(RenderingOptions options, RenderManifest manifest, string concatInputPath, string audioPath, string outputPath)
    {
        var preset = ResolveFinalEncodingPreset(options, manifest);
        var width = preset.Width;
        var height = preset.Height;
        var filter = manifest.EnableVerticalCrop || IsShortManifest(manifest)
            ? $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=increase,crop={width}:{height},pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1"
            : $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
        var hasMusic = !string.IsNullOrWhiteSpace(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath);
        var audioFilter = hasMusic ? "-filter_complex \"[2:a]volume=0.2[music];[1:a][music]amix=inputs=2:duration=first:dropout_transition=2[aout]\" -map 0:v:0 -map \"[aout]\"" : string.Empty;

        return string.Join(' ',
            "-y",
            "-f concat",
            "-safe 0",
            $"-i \"{concatInputPath}\"",
            $"-i \"{audioPath}\"",
            hasMusic ? $"-stream_loop -1 -i \"{options.BackgroundMusicPath}\"" : string.Empty,
            $"-r {(IsShortManifest(manifest) ? 30 : options.FrameRate)}",
            $"-vf \"{filter}\"",
            audioFilter,
            BuildVideoEncodeArguments(preset),
            "-c:a aac",
            $"-b:a {preset.AudioBitrate}",
            "-movflags +faststart",
            hasMusic ? string.Empty : "-map 0:v:0 -map 1:a:0",
            $"\"{outputPath}\"");
    }

    private static bool IsShortManifest(RenderManifest manifest)
        => manifest.OutputHeight.GetValueOrDefault() > manifest.OutputWidth.GetValueOrDefault();

    private static VideoEncodingPreset ResolveFinalEncodingPreset(RenderingOptions options, RenderManifest manifest)
        => manifest.EncodingProfile switch
        {
            VideoRenderProfileKind.MetaReelFinal => VideoEncodingPreset.MetaReelFinal(options),
            VideoRenderProfileKind.ShortsFinal => VideoEncodingPreset.ShortsFinal(options),
            VideoRenderProfileKind.YouTubeLongFinal => VideoEncodingPreset.YouTubeLongFinal(options),
            _ => IsShortManifest(manifest) ? VideoEncodingPreset.ShortsFinal(options) : VideoEncodingPreset.YouTubeLongFinal(options)
        };

    private static string BuildVideoEncodeArguments(VideoEncodingPreset preset)
    {
        var parts = new List<string>
        {
            $"-c:v {preset.Codec}",
            $"-preset {preset.Preset}",
            $"-crf {preset.Crf}"
        };
        if (!string.IsNullOrWhiteSpace(preset.VideoBitrate)) parts.Add($"-b:v {preset.VideoBitrate}");
        if (!string.IsNullOrWhiteSpace(preset.MaxVideoBitrate)) parts.Add($"-maxrate {preset.MaxVideoBitrate}");
        if (!string.IsNullOrWhiteSpace(preset.BufferSize)) parts.Add($"-bufsize {preset.BufferSize}");
        parts.Add($"-pix_fmt {preset.PixelFormat}");
        return string.Join(' ', parts);
    }
}
