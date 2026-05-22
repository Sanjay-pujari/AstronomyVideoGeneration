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

    public async Task<AssetAwarePreviewVideoComposeResult> ComposeAsync(AssetAwareVideoCompositionPlan plan, AssetAwarePreviewVideoRequest request, string outputVideoPath, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(outputVideoPath)!;
        Directory.CreateDirectory(dir);
        var segments = plan.Segments.Where(x => x.ImageExists && !string.IsNullOrWhiteSpace(x.ImagePath)).OrderBy(x => x.SortOrder).ToList();
        if (segments.Count == 0)
            return new(null, null, null, null, "No valid image segments found.", null, _rendering.FfmpegPath);

        var segmentFiles = new List<string>();
        string? lastArgs = null;
        ProcessExecutionResult? lastResult = null;
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
            lastArgs = args;
            var result = await processRunner.ExecuteAsync(_rendering.FfmpegPath, args, cancellationToken, TimeSpan.FromSeconds(_rendering.FfmpegSegmentTimeoutSeconds));
            lastResult = result;
            if (result.ExitCode != 0 || !File.Exists(segPath) || new FileInfo(segPath).Length <= 0)
                return new(outputVideoPath, null, args, result.ExitCode, result.StandardError, result.StandardOutput, _rendering.FfmpegPath);
            segmentFiles.Add(segPath);
        }

        var concatFile = Path.Combine(dir, "preview-concat.txt");
        await File.WriteAllLinesAsync(concatFile, segmentFiles.Select(x => $"file '{x.Replace("'", "''")}'"), cancellationToken);
        var concatArgs = $"-y -f concat -safe 0 -i \"{concatFile}\" -c copy \"{outputVideoPath}\"";
        lastArgs = concatArgs;
        var concatResult = await processRunner.ExecuteAsync(_rendering.FfmpegPath, concatArgs, cancellationToken, TimeSpan.FromSeconds(_rendering.FfmpegTimeoutSeconds));
        lastResult = concatResult;
        if (concatResult.ExitCode != 0 || !File.Exists(outputVideoPath) || new FileInfo(outputVideoPath).Length <= 0)
            return new(outputVideoPath, null, concatArgs, concatResult.ExitCode, concatResult.StandardError, concatResult.StandardOutput, _rendering.FfmpegPath);

        var thumbnailPath = Path.Combine(dir, "daily-skyguide-preview-thumbnail.png");
        var totalDurationSeconds = Math.Max(segments.Sum(x => x.SuggestedDurationSeconds), 0d);
        var thumbnailTimestampSeconds = ResolveThumbnailTimestamp(totalDurationSeconds);
        var thumbnailTimestamp = TimeSpan.FromSeconds(thumbnailTimestampSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        var thumbArgs = $"-y -ss {thumbnailTimestamp} -i \"{outputVideoPath}\" -frames:v 1 \"{thumbnailPath}\"";
        lastArgs = thumbArgs;
        var thumbResult = await processRunner.ExecuteAsync(_rendering.FfmpegPath, thumbArgs, cancellationToken, TimeSpan.FromSeconds(30));
        lastResult = thumbResult;
        if (!File.Exists(thumbnailPath) || new FileInfo(thumbnailPath).Length <= 0 || await IsLikelyBlackFrameAsync(thumbnailPath, cancellationToken))
        {
            var thumbnailCandidatePath = segments
                .FirstOrDefault(x => x.VisualRole.Equals("ThumbnailCandidate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.ImagePath) && File.Exists(x.ImagePath))
                ?.ImagePath;

            if (!string.IsNullOrWhiteSpace(thumbnailCandidatePath))
            {
                File.Copy(thumbnailCandidatePath, thumbnailPath, overwrite: true);
            }
        }

        return new(outputVideoPath, thumbnailPath, lastArgs, lastResult.ExitCode, lastResult.StandardError, lastResult.StandardOutput, _rendering.FfmpegPath);
    }

    private static double ResolveThumbnailTimestamp(double totalDurationSeconds)
    {
        if (totalDurationSeconds < 3d)
            return 1d;

        const double preferredTimestamp = 2d;
        if (preferredTimestamp < totalDurationSeconds)
            return preferredTimestamp;

        return Math.Max(1d, totalDurationSeconds * 0.1d);
    }

    private async Task<bool> IsLikelyBlackFrameAsync(string thumbnailPath, CancellationToken cancellationToken)
    {
        var args = $"-hide_banner -i \"{thumbnailPath}\" -vf \"blackframe=amount=98:threshold=32\" -f null -";
        var result = await processRunner.ExecuteAsync(_rendering.FfmpegPath, args, cancellationToken, TimeSpan.FromSeconds(15));
        var stderr = result.StandardError ?? string.Empty;
        return stderr.Contains("pblack:", StringComparison.OrdinalIgnoreCase);
    }
}
