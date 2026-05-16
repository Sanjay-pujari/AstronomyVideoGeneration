using Astronomy.MediaFactory.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Rendering;

internal static class ProceduralAtmosphereBuffer
{
    public static void BlendIntoScene(Image<Rgba32> image, int seed, string moodProfile, float intensity = 1f, ThumbnailAtmosphereOptions? options = null, RectangleF? protectedRect = null)
    {
        options ??= new ThumbnailAtmosphereOptions();
        var configuredOpacity = (float)Math.Clamp(options.ProceduralShapeOpacity, 0.0, 0.14);
        var configuredContrast = (float)Math.Clamp(options.ProceduralShapeContrast, 0.05, 1.0);
        var configuredBlur = (float)Math.Clamp(options.ProceduralShapeBlur / 120d, 0.45, 1.85);
        var width = image.Width;
        var height = image.Height;
        var tintA = ResolveTintA(moodProfile);
        var tintB = ResolveTintB(moodProfile);
        var fieldOne = new Field(
            X: 0.18f + Hash01(seed, 11) * 0.26f,
            Y: 0.16f + Hash01(seed, 17) * 0.30f,
            RadiusX: (0.44f + Hash01(seed, 23) * 0.18f) * configuredBlur,
            RadiusY: (0.24f + Hash01(seed, 29) * 0.16f) * configuredBlur,
            Strength: (0.032f + Hash01(seed, 31) * 0.018f) * configuredContrast);
        var fieldTwo = new Field(
            X: 0.58f + Hash01(seed, 37) * 0.30f,
            Y: 0.40f + Hash01(seed, 41) * 0.22f,
            RadiusX: (0.36f + Hash01(seed, 43) * 0.16f) * configuredBlur,
            RadiusY: (0.28f + Hash01(seed, 47) * 0.16f) * configuredBlur,
            Strength: (0.018f + Hash01(seed, 53) * 0.014f) * configuredContrast);
        var diagonalBias = -0.35f + Hash01(seed, 59) * 0.70f;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var ny = height <= 1 ? 0f : y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var nx = width <= 1 ? 0f : x / (float)(width - 1);
                    var irregularEdgeMask = IrregularSceneFeather(nx, ny, seed);
                    var radial = Radial(fieldOne, nx, ny) + Radial(fieldTwo, nx, ny) * 0.82f;
                    var filament = MathF.Pow(MathF.Max(0, 1f - MathF.Abs((ny - 0.18f) - (nx - 0.20f) * (0.48f + diagonalBias)) / (0.28f * configuredBlur)), 2.2f) * 0.30f;
                    var coarseNoise = ValueNoise(nx * 5.5f, ny * 5.5f, seed) * 0.52f + ValueNoise(nx * 13.0f, ny * 9.0f, seed + 101) * 0.28f;
                    var edgeBreakup = 0.76f + ValueNoise(nx * 31.0f, ny * 27.0f, seed + 211) * 0.24f;
                    var safeReadabilityMultiplier = protectedRect.HasValue && protectedRect.Value.Contains(x, y) ? 0.24f : 1f;
                    var opacity = Math.Clamp((radial + filament) * (0.72f + coarseNoise * 0.38f) * irregularEdgeMask * edgeBreakup * intensity * safeReadabilityMultiplier, 0f, configuredOpacity);
                    if (opacity <= 0.001f)
                        continue;

                    var tintBlend = Math.Clamp(radial, 0f, 1f);
                    var haze = Lerp(tintA, tintB, tintBlend);
                    row[x] = Blend(row[x], haze, opacity);
                }
            }
        });
    }

    public static double EstimateRectangularGeometryRisk(Image<Rgba32> image, int xStep, int yStep)
    {
        var width = image.Width;
        var height = image.Height;
        var samples = 0;
        var verticalAligned = 0d;
        var horizontalAligned = 0d;
        for (var y = Math.Max(yStep, height / 10); y < height - Math.Max(yStep, height / 10); y += Math.Max(yStep, height / 90))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            var prevDelta = 0d;
            for (var x = Math.Max(xStep, width / 10); x < width - Math.Max(xStep, width / 10); x += Math.Max(xStep, width / 120))
            {
                var lum = Luminance(row[x]) / 255d;
                var right = Luminance(row[Math.Clamp(x + xStep * 3, 0, width - 1)]) / 255d;
                var downRow = image.DangerousGetPixelRowMemory(Math.Clamp(y + yStep * 3, 0, height - 1)).Span;
                var down = Luminance(downRow[x]) / 255d;
                var dx = Math.Abs(lum - right);
                var dy = Math.Abs(lum - down);
                if (dx is > 0.030 and < 0.145 && Math.Abs(dx - prevDelta) < 0.012) verticalAligned += dx;
                if (dy is > 0.030 and < 0.145) horizontalAligned += dy;
                prevDelta = dx;
                samples++;
            }
        }

        if (samples == 0) return 0;
        var alignedSignal = Math.Max(verticalAligned, horizontalAligned) / samples;
        var axisBias = Math.Abs(verticalAligned - horizontalAligned) / samples;
        return Math.Clamp(alignedSignal * 9.5d + Math.Max(0, axisBias - 0.002d) * 18d, 0, 1);
    }

    public static double ScoreProceduralAtmosphere(double organicAtmosphereScore, double atmosphereContinuityScore, double compositingSeamPenalty, double rectangularGeometryRisk)
        => Math.Clamp((organicAtmosphereScore * 0.32d) + (atmosphereContinuityScore * 0.34d) + ((1d - compositingSeamPenalty) * 0.18d) + ((1d - rectangularGeometryRisk) * 0.16d), 0, 1);

    private static float IrregularSceneFeather(float nx, float ny, int seed)
    {
        var edgeDistance = MathF.Min(MathF.Min(nx, 1f - nx), MathF.Min(ny, 1f - ny));
        var breakup = ValueNoise(nx * 18f, ny * 18f, seed + 307) * 0.035f;
        return Math.Clamp((edgeDistance + breakup) / 0.11f, 0f, 1f);
    }

    private static float Radial(Field field, float nx, float ny)
    {
        var dx = (nx - field.X) / field.RadiusX;
        var dy = (ny - field.Y) / field.RadiusY;
        return MathF.Pow(MathF.Max(0f, 1f - (dx * dx + dy * dy)), 2.4f) * field.Strength;
    }

    private static Rgba32 Blend(Rgba32 src, Rgba32 overlay, float alpha)
    {
        var inv = 1f - alpha;
        return new Rgba32(
            (byte)Math.Clamp(src.R * inv + overlay.R * alpha, 0, 255),
            (byte)Math.Clamp(src.G * inv + overlay.G * alpha, 0, 255),
            (byte)Math.Clamp(src.B * inv + overlay.B * alpha, 0, 255),
            src.A);
    }

    private static Rgba32 Lerp(Rgba32 a, Rgba32 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var inv = 1f - t;
        return new Rgba32((byte)(a.R * inv + b.R * t), (byte)(a.G * inv + b.G * t), (byte)(a.B * inv + b.B * t), 255);
    }

    private static Rgba32 ResolveTintA(string moodProfile) => moodProfile switch
    {
        "deepSpace" or "DeepSpaceMode" or "WideSkyMode" => new Rgba32(70, 90, 150, 255),
        "warmGlow" or "sunset" or "EclipseMode" => new Rgba32(142, 88, 62, 255),
        _ => new Rgba32(62, 92, 156, 255)
    };

    private static Rgba32 ResolveTintB(string moodProfile) => moodProfile switch
    {
        "deepSpace" or "DeepSpaceMode" or "WideSkyMode" => new Rgba32(116, 72, 160, 255),
        "warmGlow" or "sunset" or "EclipseMode" => new Rgba32(96, 86, 142, 255),
        _ => new Rgba32(94, 76, 132, 255)
    };

    private static float ValueNoise(float x, float y, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = Smooth(x - x0);
        var ty = Smooth(y - y0);
        var a = Hash01(seed, x0 * 73856093 ^ y0 * 19349663);
        var b = Hash01(seed, (x0 + 1) * 73856093 ^ y0 * 19349663);
        var c = Hash01(seed, x0 * 73856093 ^ (y0 + 1) * 19349663);
        var d = Hash01(seed, (x0 + 1) * 73856093 ^ (y0 + 1) * 19349663);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Hash01(int seed, int salt)
    {
        unchecked
        {
            var n = (uint)(seed * 374761393 + salt * 668265263);
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0x00FFFFFF) / 16777215f;
        }
    }

    private static double Luminance(Rgba32 pixel)
        => (0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B);

    private sealed record Field(float X, float Y, float RadiusX, float RadiusY, float Strength);
}
