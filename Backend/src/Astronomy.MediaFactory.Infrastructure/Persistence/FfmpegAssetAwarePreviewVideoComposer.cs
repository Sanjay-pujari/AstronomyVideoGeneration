using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class FfmpegAssetAwarePreviewVideoComposer(
    IProcessRunner processRunner,
    IOptions<RenderingOptions> renderingOptions) : IAssetAwarePreviewVideoComposer
{
    private readonly RenderingOptions _rendering = renderingOptions.Value;

    public async Task<string?> ComposeAsync(AssetAwareVideoCompositionPlan plan, AssetAwarePreviewVideoRequest request, string outputVideoPath, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(outputVideoPath)!;
        Directory.CreateDirectory(dir);
        var segments = plan.Segments.Where(x => x.ImageExists && !string.IsNullOrWhiteSpace(x.ImagePath)).OrderBy(x => x.SortOrder).ToList();
        if (segments.Count == 0) return null;

        var segmentFiles = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var segPath = Path.Combine(dir, $"preview-segment-{i:000}.mp4");
            var duration = Math.Max(1d, s.SuggestedDurationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            var fadeOutStart = Math.Max(0, s.SuggestedDurationSeconds - 0.7d).ToString("0.###", CultureInfo.InvariantCulture);
            var filter = "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,zoompan=z='min(zoom+0.0008,1.12)':d=25*" + duration + ":s=1920x1080";
            if (request.IncludeTransitions)
                filter += $",fade=t=in:st=0:d=0.5,fade=t=out:st={fadeOutStart}:d=0.5";
            var args = $"-y -loop 1 -i \"{s.ImagePath}\" -vf \"{filter}\" -t {duration} -r 25 -pix_fmt yuv420p -an \"{segPath}\"";
            var result = await processRunner.ExecuteAsync(_rendering.FfmpegPath, args, cancellationToken, TimeSpan.FromSeconds(_rendering.FfmpegSegmentTimeoutSeconds));
            if (result.ExitCode != 0 || !File.Exists(segPath)) return null;
            segmentFiles.Add(segPath);
        }

        var concatFile = Path.Combine(dir, "preview-concat.txt");
        await File.WriteAllLinesAsync(concatFile, segmentFiles.Select(x => $"file '{x.Replace("'", "''")}'"), cancellationToken);
        var concatArgs = $"-y -f concat -safe 0 -i \"{concatFile}\" -c copy \"{outputVideoPath}\"";
        var concatResult = await processRunner.ExecuteAsync(_rendering.FfmpegPath, concatArgs, cancellationToken, TimeSpan.FromSeconds(_rendering.FfmpegTimeoutSeconds));
        if (concatResult.ExitCode != 0 || !File.Exists(outputVideoPath)) return null;

        var thumbnailPath = Path.Combine(dir, "daily-skyguide-preview-thumbnail.png");
        var thumbArgs = $"-y -i \"{outputVideoPath}\" -vf \"select=eq(n\\,0)\" -vframes 1 \"{thumbnailPath}\"";
        await processRunner.ExecuteAsync(_rendering.FfmpegPath, thumbArgs, cancellationToken, TimeSpan.FromSeconds(30));
        return outputVideoPath;
    }
}
