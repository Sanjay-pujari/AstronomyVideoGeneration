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
        var total = 0;
        var black = 0;
        var bright = 0;
        var colorRich = 0d;
        var sum = 0d;
        var sumSq = 0d;
        var sharpness = 0d;
        Rgba32? previous = null;

        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 180))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 240))
            {
                var px = row[x];
                var luminance = Luminance(px) / 255d;
                total++;
                sum += luminance;
                sumSq += luminance * luminance;
                if (luminance < 0.08) black++;
                if (luminance > 0.72) bright++;
                colorRich += (Math.Max(px.R, Math.Max(px.G, px.B)) - Math.Min(px.R, Math.Min(px.G, px.B))) / 255d;

                if (previous is { } p)
                    sharpness += Math.Abs(Luminance(px) - Luminance(p)) / 255d;
                previous = px;
            }
        }

        total = Math.Max(1, total);
        var brightness = sum / total;
        var variance = Math.Max(0, (sumSq / total) - (brightness * brightness));
        var contrast = Math.Min(1, Math.Sqrt(variance) * 2.2);
        var blackPixelPercentage = black / (double)total;
        var brightRatio = bright / (double)total;
        var objectDetected = brightRatio > 0.002;
        var objectVisibility = Math.Min(1, brightRatio * 28);
        var celestialFocalSize = objectDetected ? Math.Min(1, Math.Sqrt(brightRatio) * 4.5) : 0;
        var colorRichness = Math.Min(1, colorRich / total * 1.8);
        var textSafe = EstimateTextSafeArea(image);
        var sharpnessScore = Math.Min(1, sharpness / total * 10);

        string? rejectionReason = null;
        if (context.RejectDarkFrames && blackPixelPercentage > context.MaxBlackPixelPercentage)
            rejectionReason = "black-pixel-threshold";
        else if (context.RejectDarkFrames && brightness < context.MinimumBrightnessScore)
            rejectionReason = "minimum-brightness";

        var score = rejectionReason is not null
            ? 0
            : (brightness * 0.16)
              + (contrast * 0.16)
              + ((1 - blackPixelPercentage) * 0.14)
              + (objectVisibility * 0.16)
              + (celestialFocalSize * 0.12)
              + (colorRichness * 0.10)
              + (textSafe * 0.08)
              + (sharpnessScore * 0.08);

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
            CelestialFocalSize = Math.Round(celestialFocalSize, 4),
            ColorRichness = Math.Round(colorRichness, 4),
            TextSafeCompositionArea = Math.Round(textSafe, 4),
            Sharpness = Math.Round(sharpnessScore, 4),
            RejectionReason = rejectionReason
        };
    }

    private static double EstimateTextSafeArea(Image<Rgba32> image)
    {
        var bottomStart = (int)(image.Height * 0.66);
        var samples = 0;
        var moderate = 0;
        for (var y = bottomStart; y < image.Height; y += Math.Max(1, image.Height / 80))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 120))
            {
                var lum = Luminance(row[x]) / 255d;
                samples++;
                if (lum is > 0.08 and < 0.62) moderate++;
            }
        }

        return samples == 0 ? 0.5 : moderate / (double)samples;
    }

    private static double Luminance(Rgba32 pixel)
        => (0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B);
}
