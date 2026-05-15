using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailScoringService : IThumbnailScoringService
{
    public async Task<ThumbnailCandidateScore> ScoreAsync(string candidatePath, ThumbnailScoringContext context, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(candidatePath, cancellationToken);
        var xStep = Math.Max(1, image.Width / 240);
        var yStep = Math.Max(1, image.Height / 180);
        var total = 0;
        var black = 0;
        var bright = 0;
        var starPixels = 0;
        var colorRich = 0d;
        var sum = 0d;
        var sumSq = 0d;
        var sharpness = 0d;
        var glow = 0d;
        var focalWeightX = 0d;
        var focalWeightY = 0d;
        var focalWeight = 0d;
        Rgba32? previous = null;

        for (var y = 0; y < image.Height; y += yStep)
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += xStep)
            {
                var px = row[x];
                var luminance = Luminance(px) / 255d;
                total++;
                sum += luminance;
                sumSq += luminance * luminance;
                if (luminance < 0.08) black++;
                if (luminance > 0.62)
                {
                    bright++;
                    focalWeight += luminance;
                    focalWeightX += x * luminance;
                    focalWeightY += y * luminance;
                }
                if (luminance is > 0.22 and < 0.82 && IsSmallStarLike(px)) starPixels++;
                colorRich += (Math.Max(px.R, Math.Max(px.G, px.B)) - Math.Min(px.R, Math.Min(px.G, px.B))) / 255d;

                if (previous is { } p)
                {
                    var delta = Math.Abs(Luminance(px) - Luminance(p)) / 255d;
                    sharpness += delta;
                    if (luminance is > 0.14 and < 0.62) glow += delta < 0.22 ? luminance : 0;
                }
                previous = px;
            }
        }

        total = Math.Max(1, total);
        var brightness = sum / total;
        var variance = Math.Max(0, (sumSq / total) - (brightness * brightness));
        var contrast = Math.Min(1, Math.Sqrt(variance) * 3.0);
        var blackPixelPercentage = black / (double)total;
        var brightRatio = bright / (double)total;
        var objectDetected = brightRatio > 0.0008 || (contrast > 0.16 && brightness > 0.025);
        var objectVisibility = Math.Min(1, brightRatio * 48 + contrast * 0.25);
        var celestialFocalSize = objectDetected ? Math.Min(1, Math.Sqrt(Math.Max(brightRatio, 0.0001)) * 5.5) : 0;
        var focalObjectScore = objectDetected ? Math.Clamp((objectVisibility * 0.62) + (celestialFocalSize * 0.28) + (contrast * 0.10), 0, 1) : 0;
        var colorRichness = Math.Min(1, colorRich / total * 2.2);
        var glowScore = Math.Clamp((glow / total * 8) + (colorRichness * 0.22), 0, 1);
        var starRichness = Math.Clamp(starPixels / (double)total * 18 + brightRatio * 8, 0, 1);
        var textSafe = EstimateTextSafeArea(image);
        var sharpnessScore = Math.Min(1, sharpness / total * 11);
        var compositionBalance = EstimateCompositionBalance(image, focalWeight, focalWeightX, focalWeightY);
        var transitionFade = brightness < 0.025 && contrast < 0.035 && brightRatio < 0.0005;

        string? rejectionReason = null;
        if (transitionFade)
            rejectionReason = "transition-or-fade-frame";
        else if (!objectDetected && blackPixelPercentage > context.MaxBlackPixelPercentage)
            rejectionReason = "empty-black-frame";
        else if (sharpnessScore < 0.018 && !objectDetected)
            rejectionReason = "blurry-frame";
        else if (textSafe < 0.05)
            rejectionReason = "insufficient-text-safe-area";
        else if (!context.EnableAstronomySceneMode && context.RejectDarkFrames && blackPixelPercentage > context.MaxBlackPixelPercentage)
            rejectionReason = "black-pixel-threshold";
        else if (!context.EnableAstronomySceneMode && context.RejectDarkFrames && brightness < context.MinimumBrightnessScore)
            rejectionReason = "minimum-brightness";

        var score = rejectionReason is not null
            ? 0
            : context.EnableAstronomySceneMode
                ? (focalObjectScore * 0.35)
                  + (contrast * 0.20)
                  + (glowScore * 0.15)
                  + (starRichness * 0.10)
                  + (textSafe * 0.10)
                  + (compositionBalance * 0.10)
                : (brightness * 0.16) + (contrast * 0.16) + ((1 - blackPixelPercentage) * 0.14) + (objectVisibility * 0.16) + (celestialFocalSize * 0.12) + (colorRichness * 0.10) + (textSafe * 0.08) + (sharpnessScore * 0.08);

        return new ThumbnailCandidateScore
        {
            Path = candidatePath,
            SceneId = context.SceneId,
            TimestampSeconds = context.TimestampSeconds,
            Score = Math.Round(score, 4),
            Brightness = Math.Round(brightness, 4),
            BlackPixelPercentage = Math.Round(blackPixelPercentage, 4),
            Contrast = Math.Round(contrast, 4),
            ObjectDetected = objectDetected,
            ObjectVisibility = Math.Round(objectVisibility, 4),
            FocalObjectScore = Math.Round(focalObjectScore, 4),
            GlowScore = Math.Round(glowScore, 4),
            StarRichnessScore = Math.Round(starRichness, 4),
            CompositionBalanceScore = Math.Round(compositionBalance, 4),
            CelestialFocalSize = Math.Round(celestialFocalSize, 4),
            ColorRichness = Math.Round(colorRichness, 4),
            TextSafeCompositionArea = Math.Round(textSafe, 4),
            Sharpness = Math.Round(sharpnessScore, 4),
            RejectionReason = rejectionReason
        };
    }

    private static bool IsSmallStarLike(Rgba32 px) => Math.Abs(px.R - px.G) < 42 && Math.Abs(px.G - px.B) < 56;

    private static double EstimateCompositionBalance(Image<Rgba32> image, double focalWeight, double focalWeightX, double focalWeightY)
    {
        if (focalWeight <= 0) return 0.35;
        var x = focalWeightX / focalWeight / image.Width;
        var y = focalWeightY / focalWeight / image.Height;
        var ruleOfThirds = new[] { 1d / 3d, 2d / 3d }.Min(t => Math.Abs(x - t)) + new[] { 0.38d, 0.50d, 0.62d }.Min(t => Math.Abs(y - t));
        return Math.Clamp(1 - ruleOfThirds * 1.7, 0, 1);
    }

    private static double EstimateTextSafeArea(Image<Rgba32> image)
    {
        var bottomStart = (int)(image.Height * 0.62);
        var samples = 0;
        var usable = 0;
        for (var y = bottomStart; y < image.Height; y += Math.Max(1, image.Height / 80))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 120))
            {
                var lum = Luminance(row[x]) / 255d;
                samples++;
                if (lum < 0.68) usable++;
            }
        }

        return samples == 0 ? 0.5 : usable / (double)samples;
    }

    private static double Luminance(Rgba32 pixel)
        => (0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B);
}
