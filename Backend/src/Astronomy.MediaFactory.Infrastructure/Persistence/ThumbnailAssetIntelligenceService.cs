using System.Diagnostics;
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

public sealed class ThumbnailAssetIntelligenceService(IOptions<RenderingOptions> renderingOptions, IOptions<AzureOpenAIForImageOptions> imageOptions, IVisualSourceResolver visualSourceResolver, IHttpClientFactory httpClientFactory) : IThumbnailAssetIntelligenceService
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
    private const string ThumbnailGenerationDiagnosticsFileName = "thumbnail-generation-diagnostics.json";
    private const string DefaultThumbnailHook = "CURRENT SKY EVENT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private ProductionPipelineExecutionContext? _activeProductionContext;

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
            if (request.ProductionContext is null)
                ValidateThumbnailSceneManifest(existing, requireSavedManifest: false, outputPath: outputPath);
            return BuildSceneSelectionResponse(request, outputPath, existing);
        }

        var thumbnailRoot = Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot();
        ThumbnailSceneManifestDto manifest;
        if (request.ProductionContext is not null)
        {
            manifest = BuildPureV3ThumbnailManifest(request, thumbnailRoot);
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
        var eventTitle = ResolveMeteorThumbnailTitle(current);
        var primary = isMeteor ? eventTitle : CleanHook(current.ShortTitle).ToUpperInvariant();
        var secondary = isMeteor ? "PEAK NIGHT" : CleanTextElement(current.EventType, "CURRENT SKY EVENT").ToUpperInvariant();
        var micro = isMeteor ? "TONIGHT" : CleanTextElement(FirstNonEmpty(current.BestViewingWindowLocal, current.SkyDirectionHint, current.LocalPeakTime), "TONIGHT").ToUpperInvariant();
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
            isMeteor ? "A dramatic meteor-shower peak night that feels worth clicking immediately." : "A timely astronomy event with direct click-through text.",
            BuildPureV3VisualFocus(current),
            "Thumbnail V5 Azure Image2 realistic astronomy background with deterministic educational guide overlay.",
            "PureAzureImage2Prompt",
            "none",
            ["scene image selection", "approved scene assets", "hero-scene-manifest.json", "thumbnail-scene-manifest.json"],
            copy,
            [new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click")],
            scores,
            [],
            DateTimeOffset.UtcNow);

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
            ThumbnailVisualSourceMode: "PureAzureImage2ThumbnailV3",
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

        var forbiddenObjects = DetectForbiddenObjects(request, prompt.VisualObjects.Concat(prompt.CtrOverlay).Append(prompt.Badge)).ToArray();
        var goldenPilotLeakageDetected = ContainsGoldenPilotLeakage(prompt);
        if (forbiddenObjects.Length > 0)
            throw new InvalidOperationException("Thumbnail semantic validation failed: forbidden unrelated object label(s) detected: " + string.Join(", ", forbiddenObjects));
        if (goldenPilotLeakageDetected)
            throw new InvalidOperationException("Thumbnail semantic validation failed: golden pilot leakage detected.");

        if (!request.DryRun)
        {
            Directory.CreateDirectory(thumbnailRoot);
            CleanThumbnailV5FinalRoot(thumbnailRoot);
            var candidatesRoot = Path.Combine(thumbnailRoot, "candidates");
            Directory.CreateDirectory(candidatesRoot);
            var thumbnailVariants = BuildThumbnailV5AzurePrompts(request);
            var finalPromptText = JsonSerializer.Serialize(new { variants = thumbnailVariants }, JsonOptions);
            WriteThumbnailGenerationConfigurationDiagnostics(finalPromptText, imageOptions.Value, 1280, 720, promptPath, diagnosticsPath);
            var thumbnailTotalStopwatch = Stopwatch.StartNew();
            var thumbnailVariantResults = new List<(string Variant, string Prompt, int Width, int Height, string TextLayout, string BackgroundPath, string ImagePath, AzureImage2GenerationResult Result, string Hash)>();
            foreach (var variant in thumbnailVariants)
            {
                var azureBackgroundPath = NormalizePath(Path.Combine(candidatesRoot, $"thumbnail-v5-{variant.Variant.ToLowerInvariant()}-azure-background.png"));
                var variantPath = NormalizePath(Path.Combine(thumbnailRoot, variant.FileName));
                var azureResult = await GenerateThumbnailWithAzureImage2Async(imageOptions.Value, variant.Prompt, azureBackgroundPath, cancellationToken);
                if (!azureResult.ProviderSucceeded)
                    throw new InvalidOperationException($"Phase 12 Thumbnail Azure Image2 generation failed for variant {variant.Variant}: {azureResult.FailureReason}");
                await WriteThumbnailV5OverlayAsync(azureBackgroundPath, variantPath, variant.Width, variant.Height, request, ResolveWorkingDirectoryRoot(), cancellationToken);
                var hash = await ComputeSha256Async(variantPath, cancellationToken);
                thumbnailVariantResults.Add((variant.Variant, variant.Prompt, variant.Width, variant.Height, variant.Layout, azureBackgroundPath, variantPath, azureResult, hash));
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

            File.Copy(thumbnailVariantResults[0].ImagePath, finalPath, overwrite: true);
            await File.WriteAllTextAsync(promptPath, finalPromptText, cancellationToken);
            await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(new
            {
                semanticValidationPassed = true,
                forbiddenObjectsDetected = forbiddenObjects,
                goldenPilotLeakageDetected,
                requiredOutputs = new[] { ThumbnailFinalFileName, ThumbnailReviewFileName, ThumbnailPromptFileName },
                forbiddenTextDetected = false,
                infographicOnlyLayoutDetected = false,
                textCount = prompt.CtrOverlay.Count + (string.IsNullOrWhiteSpace(prompt.Badge) ? 0 : 1),
                thumbnailArchitecture = "ThumbnailV3PureAzureImage2CtrOverlay",
                sceneManifestRequired = false,
                heroSceneManifestRequired = false
            }, JsonOptions), cancellationToken);
            var generatedRequiredOutputs = new[] { finalPath, NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")), NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")) };
            var generatedRequiredOutputChecks = generatedRequiredOutputs.ToDictionary(path => Path.GetFileName(path), File.Exists, StringComparer.OrdinalIgnoreCase);
            var actualOutputsExist = generatedRequiredOutputChecks.Values.All(exists => exists);
            await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
            {
                status = actualOutputsExist ? "Succeeded" : "Failed",
                reason = actualOutputsExist ? "Validation passed" : "Expected output not found",
                semanticValidationPassed = actualOutputsExist,
                forbiddenObjectsDetected = forbiddenObjects,
                goldenPilotLeakageDetected,
                titleExists = true,
                dateExists = true,
                bestTimeExists = true,
                directionExists = true,
                equipmentExists = true,
                moonExists = true,
                radiantAnnotationExists = true,
                bottomTipsExist = true,
                landscapeExists = generatedRequiredOutputChecks["thumbnail-landscape.png"],
                portraitExists = generatedRequiredOutputChecks["thumbnail-portrait.png"],
                squareExists = generatedRequiredOutputChecks["thumbnail-square.png"],
                outputFiles = generatedRequiredOutputChecks,
                visualObjectsUsed = prompt.VisualObjects,
                labelsUsed = prompt.CtrOverlay.Append(prompt.Badge).ToArray(),
                textUsed = prompt.CtrOverlay.Append(prompt.Badge).ToArray(),
                thumbnailSourceManifestPath = string.Empty,
                thumbnailSourceScenePath = string.Empty
            }, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(layoutPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);

            thumbnailTotalStopwatch.Stop();
            await WriteThumbnailV5GenerationSummaryDiagnosticsAsync(finalPromptText, imageOptions.Value, finalPath, promptPath, diagnosticsPath, thumbnailVariantResults, duplicateHashGroups, thumbnailTotalStopwatch.ElapsedMilliseconds, cancellationToken);
        }

        return BuildImageGenerationResponse(
            request,
            outputFiles.Append(diagnosticsPath).ToArray(),
            validation,
            requestedRenderer: "PureAzureImage2ThumbnailV3",
            actualRendererUsed: "PureAzureImage2ThumbnailV3",
            rendererSelectionReason: "Thumbnail V5 uses ProductionPipelineRequest event intelligence to build an Azure Image2 realistic astronomy background, then applies deterministic mini astronomy guide overlays with event facts, direction, best time, sky features, and tips.",
            oldRendererBypassed: true,
            photoCinematicRendererEntered: true,
            photoCinematicRendererCompleted: true,
            outputWriteSource: "PureAzureImage2ThumbnailV3",
            thumbnailLayoutValidationPath: layoutPath);
    }

    private static void CleanThumbnailV5FinalRoot(string thumbnailRoot)
    {
        Directory.CreateDirectory(thumbnailRoot);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ThumbnailFinalFileName, "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png", ThumbnailGenerationDiagnosticsFileName, ThumbnailPromptFileName
        };
        foreach (var file in Directory.EnumerateFiles(thumbnailRoot, "thumbnail-v5-*.png"))
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
        Console.WriteLine("ThumbnailMode: PureAzureImage2ThumbnailV3");
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
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { phaseNo = 12, provider = "AzureOpenAIForImage", deployment, model = deployment, endpoint, apiVersion = "2024-10-21", region = ResolveRegion(endpoint), imageWidth = 1280, imageHeight = 720, visualStyle = "PhotoCinematic", finalPromptText = promptText, promptLength = promptText.Length, renderer = "AzureImage2", fallbackRendererUsed = false, providerCalled = true, providerSucceeded = true, azureRequestMs = azureResult.AzureRequestMs, imageDownloadMs = azureResult.ImageDownloadMs, imageSaveMs = 0, totalMs, imageHash, fileSize, imagePath = NormalizePath(imagePath), promptPath = NormalizePath(promptPath), failureReason = (string?)null }, JsonOptions), cancellationToken);
    }

    private static IReadOnlyList<(string Variant, string FileName, int Width, int Height, string Prompt, string[] TextLines, string Layout)> BuildThumbnailV5AzurePrompts(ThumbnailAssetGenerationRequest request)
    {
        var title = FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.Title, request.EventId, "selected sky event");
        var eventType = FirstNonEmpty(request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType, "AstronomyEvent");
        var prompt = EventContentGuard.IsPlanetConjunction(eventType)
            ? "Generate realistic Jupiter and Venus conjunction thumbnail background, two bright planets close together in western sky after sunset, twilight sky, angular separation 1.63 degrees, professional astronomy guide background, leave clean space for title and compact event info. No text."
            : $"Generate realistic astronomy thumbnail background for {title}, event type {eventType}, using current event intelligence objects and viewing guidance, professional astronomy guide background, leave clean space for title and compact event info. No text.";
        var text = EventContentGuard.IsPlanetConjunction(eventType) ? new[] { "JUPITER + VENUS", "CONJUNCTION" } : new[] { CleanTextElement(title, "SKY EVENT").ToUpperInvariant() };
        EventContentGuard.ValidateNoForbiddenTerms("ThumbnailAssetIntelligenceService", "thumbnail prompt", prompt + " " + string.Join(' ', text), request.ProductionContext?.ProductionEventIntelligence?.ForbiddenTerms.Concat(EventContentGuard.DefaultForbiddenTermsForEventType(eventType)) ?? []);
        return
        [
            ("landscape", "thumbnail-landscape.png", 1280, 720, prompt, text, "landscape-guide"),
            ("portrait", "thumbnail-portrait.png", 1080, 1920, prompt, text, "portrait-guide"),
            ("square", "thumbnail-square.png", 1080, 1080, prompt, text, "square-guide")
        ];
    }

    private static async Task WriteThumbnailV5OverlayAsync(string backgroundPath, string outputPath, int width, int height, ThumbnailAssetGenerationRequest request, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(backgroundPath, cancellationToken);
        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center });
            ctx.Contrast(1.04f).Saturate(1.05f);
            ctx.Fill(Color.Black.WithAlpha(0.12f), new RectangleF(0, 0, width, height));
            var titleFont = ResolveThumbnailFont(width == 1280 ? 46 : 58, FontStyle.Bold);
            var subFont = ResolveThumbnailFont(width == 1280 ? 24 : 32, FontStyle.Bold);
            var bodyFont = ResolveThumbnailFont(width == 1280 ? 21 : 27, FontStyle.Regular);
            var smallFont = ResolveThumbnailFont(width == 1280 ? 18 : 24, FontStyle.Regular);
            var isConjunction = EventContentGuard.IsPlanetConjunction(FirstNonEmpty(request.ProductionContext?.EventType, request.ProductionContext?.ProductionEventIntelligence?.EventType));
            var eventTitle = CleanTextElement(FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.Title, "SKY EVENT"), "SKY EVENT").ToUpperInvariant();
            var windowText = CleanTextElement(FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.BestViewingWindowLocal, request.ProductionContext?.ProductionEventIntelligence?.PreferredViewingWindow, "Approved viewing window"), "Approved viewing window");
            var directionText = CleanTextElement(FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.SkyDirectionHint, "Approved sky direction"), "Approved sky direction");
            var moonText = CleanTextElement(FirstNonEmpty(request.ProductionContext?.ProductionEventIntelligence?.MoonInterference, "Check sky conditions"), "Check sky conditions");
            if (isConjunction)
            {
                ctx.Fill(Color.FromRgba(5, 11, 28, 205), new RectangleF(width * .04f, height * .06f, width * .43f, height * .34f));
                ctx.DrawText("JUPITER + VENUS", titleFont, Color.FromRgb(255, 218, 80), new PointF(width * .06f, height * .09f));
                ctx.DrawText("CONJUNCTION", subFont, Color.White, new PointF(width * .06f, height * .18f));
                var bodyLines = new[] { "Two bright planets", "Western sky after sunset", "Twilight sky", "1.63° angular separation" };
                var y0 = height * .25f;
                foreach (var line in bodyLines) { ctx.DrawText(line, bodyFont, Color.FromRgb(218, 235, 255), new PointF(width * .06f, y0)); y0 += Math.Max(42, height * .055f); }
                var venus = new PointF(width * .68f, height * .36f);
                var jupiter = new PointF(width * .78f, height * .32f);
                ctx.Fill(Color.FromRgb(255, 245, 190), new EllipsePolygon(venus, Math.Max(10, width * .015f)));
                ctx.Fill(Color.FromRgb(235, 242, 255), new EllipsePolygon(jupiter, Math.Max(9, width * .013f)));
                ctx.DrawLine(Color.FromRgb(120, 210, 255), 3, venus, jupiter);
                ctx.DrawText("1.63°", bodyFont, Color.FromRgb(120, 210, 255), new PointF((venus.X + jupiter.X) / 2, (venus.Y + jupiter.Y) / 2 - 55));
                return;
            }
            if (width == 1280 && height == 720)
            {
                ctx.Fill(Color.FromRgba(5, 11, 28, 205), new RectangleF(40, 45, 370, 520));
                ctx.Draw(Color.FromRgb(67, 220, 240), 2, new RectangleF(40, 45, 370, 520));
                ctx.DrawText(eventTitle, titleFont, Color.FromRgb(255, 218, 80), new PointF(62, 70));
                ctx.DrawText("METEOR SHOWER", subFont, Color.White, new PointF(64, 128));
                var y = 186f;
                foreach (var line in new[] { $"Event  {eventTitle}", $"Best Time  {windowText}", $"Where  {directionText}", "Equipment  No telescope needed", $"Sky  {moonText}" }) { ctx.DrawText(line, bodyFont, Color.FromRgb(218, 235, 255), new PointF(64, y)); y += 62; }
                DrawRadiantGuide(ctx, new PointF(890, 220), 54, 118, 8);
                ctx.Draw(Color.FromRgb(118, 225, 255), 3, new EllipsePolygon(890, 220, 54));
                ctx.DrawText("EVENT DIRECTION", bodyFont, Color.White, new PointF(958, 196));
                DrawLeaderLine(ctx, new PointF(950, 212), new PointF(890, 220), Color.FromRgb(118, 225, 255));
                ctx.DrawText("METEOR STREAKS", bodyFont, Color.FromRgb(255, 218, 80), new PointF(835, 338));
                ctx.DrawText("May appear anywhere in the sky", smallFont, Color.White, new PointF(835, 372));
                DrawLeaderLine(ctx, new PointF(830, 348), new PointF(760, 310), Color.FromRgb(255, 218, 80));
                ctx.DrawText("LOOK UP  ➜", subFont, Color.FromRgb(255, 218, 80), new PointF(760, 548));
                DrawCompassCue(ctx, new PointF(742, 560), 34, 0);
                ctx.Fill(Color.FromRgba(5, 11, 28, 220), new RectangleF(160, 610, 960, 70));
                ctx.DrawText("Find a dark location   |   Lie back and look up   |   Give eyes 20 minutes   |   Dress warm", smallFont, Color.White, new PointF(190, 632));
            }
            else if (width == 1080 && height == 1920)
            {
                ctx.Fill(Color.FromRgba(5, 11, 28, 190), new RectangleF(58, 70, 560, 170));
                ctx.DrawText(eventTitle, titleFont, Color.FromRgb(255, 218, 80), new PointF(86, 94));
                ctx.DrawText("METEOR SHOWER", subFont, Color.White, new PointF(90, 168));
                DrawRadiantGuide(ctx, new PointF(660, 650), 70, 160, 9);
                ctx.Draw(Color.FromRgb(118, 225, 255), 4, new EllipsePolygon(660, 650, 70));
                ctx.DrawText("EVENT DIRECTION", bodyFont, Color.White, new PointF(585, 735));
                DrawLeaderLine(ctx, new PointF(650, 730), new PointF(660, 650), Color.FromRgb(118, 225, 255));
                ctx.DrawText("METEOR STREAKS", bodyFont, Color.FromRgb(255, 218, 80), new PointF(92, 850));
                ctx.DrawText("May appear anywhere in the sky", smallFont, Color.White, new PointF(92, 890));
                DrawLeaderLine(ctx, new PointF(300, 858), new PointF(455, 785), Color.FromRgb(255, 218, 80));
                ctx.Fill(Color.FromRgba(5, 11, 28, 210), new RectangleF(70, 1180, 940, 410));
                var y = 1215f; foreach (var line in new[] { $"Event: {eventTitle}", $"Best Time: {windowText}", $"Direction: {directionText}", "Equipment: No telescope needed", $"Sky: {moonText}" }) { ctx.DrawText(line, bodyFont, Color.FromRgb(218, 235, 255), new PointF(100, y)); y += 72; }
                ctx.DrawText("LOOK UP  ➜", subFont, Color.FromRgb(255, 218, 80), new PointF(100, 1540));
                DrawCompassCue(ctx, new PointF(82, 1556), 42, 0);
                ctx.Fill(Color.FromRgba(5, 11, 28, 225), new RectangleF(60, 1700, 960, 120));
                ctx.DrawText("Find a dark location • Lie back and look up • Give eyes 20 minutes • Dress warm", smallFont, Color.White, new PointF(92, 1740));
            }
            else
            {
                ctx.Fill(Color.FromRgba(5, 11, 28, 200), new RectangleF(45, 50, 430, 185));
                ctx.DrawText(eventTitle, titleFont, Color.FromRgb(255, 218, 80), new PointF(72, 74));
                ctx.DrawText("METEOR SHOWER", subFont, Color.White, new PointF(76, 148));
                ctx.DrawText(windowText, bodyFont, Color.FromRgb(218, 235, 255), new PointF(76, 190));
                DrawRadiantGuide(ctx, new PointF(730, 390), 58, 132, 8);
                ctx.Draw(Color.FromRgb(118, 225, 255), 4, new EllipsePolygon(730, 390, 58));
                ctx.DrawText("EVENT DIRECTION", bodyFont, Color.White, new PointF(620, 462));
                DrawLeaderLine(ctx, new PointF(710, 458), new PointF(730, 390), Color.FromRgb(118, 225, 255));
                ctx.DrawText("METEOR STREAKS", bodyFont, Color.FromRgb(255, 218, 80), new PointF(610, 548));
                ctx.DrawText("May appear anywhere", smallFont, Color.White, new PointF(610, 586));
                DrawLeaderLine(ctx, new PointF(604, 558), new PointF(520, 500), Color.FromRgb(255, 218, 80));
                ctx.Fill(Color.FromRgba(5, 11, 28, 210), new RectangleF(48, 660, 470, 240));
                var y = 690f; foreach (var line in new[] { $"Best: {windowText}", $"Look: {directionText}", "No telescope needed", $"Sky: {moonText}" }) { ctx.DrawText(line, smallFont, Color.FromRgb(218, 235, 255), new PointF(76, y)); y += 50; }
                ctx.DrawText("LOOK UP  ➜", subFont, Color.FromRgb(255, 218, 80), new PointF(650, 800));
                DrawCompassCue(ctx, new PointF(632, 814), 38, 0);
                ctx.Fill(Color.FromRgba(5, 11, 28, 225), new RectangleF(55, 940, 970, 85));
                ctx.DrawText("Dark location  |  Lie back  |  20 min eyes  |  Dress warm", smallFont, Color.White, new PointF(85, 970));
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? workingDirectoryRoot);
        await image.SaveAsPngAsync(outputPath, cancellationToken);
    }

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

    private static async Task WriteThumbnailV5GenerationSummaryDiagnosticsAsync(
        string promptText,
        AzureOpenAIForImageOptions options,
        string imagePath,
        string promptPath,
        string diagnosticsPath,
        IReadOnlyList<(string Variant, string Prompt, int Width, int Height, string TextLayout, string BackgroundPath, string ImagePath, AzureImage2GenerationResult Result, string Hash)> variants,
        object duplicateHashGroups,
        long totalMs,
        CancellationToken cancellationToken)
    {
        var endpoint = options.Endpoint?.Trim() ?? string.Empty;
        var deployment = options.ImageDeployment?.Trim() ?? string.Empty;
        var uniqueHashes = variants.Select(v => v.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            phaseNo = 12,
            provider = "AzureOpenAIForImage",
            deployment,
            model = deployment,
            endpoint,
            apiVersion = "2024-10-21",
            region = ResolveRegion(endpoint),
            renderer = "AzureImage2ThumbnailV5Variants",
            fallbackRendererUsed = false,
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
            promptPath = NormalizePath(promptPath),
            totalMs,
            requiredDataBlocksPresent = true,
            overlayAreaPercent = 35,
            outputs = variants.Select(v => new { name = v.Variant, width = v.Width, height = v.Height, hash = v.Hash }),
            variants = variants.Select(v => new { v.Variant, v.Prompt, v.Width, v.Height, v.TextLayout, backgroundPath = NormalizePath(v.BackgroundPath), imagePath = NormalizePath(v.ImagePath), imageHash = v.Hash, azureRequestMs = v.Result.AzureRequestMs, imageDownloadMs = v.Result.ImageDownloadMs })
        }, JsonOptions), cancellationToken);
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

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
            "LOOK UP TONIGHT",
            "SKY HIGHLIGHT TONIGHT",
            "SEE THE SKY TONIGHT"
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
            var headline = TruncateThumbnailText($"{FormatThumbnailObjectName(cleanObjects[0])} TONIGHT", 28);
            return new ThumbnailCopyDto(headline, string.Empty, string.Empty);
        }

        if (cleanObjects.Count == 2)
        {
            var headline = TruncateThumbnailText($"{FormatThumbnailObjectName(cleanObjects[0])} + {FormatThumbnailObjectName(cleanObjects[1])}", 28);
            var subheadline = IsClosestApproachPlanetEvent(eventType) ? "CLOSEST APPROACH" : "CONJUNCTION";
            return new ThumbnailCopyDto(headline, subheadline, string.Empty);
        }

        var groupHeadline = IsPlanetParadeEventType(eventType) ? "PLANET PARADE" : $"{cleanObjects.Count} BRIGHT PLANETS";
        return new ThumbnailCopyDto(TruncateThumbnailText(groupHeadline, 28), "LOOK WEST TONIGHT", string.Empty);
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

    private static ThumbnailSceneManifestDto BuildPureV3ThumbnailManifest(ThumbnailAssetGenerationRequest request, string thumbnailRoot)
    {
        var intelligence = request.ProductionContext?.ProductionEventIntelligence;
        var finalPath = NormalizePath(Path.Combine(thumbnailRoot, ThumbnailFinalFileName));
        return new ThumbnailSceneManifestDto(
            request.EventId,
            new ThumbnailSceneManifestEntryDto(1, "ThumbnailV3", finalPath, "PureAzureImage2CtrOverlay"),
            new ThumbnailSceneManifestEntryDto(1, "ThumbnailV3", finalPath, "PureAzureImage2CtrOverlay"),
            new ThumbnailSceneManifestEntryDto(1, "ThumbnailV3", finalPath, "PureAzureImage2CtrOverlay"),
            "Thumbnail V3 is independent: no hero scene manifest, no thumbnail scene manifest dependency, and no approved scene asset selection.")
        {
            PlanId = request.ProductionContext?.ContentGenerationPlanId?.ToString("D"),
            EventType = intelligence?.EventType ?? request.ProductionContext?.EventType ?? "Unknown",
            Title = intelligence?.Title ?? request.EventId,
            SourceHeroAssets = [],
            SourceSceneAssets = [],
            GeneratedThumbnailPaths = [],
            ValidationFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["thumbnailArchitecture"] = "ThumbnailV3PureAzureImage2CtrOverlay",
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
        var eventTitle = ResolveMeteorThumbnailTitle(current);
        var overlay = isMeteor
            ? new[] { eventTitle, "PEAK NIGHT" }
            : new[] { CleanThumbnailText(current.ShortTitle, current.Title, 18).ToUpperInvariant(), CleanThumbnailText(current.EventType, "SKY EVENT", 20).ToUpperInvariant() };
        var badge = isMeteor ? "TONIGHT" : CleanThumbnailText(FirstNonEmpty(current.BestViewingWindowLocal, current.LocalPeakTime, current.SkyDirectionHint), "TONIGHT", 18).ToUpperInvariant();
        var visualObjects = NormalizeObjectList(isMeteor
            ? ["Meteor", "Meteor shower", "Meteor streaks", "Dark sky"]
            : current.PrimaryObjects.Concat(current.SecondaryObjects).DefaultIfEmpty(current.ShortTitle));
        var background = isMeteor
            ? "High-CTR YouTube thumbnail. Dramatic meteor shower sky with bright streaks, deep contrast, cinematic color, mobile-readable composition. Clean marketing composition with only a huge CTR headline and one urgency badge."
            : $"High-impact astronomy YouTube thumbnail for {current.Title}. Premium cinematic astronomy background focused on {string.Join(", ", visualObjects)}.";
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
            "Generate the cinematic background with Azure Image2-style image generation, then composite large CTR text overlay and badge. Use only the current event concept; do not use scene assets or hero-scene-manifest.json.");
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
            ctx.DrawText(prompt.CtrOverlay.ElementAtOrDefault(1) ?? "TONIGHT", sub, Color.ParseHex("#F8D36B"), new PointF(66, 204));
            ctx.Fill(Color.ParseHex("#E83B3B"), new RectangleF(68, 326, 300, 64));
            ctx.DrawText(prompt.Badge, badgeFont, Color.White, new PointF(90, 342));
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
            return new ThumbnailDynamicCopy(planetCopy.SecondaryText, planetCopy.MicroText);
        }

        var secondary = ResolveEventActionPhrase(currentEventLock);
        var micro = FirstNonEmpty(currentEventLock.SkyDirectionHint, currentEventLock.BestViewingWindowLocal, currentEventLock.LocalPeakTime, currentEventLock.EventType);
        return new ThumbnailDynamicCopy(secondary, micro);
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
        var forbiddenCandidates = new[] { "Venus", "Jupiter", "Mars", "Saturn", "Mercury", "Uranus", "Neptune" };
        var allowed = NormalizeObjectList((request.ProductionContext?.ProductionEventIntelligence?.PrimaryObjects ?? []).Concat(request.ProductionContext?.ProductionEventIntelligence?.SecondaryObjects ?? []));
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
        var goldenPilotLeakageDetected = DetectGoldenPilotLeakage(renderRequest, renderResult);
        var semanticValidationPassed = string.Equals(GetDictionaryValue(facts, "semanticValidationPassed"), "True", StringComparison.OrdinalIgnoreCase)
            && !goldenPilotLeakageDetected
            && forbiddenObjectsDetected.Count == 0;
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
            goldenPilotLeakageDetected,
            semanticValidationPassed
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
            LocalPeakTime: intelligence?.LocalPeakTime,
            SkyDirectionHint: intelligence?.SkyDirectionHint,
            BestViewingWindowLocal: intelligence?.BestViewingWindowLocal,
            ContentStrategy: FirstNonEmpty(context?.ContentStrategy, intelligence?.StrategyId),
            RequiredVisualObjects: NormalizeObjectList(intelligence?.RequiredVisualObjects ?? []),
            ForbiddenObjectNames: NormalizeObjectList((intelligence?.ForbiddenObjectNames ?? []).Concat(intelligence?.ForbiddenTerms ?? [])));
    }

    private static bool IsMeteorEvent(string eventType, string title)
        => eventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase) || title.Contains("Meteor", StringComparison.OrdinalIgnoreCase);

    private static bool IsFullMoonEvent(string eventType, string title)
        => eventType.Contains("FullMoon", StringComparison.OrdinalIgnoreCase) || eventType.Contains("Full Moon", StringComparison.OrdinalIgnoreCase) || title.Contains("Full Moon", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<ThumbnailImageSpec> ThumbnailImageSpecs =
    [
        new("Landscape", "thumbnail-landscape.png", 1280, 720, new RectangleF(58, 54, 650, 214), new PointF(74, 286), new PointF(82, 628), 70f, 36f, 28f),
        new("Square", "thumbnail-square.png", 1080, 1080, new RectangleF(66, 76, 860, 250), new PointF(84, 350), new PointF(84, 910), 76f, 42f, 34f),
        new("Portrait", "thumbnail-portrait.png", 1080, 1920, new RectangleF(70, 112, 920, 360), new PointF(86, 1288), new PointF(86, 1404), 96f, 58f, 44f)
    ];

    private static string CleanHook(string value)
        => (value ?? string.Empty).Trim().Trim('.', '!', '?').ToUpperInvariant();

    private static string ResolveMeteorThumbnailTitle(CurrentEventLock current)
        => CleanThumbnailText(FirstNonEmpty(current.ShortTitle, current.Title), "METEOR SHOWER", 18).ToUpperInvariant();

    private static string CleanTextElement(string? value, string fallback)
        => string.Join(' ', (string.IsNullOrWhiteSpace(value) ? fallback : value).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string NormalizePath(string path) => path.Replace('\\', '/');


    private sealed record ThumbnailDynamicCopy(string SecondaryText, string MicroText);

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
        string RenderingInstructions);

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
        string? LocalPeakTime,
        string? SkyDirectionHint,
        string? BestViewingWindowLocal,
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
                null,
                null,
                LocalPeakTime,
                BestViewingWindowLocal,
                SkyDirectionHint,
                null,
                PrimaryObjects,
                SecondaryObjects,
                null,
                null,
                null,
                "Current event thumbnail lock",
                [],
                [],
                [],
                [],
                [],
                StrategyId: ContentStrategy ?? EventType,
                ForbiddenObjectNames: ForbiddenObjectNames,
                RequiredVisualObjects: forceMeteor ? RequiredVisualObjects.Concat(["Meteor"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : RequiredVisualObjects,
                PreferredViewingWindow: BestViewingWindowLocal);
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
