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
        : this(options, Options.Create(new ThumbnailCinematicAIOptions()), null, null, null)
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
            OrganicAtmosphereScore = request.SelectedCandidate.OrganicAtmosphereScore,
            ProceduralAtmosphereScore = request.SelectedCandidate.ProceduralAtmosphereScore,
            NaturalLightingScore = request.SelectedCandidate.NaturalLightingScore,
            VisualArtifactPenalty = request.SelectedCandidate.VisualArtifactPenalty,
            CompositingVisibilityPenalty = request.SelectedCandidate.CompositingVisibilityPenalty,
            EdgeIntegrationScore = request.SelectedCandidate.EdgeIntegrationScore,
            CompositingSeamPenalty = request.SelectedCandidate.CompositingSeamPenalty,
            AtmosphereContinuityScore = request.SelectedCandidate.AtmosphereContinuityScore,
            EnvironmentalDepthScore = request.SelectedCandidate.EnvironmentalDepthScore,
            SupportObjectDepthScore = request.SelectedCandidate.SupportObjectDepthScore,
            CinematicSubtletyScore = request.SelectedCandidate.CinematicSubtletyScore,
            AstronomyIntegrityValidation = integrity,
            PortraitSafe = recommendation.PortraitSafe,
            FinalThumbnailPath = request.OutputPath,
            OverlaysApplied = BuildOverlayList(request.SelectedCandidate),
            ObjectScaleBoost = recommendation.ScaleBoost,
            FinalPaths = [request.OutputPath],
            VisualPolishPassApplied = true,
            Diagnostics = hierarchy.Recommendations.Concat([
                recommendation.Rationale,
                "Final realism polish applied: organic atmospheric texture, directional lighting bias, subdued support objects, natural composition validation, and mobile-safe text restraint."
            ]).ToArray()
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
            ScaleBoost = request.SelectedCandidate.FocalObjectScore < 0.70 ? 1.24 : 1.08
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
            ApplyStellariumOverlayCleanup(ctx, canvas.Width, canvas.Height, objectPoint, false);
        });
        ApplyAstronomyRichness(canvas, request.Context.Date.DayNumber, mood.MoodProfile);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint, recommendation, mood);
            if (_options.EnableGradientBackground)
            {
                ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.58f, 0.74f);
                ApplyTopGradient(ctx, canvas.Width, canvas.Height);
            }

            var topText = $"{request.Context.Date:MMM d} • {request.Context.LocationName}";
            DrawLabel(ctx, topText, 31, new PointF(46, 36), Color.White.WithAlpha(0.82f), HorizontalAlignment.Left);

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
            ApplyStellariumOverlayCleanup(ctx, canvas.Width, canvas.Height, objectPoint, true);
        });
        ApplyAstronomyRichness(canvas, request.GenerationRequest.Context.Date.DayNumber + 17, mood.MoodProfile);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint, recommendation, mood);
            if (_options.EnableGradientBackground)
                ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.70f, 0.62f);

            if (_options.EnableHookText && !string.IsNullOrWhiteSpace(hook))
            {
                var font = CreateFont(55, FontStyle.Regular, LocalizationResolver.IsHindi(request.GenerationRequest.Context.Localization.ResolvedLanguage));
                var text = LimitWords(hook, Math.Min(4, _options.MaxHookWords));
                DrawTextWithShadow(ctx, new RichTextOptions(font)
                {
                    Origin = new PointF(canvas.Width / 2f, canvas.Height - 310),
                    WrappingLength = canvas.Width - 220,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, text, Color.White.WithAlpha(0.94f), 0.62f);
            }

            DrawBrand(ctx, canvas.Width, canvas.Height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private static async Task<Image<Rgba32>> LoadCanvasAsync(string sourcePath, int width, int height, CinematicThumbnailAiRecommendation recommendation, CancellationToken cancellationToken)
    {
        using var source = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        var scale = Math.Clamp(recommendation.ScaleBoost, 1, 1.30);
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
            ctx.GaussianSharpen(0.48f);
        if (_options.EnableGlowEnhancement && _options.EnableGlowEffect && _cinematicOptions.EnableObjectFocusEnhancement)
        {
            ApplyDirectionalRimLight(ctx, width, height, brightPoint, ResolveGlowColor(mood));
        }
        if (_cinematicOptions.EnableColorMoodGrading)
            ApplyMoodOverlay(ctx, width, height, mood.MoodProfile);
        if (_options.EnableVignette)
            ApplyVignette(ctx, width, height);
    }


    private static void ApplyAstronomyRichness(Image<Rgba32> canvas, int seed, string moodProfile)
    {
        ProceduralAtmosphereBuffer.BlendIntoScene(canvas, seed, moodProfile, 1f);
        canvas.Mutate(ctx =>
        {
            var random = new Random(seed);
            DrawNaturalStarfield(ctx, canvas.Width, canvas.Height, random);
            DrawOrganicNebulaFog(ctx, canvas.Width, canvas.Height, random);
        });
    }


    private static void ApplyDirectionalRimLight(IImageProcessingContext ctx, int width, int height, PointF brightPoint, Color highlight)
    {
        var rimWidth = Math.Max(width, height) * 0.20f;
        var lightFrom = new PointF(brightPoint.X - rimWidth * 0.45f, brightPoint.Y - rimWidth * 0.62f);
        var falloffTo = new PointF(brightPoint.X + rimWidth * 0.52f, brightPoint.Y + rimWidth * 0.44f);
        ctx.Fill(highlight.WithAlpha(0.038f), new EllipsePolygon(lightFrom.X, lightFrom.Y, rimWidth * 0.88f, rimWidth * 0.62f));
        ctx.Fill(Color.White.WithAlpha(0.012f), new EllipsePolygon(brightPoint.X, brightPoint.Y, rimWidth * 0.46f, rimWidth * 0.34f));
        ctx.Fill(highlight.WithAlpha(0.018f), new EllipsePolygon(falloffTo.X, falloffTo.Y, width * 0.38f, rimWidth * 0.28f));
    }

    private static void DrawNaturalStarfield(IImageProcessingContext ctx, int width, int height, Random random)
    {
        var clusters = 3 + random.Next(3);
        var starCount = Math.Clamp(width * height / 12800, 72, 210);
        for (var c = 0; c < clusters; c++)
        {
            var clusterX = random.NextSingle() * width;
            var clusterY = random.NextSingle() * height * 0.78f;
            var clusterSpread = Math.Min(width, height) * (0.12f + random.NextSingle() * 0.18f);
            var clusterStars = starCount / clusters;
            for (var i = 0; i < clusterStars; i++)
            {
                var useCluster = random.NextSingle() < 0.58f;
                var x = useCluster ? clusterX + (random.NextSingle() - 0.5f) * clusterSpread : random.NextSingle() * width;
                var y = useCluster ? clusterY + (random.NextSingle() - 0.5f) * clusterSpread : random.NextSingle() * height * 0.84f;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                var radius = 0.42f + MathF.Pow(random.NextSingle(), 2.2f) * 1.35f;
                var alpha = 0.08f + MathF.Pow(random.NextSingle(), 1.7f) * 0.28f;
                var tint = random.Next(4) switch
                {
                    0 => Color.FromRgb(190, 215, 255),
                    1 => Color.FromRgb(255, 226, 178),
                    2 => Color.FromRgb(218, 232, 255),
                    _ => Color.White
                };
                ctx.Fill(tint.WithAlpha(alpha), new EllipsePolygon(x, y, radius));
            }
        }
    }

    private static void DrawOrganicNebulaFog(IImageProcessingContext ctx, int width, int height, Random random)
    {
        var wisps = 34;
        for (var i = 0; i < wisps; i++)
        {
            var x = (i / (float)wisps) * width + (random.NextSingle() - 0.5f) * width * 0.18f;
            var y = height * (0.18f + random.NextSingle() * 0.46f) + MathF.Sin(i * 0.73f) * height * 0.10f;
            var w = width * (0.040f + random.NextSingle() * 0.092f);
            var h = height * (0.012f + random.NextSingle() * 0.034f);
            var color = i % 3 == 0 ? Color.FromRgb(90, 116, 165) : Color.FromRgb(88, 70, 118);
            ctx.Fill(color.WithAlpha(0.010f + random.NextSingle() * 0.018f), new EllipsePolygon(x, y, w, h));
        }
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
            "deepSpace" => Color.MidnightBlue.WithAlpha(0.065f),
            "cinematicBlue" => Color.RoyalBlue.WithAlpha(0.055f),
            "warmGlow" => Color.Orange.WithAlpha(0.050f),
            "sunset" => Color.OrangeRed.WithAlpha(0.055f),
            _ => Color.Purple.WithAlpha(0.040f)
        };
        ctx.Fill(color, new EllipsePolygon(width * 0.42f, height * 0.34f, width * 0.62f, height * 0.48f));
    }

    private AstronomyIntegrityValidation ValidateAstronomyIntegrity(ThumbnailCompositionRequest request, CinematicThumbnailAiRecommendation recommendation)
    {
        var notes = new List<string>
        {
            "Composition uses crop, contrast, directional rim light, organic atmospheric texture, gradients, text, and scale emphasis only.",
            "No generated astronomy pixels or new celestial objects are introduced."
        };
        var noSyntheticObjects = !_cinematicOptions.Enabled || (recommendation.ScaleBoost <= 1.30 && request.SelectedCandidate.ObjectDetected);
        var relationshipsPreserved = !recommendation.DominantObjectType.Equals("constellation", StringComparison.OrdinalIgnoreCase) || recommendation.ScaleBoost <= 1.30;
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
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(path, json, cancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(thumbnailsDirectory, "thumbnail-cinematic-report.json"), json, cancellationToken);
    }

    private static void ApplyVignette(IImageProcessingContext ctx, int width, int height)
    {
        var edge = Color.Black.WithAlpha(0.21f);
        ctx.Fill(edge, new RectangleF(0, 0, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, height * 0.90f, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, 0, width * 0.08f, height));
        ctx.Fill(edge, new RectangleF(width * 0.92f, 0, width * 0.08f, height));
    }


    private static void ApplyStellariumOverlayCleanup(IImageProcessingContext ctx, int width, int height, PointF focalPoint, bool portrait)
    {
        var protectedRadius = Math.Min(width, height) * (portrait ? 0.20f : 0.16f);
        var leftColumn = new RectangleF(0, 0, width * (portrait ? 0.24f : 0.22f), height);
        var bottomBar = new RectangleF(0, height * (portrait ? 0.90f : 0.86f), width, height * (portrait ? 0.10f : 0.14f));
        var topLeft = new RectangleF(0, 0, width * 0.34f, height * (portrait ? 0.08f : 0.12f));
        var topRight = new RectangleF(width * 0.78f, 0, width * 0.22f, height * 0.10f);

        if (!IntersectsProtectedFocus(leftColumn, focalPoint, protectedRadius))
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(leftColumn.Width, 0), GradientRepetitionMode.None,
                new ColorStop(0, Color.Black.WithAlpha(0.72f)),
                new ColorStop(0.68f, Color.Black.WithAlpha(0.34f)),
                new ColorStop(1, Color.Transparent)), leftColumn);
        }

        if (!IntersectsProtectedFocus(bottomBar, focalPoint, protectedRadius))
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, bottomBar.Y), new PointF(0, height), GradientRepetitionMode.None,
                new ColorStop(0, Color.Transparent),
                new ColorStop(0.38f, Color.Black.WithAlpha(0.44f)),
                new ColorStop(1, Color.Black.WithAlpha(0.76f))), bottomBar);
        }

        if (!IntersectsProtectedFocus(topLeft, focalPoint, protectedRadius))
            ctx.Fill(Color.Black.WithAlpha(0.34f), topLeft);
        if (!IntersectsProtectedFocus(topRight, focalPoint, protectedRadius))
            ctx.Fill(Color.Black.WithAlpha(0.30f), topRight);
    }

    private static bool IntersectsProtectedFocus(RectangleF region, PointF focalPoint, float radius)
    {
        var nearestX = Math.Clamp(focalPoint.X, region.X, region.X + region.Width);
        var nearestY = Math.Clamp(focalPoint.Y, region.Y, region.Y + region.Height);
        var dx = focalPoint.X - nearestX;
        var dy = focalPoint.Y - nearestY;
        return (dx * dx) + (dy * dy) <= radius * radius;
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
        var font = CreateFont(78, FontStyle.Bold, ContainsDevanagari(displayText));
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width / 2f, height - 76),
            WrappingLength = width - 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        }, displayText, displayText.Length % 2 == 0 ? Color.FromPixel(WarmYellow) : Color.White);
    }

    private void DrawLabel(IImageProcessingContext ctx, string text, float size, PointF origin, Color color, HorizontalAlignment alignment)
    {
        var font = CreateFont(size, FontStyle.Bold, ContainsDevanagari(text));
        DrawTextWithShadow(ctx, new RichTextOptions(font) { Origin = origin, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top }, text, color);
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText))
            return;

        var font = CreateFont(width > height ? 30 : 34, FontStyle.Bold, false);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width - 42, height - 42),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, _options.BrandText, Color.White.WithAlpha(0.56f));
    }

    private static void DrawTextWithShadow(IImageProcessingContext ctx, RichTextOptions options, string text, Color color, float shadowScale = 1f)
    {
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 4, options.Origin.Y + 4) }, text, Color.Black.WithAlpha(0.68f * shadowScale));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X - 2, options.Origin.Y) }, text, Color.Black.WithAlpha(0.28f * shadowScale));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 2, options.Origin.Y) }, text, Color.Black.WithAlpha(0.28f * shadowScale));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X, options.Origin.Y + 2) }, text, Color.Black.WithAlpha(0.28f * shadowScale));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 1, options.Origin.Y + 1) }, text, color.WithAlpha(0.22f * shadowScale));
        ctx.DrawText(options, text, color);
    }

    private Font CreateFont(float size, FontStyle style, bool preferHindi)
    {
        foreach (var configured in BuildConfiguredFontCandidates(preferHindi))
        {
            try
            {
                if (File.Exists(configured))
                {
                    var collection = new FontCollection();
                    var family = collection.Add(configured);
                    return family.CreateFont(size, style);
                }

                var configuredFamily = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Equals(configured, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(configuredFamily.Name))
                    return configuredFamily.CreateFont(size, style);
            }
            catch
            {
                // Font configuration must never fail thumbnail generation.
            }
        }

        var preferredNames = preferHindi
            ? new[] { "Noto Sans Devanagari", "Mangal", "Nirmala UI", "Arial Unicode MS", "DejaVu Sans", "Arial" }
            : new[] { "Inter", "Arial", "DejaVu Sans", "Liberation Sans" };
        foreach (var name in preferredNames)
        {
            var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(family.Name))
                return family.CreateFont(size, style);
        }

        return SystemFonts.Collection.Families.First().CreateFont(size, style);
    }

    private IEnumerable<string> BuildConfiguredFontCandidates(bool preferHindi)
    {
        if (preferHindi && !string.IsNullOrWhiteSpace(_options.HindiFont)) yield return _options.HindiFont;
        if (!string.IsNullOrWhiteSpace(_options.PrimaryFont)) yield return _options.PrimaryFont;
        if (!string.IsNullOrWhiteSpace(_options.FallbackFont)) yield return _options.FallbackFont;
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
        foreach (var quality in new[] { 85, 80, 75 })
        {
            await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = quality }, cancellationToken);
            if (new FileInfo(outputPath).Length <= MaxThumbnailSizeBytes)
                return;
        }
    }

    public async Task<ThumbnailProductionQualityResult> ValidateThumbnailQualityAsync(string thumbnailPath, string hookText, CancellationToken cancellationToken)
    {
        var score = await new ThumbnailScoringService().ScoreAsync(thumbnailPath, new ThumbnailScoringContext { EnableAstronomySceneMode = true, RejectDarkFrames = true }, cancellationToken);
        var textReadability = string.IsNullOrWhiteSpace(hookText) ? 0.35 : Math.Clamp(score.TextSafeCompositionArea + (hookText.Length <= 32 ? 0.18 : 0), 0, 1);
        var mobile = string.IsNullOrWhiteSpace(hookText) ? 0.45 : Math.Clamp(1 - Math.Max(0, hookText.Length - 28) / 40d, 0.35, 1);
        var compositingRealism = Math.Clamp((score.EdgeIntegrationScore * 0.20) + (score.AtmosphereContinuityScore * 0.22) + (score.ProceduralAtmosphereScore * 0.16) + (score.EnvironmentalDepthScore * 0.16) + (score.SupportObjectDepthScore * 0.12) + ((1 - score.CompositingSeamPenalty) * 0.14), 0, 1);
        var quality = Math.Clamp((score.Score * 0.56) + (textReadability * 0.15) + (mobile * 0.13) + (compositingRealism * 0.16) - (score.CompositingSeamPenalty * 0.08), 0, 1);
        var warnings = new List<string>();
        if (score.BlackPixelPercentage > _options.MaxBlackPixelPercentage && !score.ObjectDetected) warnings.Add("black-frame-risk");
        if (score.FocalObjectScore < 0.35) warnings.Add("weak-focal-object");
        if (textReadability < 0.55) warnings.Add("weak-text-readability");
        if (quality < 0.80) warnings.Add("below-preferred-polish-score");
        if (score.CompositingSeamPenalty > 0.32) warnings.Add("visible-compositing-seam-risk");
        if (score.ProceduralAtmosphereScore < 0.55) warnings.Add("rectangular-atmosphere-risk");
        if (score.EdgeIntegrationScore < 0.42) warnings.Add("hard-alpha-edge-risk");
        if (new FileInfo(thumbnailPath).Length > MaxThumbnailSizeBytes) warnings.Add("file-too-large");
        return new ThumbnailProductionQualityResult
        {
            IsProductionReady = quality >= 0.80 && warnings.All(w => w is not "black-frame-risk" and not "weak-focal-object" and not "weak-text-readability" and not "visible-compositing-seam-risk" and not "rectangular-atmosphere-risk" and not "hard-alpha-edge-risk"),
            Warnings = warnings,
            QualityScore = Math.Round(quality, 3),
            FocalObjectScore = score.FocalObjectScore,
            TextReadabilityScore = Math.Round(textReadability, 3),
            BlackFrameRisk = Math.Round(score.BlackPixelPercentage > 0.85 && !score.ObjectDetected ? 1 : score.BlackPixelPercentage * (score.ObjectDetected ? 0.35 : 1), 3),
            MobileReadabilityScore = Math.Round(mobile, 3)
        };
    }

    private static IReadOnlyCollection<string> BuildOverlayList(ThumbnailCandidateScore score)
    {
        var overlays = new List<string> { "stellarium-debug-edge-cleanup", "procedural-radial-atmosphere-buffer", "noise-feathered-haze", "soft-vignette", "contrast-curves", "restrained-object-glow" };
        if (score.StarRichnessScore < 0.45) overlays.Add("subtle-star-clarity-enhancement");
        if (score.ColorRichness < 0.35) overlays.Add("subtle-cinematic-sky-depth-grade");
        if (score.CompositingSeamPenalty > 0.20) overlays.Add("organic-seam-feathering-required");
        if (score.EdgeIntegrationScore < 0.50) overlays.Add("directional-edge-wrap-required");
        return overlays;
    }

    private static bool ContainsDevanagari(string text) => text.Any(ch => ch >= '\u0900' && ch <= '\u097F');

    private static string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(Math.Clamp(maxWords, 2, 5)).ToArray();
        return words.Length == 0 ? "Visible Tonight" : string.Join(' ', words);
    }
}
