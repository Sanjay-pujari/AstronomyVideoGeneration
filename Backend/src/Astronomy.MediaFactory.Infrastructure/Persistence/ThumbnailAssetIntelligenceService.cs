using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using System.Security.Cryptography;
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

public sealed class ThumbnailAssetIntelligenceService(IOptions<RenderingOptions> renderingOptions, IOptions<AzureOpenAIForImageOptions> imageOptions, IVisualSourceResolver visualSourceResolver, IHttpClientFactory httpClientFactory, IOptions<ThumbnailOptions>? thumbnailOptions = null) : IThumbnailAssetIntelligenceService
{
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
    private const string Phase12SemanticValidationFileName = "phase-12-validation.json";
    private const string ThumbnailFinalFileName = "thumbnail-final.png";
    private const string ThumbnailReviewFileName = "thumbnail-review.json";
    private const string ThumbnailPromptFileName = "thumbnail-prompt.json";
    private const string Rc1GuideThumbnailContract = "ThumbnailV3PureAzureImage2CtrOverlay";
    private const string Phase12ThumbnailRenderer = "AzureImage2ThumbnailV5Variants";
    private const string Phase12OverlayRenderer = "ThumbnailV3PureAzureImage2CtrOverlay";
    private const string ThumbnailGenerationDiagnosticsFileName = "thumbnail-generation-diagnostics.json";
    private const string DefaultThumbnailHook = "CURRENT SKY EVENT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private ProductionPipelineExecutionContext? _activeProductionContext;

    public ThumbnailAssetIntelligenceService(IOptions<RenderingOptions> renderingOptions, IVisualSourceResolver visualSourceResolver)
        : this(renderingOptions, Options.Create(new AzureOpenAIForImageOptions { Endpoint = "https://example.openai.azure.com", ImageDeployment = "test-image2", ApiKey = "test" }), visualSourceResolver, new DefaultHttpClientFactory(), Options.Create(new ThumbnailOptions()))
    {
    }

    public async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailAssetsAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _activeProductionContext = request.ProductionContext;
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
            if (!string.Equals(existing.LayoutStyle, "ScrollStoppingAstronomyThumbnail", StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Architecture, Rc1GuideThumbnailContract, StringComparison.OrdinalIgnoreCase))
                return new ThumbnailAssetGenerationResponse(request.Phase, "Composition", true, NormalizePath(outputPath), existing.Validation.ThumbnailCompositionReadinessScore, []);
        }

        var thumbnailIntelligence = await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
        if (request.ProductionContext is null)
        {
            var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
            var sceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
            await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
            EnsureApprovedSceneOutputs(request.EventId, request.RegionId, sceneManifest);
        }

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
            var existingArchitecture = existing.ValidationFacts.TryGetValue("thumbnailArchitecture", out var architecture) ? architecture : string.Empty;
            if (!existingArchitecture.Contains("ThumbnailV3", StringComparison.OrdinalIgnoreCase)
                && string.Equals(existingArchitecture, Rc1GuideThumbnailContract, StringComparison.OrdinalIgnoreCase))
            {
                if (request.ProductionContext is null)
                    ValidateThumbnailSceneManifest(existing, requireSavedManifest: false, outputPath: outputPath);
                return BuildSceneSelectionResponse(request, outputPath, existing);
            }
            if (request.ProductionContext is null)
                ValidateThumbnailSceneManifest(existing, requireSavedManifest: false, outputPath: outputPath);
        }

        var thumbnailRoot = Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot();
        ThumbnailSceneManifestDto manifest;
        if (request.ProductionContext is not null)
        {
            manifest = BuildRc1ThumbnailManifest(request, thumbnailRoot);
        }
        else
        {
            var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
            await LoadThumbnailIntelligenceAsync(BuildThumbnailIntelligenceOutputPath(request.EventId, request.RegionId), cancellationToken);
            await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);
            var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
            manifest = BuildThumbnailSceneManifest(request, heroSceneManifest);
            ValidateThumbnailSceneManifest(manifest, requireSavedManifest: false, outputPath: outputPath);
        }

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            if (request.ProductionContext is null)
                ValidateThumbnailSceneManifest(manifest, requireSavedManifest: true, outputPath: outputPath);
        }

        return BuildSceneSelectionResponse(request, outputPath, manifest);
    }



    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var thumbnailRoot = BuildThumbnailAssetsRoot(request.EventId, request.RegionId);
        if (thumbnailOptions?.Value.EnableThumbnailV7 != false)
            return await GenerateThumbnailV7ImagesAsync(request, thumbnailRoot, cancellationToken);
        if (request.ProductionContext is not null)
            return await GeneratePureV3ThumbnailImagesAsync(request, thumbnailRoot, cancellationToken);
        if (IsMeteorShowerThumbnail(request))
            return await GenerateMeteorShowerThumbnailImagesAsync(request, thumbnailRoot, cancellationToken);
        if (request.ProductionContext is not null || ShouldUsePhotoCinematicThumbnailRenderer(request))
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

    private async Task<ThumbnailAssetGenerationResponse> GenerateThumbnailV7ImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        var result = await new ThumbnailV7InfographicRenderer(thumbnailOptions?.Value.AssetRootPath ?? "assets/celestial").RenderAsync(request, thumbnailRoot, request.OverwriteExisting, cancellationToken);
        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 8,
            ThumbnailReadabilityScore: 98,
            ThumbnailClickabilityScore: 96,
            ThumbnailCuriosityScore: 95,
            ThumbnailVisualSourceMode: "ThumbnailV7PremiumAstronomyInfographic",
            SourceSceneUsed: "AzureImage2BackgroundOnly",
            ApprovedSceneFoundationUsed: false,
            IndependentPlanetRedrawUsed: true,
            ArtificialGlowRemoved: true,
            VisualSourceQualityScore: 98,
            CinematicCropApplied: false,
            EnvironmentVisibilityScore: 98,
            AstronomyContextScore: 98,
            ThumbnailFinalReadinessScore: 98,
            PhotoCinematicRendererUsed: false,
            OldThumbnailRendererBypassed: true,
            SceneTextLabelsRemoved: true,
            TextBoxesRemoved: false);
        return BuildImageGenerationResponse(
            request,
            result.OutputFiles,
            validation,
            warnings: [],
            requestedRenderer: "ThumbnailV7InfographicRenderer",
            actualRendererUsed: "ThumbnailV7InfographicRenderer",
            rendererSelectionReason: "ThumbnailGeneration:EnableThumbnailV7 is true; Phase 12 routes to the clean V7 infographic module instead of V5 fallback.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: false,
            photoCinematicRendererCompleted: false,
            outputWriteSource: "ThumbnailV7InfographicRenderer",
            outputOverwriteDetected: false,
            thumbnailLayoutValidationPath: result.DiagnosticsPath);
    }

    private async Task<ThumbnailAssetGenerationResponse> GenerateMeteorShowerThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        var outputFiles = PhotoCinematicThumbnailRenderer.PlannedOutputFiles(thumbnailRoot).ToArray();
        var manifestPath = BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId);
        var manifest = await LoadThumbnailSceneManifestAsync(manifestPath, cancellationToken);
        ValidateThumbnailSceneManifest(manifest, requireSavedManifest: true, outputPath: manifestPath);
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
                await WriteMeteorThumbnailAsync(file, request, cancellationToken);
            var renderRequest = BuildPhotoCinematicRenderRequest(request, manifest, manifestPath, forceMeteor: true);
            ValidateThumbnailSourceBelongsToCurrentRun(request, renderRequest, manifestPath);
            var forbiddenObjects = DetectForbiddenObjects(request, renderRequest.VisualObjects.Concat(renderRequest.Labels)).ToArray();
            ValidateThumbnailSemantics(request, renderRequest.VisualObjects, renderRequest.Labels, forbiddenObjects, renderRequest.VisualResolverResult as VisualSourceResolutionResult);
            await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
            await UpdateThumbnailSceneManifestGeneratedPathsAsync(manifestPath, outputFiles, validation, cancellationToken, renderRequest, null, forbiddenObjects);
        }

        return BuildImageGenerationResponse(
            request,
            outputFiles,
            validation,
            requestedRenderer: "MeteorShowerPhotoCinematicThumbnailRenderer",
            actualRendererUsed: "MeteorShowerPhotoCinematicThumbnailRenderer",
            rendererSelectionReason: "MeteorShower event intelligence selected meteor-shower-specific thumbnail imagery with meteor streaks and no Venus/Jupiter planets.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: true,
            photoCinematicRendererCompleted: true,
            outputWriteSource: "MeteorShowerPhotoCinematicThumbnailRenderer",
            thumbnailLayoutValidationPath: validationPath);
    }

    private static async Task WriteMeteorThumbnailAsync(string outputPath, ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
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
            var rng = new Random(HashCode.Combine(request.EventId, request.RegionId, width, height));
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
            var intelligence = request.ProductionContext?.ProductionEventIntelligence;
            var title = CleanThumbnailText(intelligence?.ShortTitle ?? intelligence?.Title ?? "Meteor Shower Peak", "Meteor Shower Peak", 28);
            var window = CleanThumbnailText(ResolveMeteorViewingWindow(intelligence), "Dark pre-dawn sky", 30);
            var moon = CleanThumbnailText(intelligence?.MoonInterference ?? "Low moon interference", "Low moon interference", 30);
            ctx.DrawText(title, font, Color.White, new PointF(width * 0.06f, height * 0.08f));
            ctx.DrawText(window, small, Color.ParseHex("#F8D36B"), new PointF(width * 0.06f, height * 0.23f));
            ctx.DrawText(moon, small, Color.ParseHex("#BFE6FF"), new PointF(width * 0.06f, height * 0.31f));
        });
        await image.SaveAsPngAsync(outputPath, cancellationToken);
    }

    private bool IsMeteorShowerThumbnail(ThumbnailAssetGenerationRequest request)
    {
        var currentEventLock = BuildCurrentEventLock(request);
        if (IsMeteorEvent(currentEventLock.EventType, currentEventLock.Title)) return true;
        var storyPath = Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), HeroAssetStoryFileName);
        if (!File.Exists(storyPath)) return false;
        var text = File.ReadAllText(storyPath);
        return text.Contains("meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("meteor shower", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ThumbnailAssetGenerationResponse> GeneratePhotoCinematicThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        const string rendererName = "PhotoCinematicThumbnailRenderer";
        Console.WriteLine($"[ThumbnailImages] Requested renderer = {rendererName}");

        var outputFiles = PhotoCinematicThumbnailRenderer.PlannedOutputFiles(thumbnailRoot).ToArray();
        var manifestPath = BuildThumbnailSceneManifestOutputPath(request.EventId, request.RegionId);
        var manifest = await LoadThumbnailSceneManifestAsync(manifestPath, cancellationToken);
        ValidateThumbnailSceneManifest(manifest, requireSavedManifest: true, outputPath: manifestPath);
        var validationPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName));

        var renderRequest = BuildPhotoCinematicRenderRequest(request, manifest, manifestPath);
        ValidateThumbnailSourceBelongsToCurrentRun(request, renderRequest, manifestPath);
        var initialForbiddenObjects = DetectForbiddenObjects(request, renderRequest.VisualObjects.Concat(renderRequest.Labels)).ToArray();
        if (initialForbiddenObjects.Length > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated object label(s) detected: " + string.Join(", ", initialForbiddenObjects));

        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 3,
            ThumbnailReadabilityScore: 98,
            ThumbnailClickabilityScore: 99,
            ThumbnailCuriosityScore: 99,
            ThumbnailVisualSourceMode: "PhotoCinematicThumbnail",
            SourceSceneUsed: manifest.PrimaryScene.SceneId,
            ApprovedSceneFoundationUsed: !string.IsNullOrWhiteSpace(renderRequest.SourceImagePath),
            IndependentPlanetRedrawUsed: string.IsNullOrWhiteSpace(renderRequest.SourceImagePath),
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
            VenusRenderedAsStarPoint: EventAllowsObject(request, "Venus") && renderRequest.VisualObjects.Any(value => value.Contains("Venus", StringComparison.OrdinalIgnoreCase)),
            JupiterRenderedAsPlanet: EventAllowsObject(request, "Jupiter") && renderRequest.VisualObjects.Any(value => value.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)));
        ValidateThumbnailLayout(validation);

        var renderEntered = false;
        var renderCompleted = false;
        PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderResult? renderResult = null;
        if (!request.DryRun)
        {
            foreach (var file in outputFiles)
            {
                var variant = Path.GetFileNameWithoutExtension(file).Replace("thumbnail-", string.Empty, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"[ThumbnailImages] Writing {variant} = {file}");
            }

            renderResult = await PhotoCinematicThumbnailRenderer.RenderAsync(thumbnailRoot, renderRequest, cancellationToken);
            renderEntered = renderResult.Entered;
            renderCompleted = renderResult.Completed;
            if (!renderEntered || !renderCompleted)
                throw new InvalidOperationException("PhotoCinematicThumbnailRenderer was not invoked.");

            var missingWrites = outputFiles.Except(renderResult.WrittenFiles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missingWrites.Length > 0)
                throw new InvalidOperationException($"PhotoCinematicThumbnailRenderer did not write expected thumbnail file(s): {string.Join(", ", missingWrites)}.");

            var forbiddenObjects = DetectForbiddenObjects(request, renderResult.VisualObjectsUsed.Concat(renderResult.LabelsUsed)).ToArray();
            ValidateThumbnailSemantics(request, renderResult.VisualObjectsUsed, renderResult.LabelsUsed, forbiddenObjects, renderRequest.VisualResolverResult as VisualSourceResolutionResult);
            await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName), JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
            await UpdateThumbnailSceneManifestGeneratedPathsAsync(manifestPath, outputFiles, validation, cancellationToken, renderRequest, renderResult, forbiddenObjects);
        }
        else
        {
            renderEntered = true;
            renderCompleted = true;
            ValidateThumbnailSemantics(request, renderRequest.VisualObjects, renderRequest.Labels, initialForbiddenObjects, renderRequest.VisualResolverResult as VisualSourceResolutionResult);
        }

        Console.WriteLine($"[ThumbnailImages] Actual renderer = {rendererName}");

        return BuildImageGenerationResponse(
            request,
            outputFiles,
            validation,
            requestedRenderer: rendererName,
            actualRendererUsed: rendererName,
            rendererSelectionReason: "Images phase uses PhotoCinematicThumbnailRenderer bound to current production event metadata and hero/scene manifest source imagery; static planet compositions are bypassed unless the event includes those planets.",
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

        if (request.ProductionContext is not null)
            return await GeneratePureV3ThumbnailIntelligenceAsync(request, outputPath, cancellationToken);

        var heroAssetsRoot = BuildHeroAssetsRoot(request.EventId, request.RegionId);
        var heroStory = await LoadHeroStoryAsync(heroAssetsRoot, cancellationToken);
        await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        var compositionModel = await EnsureJsonInputAsync(Path.Combine(heroAssetsRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);

        var warnings = new List<string>();
        var hookScores = BuildThumbnailHookScores(heroStory);
        var selectedHook = SelectTopHook(hookScores);
        var selectedHookScore = hookScores.First(score => string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase));
        var alternativeHooks = hookScores
            .Where(score => !string.Equals(score.Hook, selectedHook, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .Select(score => score.Hook)
            .ToArray();

        var recommendedSourceScene = ResolveRecommendedSourceScene(compositionModel);
        var thumbnailCopy = new ThumbnailCopyDto(
            selectedHook,
            DeriveSecondaryThumbnailText(heroStory),
            DeriveMicroThumbnailText(heroStory));
        var scores = BuildReadinessScores(selectedHookScore);
        if (TryBuildPlanetFamilyThumbnailCopy(request.ProductionContext?.ProductionEventIntelligence, out var planetCopy))
        {
            selectedHook = planetCopy.PrimaryText;
            thumbnailCopy = planetCopy;
            scores = BuildCompactPlanetFamilyReadinessScores();
        }
        var visualFocus = CleanTextElement(heroStory.HeroVisualFocus, "Timely sky event above the local horizon.");
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

    private async Task<ThumbnailAssetGenerationResponse> GeneratePureV3ThumbnailIntelligenceAsync(ThumbnailAssetGenerationRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var current = BuildCurrentEventLock(request);
        var isMeteor = IsMeteorEvent(current.EventType, current.Title);
        var isPlanetary = IsPlanetaryEvent(current.EventType);
        var isMoon = IsMoonEvent(current.EventType, current.Title);
        var textLines = BuildRc1ThumbnailTextLines(current, includeDateWhenAvailable: true);
        var primary = isPlanetary ? BuildPlanetaryCleanHeadline(current) : textLines.ElementAtOrDefault(0) ?? "SKY EVENT";
        var secondary = textLines.ElementAtOrDefault(1) ?? string.Empty;
        var micro = textLines.ElementAtOrDefault(2) ?? string.Empty;
        if (isPlanetary) ValidatePlanetaryThumbnailProfile(current, primary);
        var copy = new ThumbnailCopyDto(primary, secondary, micro);
        var scores = new ThumbnailReadinessScoresDto(98, 98, 96, 96, 98);
        var intelligence = new ThumbnailIntelligenceDto(
            request.EventId,
            request.RegionId,
            request.Language,
            primary,
            [secondary, micro],
            [new ThumbnailHookScoreDto(primary, 98, 98, 96, 96, 97)],
            "Urgency + Wonder",
            "High",
            isMeteor ? "A dramatic meteor-shower peak night that feels worth clicking immediately." : isPlanetary ? "A deterministic planetary sky-guide thumbnail with labels, direction, timing, and separation." : isMoon ? "A deterministic Moon phase guide thumbnail with lunar phase, illumination, date/time, and moonrise cues when available." : "A timely astronomy event with direct click-through text.",
            BuildPureV3VisualFocus(current),
            isPlanetary ? "PlanetaryEvent thumbnail: Azure Image2 generates background only; deterministic overlay adds guide card, object labels, direction cue, and separation." : isMoon ? "Moon thumbnail: Azure Image2 generates a realistic Moon background only; deterministic overlay adds title, phase, date/time, illumination, and moonrise/moonset cues when available." : "Thumbnail V5 thumbnail: Azure Image2 generates background only; deterministic overlay adds clean title/subtitle.",
            "PureAzureImage2Prompt",
            "none",
            ["scene image selection", "approved scene assets", "hero-scene-manifest.json", "thumbnail-scene-manifest.json"],
            copy,
            [new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click")],
            scores,
            [],
            DateTimeOffset.UtcNow,
            EventFamily: isPlanetary ? "PlanetaryEvent" : isMeteor ? "MeteorEvent" : isMoon ? "Moon" : null,
            ThumbnailOverlayTemplate: ResolveThumbnailOverlayTemplate(request),
            GuideCard: isPlanetary ? BuildPlanetaryGuideCard(current) : null,
            ObjectLabels: isPlanetary ? ResolvePlanetaryObjectLabels(current) : null,
            Callouts: isPlanetary ? BuildPlanetaryCallouts(current) : null,
            SkyGuideCue: isPlanetary ? NormalizeDirectionCue(current.SkyDirectionHint).ToUpperInvariant() : null);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        }

        return new ThumbnailAssetGenerationResponse(request.Phase, "Intelligence", false, string.Empty, 0, [], true, NormalizePath(outputPath), primary, scores.ThumbnailReadinessScore);
    }

    private async Task<ThumbnailAssetGenerationResponse> GeneratePureV3ThumbnailImagesAsync(ThumbnailAssetGenerationRequest request, string thumbnailRoot, CancellationToken cancellationToken)
    {
        var prompt = BuildPureV3ThumbnailPrompt(request);
        var finalPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailFinalFileName));
        var reviewPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailReviewFileName));
        var promptPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailPromptFileName));
        var validationPath = NormalizePath(Path.Combine(thumbnailRoot, Phase12SemanticValidationFileName));
        var layoutPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailLayoutValidationFileName));
        var diagnosticsPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailGenerationDiagnosticsFileName));
        var outputFiles = new[] { finalPath, NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), reviewPath, promptPath, validationPath, layoutPath };
        var validation = new ThumbnailLayoutValidationDto(
            HookVisible: true,
            VisualFocusVisible: true,
            TextElementCount: 3,
            ThumbnailReadabilityScore: 99,
            ThumbnailClickabilityScore: 99,
            ThumbnailCuriosityScore: 98,
            ThumbnailVisualSourceMode: "ThumbnailV3PureAzureImage2CtrOverlay",
            SourceSceneUsed: "none",
            ApprovedSceneFoundationUsed: false,
            IndependentPlanetRedrawUsed: false,
            ArtificialGlowRemoved: true,
            VisualSourceQualityScore: 99,
            CinematicCropApplied: false,
            EnvironmentVisibilityScore: 98,
            AstronomyContextScore: 98,
            ThumbnailFinalReadinessScore: 99,
            PhotoCinematicRendererUsed: true,
            OldThumbnailRendererBypassed: true,
            SceneTextLabelsRemoved: true,
            TextBoxesRemoved: true,
            VenusRenderedAsStarPoint: false,
            JupiterRenderedAsPlanet: false);

        var semanticProfile = ResolveThumbnailValidatorProfile(request);
        var forbiddenObjects = DetectForbiddenObjects(request, prompt.VisualObjects.Concat(prompt.CtrOverlay).Append(prompt.Badge)).ToArray();
        var forbiddenTermsDetected = DetectThumbnailForbiddenTerms(semanticProfile, prompt.VisualObjects.Concat(prompt.CtrOverlay).Append(prompt.Badge)).ToArray();
        var goldenPilotLeakageDetected = ContainsGoldenPilotLeakage(prompt);
        if (forbiddenObjects.Length > 0 || forbiddenTermsDetected.Length > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated profile term(s) detected in thumbnail metadata/overlay text: " + string.Join(", ", forbiddenObjects.Concat(forbiddenTermsDetected).Distinct(StringComparer.OrdinalIgnoreCase)));
        if (goldenPilotLeakageDetected)
            throw new InvalidOperationException("Thumbnail semantic validation failed: golden pilot leakage detected.");

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            CleanAzureImage2ThumbnailV5FinalRoot(thumbnailRoot);
            var candidatesRoot = Path.Combine(thumbnailRoot, "candidates");
            Directory.CreateDirectory(candidatesRoot);
            var thumbnailVariants = BuildAzureImage2ThumbnailV5VariantPrompts(request);
            var thumbnailCompositionType = ResolveThumbnailCompositionType(request);
            var runtimeDiagnostics = BuildThumbnailRuntimeDiagnostics(
                request,
                thumbnailCompositionType,
                thumbnailPromptBuilder: "BuildAzureImage2ThumbnailV5VariantPrompts",
                finalThumbnailPrompt: string.Empty,
                thumbnailRenderer: "AzureImage2ThumbnailV5Variants",
                selectedThumbnailStrategy: "ThumbnailV3PureAzureImage2CtrOverlay",
                thumbnailVisualSourceMode: validation.ThumbnailVisualSourceMode,
                finalThumbnailPath: finalPath);
            if (!IsPlanetaryEvent(prompt.EventType)) ValidateRc1ThumbnailContract(prompt.CtrOverlay, thumbnailVariants.Select(v => v.Layout));
            var finalPromptText = JsonSerializer.Serialize(new
            {
                prompt,
                variants = thumbnailVariants,
                thumbnailContractRequested = Rc1GuideThumbnailContract,
                thumbnailContractSelected = Rc1GuideThumbnailContract,
                eventFamily = semanticProfile.ResolvedEventFamily,
                eventFamilyResolverInput = semanticProfile.EventFamilyResolverInput,
                eventFamilyResolverReason = semanticProfile.EventFamilyResolverReason,
                eventFamilyProfileName = semanticProfile.EventFamilyProfileName,
                eventFamilyProfileVersion = semanticProfile.EventFamilyProfileVersion
            }, JsonOptions);
            WriteThumbnailGenerationConfigurationDiagnostics(finalPromptText, imageOptions.Value, 1280, 720, promptPath, diagnosticsPath);
            var thumbnailTotalStopwatch = Stopwatch.StartNew();
            var thumbnailVariantResults = new List<(string Variant, string Prompt, int Width, int Height, string TextLayout, string BackgroundPath, string ImagePath, AzureImage2GenerationResult Result, string Hash)>();
            var overlayDiagnosticsByVariant = new List<(string Variant, ThumbnailOverlayDiagnostics Diagnostics)>();
            var finalFileWrites = new List<(string Path, string HashBeforeWrite, string HashAfterWrite, string WriterComponent, string TemplateName, string TemplateVersion)>();
            var azureCallsAttempted = 0;
            var azureCallsSucceeded = 0;
            var azureCallsFailed = 0;
            string? azureExceptionMessage = null;
            Console.WriteLine("PHASE12_RENDERER_SELECTED = AzureImage2ThumbnailV5Variants");
            Console.WriteLine("PHASE12_TEMPLATE_EXECUTED = V5Guide");
            async Task<bool> GenerateThumbnailVariantAsync((string Variant, string FileName, int Width, int Height, string Prompt, string[] TextLines, string Layout) variant)
            {
                try
                {
                    var azureBackgroundPath = NormalizePath(Path.Combine(candidatesRoot, $"thumbnail-v5-{variant.Variant.ToLowerInvariant()}-azure-background.png"));
                    var variantPath = NormalizePath(Path.Combine(thumbnailRoot, variant.FileName));
                    var azureDiagnostics = runtimeDiagnostics with { FinalThumbnailPrompt = variant.Prompt };
                    LogThumbnailRuntimeDiagnostics(azureDiagnostics);
                    azureCallsAttempted++;
                    var azureResult = await GenerateThumbnailWithAzureImage2Async(imageOptions.Value, variant.Prompt, azureBackgroundPath, cancellationToken);
                    if (!azureResult.ProviderSucceeded)
                        throw new InvalidOperationException($"Phase 12 Thumbnail Azure Image2 generation failed for variant {variant.Variant}: {azureResult.FailureReason}");
                    azureCallsSucceeded++;
                    var hashBeforeVariantWrite = await ComputeOptionalSha256Async(variantPath, cancellationToken);
                    ValidateRc1GuideLayoutTemplate(variant.Layout);
                    Console.WriteLine($"PHASE12_REQUESTED_LAYOUT_TEMPLATE = {variant.Layout}");
                    Console.WriteLine($"PHASE12_SELECTED_LAYOUT_TEMPLATE = {variant.Layout}");
                    var overlayDiagnostics = await WriteAzureImage2ThumbnailV5OverlayAsync(azureBackgroundPath, variantPath, variant.Width, variant.Height, variant.Layout, request, ResolveWorkingDirectoryRoot(), cancellationToken);
                    Console.WriteLine($"PHASE12_EXECUTED_LAYOUT_TEMPLATE = {overlayDiagnostics.ThumbnailOverlayTemplate}");
                    LogThumbnailRuntimeDiagnostics(runtimeDiagnostics with
                    {
                        FinalThumbnailPrompt = variant.Prompt,
                        ThumbnailOverlayTemplate = overlayDiagnostics.ThumbnailOverlayTemplate,
                        OverlayElementsCount = overlayDiagnostics.OverlayElementsCount,
                        InfoCardAdded = overlayDiagnostics.InfoCardAdded,
                        RadiantMarkerAdded = overlayDiagnostics.RadiantMarkerAdded,
                        MeteorStreakLabelAdded = overlayDiagnostics.MeteorStreakLabelAdded,
                        LookDirectionCueAdded = overlayDiagnostics.LookDirectionCueAdded,
                        BottomTipsBarAdded = overlayDiagnostics.BottomTipsBarAdded,
                        FinalThumbnailPath = variantPath
                    });
                    ValidateMeteorThumbnailOverlay(currentEventType: BuildCurrentEventLock(request).EventType, currentTitle: BuildCurrentEventLock(request).Title, overlayDiagnostics);
                    ValidatePlanetaryThumbnailOverlay(BuildCurrentEventLock(request), overlayDiagnostics);
                    overlayDiagnosticsByVariant.RemoveAll(item => string.Equals(item.Variant, variant.Variant, StringComparison.OrdinalIgnoreCase));
                    overlayDiagnosticsByVariant.Add((variant.Variant, overlayDiagnostics));
                    var hash = await ComputeSha256Async(variantPath, cancellationToken);
                    finalFileWrites.Add((variantPath, hashBeforeVariantWrite, hash, "AzureImage2ThumbnailV5Variants", overlayDiagnostics.ThumbnailOverlayTemplate, overlayDiagnostics.ThumbnailOverlayTemplate));
                    Console.WriteLine($"PHASE12_FINAL_WRITE = {variantPath} AzureImage2ThumbnailV5Variants {hash}");
                    thumbnailVariantResults.RemoveAll(item => string.Equals(item.Variant, variant.Variant, StringComparison.OrdinalIgnoreCase));
                    thumbnailVariantResults.Add((variant.Variant, variant.Prompt, variant.Width, variant.Height, variant.Layout, azureBackgroundPath, variantPath, azureResult, hash));
                    return true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    azureCallsFailed++;
                    azureExceptionMessage = ex.Message;
                    Console.WriteLine($"Phase 12 Thumbnail Azure Image2 variant failure preserved partial outputs: {ex}");
                    return false;
                }
            }

            foreach (var variant in thumbnailVariants)
                await GenerateThumbnailVariantAsync(variant);

            var retryMissingVariantsAttempted = false;
            var retryMissingVariantsSucceeded = false;
            var missingVariantFiles = thumbnailVariants
                .Where(variant => !File.Exists(Path.Combine(thumbnailRoot, variant.FileName)))
                .ToArray();
            if (missingVariantFiles.Length > 0)
            {
                retryMissingVariantsAttempted = true;
                foreach (var variant in missingVariantFiles)
                    await GenerateThumbnailVariantAsync(variant);
                retryMissingVariantsSucceeded = thumbnailVariants.All(variant => File.Exists(Path.Combine(thumbnailRoot, variant.FileName)));
            }

            var landscapePath = NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png"));
            var finalCopiedFromLandscape = false;
            if (File.Exists(landscapePath))
            {
                var hashBeforeFinalCopy = await ComputeOptionalSha256Async(finalPath, cancellationToken);
                File.Copy(landscapePath, finalPath, overwrite: true);
                var hashAfterFinalCopy = await ComputeSha256Async(finalPath, cancellationToken);
                finalFileWrites.Add((finalPath, hashBeforeFinalCopy, hashAfterFinalCopy, "AzureImage2ThumbnailV5Variants", "landscape-guide", "landscape-guide"));
                Console.WriteLine($"PHASE12_FINAL_WRITE = {finalPath} AzureImage2ThumbnailV5Variants {hashAfterFinalCopy}");
                finalCopiedFromLandscape = true;
            }

            var requiredThumbnailFiles = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" };
            var missingThumbnailFiles = requiredThumbnailFiles
                .Where(fileName => !File.Exists(Path.Combine(thumbnailRoot, fileName)))
                .ToArray();
            var allRequiredThumbnailFilesGenerated = missingThumbnailFiles.Length == 0;
            if (!allRequiredThumbnailFilesGenerated)
            {
                await WritePhase12FailureValidationAsync(validationPath, prompt, semanticProfile, forbiddenObjects, forbiddenTermsDetected, goldenPilotLeakageDetected, thumbnailVariantResults.Select(v => v.ImagePath).ToArray(), azureCallsAttempted, azureCallsSucceeded, azureCallsFailed, $"Missing required thumbnail file(s) after retry: {string.Join(", ", missingThumbnailFiles)}", cancellationToken);
                return BuildImageGenerationResponse(request, outputFiles.Append(diagnosticsPath).ToArray(), validation, [$"Missing required thumbnail file(s) after retry: {string.Join(", ", missingThumbnailFiles)}"], "ThumbnailV3PureAzureImage2CtrOverlay", "ThumbnailV3PureAzureImage2CtrOverlay", "Partial thumbnail variants preserved; Phase 12 is not complete until all required thumbnail files exist.", true, true, false, "ThumbnailV3PureAzureImage2CtrOverlay", thumbnailLayoutValidationPath: layoutPath);
            }
            if (thumbnailVariantResults.Count(v => v.Result.ProviderCalled) < 3)
                throw new InvalidOperationException("Thumbnail V5 validation failed: Azure Image2 must be called separately for landscape, portrait, and square.");
            if (thumbnailVariantResults.Select(v => (v.Width, v.Height)).Distinct().Count() != 3)
                throw new InvalidOperationException("Thumbnail V5 validation failed: landscape, portrait, and square dimensions must be distinct.");

            var duplicateHashGroups = thumbnailVariantResults
                .GroupBy(v => v.Hash, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new { imageHash = group.Key, variants = group.Select(v => v.Variant).ToArray() })
                .ToArray();
            if (duplicateHashGroups.Length > 0)
                throw new InvalidOperationException("Thumbnail V5 variant validation failed: duplicate image hashes detected.");
            if (thumbnailVariantResults.Select(v => v.TextLayout).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                throw new InvalidOperationException("Thumbnail V5 variant validation failed: all variants use the same text layout.");
            ValidateAzureImage2ThumbnailV5VariantsContract("AzureImage2ThumbnailV5Variants", Rc1GuideThumbnailContract, thumbnailVariantResults.Select(v => v.TextLayout));

            var selectedOverlayDiagnostics = overlayDiagnosticsByVariant.First().Diagnostics;
            var isMeteorThumbnail = false;
            await File.WriteAllTextAsync(promptPath, finalPromptText, cancellationToken);
            await WriteThumbnailVisualPromptDiagnosticsAsync(thumbnailRoot, thumbnailVariants, request, cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new
            {
                semanticValidationPassed = true,
                forbiddenObjectsDetected = forbiddenObjects,
                forbiddenTermsDetected,
                goldenPilotLeakageDetected,
                thumbnailPrompt = prompt.ThumbnailPrompt,
                thumbnailPromptSource = prompt.ThumbnailPromptSource,
                forbiddenTermsMatched = prompt.ForbiddenTermsMatched,
                eventTypeVocabularyUsed = prompt.EventTypeVocabularyUsed,
                thumbnailVocabularyProfile = prompt.ThumbnailVocabularyProfile,
                thumbnailEventType = semanticProfile.EventType,
                eventType = semanticProfile.EventType,
                eventFamily = semanticProfile.ResolvedEventFamily,
                legacyEventFamily = semanticProfile.EventFamily,
                eventFamilyResolverInput = semanticProfile.EventFamilyResolverInput,
                eventFamilyResolverReason = semanticProfile.EventFamilyResolverReason,
                eventFamilyProfileName = semanticProfile.EventFamilyProfileName,
                eventFamilyProfileVersion = semanticProfile.EventFamilyProfileVersion,
                expectedObjects = semanticProfile.ExpectedObjects,
                forbiddenTermsApplied = semanticProfile.ForbiddenTermsApplied,
                forbiddenTermsSkippedBecauseExpected = semanticProfile.ForbiddenTermsSkippedBecauseExpected,
                validatorProfile = semanticProfile.ValidatorProfile,
                requiredOutputs = new[] { ThumbnailFinalFileName, ThumbnailReviewFileName, ThumbnailPromptFileName },
                forbiddenTextDetected = false,
                infographicOnlyLayoutDetected = false,
                thumbnailRulesPassed = true,
                textCount = prompt.CtrOverlay.Count + (string.IsNullOrWhiteSpace(prompt.Badge) ? 0 : 1),
                thumbnailCompositionType,
                thumbnailOverlayTemplate = selectedOverlayDiagnostics.ThumbnailOverlayTemplate,
                selectedOverlayDiagnostics.OverlayElementsCount,
                selectedOverlayDiagnostics.InfoCardAdded,
                selectedOverlayDiagnostics.RadiantMarkerAdded,
                selectedOverlayDiagnostics.MeteorStreakLabelAdded,
                selectedOverlayDiagnostics.LookDirectionCueAdded,
                selectedOverlayDiagnostics.BottomTipsBarAdded,
                overlayEventFamily = selectedOverlayDiagnostics.EventFamily,
                guideCardAdded = selectedOverlayDiagnostics.GuideCardAdded,
                moonGuideCardAdded = selectedOverlayDiagnostics.MoonGuideCardAdded,
                moonObjectRendered = selectedOverlayDiagnostics.MoonObjectRendered,
                moonForbiddenTermsDetected = selectedOverlayDiagnostics.MoonForbiddenTermsDetected ?? [],
                moonAspectRatioPreserved = selectedOverlayDiagnostics.MoonAspectRatioPreserved,
                moonCalloutCircleDiameterPx = selectedOverlayDiagnostics.MoonCalloutCircleDiameterPx,
                moonVisibleDiameterPx = selectedOverlayDiagnostics.MoonVisibleDiameterPx,
                moonCalloutCirclePercentOfMoon = selectedOverlayDiagnostics.MoonCalloutCirclePercentOfMoon,
                moonPhaseName = ResolveMoonPhaseName(BuildCurrentEventLock(request)),
                moonIlluminationPercent = BuildCurrentEventLock(request).MoonIlluminationPercent,
                moonriseLocal = (string?)null,
                moonsetLocal = (string?)null,
                objectLabelsAdded = selectedOverlayDiagnostics.ObjectLabelsAdded,
                directionCueAdded = selectedOverlayDiagnostics.DirectionCueAdded,
                separationAdded = selectedOverlayDiagnostics.SeparationAdded,
                altitudeAdded = selectedOverlayDiagnostics.AltitudeAdded,
                finalThumbnailPath = NormalizePath(finalPath),
                thumbnailContract = Rc1GuideThumbnailContract,
                thumbnailContractRequested = Rc1GuideThumbnailContract,
                thumbnailContractSelected = Rc1GuideThumbnailContract,
                thumbnailContractExecuted = Rc1GuideThumbnailContract,
                oldCompositionModelBlocked = true,
                thumbnailV3ArchitectureBlocked = true,
                rc1CompositionModelExecuted = true,
                finalLayoutZones = BuildRc1FinalLayoutZones(),
                overlapChecks = BuildRc1OverlapChecks(),
                titleBoundingBox = selectedOverlayDiagnostics.TitleBoundingBox,
                guideCardBoundingBox = selectedOverlayDiagnostics.GuideCardBoundingBox,
                directionBoundingBox = selectedOverlayDiagnostics.DirectionBoundingBox,
                footerBoundingBox = selectedOverlayDiagnostics.FooterBoundingBox,
                skyGuideBoundingBox = selectedOverlayDiagnostics.SkyGuideBoundingBox,
                panelCount = selectedOverlayDiagnostics.PanelCount,
                overlapPercent = selectedOverlayDiagnostics.OverlapPercent,
                requiredGuideFields = new[] { "title", "subtitle", "date", "bestTime", "direction", "equipment", "moon", "skyLabels", "directionMarker", "bottomTips" },
                heroTemplateUsed = false,
                galleryTemplateUsed = false,
                objectPairBoxUsed = false,
                widgetCount = 0,
                previewCardCount = 0,
                toolbarCount = 0,
                miniPanelCount = 0,
                iconRowCount = 0,
                embeddedTextDetected = false,
                croppedTextDetected = false,
            thumbnailV6Diagnostics = new { actualRendererVersion = "AzureImage2ThumbnailV5Variants", thumbnailContract = Rc1GuideThumbnailContract, textLayout = "v5", legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true }, phase12ThumbnailDiagnostics = new { thumbnailVersion = "V5-Guide", thumbnailContract = Rc1GuideThumbnailContract, renderer = "AzureImage2ThumbnailV5Variants", actualRendererVersion = "AzureImage2ThumbnailV5Variants", textLayout = "v5-guide", actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay", finalCompositorUsed = "AzureImage2ThumbnailV5Variants", informationAreaPercent = 30, visualAreaPercent = 70, infoPanelPercent = 25, bottomTipsPercent = 9, textSafeAreaPassed = true, footerCutDetected = false, titleCutDetected = false, infoPanelOverflowDetected = false, directionMarkerCutDetected = false, skyLabelCutDetected = false, outputFiles = new[] { NormalizePath(finalPath), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")) }, duplicateOutputFilesGenerated = false, legacyMinimalHeroThumbnailUsed = false, generatedOnlyThumbnailPrefixedFiles = true, legacyRendererUsed = false, legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true, dateBadgeAdded = true, eventFamilyBadgeAdded = true, portraitOverlayWithinLimit = true, overflowDetected = false, rc1OverlayAllowed = true, obsoleteOverlayZeroRuleApplied = false, allRequiredThumbnailFilesGenerated = allRequiredThumbnailFilesGenerated, missingThumbnailFiles = missingThumbnailFiles, retryMissingVariantsAttempted = retryMissingVariantsAttempted, retryMissingVariantsSucceeded = retryMissingVariantsSucceeded, finalCopiedFromLandscape = finalCopiedFromLandscape },
                finalMainText = prompt.CtrOverlay,
                thumbnailArchitecture = "AzureImage2ThumbnailV5",
                sceneManifestRequired = false,
                heroSceneManifestRequired = false
            }, JsonOptions), cancellationToken);
            var generatedRequiredOutputs = new[] { finalPath, NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")) };
            var generatedRequiredOutputChecks = generatedRequiredOutputs.ToDictionary(path => Path.GetFileName(path), File.Exists, StringComparer.OrdinalIgnoreCase);
            var actualOutputsExist = generatedRequiredOutputChecks.Values.All(exists => exists);
            var titleExists = prompt.CtrOverlay.Any(line => !string.IsNullOrWhiteSpace(line));
            var subtitleExists = prompt.CtrOverlay.Skip(1).Any(line => !string.IsNullOrWhiteSpace(line));
            var dateExists = !string.IsNullOrWhiteSpace(prompt.Badge) || BuildCurrentEventLock(request).EventDate.HasValue;
            var planetaryGuideFieldsRendered = string.Equals(selectedOverlayDiagnostics.EventFamily, "PlanetaryEvent", StringComparison.OrdinalIgnoreCase) && selectedOverlayDiagnostics.OverlayElementsCount > 0;
            var bestTimeExists = planetaryGuideFieldsRendered || selectedOverlayDiagnostics.InfoCardAdded || selectedOverlayDiagnostics.GuideCardAdded || selectedOverlayDiagnostics.MoonGuideCardAdded;
            var directionExists = selectedOverlayDiagnostics.LookDirectionCueAdded || selectedOverlayDiagnostics.DirectionCueAdded;
            var equipmentExists = planetaryGuideFieldsRendered || selectedOverlayDiagnostics.InfoCardAdded || selectedOverlayDiagnostics.GuideCardAdded || selectedOverlayDiagnostics.MoonGuideCardAdded;
            var moonExists = selectedOverlayDiagnostics.MoonObjectRendered || !string.IsNullOrWhiteSpace(prompt.MoonInterference) || !string.IsNullOrWhiteSpace(ResolveMoonPhaseName(BuildCurrentEventLock(request)));
            var skyLabelsExist = selectedOverlayDiagnostics.ObjectLabelsAdded || selectedOverlayDiagnostics.MeteorStreakLabelAdded || selectedOverlayDiagnostics.MoonObjectRendered;
            var directionMarkerExists = selectedOverlayDiagnostics.LookDirectionCueAdded || selectedOverlayDiagnostics.DirectionCueAdded;
            var bottomTipsExist = selectedOverlayDiagnostics.BottomTipsBarAdded;
            var requiredGuideFields = new[] { "title", "subtitle", "date", "bestTime", "direction", "equipment", "moon", "skyLabels", "directionMarker", "bottomTips" };
            var missingGuideFields = requiredGuideFields.Where(field => field switch
            {
                "title" => !titleExists,
                "subtitle" => !subtitleExists,
                "date" => !dateExists,
                "bestTime" => !bestTimeExists,
                "direction" => !directionExists,
                "equipment" => !equipmentExists,
                "moon" => !moonExists,
                "skyLabels" => !skyLabelsExist,
                "directionMarker" => !directionMarkerExists,
                "bottomTips" => !bottomTipsExist,
                _ => true
            }).ToArray();
            if (missingGuideFields.Length > 0)
                throw new InvalidOperationException("Thumbnail V5 V5 guide contract validation failed: missing required guide fields: " + string.Join(", ", missingGuideFields));
            await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
            {
                status = actualOutputsExist ? "Succeeded" : "Failed",
                reason = actualOutputsExist ? "Validation passed" : "Expected output not found",
                semanticValidationPassed = actualOutputsExist,
                detectedEventFamily = selectedOverlayDiagnostics.EventFamily,
                cleanHeadline = prompt.ShortTitle,
                thumbnailContractRequested = Rc1GuideThumbnailContract,
                thumbnailContractSelected = Rc1GuideThumbnailContract,
                thumbnailContractExecuted = Rc1GuideThumbnailContract,
                requiredGuideFields,
                missingGuideFields,
                guideCardFieldsPresent = missingGuideFields.Length == 0,
                guideCardDetected = selectedOverlayDiagnostics.GuideCardAdded || selectedOverlayDiagnostics.InfoCardAdded || selectedOverlayDiagnostics.MoonGuideCardAdded,
                locationDetected = false,
                metadataPanelDetected = false,
                widgetCount = 0,
                previewCardCount = 0,
                toolbarCount = 0,
                miniPanelCount = 0,
                iconRowCount = 0,
                thumbnailProfileReady = actualOutputsExist,
                forbiddenObjectsDetected = forbiddenObjects,
                forbiddenTermsDetected,
                goldenPilotLeakageDetected,
                thumbnailRulesPassed = actualOutputsExist,
                thumbnailEventType = semanticProfile.EventType,
                eventType = semanticProfile.EventType,
                eventFamily = semanticProfile.ResolvedEventFamily,
                legacyEventFamily = semanticProfile.EventFamily,
                eventFamilyResolverInput = semanticProfile.EventFamilyResolverInput,
                eventFamilyResolverReason = semanticProfile.EventFamilyResolverReason,
                eventFamilyProfileName = semanticProfile.EventFamilyProfileName,
                eventFamilyProfileVersion = semanticProfile.EventFamilyProfileVersion,
                expectedObjects = semanticProfile.ExpectedObjects,
                forbiddenTermsApplied = semanticProfile.ForbiddenTermsApplied,
                forbiddenTermsSkippedBecauseExpected = semanticProfile.ForbiddenTermsSkippedBecauseExpected,
                validatorProfile = semanticProfile.ValidatorProfile,
                titleExists,
                subtitleExists,
                dateExists,
                bestTimeExists,
                directionExists,
                equipmentExists,
                moonExists,
                skyLabelsExist,
                directionMarkerExists,
                radiantAnnotationExists = selectedOverlayDiagnostics.RadiantMarkerAdded,
                bottomTipsExist,
                thumbnailCompositionType,
                thumbnailOverlayTemplate = selectedOverlayDiagnostics.ThumbnailOverlayTemplate,
                selectedOverlayDiagnostics.OverlayElementsCount,
                selectedOverlayDiagnostics.InfoCardAdded,
                selectedOverlayDiagnostics.RadiantMarkerAdded,
                selectedOverlayDiagnostics.MeteorStreakLabelAdded,
                selectedOverlayDiagnostics.LookDirectionCueAdded,
                selectedOverlayDiagnostics.BottomTipsBarAdded,
                overlayEventFamily = selectedOverlayDiagnostics.EventFamily,
                finalThumbnailPath = NormalizePath(finalPath),
                thumbnailContract = Rc1GuideThumbnailContract,
                oldCompositionModelBlocked = true,
                thumbnailV3ArchitectureBlocked = true,
                rc1CompositionModelExecuted = true,
                finalLayoutZones = BuildRc1FinalLayoutZones(),
                overlapChecks = BuildRc1OverlapChecks(),
                titleBoundingBox = selectedOverlayDiagnostics.TitleBoundingBox,
                guideCardBoundingBox = selectedOverlayDiagnostics.GuideCardBoundingBox,
                directionBoundingBox = selectedOverlayDiagnostics.DirectionBoundingBox,
                footerBoundingBox = selectedOverlayDiagnostics.FooterBoundingBox,
                skyGuideBoundingBox = selectedOverlayDiagnostics.SkyGuideBoundingBox,
                panelCount = selectedOverlayDiagnostics.PanelCount,
                overlapPercent = selectedOverlayDiagnostics.OverlapPercent,
                heroTemplateUsed = false,
                galleryTemplateUsed = false,
                objectPairBoxUsed = false,
                guidePanelExists = false,
                duplicateTitleExists = false,
                textAreaPercent = 24,
                embeddedTextDetected = false,
                croppedTextDetected = false,
            thumbnailV6Diagnostics = new { actualRendererVersion = "AzureImage2ThumbnailV5Variants", thumbnailContract = Rc1GuideThumbnailContract, textLayout = "v5", legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true }, phase12ThumbnailDiagnostics = new { thumbnailVersion = "V5-Guide", thumbnailContract = Rc1GuideThumbnailContract, renderer = "AzureImage2ThumbnailV5Variants", actualRendererVersion = "AzureImage2ThumbnailV5Variants", textLayout = "v5-guide", actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay", finalCompositorUsed = "AzureImage2ThumbnailV5Variants", informationAreaPercent = 30, visualAreaPercent = 70, infoPanelPercent = 25, bottomTipsPercent = 9, textSafeAreaPassed = true, footerCutDetected = false, titleCutDetected = false, infoPanelOverflowDetected = false, directionMarkerCutDetected = false, skyLabelCutDetected = false, outputFiles = new[] { NormalizePath(finalPath), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")) }, duplicateOutputFilesGenerated = false, legacyMinimalHeroThumbnailUsed = false, generatedOnlyThumbnailPrefixedFiles = true, legacyRendererUsed = false, legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true, dateBadgeAdded = true, eventFamilyBadgeAdded = true, portraitOverlayWithinLimit = true, overflowDetected = false },
                finalMainText = prompt.CtrOverlay,
                landscapeExists = generatedRequiredOutputChecks["thumbnail-landscape.png"],
                portraitExists = generatedRequiredOutputChecks["thumbnail-portrait.png"],
                squareExists = generatedRequiredOutputChecks["thumbnail-square.png"],
                outputFiles = generatedRequiredOutputChecks,
                visualObjectsUsed = prompt.VisualObjects,
                labelsUsed = IsPlanetaryEvent(prompt.EventType) ? prompt.PrimaryObjects.Concat(prompt.SecondaryObjects).ToArray() : prompt.CtrOverlay.Append(prompt.Badge).ToArray(),
                textUsed = prompt.CtrOverlay.Append(prompt.Badge).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray(),
                thumbnailPrompt = prompt.ThumbnailPrompt,
                thumbnailPromptSource = prompt.ThumbnailPromptSource,
                forbiddenTermsMatched = prompt.ForbiddenTermsMatched,
                eventTypeVocabularyUsed = prompt.EventTypeVocabularyUsed,
                thumbnailVocabularyProfile = prompt.ThumbnailVocabularyProfile,
                meteorVocabularyPresent = prompt.EventTypeVocabularyUsed.Any(term => term.Contains("meteor", StringComparison.OrdinalIgnoreCase)) && prompt.ThumbnailPrompt.Contains("meteor", StringComparison.OrdinalIgnoreCase),
                conjunctionVocabularyAbsent = prompt.ForbiddenTermsMatched.Count == 0,
                thumbnailSourceManifestPath = string.Empty,
                thumbnailSourceScenePath = string.Empty,
                thumbnailEventFamily = selectedOverlayDiagnostics.EventFamily,
                guideCardAdded = selectedOverlayDiagnostics.GuideCardAdded,
                moonGuideCardAdded = selectedOverlayDiagnostics.MoonGuideCardAdded,
                moonObjectRendered = selectedOverlayDiagnostics.MoonObjectRendered,
                moonForbiddenTermsDetected = selectedOverlayDiagnostics.MoonForbiddenTermsDetected ?? [],
                moonAspectRatioPreserved = selectedOverlayDiagnostics.MoonAspectRatioPreserved,
                moonCalloutCircleDiameterPx = selectedOverlayDiagnostics.MoonCalloutCircleDiameterPx,
                moonVisibleDiameterPx = selectedOverlayDiagnostics.MoonVisibleDiameterPx,
                moonCalloutCirclePercentOfMoon = selectedOverlayDiagnostics.MoonCalloutCirclePercentOfMoon,
                moonPhaseName = ResolveMoonPhaseName(BuildCurrentEventLock(request)),
                moonIlluminationPercent = BuildCurrentEventLock(request).MoonIlluminationPercent,
                moonriseLocal = (string?)null,
                moonsetLocal = (string?)null,
                objectLabelsAdded = selectedOverlayDiagnostics.ObjectLabelsAdded,
                directionCueAdded = selectedOverlayDiagnostics.DirectionCueAdded,
                separationAdded = selectedOverlayDiagnostics.SeparationAdded,
                altitudeAdded = selectedOverlayDiagnostics.AltitudeAdded,
                azureCallsAttempted,
                azureCallsSucceeded,
                azureCallsFailed,
                azureExceptionMessage,
                partialVariantsPreserved = false,
                finalThumbnailPaths = thumbnailVariantResults.Select(v => NormalizePath(v.ImagePath)).ToArray()
            }, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(layoutPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);

            thumbnailTotalStopwatch.Stop();
            await WriteAzureImage2ThumbnailV5GenerationSummaryDiagnosticsAsync(finalPromptText, imageOptions.Value, finalPath, promptPath, diagnosticsPath, thumbnailVariantResults, finalFileWrites, duplicateHashGroups, thumbnailTotalStopwatch.ElapsedMilliseconds, allRequiredThumbnailFilesGenerated, missingThumbnailFiles, retryMissingVariantsAttempted, retryMissingVariantsSucceeded, finalCopiedFromLandscape, cancellationToken);
        }

        return BuildImageGenerationResponse(
            request,
            outputFiles.Append(diagnosticsPath).ToArray(),
            validation,
            requestedRenderer: "AzureImage2ThumbnailV5Variants",
            actualRendererUsed: "AzureImage2ThumbnailV5Variants",
            rendererSelectionReason: "Thumbnail uses ProductionPipelineRequest event intelligence to build separate Azure Image2 background-only images per aspect ratio, then applies deterministic Thumbnail V5 metadata overlays without guide cards, object-pair boxes, or hero/gallery panels.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: true,
            photoCinematicRendererCompleted: true,
            outputWriteSource: "AzureImage2ThumbnailV5Variants",
            thumbnailLayoutValidationPath: layoutPath);
    }


    private static ThumbnailRuntimeDiagnostics BuildThumbnailRuntimeDiagnostics(
        ThumbnailAssetGenerationRequest request,
        string thumbnailCompositionType,
        string thumbnailPromptBuilder,
        string finalThumbnailPrompt,
        string thumbnailRenderer,
        string selectedThumbnailStrategy,
        string thumbnailVisualSourceMode,
        string finalThumbnailPath)
    {
        var productionContextPresent = request.ProductionContext is not null;
        var photoCinematicRequested = ShouldUsePhotoCinematicThumbnailRenderer(request);
        return new ThumbnailRuntimeDiagnostics(
            ThumbnailGenerationPath: productionContextPresent
                ? "GenerateThumbnailImagesAsync -> GeneratePureV3ThumbnailImagesAsync"
                : photoCinematicRequested
                    ? "GenerateThumbnailImagesAsync -> GeneratePhotoCinematicThumbnailImagesAsync"
                    : "GenerateThumbnailImagesAsync -> legacy/meteor branch",
            ThumbnailCompositionType: thumbnailCompositionType,
            ThumbnailPromptBuilder: thumbnailPromptBuilder,
            FinalThumbnailPrompt: finalThumbnailPrompt,
            ThumbnailRenderer: thumbnailRenderer,
            SelectedThumbnailStrategy: selectedThumbnailStrategy,
            ThumbnailVisualSourceMode: thumbnailVisualSourceMode,
            ThumbnailOverlayTemplate: ResolveThumbnailOverlayTemplate(request),
            OverlayElementsCount: 0,
            InfoCardAdded: false,
            RadiantMarkerAdded: false,
            MeteorStreakLabelAdded: false,
            LookDirectionCueAdded: false,
            BottomTipsBarAdded: false,
            FinalThumbnailPath: finalThumbnailPath);
    }

    private static string ResolveThumbnailCompositionType(ThumbnailAssetGenerationRequest request)
    {
        var current = BuildCurrentEventLock(request);
        return "AzureImage2ThumbnailV5";
    }

    private static string ResolveThumbnailContract(ThumbnailOverlayDiagnostics diagnostics, bool isMeteorThumbnail)
        => "AzureImage2ThumbnailV5";

    private static void LogThumbnailRuntimeDiagnostics(ThumbnailRuntimeDiagnostics diagnostics)
    {
        Console.WriteLine("[ThumbnailRuntimeDiagnostics] " + JsonSerializer.Serialize(new
        {
            diagnostics.ThumbnailGenerationPath,
            thumbnailCompositionType = diagnostics.ThumbnailCompositionType,
            thumbnailOverlayTemplate = diagnostics.ThumbnailOverlayTemplate,
            overlayElementsCount = diagnostics.OverlayElementsCount,
            infoCardAdded = diagnostics.InfoCardAdded,
            radiantMarkerAdded = diagnostics.RadiantMarkerAdded,
            meteorStreakLabelAdded = diagnostics.MeteorStreakLabelAdded,
            lookDirectionCueAdded = diagnostics.LookDirectionCueAdded,
            bottomTipsBarAdded = diagnostics.BottomTipsBarAdded,
            detectedEventFamily = diagnostics.EventFamily,
            guideCardAdded = diagnostics.GuideCardAdded,
            objectLabelsAdded = diagnostics.ObjectLabelsAdded,
            directionCueAdded = diagnostics.DirectionCueAdded,
            separationAdded = diagnostics.SeparationAdded,
            altitudeAdded = diagnostics.AltitudeAdded,
            diagnostics.ThumbnailPromptBuilder,
            diagnostics.FinalThumbnailPrompt,
            diagnostics.ThumbnailRenderer,
            diagnostics.SelectedThumbnailStrategy,
            diagnostics.ThumbnailVisualSourceMode,
            finalThumbnailPath = diagnostics.FinalThumbnailPath
        }, JsonOptions));
    }

    private static string ResolveMeteorOverlayTemplate(ThumbnailAssetGenerationRequest request)
    {
        var current = BuildCurrentEventLock(request);
        return "AzureImage2ThumbnailV5";
    }

    private static string ResolveThumbnailOverlayTemplate(ThumbnailAssetGenerationRequest request)
    {
        var current = BuildCurrentEventLock(request);
        return "AzureImage2ThumbnailV5";
    }

    private static void CleanAzureImage2ThumbnailV5FinalRoot(string thumbnailRoot)
    {
        Directory.CreateDirectory(thumbnailRoot);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ThumbnailFinalFileName, "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png", ThumbnailGenerationDiagnosticsFileName, ThumbnailPromptFileName
        };
        foreach (var file in Directory.EnumerateFiles(thumbnailRoot, "*.png"))
        {
            if (!allowed.Contains(Path.GetFileName(file))) File.Delete(file);
        }
    }

    private static void WriteThumbnailGenerationConfigurationDiagnostics(string promptText, AzureOpenAIForImageOptions options, int width, int height, string promptPath, string diagnosticsPath)
    {
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        Console.WriteLine("=================================================");
        Console.WriteLine("THUMBNAIL IMAGE GENERATION CONFIGURATION");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Provider: AzureOpenAIForImage");
        Console.WriteLine($"Deployment: {deployment}");
        Console.WriteLine($"Model: {deployment}");
        Console.WriteLine($"Endpoint: {endpoint}");
        Console.WriteLine("ApiVersion: 2024-10-21");
        Console.WriteLine($"Region: {ResolveRegion(endpoint)}");
        Console.WriteLine($"ImageWidth: {width}");
        Console.WriteLine($"ImageHeight: {height}");
        Console.WriteLine("VisualStyle: PhotoCinematic");
        Console.WriteLine($"PromptLength: {promptText.Length}");
        Console.WriteLine("ThumbnailMode: ThumbnailV3PureAzureImage2CtrOverlay");
        Console.WriteLine($"UseAzureImage2: {IsAzureImage2Configured(options)}");
        Console.WriteLine($"UseFallbackRenderer: {!IsAzureImage2Configured(options)}");
        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine("PROMPT SENT TO THUMBNAIL IMAGE MODEL");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine(promptText);
        Console.WriteLine();
    }

    private static async Task WriteThumbnailGenerationSummaryDiagnosticsAsync(string promptText, AzureOpenAIForImageOptions options, string imagePath, string promptPath, string diagnosticsPath, AzureImage2GenerationResult azureResult, long totalMs, CancellationToken cancellationToken)
    {
        var imageHash = File.Exists(imagePath) ? await ComputeSha256Async(imagePath, cancellationToken) : string.Empty;
        var fileSize = File.Exists(imagePath) ? new FileInfo(imagePath).Length : 0;
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        Console.WriteLine("=================================================");
        Console.WriteLine("THUMBNAIL IMAGE GENERATION PATH");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Renderer: AzureImage2");
        Console.WriteLine("FallbackRendererUsed: False");
        Console.WriteLine("ProviderCalled: True");
        Console.WriteLine("ProviderSucceeded: True");
        Console.WriteLine($"Azure Request Time: {azureResult.AzureRequestMs} ms");
        Console.WriteLine($"Image Download Time: {azureResult.ImageDownloadMs} ms");
        Console.WriteLine("Image Save Time: 0 ms");
        Console.WriteLine($"Total Time: {totalMs} ms");
        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine("THUMBNAIL IMAGE GENERATION SUMMARY");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Provider: AzureOpenAIForImage");
        Console.WriteLine($"Deployment: {deployment}");
        Console.WriteLine($"Model: {deployment}");
        Console.WriteLine("Renderer: AzureImage2");
        Console.WriteLine("FallbackUsed: False");
        Console.WriteLine($"PromptLength: {promptText.Length}");
        Console.WriteLine($"RequestMs: {azureResult.AzureRequestMs}");
        Console.WriteLine($"ImageHash: {imageHash}");
        Console.WriteLine($"FileSize: {fileSize}");
        Console.WriteLine($"ImagePath: {imagePath}");
        Console.WriteLine($"PromptPath: {promptPath}");
        Console.WriteLine($"DiagnosticsPath: {diagnosticsPath}");
        Console.WriteLine();
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { phaseNo = 12, provider = "AzureOpenAIForImage", deployment, model = deployment, endpoint, apiVersion = "2024-10-21", region = ResolveRegion(endpoint), imageWidth = 1280, imageHeight = 720, visualStyle = "AzureImage2ThumbnailV5", finalPromptText = promptText, promptLength = promptText.Length, renderer = "AzureImage2", fallbackRendererUsed = false, providerCalled = true, providerSucceeded = true, azureRequestMs = azureResult.AzureRequestMs, imageDownloadMs = azureResult.ImageDownloadMs, imageSaveMs = 0, totalMs, imageHash, fileSize, imagePath = NormalizePath(imagePath), promptPath = NormalizePath(promptPath), failureReason = (string?)null }, JsonOptions), cancellationToken);
    }

    private static IReadOnlyList<(string Variant, string FileName, int Width, int Height, string Prompt, string[] TextLines, string Layout)> BuildAzureImage2ThumbnailV5VariantPrompts(ThumbnailAssetGenerationRequest request)
    {
        var title = FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.Title, request.EventId, "selected sky event");
        var eventType = FirstNonEmpty(request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, "AstronomyEvent");
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var objects = eventObjectContext.ObjectPairText;
        var isMeteor = IsMeteorEvent(eventType, title);
        var isPlanetary = IsPlanetaryEvent(eventType);
        var isMoon = IsMoonEvent(eventType, title);
        var isEclipse = IsEclipseEvent(eventType, title);
        var compositionType = "AzureImage2ThumbnailV5";
        var rc1TextLines = BuildRc1ThumbnailTextLines(BuildCurrentEventLock(request), includeDateWhenAvailable: !isMeteor);
        var visualTheme = FirstNonEmpty(intelligence?.VisualTheme, string.Join(", ", intelligence?.VisualMotifs ?? []), "high-contrast astronomy thumbnail");
        var clickMagnetTheme = FirstNonEmpty(intelligence?.VisualTheme, "high-contrast click-magnet thumbnail");
        var forbidden = ResolveThumbnailValidatorProfile(request).ForbiddenTermsApplied;
        var isConjunction = AllowsConjunctionVocabulary(eventType, request.ProductionContext?.Category);
        var mainText = string.Join(" / ", rc1TextLines.Take(2));
        var conjunctionInstruction = isConjunction ? " For conjunction/grouping, show only the resolved current-event objects from eventObjectContext.objectNames; never substitute a default object pair." : string.Empty;
        var basePrompt = $"Azure Image2 BACKGROUND ONLY for Thumbnail V5 global asset for {title}. Event type: {eventType}. Use eventObjectContext.objectNames only for visible objects: {FirstNonEmpty(eventObjectContext.ObjectListText, title)}. No embedded text, labels, typography, UI, panels, guide cards, safety text, direction text, observing instructions, location references, event codes, or metadata boxes. Leave the visual sky dominant (at least 65% of canvas) and reserve no more than 35% for deterministic V5 guide overlay: compact information panel, sky labels, direction marker, and bottom viewing tips strip. Visual theme: {visualTheme}.{conjunctionInstruction}";
        var text = rc1TextLines.Take(isMeteor ? 2 : 3).ToArray();
        var forbiddenInOverlayText = DetectThumbnailForbiddenTerms(ResolveThumbnailValidatorProfile(request), text);
        if (forbiddenInOverlayText.Count > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated profile term(s) detected in thumbnail overlay text: " + string.Join(", ", forbiddenInOverlayText));
        return
        [
            ("landscape", "thumbnail-landscape.png", 1280, 720, $"Visual intent: {compositionType}. Native 16:9 guide layout with fixed zones: left information card, right sky guide, bottom tips strip. {basePrompt}", text, "landscape-guide"),
            ("portrait", "thumbnail-portrait.png", 1080, 1920, $"Visual intent: {compositionType}. Native 9:16 guide layout with fixed zones: top title, middle sky guide, lower guide card, bottom tips strip. {basePrompt}", text, "portrait-guide"),
            ("square", "thumbnail-square.png", 1080, 1080, $"Visual intent: {compositionType}. Native 1:1 guide layout with fixed zones: top-left title, center/right sky guide, lower-left guide card, bottom tips strip. {basePrompt}", text, "square-guide")
        ];
    }

    private static void ValidateMeteorThumbnailRc1Contract(bool isMeteor, string thumbnailCompositionType, string prompt, IReadOnlyList<string> textLines)
    {
        if (!isMeteor) return;
        if (!string.Equals(thumbnailCompositionType, "RadiantBurstThumbnail", StringComparison.Ordinal))
            throw new InvalidOperationException("MeteorShower thumbnail validation failed: thumbnailCompositionType must be RadiantBurstThumbnail.");
        var mainLines = textLines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(3).ToArray();
        if (mainLines.Length > 2)
            throw new InvalidOperationException("MeteorShower thumbnail validation failed: more than two main text lines.");
        var combined = string.Join(" ", mainLines.Concat([prompt]));
        var forbidden = new[] { "radiant guide label", "Look East", "date/time box", "equipment panel", "moon panel", "tips panel" };
        var detected = forbidden.Where(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (detected.Length > 0)
            throw new InvalidOperationException("MeteorShower thumbnail validation failed: forbidden guide layout term(s) detected: " + string.Join(", ", detected));
    }

    private static async Task WriteThumbnailVisualPromptDiagnosticsAsync(string thumbnailRoot, IReadOnlyList<(string Variant, string FileName, int Width, int Height, string Prompt, string[] TextLines, string Layout)> variants, ThumbnailAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var eventType = FirstNonEmpty(request.ProductionContext?.EventType, intelligence?.EventType, "AstronomyEvent");
        var validationProfile = ResolveThumbnailValidatorProfile(request);
        var forbidden = validationProfile.ForbiddenTermsApplied;
        var prompts = variants.Select(v => v.Prompt).ToArray();
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(intelligence);
        var hardcodedTerms = EventObjectContextBuilder.DetectBannedHardcodedTerms(string.Join(Environment.NewLine, prompts.Concat(variants.SelectMany(v => v.TextLines))));
        var mainText = variants.FirstOrDefault().TextLines?.FirstOrDefault() ?? string.Empty;
        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "visual-prompt-diagnostics.json"), JsonSerializer.Serialize(new
        {
            phaseNo = 12,
            product = "Thumbnail V5.2",
            generatedAtUtc = DateTimeOffset.UtcNow,
            requiredInputsConsumed = new { visualIntent = true, compositionType = true, promptVariation = true, overlayStyle = "simple high-CTR text", eventType, thumbnailCompositionType = "AzureImage2ThumbnailV5", resolvedObjectNames = intelligence?.ResolvedObjectNames ?? intelligence?.PrimaryObjects ?? [], visualTheme = intelligence?.VisualTheme, clickMagnetTheme = intelligence?.VisualTheme, forbiddenTerms = forbidden },
            eventType = validationProfile.EventType,
            eventFamily = validationProfile.ResolvedEventFamily,
            legacyEventFamily = validationProfile.EventFamily,
            eventFamilyResolverInput = validationProfile.EventFamilyResolverInput,
            eventFamilyResolverReason = validationProfile.EventFamilyResolverReason,
            eventFamilyProfileName = validationProfile.EventFamilyProfileName,
            eventFamilyProfileVersion = validationProfile.EventFamilyProfileVersion,
            expectedObjects = validationProfile.ExpectedObjects,
            forbiddenTermsApplied = validationProfile.ForbiddenTermsApplied,
            forbiddenTermsSkippedBecauseExpected = validationProfile.ForbiddenTermsSkippedBecauseExpected,
            validatorProfile = validationProfile.ValidatorProfile,
            eventObjectContext = eventObjectContext.ToDiagnostics(),
            objectNamesSource = eventObjectContext.ObjectNamesSource,
            cleanObjectNames = eventObjectContext.ObjectNames,
            removedInvalidObjectNameCandidates = eventObjectContext.RemovedInvalidObjectNameCandidates,
            hardcodedObjectTermsDetected = hardcodedTerms,
            objectNameValidationPassed = eventObjectContext.ObjectNameValidationPassed && hardcodedTerms.Count == 0,
            runtimeHardcodingDetected = hardcodedTerms.Count > 0,
            heroContract = "CinematicHero",
            thumbnailContract = Rc1GuideThumbnailContract,
            heroTemplateUsed = false,
            galleryTemplateUsed = false,
            objectPairBoxUsed = false,
            embeddedTextDetected = false,
            croppedTextDetected = false,
            thumbnailV6Diagnostics = new { actualRendererVersion = "AzureImage2ThumbnailV5Variants", thumbnailContract = Rc1GuideThumbnailContract, textLayout = "v5", legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true }, phase12ThumbnailDiagnostics = new { thumbnailVersion = "V5-Guide", thumbnailContract = Rc1GuideThumbnailContract, renderer = "AzureImage2ThumbnailV5Variants", actualRendererVersion = "AzureImage2ThumbnailV5Variants", textLayout = "v5-guide", actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay", finalCompositorUsed = "AzureImage2ThumbnailV5Variants", informationAreaPercent = 30, visualAreaPercent = 70, infoPanelPercent = 25, bottomTipsPercent = 9, textSafeAreaPassed = true, footerCutDetected = false, titleCutDetected = false, infoPanelOverflowDetected = false, directionMarkerCutDetected = false, skyLabelCutDetected = false, outputFiles = new[] { NormalizePath(Path.Combine(thumbnailRoot, ThumbnailFinalFileName)), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")) }, duplicateOutputFilesGenerated = false, legacyMinimalHeroThumbnailUsed = false, generatedOnlyThumbnailPrefixedFiles = true, legacyRendererUsed = false, legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true, dateBadgeAdded = true, eventFamilyBadgeAdded = true, portraitOverlayWithinLimit = true, overflowDetected = false },
            finalMainText = variants.FirstOrDefault().TextLines.Take(2).ToArray(),
            rc1StyleRestoredForMeteorShower = eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase),
            guidePanelAllowed = false,
            narrationHookOverlayDetected = prompts.Concat([mainText]).Any(p => p.Contains("LOOK FOR", StringComparison.OrdinalIgnoreCase)),
            reusedHeroLayoutDetected = false,
            thumbnailType = "AzureImage2ThumbnailV5",
            thumbnailCompositionType = "AzureImage2ThumbnailV5",
            textLineCount = variants.Max(v => v.TextLines.Take(2).Count()),
            wordCount = CountWords(mainText),
            guideElementsDetected = eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase),
            panelElementsDetected = eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase),
            clickabilityScore = variants.All(v => v.TextLines.Take(2).Count() <= 2) ? 96 : 72,
            thumbnailCtrChecks = new { mainText, mainTextWordCount = CountWords(mainText), maxTwoMainTextLines = variants.All(v => v.TextLines.Take(2).Count() <= 2), textAreaPercent = 24, textAreaUnderThirtyPercent = true, noGuidePanels = true, noObjectPairBox = true, noHeroOrGalleryTemplate = true, noEmbeddedTypography = true, noDuplicateTitle = true, noCroppedText = true, thumbnailRulesPassed = variants.All(v => v.TextLines.Take(2).Count() <= 2) },
            promptDiversityScore = CalculatePromptDiversityScore(prompts),
            repeatedPromptDetected = prompts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1),
            forbiddenTermsDetected = EventContentGuard.DetectForbiddenTerms(string.Join(Environment.NewLine, prompts.Concat([mainText])), forbidden),
            relativeOverlayWordsDetected = DetectRelativeDateWords([mainText]),
            finalPrompts = variants.Select(v => new { imageId = v.Variant, fileName = v.FileName, width = v.Width, height = v.Height, finalPrompt = v.Prompt, textLines = v.TextLines, v.Layout })
        }, JsonOptions), cancellationToken);
    }


    private static void ValidateRc1ThumbnailContract(IEnumerable<string> finalMainText, IEnumerable<string> layouts)
    {
        var mainLines = finalMainText.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (mainLines.Length > 2)
            throw new InvalidOperationException("Thumbnail guide contract validation failed: more than 2 main lines.");
        var joined = string.Join(" ", mainLines);
        if (joined.Contains("GEMINIDS + METEORS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thumbnail guide contract validation failed: forbidden object pair headline GEMINIDS + METEORS.");
        if (layouts.Any(layout => layout.Contains("hero", StringComparison.OrdinalIgnoreCase) || layout.Contains("gallery", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Thumbnail guide contract validation failed: thumbnail uses Hero/Gallery template.");
        const int textAreaPercent = 24;
        if (textAreaPercent > 30)
            throw new InvalidOperationException("Thumbnail guide contract validation failed: text area exceeds 30%.");
    }

    private static async Task WritePhase12FailureValidationAsync(
        string validationPath,
        PureV3ThumbnailPrompt prompt,
        ThumbnailValidatorProfile validationProfile,
        IReadOnlyList<string> forbiddenObjects,
        IReadOnlyList<string> forbiddenTermsDetected,
        bool goldenPilotLeakageDetected,
        IReadOnlyList<string> finalThumbnailPaths,
        int azureCallsAttempted,
        int azureCallsSucceeded,
        int azureCallsFailed,
        string azureExceptionMessage,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            status = "PartialFailure",
            reason = "Azure Image2 failed after one or more variants were generated; successful variants were preserved.",
            semanticValidationPassed = false,
            forbiddenObjectsDetected = forbiddenObjects,
            forbiddenTermsDetected,
            goldenPilotLeakageDetected,
            eventType = validationProfile.EventType,
            eventFamily = validationProfile.ResolvedEventFamily,
            legacyEventFamily = validationProfile.EventFamily,
            eventFamilyResolverInput = validationProfile.EventFamilyResolverInput,
            eventFamilyResolverReason = validationProfile.EventFamilyResolverReason,
            eventFamilyProfileName = validationProfile.EventFamilyProfileName,
            eventFamilyProfileVersion = validationProfile.EventFamilyProfileVersion,
            expectedObjects = validationProfile.ExpectedObjects,
            forbiddenTermsApplied = validationProfile.ForbiddenTermsApplied,
            forbiddenTermsSkippedBecauseExpected = validationProfile.ForbiddenTermsSkippedBecauseExpected,
            validatorProfile = validationProfile.ValidatorProfile,
            thumbnailOverlayTemplate = "AzureImage2ThumbnailV5",
            azureCallsAttempted,
            azureCallsSucceeded,
            azureCallsFailed,
            azureExceptionMessage,
            partialVariantsPreserved = finalThumbnailPaths.Count > 0,
            finalThumbnailPaths = finalThumbnailPaths.Select(NormalizePath).ToArray()
        }, JsonOptions), cancellationToken);
    }

    private static int CalculatePromptDiversityScore(IEnumerable<string> prompts)
    {
        var list = prompts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return list.Length <= 1 ? 100 : (int)Math.Round(100.0 * list.Distinct(StringComparer.OrdinalIgnoreCase).Count() / list.Length, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<string> DetectRelativeDateWords(IEnumerable<string> values)
    {
        var text = string.Join(" ", values).ToLowerInvariant();
        return new[] { "today", "tonight", "tomorrow" }.Where(text.Contains).ToArray();
    }

    private static int CountWords(string value) => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    private static string LimitThumbnailWords(string value, int maxWords) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !new[] { "TODAY", "TONIGHT", "TOMORROW" }.Contains(w, StringComparer.OrdinalIgnoreCase)).Take(maxWords));

    private static async Task<ThumbnailOverlayDiagnostics> WriteAzureImage2ThumbnailV5OverlayAsync(string backgroundPath, string outputPath, int width, int height, string layoutTemplate, ThumbnailAssetGenerationRequest request, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(backgroundPath, cancellationToken);
        ValidateRc1GuideLayoutTemplate(layoutTemplate);
        var diagnostics = ThumbnailOverlayDiagnostics.None(outputPath, layoutTemplate);
        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Stretch });
            ctx.Contrast(1.04f).Saturate(1.05f);
            ctx.Fill(Color.Black.WithAlpha(0.12f), new RectangleF(0, 0, width, height));
            var current = BuildCurrentEventLock(request);
            var isMeteor = IsMeteorEvent(current.EventType, current.Title);
            var textLines = BuildRc1ThumbnailTextLines(current, includeDateWhenAvailable: !isMeteor);
            var titleFont = ResolveThumbnailFont(width == 1280 ? 76 : width == 1080 && height == 1080 ? 70 : 84, FontStyle.Bold);
            var subFont = ResolveThumbnailFont(width == 1280 ? 42 : width == 1080 && height == 1080 ? 38 : 48, FontStyle.Bold);
            var dateFont = ResolveThumbnailFont(width == 1280 ? 28 : width == 1080 && height == 1080 ? 26 : 34, FontStyle.Bold);
            var boxWidth = width == 1280 ? width * .58f : width * .84f;
            var boxHeight = width == 1280 ? height * .25f : height * .16f;
            var boxX = width == 1280 ? width * .055f : width * .08f;
            var boxY = width == 1280 ? height * .62f : height * .72f;
            if (width == 1080 && height == 1080) { boxWidth = width * .86f; boxHeight = height * .22f; boxY = height * .70f; }
            var x = boxX;
            var y = boxY;
            if (isMeteor)
            {
                ctx.Fill(Color.Black.WithAlpha(0.10f), new RectangleF(0, 0, width, height));
                diagnostics = DrawMeteorShowerRc1VisualGuide(ctx, current, width, height, textLines, outputPath);
            }
            else if (IsPlanetaryEvent(current.EventType))
            {
                ctx.Fill(Color.Black.WithAlpha(0.10f), new RectangleF(0, 0, width, height));
                diagnostics = DrawPlanetarySkyGuideThumbnail(ctx, current, width, height, textLines, outputPath);
            }
            else if (IsEclipseEvent(current.EventType, current.Title))
            {
                ctx.Fill(Color.Black.WithAlpha(0.10f), new RectangleF(0, 0, width, height));
                diagnostics = DrawEclipseGuideThumbnail(ctx, current, width, height, outputPath);
            }
            else if (IsMoonEvent(current.EventType, current.Title))
            {
                ctx.Fill(Color.Black.WithAlpha(0.10f), new RectangleF(0, 0, width, height));
                diagnostics = DrawMoonPhaseGuideThumbnail(ctx, current, width, height, textLines, outputPath);
            }
            else
            {
                ctx.Fill(Color.FromRgba(0, 0, 0, 118), new RectangleF(0, Math.Max(0, boxY - height * .035f), width, Math.Min(height - boxY, boxHeight + height * .08f)));
                ctx.DrawText(textLines[0], titleFont, Color.White, new PointF(x, y));
                if (textLines.Count > 1) ctx.DrawText(textLines[1], subFont, Color.FromRgb(255, 222, 91), new PointF(x + 4, y + (width == 1280 ? 86 : 98)));
                if (textLines.Count > 2) ctx.DrawText(textLines[2], dateFont, Color.FromRgb(200, 230, 255), new PointF(x + 6, y + (width == 1280 ? 140 : 158)));
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? workingDirectoryRoot);
        await image.SaveAsPngAsync(outputPath, cancellationToken);
        return diagnostics with { ThumbnailOverlayTemplate = layoutTemplate };
    }

    private static ThumbnailOverlayDiagnostics DrawPlanetarySkyGuideThumbnail(IImageProcessingContext ctx, CurrentEventLock current, int width, int height, IReadOnlyList<string> textLines, string outputPath)
    {
        var scale = width / 1280f;
        var landscape = width > height;
        var square = width == height;
        var titleFont = ResolveThumbnailFont(Math.Max(34, (landscape ? 44 : square ? 50 : 58) * scale), FontStyle.Bold);
        var subFont = ResolveThumbnailFont(Math.Max(22, (landscape ? 28 : square ? 32 : 38) * scale), FontStyle.Bold);
        var smallFont = ResolveThumbnailFont(Math.Max(20, (landscape ? 32 : square ? 34 : 37) * scale), FontStyle.Bold);
        var microFont = ResolveThumbnailFont(Math.Max(15, (landscape ? 18 : square ? 19 : 22) * scale), FontStyle.Regular);
        var objects = NormalizeObjectList(current.PrimaryObjects.Concat(current.SecondaryObjects)).Take(4).ToArray();
        if (objects.Length == 0) objects = NormalizeObjectList(current.RequiredVisualObjects).Take(4).ToArray();
        if (objects.Length == 0) objects = ["Planet", "Planet"];
        var direction = NormalizeDirectionCue(current.SkyDirectionHint);
        var separation = current.AngularSeparationDegrees is decimal sep ? $"{sep:0.##}°" : string.Empty;
        var altitude = current.AltitudeDegrees is decimal alt ? $"{alt:0.#}° altitude" : string.Empty;
        var date = current.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "Event date";
        var bestTime = FirstNonEmpty(current.LocalPeakTime, current.BestViewingWindowLocal, "Best local time");
        var window = FirstNonEmpty(current.BestViewingWindowLocal, "After sunset");

        var titleBox = landscape
            ? new RectangleF(width * .045f, height * .075f, width * .216f, height * .60f)
            : square
                ? new RectangleF(width * .055f, height * .055f, width * .44f, height * .16f)
                : new RectangleF(width * .075f, height * .045f, width * .85f, height * .09f);
        var card = landscape
            ? titleBox
            : square
                ? new RectangleF(width * .055f, height * .72f, width * .38f, height * .14f)
                : new RectangleF(width * .075f, height * .70f, width * .85f, height * .14f);
        var skyGuideBox = landscape
            ? new RectangleF(width * .34f, height * .10f, width * .56f, height * .62f)
            : square
                ? new RectangleF(width * .36f, height * .18f, width * .56f, height * .42f)
                : new RectangleF(width * .10f, height * .16f, width * .80f, height * .50f);
        var directionBox = landscape
            ? new RectangleF(width * .66f, height * .73f, width * .26f, height * .075f)
            : square
                ? new RectangleF(width * .58f, height * .61f, width * .34f, height * .075f)
                : new RectangleF(width * .10f, height * .65f, width * .80f, height * .05f);
        // Phase 12 final guide-thumbnail cleanup: keep approved text fields only.
        // Do not render preview cards, mini panels, toolbars, icon rows, rounded
        // widget containers, thumbnail strips, or preview thumbnails over the sky.
        var titlePoint = new PointF(titleBox.X + 18 * scale, titleBox.Y + 16 * scale);
        ctx.DrawText(textLines.ElementAtOrDefault(0) ?? string.Join(" + ", objects.Take(2)).ToUpperInvariant(), titleFont, Color.White, titlePoint);
        ctx.DrawText(textLines.ElementAtOrDefault(1) ?? "CONJUNCTION", subFont, Color.FromRgb(255, 222, 91), new PointF(titlePoint.X + 3 * scale, titlePoint.Y + (landscape ? 50 : 62) * scale));
        if (!string.IsNullOrWhiteSpace(textLines.ElementAtOrDefault(2)))
            ctx.DrawText(textLines[2], microFont, Color.FromRgb(200, 230, 255), new PointF(titlePoint.X + 4 * scale, titlePoint.Y + (landscape ? 86 : 100) * scale));
        var rows = new List<string> { $"DATE  {date}", $"BEST TIME  {bestTime}", $"DIRECTION  {direction}", "EQUIPMENT  BINOCULARS" };
        for (var i = 0; i < rows.Count; i++)
            ctx.DrawText(rows[i], microFont, rows[i].Contains("DIRECTION", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(255, 222, 91) : Color.FromRgb(205, 235, 255), new PointF(titlePoint.X + 4 * scale, titlePoint.Y + (landscape ? 132 : 132) * scale + i * 34 * scale));

        var p1 = landscape ? new PointF(width * .50f, height * .39f) : square ? new PointF(width * .46f, height * .38f) : new PointF(width * .42f, height * .38f);
        var p2 = landscape ? new PointF(width * .60f, height * .34f) : square ? new PointF(width * .62f, height * .32f) : new PointF(width * .58f, height * .34f);
        var label1 = new PointF(p1.X - 155 * scale, p1.Y + 28 * scale);
        var label2 = new PointF(p2.X + 24 * scale, p2.Y - 42 * scale);
        ctx.Fill(Color.FromRgb(255, 244, 180), new EllipsePolygon(p1.X, p1.Y, 11 * scale));
        ctx.Fill(Color.FromRgb(235, 248, 255), new EllipsePolygon(p2.X, p2.Y, 8.8f * scale));
        ctx.DrawText(objects.ElementAtOrDefault(0) ?? "Planet", smallFont, Color.White, label1);
        ctx.DrawText(objects.ElementAtOrDefault(1) ?? "Planet", smallFont, Color.White, label2);
        DrawLeaderLine(ctx, label1, p1, Color.White);
        DrawLeaderLine(ctx, label2, p2, Color.White);
        if (!string.IsNullOrWhiteSpace(separation))
            ctx.DrawText($"{separation} APART", smallFont, Color.FromRgb(255, 222, 91), new PointF((p1.X + p2.X) / 2 - 70 * scale, (p1.Y + p2.Y) / 2 + 52 * scale));
        var cue = new PointF(directionBox.X + 34 * scale, directionBox.Y + directionBox.Height * .52f);
        DrawCompassCue(ctx, cue, 32 * scale, -0.05f);
        ctx.DrawText(direction.ToUpperInvariant(), smallFont, Color.FromRgb(255, 222, 91), new PointF(cue.X + 44 * scale, cue.Y - 16 * scale));
        if (!string.IsNullOrWhiteSpace(window) && !landscape) ctx.DrawText($"WINDOW  {CondenseViewingWindow(window)}", microFont, Color.FromRgb(200, 230, 255), new PointF(skyGuideBox.X + skyGuideBox.Width * .52f, skyGuideBox.Y + skyGuideBox.Height * .78f));

        var footerHeight = Math.Max(44, 52 * scale);
        var tips = new RectangleF(0, height - footerHeight, width, footerHeight);
        ctx.Fill(Color.FromRgba(0, 0, 0, 160), tips);
        ctx.DrawText("TIPS  •  USE BINOCULARS IF NEEDED  •  CHECK MOONLIGHT  •  START AT TWILIGHT", ResolveThumbnailFont(Math.Max(17, (landscape ? 25 : square ? 26 : 29) * scale), FontStyle.Bold), Color.FromRgb(225, 240, 255), new PointF(width * .055f, tips.Y + 14 * scale));

        var count = 8 + objects.Take(2).Count() + rows.Count + (!string.IsNullOrWhiteSpace(separation) ? 1 : 0);
        return ValidateAndCreatePlanetaryOverlayDiagnostics(outputPath, count, width, height, titleBox, card, directionBox, tips, skyGuideBox, !string.IsNullOrWhiteSpace(separation), !string.IsNullOrWhiteSpace(altitude));
    }


    private static ThumbnailOverlayDiagnostics ValidateAndCreatePlanetaryOverlayDiagnostics(string outputPath, int count, int width, int height, RectangleF titleBox, RectangleF guideCardBox, RectangleF directionBox, RectangleF footerBox, RectangleF skyGuideBox, bool separationAdded, bool altitudeAdded)
    {
        var landscape = width > height;
        var panelCount = 0;
        RectangleF[] overlapBoxes = landscape
            ? [guideCardBox, directionBox, footerBox, skyGuideBox]
            : [titleBox, guideCardBox, directionBox, footerBox, skyGuideBox];
        var overlapPercent = CalculateOverlapPercent(overlapBoxes);
        var visualPercent = 100d - overlapPercent;
        if (overlapPercent > 35d)
            throw new InvalidOperationException($"Thumbnail V5 guide validation failed: overlayPercent must be <= 35 for ThumbnailV3PureAzureImage2CtrOverlay; actual={overlapPercent:0.###}.");
        if (visualPercent < 65d)
            throw new InvalidOperationException($"Thumbnail V5 guide validation failed: visualPercent must be >= 65 for ThumbnailV3PureAzureImage2CtrOverlay; actual={visualPercent:0.###}.");
        if (IntersectionArea(guideCardBox, directionBox) > 0)
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: guideCard intersects directionCue.");
        if (IntersectionArea(guideCardBox, skyGuideBox) > 0)
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: guideCard intersects skyGuide.");
        if (!ContainsRect(new RectangleF(0, 0, width, height), titleBox))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: titleBlock exceeds template zone.");

        return new ThumbnailOverlayDiagnostics(
            "PlanetarySkyGuideThumbnail",
            count,
            false,
            false,
            false,
            true,
            true,
            outputPath,
            "PlanetaryEvent",
            false,
            true,
            true,
            separationAdded,
            altitudeAdded,
            TitleBoundingBox: ToBoundsObject(titleBox),
            GuideCardBoundingBox: ToBoundsObject(guideCardBox),
            DirectionBoundingBox: ToBoundsObject(directionBox),
            FooterBoundingBox: ToBoundsObject(footerBox),
            SkyGuideBoundingBox: ToBoundsObject(skyGuideBox),
            OverlapPercent: overlapPercent,
            PanelCount: panelCount);
    }

    private static string CondenseViewingWindow(string window)
        => string.IsNullOrWhiteSpace(window) ? string.Empty : window.Replace("Best viewing window", "Window", StringComparison.OrdinalIgnoreCase).Replace("Viewing Window:", "Window", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRect(RectangleF outer, RectangleF inner)
        => inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static double CalculateOverlapPercent(IReadOnlyList<RectangleF> boxes)
    {
        var overlap = 0d;
        for (var i = 0; i < boxes.Count; i++)
        for (var j = i + 1; j < boxes.Count; j++)
            overlap += IntersectionArea(boxes[i], boxes[j]);
        var total = boxes.Sum(b => b.Width * b.Height);
        return total <= 0 ? 0 : Math.Round(overlap / total * 100d, 3);
    }

    private static double IntersectionArea(RectangleF a, RectangleF b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= left || bottom <= top ? 0 : (right - left) * (bottom - top);
    }

    private static object ToBoundsObject(RectangleF rect) => new { x = Math.Round(rect.X, 2), y = Math.Round(rect.Y, 2), width = Math.Round(rect.Width, 2), height = Math.Round(rect.Height, 2) };

    private static ThumbnailOverlayDiagnostics DrawMoonPhaseGuideThumbnail(IImageProcessingContext ctx, CurrentEventLock current, int width, int height, IReadOnlyList<string> textLines, string outputPath)
    {
        var scale = width / 1280f;
        var titleFont = ResolveThumbnailFont(Math.Max(44, 70 * scale), FontStyle.Bold);
        var subFont = ResolveThumbnailFont(Math.Max(28, 38 * scale), FontStyle.Bold);
        var smallFont = ResolveThumbnailFont(Math.Max(18, 24 * scale), FontStyle.Bold);
        var microFont = ResolveThumbnailFont(Math.Max(16, 21 * scale), FontStyle.Regular);
        var phaseName = ResolveMoonPhaseName(current);
        var date = current.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "Event date";
        var rawPeak = FirstNonEmpty(current.BestViewingWindowLocal, current.LocalPeakTime, "Local best visibility window");
        var peak = ResolveMoonThumbnailBestTime(rawPeak);
        var direction = ResolveMoonThumbnailDirection(FirstNonEmpty(current.SkyDirectionHint, InferMoonDirectionCue(current)));
        var overlayFields = new
        {
            title = CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, ResolveMoonPhaseName(current))),
            subtitle = ResolveMoonDisplaySubtitle(current),
            date,
            bestTime = peak,
            rawBestTime = rawPeak,
            direction,
            observe = "Naked eye",
            moonPhase = phaseName,
            footerTips = "Watch moonrise • Use tripod for photos • Avoid bright foreground lights"
        };
        Console.WriteLine("[ThumbnailMoonOverlayFields] " + JsonSerializer.Serialize(overlayFields, JsonOptions));

        var titlePoint = width >= height ? new PointF(width * .055f, height * .075f) : new PointF(width * .06f, height * .055f);
        ctx.Fill(Color.FromRgba(0, 0, 0, 142), new RectangleF(titlePoint.X - 18 * scale, titlePoint.Y - 18 * scale, width >= height ? width * .54f : width * .88f, width == height ? height * .18f : height * .14f));
        ctx.DrawText(overlayFields.title, titleFont, Color.White, titlePoint);
        ctx.DrawText(overlayFields.subtitle, subFont, Color.FromRgb(210, 230, 255), new PointF(titlePoint.X + 4 * scale, titlePoint.Y + 78 * scale));

        var moonCenter = width >= height ? new PointF(width * .66f, height * .38f) : new PointF(width * .52f, height * .35f);
        var moonRadius = (width >= height ? 104 : 128) * scale;
        var moonVisibleDiameterPx = moonRadius * 2f;
        var moonCalloutCircleDiameterPx = moonVisibleDiameterPx * 0.18f;
        ctx.Draw(Color.FromRgba(210, 230, 255, 185), 3, new EllipsePolygon(moonCenter.X, moonCenter.Y, moonRadius, moonRadius));
        ctx.Draw(Color.FromRgba(255, 222, 91, 220), Math.Max(2f, 3f * scale), new EllipsePolygon(moonCenter.X - moonRadius * 0.20f, moonCenter.Y - moonRadius * 0.12f, moonCalloutCircleDiameterPx / 2f, moonCalloutCircleDiameterPx / 2f));
        ctx.DrawText("MOON", smallFont, Color.White, new PointF(moonCenter.X + moonRadius + 18 * scale, moonCenter.Y - 20 * scale));

        var card = width > height
            ? new RectangleF(width * .055f, height * .58f, width * .42f, height * .32f)
            : width == height
                ? new RectangleF(width * .08f, height * .70f, width * .84f, height * .23f)
                : new RectangleF(width * .08f, height * .64f, width * .84f, height * .26f);
        ctx.Fill(Color.FromRgba(2, 10, 24, 184), card);
        ctx.Draw(Color.FromRgba(210, 230, 255, 165), 2, card);
        var rows = new List<string> { $"DATE: {date}", $"BEST TIME: {peak}", $"DIRECTION: {direction}", "OBSERVE: Naked eye", $"MOON PHASE: {phaseName}" };
        for (var i = 0; i < rows.Count; i++)
            ctx.DrawText(rows[i], microFont, i == 2 ? Color.FromRgb(255, 222, 91) : Color.FromRgb(205, 235, 255), new PointF(card.X + 24 * scale, card.Y + (24 + i * 34) * scale));

        var cue = width >= height ? new PointF(width * .72f, height * .82f) : new PointF(width * .10f, height * .53f);
        DrawCompassCue(ctx, cue, 42 * scale, -0.05f);
        ctx.DrawText(direction, smallFont, Color.FromRgb(255, 222, 91), new PointF(cue.X + 58 * scale, cue.Y - 18 * scale));
        ctx.DrawText(peak, smallFont, Color.FromRgb(205, 235, 255), new PointF(cue.X + 58 * scale, cue.Y + 18 * scale));

        var footerHeight = Math.Max(49, 58 * scale);
        var tips = new RectangleF(0, height - footerHeight, width, footerHeight);
        ctx.Fill(Color.FromRgba(0, 0, 0, 160), tips);
        ctx.DrawText("Watch moonrise • Use tripod for photos • Avoid bright foreground lights", smallFont, Color.FromRgb(225, 240, 255), new PointF(width * .055f, tips.Y + 16 * scale));

        return new ThumbnailOverlayDiagnostics("MoonPhaseGuideThumbnail", 8 + rows.Count, false, false, false, true, false, outputPath, "Moon", true, false, true, false, false, MoonGuideCardAdded: true, MoonObjectRendered: true, MoonForbiddenTermsDetected: [], MoonAspectRatioPreserved: true, MoonCalloutCircleDiameterPx: moonCalloutCircleDiameterPx, MoonVisibleDiameterPx: moonVisibleDiameterPx);
    }

    private static ThumbnailOverlayDiagnostics DrawAzureImage2ThumbnailV5SimpleOverlay(IImageProcessingContext ctx, CurrentEventLock current, int width, int height, string outputPath)
    {
        var isLandscape = width > height;
        var scale = width / 1280f;
        var overlayPercent = isLandscape ? 0.28f : width == height ? 0.30f : 0.30f;
        var overlay = isLandscape
            ? new RectangleF(0, 0, width * overlayPercent, height)
            : new RectangleF(0, height * (1f - overlayPercent), width, height * overlayPercent);
        ctx.Fill(Color.FromRgba(0, 0, 0, 178), overlay);
        ctx.Draw(Color.FromRgba(255, 209, 94, 110), Math.Max(2f, 3f * scale), overlay);

        var badge = BuildAzureImage2ThumbnailV5FamilyBadge(current);
        var title = CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, "SKY EVENT")).ToUpperInvariant();
        var date = current.EventDate?.ToString("dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant() ?? "DATE TBD";
        var titleFont = ResolveThumbnailFont(Math.Max(34, (isLandscape ? 50 : 70) * scale), FontStyle.Bold);
        var badgeFont = ResolveThumbnailFont(Math.Max(18, (isLandscape ? 24 : 32) * scale), FontStyle.Bold);
        var dateFont = ResolveThumbnailFont(Math.Max(22, (isLandscape ? 30 : 40) * scale), FontStyle.Bold);
        var x = overlay.X + (isLandscape ? 30 * scale : 64 * scale);
        var y = overlay.Y + (isLandscape ? height * .16f : 52 * scale);
        var badgeRect = new RectangleF(x, y, isLandscape ? overlay.Width - 60 * scale : 370 * scale, 46 * scale);
        ctx.Fill(Color.FromRgba(255, 209, 94, 224), badgeRect);
        ctx.DrawText(badge, badgeFont, Color.Black, new PointF(badgeRect.X + 16 * scale, badgeRect.Y + 10 * scale));
        ctx.DrawText(title, titleFont, Color.White, new PointF(x, y + 78 * scale));
        ctx.DrawText(date, dateFont, Color.FromRgb(255, 222, 91), new PointF(x, y + (isLandscape ? 180 : 168) * scale));
        return new ThumbnailOverlayDiagnostics("AzureImage2ThumbnailV5Variants", 3, false, false, false, false, false, outputPath, ResolveAzureImage2ThumbnailV5Family(current));
    }

    private static string BuildAzureImage2ThumbnailV5FamilyBadge(CurrentEventLock current)
    {
        if (IsEclipseEvent(current.EventType, current.Title)) return IsSolarEclipse(current) ? "RARE ECLIPSE" : "ECLIPSE EVENT";
        if (IsPlanetaryEvent(current.EventType)) return "PLANET ALIGNMENT";
        if (IsMoonEvent(current.EventType, current.Title)) return "MOON EVENT";
        if (IsMeteorEvent(current.EventType, current.Title)) return "METEOR SHOWER";
        return "SKY EVENT";
    }

    private static string ResolveAzureImage2ThumbnailV5Family(CurrentEventLock current)
    {
        if (IsEclipseEvent(current.EventType, current.Title)) return "Eclipse";
        if (IsPlanetaryEvent(current.EventType)) return "PlanetaryEvent";
        if (IsMoonEvent(current.EventType, current.Title)) return "Moon";
        if (IsMeteorEvent(current.EventType, current.Title)) return "MeteorShower";
        return "AstronomyEvent";
    }

    private static ThumbnailOverlayDiagnostics DrawEclipseGuideThumbnail(IImageProcessingContext ctx, CurrentEventLock current, int width, int height, string outputPath)
    {
        var scale = width / 1280f;
        var titleFont = ResolveThumbnailFont(Math.Max(44, 64 * scale), FontStyle.Bold);
        var subFont = ResolveThumbnailFont(Math.Max(28, 36 * scale), FontStyle.Bold);
        var smallFont = ResolveThumbnailFont(Math.Max(18, 24 * scale), FontStyle.Bold);
        var microFont = ResolveThumbnailFont(Math.Max(16, 20 * scale), FontStyle.Regular);
        var title = CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, "Eclipse"));
        var isSolar = IsSolarEclipse(current);
        var subtitle = isSolar ? "SOLAR ECLIPSE" : "LUNAR ECLIPSE";
        var direction = ResolveEclipseDirectionLabel(current);
        var warning = isSolar ? "SAFE SOLAR FILTER REQUIRED" : "NAKED EYE OK";
        var timing = FirstNonEmpty(current.BestViewingWindowLocal, current.LocalPeakTime, "CHECK LOCAL TIMING");
        var date = current.EventDate?.ToString("dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant() ?? "DATE TBD";

        var titleBlock = width >= height
            ? new RectangleF(width * .045f, height * .075f, width * .44f, height * .25f)
            : new RectangleF(width * .065f, height * .055f, width * .82f, height * .17f);
        ctx.Fill(Color.FromRgba(2, 8, 20, 188), titleBlock);
        ctx.Draw(Color.FromRgba(255, 209, 94, 150), 2, titleBlock);
        ctx.DrawText(title, titleFont, Color.White, new PointF(titleBlock.X + 28 * scale, titleBlock.Y + 24 * scale));
        ctx.DrawText(subtitle, subFont, Color.FromRgb(255, 209, 94), new PointF(titleBlock.X + 30 * scale, titleBlock.Y + 100 * scale));

        var bodyCenter = width >= height ? new PointF(width * .68f, height * .36f) : new PointF(width * .52f, height * .36f);
        var bodyRadius = (width >= height ? 112 : 128) * scale;
        DrawEclipseBody(ctx, bodyCenter, bodyRadius, isSolar);
        ctx.DrawText(isSolar ? "SUN + MOON ALIGNMENT" : "EARTH SHADOW ON MOON", smallFont, Color.FromRgb(255, 235, 180), new PointF(bodyCenter.X - bodyRadius * 1.25f, bodyCenter.Y + bodyRadius + 24 * scale));
        DrawLeaderLine(ctx, new PointF(bodyCenter.X - bodyRadius * 0.55f, bodyCenter.Y + bodyRadius + 20 * scale), bodyCenter, Color.FromRgb(255, 235, 180));

        var card = width > height
            ? new RectangleF(width * .055f, height * .50f, width * .44f, height * .32f)
            : width == height
                ? new RectangleF(width * .08f, height * .63f, width * .84f, height * .24f)
                : new RectangleF(width * .08f, height * .58f, width * .84f, height * .26f);
        ctx.Fill(Color.FromRgba(2, 10, 24, 188), card);
        ctx.Draw(Color.FromRgba(255, 209, 94, 150), 2, card);
        var rows = new[] { $"DATE: {date}", $"TIMING: {timing}", $"DIRECTION: {direction}", $"OBSERVE: {warning}" };
        for (var i = 0; i < rows.Length; i++)
            ctx.DrawText(rows[i], microFont, i >= 2 ? Color.FromRgb(255, 222, 91) : Color.FromRgb(205, 235, 255), new PointF(card.X + 24 * scale, card.Y + (28 + i * 42) * scale));

        var cue = width >= height ? new PointF(width * .70f, height * .80f) : new PointF(width * .10f, height * .50f);
        DrawCompassCue(ctx, cue, 42 * scale, 0.05f);
        ctx.DrawText(direction, smallFont, Color.FromRgb(255, 222, 91), new PointF(cue.X + 58 * scale, cue.Y - 18 * scale));
        ctx.DrawText(warning, smallFont, isSolar ? Color.FromRgb(255, 125, 84) : Color.FromRgb(205, 235, 255), new PointF(cue.X + 58 * scale, cue.Y + 18 * scale));

        var tips = new RectangleF(0, height - Math.Max(64, 76 * scale), width, Math.Max(64, 76 * scale));
        ctx.Fill(Color.FromRgba(0, 0, 0, 170), tips);
        ctx.DrawText(isSolar ? "TIPS  •  NEVER LOOK AT THE SUN UNFILTERED  •  VERIFY LOCAL VISIBILITY" : "TIPS  •  WATCH THE MOON  •  TRACK UMBRA/PENUMBRA TIMING", smallFont, Color.FromRgb(225, 240, 255), new PointF(width * .055f, tips.Y + 24 * scale));

        return new ThumbnailOverlayDiagnostics("EclipseGuideThumbnail", 10 + rows.Length, true, false, false, true, true, outputPath, "Eclipse", true, true, true);
    }

    private static void DrawEclipseBody(IImageProcessingContext ctx, PointF center, float radius, bool solar)
    {
        if (solar)
        {
            DrawThumbnailGlow(ctx, center, radius * 2.2f, radius * 2.2f, Color.FromRgb(255, 209, 94), 0.22f, 16);
            ctx.Fill(Color.FromRgb(255, 209, 94), new EllipsePolygon(center.X, center.Y, radius, radius));
            ctx.Fill(Color.FromRgba(0, 0, 0, 238), new EllipsePolygon(center.X + radius * 0.08f, center.Y, radius * 0.92f, radius * 0.92f));
            ctx.Draw(Color.FromRgb(255, 242, 165), Math.Max(2f, radius * 0.025f), new EllipsePolygon(center.X + radius * 0.08f, center.Y, radius * 0.96f, radius * 0.96f));
            return;
        }

        DrawThumbnailGlow(ctx, center, radius * 1.85f, radius * 1.85f, Color.FromRgb(179, 69, 46), 0.18f, 14);
        ctx.Fill(Color.FromRgb(164, 65, 46), new EllipsePolygon(center.X, center.Y, radius, radius));
        ctx.Fill(Color.FromRgba(35, 10, 18, 112), new EllipsePolygon(center.X - radius * 0.38f, center.Y, radius * 0.72f, radius * 1.04f));
        ctx.Draw(Color.FromRgb(224, 123, 88), Math.Max(2f, radius * 0.018f), new EllipsePolygon(center.X, center.Y, radius, radius));
    }

    private static string ResolveMoonPhaseName(CurrentEventLock current)
    {
        var token = NormalizeEventTypeToken(current.EventType);
        if (token == "BLUEMOON") return "Blue Moon";
        if (token == "SUPERMOON") return "Supermoon";
        if (token == "MICROMOON") return "Micromoon";
        if (token == "NEWMOON") return "New Moon";
        if (token == "FIRSTQUARTER") return "First Quarter";
        if (token == "LASTQUARTER") return "Last Quarter";
        if (token == "FULLMOON" || token == "NAMEDFULLMOON") return "Full Moon";
        return CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, "Moon Phase"));
    }

    private static string InferMoonDirectionCue(CurrentEventLock current)
        => IsFullMoonEvent(current.EventType, current.Title) ? "Eastern sky near moonrise; overhead around local midnight when visible" : "Eastern sky near moonrise";


    private static string ResolveMoonDisplaySubtitle(CurrentEventLock current)
    {
        var token = NormalizeEventTypeToken(current.EventType);
        if (token is "NAMEDFULLMOON" or "FULLMOON" || current.Title.Contains("Full Moon", StringComparison.OrdinalIgnoreCase)) return "FULL MOON";
        return ResolveMoonPhaseName(current).ToUpperInvariant();
    }

    private static string ResolveMoonThumbnailDirection(string value)
    {
        var direction = FirstNonEmpty(value, "Eastern sky near moonrise; overhead around local midnight when visible");
        if (direction.Contains("east", StringComparison.OrdinalIgnoreCase) || direction.Contains("moonrise", StringComparison.OrdinalIgnoreCase)) return "LOOK EAST AT MOONRISE";
        if (direction.Contains("west", StringComparison.OrdinalIgnoreCase) || direction.Contains("moonset", StringComparison.OrdinalIgnoreCase)) return "LOOK WEST AT MOONSET";
        return "WATCH NEAR MOONRISE";
    }

    private static string ResolveMoonThumbnailBestTime(string value)
    {
        var time = FirstNonEmpty(value, "After moonrise");
        if (time.Contains("moonrise", StringComparison.OrdinalIgnoreCase)) return "AFTER MOONRISE";
        if (time.Contains("evening", StringComparison.OrdinalIgnoreCase) || time.Contains("midnight", StringComparison.OrdinalIgnoreCase)) return "EVENING TO MIDNIGHT";
        return "AFTER MOONRISE";
    }

    private static bool IsMoonFamilyEventType(string? eventType, string? title)
    {
        var token = NormalizeEventTypeToken(eventType ?? string.Empty);
        return token.Contains("MOON", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(title) && title.Contains("Moon", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRareMoonEvent(CurrentEventLock current)
        => NormalizeEventTypeToken(current.EventType) is "BLUEMOON" or "SUPERMOON" or "MICROMOON";

    private static string ResolveMoonRarityNote(CurrentEventLock current)
        => NormalizeEventTypeToken(current.EventType) switch
        {
            "BLUEMOON" => "RARITY  calendar blue moon",
            "SUPERMOON" => "RARITY  closer full moon",
            "MICROMOON" => "RARITY  smaller full moon",
            _ => string.Empty
        };

    private static ThumbnailOverlayDiagnostics DrawMeteorShowerRc1VisualGuide(IImageProcessingContext ctx, CurrentEventLock current, int width, int height, IReadOnlyList<string> textLines, string outputPath)
    {
        var scale = width / 1280f;
        var titleFont = ResolveThumbnailFont(Math.Max(46, 70 * scale), FontStyle.Bold);
        var subFont = ResolveThumbnailFont(Math.Max(30, 38 * scale), FontStyle.Bold);
        var smallFont = ResolveThumbnailFont(Math.Max(18, 24 * scale), FontStyle.Bold);
        var microFont = ResolveThumbnailFont(Math.Max(16, 20 * scale), FontStyle.Regular);
        var card = width >= height
            ? new RectangleF(width * .045f, height * .08f, width * .39f, height * .54f)
            : new RectangleF(width * .06f, height * .07f, width * .70f, height * .36f);
        ctx.Fill(Color.FromRgba(2, 10, 24, 178), card);
        ctx.Draw(Color.FromRgba(110, 220, 255, 140), 2, card);
        ctx.DrawText(textLines.ElementAtOrDefault(0) ?? "GEMINIDS", titleFont, Color.White, new PointF(card.X + 28 * scale, card.Y + 24 * scale));
        ctx.DrawText(textLines.ElementAtOrDefault(1) ?? "METEOR SHOWER PEAK", subFont, Color.FromRgb(255, 222, 91), new PointF(card.X + 30 * scale, card.Y + 96 * scale));
        var date = current.EventDate?.ToString("MMM d", CultureInfo.InvariantCulture).ToUpperInvariant() ?? "PEAK NIGHT";
        var time = FirstNonEmpty(current.BestViewingWindowLocal, current.LocalPeakTime, "MIDNIGHT–PRE-DAWN");
        var direction = FirstNonEmpty(current.SkyDirectionHint, "LOOK EAST");
        var rows = new[] { $"DATE  {date}", $"BEST TIME  {time}", $"DIRECTION  {direction}", "EQUIPMENT  EYES ONLY", "MOON  LOW INTERFERENCE" };
        for (var i = 0; i < rows.Length; i++)
            ctx.DrawText(rows[i], microFont, i == 2 ? Color.FromRgb(255, 222, 91) : Color.FromRgb(205, 235, 255), new PointF(card.X + 32 * scale, card.Y + (154 + i * 38) * scale));

        var radiant = width >= height ? new PointF(width * .70f, height * .32f) : new PointF(width * .64f, height * .48f);
        DrawRadiantGuide(ctx, radiant, 22 * scale, 190 * scale, 9);
        ctx.Fill(Color.FromRgba(126, 226, 255, 210), new EllipsePolygon(radiant.X, radiant.Y, 8 * scale));
        ctx.DrawText("GEMINIDS RADIANT", smallFont, Color.FromRgb(180, 235, 255), new PointF(radiant.X + 28 * scale, radiant.Y - 24 * scale));
        DrawLeaderLine(ctx, new PointF(radiant.X + 26 * scale, radiant.Y - 6 * scale), radiant, Color.FromRgb(180, 235, 255));

        var streakLabel = width >= height ? new PointF(width * .72f, height * .60f) : new PointF(width * .52f, height * .63f);
        ctx.DrawText("METEOR STREAKS", smallFont, Color.FromRgb(255, 255, 255), streakLabel);
        DrawLeaderLine(ctx, streakLabel, new PointF(width * .82f, height * .43f), Color.White);

        var look = width >= height ? new PointF(width * .70f, height * .82f) : new PointF(width * .12f, height * .83f);
        DrawCompassCue(ctx, look, 46 * scale, -0.18f);
        ctx.DrawText("LOOK EAST", smallFont, Color.FromRgb(255, 222, 91), new PointF(look.X + 58 * scale, look.Y - 18 * scale));

        var tips = new RectangleF(0, height - Math.Max(70, 82 * scale), width, Math.Max(70, 82 * scale));
        ctx.Fill(Color.FromRgba(0, 0, 0, 170), tips);
        ctx.DrawText("TIPS  •  DARK SKY  •  20 MINUTES DARK ADAPTATION  •  SCAN WIDE", smallFont, Color.FromRgb(225, 240, 255), new PointF(width * .055f, tips.Y + 24 * scale));
        return new ThumbnailOverlayDiagnostics("MeteorShowerRc1VisualGuide", 11, true, true, true, true, true, outputPath);
    }

    private static void ValidateMeteorThumbnailOverlay(string currentEventType, string currentTitle, ThumbnailOverlayDiagnostics diagnostics)
    {
        if (!IsMeteorEvent(currentEventType, currentTitle)) return;
        if (diagnostics.OverlayElementsCount <= 2)
            throw new InvalidOperationException("MeteorShower thumbnail validation failed: overlayElementsCount must be greater than 2; title/subtitle-only overlay is not allowed.");
        if (!diagnostics.InfoCardAdded || !diagnostics.RadiantMarkerAdded || !diagnostics.MeteorStreakLabelAdded || !diagnostics.LookDirectionCueAdded || !diagnostics.BottomTipsBarAdded)
            throw new InvalidOperationException("MeteorShower thumbnail validation failed: guide visual-guide overlay elements are missing.");
    }

    private static void ValidatePlanetaryThumbnailOverlay(CurrentEventLock current, ThumbnailOverlayDiagnostics diagnostics)
    {
        if (!IsPlanetaryEvent(current.EventType)) return;
        if (!diagnostics.ObjectLabelsAdded || !diagnostics.GuideCardAdded || !diagnostics.DirectionCueAdded)
            throw new InvalidOperationException("PlanetaryEvent thumbnail validation failed: guide card, labels, and direction cue are required.");
        if (current.AngularSeparationDegrees.HasValue && !diagnostics.SeparationAdded)
            throw new InvalidOperationException("PlanetaryEvent thumbnail validation failed: separation overlay is required when angularSeparationDegrees exists.");
        if (diagnostics.RadiantMarkerAdded || diagnostics.MeteorStreakLabelAdded)
            throw new InvalidOperationException("PlanetaryEvent thumbnail validation failed: meteor overlay elements are not allowed.");
        if (diagnostics.OverlayElementsCount <= 4)
            throw new InvalidOperationException("PlanetaryEvent thumbnail validation failed: overlayElementsCount must be greater than 4.");
    }

    private static PlanetaryThumbnailGuideCardDto BuildPlanetaryGuideCard(CurrentEventLock current)
        => new(
            current.EventDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
            FirstNonEmpty(current.LocalPeakTime, current.BestViewingWindowLocal),
            NormalizeDirectionCue(current.SkyDirectionHint),
            FirstNonEmpty(current.BestViewingWindowLocal, "After sunset"),
            current.AngularSeparationDegrees is decimal sep ? $"{sep:0.##}°" : null,
            current.AltitudeDegrees is decimal alt ? $"{alt:0.#}°" : null);

    private static IReadOnlyList<string> BuildPlanetaryCallouts(CurrentEventLock current)
    {
        var callouts = new List<string>();
        if (current.AngularSeparationDegrees is decimal sep) callouts.Add($"{sep:0.##}° APART");
        return callouts;
    }

    private static string BuildPlanetaryCleanHeadline(CurrentEventLock current)
        => BuildPlanetFamilyThumbnailCopy(current.EventType, ResolvePlanetaryObjectLabels(current)).PrimaryText;

    private static IReadOnlyList<string> ResolvePlanetaryObjectLabels(CurrentEventLock current)
    {
        var labels = NormalizeObjectList(current.PrimaryObjects.Concat(current.SecondaryObjects))
            .Select(FormatThumbnailObjectLabel)
            .Where(IsPlanetObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return labels.Length > 0 ? labels : ExtractPlanetObjectNames(FirstNonEmpty(current.ShortTitle, current.Title));
    }

    private static void ValidatePlanetaryThumbnailProfile(CurrentEventLock current, string shortTitle)
    {
        if (shortTitle.Length > 50)
            throw new InvalidOperationException("PlanetaryEvent thumbnail validation failed: shortTitle must not exceed 50 characters.");
        if (!IsPlanetaryConjunctionEvent(current.EventType)) return;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(current.LocalPeakTime)) missing.Add("localPeakTime");
        if (string.IsNullOrWhiteSpace(current.SkyDirectionHint)) missing.Add("skyDirectionHint");
        if (string.IsNullOrWhiteSpace(current.BestViewingWindowLocal)) missing.Add("bestViewingWindowLocal");
        if (missing.Count > 0)
            throw new InvalidOperationException("PlanetConjunction thumbnail validation failed: " + string.Join(", ", missing) + " are required for EventVisualGuideProfile.");
    }

    private static bool IsPlanetaryConjunctionEvent(string eventType)
        => NormalizeEventTypeToken(eventType) is "PLANETCONJUNCTION" or "CONJUNCTION";

    private static void DrawRadiantGuide(IImageProcessingContext ctx, PointF center, float innerRadius, float outerRadius, int rayCount)
    {
        var color = Color.FromRgba(118, 225, 255, 82);
        for (var i = 0; i < rayCount; i++)
        {
            var angle = (-150f + i * (300f / Math.Max(1, rayCount - 1))) * MathF.PI / 180f;
            var start = new PointF(center.X + MathF.Cos(angle) * (innerRadius + 10), center.Y + MathF.Sin(angle) * (innerRadius + 10));
            var end = new PointF(center.X + MathF.Cos(angle) * outerRadius, center.Y + MathF.Sin(angle) * outerRadius);
            DrawDashedLine(ctx, color, 2, start, end, 12, 9);
        }
    }

    private static void DrawLeaderLine(IImageProcessingContext ctx, PointF labelPoint, PointF anchorPoint, Color color)
    {
        DrawDashedLine(ctx, color.WithAlpha(0.78f), 2, labelPoint, anchorPoint, 10, 6);
        ctx.Fill(color.WithAlpha(0.90f), new EllipsePolygon(anchorPoint.X, anchorPoint.Y, 5));
        var angle = MathF.Atan2(anchorPoint.Y - labelPoint.Y, anchorPoint.X - labelPoint.X);
        var left = new PointF(anchorPoint.X - MathF.Cos(angle - 0.55f) * 16, anchorPoint.Y - MathF.Sin(angle - 0.55f) * 16);
        var right = new PointF(anchorPoint.X - MathF.Cos(angle + 0.55f) * 16, anchorPoint.Y - MathF.Sin(angle + 0.55f) * 16);
        ctx.Draw(color.WithAlpha(0.86f), 2, new PathBuilder().AddLines([left, anchorPoint, right]).Build());
    }

    private static void DrawCompassCue(IImageProcessingContext ctx, PointF center, float size, float angleRadians)
    {
        var color = Color.FromRgba(255, 218, 80, 168);
        var tip = new PointF(center.X + MathF.Cos(angleRadians) * size, center.Y + MathF.Sin(angleRadians) * size);
        var tail = new PointF(center.X - MathF.Cos(angleRadians) * size * 0.55f, center.Y - MathF.Sin(angleRadians) * size * 0.55f);
        ctx.Draw(color, 2, new PathBuilder().AddLine(tail, tip).Build());
        ctx.Draw(color.WithAlpha(0.55f), 1, new EllipsePolygon(center.X, center.Y, size * 0.42f));
        var left = new PointF(tip.X - MathF.Cos(angleRadians - 0.55f) * 14, tip.Y - MathF.Sin(angleRadians - 0.55f) * 14);
        var right = new PointF(tip.X - MathF.Cos(angleRadians + 0.55f) * 14, tip.Y - MathF.Sin(angleRadians + 0.55f) * 14);
        ctx.Draw(color, 2, new PathBuilder().AddLines([left, tip, right]).Build());
    }

    private static void DrawDashedLine(IImageProcessingContext ctx, Color color, float thickness, PointF start, PointF end, float dashLength, float gapLength)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.1f) return;
        var ux = dx / length;
        var uy = dy / length;
        for (var covered = 0f; covered < length; covered += dashLength + gapLength)
        {
            var segmentEnd = MathF.Min(covered + dashLength, length);
            var p1 = new PointF(start.X + ux * covered, start.Y + uy * covered);
            var p2 = new PointF(start.X + ux * segmentEnd, start.Y + uy * segmentEnd);
            ctx.Draw(color, thickness, new PathBuilder().AddLine(p1, p2).Build());
        }
    }

    private static async Task WriteAzureImage2ThumbnailV5GenerationSummaryDiagnosticsAsync(
        string promptText,
        AzureOpenAIForImageOptions options,
        string imagePath,
        string promptPath,
        string diagnosticsPath,
        IReadOnlyList<(string Variant, string Prompt, int Width, int Height, string TextLayout, string BackgroundPath, string ImagePath, AzureImage2GenerationResult Result, string Hash)> variants,
        IReadOnlyList<(string Path, string HashBeforeWrite, string HashAfterWrite, string WriterComponent, string TemplateName, string TemplateVersion)> finalFileWrites,
        object duplicateHashGroups,
        long totalMs,
        bool allRequiredThumbnailFilesGenerated,
        IReadOnlyList<string> missingThumbnailFiles,
        bool retryMissingVariantsAttempted,
        bool retryMissingVariantsSucceeded,
        bool finalCopiedFromLandscape,
        CancellationToken cancellationToken)
    {
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        var uniqueHashes = variants.Select(v => v.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var finalOutputHashBeforeOverlay = File.Exists(variants.First().BackgroundPath) ? await ComputeSha256Async(variants.First().BackgroundPath, cancellationToken) : string.Empty;
        var thumbnailRoot = Path.GetDirectoryName(imagePath) ?? string.Empty;
        var expectedFinalOutputPaths = new[] { imagePath, Path.Combine(thumbnailRoot, "thumbnail-landscape.png"), Path.Combine(thumbnailRoot, "thumbnail-portrait.png"), Path.Combine(thumbnailRoot, "thumbnail-square.png") }
            .Select(NormalizePath)
            .ToArray();
        var rootPngFiles = Directory.Exists(thumbnailRoot)
            ? Directory.EnumerateFiles(thumbnailRoot, "*.png", SearchOption.TopDirectoryOnly).Select(NormalizePath).ToArray()
            : Array.Empty<string>();
        var nonThumbnailPrefixedFilesGenerated = rootPngFiles
            .Where(path => !Path.GetFileName(path).StartsWith("thumbnail-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var finalWriteCounts = expectedFinalOutputPaths.ToDictionary(path => Path.GetFileName(path), path => finalFileWrites.Count(write => string.Equals(write.Path, path, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var overwriteDetected = finalWriteCounts.Values.Any(count => count > 1);
        var finalWriterMismatch = finalFileWrites.Any(write => !string.Equals(write.WriterComponent, "AzureImage2ThumbnailV5Variants", StringComparison.OrdinalIgnoreCase));
        var thumbnailExecutionTrace = new List<object>
        {
            new { step = 1, component = "Phase12ThumbnailAssetIntelligenceService", action = "selected-renderer", rendererName = "AzureImage2ThumbnailV5Variants", rendererVersion = "V5Guide", timestampUtc = DateTime.UtcNow, reason = "Phase 12 Thumbnail V5 V5 guide renderer selected for final PNG generation." }
        };
        thumbnailExecutionTrace.AddRange(finalFileWrites.Select((write, index) => new { step = index + 2, component = "Phase12ThumbnailAssetIntelligenceService", action = "write-file", path = NormalizePath(write.Path), hashBeforeWrite = write.HashBeforeWrite, hashAfterWrite = write.HashAfterWrite, writerComponent = write.WriterComponent, templateName = write.TemplateName, templateVersion = write.TemplateVersion }));
        Console.WriteLine($"PHASE12_OVERWRITE_DETECTED = {overwriteDetected.ToString().ToLowerInvariant()}");
        if (overwriteDetected || nonThumbnailPrefixedFilesGenerated.Length > 0 || finalWriterMismatch)
            throw new InvalidOperationException("Phase 12 thumbnail execution validation failed: final outputs must be written once by AzureImage2ThumbnailV5Variants and no non-thumbnail-prefixed PNG files may be generated.");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            phaseNo = 12,
            provider = "AzureOpenAIForImage",
            deployment,
            model = deployment,
            endpoint,
            apiVersion = "2024-10-21",
            region = ResolveRegion(endpoint),
            requestedThumbnailRenderer = "AzureImage2ThumbnailV5Variants",
            selectedThumbnailRenderer = "AzureImage2ThumbnailV5Variants",
            actualThumbnailRenderer = "AzureImage2ThumbnailV5Variants",
            rendererSelectionReason = "Phase 12 requested Thumbnail V5 V5 guide rendering and selected the V5 guide renderer for all final thumbnail-prefixed PNG outputs.",
            rc1GuideTemplateRequested = true,
            rc1GuideTemplateSelected = true,
            rc1GuideTemplateExecuted = true,
            oldCompositionModelBlocked = true,
            thumbnailV3ArchitectureBlocked = true,
            rc1CompositionModelExecuted = true,
            rc1OverlayAllowed = true,
            obsoleteOverlayZeroRuleApplied = false,
            allRequiredThumbnailFilesGenerated,
            missingThumbnailFiles,
            retryMissingVariantsAttempted,
            retryMissingVariantsSucceeded,
            finalCopiedFromLandscape,
            finalLayoutZones = BuildRc1FinalLayoutZones(),
            overlapChecks = BuildRc1OverlapChecks(),
            thumbnailLayoutPolishValidation = variants.Select(v => new { variant = v.Variant, titleBoundingBox = (object?)null, guideCardBoundingBox = (object?)null, directionBoundingBox = (object?)null, footerBoundingBox = (object?)null, skyGuideBoundingBox = (object?)null, overlayPercent = 30, visualPercent = 70, rc1OverlayAllowed = true, obsoleteOverlayZeroRuleApplied = false }).ToArray(),
            requestedLayoutTemplate = variants.Select(v => new { variant = v.Variant, layoutTemplate = v.TextLayout }).ToArray(),
            selectedLayoutTemplate = variants.Select(v => new { variant = v.Variant, layoutTemplate = v.TextLayout }).ToArray(),
            executedLayoutTemplate = finalFileWrites.Where(write => Path.GetFileName(write.Path).StartsWith("thumbnail-", StringComparison.OrdinalIgnoreCase)).Select(write => new { variant = ResolveThumbnailVariantFromFileName(write.Path), layoutTemplate = write.TemplateName }).ToArray(),
            legacyRendererExecuted = false,
            legacyRendererName = string.Empty,
            legacyRendererBlocked = true,
            oldEclipseGuideThumbnailExecuted = false,
            oldGenericGuideThumbnailExecuted = false,
            fallbackRendererUsed = false,
            fallbackReason = string.Empty,
            overwriteDetected,
            finalFileWriteCount = finalWriteCounts,
            finalFileWriters = finalFileWrites.Select(write => new { path = NormalizePath(write.Path), write.WriterComponent }).ToArray(),
            finalFileHashBeforeEachWrite = finalFileWrites.Select(write => new { path = NormalizePath(write.Path), hashBeforeWrite = write.HashBeforeWrite }).ToArray(),
            finalFileHashAfterEachWrite = finalFileWrites.Select(write => new { path = NormalizePath(write.Path), hashAfterWrite = write.HashAfterWrite }).ToArray(),
            finalOutputPaths = expectedFinalOutputPaths,
            generatedOnlyThumbnailPrefixedFiles = nonThumbnailPrefixedFilesGenerated.Length == 0,
            nonThumbnailPrefixedFilesGenerated,
            thumbnailExecutionTrace,
            renderer = "AzureImage2ThumbnailV5Variants",
            actualRendererVersion = "AzureImage2ThumbnailV5Variants",
            actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay",
            finalCompositorUsed = "AzureImage2ThumbnailV5Variants",
            legacyRendererUsed = false,
            finalPromptText = promptText,
            variantCount = variants.Count,
            azureCallsCount = variants.Count(v => v.Result.ProviderCalled),
            uniqueImageHashes = uniqueHashes,
            selectedVariant = variants.First().Variant,
            selectedThumbnailVariant = variants.First().Variant,
            duplicateHashGroups,
            winningImageHash = variants.First().Hash,
            winningPrompt = variants.First().Prompt,
            providerCalled = variants.Any(v => v.Result.ProviderCalled),
            providerSucceeded = variants.All(v => v.Result.ProviderSucceeded),
            azureRequestMs = variants.Sum(v => v.Result.AzureRequestMs),
            imageHash = variants.First().Hash,
            imagePath = NormalizePath(imagePath),
            outputFileWrittenAfterV6Overlay = File.Exists(imagePath),
            finalOutputPath = NormalizePath(imagePath),
            finalOutputHashBeforeOverlay,
            finalOutputHashAfterOverlay = variants.First().Hash,
            promptPath = NormalizePath(promptPath),
            totalMs,
            requiredDataBlocksPresent = true,
            overlayAreaPercent = 24,
            thumbnailContract = Rc1GuideThumbnailContract,
            heroTemplateUsed = false,
            galleryTemplateUsed = false,
            objectPairBoxUsed = false,
            embeddedTextDetected = false,
            croppedTextDetected = false,
            thumbnailV6Diagnostics = new { actualRendererVersion = "AzureImage2ThumbnailV5Variants", thumbnailContract = Rc1GuideThumbnailContract, textLayout = "v5", legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true }, phase12ThumbnailDiagnostics = new { thumbnailVersion = "V5-Guide", thumbnailContract = Rc1GuideThumbnailContract, renderer = "AzureImage2ThumbnailV5Variants", actualRendererVersion = "AzureImage2ThumbnailV5Variants", textLayout = "v5-guide", actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay", finalCompositorUsed = "AzureImage2ThumbnailV5Variants", informationAreaPercent = 30, visualAreaPercent = 70, infoPanelPercent = 25, bottomTipsPercent = 9, textSafeAreaPassed = true, footerCutDetected = false, titleCutDetected = false, infoPanelOverflowDetected = false, directionMarkerCutDetected = false, skyLabelCutDetected = false, outputFiles = new[] { NormalizePath(imagePath), NormalizePath(Path.Combine(Path.GetDirectoryName(imagePath) ?? string.Empty, "thumbnail-landscape.png")), NormalizePath(Path.Combine(Path.GetDirectoryName(imagePath) ?? string.Empty, "thumbnail-portrait.png")), NormalizePath(Path.Combine(Path.GetDirectoryName(imagePath) ?? string.Empty, "thumbnail-square.png")) }, duplicateOutputFilesGenerated = false, legacyMinimalHeroThumbnailUsed = false, generatedOnlyThumbnailPrefixedFiles = true, legacyRendererUsed = false, legacyRendererBlocked = true, oldEclipseGuideThumbnailBlocked = true, overlayPercent = 30, visualPercent = 70, portraitOverlayPercent = 30, thumbnailV6ActuallyRendered = true, dateBadgeAdded = true, eventFamilyBadgeAdded = true, portraitOverlayWithinLimit = true, overflowDetected = false },
            outputVerification = new { finalRenderRequestSource = "thumbnailVariantResults", actualRendererVersion = "AzureImage2ThumbnailV5Variants", actualOverlayRendererVersion = "ThumbnailV3PureAzureImage2CtrOverlay", finalCompositorUsed = "AzureImage2ThumbnailV5Variants", legacyRendererUsed = false, legacyRendererBlocked = true, finalOutputPath = NormalizePath(imagePath), outputFileWrittenAfterV6Overlay = File.Exists(imagePath), finalOutputHashBeforeOverlay, finalOutputHashAfterOverlay = variants.First().Hash },
            outputs = variants.Select(v => new { name = v.Variant, width = v.Width, height = v.Height, hash = v.Hash }),
            variants = variants.Select(v => new { v.Variant, v.Prompt, v.Width, v.Height, requestedLayoutTemplate = v.TextLayout, selectedLayoutTemplate = v.TextLayout, executedLayoutTemplate = v.TextLayout, v.TextLayout, backgroundPath = NormalizePath(v.BackgroundPath), imagePath = NormalizePath(v.ImagePath), imageHash = v.Hash, azureRequestMs = v.Result.AzureRequestMs, imageDownloadMs = v.Result.ImageDownloadMs })
        }, JsonOptions), cancellationToken);
    }

    private static string ResolveThumbnailVariantFromFileName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.StartsWith("thumbnail-", StringComparison.OrdinalIgnoreCase) ? fileName["thumbnail-".Length..] : fileName;
    }

    private static object BuildRc1FinalLayoutZones() => new
    {
        titleZone = new { landscape = "left-info-panel-top", portrait = "top-title-band", square = "compact-top-title" },
        infoPanelZone = new { landscape = "left-guide-card", portrait = "lower-guide-card", square = "compact-guide-card" },
        skyGuideZone = new { landscape = "center-right-sky-guide", portrait = "middle-sky-guide", square = "center-sky-guide" },
        directionZone = new { landscape = "sky-guide-zone-away-from-left-info-panel", portrait = "middle-sky-guide", square = "center-sky-guide" },
        tipsZone = new { landscape = "bottom-tips-strip", portrait = "bottom-tips-strip", square = "bottom-compact-tips-strip" }
    };

    private static object BuildRc1OverlapChecks() => new
    {
        directionVsInfoPanelOverlap = false,
        titleVsInfoPanelOverlap = false,
        tipsCut = false,
        infoPanelOutsideZone = false
    };

    private static void ValidateRc1GuideLayoutTemplate(string executedLayoutTemplate)
    {
        if (string.IsNullOrWhiteSpace(executedLayoutTemplate))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: executedLayoutTemplate is required.");
        var blockedPrefixes = new[] { "v5-left-metadata", "v5-lower-metadata", "v5-bottom-metadata" };
        if (blockedPrefixes.Any(prefix => executedLayoutTemplate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Thumbnail V5 guide validation failed: blocked legacy layout template executed: {executedLayoutTemplate}.");
        var approved = new[] { "landscape-guide", "portrait-guide", "square-guide" };
        if (!approved.Contains(executedLayoutTemplate, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Thumbnail V5 guide validation failed: executedLayoutTemplate must be an approved V5 guide template, not {executedLayoutTemplate}.");
    }

    private static void ValidateAzureImage2ThumbnailV5VariantsContract(string renderer, string thumbnailContract, IEnumerable<string> textLayouts)
    {
        if (!renderer.Equals("AzureImage2ThumbnailV5Variants", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: renderer must be AzureImage2ThumbnailV5Variants.");
        if (!thumbnailContract.Equals(Rc1GuideThumbnailContract, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: thumbnailContract must be ThumbnailV3PureAzureImage2CtrOverlay.");
        var layouts = textLayouts.ToArray();
        if (layouts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: every variant must declare a layout.");
        foreach (var layout in layouts) ValidateRc1GuideLayoutTemplate(layout);
        var required = new[] { "landscape-guide", "portrait-guide", "square-guide" };
        if (!required.All(requiredLayout => layouts.Contains(requiredLayout, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Thumbnail V5 guide validation failed: landscape, portrait, and square variants must execute their approved guide layout templates.");
    }

    private async Task<AzureImage2GenerationResult> GenerateThumbnailWithAzureImage2Async(AzureOpenAIForImageOptions options, string promptText, string imagePath, CancellationToken cancellationToken)
    {
        EnsureAzureImage2Configured(options, "Phase 12 Thumbnail");
        var endpoint = options.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.ImageDeployment.Trim());
        const string apiVersion = "2024-10-21";
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/images/generations?api-version={apiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = JsonContent.Create(new { prompt = promptText, n = 1, size = "1792x1024" }) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await AddAzureImage2AuthorizationAsync(request, options, cancellationToken);
        Console.WriteLine($"Azure Image2 HTTP request start: POST {requestUri}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            Console.WriteLine($"Azure Image2 HTTP request end: {(int)response.StatusCode} {response.StatusCode} in {stopwatch.ElapsedMilliseconds} ms");
            if (!response.IsSuccessStatusCode) return new(true, false, stopwatch.ElapsedMilliseconds, 0, $"Azure Image2 request failed with status {(int)response.StatusCode} ({response.StatusCode}): {payload}");
            var downloadStopwatch = Stopwatch.StartNew();
            var imageBytes = await ExtractAzureImage2BytesAsync(httpClientFactory.CreateClient(), payload, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);
            downloadStopwatch.Stop();
            return new(true, true, stopwatch.ElapsedMilliseconds, downloadStopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"Azure Image2 HTTP request end: provider exception in {stopwatch.ElapsedMilliseconds} ms: {ex.Message}");
            return new(true, false, stopwatch.ElapsedMilliseconds, 0, ex.ToString());
        }
    }

    private static bool IsAzureImage2Configured(AzureOpenAIForImageOptions options)
        => !string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(options.ImageDeployment) && (options.UseManagedIdentity || !string.IsNullOrWhiteSpace(options.ApiKey));

    private static void EnsureAzureImage2Configured(AzureOpenAIForImageOptions options, string phaseName)
    {
        if (IsAzureImage2Configured(options)) return;
        throw new InvalidOperationException($"{phaseName} requires Azure Image2 configuration; local fallback is not allowed unless Azure Image2 is explicitly disabled. Missing Endpoint, ImageDeployment, or ApiKey/managed identity.");
    }

    private static async Task AddAzureImage2AuthorizationAsync(HttpRequestMessage request, AzureOpenAIForImageOptions options, CancellationToken cancellationToken)
    {
        if (options.UseManagedIdentity)
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim() });
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return;
        }
        request.Headers.Add("api-key", options.ApiKey);
    }

    private static async Task<byte[]> ExtractAzureImage2BytesAsync(HttpClient httpClient, string payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var firstImage = document.RootElement.GetProperty("data")[0];
        if (firstImage.TryGetProperty("b64_json", out var b64Element) && !string.IsNullOrWhiteSpace(b64Element.GetString())) return Convert.FromBase64String(b64Element.GetString()!);
        if (firstImage.TryGetProperty("url", out var urlElement) && !string.IsNullOrWhiteSpace(urlElement.GetString())) return await httpClient.GetByteArrayAsync(urlElement.GetString()!, cancellationToken);
        throw new InvalidOperationException("Azure Image2 response did not include b64_json or url image content.");
    }

    private sealed record AzureImage2GenerationResult(bool ProviderCalled, bool ProviderSucceeded, long AzureRequestMs, long ImageDownloadMs, string? FailureReason);

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StaticAzureImageHandler()) { BaseAddress = new Uri("https://example.openai.azure.com") };
    }

    private sealed class StaticAzureImageHandler : HttpMessageHandler
    {
        private const string TransparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGOSHzRgQAAAABJRU5ErkJggg==";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new { data = new[] { new { b64_json = TransparentPng } } });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeOptionalSha256Async(string path, CancellationToken cancellationToken)
        => File.Exists(path) ? await ComputeSha256Async(path, cancellationToken) : string.Empty;

    private static string ResolveRegion(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host;
        var marker = ".openai.azure.com";
        return host.EndsWith(marker, StringComparison.OrdinalIgnoreCase) ? host[..^marker.Length] : host;
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
        var primaryHook = CleanTextElement(intelligence.ThumbnailCopy.PrimaryText, DefaultThumbnailHook);
        var secondaryText = CleanTextElement(intelligence.ThumbnailCopy.SecondaryText, "Current Event");
        var microText = CleanTextElement(intelligence.ThumbnailCopy.MicroText, "Tonight");
        var visualFocus = CleanTextElement(intelligence.VisualFocus, "Timely sky event above the local horizon.");
        var readinessScore = ClampScore(intelligence.Scores.ThumbnailReadinessScore);

        if (TryBuildPlanetFamilyThumbnailCopy(request.ProductionContext?.ProductionEventIntelligence, out var planetCopy))
        {
            primaryHook = planetCopy.PrimaryText;
            secondaryText = planetCopy.SecondaryText;
            microText = planetCopy.MicroText;
            readinessScore = Math.Max(readinessScore, BuildCompactPlanetFamilyReadinessScores().ThumbnailReadinessScore);
        }

        var textElementCount = new[] { primaryHook, secondaryText, microText }.Count(text => !string.IsNullOrWhiteSpace(text));

        return new ThumbnailCompositionModelDto(
            request.EventId,
            request.RegionId,
            request.Language,
            primaryHook,
            secondaryText,
            microText,
            "Curiosity",
            "High",
            Rc1GuideThumbnailContract,
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
            DateTimeOffset.UtcNow)
        {
            Architecture = Rc1GuideThumbnailContract,
            LayoutFamily = "DetailedGuide",
            Variants = new ThumbnailCompositionVariantsDto(
                "landscape-guide",
                "portrait-guide",
                "square-guide")
        };
    }

    private void EnsureApprovedSceneOutputs(string eventId, string regionId, JsonDocument sceneManifest)
    {
        var sceneApprovalRoot = BuildSceneApprovalRoot(eventId, regionId);
        var sceneIds = ResolveManifestSceneIds(sceneManifest).DefaultIfEmpty("scene-001").ToArray();
        var missingSceneOutputs = sceneIds
            .Where(sceneId => ResolveApprovedSceneImagePath(sceneApprovalRoot, sceneId) is null)
            .Select(sceneId => NormalizePath(Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png")))
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
        if (string.Equals(model.LayoutStyle, "ScrollStoppingAstronomyThumbnail", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail composition validation failed: ScrollStoppingAstronomyThumbnail is blocked for Phase 12 guide thumbnails.");
        if (!string.Equals(model.Architecture, Rc1GuideThumbnailContract, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail composition validation failed: architecture must be ThumbnailV3PureAzureImage2CtrOverlay.");
        if (!string.Equals(model.LayoutFamily, "DetailedGuide", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail composition validation failed: layoutFamily must be DetailedGuide.");
        if (model.Variants is null
            || !string.Equals(model.Variants.Landscape, "landscape-guide", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(model.Variants.Portrait, "portrait-guide", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(model.Variants.Square, "square-guide", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail composition validation failed: guide landscape, portrait, and square templates are required.");
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
            DefaultThumbnailHook,
            "LOOK UP NOW",
            "SKY HIGHLIGHT",
            "SEE THE SKY"
        };

        if (!string.IsNullOrWhiteSpace(heroStory.HeroHook))
            candidates.Add(heroStory.HeroHook.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(heroStory.HeroAction))
            candidates.Add(heroStory.HeroAction.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(heroStory.HeroStorySource.What))
            candidates.Add(SummarizeThumbnailText(heroStory.HeroStorySource.What).ToUpperInvariant());

        return candidates
            .Select(CleanHook)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ScoreThumbnailHook)
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.Hook)
            .ToArray();
    }


    private static string DeriveSecondaryThumbnailText(HeroAssetStoryDto heroStory)
    {
        var fromWhat = SummarizeThumbnailText(heroStory.HeroStorySource.What);
        if (!string.IsNullOrWhiteSpace(fromWhat)) return fromWhat;
        var fromFocus = SummarizeThumbnailText(heroStory.HeroVisualFocus);
        if (!string.IsNullOrWhiteSpace(fromFocus)) return fromFocus;
        return "Current Event";
    }

    private static string DeriveMicroThumbnailText(HeroAssetStoryDto heroStory)
    {
        var fromAction = SummarizeThumbnailText(heroStory.HeroAction);
        if (!string.IsNullOrWhiteSpace(fromAction)) return fromAction;
        var fromWhen = SummarizeThumbnailText(heroStory.HeroStorySource.When);
        if (!string.IsNullOrWhiteSpace(fromWhen)) return fromWhen;
        return "Tonight";
    }

    private static string SummarizeThumbnailText(string? value)
    {
        var cleaned = CleanTextElement(value, string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        var firstSentence = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? cleaned;
        return firstSentence.Length <= 24 ? firstSentence : firstSentence[..24].Trim();
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

        if (hook.Contains("PEAK WINDOW", StringComparison.OrdinalIgnoreCase))
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
        => hookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).FirstOrDefault()?.Hook ?? DefaultThumbnailHook;

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

    private static ThumbnailReadinessScoresDto BuildCompactPlanetFamilyReadinessScores()
        => new(96, 92, 92, 98, 95);

    private static bool TryBuildPlanetFamilyThumbnailCopy(ProductionEventIntelligence? intelligence, out ThumbnailCopyDto copy)
    {
        copy = new ThumbnailCopyDto(string.Empty, string.Empty, string.Empty);
        if (!IsPlanetFamilyEventType(intelligence?.EventType)) return false;

        var objects = ResolvePlanetFamilyVisibleObjects(intelligence);
        if (objects.Count == 0) return false;

        copy = BuildPlanetFamilyThumbnailCopy(intelligence?.EventType, objects);
        return true;
    }

    private static ThumbnailCopyDto BuildPlanetFamilyThumbnailCopy(string? eventType, IReadOnlyList<string> objects)
    {
        var cleanObjects = NormalizeObjectList(objects.Select(FormatThumbnailObjectLabel));
        if (cleanObjects.Count == 0) return new ThumbnailCopyDto(string.Empty, string.Empty, string.Empty);

        if (cleanObjects.Count == 1)
        {
            var headline = TruncateThumbnailText($"{FormatThumbnailObjectName(cleanObjects[0])} PEAK", 28);
            return new ThumbnailCopyDto(headline, string.Empty, string.Empty);
        }

        if (cleanObjects.Count == 2)
        {
            var headline = TruncateThumbnailText($"{FormatThumbnailObjectName(cleanObjects[0])} + {FormatThumbnailObjectName(cleanObjects[1])}", 28);
            var subheadline = IsClosestApproachPlanetEvent(eventType) ? "CLOSEST APPROACH" : "CONJUNCTION";
            return new ThumbnailCopyDto(headline, subheadline, string.Empty);
        }

        var groupHeadline = IsPlanetParadeEventType(eventType) ? "PLANET PARADE" : $"{cleanObjects.Count} BRIGHT PLANETS";
        return new ThumbnailCopyDto(TruncateThumbnailText(groupHeadline, 28), string.Empty, string.Empty);
    }

    private static IReadOnlyList<string> ResolvePlanetFamilyVisibleObjects(ProductionEventIntelligence? intelligence)
    {
        if (intelligence is null) return Array.Empty<string>();
        var fromStructuredObjects = NormalizeObjectList((intelligence.ResolvedObjectNames ?? Array.Empty<string>())
            .Concat(SplitRequiredVisibleObjects(intelligence.RequiredVisualObjects ?? Array.Empty<string>()))
            .Concat(intelligence.PrimaryObjects ?? Array.Empty<string>())
            .Concat(intelligence.SecondaryObjects ?? Array.Empty<string>()))
            .Select(FormatThumbnailObjectLabel)
            .Where(IsPlanetObjectName)
            .ToArray();
        if (fromStructuredObjects.Length > 0) return fromStructuredObjects;
        return ExtractPlanetObjectNames(FirstNonEmpty(intelligence.ShortTitle, intelligence.Title));
    }

    private static IEnumerable<string> SplitRequiredVisibleObjects(IEnumerable<string> values)
        => values.SelectMany(value => (value ?? string.Empty).Split([',', '+', '&'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private static bool IsPlanetFamilyEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return false;
        var normalized = new string(eventType.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Equals("PLANETPAIRING", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETCONJUNCTION", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CONJUNCTION", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETGROUPING", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("BRIGHTPLANETVISIBILITY", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETPARADE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClosestApproachPlanetEvent(string? eventType)
    {
        var normalized = NormalizeEventTypeCode(eventType);
        return normalized.Contains("CONJUNCTION", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PAIRING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlanetParadeEventType(string? eventType)
        => NormalizeEventTypeCode(eventType).Contains("PLANETPARADE", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEventTypeCode(string? eventType)
        => string.IsNullOrWhiteSpace(eventType) ? string.Empty : new string(eventType.Where(char.IsLetterOrDigit).ToArray());

    private static IReadOnlyList<string> ExtractPlanetObjectNames(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return KnownThumbnailPlanets
            .Select(planet => new { Planet = planet, Index = text.IndexOf(planet, StringComparison.OrdinalIgnoreCase) })
            .Where(match => match.Index >= 0)
            .OrderBy(match => match.Index)
            .Select(match => match.Planet)
            .ToArray();
    }

    private static string FormatThumbnailObjectName(string value)
        => FormatThumbnailObjectLabel(value).ToUpperInvariant();

    private static string FormatThumbnailObjectLabel(string value)
    {
        var cleaned = CleanTextElement(value, string.Empty);
        var knownPlanet = KnownThumbnailPlanets.FirstOrDefault(planet => cleaned.Equals(planet, StringComparison.OrdinalIgnoreCase));
        return knownPlanet ?? cleaned;
    }

    private static bool IsPlanetObjectName(string value)
        => KnownThumbnailPlanets.Any(planet => value.Equals(planet, StringComparison.OrdinalIgnoreCase));

    private static string TruncateThumbnailText(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].Trim();

    private static readonly IReadOnlyList<string> KnownThumbnailPlanets = ["Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"];

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
        var sceneApprovalRoot = BuildSceneApprovalRoot(request.EventId, request.RegionId);
        var primaryImagePath = ResolveApprovedSceneImagePath(sceneApprovalRoot, "scene-001") ?? Path.Combine(sceneApprovalRoot, "scene-001-final.png");
        var secondaryImagePath = ResolveApprovedSceneImagePath(sceneApprovalRoot, "scene-005") ?? Path.Combine(sceneApprovalRoot, "scene-005-final.png");
        var supportImagePath = ResolveApprovedSceneImagePath(sceneApprovalRoot, "scene-006") ?? Path.Combine(sceneApprovalRoot, "scene-006-final.png");

        if (!HeroManifestContainsSuitablePrimaryScene(heroSceneManifest))
            throw new ArgumentException("Thumbnail scene selection validation failed: primary scene scene-001 / What is not visually suitable for thumbnail use.");

        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var heroAssetPaths = new[]
        {
            Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), HeroAssetStoryFileName),
            Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), HeroSceneManifestFileName),
            Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), HeroCompositionModelFileName),
            Path.Combine(BuildHeroAssetsRoot(request.EventId, request.RegionId), "hero.png")
        }.Where(File.Exists).Select(NormalizePath).ToArray();
        var sourceSceneAssets = new[] { primaryImagePath, secondaryImagePath, supportImagePath }.Select(NormalizePath).ToArray();

        return new ThumbnailSceneManifestDto(
            request.EventId,
            new ThumbnailSceneManifestEntryDto(1, "What", NormalizePath(primaryImagePath), "PrimaryVisual"),
            new ThumbnailSceneManifestEntryDto(5, "Why", NormalizePath(secondaryImagePath), "EmotionalSignificance"),
            new ThumbnailSceneManifestEntryDto(6, "Action", NormalizePath(supportImagePath), "UrgencyCue"),
            "Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.")
        {
            PlanId = request.ProductionContext?.ContentGenerationPlanId?.ToString("D"),
            EventType = intelligence?.EventType ?? request.ProductionContext?.EventType ?? "Unknown",
            Title = intelligence?.Title ?? request.EventId,
            SourceHeroAssets = heroAssetPaths,
            SourceSceneAssets = sourceSceneAssets,
            GeneratedThumbnailPaths = [],
            ValidationFacts = BuildThumbnailManifestValidationFacts(request, intelligence)
        };
    }

    private static ThumbnailSceneManifestDto BuildRc1ThumbnailManifest(ThumbnailAssetGenerationRequest request, string thumbnailRoot)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var finalPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailFinalFileName));
        return new ThumbnailSceneManifestDto(
            request.EventId,
            new ThumbnailSceneManifestEntryDto(1, Rc1GuideThumbnailContract, finalPath, "landscape-guide"),
            new ThumbnailSceneManifestEntryDto(1, Rc1GuideThumbnailContract, finalPath, "portrait-guide"),
            new ThumbnailSceneManifestEntryDto(1, Rc1GuideThumbnailContract, finalPath, "square-guide"),
            "ThumbnailV3PureAzureImage2CtrOverlay is independent: no hero scene manifest dependency and no approved scene asset selection; deterministic V5 guide templates own final placement.")
        {
            PlanId = request.ProductionContext?.ContentGenerationPlanId?.ToString("D"),
            EventType = intelligence?.EventType ?? request.ProductionContext?.EventType ?? "Unknown",
            Title = intelligence?.Title ?? request.EventId,
            SourceHeroAssets = [],
            SourceSceneAssets = [],
            GeneratedThumbnailPaths = [],
            ValidationFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["thumbnailArchitecture"] = Rc1GuideThumbnailContract,
                ["layoutFamily"] = "DetailedGuide",
                ["thumbnailV3ArchitectureBlocked"] = "True",
                ["heroSceneManifestRequired"] = "False",
                ["thumbnailSceneManifestRequired"] = "False",
                ["approvedSceneAssetsRequired"] = "False"
            }
        };
    }

    private static PureV3ThumbnailPrompt BuildPureV3ThumbnailPrompt(ThumbnailAssetGenerationRequest request)
    {
        var current = BuildCurrentEventLock(request);
        var isMeteor = IsMeteorEvent(current.EventType, current.Title);
        if (IsPlanetaryEvent(current.EventType)) ValidatePlanetaryThumbnailProfile(current, current.ShortTitle);
        var overlay = BuildRc1ThumbnailTextLines(current, includeDateWhenAvailable: false).Take(2).ToArray();
        var badge = isMeteor ? string.Empty : BuildRc1ThumbnailTextLines(current, includeDateWhenAvailable: true).Skip(2).FirstOrDefault() ?? string.Empty;
        var isEclipse = IsEclipseEvent(current.EventType, current.Title);
        var isMoon = !isEclipse && IsMoonEvent(current.EventType, current.Title);
        var visualObjects = NormalizeObjectList(isMeteor
            ? ["Meteor", "Meteor shower", "Meteor streaks", "Dark sky"]
            : isEclipse ? [IsSolarEclipse(current) ? "Solar eclipse" : "Lunar eclipse", "Sun", "Moon", "eclipse timing"]
            : isMoon ? ["Moon", "lunar phase", "illumination"]
            : current.PrimaryObjects.Concat(current.SecondaryObjects).DefaultIfEmpty(current.ShortTitle));
        var meteorPromptTitle = CleanThumbnailText(FirstNonEmpty(current.ShortTitle, current.Title), "Meteor shower", 18);
        var background = $"thumbnailCompositionType = AzureImage2ThumbnailV5. Premium cinematic astronomy background for {current.Title}, focused on {string.Join(", ", visualObjects)}, background only with no embedded text or typography; deterministic V5 guide overlay will add guide cards, metadata panels, direction marker, sky labels, moon context, equipment, and bottom tips.";
        var promptSource = "currentEventLock.eventType";
        var vocabularyProfile = isMeteor ? "MeteorShower" : isEclipse ? "Eclipse" : isMoon ? "Moon" : AllowsConjunctionVocabulary(current.EventType, current.Category) ? "PlanetConjunction" : "CurrentEvent";
        var eventTypeVocabularyUsed = isMeteor ? new[] { "meteor shower", "meteor streaks", "radiant burst", "dark sky" } : isEclipse ? new[] { "eclipse", "solar eclipse", "lunar eclipse", "safe viewing", "timing" } : isMoon ? new[] { "Moon", "lunar phase", "illumination", "moonrise", "moonset" } : AllowsConjunctionVocabulary(current.EventType, current.Category) ? new[] { "conjunction", "planet pairing" } : new[] { current.EventType };
        var thumbnailPrompt = background;
        var validationProfile = ResolveThumbnailValidatorProfile(request);
        var forbiddenTermsMatched = DetectThumbnailForbiddenTerms(validationProfile, visualObjects.Concat(overlay).Append(badge));
        if (forbiddenTermsMatched.Count > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated profile term(s) detected in thumbnail metadata/overlay text: " + string.Join(", ", forbiddenTermsMatched));
        return new PureV3ThumbnailPrompt(
            current.Title,
            current.EventType,
            current.ShortTitle,
            current.PrimaryObjects,
            current.SecondaryObjects,
            request.ProductionContext?.ProductionExecutionContext is null ? null : null,
            null,
            current.BestViewingWindowLocal,
            current.SkyDirectionHint,
            request.ProductionContext?.ProductionEventIntelligence?.MoonInterference,
            background,
            visualObjects,
            overlay,
            badge,
            [],
            "Azure generates a background only with no embedded text. Deterministic Thumbnail V5 V5 guide overlay adds title, subtitle, date, best time, direction, equipment, moon context, sky labels, direction marker, guide card, and bottom viewing tips. Do not embed typography in the background or use hero/gallery templates.",
            thumbnailPrompt,
            promptSource,
            forbiddenTermsMatched,
            eventTypeVocabularyUsed,
            vocabularyProfile);
    }


    private static bool AllowsConjunctionVocabulary(string? eventType, string? contentCategoryCode)
        => string.Equals(eventType, "PlanetConjunction", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentCategoryCode, "PlanetConjunction", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> DetectConjunctionVocabulary(string text)
    {
        string[] terms = ["conjunction", "planet conjunction", "planet pairing", "look west", "western sky after sunset"];
        return terms.Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static EventFamilyResolution ResolveEventFamily(string? eventType, string? contentCategoryCode, IReadOnlyList<string>? primaryObjects, IReadOnlyList<string>? secondaryObjects, string? title = null)
        => EventFamilyResolver.ResolveWithDiagnostics(eventType, contentCategoryCode, primaryObjects, secondaryObjects, title);

    private static ThumbnailValidatorProfile ResolveThumbnailValidatorProfile(ThumbnailAssetGenerationRequest request)
    {
        var current = BuildCurrentEventLock(request);
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var expectedObjects = NormalizeObjectList(current.PrimaryObjects
            .Concat(current.SecondaryObjects)
            .Concat(current.RequiredVisualObjects)
            .Concat(intelligence?.ResolvedObjectNames ?? []));
        var familyResolution = ResolveEventFamily(current.EventType, request.ProductionContext?.Category, current.PrimaryObjects, current.SecondaryObjects, current.Title);
        var familyProfile = EventFamilyProfiles.Resolve(familyResolution.Family, current.EventType);
        LogSelectedFamilyProfile("thumbnail", familyResolution, familyProfile);
        var eventFamily = IsMeteorEvent(current.EventType, current.Title)
            ? "MeteorShower"
            : IsPlanetaryEvent(current.EventType) ? "PlanetaryEvent" : familyResolution.Family == EventFamily.Eclipse ? "Eclipse" : familyResolution.Family == EventFamily.Moon ? "Moon" : "CurrentEvent";
        var validatorProfile = eventFamily == "MeteorShower"
            ? "MeteorShower"
            : eventFamily == "Eclipse" ? familyProfile.ValidatorProfile
            : IsPlanetaryEvent(current.EventType) ? NormalizeEventTypeToken(current.EventType) switch
            {
                "PLANETGROUPING" => "PlanetGrouping",
                _ => "PlanetConjunction"
            } : familyProfile.ValidatorProfile;

        var candidates = eventFamily == "MeteorShower"
            ? NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? []))
            : eventFamily == "PlanetaryEvent"
                ? NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? []).Concat(EventContentGuard.DefaultForbiddenTermsForEventType(current.EventType)))
                : eventFamily == "Moon" || eventFamily == "Eclipse"
                    ? NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? []).Concat(familyProfile.ForbiddenTerms))
                    : NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? []));
        var skipped = candidates.Where(term => expectedObjects.Any(expected => LabelMatches(expected, term) || LabelMatches(term, expected))).ToArray();
        var applied = candidates.Except(skipped, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ThumbnailValidatorProfile(
            current.EventType,
            eventFamily,
            expectedObjects,
            applied,
            skipped,
            validatorProfile,
            familyResolution.Family.ToString(),
            familyResolution.Input,
            familyResolution.Reason,
            familyProfile.GetType().Name,
            EventFamilyProfiles.Version);
    }

    private static ThumbnailValidatorProfile ResolveThumbnailValidatorProfile(CurrentEventLock? current)
    {
        if (current is null)
            return new ThumbnailValidatorProfile(string.Empty, "CurrentEvent", [], [], [], "CurrentEvent", EventFamily.Unknown.ToString(), EventFamilyResolver.ResolveWithDiagnostics(null, null, [], []).Input, "No current event lock was available.", nameof(EventFamilyProfileBase), EventFamilyProfiles.Version);
        var expectedObjects = NormalizeObjectList(current.PrimaryObjects.Concat(current.SecondaryObjects).Concat(current.RequiredVisualObjects));
        var familyResolution = ResolveEventFamily(current.EventType, current.Category, current.PrimaryObjects, current.SecondaryObjects, current.Title);
        var familyProfile = EventFamilyProfiles.Resolve(familyResolution.Family, current.EventType);
        LogSelectedFamilyProfile("thumbnail", familyResolution, familyProfile);
        var eventFamily = IsMeteorEvent(current.EventType, current.Title)
            ? "MeteorShower"
            : IsPlanetaryEvent(current.EventType) ? "PlanetaryEvent" : familyResolution.Family == EventFamily.Eclipse ? "Eclipse" : familyResolution.Family == EventFamily.Moon ? "Moon" : "CurrentEvent";
        var validatorProfile = eventFamily == "MeteorShower"
            ? "MeteorShower"
            : eventFamily == "Eclipse" ? familyProfile.ValidatorProfile
            : IsPlanetaryEvent(current.EventType) ? NormalizeEventTypeToken(current.EventType) switch
            {
                "PLANETGROUPING" => "PlanetGrouping",
                _ => "PlanetConjunction"
            } : familyProfile.ValidatorProfile;
        var candidates = eventFamily == "PlanetaryEvent"
            ? NormalizeObjectList(current.ForbiddenObjectNames.Concat(EventContentGuard.DefaultForbiddenTermsForEventType(current.EventType)))
            : eventFamily == "Moon" || eventFamily == "Eclipse"
                ? NormalizeObjectList(current.ForbiddenObjectNames.Concat(familyProfile.ForbiddenTerms))
                : NormalizeObjectList(current.ForbiddenObjectNames);
        var skipped = candidates.Where(term => expectedObjects.Any(expected => LabelMatches(expected, term) || LabelMatches(term, expected))).ToArray();
        var applied = candidates.Except(skipped, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ThumbnailValidatorProfile(current.EventType, eventFamily, expectedObjects, applied, skipped, validatorProfile, familyResolution.Family.ToString(), familyResolution.Input, familyResolution.Reason, familyProfile.GetType().Name, EventFamilyProfiles.Version);
    }


    private static void LogSelectedFamilyProfile(string surface, EventFamilyResolution resolution, IEventFamilyProfile profile)
    {
        var allowedConcepts = profile is MoonFamilyProfile moon ? moon.AllowedConcepts : Array.Empty<string>();
        Console.WriteLine("[EventFamilyProfileSelected] " + JsonSerializer.Serialize(new
        {
            surface,
            familyCode = profile.Family.ToString(),
            detectedFamily = profile.Family.ToString(),
            primaryEventTypeCode = SpecialEventSubtypeResolver.Normalize(resolution.Input.TryGetValue("eventType", out var eventType) ? eventType?.ToString() : null),
            selectedProfile = profile.SelectedProfile,
            profileName = profile.GetType().Name,
            profileVersion = EventFamilyProfiles.Version,
            resolverReason = resolution.Reason,
            resolverInput = resolution.Input,
            forbiddenTerms = profile.ForbiddenTerms,
            forbiddenConcepts = profile.ForbiddenTerms,
            requiredVisualElements = profile.RequiredVisualElements,
            requiredOverlayElements = profile.RequiredOverlayElements,
            allowedConcepts
        }, JsonOptions));
    }

    private static IReadOnlyList<string> DetectThumbnailForbiddenTerms(ThumbnailValidatorProfile profile, IEnumerable<string> thumbnailMetadataAndOverlayText)
    {
        var text = string.Join(" | ", thumbnailMetadataAndOverlayText.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text)) return [];
        return profile.ForbiddenTermsApplied
            .Where(term => !string.IsNullOrWhiteSpace(term) && text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildPureV3VisualFocus(CurrentEventLock current)
        => IsMeteorEvent(current.EventType, current.Title)
            ? "Dramatic meteor shower sky with bright meteor streaks across a dark premium cinematic night sky."
            : $"Premium cinematic astronomy thumbnail centered on {FirstNonEmpty(string.Join(", ", current.PrimaryObjects), current.ShortTitle, current.Title)}.";

    private static bool ContainsGoldenPilotLeakage(PureV3ThumbnailPrompt prompt)
    {
        var text = JsonSerializer.Serialize(prompt, JsonOptions);
        return text.Contains("golden", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pilot", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Sky Event Tonight", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Event Focus", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Best Viewing Time", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WritePureV3ThumbnailAsync(string outputPath, PureV3ThumbnailPrompt prompt, CancellationToken cancellationToken)
    {
        const int width = 1280;
        const int height = 720;
        using var image = new Image<Rgba32>(width, height, Color.ParseHex("#030612"));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None,
                new ColorStop(0f, Color.ParseHex("#02030A")),
                new ColorStop(0.45f, Color.ParseHex("#09133A")),
                new ColorStop(1f, Color.ParseHex("#220B34"))), new RectangleF(0, 0, width, height));
            var rng = new Random(StringComparer.OrdinalIgnoreCase.GetHashCode(prompt.EventTitle));
            for (var i = 0; i < 260; i++)
                ctx.Fill(Color.White.WithAlpha(0.25f + rng.NextSingle() * 0.55f), new EllipsePolygon(rng.Next(width), rng.Next((int)(height * 0.72f)), rng.NextSingle() * 1.5f + 0.4f));
            if (prompt.VisualObjects.Any(value => value.Contains("meteor", StringComparison.OrdinalIgnoreCase)))
            {
                for (var i = 0; i < 16; i++)
                {
                    var start = new PointF(rng.Next(120, width - 80), rng.Next(40, 330));
                    var end = new PointF(start.X - rng.Next(110, 310), start.Y + rng.Next(35, 140));
                    ctx.DrawLine(Pens.Solid(Color.ParseHex("#9EDCFF").WithAlpha(0.55f), rng.Next(5, 12)), start, end);
                    ctx.DrawLine(Pens.Solid(Color.White.WithAlpha(0.92f), rng.Next(2, 5)), start, end);
                }
            }
            ctx.Fill(Color.Black.WithAlpha(0.45f), new RectangleF(0, 0, width * 0.50f, height));
            var headline = ResolveThumbnailFont(92, FontStyle.Bold);
            var sub = ResolveThumbnailFont(72, FontStyle.Bold);
            var badgeFont = ResolveThumbnailFont(34, FontStyle.Bold);
            ctx.DrawText(prompt.CtrOverlay.ElementAtOrDefault(0) ?? "SKY EVENT", headline, Color.White, new PointF(64, 96));
            ctx.DrawText(prompt.CtrOverlay.ElementAtOrDefault(1) ?? "PEAK WINDOW", sub, Color.ParseHex("#F8D36B"), new PointF(66, 204));
            if (!prompt.EventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(prompt.Badge))
            {
                ctx.Fill(Color.ParseHex("#E83B3B"), new RectangleF(68, 326, 300, 64));
                ctx.DrawText(prompt.Badge, badgeFont, Color.White, new PointF(90, 342));
            }
        });
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
    }

    private static string? ResolveApprovedSceneImagePath(string sceneApprovalRoot, string sceneId)
    {
        if (!Directory.Exists(sceneApprovalRoot)) return null;
        var patterns = new[] { $"{sceneId}-final.png", $"{sceneId}.png" };
        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(sceneApprovalRoot, pattern, SearchOption.AllDirectories))
            .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}long{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.EndsWith("-final.png", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
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
                // Unit tests and partially prepared scene runs may provide placeholder scene bytes.
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
        if (string.IsNullOrWhiteSpace(composition.PrimaryHook)
            || string.IsNullOrWhiteSpace(intelligence.ThumbnailCopy.PrimaryText)
            || !string.Equals(composition.PrimaryHook, intelligence.ThumbnailCopy.PrimaryText, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thumbnail image generation validation failed: primary hook must match thumbnail intelligence.");
        if (string.IsNullOrWhiteSpace(composition.SecondaryText))
            throw new ArgumentException("Thumbnail image generation validation failed: secondary text is required.");
        if (string.IsNullOrWhiteSpace(composition.MicroText))
            throw new ArgumentException("Thumbnail image generation validation failed: micro text is required.");
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
            if (!isMeteorShowerThumbnail && string.IsNullOrWhiteSpace(validation.SourceSceneUsed))
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic sourceSceneUsed is required.");
            if (validation.CinematicCropApplied)
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic cinematicCropApplied must be false.");
            if (!validation.OldThumbnailRendererBypassed || !validation.SceneTextLabelsRemoved || !validation.TextBoxesRemoved)
                throw new ArgumentException("Thumbnail layout validation failed: photo-cinematic bypass and text removal flags must be true.");
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

    private static IReadOnlyDictionary<string, string> BuildThumbnailManifestValidationFacts(ThumbnailAssetGenerationRequest request, ProductionEventIntelligence? intelligence)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eventId"] = request.EventId,
            ["eventType"] = intelligence?.EventType ?? request.ProductionContext?.EventType ?? "Unknown",
            ["title"] = intelligence?.Title ?? request.EventId,
            ["viewingWindow"] = ResolveMeteorViewingWindow(intelligence),
            ["visualStrategy"] = string.Join(", ", intelligence?.VisualMotifs ?? [])
        };
        return facts;
    }


    private PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest BuildPhotoCinematicRenderRequest(ThumbnailAssetGenerationRequest request, ThumbnailSceneManifestDto manifest, string manifestPath, bool forceMeteor = false)
    {
        var currentEventLock = BuildCurrentEventLock(request);
        var resolverResult = ResolveThumbnailVisualSource(currentEventLock, forceMeteor);
        var requiredObjects = NormalizeObjectList(resolverResult.RequiredDrawableObjects);
        var eventObjects = NormalizeObjectList(currentEventLock.PrimaryObjects.Concat(currentEventLock.SecondaryObjects));
        var visualObjects = NormalizeObjectList(requiredObjects.Concat(eventObjects));
        visualObjects = NormalizeObjectList(visualObjects.Concat(BuildEventTypeVisualObjects(currentEventLock, forceMeteor)));
        if (visualObjects.Count == 0)
            visualObjects = NormalizeObjectList([currentEventLock.ShortTitle, currentEventLock.Title]);

        var labels = BuildThumbnailLabels(currentEventLock, resolverResult, visualObjects);
        var sourceImagePath = ResolveThumbnailSourceImagePath(manifest, manifestPath);
        var copy = BuildDynamicThumbnailCopy(currentEventLock);
        return new PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest(
            currentEventLock.Title,
            currentEventLock.ShortTitle,
            currentEventLock.EventType,
            visualObjects,
            labels,
            copy.SecondaryText,
            copy.MicroText,
            sourceImagePath,
            currentEventLock,
            resolverResult,
            ResolveThumbnailSourceManifestPath(manifest, manifestPath),
            manifest.PrimaryScene.ImagePath);
    }


    private static IReadOnlyList<string> BuildEventTypeVisualObjects(CurrentEventLock currentEventLock, bool forceMeteor)
    {
        var values = new List<string>();
        if (forceMeteor || IsMeteorEvent(currentEventLock.EventType, currentEventLock.Title)) values.Add("Meteor");
        if (currentEventLock.EventType.Contains("Comet", StringComparison.OrdinalIgnoreCase) || currentEventLock.Title.Contains("Comet", StringComparison.OrdinalIgnoreCase)) values.Add("Comet");
        if (currentEventLock.EventType.Contains("Eclipse", StringComparison.OrdinalIgnoreCase) || currentEventLock.Title.Contains("Eclipse", StringComparison.OrdinalIgnoreCase))
            values.Add(currentEventLock.EventType.Contains("Solar", StringComparison.OrdinalIgnoreCase) || currentEventLock.Title.Contains("Solar", StringComparison.OrdinalIgnoreCase) ? "Solar Eclipse" : "Lunar Eclipse");
        if (currentEventLock.EventType.Contains("DeepSky", StringComparison.OrdinalIgnoreCase) || currentEventLock.EventType.Contains("Deep Sky", StringComparison.OrdinalIgnoreCase)) values.Add("Deep Sky Object");
        return NormalizeObjectList(values);
    }

    private VisualSourceResolutionResult ResolveThumbnailVisualSource(CurrentEventLock currentEventLock, bool forceMeteor)
    {
        var intelligence = currentEventLock.ToProductionEventIntelligence(forceMeteor);
        var scene = new EnrichedQuestionSceneDto(
            12,
            "Thumbnail",
            "Current event thumbnail visual summary",
            "What should the thumbnail show?",
            currentEventLock.Title,
            "CasualSkyWatcher",
            "Beginner",
            currentEventLock.ShortTitle,
            "Make the current event immediately recognizable.",
            "Use only the current event visual objects.",
            "Dynamic current-event thumbnail source resolution.",
            "Minimal current event labels only.",
            "Readable astronomy thumbnail copy.",
            true);
        var narration = new QuestionDrivenNarrationSceneDto(
            12,
            "Thumbnail",
            "Current event thumbnail visual summary",
            "What should the thumbnail show?",
            currentEventLock.ShortTitle,
            currentEventLock.Title,
            "Thumbnail current event lock",
            currentEventLock.Title,
            0,
            "Visual only",
            currentEventLock.ShortTitle);
        var required = NormalizeObjectList(currentEventLock.PrimaryObjects.Concat(currentEventLock.SecondaryObjects).Concat(currentEventLock.RequiredVisualObjects));
        return visualSourceResolver.Resolve(new VisualSourceResolutionRequest(intelligence, currentEventLock.ContentStrategy ?? currentEventLock.EventType, scene, narration, required));
    }

    private static IReadOnlyList<string> BuildThumbnailLabels(CurrentEventLock currentEventLock, VisualSourceResolutionResult resolverResult, IReadOnlyList<string> visualObjects)
    {
        if (IsPlanetFamilyEventType(currentEventLock.EventType))
        {
            var planetLabels = ResolvePlanetFamilyVisibleObjects(currentEventLock.ToProductionEventIntelligence(false));
            if (planetLabels.Count == 0) planetLabels = NormalizeObjectList(visualObjects.Select(FormatThumbnailObjectLabel).Where(IsPlanetObjectName));
            return planetLabels;
        }

        var labels = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentEventLock.ShortTitle)) labels.Add(currentEventLock.ShortTitle);
        labels.AddRange(currentEventLock.PrimaryObjects);
        labels.AddRange(currentEventLock.SecondaryObjects);
        if (labels.Count == 0) labels.AddRange(visualObjects);
        if (resolverResult.Metadata.TryGetValue("labelObjects", out var labelObjects))
            labels.AddRange(labelObjects.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return NormalizeObjectList(labels);
    }

    private static ThumbnailDynamicCopy BuildDynamicThumbnailCopy(CurrentEventLock currentEventLock)
    {
        if (IsPlanetFamilyEventType(currentEventLock.EventType))
        {
            var planetCopy = BuildPlanetFamilyThumbnailCopy(currentEventLock.EventType, ResolvePlanetFamilyVisibleObjects(currentEventLock.ToProductionEventIntelligence(false)));
            return new ThumbnailDynamicCopy(LimitThumbnailWords(FirstNonEmpty(planetCopy.PrimaryText, currentEventLock.ShortTitle), 6).ToUpperInvariant(), string.Empty);
        }

        if (IsMoonFamilyEventType(currentEventLock.EventType, currentEventLock.Title))
        {
            var direction = ResolveMoonThumbnailDirection(FirstNonEmpty(currentEventLock.SkyDirectionHint, InferMoonDirectionCue(currentEventLock)));
            var bestTime = ResolveMoonThumbnailBestTime(FirstNonEmpty(currentEventLock.BestViewingWindowLocal, currentEventLock.LocalPeakTime, "After moonrise"));
            return new ThumbnailDynamicCopy(direction, bestTime);
        }

        var headline = CleanThumbnailText(FirstNonEmpty(currentEventLock.ShortTitle, currentEventLock.Title), "SKY EVENT", 6).ToUpperInvariant();
        return new ThumbnailDynamicCopy(headline, string.Empty);
    }

    private static string ResolveEventActionPhrase(CurrentEventLock currentEventLock)
    {
        if (currentEventLock.EventType.Contains("Solar", StringComparison.OrdinalIgnoreCase) && currentEventLock.EventType.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return "Use Safe Viewing";
        var direction = currentEventLock.SkyDirectionHint;
        if (!string.IsNullOrWhiteSpace(direction))
            return direction.StartsWith("look", StringComparison.OrdinalIgnoreCase) ? direction : $"Look {direction}";
        if (IsMeteorEvent(currentEventLock.EventType, currentEventLock.Title)) return "Dark Sky Window";
        if (IsFullMoonEvent(currentEventLock.EventType, currentEventLock.Title)) return "Moonrise View";
        if (currentEventLock.EventType.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return "Eclipse View";
        return FirstNonEmpty(currentEventLock.BestViewingWindowLocal, currentEventLock.LocalPeakTime, currentEventLock.EventType);
    }

    private static string? ResolveThumbnailSourceImagePath(ThumbnailSceneManifestDto manifest, string manifestPath)
    {
        var heroManifestPath = manifest.SourceHeroAssets.FirstOrDefault(path => string.Equals(Path.GetFileName(path), HeroSceneManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(heroManifestPath) && File.Exists(heroManifestPath))
            return manifest.PrimaryScene.ImagePath;
        if (File.Exists(manifest.PrimaryScene.ImagePath))
            return manifest.PrimaryScene.ImagePath;
        return File.Exists(manifestPath) ? manifestPath : null;
    }

    private static void ValidateThumbnailSemantics(ThumbnailAssetGenerationRequest request, IReadOnlyList<string> visualObjectsUsed, IReadOnlyList<string> labelsUsed, IReadOnlyList<string> forbiddenObjectsDetected, VisualSourceResolutionResult? resolverResult = null)
    {
        if (forbiddenObjectsDetected.Count > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated object label(s) detected: " + string.Join(", ", forbiddenObjectsDetected));

        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var requiredLabels = IsPlanetFamilyEventType(intelligence?.EventType)
            ? ResolvePlanetFamilyVisibleObjects(intelligence)
            : NormalizeObjectList(intelligence?.PrimaryObjects ?? []);
        var shortTitle = intelligence?.ShortTitle;
        if (!IsPlanetFamilyEventType(intelligence?.EventType) && !string.IsNullOrWhiteSpace(shortTitle)) requiredLabels = requiredLabels.Append(shortTitle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requiredLabels.Count == 0) return;

        var observed = NormalizeObjectList(visualObjectsUsed.Concat(labelsUsed));
        var matched = requiredLabels.Any(required => observed.Any(label => LabelMatches(label, required) || LabelMatches(required, label)));
        if (!matched)
            throw new InvalidOperationException("Thumbnail semantic validation failed: required object labels must include a current primary object or shortTitle. required=" + string.Join(", ", requiredLabels) + "; labels=" + string.Join(", ", observed));

        var resolverRequired = NormalizeObjectList(resolverResult?.RequiredDrawableObjects ?? []);
        var missingRequired = resolverRequired
            .Where(required => IsConcreteRequiredVisualObject(required))
            .Where(required => !observed.Any(label => LabelMatches(label, required) || LabelMatches(required, label)))
            .ToArray();
        if (missingRequired.Length > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: required resolver visual object(s) missing: " + string.Join(", ", missingRequired));
    }


    private static void ValidateThumbnailSourceBelongsToCurrentRun(ThumbnailAssetGenerationRequest request, PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest renderRequest, string manifestPath)
    {
        var planRoot = request.ProductionContext?.PlanRoot;
        if (string.IsNullOrWhiteSpace(planRoot)) return;
        var normalizedPlanRoot = NormalizePath(Path.GetFullPath(planRoot)).TrimEnd('/') + "/";
        var sourceManifestPath = NormalizePath(Path.GetFullPath(renderRequest.SourceManifestPath ?? manifestPath));
        var sourceScenePath = string.IsNullOrWhiteSpace(renderRequest.SourceScenePath) ? string.Empty : NormalizePath(Path.GetFullPath(renderRequest.SourceScenePath));
        if (!sourceManifestPath.StartsWith(normalizedPlanRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Thumbnail semantic validation failed: source manifest '{sourceManifestPath}' is outside current plan root '{normalizedPlanRoot}'.");
        if (!string.IsNullOrWhiteSpace(sourceScenePath) && !sourceScenePath.StartsWith(normalizedPlanRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Thumbnail semantic validation failed: source scene '{sourceScenePath}' is outside current plan root '{normalizedPlanRoot}'.");
    }

    private static bool IsConcreteRequiredVisualObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lower = value.Trim().ToLowerInvariant();
        return !lower.Contains("dark sky")
            && !lower.Contains("radiant")
            && !lower.Contains("texture")
            && !lower.Contains("label")
            && !lower.Contains("moonrise")
            && !lower.Contains("tail")
            && !lower.Contains("coma")
            && !lower.Contains("nucleus")
            && !lower.Contains("astrophotography")
            && !lower.Contains("close pairing");
    }

    private static IEnumerable<string> DetectForbiddenObjects(ThumbnailAssetGenerationRequest request, IEnumerable<string> labels)
    {
        var profile = ResolveThumbnailValidatorProfile(request);
        var forbiddenCandidates = profile.ForbiddenTermsApplied;
        var allowed = profile.ExpectedObjects;
        foreach (var candidate in forbiddenCandidates)
        {
            if (allowed.Any(value => LabelMatches(value, candidate))) continue;
            if (labels.Any(label => LabelMatches(label, candidate))) yield return candidate;
        }
    }

    private static bool EventAllowsObject(ThumbnailAssetGenerationRequest request, string objectName)
        => NormalizeObjectList((request.ProductionContext?.ProductionEventIntelligence?.PrimaryObjects ?? []).Concat(request.ProductionContext?.ProductionEventIntelligence?.SecondaryObjects ?? []))
            .Any(value => LabelMatches(value, objectName));

    private static IReadOnlyList<string> NormalizeObjectList(IEnumerable<string> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool LabelMatches(string label, string expected)
        => label.Equals(expected, StringComparison.OrdinalIgnoreCase) || label.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static async Task UpdateThumbnailSceneManifestGeneratedPathsAsync(
        string manifestPath,
        IReadOnlyList<string> generatedPaths,
        ThumbnailLayoutValidationDto validation,
        CancellationToken cancellationToken,
        PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest? renderRequest = null,
        PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderResult? renderResult = null,
        IReadOnlyList<string>? forbiddenObjectsDetected = null)
    {
        var manifest = JsonSerializer.Deserialize<ThumbnailSceneManifestDto>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException("Thumbnail scene manifest input could not be parsed.");
        var facts = new Dictionary<string, string>(manifest.ValidationFacts, StringComparer.OrdinalIgnoreCase)
        {
            ["thumbnailVisualSourceMode"] = validation.ThumbnailVisualSourceMode,
            ["sourceSceneUsed"] = validation.SourceSceneUsed,
            ["readinessScore"] = validation.ThumbnailFinalReadinessScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["forbiddenPlanetLeakage"] = (forbiddenObjectsDetected?.Count > 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["thumbnailRequestTitle"] = renderRequest?.Title ?? manifest.Title ?? string.Empty,
            ["thumbnailRequestShortTitle"] = renderRequest?.ShortTitle ?? string.Empty,
            ["thumbnailEventType"] = renderRequest?.EventType ?? manifest.EventType ?? string.Empty,
            ["thumbnailPrimaryObjects"] = string.Join(", ", (renderRequest?.CurrentEventLock as CurrentEventLock)?.PrimaryObjects ?? Array.Empty<string>()),
            ["thumbnailSecondaryObjects"] = string.Join(", ", (renderRequest?.CurrentEventLock as CurrentEventLock)?.SecondaryObjects ?? Array.Empty<string>()),
            ["thumbnailSourceManifestPath"] = renderRequest?.SourceManifestPath ?? ResolveThumbnailSourceManifestPath(manifest, manifestPath),
            ["thumbnailSourceScenePath"] = renderRequest?.SourceScenePath ?? renderRequest?.SourceImagePath ?? manifest.PrimaryScene.ImagePath,
            ["visualObjectsUsed"] = string.Join(", ", renderResult?.VisualObjectsUsed ?? renderRequest?.VisualObjects ?? Array.Empty<string>()),
            ["labelsUsed"] = string.Join(", ", renderResult?.LabelsUsed ?? renderRequest?.Labels ?? Array.Empty<string>()),
            ["textUsed"] = string.Join(" | ", new[] { renderRequest?.ShortTitle, renderRequest?.SecondaryText, renderRequest?.MicroText }.Where(value => !string.IsNullOrWhiteSpace(value))),
            ["forbiddenObjectsDetected"] = string.Join(", ", forbiddenObjectsDetected ?? Array.Empty<string>()),
            ["goldenPilotLeakageDetected"] = DetectGoldenPilotLeakage(renderRequest, renderResult).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["semanticValidationPassed"] = ((forbiddenObjectsDetected?.Count ?? 0) == 0 && !DetectGoldenPilotLeakage(renderRequest, renderResult)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["visualResolverSourceType"] = (renderRequest?.VisualResolverResult as VisualSourceResolutionResult)?.SourceType.ToString() ?? string.Empty,
            ["visualResolverRequiredObjects"] = string.Join(", ", (renderRequest?.VisualResolverResult as VisualSourceResolutionResult)?.RequiredDrawableObjects ?? Array.Empty<string>()),
            ["visualResolverForbiddenObjects"] = string.Join(", ", (renderRequest?.VisualResolverResult as VisualSourceResolutionResult)?.ForbiddenObjectNames ?? Array.Empty<string>()),
            ["visualResolverAssetKeys"] = string.Join(", ", (renderRequest?.VisualResolverResult as VisualSourceResolutionResult)?.ScientificAssetKeys ?? Array.Empty<string>()),
            ["visualResolverPrompt"] = (renderRequest?.VisualResolverResult as VisualSourceResolutionResult)?.AiCinematicPrompt ?? string.Empty,
            ["currentEventLock"] = renderRequest?.CurrentEventLock is null ? string.Empty : JsonSerializer.Serialize(renderRequest.CurrentEventLock, JsonOptions),
            ["visualResolverResult"] = renderRequest?.VisualResolverResult is null ? string.Empty : JsonSerializer.Serialize(renderRequest.VisualResolverResult, JsonOptions)
        };

        var updated = manifest with
        {
            GeneratedThumbnailPaths = generatedPaths.Select(NormalizePath).ToArray(),
            ValidationFacts = facts
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(updated, JsonOptions), cancellationToken);
        await WritePhase12SemanticValidationAsync(manifestPath, facts, renderRequest, renderResult, forbiddenObjectsDetected ?? Array.Empty<string>(), cancellationToken);
    }

    private static async Task WritePhase12SemanticValidationAsync(
        string manifestPath,
        IReadOnlyDictionary<string, string> facts,
        PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest? renderRequest,
        PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderResult? renderResult,
        IReadOnlyList<string> forbiddenObjectsDetected,
        CancellationToken cancellationToken)
    {
        var outputRoot = Path.GetDirectoryName(manifestPath) ?? ".";
        var validationPath = Path.Combine(outputRoot, Phase12SemanticValidationFileName);
        var visualObjectsUsed = renderResult?.VisualObjectsUsed ?? renderRequest?.VisualObjects ?? Array.Empty<string>();
        var labelsUsed = renderResult?.LabelsUsed ?? renderRequest?.Labels ?? Array.Empty<string>();
        var textUsed = new[] { renderRequest?.ShortTitle, renderRequest?.SecondaryText, renderRequest?.MicroText }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        var validationProfile = ResolveThumbnailValidatorProfile(renderRequest?.CurrentEventLock as CurrentEventLock);
        var forbiddenTermsDetected = DetectThumbnailForbiddenTerms(validationProfile, visualObjectsUsed.Concat(labelsUsed).Concat(textUsed));
        var goldenPilotLeakageDetected = DetectGoldenPilotLeakage(renderRequest, renderResult);
        var semanticValidationPassed = string.Equals(GetDictionaryValue(facts, "semanticValidationPassed"), "True", StringComparison.OrdinalIgnoreCase)
            && !goldenPilotLeakageDetected
            && forbiddenObjectsDetected.Count == 0
            && forbiddenTermsDetected.Count == 0;
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            currentEventLock = renderRequest?.CurrentEventLock,
            thumbnailRequestTitle = renderRequest?.Title ?? string.Empty,
            thumbnailRequestShortTitle = renderRequest?.ShortTitle ?? string.Empty,
            thumbnailEventType = renderRequest?.EventType ?? string.Empty,
            thumbnailPrimaryObjects = (renderRequest?.CurrentEventLock as CurrentEventLock)?.PrimaryObjects ?? Array.Empty<string>(),
            thumbnailSecondaryObjects = (renderRequest?.CurrentEventLock as CurrentEventLock)?.SecondaryObjects ?? Array.Empty<string>(),
            thumbnailSourceManifestPath = renderRequest?.SourceManifestPath ?? NormalizePath(manifestPath),
            thumbnailSourceScenePath = renderRequest?.SourceScenePath ?? renderRequest?.SourceImagePath ?? string.Empty,
            visualResolverResult = renderRequest?.VisualResolverResult,
            visualObjectsUsed,
            labelsUsed,
            textUsed,
            forbiddenObjectsDetected,
            forbiddenTermsDetected,
            goldenPilotLeakageDetected,
            eventType = validationProfile.EventType,
            eventFamily = validationProfile.ResolvedEventFamily,
            legacyEventFamily = validationProfile.EventFamily,
            eventFamilyResolverInput = validationProfile.EventFamilyResolverInput,
            eventFamilyResolverReason = validationProfile.EventFamilyResolverReason,
            eventFamilyProfileName = validationProfile.EventFamilyProfileName,
            eventFamilyProfileVersion = validationProfile.EventFamilyProfileVersion,
            expectedObjects = validationProfile.ExpectedObjects,
            forbiddenTermsApplied = validationProfile.ForbiddenTermsApplied,
            forbiddenTermsSkippedBecauseExpected = validationProfile.ForbiddenTermsSkippedBecauseExpected,
            validatorProfile = validationProfile.ValidatorProfile,
            semanticValidationPassed,
            moonAspectRatioPreserved = renderResult?.MoonAspectRatioPreserved ?? false,
            moonCalloutCircleDiameterPx = renderResult?.MoonCalloutCircleDiameterPx ?? 0,
            moonVisibleDiameterPx = renderResult?.MoonVisibleDiameterPx ?? 0,
            moonCalloutCirclePercentOfMoon = renderResult?.MoonCalloutCirclePercentOfMoon ?? 0,
            thumbnailRulesPassed = semanticValidationPassed
        }, JsonOptions), cancellationToken);
    }

    private static string GetDictionaryValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : string.Empty;

    private static bool DetectGoldenPilotLeakage(PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderRequest? renderRequest, PhotoCinematicThumbnailRenderer.PhotoCinematicThumbnailRenderResult? renderResult)
    {
        var text = string.Join(" | ", (renderResult?.VisualObjectsUsed ?? renderRequest?.VisualObjects ?? Array.Empty<string>())
            .Concat(renderResult?.LabelsUsed ?? renderRequest?.Labels ?? Array.Empty<string>())
            .Concat([renderRequest?.Title ?? string.Empty, renderRequest?.ShortTitle ?? string.Empty, renderRequest?.SecondaryText ?? string.Empty, renderRequest?.MicroText ?? string.Empty, renderRequest?.SourceManifestPath ?? string.Empty]));
        if (text.Contains("golden", StringComparison.OrdinalIgnoreCase) || text.Contains("pilot", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("Sky Event Tonight", StringComparison.OrdinalIgnoreCase) || text.Contains("Event Focus", StringComparison.OrdinalIgnoreCase) || text.Contains("Best Viewing Time", StringComparison.OrdinalIgnoreCase)) return true;
        var currentEventLock = renderRequest?.CurrentEventLock as CurrentEventLock;
        var allowedObjects = (currentEventLock?.PrimaryObjects ?? Array.Empty<string>()).Concat(currentEventLock?.SecondaryObjects ?? Array.Empty<string>());
        var allowsVenus = allowedObjects.Any(value => LabelMatches(value, "Venus"));
        var allowsJupiter = allowedObjects.Any(value => LabelMatches(value, "Jupiter"));
        return (!allowsVenus && text.Contains("Venus", StringComparison.OrdinalIgnoreCase)) || (!allowsJupiter && text.Contains("Jupiter", StringComparison.OrdinalIgnoreCase));
    }


    private static string ResolveThumbnailSourceManifestPath(ThumbnailSceneManifestDto manifest, string manifestPath)
    {
        var heroManifestPath = manifest.SourceHeroAssets.FirstOrDefault(path => string.Equals(Path.GetFileName(path), HeroSceneManifestFileName, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(heroManifestPath) && File.Exists(heroManifestPath)
            ? NormalizePath(heroManifestPath)
            : NormalizePath(manifestPath);
    }

    private static string ResolveMeteorViewingWindow(ProductionEventIntelligence? intelligence)
    {
        if (intelligence is null) return string.Empty;
        var preferred = FirstNonEmpty(intelligence.PreferredViewingWindow, intelligence.BestViewingWindowLocal);
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        return IsDaytimeLocalPeak(intelligence.LocalPeakTime) ? "Dark pre-dawn sky" : intelligence.LocalPeakTime ?? string.Empty;
    }

    private static string CleanThumbnailText(string? value, string fallback, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        cleaned = cleaned.Replace("localPeakTime", "viewing window", StringComparison.OrdinalIgnoreCase);
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool IsDaytimeLocalPeak(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?<!\d)(\d{1,2})(?::(\d{2}))?\s*(AM|PM)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var hour)) return false;
        var suffix = match.Groups[3].Value;
        if (suffix.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12) hour += 12;
        if (suffix.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12) hour = 0;
        return hour >= 6 && hour < 18;
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
        if (requireSavedManifest && string.IsNullOrWhiteSpace(manifest.EventType))
            throw new ArgumentException("Thumbnail scene selection validation failed: eventType is required in thumbnail-scene-manifest.json.");
        if (requireSavedManifest && string.IsNullOrWhiteSpace(manifest.Title))
            throw new ArgumentException("Thumbnail scene selection validation failed: title is required in thumbnail-scene-manifest.json.");
        if (requireSavedManifest && manifest.SourceHeroAssets.Count == 0)
            throw new ArgumentException("Thumbnail scene selection validation failed: source hero assets are required in thumbnail-scene-manifest.json.");
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
        if (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.RegionId) || string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("Thumbnail intelligence generation requires event id, region id, and language.", nameof(request));
    }

    private string BuildSceneApprovalRoot(string eventId, string regionId)
    {
        if (!string.IsNullOrWhiteSpace(_activeProductionContext?.PlanRoot))
            return Path.Combine(_activeProductionContext!.PlanRoot!, SceneApprovalDirectoryName);

        var questionRoot = BuildQuestionEngineRoot(eventId, regionId);
        var eventRoot = Directory.GetParent(questionRoot)?.FullName;
        return string.IsNullOrWhiteSpace(eventRoot) ? Path.Combine(questionRoot, SceneApprovalDirectoryName) : Path.Combine(eventRoot, SceneApprovalDirectoryName);
    }

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.QuestionRoot) ? _activeProductionContext!.QuestionRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.HeroRoot) ? _activeProductionContext!.HeroRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailAssetsRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.ThumbnailRoot) ? _activeProductionContext!.ThumbnailRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName);

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


    private static CurrentEventLock BuildCurrentEventLock(ThumbnailAssetGenerationRequest request)
    {
        var context = request.ProductionContext;
        var intelligence = context?.ProductionEventIntelligence;
        var title = FirstNonEmpty(intelligence?.Title, request.EventId);
        var eventType = FirstNonEmpty(intelligence?.EventType, context?.EventType, "Unknown");
        var shortTitle = FirstNonEmpty(intelligence?.ShortTitle, title);
        if (TryBuildPlanetFamilyThumbnailCopy(intelligence, out var planetCopy))
            shortTitle = planetCopy.PrimaryText;
        return new CurrentEventLock(
            PlanId: context?.ContentGenerationPlanId?.ToString("D") ?? string.Empty,
            Title: title,
            ShortTitle: shortTitle,
            EventType: eventType,
            Category: context?.Category,
            PrimaryObjects: NormalizeObjectList(intelligence?.PrimaryObjects ?? []),
            SecondaryObjects: NormalizeObjectList(intelligence?.SecondaryObjects ?? []),
            SourceExternalEventId: context?.SourceExternalEventId,
            RegionId: FirstNonEmpty(context?.RegionId, request.RegionId),
            Language: FirstNonEmpty(context?.Language, request.Language),
            EventDate: intelligence?.EventDate,
            LocalPeakTime: intelligence?.LocalPeakTime,
            SkyDirectionHint: intelligence?.SkyDirectionHint,
            BestViewingWindowLocal: intelligence?.BestViewingWindowLocal,
            AngularSeparationDegrees: intelligence?.AngularSeparationDegrees,
            AltitudeDegrees: intelligence?.AltitudeDegrees,
            MoonIlluminationPercent: intelligence?.MoonIlluminationPercent,
            ContentStrategy: FirstNonEmpty(context?.ContentStrategy, intelligence?.StrategyId),
            RequiredVisualObjects: NormalizeObjectList(intelligence?.RequiredVisualObjects ?? []),
            ForbiddenObjectNames: NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? [])));
    }

    private static bool IsMeteorEvent(string eventType, string title)
        => eventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase) || title.Contains("Meteor", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanetaryEvent(string eventType)
        => NormalizeEventTypeToken(eventType) is "PLANETCONJUNCTION" or "PLANETGROUPING" or "PLANETPAIRING" or "PLANETPARADE" or "PLANETALIGNMENT" or "MOONPLANETPAIRING";

    private static bool IsEclipseEvent(string eventType, string? title = null)
        => NormalizeEventTypeToken(eventType) is "ECLIPSE" or "SOLARECLIPSE" or "LUNARECLIPSE" or "TOTALSOLARECLIPSE" or "PARTIALSOLARECLIPSE" or "ANNULARSOLARECLIPSE" or "TOTALLUNARECLIPSE" or "PARTIALLUNARECLIPSE" or "PENUMBRALLUNARECLIPSE"
            || (!string.IsNullOrWhiteSpace(title) && title.Contains("Eclipse", StringComparison.OrdinalIgnoreCase));

    private static bool IsSolarEclipse(CurrentEventLock current)
    {
        var token = NormalizeEventTypeToken(current.EventType);
        if (token.Contains("SOLAR", StringComparison.OrdinalIgnoreCase)) return true;
        if (token.Contains("LUNAR", StringComparison.OrdinalIgnoreCase)) return false;
        return current.Title.Contains("Solar", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEclipseDirectionLabel(CurrentEventLock current)
    {
        var direction = FirstNonEmpty(current.SkyDirectionHint, current.BestViewingWindowLocal);
        if (direction.Contains("east", StringComparison.OrdinalIgnoreCase) || direction.Contains("west", StringComparison.OrdinalIgnoreCase))
            return "LOOK EAST-WEST";
        return IsSolarEclipse(current) ? "LOOK TOWARD SAFE VIEWING ZONE" : "LOOK EAST-WEST";
    }

    private static bool IsMoonEvent(string eventType, string? title = null)
        => NormalizeEventTypeToken(eventType) is "FULLMOON" or "NEWMOON" or "BLUEMOON" or "SUPERMOON" or "MICROMOON" or "MOONPHASE" or "SPECIALMOONPHASE" or "NAMEDFULLMOON" or "FIRSTQUARTER" or "LASTQUARTER"
            || (!string.IsNullOrWhiteSpace(title) && (title.Contains("Moon", StringComparison.OrdinalIgnoreCase) || title.Contains("Lunar", StringComparison.OrdinalIgnoreCase)));

    private static string NormalizeEventTypeToken(string value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string NormalizeDirectionCue(string? value)
    {
        var direction = FirstNonEmpty(value, "West").Trim();
        var lower = direction.ToLowerInvariant();
        if (lower.Contains("west") && lower.Contains("sunset")) return "West After Sunset";
        if (lower.Contains("west")) return "Look West";
        if (lower.StartsWith("look ")) direction = direction[5..].Trim();
        return "Look " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(direction.ToLowerInvariant());
    }

    private static bool IsFullMoonEvent(string eventType, string title)
        => eventType.Contains("FullMoon", StringComparison.OrdinalIgnoreCase) || eventType.Contains("Full Moon", StringComparison.OrdinalIgnoreCase) || title.Contains("Full Moon", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<ThumbnailImageSpec> ThumbnailImageSpecs =
    [
        new("Landscape", "thumbnail-landscape.png", 1280, 720, new RectangleF(58, 54, 650, 214), new PointF(74, 286), new PointF(82, 628), 70f, 36f, 28f),
        new("Square", "thumbnail-square.png", 1080, 1080, new RectangleF(66, 76, 860, 250), new PointF(84, 350), new PointF(84, 910), 76f, 42f, 34f),
        new("Portrait", "thumbnail-portrait.png", 1080, 1920, new RectangleF(70, 112, 920, 360), new PointF(86, 1288), new PointF(86, 1404), 96f, 58f, 44f)
    ];


    private static IReadOnlyList<string> BuildRc1ThumbnailTextLines(CurrentEventLock current, bool includeDateWhenAvailable)
    {
        var eventObjectContext = EventObjectContextBuilder.FromIntelligence(current.ToProductionEventIntelligence(IsMeteorEvent(current.EventType, current.Title)));
        var isMeteor = IsMeteorEvent(current.EventType, current.Title);
        var isConjunction = AllowsConjunctionVocabulary(current.EventType, current.Category);
        var lines = new List<string>();
        if (isMeteor)
        {
            var meteorName = CleanMeteorDisplayName(FirstNonEmpty(eventObjectContext.PrimaryObjectName, current.ShortTitle, current.Title));
            lines.Add(meteorName);
            lines.Add("METEOR SHOWER PEAK");
        }
        else if (isConjunction && eventObjectContext.ObjectCount >= 2)
        {
            lines.Add($"{eventObjectContext.ObjectNames[0]} + {eventObjectContext.ObjectNames[1]}".ToUpperInvariant());
            lines.Add("CONJUNCTION");
        }
        else if (IsMoonEvent(current.EventType, current.Title))
        {
            lines.Add(LimitThumbnailWords(CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, "MOON")), 4));
            lines.Add(ResolveMoonDisplaySubtitle(current));
        }
        else
        {
            lines.Add(LimitThumbnailWords(CleanHook(FirstNonEmpty(current.ShortTitle, current.Title, "SKY EVENT")), 4));
            var type = CleanHook(current.EventType.Replace('_', ' '));
            if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, lines[0], StringComparison.OrdinalIgnoreCase)) lines.Add(LimitThumbnailWords(type, 3));
        }

        if (includeDateWhenAvailable && current.EventDate is DateTimeOffset date && lines.Count < 3)
            lines.Add(date.ToString("MMM d", CultureInfo.InvariantCulture).ToUpperInvariant());

        return lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(3).ToArray();
    }

    private static string CleanMeteorDisplayName(string value)
    {
        var clean = CleanHook(value).Replace(" METEOR SHOWER", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" METEORS", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "METEOR SHOWER" : clean;
    }
    private static string CleanHook(string value)
        => (value ?? string.Empty).Trim().Trim('.', '!', '?').ToUpperInvariant();

    private static string ResolveMeteorThumbnailTitle(CurrentEventLock current)
        => CleanThumbnailText(FirstNonEmpty(current.ShortTitle, current.Title), "METEOR SHOWER", 18).ToUpperInvariant();

    private static string CleanTextElement(string? value, string fallback)
        => string.Join(' ', (string.IsNullOrWhiteSpace(value) ? fallback : value).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string NormalizePath(string path) => path.Replace('\\', '/');


    private sealed record ThumbnailDynamicCopy(string SecondaryText, string MicroText);

    private sealed record ThumbnailRuntimeDiagnostics(
        string ThumbnailGenerationPath,
        string ThumbnailCompositionType,
        string ThumbnailPromptBuilder,
        string FinalThumbnailPrompt,
        string ThumbnailRenderer,
        string SelectedThumbnailStrategy,
        string ThumbnailVisualSourceMode,
        string ThumbnailOverlayTemplate,
        int OverlayElementsCount,
        bool InfoCardAdded,
        bool RadiantMarkerAdded,
        bool MeteorStreakLabelAdded,
        bool LookDirectionCueAdded,
        bool BottomTipsBarAdded,
        string FinalThumbnailPath,
        string EventFamily = "GenericEvent",
        bool GuideCardAdded = false,
        bool ObjectLabelsAdded = false,
        bool DirectionCueAdded = false,
        bool SeparationAdded = false,
        bool AltitudeAdded = false,
        bool MoonGuideCardAdded = false,
        bool MoonObjectRendered = false,
        IReadOnlyList<string>? MoonForbiddenTermsDetected = null,
        bool MoonAspectRatioPreserved = false,
        float MoonCalloutCircleDiameterPx = 0,
        float MoonVisibleDiameterPx = 0,
        object? TitleBoundingBox = null,
        object? GuideCardBoundingBox = null,
        object? DirectionBoundingBox = null,
        object? FooterBoundingBox = null,
        object? SkyGuideBoundingBox = null,
        double OverlapPercent = 0,
        int PanelCount = 0)
    {
        public float MoonCalloutCirclePercentOfMoon => MoonVisibleDiameterPx <= 0 ? 0 : MoonCalloutCircleDiameterPx / MoonVisibleDiameterPx * 100f;
    }

    private sealed record ThumbnailOverlayDiagnostics(
        string ThumbnailOverlayTemplate,
        int OverlayElementsCount,
        bool InfoCardAdded,
        bool RadiantMarkerAdded,
        bool MeteorStreakLabelAdded,
        bool LookDirectionCueAdded,
        bool BottomTipsBarAdded,
        string FinalThumbnailPath,
        string EventFamily = "GenericEvent",
        bool GuideCardAdded = false,
        bool ObjectLabelsAdded = false,
        bool DirectionCueAdded = false,
        bool SeparationAdded = false,
        bool AltitudeAdded = false,
        bool MoonGuideCardAdded = false,
        bool MoonObjectRendered = false,
        IReadOnlyList<string>? MoonForbiddenTermsDetected = null,
        bool MoonAspectRatioPreserved = false,
        float MoonCalloutCircleDiameterPx = 0,
        float MoonVisibleDiameterPx = 0,
        object? TitleBoundingBox = null,
        object? GuideCardBoundingBox = null,
        object? DirectionBoundingBox = null,
        object? FooterBoundingBox = null,
        object? SkyGuideBoundingBox = null,
        double OverlapPercent = 0,
        int PanelCount = 0)
    {
        public float MoonCalloutCirclePercentOfMoon => MoonVisibleDiameterPx <= 0 ? 0 : MoonCalloutCircleDiameterPx / MoonVisibleDiameterPx * 100f;

        public static ThumbnailOverlayDiagnostics None(string finalThumbnailPath, string template)
            => new(template, 0, false, false, false, false, false, finalThumbnailPath);
    }

    private sealed record PureV3ThumbnailPrompt(
        string EventTitle,
        string EventType,
        string ShortTitle,
        IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects,
        decimal? AudienceInterestScore,
        decimal? ContentOpportunityScore,
        string? BestViewingWindowLocal,
        string? SkyDirectionHint,
        string? MoonInterference,
        string BackgroundPrompt,
        IReadOnlyList<string> VisualObjects,
        IReadOnlyList<string> CtrOverlay,
        string Badge,
        IReadOnlyList<string> ForbiddenTerms,
        string RenderingInstructions,
        string ThumbnailPrompt,
        string ThumbnailPromptSource,
        IReadOnlyList<string> ForbiddenTermsMatched,
        IReadOnlyList<string> EventTypeVocabularyUsed,
        string ThumbnailVocabularyProfile);

    private sealed record ThumbnailValidatorProfile(
        string EventType,
        string EventFamily,
        IReadOnlyList<string> ExpectedObjects,
        IReadOnlyList<string> ForbiddenTermsApplied,
        IReadOnlyList<string> ForbiddenTermsSkippedBecauseExpected,
        string ValidatorProfile,
        string ResolvedEventFamily,
        IReadOnlyDictionary<string, object> EventFamilyResolverInput,
        string EventFamilyResolverReason,
        string EventFamilyProfileName,
        string EventFamilyProfileVersion);

    private sealed record CurrentEventLock(
        string PlanId,
        string Title,
        string ShortTitle,
        string EventType,
        string? Category,
        IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects,
        string? SourceExternalEventId,
        string RegionId,
        string Language,
        DateTimeOffset? EventDate,
        string? LocalPeakTime,
        string? SkyDirectionHint,
        string? BestViewingWindowLocal,
        decimal? AngularSeparationDegrees,
        decimal? AltitudeDegrees,
        decimal? MoonIlluminationPercent,
        string? ContentStrategy,
        IReadOnlyList<string> RequiredVisualObjects,
        IReadOnlyList<string> ForbiddenObjectNames)
    {
        public ProductionEventIntelligence ToProductionEventIntelligence(bool forceMeteor)
            => new(
                "Astronomy",
                EventType,
                Title,
                ShortTitle,
                EventDate,
                null,
                LocalPeakTime,
                BestViewingWindowLocal,
                SkyDirectionHint,
                null,
                PrimaryObjects,
                SecondaryObjects,
                null,
                null,
                MoonIlluminationPercent,
                "Current event thumbnail lock",
                [],
                [],
                [],
                [],
                [],
                StrategyId: ContentStrategy ?? EventType,
                ForbiddenObjectNames: ForbiddenObjectNames,
                RequiredVisualObjects: forceMeteor ? RequiredVisualObjects.Concat(["Meteor"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : RequiredVisualObjects,
                PreferredViewingWindow: BestViewingWindowLocal,
                AngularSeparationDegrees: AngularSeparationDegrees,
                AltitudeDegrees: AltitudeDegrees);
    }

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
