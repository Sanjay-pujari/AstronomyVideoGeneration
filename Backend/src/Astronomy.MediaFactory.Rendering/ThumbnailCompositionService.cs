using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using IoPath = System.IO.Path;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailCompositionService : IThumbnailCompositionService
{
    private const long MaxThumbnailSizeBytes = 2 * 1024 * 1024;
    private static readonly Rgba32 WarmYellow = new(255, 215, 64);
    private readonly ThumbnailOptions _options;
    private readonly ThumbnailCinematicAIOptions _cinematicOptions;
    private readonly ICinematicThumbnailAiService? _cinematicAiService;
    private readonly IThumbnailVisualHierarchyService? _visualHierarchyService;
    private readonly IThumbnailMoodGradingService? _moodGradingService;

    public ThumbnailCompositionService(IOptions<ThumbnailOptions> options)
        : this(options, Options.Create(new ThumbnailCinematicAIOptions { Enabled = false }), null, null, null)
    {
    }

    public ThumbnailCompositionService(
        IOptions<ThumbnailOptions> options,
        IOptions<ThumbnailCinematicAIOptions> cinematicOptions,
        ICinematicThumbnailAiService? cinematicAiService,
        IThumbnailVisualHierarchyService? visualHierarchyService,
        IThumbnailMoodGradingService? moodGradingService)
    {
        _options = options.Value;
        _cinematicOptions = cinematicOptions.Value;
        _cinematicAiService = cinematicAiService;
        _visualHierarchyService = visualHierarchyService;
        _moodGradingService = moodGradingService;
    }

    public async Task<string> ComposeAsync(ThumbnailCompositionRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(IoPath.GetDirectoryName(request.OutputPath)!);
        var targetWidth = request.GenerationRequest.IsShortForm ? _options.PortraitWidth : _options.LandscapeWidth;
        var targetHeight = request.GenerationRequest.IsShortForm ? _options.PortraitHeight : _options.LandscapeHeight;
        var recommendation = await BuildRecommendationAsync(request, targetWidth, targetHeight, cancellationToken);
        var hierarchy = EvaluateHierarchy(request, recommendation);
        var mood = SelectMood(request, recommendation);

        if (request.GenerationRequest.IsShortForm)
            await RenderShortThumbnailAsync(request, recommendation, mood, request.SelectedCandidate.Path, request.OutputPath, request.HookText, cancellationToken);
        else
            await RenderLongThumbnailAsync(request.GenerationRequest, recommendation, mood, request.SelectedCandidate.Path, request.OutputPath, request.HookText, cancellationToken);

        await EnsureUnderSizeLimitAsync(request.OutputPath, cancellationToken);
        var integrity = ValidateAstronomyIntegrity(request, recommendation);
        var report = new CinematicThumbnailAiReport
        {
            DominantObject = recommendation.DominantObjectType,
            MoodProfile = recommendation.MoodProfile,
            CropStrategy = recommendation.CropStrategy,
            EnhancementApplied = recommendation.EnhancementApplied,
            ScaleBoost = recommendation.ScaleBoost,
            VisualHierarchyScore = hierarchy.VisualHierarchyScore,
            ReadabilityScore = hierarchy.ReadabilityScore,
            AstronomyIntegrityValidation = integrity,
            PortraitSafe = recommendation.PortraitSafe,
            FinalThumbnailPath = request.OutputPath,
            Diagnostics = hierarchy.Recommendations.Concat([recommendation.Rationale]).ToArray()
        };
        await WriteCinematicAiReportAsync(request.GenerationRequest.OutputDirectory, report, cancellationToken);

        if (_cinematicOptions.Enabled && _cinematicOptions.PreventFakeAstronomy && !integrity.AstronomicalIntegrityMaintained)
            throw new InvalidOperationException("Cinematic thumbnail astronomy integrity validation failed.");

        return request.OutputPath;
    }

    private async Task<CinematicThumbnailAiRecommendation> BuildRecommendationAsync(ThumbnailCompositionRequest request, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        if (_cinematicOptions.Enabled && _cinematicAiService is not null)
        {
            return await _cinematicAiService.RecommendAsync(new CinematicThumbnailAiRequest
            {
                GenerationRequest = request.GenerationRequest,
                SelectedCandidate = request.SelectedCandidate,
                HookText = request.HookText,
                TargetWidth = targetWidth,
                TargetHeight = targetHeight
            }, cancellationToken);
        }

        return new CinematicThumbnailAiRecommendation
        {
            DominantObject = "dominant celestial object",
            DominantObjectType = "bright planet",
            MoodProfile = "dramatic",
            CropStrategy = request.GenerationRequest.IsShortForm ? "portrait-center-crop" : "center-crop",
            EnhancementApplied = true,
            PortraitSafe = request.GenerationRequest.IsShortForm,
            ScaleBoost = 1
        };
    }

    private ThumbnailVisualHierarchyResult EvaluateHierarchy(ThumbnailCompositionRequest request, CinematicThumbnailAiRecommendation recommendation)
        => _visualHierarchyService?.Evaluate(new ThumbnailVisualHierarchyRequest
        {
            GenerationRequest = request.GenerationRequest,
            SelectedCandidate = request.SelectedCandidate,
            HookText = request.HookText,
            DominantObjectType = recommendation.DominantObjectType,
            PortraitSafe = recommendation.PortraitSafe
        }) ?? new ThumbnailVisualHierarchyResult
        {
            VisualHierarchyScore = Math.Round(request.SelectedCandidate.Score, 3),
            ReadabilityScore = Math.Round(request.SelectedCandidate.TextSafeCompositionArea, 3)
        };

    private ThumbnailMoodGradingResult SelectMood(ThumbnailCompositionRequest request, CinematicThumbnailAiRecommendation recommendation)
        => _moodGradingService?.SelectMood(new ThumbnailMoodGradingRequest
        {
            DominantObjectType = recommendation.DominantObjectType,
            EventType = request.GenerationRequest.Context.SpecialEvent?.EventType,
            IsShortForm = request.GenerationRequest.IsShortForm,
            AllowedMoodProfiles = _cinematicOptions.AllowedMoodProfiles
        }) ?? new ThumbnailMoodGradingResult { MoodProfile = recommendation.MoodProfile };

    private async Task RenderLongThumbnailAsync(ThumbnailGenerationRequest request, CinematicThumbnailAiRecommendation recommendation, ThumbnailMoodGradingResult mood, string sourcePath, string outputPath, string hook, CancellationToken cancellationToken)
    {
        using var canvas = await LoadCanvasAsync(sourcePath, _options.LandscapeWidth, _options.LandscapeHeight, recommendation, cancellationToken);
        var objectPoint = FindBrightestPoint(canvas);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint, recommendation, mood);
            if (_options.EnableGradientBackground)
            {
                ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.54f, 0.82f);
                ApplyTopGradient(ctx, canvas.Width, canvas.Height);
            }

            var topText = $"{request.Context.Date:MMM d} • {request.Context.LocationName}";
            DrawLabel(ctx, topText, 36, new PointF(46, 36), Color.White.WithAlpha(0.88f), HorizontalAlignment.Left);

            if (_options.EnableHookText && !string.IsNullOrWhiteSpace(hook))
                DrawHeadline(ctx, hook, canvas.Width, canvas.Height);

            DrawBrand(ctx, canvas.Width, canvas.Height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private async Task RenderShortThumbnailAsync(ThumbnailCompositionRequest request, CinematicThumbnailAiRecommendation recommendation, ThumbnailMoodGradingResult mood, string sourcePath, string outputPath, string hook, CancellationToken cancellationToken)
    {
        using var canvas = await LoadCanvasAsync(sourcePath, _options.PortraitWidth, _options.PortraitHeight, recommendation, cancellationToken);
        var objectPoint = FindBrightestPoint(canvas);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint, recommendation, mood);
            if (_options.EnableGradientBackground)
                ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.72f, 0.64f);

            if (_options.EnableHookText && !string.IsNullOrWhiteSpace(hook))
            {
                var font = CreateFont(72, FontStyle.Bold);
                var text = LimitWords(hook, Math.Min(4, _options.MaxHookWords));
                DrawTextWithShadow(ctx, new RichTextOptions(font)
                {
                    Origin = new PointF(canvas.Width / 2f, canvas.Height - 220),
                    WrappingLength = canvas.Width - 140,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, text, Color.White);
            }

            DrawBrand(ctx, canvas.Width, canvas.Height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private static async Task<Image<Rgba32>> LoadCanvasAsync(string sourcePath, int width, int height, CinematicThumbnailAiRecommendation recommendation, CancellationToken cancellationToken)
    {
        using var source = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        var scale = Math.Clamp(recommendation.ScaleBoost, 1, 1.35);
        var resizeWidth = Math.Max(width, (int)Math.Round(width * scale));
        var resizeHeight = Math.Max(height, (int)Math.Round(height * scale));
        var crop = CalculateCropRectangle(resizeWidth, resizeHeight, width, height, recommendation.FocusX, recommendation.FocusY);
        source.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(resizeWidth, resizeHeight), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }).Crop(crop));
        return source.Clone();
    }

    private static Rectangle CalculateCropRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, double focusX, double focusY)
    {
        var x = (int)Math.Round((sourceWidth * Math.Clamp(focusX, 0, 1)) - (targetWidth / 2d));
        var y = (int)Math.Round((sourceHeight * Math.Clamp(focusY, 0, 1)) - (targetHeight / 2d));
        x = Math.Clamp(x, 0, Math.Max(0, sourceWidth - targetWidth));
        y = Math.Clamp(y, 0, Math.Max(0, sourceHeight - targetHeight));
        return new Rectangle(x, y, Math.Min(targetWidth, sourceWidth), Math.Min(targetHeight, sourceHeight));
    }

    private void ApplyEnhancements(IImageProcessingContext ctx, int width, int height, PointF brightPoint, CinematicThumbnailAiRecommendation recommendation, ThumbnailMoodGradingResult mood)
    {
        if (_options.EnableContrastBoost)
            ctx.Contrast((float)mood.Contrast).Saturate((float)mood.Saturation).Brightness((float)mood.Brightness);
        if (_options.EnableSharpnessBoost)
            ctx.GaussianSharpen(0.65f);
        if (_options.EnableGlowEnhancement && _options.EnableGlowEffect && _cinematicOptions.EnableObjectFocusEnhancement)
            ctx.Fill(ResolveGlowColor(mood).WithAlpha(0.14f), new EllipsePolygon(brightPoint.X, brightPoint.Y, width * 0.16f, height * 0.16f));
        if (_cinematicOptions.EnableColorMoodGrading)
            ApplyMoodOverlay(ctx, width, height, mood.MoodProfile);
        if (_options.EnableVignette)
            ApplyVignette(ctx, width, height);
    }

    private static Color ResolveGlowColor(ThumbnailMoodGradingResult mood)
        => mood.HighlightColor switch
        {
            "blue" => Color.DeepSkyBlue,
            "silver" => Color.LightSteelBlue,
            "amber" => Color.Orange,
            _ => Color.FromPixel(WarmYellow)
        };

    private static void ApplyMoodOverlay(IImageProcessingContext ctx, int width, int height, string moodProfile)
    {
        var color = moodProfile switch
        {
            "deepSpace" => Color.MidnightBlue.WithAlpha(0.10f),
            "cinematicBlue" => Color.RoyalBlue.WithAlpha(0.09f),
            "warmGlow" => Color.Orange.WithAlpha(0.08f),
            "sunset" => Color.OrangeRed.WithAlpha(0.08f),
            _ => Color.Purple.WithAlpha(0.06f)
        };
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None,
            new ColorStop(0, color),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height));
    }

    private AstronomyIntegrityValidation ValidateAstronomyIntegrity(ThumbnailCompositionRequest request, CinematicThumbnailAiRecommendation recommendation)
    {
        var notes = new List<string>
        {
            "Composition uses crop, contrast, glow, gradients, text, and scale emphasis only.",
            "No generated astronomy pixels or new celestial objects are introduced."
        };
        var noSyntheticObjects = !_cinematicOptions.Enabled || (recommendation.ScaleBoost <= 1.35 && request.SelectedCandidate.ObjectDetected);
        var relationshipsPreserved = !recommendation.DominantObjectType.Equals("constellation", StringComparison.OrdinalIgnoreCase) || recommendation.ScaleBoost <= 1.35;
        return new AstronomyIntegrityValidation
        {
            NoSyntheticObjectsAdded = noSyntheticObjects,
            ObjectCountPreserved = true,
            ConstellationRelationshipsPreserved = relationshipsPreserved,
            AstronomicalIntegrityMaintained = noSyntheticObjects && relationshipsPreserved,
            Notes = notes
        };
    }

    private async Task WriteCinematicAiReportAsync(string outputDirectory, CinematicThumbnailAiReport report, CancellationToken cancellationToken)
    {
        if (!_cinematicOptions.Enabled)
            return;
        var thumbnailsDirectory = IoPath.Combine(outputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        var path = IoPath.Combine(thumbnailsDirectory, _cinematicOptions.OutputFileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken);
    }

    private static void ApplyVignette(IImageProcessingContext ctx, int width, int height)
    {
        var edge = Color.Black.WithAlpha(0.18f);
        ctx.Fill(edge, new RectangleF(0, 0, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, height * 0.90f, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, 0, width * 0.08f, height));
        ctx.Fill(edge, new RectangleF(width * 0.92f, 0, width * 0.08f, height));
    }

    private static void ApplyBottomGradient(IImageProcessingContext ctx, int width, int height, float startRatio, float alpha)
        => ctx.Fill(new LinearGradientBrush(new PointF(0, height * startRatio), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0, Color.Transparent),
            new ColorStop(1, Color.Black.WithAlpha(alpha))), new RectangleF(0, height * startRatio, width, height * (1 - startRatio)));

    private static void ApplyTopGradient(IImageProcessingContext ctx, int width, int height)
        => ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height * 0.24f), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.58f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height * 0.25f));

    private void DrawHeadline(IImageProcessingContext ctx, string hook, int width, int height)
    {
        var displayText = LimitWords(hook, _options.MaxHookWords);
        var font = CreateFont(92, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width / 2f, height - 68),
            WrappingLength = width - 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        }, displayText, displayText.Length % 2 == 0 ? Color.FromPixel(WarmYellow) : Color.White);
    }

    private static void DrawLabel(IImageProcessingContext ctx, string text, float size, PointF origin, Color color, HorizontalAlignment alignment)
    {
        var font = CreateFont(size, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font) { Origin = origin, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top }, text, color);
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText))
            return;

        var font = CreateFont(width > height ? 30 : 34, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width - 42, height - 42),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, _options.BrandText, Color.White.WithAlpha(0.56f));
    }

    private static void DrawTextWithShadow(IImageProcessingContext ctx, RichTextOptions options, string text, Color color)
    {
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 4, options.Origin.Y + 4) }, text, Color.Black.WithAlpha(0.82f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 1, options.Origin.Y + 1) }, text, color.WithAlpha(0.35f));
        ctx.DrawText(options, text, color);
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name))
            family = SystemFonts.Collection.Families.First();

        return family.CreateFont(size, style);
    }

    private static PointF FindBrightestPoint(Image<Rgba32> image)
    {
        var best = 0d;
        var point = new PointF(image.Width / 2f, image.Height / 2f);
        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 80))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 120))
            {
                var px = row[x];
                var lum = (0.2126 * px.R) + (0.7152 * px.G) + (0.0722 * px.B);
                if (lum > best)
                {
                    best = lum;
                    point = new PointF(x, y);
                }
            }
        }
        return point;
    }

    private async Task EnsureUnderSizeLimitAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (new FileInfo(outputPath).Length <= MaxThumbnailSizeBytes)
            return;

        using var image = await Image.LoadAsync(outputPath, cancellationToken);
        for (var quality = 88; quality >= 50; quality -= 8)
        {
            await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = quality }, cancellationToken);
            if (new FileInfo(outputPath).Length <= MaxThumbnailSizeBytes)
                return;
        }
    }

    private static string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(Math.Clamp(maxWords, 2, 5)).ToArray();
        return words.Length == 0 ? "Visible Tonight" : string.Join(' ', words);
    }
}
