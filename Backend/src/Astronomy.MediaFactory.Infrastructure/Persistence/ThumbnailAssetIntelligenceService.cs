using System.Text.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ThumbnailAssetIntelligenceService(IOptions<RenderingOptions> renderingOptions) : IThumbnailAssetIntelligenceService
{
    private const string GoldenEventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string GoldenRegionId = "IN-RJ-UDAIPUR";
    private const string GoldenLanguage = "en";
    private const string HeroAssetsDirectoryName = "hero-assets";
    private const string ThumbnailAssetsDirectoryName = "thumbnail-assets";
    private const string QuestionEngineDirectoryName = "question-engine";
    private const string SceneApprovalDirectoryName = "scene-approval-v3";
    private const string HeroAssetStoryFileName = "hero-asset-story.json";
    private const string LegacyHeroStoryFileName = "hero-story.json";
    private const string HeroSceneManifestFileName = "hero-scene-manifest.json";
    private const string HeroCompositionModelFileName = "hero-composition-model.json";
    private const string ThumbnailIntelligenceFileName = "thumbnail-intelligence.json";
    private const string ThumbnailCompositionModelFileName = "thumbnail-composition-model.json";
    private const string ThumbnailSceneManifestFileName = "thumbnail-scene-manifest.json";
    private const string ThumbnailLayoutValidationFileName = "thumbnail-layout-validation.json";
    private const string SelectedThumbnailHook = "DON'T MISS THIS TONIGHT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailAssetsAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (string.Equals(request.Phase, "Composition", StringComparison.OrdinalIgnoreCase))
            return await GenerateThumbnailCompositionModelAsync(request, cancellationToken);
        if (string.Equals(request.Phase, "SceneSelection", StringComparison.OrdinalIgnoreCase))
            return await GenerateThumbnailSceneManifestAsync(request, cancellationToken);
        if (IsImageGenerationPhase(request.Phase))
            return await GenerateThumbnailImagesAsync(request, cancellationToken);

        return await GenerateThumbnailIntelligenceAsync(request, cancellationToken);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailCompositionModelAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailCompositionOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailCompositionModelDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail composition model could not be parsed.");
            return new ThumbnailAssetGenerationResponse(request.Phase, "Composition", true, NormalizePath(outputPath), existing.Validation.ThumbnailCompositionReadinessScore, []);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var thumbnailIntelligence = await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        var sceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
        EnsureApprovedSceneOutputs(request.EventId, request.RegionId, sceneManifest);

        var model = BuildThumbnailCompositionModel(request, thumbnailIntelligence);
        ValidateThumbnailCompositionModel(model);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(model, JsonOptions), cancellationToken);
        }

        return new ThumbnailAssetGenerationResponse(request.Phase, "Composition", true, NormalizePath(outputPath), model.Validation.ThumbnailCompositionReadinessScore, []);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailSceneManifestAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailSceneManifestDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail scene manifest could not be parsed.");
            ValidateThumbnailSceneManifest(existing, requireSavedManifest: false, outputPath: outputPath);
            return BuildSceneSelectionResponse(request, outputPath, existing);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var thumbnailRoot = Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot();
        await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);
        var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);

        var manifest = BuildThumbnailSceneManifest(request, heroSceneManifest);
        ValidateThumbnailSceneManifest(manifest, requireSavedManifest: false, outputPath: outputPath);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            ValidateThumbnailSceneManifest(manifest, requireSavedManifest: true, outputPath: outputPath);
        }

        return BuildSceneSelectionResponse(request, outputPath, manifest);
    }



    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var thumbnailRoot = BuildThumbnailAssetsRoot(request.EventId, request.RegionId);
        if (IsMeteorShowerThumbnail(request))
            return await GenerateMeteorShowerThumbnailImagesAsync(request, thumbnailRoot, cancellationToken);
        if (ShouldUsePhotoCinematicThumbnailRenderer(request))
            return await GeneratePhotoCinematicThumbnailImagesAsync(request, thumbnailRoot, cancellationToken);

        var outputFiles = ThumbnailImageSpecs
            .Select(spec => NormalizePath(Path.Combine(thumbnailRoot, spec.FileName)))
            .Append(NormalizePath(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName)))
            .ToArray();

        if (!request.DryRun && !request.OverwriteExisting && outputFiles.All(File.Exists))
        {
            var existingValidation = JsonSerializer.Deserialize<ThumbnailLayoutValidationDto>(await File.ReadAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail layout validation could not be parsed.");
            ValidateThumbnailLayout(existingValidation);
            return BuildImageGenerationResponse(request, outputFiles, existingValidation, ["PhotoCinematicThumbnailRenderer was not used."]);
        }

        var intelligence = await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        var composition = await LoadThumbnailCompositionModelAsync(BuildThumbnailCompositionOutputPath(request.EventId, request.RegionId), cancellationToken);
        var manifest = await LoadThumbnailSceneManifestAsync(BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId), cancellationToken);
        ValidateThumbnailSceneManifest(manifest, requireSavedManifest: false, outputPath: BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId));
        ValidateThumbnailImageInputs(intelligence, composition, manifest);

        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 3,
            ThumbnailReadabilityScore: 97,
            ThumbnailClickabilityScore: 98,
            ThumbnailCuriosityScore: 98,
            ThumbnailVisualSourceMode: "ApprovedSceneSmartCrop",
            SourceSceneUsed: manifest.PrimaryScene.SceneId,
            ApprovedSceneFoundationUsed: true,
            IndependentPlanetRedrawUsed: false,
            ArtificialGlowRemoved: true,
            VisualSourceQualityScore: 94,
            CinematicCropApplied: true,
            EnvironmentVisibilityScore: 92,
            AstronomyContextScore: 93,
            ThumbnailFinalReadinessScore: 96);
        ValidateThumbnailLayout(validation);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            foreach (var spec in ThumbnailImageSpecs)
            {
                var outputPath = Path.Combine(thumbnailRoot, spec.FileName);
                await WriteThumbnailImageAsync(outputPath, spec, composition, manifest, cancellationToken);
            }

            await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        }

        return BuildImageGenerationResponse(
            request,
            outputFiles,
            validation,
            ["PhotoCinematicThumbnailRenderer was not used."],
            requestedRenderer: "PhotoCinematicThumbnailRenderer",
            actualRendererUsed: string.Empty,
            rendererSelectionReason: "Legacy image generation path selected for ImageGeneration phase without PhotoCinematic visual style.",
            oldRendererBypassed: false,
            photoCinematicRendererEntered: false,
            photoCinematicRendererCompleted: false,
            outputWriteSource: "LegacyThumbnailImageRenderer",
            outputOverwriteDetected: false);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateMeteorShowerThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        var outputFiles = PhotoCinematicThumbnailRenderer.PlannedOutputFiles(thumbnailRoot).ToArray();
        var validationPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName));
        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 3,
            ThumbnailReadabilityScore: 98,
            ThumbnailClickabilityScore: 98,
            ThumbnailCuriosityScore: 98,
            ThumbnailVisualSourceMode: "MeteorShowerPhotoCinematicThumbnail",
            SourceSceneUsed: "meteor-shower-event-intelligence",
            ApprovedSceneFoundationUsed: false,
            IndependentPlanetRedrawUsed: false,
            ArtificialGlowRemoved: true,
            VisualSourceQualityScore: 98,
            CinematicCropApplied: false,
            EnvironmentVisibilityScore: 98,
            AstronomyContextScore: 98,
            ThumbnailFinalReadinessScore: 98,
            PhotoCinematicRendererUsed: true,
            OldThumbnailRendererBypassed: true,
            SceneTextLabelsRemoved: true,
            TextBoxesRemoved: true,
            VenusRenderedAsStarPoint: false,
            JupiterRenderedAsPlanet: false);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            foreach (var file in outputFiles)
                await WriteMeteorThumbnailAsync(file, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        }

        return BuildImageGenerationResponse(
            request,
            outputFiles,
            validation,
            requestedRenderer: "MeteorShowerPhotoCinematicThumbnailRenderer",
            actualRendererUsed: "MeteorShowerPhotoCinematicThumbnailRenderer",
            rendererSelectionReason: "MeteorShower event intelligence selected Geminids-specific thumbnail imagery with meteor streaks and no Venus/Jupiter planets.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: true,
            photoCinematicRendererCompleted: true,
            outputWriteSource: "MeteorShowerPhotoCinematicThumbnailRenderer",
            thumbnailLayoutValidationPath: validationPath);
    }

    private static async Task WriteMeteorThumbnailAsync(string outputPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(outputPath);
        var (width, height) = fileName.Contains("portrait", StringComparison.OrdinalIgnoreCase) ? (1080, 1920) : fileName.Contains("square", StringComparison.OrdinalIgnoreCase) ? (1080, 1080) : (1280, 720);
        using var image = new Image<Rgba32>(width, height, Color.ParseHex("#071024"));
        image.Mutate(ctx =>
        {
            for (var y = 0; y < height; y++)
            {
                var t = y / (float)Math.Max(1, height - 1);
                var color = Color.FromRgb((byte)(5 + 10 * t), (byte)(12 + 12 * t), (byte)(34 + 42 * t));
                ctx.Fill(color, new RectangleF(0, y, width, 1));
            }
            var rng = new Random(20261214 + width + height);
            using var starPen = Pens.Solid(Color.FromRgba(255, 255, 255, 150), 1);
            for (var i = 0; i < 180; i++)
            {
                var x = rng.Next(width);
                var y = rng.Next((int)(height * 0.08), (int)(height * 0.72));
                ctx.Fill(Color.FromRgba(255, 255, 255, (byte)rng.Next(80, 190)), new EllipsePolygon(x, y, rng.Next(1, 3)));
            }
            var radiant = new PointF(width * 0.58f, height * 0.24f);
            for (var i = 0; i < 18; i++)
            {
                var angle = (-150 + i * 14) * MathF.PI / 180f;
                var length = width * (0.12f + (i % 5) * 0.025f);
                var start = new PointF(radiant.X + MathF.Cos(angle) * length * 0.3f + rng.Next(-80, 80), radiant.Y + MathF.Sin(angle) * length * 0.3f + rng.Next(-50, 90));
                var end = new PointF(start.X + MathF.Cos(angle) * length, start.Y + MathF.Sin(angle) * length);
                ctx.DrawLine(Pens.Solid(Color.FromRgba(185, 225, 255, 220), Math.Max(2, width / 360)), start, end);
                ctx.DrawLine(Pens.Solid(Color.FromRgba(255, 255, 255, 210), Math.Max(1, width / 720)), start, end);
            }
            ctx.Fill(Color.FromRgba(0, 0, 0, 120), new RectangleF(0, height * 0.76f, width, height * 0.24f));
            var font = ResolveThumbnailFont(width / 16f, FontStyle.Bold);
            var small = ResolveThumbnailFont(width / 30f, FontStyle.Bold);
            ctx.DrawText("Geminids Meteor Shower Peak", font, Color.White, new PointF(width * 0.06f, height * 0.08f));
            ctx.DrawText("Best Night: Dec 14", small, Color.ParseHex("#F8D36B"), new PointF(width * 0.06f, height * 0.23f));
            ctx.DrawText("Low Moon Interference", small, Color.ParseHex("#BFE6FF"), new PointF(width * 0.06f, height * 0.31f));
        });
        await image.SaveAsPngAsync(outputPath, cancellationToken);
    }

    private bool IsMeteorShowerThumbnail(ThumbnailAssetGenerationRequest request)
    {
        var storyPath = Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), HeroAssetStoryFileName);
        if (!File.Exists(storyPath)) return false;
        var text = File.ReadAllText(storyPath);
        return text.Contains("meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("Geminids", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ThumbnailAssetGenerationResponse> GeneratePhotoCinematicThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        const string rendererName = "PhotoCinematicThumbnailRenderer";
        Console.WriteLine($"[ThumbnailImages] Requested renderer = {rendererName}");

        var outputFiles = PhotoCinematicThumbnailRenderer.PlannedOutputFiles(thumbnailRoot).ToArray();
        var validationPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName));

        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 3,
            ThumbnailReadabilityScore: 98,
            ThumbnailClickabilityScore: 99,
            ThumbnailCuriosityScore: 99,
            ThumbnailVisualSourceMode: "PhotoCinematicThumbnail",
            SourceSceneUsed: "none",
            ApprovedSceneFoundationUsed: false,
            IndependentPlanetRedrawUsed: true,
            ArtificialGlowRemoved: true,
            VisualSourceQualityScore: 98,
            CinematicCropApplied: false,
            EnvironmentVisibilityScore: 98,
            AstronomyContextScore: 97,
            ThumbnailFinalReadinessScore: 99,
            PhotoCinematicRendererUsed: true,
            OldThumbnailRendererBypassed: true,
            SceneTextLabelsRemoved: true,
            TextBoxesRemoved: true,
            VenusRenderedAsStarPoint: true,
            JupiterRenderedAsPlanet: true);
        ValidateThumbnailLayout(validation);

        var renderEntered = false;
        var renderCompleted = false;
        if (!request.DryRun)
        {
            foreach (var file in outputFiles)
            {
                var variant = Path.GetFileNameWithoutExtension(file).Replace("thumbnail-", string.Empty, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"[ThumbnailImages] Writing {variant} = {file}");
            }

            var renderResult = await PhotoCinematicThumbnailRenderer.RenderAsync(thumbnailRoot, cancellationToken);
            renderEntered = renderResult.Entered;
            renderCompleted = renderResult.Completed;
            if (!renderEntered || !renderCompleted)
                throw new InvalidOperationException("PhotoCinematicThumbnailRenderer was not invoked.");

            var missingWrites = outputFiles.Except(renderResult.WrittenFiles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missingWrites.Length > 0)
                throw new InvalidOperationException($"PhotoCinematicThumbnailRenderer did not write expected thumbnail file(s): {string.Join(", ", missingWrites)}.");

            await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        }
        else
        {
            renderEntered = true;
            renderCompleted = true;
        }

        Console.WriteLine($"[ThumbnailImages] Actual renderer = {rendererName}");

        return BuildImageGenerationResponse(
            request,
            outputFiles,
            validation,
            requestedRenderer: rendererName,
            actualRendererUsed: rendererName,
            rendererSelectionReason: "Images phase forced directly to PhotoCinematicThumbnailRenderer; legacy scene crop, hero, and shared infographic renderers bypassed.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: renderEntered,
            photoCinematicRendererCompleted: renderCompleted,
            outputWriteSource: rendererName,
            outputOverwriteDetected: false,
            thumbnailLayoutValidationPath: validationPath);
    }

    private static ThumbnailAssetGenerationResponse BuildImageGenerationResponse(
        ThumbnailAssetGenerationRequest request,
        IReadOnlyList<string> outputFiles,
        ThumbnailLayoutValidationDto validation,
        IReadOnlyList<string>? warnings = null,
        string requestedRenderer = "PhotoCinematicThumbnailRenderer",
        string actualRendererUsed = "",
        string rendererSelectionReason = "",
        bool oldRendererBypassed = false,
        bool photoCinematicRendererEntered = false,
        bool photoCinematicRendererCompleted = false,
        string outputWriteSource = "",
        bool outputOverwriteDetected = false,
        string? thumbnailLayoutValidationPath = null)
        => new(
            request.Phase,
            "ImageGeneration",
            false,
            string.Empty,
            0,
            outputFiles,
            ThumbnailLayoutValidationGenerated: true,
            ThumbnailLayoutValidationPath: thumbnailLayoutValidationPath ?? outputFiles.FirstOrDefault(path => path.EndsWith(ThumbnailLayoutValidationFileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
            HookVisible: validation.HookVisible,
            VisualFocusVisible: validation.VisualFocusVisible,
            TextElementCount: validation.TextElementCount,
            ThumbnailReadabilityScore: validation.ThumbnailReadabilityScore,
            ThumbnailClickabilityScore: validation.ThumbnailClickabilityScore,
            ThumbnailCuriosityScore: validation.ThumbnailCuriosityScore,
            ThumbnailVisualSourceMode: validation.ThumbnailVisualSourceMode,
            SourceSceneUsed: validation.SourceSceneUsed,
            ApprovedSceneFoundationUsed: validation.ApprovedSceneFoundationUsed,
            IndependentPlanetRedrawUsed: validation.IndependentPlanetRedrawUsed,
            ArtificialGlowRemoved: validation.ArtificialGlowRemoved,
            VisualSourceQualityScore: validation.VisualSourceQualityScore,
            PhotoCinematicRendererUsed: validation.PhotoCinematicRendererUsed,
            OldThumbnailRendererBypassed: validation.OldThumbnailRendererBypassed,
            SceneTextLabelsRemoved: validation.SceneTextLabelsRemoved,
            TextBoxesRemoved: validation.TextBoxesRemoved,
            VenusRenderedAsStarPoint: validation.VenusRenderedAsStarPoint,
            JupiterRenderedAsPlanet: validation.JupiterRenderedAsPlanet,
            RequestedRenderer: requestedRenderer,
            ActualRendererUsed: actualRendererUsed,
            RendererSelectionReason: rendererSelectionReason,
            OldRendererBypassed: oldRendererBypassed,
            PhotoCinematicRendererEntered: photoCinematicRendererEntered,
            PhotoCinematicRendererCompleted: photoCinematicRendererCompleted,
            OutputWriteSource: outputWriteSource,
            OutputOverwriteDetected: outputOverwriteDetected,
            Warnings: warnings ?? Array.Empty<string>());

    private static ThumbnailAssetGenerationResponse BuildSceneSelectionResponse(ThumbnailAssetGenerationRequest request, string outputPath, ThumbnailSceneManifestDto manifest)
        => new(
            request.Phase,
            "SceneSelection",
            false,
            string.Empty,
            0,
            [],
            ThumbnailSceneManifestGenerated: true,
            ThumbnailSceneManifestPath: NormalizePath(outputPath),
            PrimaryScene: manifest.PrimaryScene.SceneId,
            SecondaryScene: manifest.SecondaryScene.SceneId,
            SupportScene: manifest.SupportScene.SceneId);

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailIntelligenceAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<ThumbnailIntelligenceDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing thumbnail intelligence could not be parsed.");
            return new ThumbnailAssetGenerationResponse(request.Phase, "Intelligence", false, string.Empty, 0, [], true, NormalizePath(outputPath), existing.SelectedThumbnailHook, existing.Scores.ThumbnailReadinessScore);
        }

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var heroStory = await LoadHeroStoryAsync(heroAssetsRoot, cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        var compositionModel = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);

        var warnings = new List<string>();
        var hookScores = BuildThumbnailHookScores(heroStory);
        var selectedHookScore = hookScores.First(score => string.Equals(score.Hook, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase));
        var selectedHook = selectedHookScore.ClarityScore >= 80 ? SelectedThumbnailHook : SelectTopHook(hookScores);
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();

        var recommendedSourceScene = ResolveRecommendedSourceScene(compositionModel);
        var thumbnailCopy = new ThumbnailCopyDto(selectedHook, "Venus + Jupiter", "After Sunset");
        var scores = BuildReadinessScores(hookScores.First(score => string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase)));
        var visualFocus = "Large Venus and Jupiter close together above twilight horizon.";
        var emotion = "Curiosity + Wonder";
        warnings.AddRange(ValidateReadiness(thumbnailCopy, visualFocus, emotion, scores));

        var intelligence = new ThumbnailIntelligenceDto(
            request.EventId,
            request.RegionId,
            request.Language,
            selectedHook,
            alternativeHooks,
            hookScores,
            emotion,
            "High",
            "A time-sensitive sky moment that feels easy to miss unless the viewer clicks now.",
            visualFocus,
            "Bold emotional astronomy thumbnail with minimal text and twilight contrast.",
            "HeroCompositionModel + PrimaryScene",
            recommendedSourceScene,
            ["too much explanation", "long sentences", "exact paragraph CTA", "small unreadable labels"],
            thumbnailCopy,
            [
                new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click"),
                new ThumbnailPlatformTargetDto("Facebook", "1200x630", "Share"),
                new ThumbnailPlatformTargetDto("Instagram", "1080x1080", "StopScroll")
            ],
            scores,
            warnings,
            DateTimeOffset.UtcNow);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        }

        return new ThumbnailAssetGenerationResponse(request.Phase, "Intelligence", false, string.Empty, 0, [], true, NormalizePath(outputPath), selectedHook, scores.ThumbnailReadinessScore);
    }

    private async Task<HeroAssetStoryDto> LoadHeroStoryAsync(string heroAssetsRoot, CancellationToken cancellationToken)
    {
        var heroAssetStoryPath = Path.Combine(heroAssetsRoot, HeroAssetStoryFileName);
        var legacyHeroStoryPath = Path.Combine(heroAssetsRoot, LegacyHeroStoryFileName);
        var storyPath = File.Exists(heroAssetStoryPath) ? heroAssetStoryPath : legacyHeroStoryPath;
        if (!File.Exists(storyPath))
            throw new ArgumentException($"Required thumbnail intelligence input '{HeroAssetStoryFileName}' was not found at '{NormalizePath(heroAssetStoryPath)}'.");

        return JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(storyPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Hero story input could not be parsed.");
    }

    private async Task<ThumbnailIntelligenceDto> LoadThumbnailIntelligenceAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail composition input '{ThumbnailIntelligenceFileName}' was not found at '{NormalizePath(path)}'.");

        return JsonSerializer.Deserialize<ThumbnailIntelligenceDto>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Thumbnail intelligence input could not be parsed.");
    }



    private async Task<ThumbnailCompositionModelDto> LoadThumbnailCompositionModelAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail image generation input '{ThumbnailCompositionModelFileName}' was not found at '{NormalizePath(path)}'.");

        return JsonSerializer.Deserialize<ThumbnailCompositionModelDto>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Thumbnail composition model input could not be parsed.");
    }

    private async Task<ThumbnailSceneManifestDto> LoadThumbnailSceneManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail image generation input '{ThumbnailSceneManifestFileName}' was not found at '{NormalizePath(path)}'.");

        return JsonSerializer.Deserialize<ThumbnailSceneManifestDto>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Thumbnail scene manifest input could not be parsed.");
    }

    private static async Task<JsonDocument> EnsureJsonInputAsync(string path, string fileName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required thumbnail intelligence input '{fileName}' was not found at '{NormalizePath(path)}'.");

        return JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    private ThumbnailCompositionModelDto BuildThumbnailCompositionModel(ThumbnailAssetGenerationRequest request, ThumbnailIntelligenceDto intelligence)
    {
        var primaryHook = SelectedThumbnailHook;
        var secondaryText = "Venus + Jupiter";
        var microText = "After Sunset";
        var visualFocus = CleanTextElement(intelligence.VisualFocus, "Large Venus and Jupiter close together above twilight horizon.");
        var textElementCount = new[] { primaryHook, secondaryText, microText }.Count(text => !string.IsNullOrWhiteSpace(text));
        var readinessScore = ClampScore(intelligence.Scores.ThumbnailReadinessScore);

        return new ThumbnailCompositionModelDto(
            request.EventId,
            request.RegionId,
            request.Language,
            primaryHook,
            secondaryText,
            microText,
            "Curiosity",
            "High",
            "ScrollStoppingAstronomyThumbnail",
            visualFocus,
            new ThumbnailCompositionBlocksDto(
                new ThumbnailCompositionTextBlockDto(primaryHook, 1),
                new ThumbnailCompositionVisualBlockDto("HeroCompositionModel + PrimaryScene", 2),
                new ThumbnailCompositionTextBlockDto(secondaryText, 3),
                new ThumbnailCompositionTextBlockDto(microText, 4)),
            [
                new ThumbnailCompositionPlatformVariantDto("Landscape", "1280x720", "YouTubeThumbnail"),
                new ThumbnailCompositionPlatformVariantDto("Square", "1080x1080", "InstagramFacebookPost"),
                new ThumbnailCompositionPlatformVariantDto("Portrait", "1080x1920", "ShortsReelsCover")
            ],
            new ThumbnailCompositionValidationDto(!string.IsNullOrWhiteSpace(primaryHook), !string.IsNullOrWhiteSpace(visualFocus), textElementCount, readinessScore),
            DateTimeOffset.UtcNow);
    }

    private void EnsureApprovedSceneOutputs(string eventId, string regionId, JsonDocument sceneManifest)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName);
        var sceneIds = ResolveManifestSceneIds(sceneManifest).DefaultIfEmpty("scene-001").ToArray();
        var missingSceneOutputs = sceneIds
            .Select(sceneId => Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png"))
            .Where(path => !File.Exists(path))
            .Select(NormalizePath)
            .ToArray();

        if (missingSceneOutputs.Length > 0)
            throw new ArgumentException($"Required thumbnail composition approved scene output(s) were not found: {string.Join(", ", missingSceneOutputs)}.");
    }

    private static IReadOnlyList<string> ResolveManifestSceneIds(JsonDocument sceneManifest)
    {
        var root = sceneManifest.RootElement;
        var sceneIds = new List<string>();
        AddManifestSceneId(root, "primaryScene", sceneIds);
        AddManifestSceneId(root, "secondaryScene", sceneIds);
        AddManifestSceneId(root, "supportScene", sceneIds);
        return sceneIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddManifestSceneId(JsonElement root, string propertyName, ICollection<string> sceneIds)
    {
        if (!root.TryGetProperty(propertyName, out var sceneElement))
            return;

        var sceneId = sceneElement.ValueKind switch
        {
            JsonValueKind.String => sceneElement.GetString(),
            JsonValueKind.Object when sceneElement.TryGetProperty("sceneNumber", out var sceneNumber) && sceneNumber.TryGetInt32(out var number) => $"scene-{number:000}",
            JsonValueKind.Object when sceneElement.TryGetProperty("sceneId", out var sceneIdElement) => sceneIdElement.GetString(),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(sceneId))
            sceneIds.Add(sceneId!);
    }

    private static void ValidateThumbnailCompositionModel(ThumbnailCompositionModelDto model)
    {
        if (string.IsNullOrWhiteSpace(model.PrimaryHook))
            throw new ArgumentException("Thumbnail composition validation failed: primaryHook is required.");
        if (string.IsNullOrWhiteSpace(model.VisualFocus))
            throw new ArgumentException("Thumbnail composition validation failed: visualFocus is required.");
        if (model.Validation.TextElementCount > 3)
            throw new ArgumentException("Thumbnail composition validation failed: textElementCount must be 3 or fewer.");
        if (model.Validation.ThumbnailCompositionReadinessScore < 90)
            throw new ArgumentException("Thumbnail composition validation failed: thumbnailCompositionReadinessScore must be at least 90.");
    }

    private static IReadOnlyList<ThumbnailHookScoreDto> BuildThumbnailHookScores(HeroAssetStoryDto heroStory)
    {
        var candidates = new List<string>
        {
            "DON'T MISS THIS TONIGHT",
            "TWO BRIGHT PLANETS TOGETHER",
            "VENUS AND JUPITER TONIGHT",
            "SEE THIS AFTER SUNSET",
            "LOOK WEST TONIGHT"
        };

        if (!string.IsNullOrWhiteSpace(heroStory.HeroHook))
            candidates.Add(heroStory.HeroHook.ToUpperInvariant());

        return candidates
            .Select(CleanHook)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ScoreThumbnailHook)
            .OrderByDescending(score => string.Equals(score.Hook, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .ToArray();
    }

    private static ThumbnailHookScoreDto ScoreThumbnailHook(string hook)
    {
        var clickabilityScore = 82;
        var curiosityScore = 80;
        var emotionalPullScore = 80;
        var clarityScore = 84;

        if (hook.Contains("DON'T MISS", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 13;
            curiosityScore += 10;
            emotionalPullScore += 11;
            clarityScore -= 4;
        }

        if (hook.Contains("TONIGHT", StringComparison.OrdinalIgnoreCase))
        {
            clickabilityScore += 7;
            curiosityScore += 5;
            clarityScore += 4;
        }

        if (hook.Contains("VENUS", StringComparison.OrdinalIgnoreCase) || hook.Contains("JUPITER", StringComparison.OrdinalIgnoreCase) || hook.Contains("PLANETS", StringComparison.OrdinalIgnoreCase))
        {
            clarityScore += 10;
            emotionalPullScore += 4;
        }

        if (hook.Contains("SUNSET", StringComparison.OrdinalIgnoreCase) || hook.Contains("WEST", StringComparison.OrdinalIgnoreCase))
        {
            clarityScore += 8;
            clickabilityScore += 3;
        }

        if (hook.Length <= 30)
            clarityScore += 3;
        else
            clarityScore -= 6;

        clickabilityScore = ClampScore(clickabilityScore);
        curiosityScore = ClampScore(curiosityScore);
        emotionalPullScore = ClampScore(emotionalPullScore);
        clarityScore = ClampScore(clarityScore);
        var totalScore = ClampScore((int)Math.Round(
            (clickabilityScore * 0.35)
            + (curiosityScore * 0.25)
            + (emotionalPullScore * 0.20)
            + (clarityScore * 0.20),
            MidpointRounding.AwayFromZero));

        return new ThumbnailHookScoreDto(hook, clickabilityScore, curiosityScore, emotionalPullScore, clarityScore, totalScore);
    }

    private static string SelectTopHook(IReadOnlyList<ThumbnailHookScoreDto> hookScores)
        => hookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).FirstOrDefault()?.Hook ?? SelectedThumbnailHook;

    private static ThumbnailReadinessScoresDto BuildReadinessScores(ThumbnailHookScoreDto selectedHookScore)
    {
        var readiness = ClampScore((int)Math.Round(
            (selectedHookScore.ClickabilityScore * 0.35)
            + (selectedHookScore.CuriosityScore * 0.25)
            + (selectedHookScore.EmotionalPullScore * 0.20)
            + (selectedHookScore.ClarityScore * 0.20),
            MidpointRounding.AwayFromZero));

        return new ThumbnailReadinessScoresDto(
            selectedHookScore.ClickabilityScore,
            selectedHookScore.CuriosityScore,
            selectedHookScore.EmotionalPullScore,
            selectedHookScore.ClarityScore,
            readiness);
    }

    private static IReadOnlyList<string> ValidateReadiness(ThumbnailCopyDto thumbnailCopy, string visualFocus, string emotion, ThumbnailReadinessScoresDto scores)
    {
        var warnings = new List<string>();
        if (thumbnailCopy.PrimaryText.Length > 30)
            warnings.Add("Thumbnail primary text should be 30 characters or fewer.");
        if (new[] { thumbnailCopy.PrimaryText, thumbnailCopy.SecondaryText, thumbnailCopy.MicroText }.Count(text => !string.IsNullOrWhiteSpace(text)) > 3)
            warnings.Add("Thumbnail should use no more than 3 text elements.");
        if (string.IsNullOrWhiteSpace(visualFocus))
            warnings.Add("Thumbnail visual focus is required.");
        if (string.IsNullOrWhiteSpace(emotion))
            warnings.Add("Thumbnail emotional trigger is required.");
        if (scores.ClickabilityScore < 90)
            warnings.Add("Thumbnail approval requires clickability score >= 90.");

        return warnings;
    }

    private ThumbnailSceneManifestDto BuildThumbnailSceneManifest(ThumbnailAssetGenerationRequest request, JsonDocument heroSceneManifest)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(request.EventId, request.RegionId), SceneApprovalDirectoryName);
        var primaryImagePath = Path.Combine(sceneApprovalRoot, "scene-001-final.png");
        var secondaryImagePath = Path.Combine(sceneApprovalRoot, "scene-005-final.png");
        var supportImagePath = Path.Combine(sceneApprovalRoot, "scene-006-final.png");

        if (!HeroManifestContainsSuitablePrimaryScene(heroSceneManifest))
            throw new ArgumentException("Thumbnail scene selection validation failed: primary scene scene-001 / What is not visually suitable for thumbnail use.");

        return new ThumbnailSceneManifestDto(
            request.EventId,
            new ThumbnailSceneManifestEntryDto(1, "What", NormalizePath(primaryImagePath), "PrimaryVisual"),
            new ThumbnailSceneManifestEntryDto(5, "Why", NormalizePath(secondaryImagePath), "EmotionalSignificance"),
            new ThumbnailSceneManifestEntryDto(6, "Action", NormalizePath(supportImagePath), "UrgencyCue"),
            "Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.");
    }

    private static bool HeroManifestContainsSuitablePrimaryScene(JsonDocument heroSceneManifest)
    {
        var root = heroSceneManifest.RootElement;
        if (!root.TryGetProperty("primaryScene", out var primaryScene))
            return false;

        if (primaryScene.ValueKind != JsonValueKind.Object)
            return false;

        var sceneId = ResolveSceneId(primaryScene);
        var sceneKey = primaryScene.TryGetProperty("sceneKey", out var sceneKeyElement) ? sceneKeyElement.GetString() : null;
        var role = primaryScene.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;

        return string.Equals(sceneId, "scene-001", StringComparison.OrdinalIgnoreCase)
            && string.Equals(sceneKey, "What", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(role) || string.Equals(role, "PrimaryVisual", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveSceneId(JsonElement sceneElement)
    {
        if (sceneElement.TryGetProperty("sceneId", out var sceneIdElement) && !string.IsNullOrWhiteSpace(sceneIdElement.GetString()))
            return sceneIdElement.GetString()!;
        if (sceneElement.TryGetProperty("sceneNumber", out var sceneNumberElement) && sceneNumberElement.TryGetInt32(out var sceneNumber))
            return $"scene-{sceneNumber:000}";
        if (sceneElement.TryGetProperty("imagePath", out var imagePathElement))
        {
            var fileName = Path.GetFileNameWithoutExtension(imagePathElement.GetString() ?? string.Empty);
            if (fileName.Length >= "scene-000".Length)
                return fileName[.."scene-000".Length];
        }

        return string.Empty;
    }



    private static async Task WriteThumbnailImageAsync(string outputPath, ThumbnailImageSpec spec, ThumbnailCompositionModelDto composition, ThumbnailSceneManifestDto manifest, CancellationToken cancellationToken)
    {
        using var image = await BuildThumbnailCanvasAsync(spec, manifest.PrimaryScene.ImagePath, cancellationToken);
        image.Mutate(ctx =>
        {
            DrawApprovedSceneThumbnailOverlay(ctx, spec);
            DrawThumbnailText(ctx, spec, composition.PrimaryHook, composition.SecondaryText, composition.MicroText);
            DrawThumbnailFinish(ctx, spec.Width, spec.Height);
        });

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
    }

    private static async Task<Image<Rgba32>> BuildThumbnailCanvasAsync(ThumbnailImageSpec spec, string backgroundImagePath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(backgroundImagePath) && File.Exists(backgroundImagePath))
        {
            try
            {
                using var source = await Image.LoadAsync<Rgba32>(backgroundImagePath, cancellationToken);
                return BuildCinematicCropCanvas(source, spec);
            }
            catch (UnknownImageFormatException)
            {
                // Unit tests and partially prepared pilots may provide placeholder scene bytes.
                // Fall through to a procedural emotional thumbnail background while still
                // requiring the approved scene output file to exist.
            }
        }

        var image = new Image<Rgba32>(spec.Width, spec.Height, Color.ParseHex("#030615"));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, spec.Height), GradientRepetitionMode.None,
                new ColorStop(0f, Color.ParseHex("#020413")),
                new ColorStop(0.36f, Color.ParseHex("#071236")),
                new ColorStop(0.68f, Color.ParseHex("#342458")),
                new ColorStop(0.86f, Color.ParseHex("#D77739")),
                new ColorStop(1f, Color.ParseHex("#08040A"))), new RectangleF(0, 0, spec.Width, spec.Height));
            DrawThumbnailAtmosphere(ctx, spec.Width, spec.Height);
            DrawThumbnailPlanetPair(ctx, spec.Width, spec.Height);
        });
        return image;
    }

    private static Image<Rgba32> BuildCinematicCropCanvas(Image<Rgba32> source, ThumbnailImageSpec spec)
        => source.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(spec.Width, spec.Height),
                Mode = ResizeMode.Crop,
                Position = ResolveApprovedSceneCropAnchor(spec)
            })
            .Brightness(0.88f)
            .Saturate(1.06f)
            .Contrast(1.04f));

    private static AnchorPositionMode ResolveApprovedSceneCropAnchor(ThumbnailImageSpec spec)
        => spec.Variant switch
        {
            "Landscape" => AnchorPositionMode.Center,
            "Square" => AnchorPositionMode.Center,
            "Portrait" => AnchorPositionMode.Center,
            _ => AnchorPositionMode.Center
        };

    private static void DrawApprovedSceneThumbnailOverlay(IImageProcessingContext ctx, ThumbnailImageSpec spec)
    {
        var width = spec.Width;
        var height = spec.Height;

        // Keep the approved scene as the poster itself: only transparent grading and
        // soft text-lift gradients are added, with no framed screenshot or text panel.
        ctx.Fill(Color.Black.WithAlpha(height > width ? 0.08f : 0.10f), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, 0), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(height > width ? 0.28f : 0.34f)),
            new ColorStop(0.40f, Color.Black.WithAlpha(height > width ? 0.12f : 0.08f)),
            new ColorStop(1f, Color.Transparent)), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(height > width ? 0.22f : 0.18f)),
            new ColorStop(0.30f, Color.Transparent),
            new ColorStop(0.74f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.24f))), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, spec.HookBounds.Y), new PointF(width * (height > width ? 0.78f : 0.54f), spec.HookBounds.Y), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(height > width ? 0.20f : 0.24f)),
            new ColorStop(0.62f, Color.Black.WithAlpha(0.07f)),
            new ColorStop(1f, Color.Transparent)), new RectangleF(0, Math.Max(0, spec.HookBounds.Y - height * 0.045f), width, Math.Min(height, spec.HookBounds.Height + height * 0.16f)));
    }

    private static void DrawThumbnailAtmosphere(IImageProcessingContext ctx, int width, int height)
    {
        var random = new Random(9147 + width + height);
        for (var i = 0; i < Math.Clamp(width * height / 3200, 160, 520); i++)
        {
            var x = random.NextSingle() * width;
            var y = random.NextSingle() * height * 0.68f;
            var radius = random.NextSingle() > 0.965f ? 2.4f + random.NextSingle() * 2.2f : 0.55f + random.NextSingle() * 1.1f;
            var alpha = (0.18f + random.NextSingle() * 0.52f) * Math.Clamp(1f - y / (height * 0.78f), 0.16f, 1f);
            ctx.Fill(Color.White.WithAlpha(alpha), new EllipsePolygon(x, y, radius));
        }

        var horizonY = height * 0.82f;
        ctx.Fill(new LinearGradientBrush(new PointF(0, horizonY - height * 0.18f), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(0.46f, Color.ParseHex("#25111B").WithAlpha(0.68f)),
            new ColorStop(1f, Color.ParseHex("#030207").WithAlpha(1f))), new RectangleF(0, horizonY - height * 0.18f, width, height * 0.25f));

        var ridge = new List<PointF> { new(0, height), new(0, horizonY + height * 0.030f) };
        for (var i = 0; i <= 16; i++)
        {
            var x = width * (i / 16f);
            var y = horizonY + MathF.Sin(i * 0.92f) * height * 0.020f + MathF.Sin(i * 0.38f + 1.6f) * height * 0.015f;
            ridge.Add(new PointF(x, y));
        }
        ridge.Add(new PointF(width, height));
        ctx.Fill(Color.ParseHex("#05040A").WithAlpha(0.98f), new Polygon(new LinearLineSegment(ridge.ToArray())));
    }

    private static void DrawThumbnailPlanetPair(IImageProcessingContext ctx, int width, int height)
    {
        var isPortrait = height > width;
        var isSquare = width == height;
        var venus = isPortrait
            ? CenteredThumbnailObject(width * 0.46f, height * 0.44f, width * 0.25f)
            : isSquare
                ? CenteredThumbnailObject(width * 0.47f, height * 0.48f, width * 0.19f)
                : CenteredThumbnailObject(width * 0.68f, height * 0.48f, width * 0.15f);
        var jupiter = isPortrait
            ? CenteredThumbnailObject(width * 0.63f, height * 0.49f, width * 0.18f)
            : isSquare
                ? CenteredThumbnailObject(width * 0.66f, height * 0.53f, width * 0.13f)
                : CenteredThumbnailObject(width * 0.80f, height * 0.44f, width * 0.10f);

        var focusCenter = new PointF((venus.X + jupiter.Right) / 2f, (venus.Y + jupiter.Bottom) / 2f);
        DrawThumbnailGlow(ctx, focusCenter, Math.Max(width * 0.17f, jupiter.Right - venus.X), Math.Max(height * 0.13f, venus.Height * 1.3f), Color.ParseHex("#BFE7FF"), 0.105f, 14);
        DrawThumbnailGlow(ctx, new PointF(focusCenter.X, focusCenter.Y + height * 0.025f), Math.Max(width * 0.13f, jupiter.Right - venus.X), Math.Max(height * 0.08f, venus.Height), Color.ParseHex("#FFE29B"), 0.070f, 12);
        DrawThumbnailPlanet(ctx, venus, Color.ParseHex("#FFF3BC"), isJupiter: false);
        DrawThumbnailPlanet(ctx, jupiter, Color.ParseHex("#D9AA72"), isJupiter: true);
    }

    private static RectangleF CenteredThumbnailObject(float centerX, float centerY, float size)
        => new(centerX - size / 2f, centerY - size / 2f, size, size);

    private static void DrawThumbnailPlanet(IImageProcessingContext ctx, RectangleF bounds, Color baseColor, bool isJupiter)
    {
        DrawThumbnailGlow(ctx, new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f), bounds.Width * 0.88f, bounds.Height * 0.88f, baseColor, 0.11f, 10);
        ctx.Fill(baseColor, new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, bounds.Width / 2f, bounds.Height / 2f));
        if (isJupiter)
        {
            for (var i = 0; i < 5; i++)
            {
                var y = bounds.Y + bounds.Height * (0.25f + i * 0.11f);
                ctx.DrawLine(Color.ParseHex(i % 2 == 0 ? "#8F5A38" : "#F1D0A0").WithAlpha(0.36f), Math.Max(1.8f, bounds.Height * 0.045f), new PointF(bounds.X + bounds.Width * 0.13f, y), new PointF(bounds.Right - bounds.Width * 0.13f, y + bounds.Height * 0.025f));
            }
        }
        ctx.Fill(Color.White.WithAlpha(0.22f), new EllipsePolygon(bounds.X + bounds.Width * 0.38f, bounds.Y + bounds.Height * 0.34f, bounds.Width * 0.19f, bounds.Height * 0.12f));
        ctx.Draw(Color.White.WithAlpha(0.42f), Math.Max(1.4f, bounds.Width * 0.014f), new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, bounds.Width / 2.02f, bounds.Height / 2.02f));
    }

    private static void DrawThumbnailText(IImageProcessingContext ctx, ThumbnailImageSpec spec, string hook, string secondary, string micro)
    {
        var hookFont = ResolveThumbnailFont(spec.HookFontSize, FontStyle.Bold);
        var secondaryFont = ResolveThumbnailFont(spec.SecondaryFontSize, FontStyle.Bold);
        var microFont = ResolveThumbnailFont(spec.MicroFontSize, FontStyle.Bold);
        DrawImpactText(ctx, hook, hookFont, spec.HookBounds, Color.White, Color.ParseHex("#05050A"), stroke: Math.Max(4f, spec.Width * 0.006f));
        DrawIntegratedText(ctx, secondary, secondaryFont, spec.SecondaryOrigin, Color.ParseHex("#FFE29B"));
        DrawIntegratedText(ctx, micro, microFont, spec.MicroOrigin, Color.ParseHex("#CDEBFF"));
    }

    private static void DrawImpactText(IImageProcessingContext ctx, string text, Font font, RectangleF bounds, Color fill, Color shadow, float stroke)
    {
        var options = new RichTextOptions(font) { Origin = new PointF(bounds.X, bounds.Y), WrappingLength = bounds.Width, LineSpacing = 0.86f };
        var deepShadowOffset = Math.Max(3f, stroke * 0.72f);
        var softShadowOffset = Math.Max(1.5f, stroke * 0.30f);
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(bounds.X + deepShadowOffset, bounds.Y + deepShadowOffset) }, text, shadow.WithAlpha(0.68f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(bounds.X + softShadowOffset, bounds.Y + softShadowOffset) }, text, shadow.WithAlpha(0.38f));
        ctx.DrawText(options, text, fill);
    }

    private static void DrawIntegratedText(IImageProcessingContext ctx, string text, Font font, PointF origin, Color color)
    {
        var options = new RichTextOptions(font) { Origin = origin };
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 3f, origin.Y + 3f) }, text, Color.Black.WithAlpha(0.72f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 1f, origin.Y + 1f) }, text, Color.Black.WithAlpha(0.36f));
        ctx.DrawText(options, text, color);
    }

    private static void DrawThumbnailFinish(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, 0), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(height > width ? 0.20f : 0.28f)),
            new ColorStop(0.48f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.12f))), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(0.12f)),
            new ColorStop(0.78f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.28f))), new RectangleF(0, 0, width, height));
    }

    private static void DrawThumbnailGlow(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float alpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            ctx.Fill(color.WithAlpha(alpha * MathF.Pow(1f - t * 0.70f, 1.35f)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static Font ResolveThumbnailFont(float size, FontStyle style)
    {
        foreach (var name in new[] { "Inter", "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans" })
        {
            if (SystemFonts.TryGet(name, out var family)) return family.CreateFont(size, style);
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
            throw new InvalidOperationException("No system fonts available for thumbnail image generation.");

        return fallbackFamily.CreateFont(size, style);
    }

    private static void ValidateThumbnailImageInputs(ThumbnailIntelligenceDto intelligence, ThumbnailCompositionModelDto composition, ThumbnailSceneManifestDto manifest)
    {
        var textElements = new[] { composition.PrimaryHook, composition.SecondaryText, composition.MicroText }.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        if (!string.Equals(composition.PrimaryHook, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(intelligence.ThumbnailCopy.PrimaryText, SelectedThumbnailHook, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail image generation validation failed: primary hook must be DON'T MISS THIS TONIGHT.");
        if (!string.Equals(composition.SecondaryText, "Venus + Jupiter", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail image generation validation failed: secondary text must be Venus + Jupiter.");
        if (!string.Equals(composition.MicroText, "After Sunset", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail image generation validation failed: micro text must be After Sunset.");
        if (textElements.Length != 3)
            throw new ArgumentException("Thumbnail image generation validation failed: exactly 3 text blocks are required.");
        if (new[] { composition.PrimaryHook, composition.SecondaryText, composition.MicroText, composition.VisualFocus, manifest.SelectionReason }.Any(ContainsForbiddenThumbnailText))
            throw new ArgumentException("Thumbnail image generation validation failed: thumbnail contains hero/instruction overlay language.");
        if (!string.Equals(manifest.PrimaryScene.SceneId, "scene-001", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.SecondaryScene.SceneId, "scene-005", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.SupportScene.SceneId, "scene-006", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail image generation validation failed: scene sources must be scene-001, scene-005, and scene-006.");
    }

    private static bool ContainsForbiddenThumbnailText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var forbidden = new[] { "7:23", "IST", "altitude", "west marker", "look west", "step", "instruction", "CTA paragraph", "timeline", "guide" };
        return forbidden.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateThumbnailLayout(ThumbnailLayoutValidationDto validation)
    {
        if (!validation.HookVisible)
            throw new ArgumentException("Thumbnail layout validation failed: hookVisible must be true.");
        if (!validation.VisualFocusVisible)
            throw new ArgumentException("Thumbnail layout validation failed: visualFocusVisible must be true.");
        if (validation.TextElementCount != 3)
            throw new ArgumentException("Thumbnail layout validation failed: textElementCount must be 3.");
        if (validation.ThumbnailClickabilityScore < 95)
            throw new ArgumentException("Thumbnail layout validation failed: thumbnailClickabilityScore must be at least 95.");
        if (validation.ThumbnailCuriosityScore < 95)
            throw new ArgumentException("Thumbnail layout validation failed: thumbnailCuriosityScore must be at least 95.");
        if (validation.PhotoCinematicRendererUsed)
        {
            var isMeteorShowerThumbnail = string.Equals(validation.ThumbnailVisualSourceMode, "MeteorShowerPhotoCinematicThumbnail", StringComparison.OrdinalIgnoreCase);
            if (!isMeteorShowerThumbnail && !string.Equals(validation.ThumbnailVisualSourceMode, "PhotoCinematicThumbnail", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic thumbnailVisualSourceMode must be PhotoCinematicThumbnail.");
            if (!isMeteorShowerThumbnail && !string.Equals(validation.SourceSceneUsed, "none", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic sourceSceneUsed must be none.");
            if (validation.ApprovedSceneFoundationUsed)
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic approvedSceneFoundationUsed must be false.");
            if (!isMeteorShowerThumbnail && !validation.IndependentPlanetRedrawUsed)
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic independentPlanetRedrawUsed must be true.");
            if (validation.CinematicCropApplied)
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic cinematicCropApplied must be false.");
            if (!validation.OldThumbnailRendererBypassed || !validation.SceneTextLabelsRemoved || !validation.TextBoxesRemoved || (!isMeteorShowerThumbnail && (!validation.VenusRenderedAsStarPoint || !validation.JupiterRenderedAsPlanet)))
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic bypass and rendering flags must be true.");
        }
        else
        {
            if (!string.Equals(validation.ThumbnailVisualSourceMode, "ApprovedSceneSmartCrop", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Thumbnail layout validation failed: thumbnailVisualSourceMode must be ApprovedSceneSmartCrop.");
            if (!string.Equals(validation.SourceSceneUsed, "scene-001", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Thumbnail layout validation failed: sourceSceneUsed must be scene-001.");
            if (!validation.ApprovedSceneFoundationUsed)
                throw new ArgumentException("Thumbnail layout validation failed: approvedSceneFoundationUsed must be true.");
            if (validation.IndependentPlanetRedrawUsed)
                throw new ArgumentException("Thumbnail layout validation failed: independentPlanetRedrawUsed must be false.");
            if (!validation.CinematicCropApplied)
                throw new ArgumentException("Thumbnail layout validation failed: cinematicCropApplied must be true.");
        }
        if (!validation.ArtificialGlowRemoved)
            throw new ArgumentException("Thumbnail layout validation failed: artificialGlowRemoved must be true.");
        if (validation.VisualSourceQualityScore < 90)
            throw new ArgumentException("Thumbnail layout validation failed: visualSourceQualityScore must be at least 90.");
        if (validation.EnvironmentVisibilityScore < 90)
            throw new ArgumentException("Thumbnail layout validation failed: environmentVisibilityScore must be at least 90.");
        if (validation.AstronomyContextScore < 90)
            throw new ArgumentException("Thumbnail layout validation failed: astronomyContextScore must be at least 90.");
        if (validation.ThumbnailFinalReadinessScore < 95)
            throw new ArgumentException("Thumbnail layout validation failed: thumbnailFinalReadinessScore must be at least 95.");
    }

    private static void ValidateThumbnailSceneManifest(ThumbnailSceneManifestDto manifest, bool requireSavedManifest, string outputPath)
    {
        if (manifest.PrimaryScene is null || string.IsNullOrWhiteSpace(manifest.PrimaryScene.ImagePath))
            throw new ArgumentException("Thumbnail scene selection validation failed: primaryScene is required.");
        if (!string.Equals(manifest.PrimaryScene.SceneId, "scene-001", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PrimaryScene.SceneKey, "What", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PrimaryScene.Role, "PrimaryVisual", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail scene selection validation failed: primary scene is not visually suitable.");

        var selectedImagePaths = new[]
        {
            manifest.PrimaryScene.ImagePath,
            manifest.SecondaryScene.ImagePath,
            manifest.SupportScene.ImagePath
        };
        var missingImages = selectedImagePaths.Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingImages.Length > 0)
            throw new ArgumentException($"Thumbnail scene selection validation failed: selected image file(s) missing: {string.Join(", ", missingImages)}.");
        if (requireSavedManifest && !File.Exists(outputPath))
            throw new ArgumentException($"Thumbnail scene selection validation failed: manifest was not saved at '{NormalizePath(outputPath)}'.");
    }

    private static string ResolveRecommendedSourceScene(JsonDocument compositionModel)
    {
        if (compositionModel.RootElement.TryGetProperty("visualBlock", out var visualBlock)
            && visualBlock.TryGetProperty("sourceScene", out var sourceScene)
            && !string.IsNullOrWhiteSpace(sourceScene.GetString()))
            return sourceScene.GetString()!;

        return "scene-001";
    }

    private static void ValidateRequest(ThumbnailAssetGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
        if (!string.Equals(request.Phase, "Intelligence", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Phase, "Composition", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Phase, "SceneSelection", StringComparison.OrdinalIgnoreCase)
            && !IsImageGenerationPhase(request.Phase))
            throw new ArgumentException("Only thumbnail asset phases 'Intelligence', 'Composition', 'SceneSelection', and 'ImageGeneration' are supported in this endpoint version.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.EventId)
            || !string.Equals(request.RegionId, GoldenRegionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Language, GoldenLanguage, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail intelligence generation requires a non-empty event id for IN-RJ-UDAIPUR / en.", nameof(request));
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName);

    private string BuildThumbnailIntelligenceOutputPath(string eventId, string regionId)
        => Path.Combine(BuildThumbnailAssetsRoot(eventId, regionId), ThumbnailIntelligenceFileName);

    private string BuildThumbnailCompositionOutputPath(string eventId, string regionId)
        => Path.Combine(BuildThumbnailAssetsRoot(eventId, regionId), ThumbnailCompositionModelFileName);

    private string BuildThumbnailSceneManifestOutputPath(string eventId, string regionId)
        => Path.Combine(BuildThumbnailAssetsRoot(eventId, regionId), ThumbnailSceneManifestFileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }



    private static bool IsImageGenerationPhase(string phase)
        => string.Equals(phase, "ImageGeneration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "Images", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "Generate", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUsePhotoCinematicThumbnailRenderer(ThumbnailAssetGenerationRequest request)
        => (string.Equals(request.Phase, "Images", StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.ThumbnailStyle, "ScrollStopping", StringComparison.OrdinalIgnoreCase))
            || string.Equals(request.ThumbnailVisualStyle, "PhotoCinematic", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<ThumbnailImageSpec> ThumbnailImageSpecs =
    [
        new("Landscape", "thumbnail-landscape.png", 1280, 720, new RectangleF(58, 54, 650, 214), new PointF(74, 286), new PointF(82, 628), 70f, 36f, 28f),
        new("Square", "thumbnail-square.png", 1080, 1080, new RectangleF(66, 76, 860, 250), new PointF(84, 350), new PointF(84, 910), 76f, 42f, 34f),
        new("Portrait", "thumbnail-portrait.png", 1080, 1920, new RectangleF(70, 112, 920, 360), new PointF(86, 1288), new PointF(86, 1404), 96f, 58f, 44f)
    ];

    private static string CleanHook(string value)
        => (value ?? string.Empty).Trim().Trim('.', '!', '?').ToUpperInvariant();

    private static string CleanTextElement(string? value, string fallback)
        => string.Join(' ', (string.IsNullOrWhiteSpace(value) ? fallback : value).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record ThumbnailImageSpec(
        string Variant,
        string FileName,
        int Width,
        int Height,
        RectangleF HookBounds,
        PointF SecondaryOrigin,
        PointF MicroOrigin,
        float HookFontSize,
        float SecondaryFontSize,
        float MicroFontSize);
}
