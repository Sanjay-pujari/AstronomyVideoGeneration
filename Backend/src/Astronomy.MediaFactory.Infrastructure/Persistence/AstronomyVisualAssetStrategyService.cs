using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyVisualAssetStrategyService(
    IOptions<RenderingOptions> renderingOptions,
    IOptions<CelestialAssetsOptions> celestialAssetsOptions,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IOptions<CelestialAssetPackOptions> celestialAssetPackOptions,
    IOptions<AzureOpenAIForImageOptions> imageOptions,
    IRuntimeAssetPathResolver assetPathResolver) : IAstronomyVisualAssetStrategyService
{
    private static readonly string[] RequiredProductionObjects = ["venus", "jupiter"];
    private static readonly string[] InventoryObjects = ["venus", "jupiter", "mercury", "moon", "saturn"];
    private static readonly string[] PreferredAssetFileNames = ["hero-transparent.png", "hero.png", "cinematic.png", "closeup.png"];

    public Task<AstronomyVisualAssetStrategyResponse> ResolveAstronomyVisualAssetStrategyAsync(AstronomyVisualAssetStrategyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDynamicRequest(request);

        var rendering = renderingOptions.Value;
        var celestial = celestialAssetsOptions.Value;
        var thumbnail = thumbnailOptions.Value;
        var pack = celestialAssetPackOptions.Value;
        var image = imageOptions.Value;

        var missingAssets = new List<AstronomyMissingAsset>();
        var warnings = new List<string>();
        var recommendations = new List<string>();

        var objectAssets = InventoryObjects.ToDictionary(static key => key, ResolveObjectAsset, StringComparer.OrdinalIgnoreCase);
        foreach (var key in RequiredProductionObjects)
        {
            if (objectAssets[key] is null)
            {
                var expected = assetPathResolver.ResolveCelestialAssetPath(key, "hero-transparent.png");
                missingAssets.Add(new AstronomyMissingAsset("celestial-object", key, expected, $"Local transparent {key} asset is required; fake circles and DrawEllipse fallbacks are not production-approved.", true));
            }
        }

        foreach (var key in InventoryObjects.Except(RequiredProductionObjects, StringComparer.OrdinalIgnoreCase))
        {
            if (objectAssets[key] is null)
            {
                var expected = assetPathResolver.ResolveCelestialAssetPath(key, "hero-transparent.png");
                missingAssets.Add(new AstronomyMissingAsset("celestial-object", key, expected, $"Local {key} asset was requested for inventory verification but is not required by the golden Venus/Jupiter scenes.", false));
            }
        }

        var backgroundSources = ResolveBackgroundSources(rendering, thumbnail, image, pack);
        var hasBackgroundSource = backgroundSources.Count > 0;
        if (!hasBackgroundSource)
        {
            missingAssets.Add(new AstronomyMissingAsset("background-source", "realistic-western-sky", rendering.WorkingDirectory, "No AI image configuration, Stellarium/thumbnail fallback, local Milky Way asset, or Rendering:WorkingDirectory background source was found.", true));
        }

        if (!HasUsableImageGeneration(image))
        {
            warnings.Add("AzureOpenAIForImage is not fully configured; strategy will rely on curated/runtime fallback backgrounds and must not generate final images yet.");
        }

        var constellationAssets = ResolveConstellationAssets();
        if (constellationAssets.Count == 0)
        {
            warnings.Add("No Leo/Regulus constellation reference asset was found; use a generic western-sky orientation guide until curated constellation data is available.");
            recommendations.Add("Add a Leo outline and Regulus reference-star layer to the local asset library for beginner-friendly context.");
        }

        var celestialAvailable = objectAssets.Where(kvp => kvp.Value is not null).Select(kvp => $"{kvp.Key}:{kvp.Value}").ToArray();
        var requiredCelestialMissing = RequiredProductionObjects.Where(key => objectAssets[key] is null).ToArray();
        var scenes = BuildScenePlans(objectAssets, hasBackgroundSource, constellationAssets.Count > 0);
        var strategy = new AstronomyVisualAssetStrategy(
            BackgroundLayer: new VisualLayerPlan(
                "Provide a realistic sky, horizon, landscape, and atmosphere only.",
                backgroundSources,
                ["realistic evening/twilight sky", "western horizon", "landscape silhouette", "atmospheric gradient"],
                ["planets", "labels", "arrows", "text", "title cards"],
                backgroundSources,
                hasBackgroundSource ? [] : ["realistic-western-sky-background-source"],
                ["Background prompts must explicitly exclude planets, labels, arrows, text, and cards.", "Backgrounds are inputs only; this endpoint performs no image generation."],
                hasBackgroundSource,
                hasBackgroundSource),
            CelestialObjectLayer: new VisualLayerPlan(
                "Composite real local transparent assets for Venus, Jupiter, Mercury, Moon, and Saturn; Venus/Jupiter block this golden event.",
                [celestial.RootPath, rendering.CelestialAssetsRoot, thumbnail.AssetRootPath, pack.OutputRootPath, assetPathResolver.GetCelestialRoot()],
                ["local transparent Venus asset", "local transparent Jupiter asset", "no fake circle production fallback"],
                ["DrawEllipse as final planet rendering", "fake circles", "debug dots", "silent placeholder fallback"],
                celestialAvailable,
                requiredCelestialMissing,
                ["Production readiness requires Venus and Jupiter local assets.", "Missing planet assets may only use review placeholders, never approved production output."],
                requiredCelestialMissing.Length == 0,
                requiredCelestialMissing.Length == 0),
            ConstellationLayer: new VisualLayerPlan(
                "Optional but preferred beginner reference with Leo/Regulus when available; otherwise generic western-sky guide.",
                [assetPathResolver.GetAssetsRoot(), Path.Combine(rendering.WorkingDirectory, "constellation-guides")],
                ["Leo outline if applicable", "Regulus reference star if applicable", "simple sky orientation guide"],
                ["dense expert star charts", "large text blocks", "debug coordinate grids"],
                constellationAssets,
                constellationAssets.Count == 0 ? ["leo-or-regulus-reference-layer"] : [],
                ["Constellation content must remain optional and must not block Venus/Jupiter event readiness."],
                true,
                true),
            SkyGuidanceLayer: new VisualLayerPlan(
                "Draw programmatic visual guidance over the background and real planet assets.",
                ["programmatic composer overlays"],
                ["West direction marker", "horizon line", "altitude guide", "Venus-to-Jupiter arrow", "closeness bracket"],
                ["planet rendering", "debug dots", "large card containers"],
                ["West marker", "horizon line", "altitude guide", "Venus-to-Jupiter arrow", "closeness bracket"],
                [],
                ["Guide layers may use vector drawing, but never as a fake planet substitute."],
                true,
                true),
            EducationalLayer: new VisualLayerPlan(
                "Add scene-specific teaching only: WHEN timeline, HOW 3-step guide, WHY comparison/significance.",
                ["programmatic educational overlays"],
                ["timeline", "3-step guide", "comparison/why graphic"],
                ["large card layout", "full-slide text boxes", "unrelated generic facts"],
                ["7:23 PM IST marker", "3-step guide", "short significance comparison"],
                [],
                ["Educational overlays must be small and scene-specific."],
                true,
                true),
            AnnotationLayer: new VisualLayerPlan(
                "Minimal labels, titles, and short captions only.",
                ["programmatic text overlays"],
                ["minimal title", "short labels", "short captions"],
                ["large text boxes", "title cards", "internal IDs", "file paths"],
                ["scene title", "Venus/Jupiter labels", "minimal CTA"],
                [],
                ["Keep text minimal; visual astronomy information must dominate."],
                true,
                true));

        if (requiredCelestialMissing.Length > 0)
        {
            recommendations.Add("Ingest or restore hero-transparent.png local assets for Venus and Jupiter before enabling infographic generation.");
        }
        if (!hasBackgroundSource)
        {
            recommendations.Add("Configure AzureOpenAIForImage for planet-free background generation or add a curated western-horizon sky background under the existing Rendering:WorkingDirectory/assets roots.");
        }
        recommendations.Add("Use this strategy gate before any final image generation; do not invoke TTS, video rendering, publishing, DailySkyGuide, or /api/pipeline/run.");

        var ready = requiredCelestialMissing.Length == 0
            && hasBackgroundSource
            && scenes.All(static scene => scene.IsNonCardComposition && !scene.UsesFakeCirclePlanets && scene.IsReadyForInfographicGeneration);

        return Task.FromResult(new AstronomyVisualAssetStrategyResponse(
            request.EventId,
            request.RegionId,
            string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            ready,
            strategy,
            scenes,
            missingAssets,
            warnings,
            recommendations));
    }

    private static void ValidateDynamicRequest(AstronomyVisualAssetStrategyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId)) throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
    }

    private string? ResolveObjectAsset(string objectKey)
    {
        foreach (var fileName in PreferredAssetFileNames)
        {
            var path = assetPathResolver.ResolveCelestialAssetPath(objectKey, fileName);
            if (File.Exists(path)) return path;
        }

        var cwdRelative = Path.Combine(Directory.GetCurrentDirectory(), "Backend", "src", "Astronomy.MediaFactory.Api", "assets", "celestial", objectKey, "hero-transparent.png");
        return File.Exists(cwdRelative) ? cwdRelative : null;
    }

    private List<string> ResolveBackgroundSources(RenderingOptions rendering, ThumbnailOptions thumbnail, AzureOpenAIForImageOptions image, CelestialAssetPackOptions pack)
    {
        var sources = new List<string>();
        if (HasUsableImageGeneration(image)) sources.Add($"AzureOpenAIForImage:{image.ImageDeployment} (background-only prompts; no planets/text/arrows)");
        if (thumbnail.EnableStellariumBackground || thumbnail.FallbackToStellariumFrame) sources.Add("ThumbnailGeneration Stellarium/extracted-frame background fallback");
        if (!string.IsNullOrWhiteSpace(rendering.WorkingDirectory)) sources.Add($"Rendering:WorkingDirectory={rendering.WorkingDirectory}");

        foreach (var candidate in new[]
                 {
                     assetPathResolver.ResolveCelestialAssetPath("milky-way", "hero.png"),
                     Path.Combine(pack.OutputRootPath, "milky-way", "hero.png")
                 })
        {
            if (File.Exists(candidate)) sources.Add(candidate);
        }

        return sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasUsableImageGeneration(AzureOpenAIForImageOptions image)
        => !string.IsNullOrWhiteSpace(image.Endpoint)
           && !image.Endpoint.Contains('<', StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(image.ImageDeployment)
           && (image.UseManagedIdentity || !string.IsNullOrWhiteSpace(image.ApiKey));

    private List<string> ResolveConstellationAssets()
    {
        var roots = new[]
        {
            assetPathResolver.GetAssetsRoot(),
            Path.Combine(renderingOptions.Value.WorkingDirectory, "constellation-guides")
        };
        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            matches.AddRange(Directory.EnumerateFiles(root, "*leo*", SearchOption.AllDirectories).Take(10));
            matches.AddRange(Directory.EnumerateFiles(root, "*regulus*", SearchOption.AllDirectories).Take(10));
        }
        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<AstronomySceneAssetPlan> BuildScenePlans(IReadOnlyDictionary<string, string?> objectAssets, bool hasBackgroundSource, bool hasConstellationReference)
    {
        return
        [
            BuildScene(1, "WHAT", "Show what viewers will see: Venus and Jupiter close together in a realistic sky.", ["realistic sky background", "horizon/atmosphere", "no card layout"], ["venus", "jupiter"], [], [], [], ["minimal title"], objectAssets, hasBackgroundSource, hasConstellationReference),
            BuildScene(2, "WHERE", "Show where to look above the western horizon.", ["western horizon background"], ["venus", "jupiter"], ["Leo/Regulus optional reference or generic western-sky guide"], ["West marker", "horizon line"], [], ["short labels"], objectAssets, hasBackgroundSource, hasConstellationReference),
            BuildScene(3, "WHEN", "Teach the exact viewing time with a twilight timeline.", ["twilight gradient/background"], [], [], [], ["timeline layer", "7:23 PM IST marker"], ["short time caption"], objectAssets, hasBackgroundSource, hasConstellationReference),
            BuildScene(4, "HOW", "Teach a simple 3-step viewing method.", ["western-sky background"], ["venus", "jupiter"], [], ["arrows", "West marker"], ["3 steps"], ["short step labels"], objectAssets, hasBackgroundSource, hasConstellationReference),
            BuildScene(5, "WHY", "Explain the significance of the close apparent pairing.", ["beautiful sky background"], ["venus", "jupiter"], [], ["closeness bracket"], ["brightness/comparison if useful", "short significance annotation"], ["short significance caption"], objectAssets, hasBackgroundSource, hasConstellationReference),
            BuildScene(6, "ACTION", "Close with a minimal call to action over a beautiful sky.", ["beautiful sky background", "horizon/atmosphere"], ["venus", "jupiter"], [], [], [], ["minimal CTA"], objectAssets, hasBackgroundSource, hasConstellationReference)
        ];
    }

    private static AstronomySceneAssetPlan BuildScene(
        int sceneNumber,
        string sceneKey,
        string purpose,
        IReadOnlyList<string> backgroundRequirements,
        IReadOnlyList<string> celestialObjects,
        IReadOnlyList<string> constellationRequirements,
        IReadOnlyList<string> skyGuidance,
        IReadOnlyList<string> educational,
        IReadOnlyList<string> annotations,
        IReadOnlyDictionary<string, string?> objectAssets,
        bool hasBackgroundSource,
        bool hasConstellationReference)
    {
        var missing = new List<string>();
        var warnings = new List<string>();
        if (!hasBackgroundSource) missing.Add("background-source");
        foreach (var obj in celestialObjects.Where(obj => !objectAssets.TryGetValue(obj, out var path) || path is null)) missing.Add($"celestial:{obj}");
        if (constellationRequirements.Count > 0 && !hasConstellationReference) warnings.Add("Leo/Regulus reference not found; generic western-sky guide is acceptable for this scene.");

        return new AstronomySceneAssetPlan(
            sceneNumber,
            sceneKey,
            purpose,
            backgroundRequirements,
            celestialObjects,
            constellationRequirements,
            skyGuidance,
            educational,
            annotations,
            UsesCardLayout: false,
            UsesFakeCirclePlanets: false,
            IsNonCardComposition: true,
            IsReadyForInfographicGeneration: missing.Count == 0,
            missing,
            warnings);
    }
}
