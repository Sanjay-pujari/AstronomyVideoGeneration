using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using IoPath = System.IO.Path;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailCandidateSelector : IThumbnailCandidateSelector
{
    private readonly IThumbnailScoringService _thumbnailScoringService;
    private readonly ThumbnailOptions _options;

    public ThumbnailCandidateSelector(IThumbnailScoringService thumbnailScoringService, IOptions<ThumbnailOptions> options)
    {
        _thumbnailScoringService = thumbnailScoringService;
        _options = options.Value;
    }

    public async Task<ThumbnailCandidateSelection> SelectAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var candidates = await BuildCandidatesAsync(request, cancellationToken);
        var scores = new List<ThumbnailCandidateScore>(candidates.Count);
        var errors = new List<string>();

        foreach (var candidate in candidates)
        {
            try
            {
                scores.Add(await _thumbnailScoringService.ScoreAsync(candidate.Path, new ThumbnailScoringContext
                {
                    MaxBlackPixelPercentage = _options.MaxBlackPixelPercentage,
                    MinimumBrightnessScore = _options.MinimumBrightnessScore,
                    RejectDarkFrames = _options.RejectDarkFrames,
                    SceneId = candidate.SceneId,
                    TimestampSeconds = candidate.TimestampSeconds,
                    EnableAstronomySceneMode = _options.EnableAstronomySceneMode
                }, cancellationToken));
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.Path}: {ex.Message}");
            }
        }

        var selected = scores.Where(x => !x.IsRejected).OrderByDescending(x => x.Score).FirstOrDefault();
        var fallbackUsed = false;
        if (selected is null)
        {
            selected = await CreateFallbackCandidateAsync(request, cancellationToken);
            scores.Add(selected);
            fallbackUsed = true;
        }

        return new ThumbnailCandidateSelection
        {
            SelectedCandidate = selected,
            CandidateScores = scores,
            FallbackUsed = fallbackUsed,
            Errors = errors
        };
    }

    private async Task<List<ThumbnailCandidate>> BuildCandidatesAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var candidatesDirectory = IoPath.Combine(request.OutputDirectory, "thumbnails", "candidates");
        Directory.CreateDirectory(candidatesDirectory);
        var visuals = request.AvailableVisuals.Where(File.Exists).ToArray();
        var candidates = new List<ThumbnailCandidate>();

        for (var i = 0; i < visuals.Length; i++)
        {
            var scene = request.Scenes.ElementAtOrDefault(i);
            var sceneId = scene?.SceneId ?? request.Context.SceneObservationContexts.ElementAtOrDefault(i)?.SceneId ?? $"scene-{i + 1:000}";
            var duration = Math.Max(1, scene?.DurationSeconds ?? EstimateSceneDurationSeconds(request));
            foreach (var timestamp in BuildSampleTimes(duration))
            {
                var safeTimestamp = AvoidFadeFrame(timestamp, duration);
                if (safeTimestamp is null)
                    continue;

                var suffix = Math.Round(safeTimestamp.Value / duration * 100).ToString("000");
                var output = IoPath.Combine(candidatesDirectory, $"{SanitizeFileName(sceneId)}-{suffix}.jpg");
                await CopyAsCandidateAsync(visuals[i], output, cancellationToken);
                candidates.Add(new ThumbnailCandidate(output, sceneId, safeTimestamp.Value));
            }
        }

        return candidates;
    }

    private IEnumerable<double> BuildSampleTimes(double durationSeconds)
    {
        var ratios = durationSeconds > 20
            ? new[] { 0.20, 0.35, 0.50, 0.65, 0.80 }
            : new[] { 0.30, 0.50, 0.70 };

        foreach (var ratio in ratios.Take(Math.Max(1, _options.CandidateFramesPerScene)))
            yield return durationSeconds * ratio;
    }

    private double? AvoidFadeFrame(double timestamp, double durationSeconds)
    {
        if (!_options.AvoidFadeFrames)
            return timestamp;

        var fade = Math.Max(0, _options.FadeAvoidanceSeconds);
        if (durationSeconds <= fade * 2)
            return durationSeconds / 2d;

        return Math.Clamp(timestamp, fade, durationSeconds - fade);
    }

    private static int EstimateSceneDurationSeconds(ThumbnailGenerationRequest request)
        => request.IsShortForm ? 6 : request.Context.SceneObservationContexts.Count > 0 ? 10 : 6;

    private static async Task CopyAsCandidateAsync(string input, string output, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private async Task<ThumbnailCandidateScore> CreateFallbackCandidateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var path = IoPath.Combine(request.OutputDirectory, "thumbnails", "candidates", "fallback.jpg");
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(request.IsShortForm ? _options.PortraitWidth : _options.LandscapeWidth, request.IsShortForm ? _options.PortraitHeight : _options.LandscapeHeight, new Rgba32(6, 12, 32));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(image.Width, image.Height), GradientRepetitionMode.None,
                new ColorStop(0, Color.FromRgb(8, 16, 42)),
                new ColorStop(1, Color.FromRgb(54, 38, 92))), new RectangleF(0, 0, image.Width, image.Height));
            ctx.Fill(Color.White.WithAlpha(0.85f), new EllipsePolygon(image.Width * 0.58f, image.Height * 0.38f, image.Width * 0.08f));
        });
        await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = 92 }, cancellationToken);
        return await _thumbnailScoringService.ScoreAsync(path, new ThumbnailScoringContext { RejectDarkFrames = false, EnableAstronomySceneMode = _options.EnableAstronomySceneMode }, cancellationToken);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = IoPath.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "scene" : cleaned;
    }

    private sealed record ThumbnailCandidate(string Path, string SceneId, double TimestampSeconds);
}
