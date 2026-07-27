using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegArgumentBuilder
{
    public string BuildVariant(RenderingOptions options, string concatInputPath, string outputPath, int width, int height, int frameRate, bool includeAudio)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (width <= 0 || height <= 0 || frameRate <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var preset = height > width ? VideoEncodingPreset.ShortsFinal(options) : VideoEncodingPreset.YouTubeLongFinal(options);
        var filter = $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
        var audio = includeAudio ? $"-map 0:v:0 -map 0:a:0 -c:a aac -b:a {preset.AudioBitrate}" : "-map 0:v:0 -an";
        return $"-y -f concat -safe 0 -i {Quote(concatInputPath)} -vf \"{filter}\" -r {frameRate} {BuildVideoEncodeArguments(preset)} {audio} -movflags +faststart {Quote(outputPath)}";
    }

    /// <summary>Builds the existing intermediate-profile operation for exactly one documentary scene.</summary>
    public string BuildScene(
        RenderingOptions options,
        IReadOnlyList<string> orderedVisualPaths,
        string? narrationPath,
        string? subtitlePath,
        string outputPath,
        int width,
        int height,
        int frameRate,
        long durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (orderedVisualPaths is null || orderedVisualPaths.Count == 0) throw new ArgumentException("A visual is required.", nameof(orderedVisualPaths));
        if (width <= 0 || height <= 0 || frameRate <= 0 || durationMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));

        var preset = VideoEncodingPreset.IntermediateSegment(options, width, height);
        var seconds = (durationMilliseconds / 1000m).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var perVisual = (durationMilliseconds / 1000m / orderedVisualPaths.Count).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var inputs = string.Join(' ', orderedVisualPaths.Select(path => $"-loop 1 -t {perVisual} -i {Quote(path)}"));
        var audioIndex = orderedVisualPaths.Count;
        var audio = narrationPath is null ? string.Empty : $"-i {Quote(narrationPath)}";
        var scale = $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={frameRate}";
        string videoFilter;
        if (orderedVisualPaths.Count == 1) videoFilter = $"[0:v]{scale}[v]";
        else
        {
            var scaled = string.Concat(Enumerable.Range(0, orderedVisualPaths.Count).Select(i => $"[{i}:v]{scale}[v{i}];"));
            var concatInputs = string.Concat(Enumerable.Range(0, orderedVisualPaths.Count).Select(i => $"[v{i}]"));
            videoFilter = $"{scaled}{concatInputs}concat=n={orderedVisualPaths.Count}:v=1:a=0[v]";
        }
        if (subtitlePath is not null) videoFilter += $";[v]subtitles={EscapeFilterPath(subtitlePath)}[outv]";
        var videoMap = subtitlePath is null ? "[v]" : "[outv]";
        var encode = $"-c:v {preset.Codec} -preset {preset.Preset} -crf {preset.Crf} -pix_fmt {preset.PixelFormat}";
        var audioArgs = narrationPath is null ? string.Empty : $"-map {audioIndex}:a:0 -c:a aac -b:a {preset.AudioBitrate}";
        return $"-y {inputs} {audio} -filter_complex \"{videoFilter}\" -map \"{videoMap}\" {audioArgs} -t {seconds} -r {frameRate} {encode} -movflags +faststart {Quote(outputPath)}";
    }

    private static string Quote(string path) => $"\"{path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    private static string EscapeFilterPath(string path) => path.Replace("\\", "/", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    public string Build(RenderingOptions options, RenderManifest manifest, string concatInputPath, string audioPath, string outputPath)
    {
        var preset = ResolveFinalEncodingPreset(options, manifest);
        var width = preset.Width;
        var height = preset.Height;
        var filter = manifest.EnableVerticalCrop || IsShortManifest(manifest)
            ? $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=increase,crop={width}:{height},pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1"
            : $"scale={width}:{height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
        var hasMusic = !string.IsNullOrWhiteSpace(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath);
        var audioFilter = hasMusic ? "-filter_complex \"[2:a]volume=0.20[music];[1:a][music]amix=inputs=2:duration=first:normalize=0[aout]\" -map 0:v:0 -map \"[aout]\"" : string.Empty;

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
