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
        var edgeMidTone = 0;
        var midTone = 0;
        var radialRing = 0;
        var radialSamples = 0;
        var symmetryDelta = 0d;
        var symmetrySamples = 0;
        var cells = new double[4, 6];
        var cellCounts = new int[4, 6];
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
                if (luminance is > 0.10 and < 0.58)
                {
                    midTone++;
                    var edgeDistance = Math.Min(Math.Min(x / (double)image.Width, 1 - (x / (double)image.Width)), Math.Min(y / (double)image.Height, 1 - (y / (double)image.Height)));
                    if (edgeDistance < 0.14) edgeMidTone++;
                }

                var normalizedRadius = Math.Sqrt(Math.Pow((x - image.Width / 2d) / image.Width, 2) + Math.Pow((y - image.Height / 2d) / image.Height, 2));
                if (normalizedRadius is > 0.18 and < 0.34)
                {
                    radialSamples++;
                    if (luminance is > 0.12 and < 0.64) radialRing++;
                }

                var cellX = Math.Clamp((int)(x / (double)image.Width * 6), 0, 5);
                var cellY = Math.Clamp((int)(y / (double)image.Height * 4), 0, 3);
                cells[cellY, cellX] += luminance;
                cellCounts[cellY, cellX]++;

                var mirrorX = image.Width - 1 - x;
                if (mirrorX >= 0 && mirrorX < row.Length)
                {
                    symmetryDelta += Math.Abs(luminance - (Luminance(row[mirrorX]) / 255d));
                    symmetrySamples++;
                }

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
        var organicAtmosphereScore = EstimateOrganicAtmosphereScore(cells, cellCounts, colorRichness, starRichness);
        var symmetryPenalty = EstimateSymmetryPenalty(symmetryDelta, symmetrySamples);
        var centeredBiasPenalty = EstimateCenteredBiasPenalty(image, focalWeight, focalWeightX, focalWeightY);
        var geometricMaskPenalty = EstimateGeometricMaskPenalty(midTone, edgeMidTone, radialRing, radialSamples);
        var compositingSeamPenalty = EstimateCompositingSeamPenalty(image, xStep, yStep);
        var atmosphereContinuityScore = Math.Clamp((organicAtmosphereScore * 0.48) + ((1 - compositingSeamPenalty) * 0.32) + ((1 - geometricMaskPenalty) * 0.20), 0, 1);
        var environmentalDepthScore = Math.Clamp((starRichness * 0.28) + (colorRichness * 0.22) + (organicAtmosphereScore * 0.34) + (contrast * 0.16), 0, 1);
        var supportObjectDepthScore = Math.Clamp((1 - centeredBiasPenalty) * 0.34 + (1 - symmetryPenalty) * 0.28 + atmosphereContinuityScore * 0.38, 0, 1);
        var edgeIntegrationScore = Math.Clamp((glowScore * 0.34) + (NaturalLightingScoreFromInputs(contrast, symmetryPenalty, centeredBiasPenalty, glowScore) * 0.24) + ((1 - compositingSeamPenalty) * 0.24) + (atmosphereContinuityScore * 0.18), 0, 1);
        var compositingVisibilityPenalty = Math.Clamp((geometricMaskPenalty * 0.40) + (compositingSeamPenalty * 0.30) + (symmetryPenalty * 0.16) + (centeredBiasPenalty * 0.14), 0, 1);
        var visualArtifactPenalty = Math.Clamp((blackPixelPercentage > 0.94 ? 0.22 : 0) + Math.Max(0, sharpnessScore - 0.88) * 0.32 + geometricMaskPenalty * 0.30 + compositingSeamPenalty * 0.28, 0, 1);
        var naturalLightingScore = NaturalLightingScoreFromInputs(contrast, symmetryPenalty, centeredBiasPenalty, glowScore);
        var cinematicSubtletyScore = Math.Clamp((organicAtmosphereScore * 0.28) + (naturalLightingScore * 0.26) + (edgeIntegrationScore * 0.16) + (atmosphereContinuityScore * 0.14) + ((1 - visualArtifactPenalty) * 0.08) + ((1 - compositingVisibilityPenalty) * 0.08), 0, 1);
        var naturalCompositionPenalty = Math.Clamp((visualArtifactPenalty * 0.38) + (compositingVisibilityPenalty * 0.46) + (compositingSeamPenalty * 0.16), 0, 0.32);
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
                  + (textSafe * 0.08)
                  + (compositionBalance * 0.08)
                  + (organicAtmosphereScore * 0.07)
                  + (naturalLightingScore * 0.07)
                  + (cinematicSubtletyScore * 0.05)
                  - naturalCompositionPenalty
                : (brightness * 0.16) + (contrast * 0.16) + ((1 - blackPixelPercentage) * 0.14) + (objectVisibility * 0.16) + (celestialFocalSize * 0.12) + (colorRichness * 0.10) + (textSafe * 0.08) + (sharpnessScore * 0.08);

        return new ThumbnailCandidateScore
        {
            Path = candidatePath,
            SceneId = context.SceneId,
            TimestampSeconds = context.TimestampSeconds,
            Score = Math.Round(Math.Clamp(score, 0, 1), 4),
            Brightness = Math.Round(brightness, 4),
            BlackPixelPercentage = Math.Round(blackPixelPercentage, 4),
            Contrast = Math.Round(contrast, 4),
            ObjectDetected = objectDetected,
            ObjectVisibility = Math.Round(objectVisibility, 4),
            FocalObjectScore = Math.Round(focalObjectScore, 4),
            GlowScore = Math.Round(glowScore, 4),
            StarRichnessScore = Math.Round(starRichness, 4),
            CompositionBalanceScore = Math.Round(compositionBalance, 4),
            OrganicAtmosphereScore = Math.Round(organicAtmosphereScore, 4),
            NaturalLightingScore = Math.Round(naturalLightingScore, 4),
            VisualArtifactPenalty = Math.Round(visualArtifactPenalty, 4),
            CompositingVisibilityPenalty = Math.Round(compositingVisibilityPenalty, 4),
            CinematicSubtletyScore = Math.Round(cinematicSubtletyScore, 4),
            EdgeIntegrationScore = Math.Round(edgeIntegrationScore, 4),
            CompositingSeamPenalty = Math.Round(compositingSeamPenalty, 4),
            AtmosphereContinuityScore = Math.Round(atmosphereContinuityScore, 4),
            EnvironmentalDepthScore = Math.Round(environmentalDepthScore, 4),
            SupportObjectDepthScore = Math.Round(supportObjectDepthScore, 4),
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


    private static double EstimateOrganicAtmosphereScore(double[,] cells, int[,] cellCounts, double colorRichness, double starRichness)
    {
        var averages = new List<double>();
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                if (cellCounts[y, x] > 0) averages.Add(cells[y, x] / cellCounts[y, x]);
            }
        }

        if (averages.Count == 0) return 0.45;
        var mean = averages.Average();
        var variance = averages.Sum(v => Math.Pow(v - mean, 2)) / averages.Count;
        var nonUniformity = Math.Clamp(Math.Sqrt(variance) * 6.0, 0, 1);
        var dustTone = Math.Clamp(mean * 2.4, 0, 1);
        return Math.Clamp((nonUniformity * 0.42) + (dustTone * 0.20) + (colorRichness * 0.20) + (starRichness * 0.18), 0, 1);
    }

    private static double EstimateSymmetryPenalty(double symmetryDelta, int symmetrySamples)
    {
        if (symmetrySamples == 0) return 0;
        var averageDelta = symmetryDelta / symmetrySamples;
        return Math.Clamp(1 - (averageDelta * 5.5), 0, 1);
    }

    private static double EstimateCenteredBiasPenalty(Image<Rgba32> image, double focalWeight, double focalWeightX, double focalWeightY)
    {
        if (focalWeight <= 0) return 0.20;
        var x = focalWeightX / focalWeight / image.Width;
        var y = focalWeightY / focalWeight / image.Height;
        var distanceFromCenter = Math.Sqrt(Math.Pow(x - 0.5, 2) + Math.Pow(y - 0.5, 2));
        return Math.Clamp(1 - (distanceFromCenter / 0.26), 0, 1);
    }

    private static double NaturalLightingScoreFromInputs(double contrast, double symmetryPenalty, double centeredBiasPenalty, double glowScore)
        => Math.Clamp((contrast * 0.30) + ((1 - symmetryPenalty) * 0.30) + ((1 - centeredBiasPenalty) * 0.22) + ((1 - glowScore) * 0.18), 0, 1);

    private static double EstimateCompositingSeamPenalty(Image<Rgba32> image, int xStep, int yStep)
    {
        var vertical = 0d;
        var horizontal = 0d;
        var samples = 0;
        for (var y = Math.Max(yStep, image.Height / 12); y < image.Height - yStep; y += Math.Max(yStep, image.Height / 72))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = Math.Max(xStep, image.Width / 12); x < image.Width - xStep; x += Math.Max(xStep, image.Width / 96))
            {
                var center = Luminance(row[x]) / 255d;
                var dx = Math.Abs(center - (Luminance(row[Math.Clamp(x + xStep * 2, 0, image.Width - 1)]) / 255d));
                var dyRow = image.DangerousGetPixelRowMemory(Math.Clamp(y + yStep * 2, 0, image.Height - 1)).Span;
                var dy = Math.Abs(center - (Luminance(dyRow[x]) / 255d));
                if (dx > 0.16) vertical += dx;
                if (dy > 0.16) horizontal += dy;
                samples++;
            }
        }

        if (samples == 0) return 0;
        var directionalSeamSignal = Math.Max(vertical, horizontal) / samples;
        var rectangularLayerSignal = Math.Max(0, Math.Abs(vertical - horizontal) / samples - 0.006) * 8.5;
        return Math.Clamp(directionalSeamSignal * 1.8 + rectangularLayerSignal, 0, 1);
    }

    private static double EstimateGeometricMaskPenalty(int midTone, int edgeMidTone, int radialRing, int radialSamples)
    {
        if (midTone == 0 || radialSamples == 0) return 0;
        var edgeRatio = edgeMidTone / (double)midTone;
        var ringRatio = radialRing / (double)radialSamples;
        var circularOverlaySignal = Math.Max(0, ringRatio - 0.42) * 1.8;
        var hardEdgeSignal = Math.Max(0, edgeRatio - 0.36) * 1.4;
        return Math.Clamp((circularOverlaySignal * 0.62) + (hardEdgeSignal * 0.38), 0, 1);
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
