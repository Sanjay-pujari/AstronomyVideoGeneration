using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class VisualAssetGenerationService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<VisualAssetGenerationService> logger) : IVisualAssetGenerationService
{
    private const string AssemblyFileName = "scene-assembly-plan.json";
    private const string OutputDirectoryName = "visual-assets";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];
    private const string StellariumCaptureType = "StellariumCapture";
    private const string AiPromptVisualType = "AiPromptVisual";
    private const string SkyMapVisualType = "SkyMapVisual";
    private const string ConstellationGuideVisualType = "ConstellationGuideVisual";
    private const string NasaMetadataVisualType = "NasaMetadataVisual";
    private const string TextOverlayVisualType = "TextOverlayVisual";
    private static readonly string[] ForbiddenTerms =
    [
        "internal asset IDs",
        "internal asset ID",
        "TextOverlayCard",
        "SkyMapCard",
        "PlannedVisual",
        "Objects: scene",
        "file name",
        "asset ids",
        "asset id",
        "prompt",
        "GUID"
    ];

    public async Task<VisualAssetGenerationResponse> GenerateVisualAssetsAsync(VisualAssetGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1) throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var candidates = await ResolveCandidatesAsync(request, root, cancellationToken);
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var planned = new List<VisualAssetGenerationPlanItem>();
        var sceneCount = 0;
        var generatedVisualCount = 0;
        var approvedVisualCount = 0;
        var failedVisualCount = 0;

        foreach (var candidate in candidates)
        {
            var planRoot = BuildPlanRoot(root, candidate.RegionId, candidate.Id.ToString("D"));
            var assemblyPath = Path.Combine(planRoot, "assembly", AssemblyFileName);
            var assembly = await ReadJsonAsync<SceneAssemblyPlanDocument>(assemblyPath, cancellationToken);
            if (assembly is null)
            {
                warnings.Add($"Missing or unreadable scene assembly plan for plan {candidate.Id}: {assemblyPath}");
                continue;
            }

            var package = DiscoverPackageFiles(planRoot);
            var sameEventSkyMapCards = await DiscoverSameEventSkyMapCardsAsync(root, candidate, cancellationToken);
            foreach (var scene in assembly.Scenes.OrderBy(x => x.SceneNumber))
            {
                sceneCount++;
                var outputDir = Path.Combine(planRoot, OutputDirectoryName);
                var backgroundPath = Path.Combine(outputDir, $"scene-{scene.SceneNumber:000}-background.png");
                var overlayPath = Path.Combine(outputDir, $"scene-{scene.SceneNumber:000}-overlay.png");
                var manifestPath = Path.Combine(outputDir, $"scene-{scene.SceneNumber:000}-visual-manifest.json");
                var source = SelectPrimarySource(planRoot, assembly, scene, package, sameEventSkyMapCards);
                var overlaySource = SelectOverlaySource(planRoot, scene, package);
                var objects = ExtractObjects(scene, source.Path).ToArray();
                var availability = DiscoverVisualAvailability(planRoot, scene, package);
                var issues = await ValidateVisualApprovalAsync(assembly, scene, source, objects, availability, cancellationToken);

                planned.Add(new VisualAssetGenerationPlanItem(
                    assembly.ContentGenerationPlanId,
                    assembly.RegionId,
                    scene.SceneNumber,
                    scene.SceneName,
                    backgroundPath,
                    overlaySource is null ? string.Empty : overlayPath,
                    manifestPath,
                    source.Type,
                    source.Path,
                    objects,
                    issues));

                if (request.DryRun) continue;
                Directory.CreateDirectory(outputDir);
                var wroteBackground = false;
                var wroteOverlay = false;
                try
                {
                    if ((File.Exists(backgroundPath) || File.Exists(manifestPath)) && !request.OverwriteExisting)
                    {
                        warnings.Add($"Skipped existing visual assets for plan {assembly.ContentGenerationPlanId} scene {scene.SceneNumber:000}. Set overwriteExisting=true to replace them.");
                        continue;
                    }

                    await RenderBackgroundAsync(backgroundPath, assembly, scene, source, objects, cancellationToken);
                    wroteBackground = true;
                    generatedFiles.Add(backgroundPath);
                    generatedVisualCount++;

                    if (overlaySource is not null)
                    {
                        await RenderOverlayAsync(overlayPath, scene, overlaySource, objects, cancellationToken);
                        wroteOverlay = true;
                        generatedFiles.Add(overlayPath);
                        generatedVisualCount++;
                    }

                    var manifest = new SceneVisualAssetManifest(
                        scene.SceneNumber,
                        backgroundPath,
                        wroteOverlay ? overlayPath : string.Empty,
                        source.Type,
                        objects,
                        issues.Count == 0,
                        issues,
                        DateTimeOffset.UtcNow);
                    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
                    generatedFiles.Add(manifestPath);

                    if (issues.Count == 0) approvedVisualCount++; else failedVisualCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedVisualCount++;
                    warnings.Add($"Visual asset generation failed for plan {assembly.ContentGenerationPlanId} scene {scene.SceneNumber:000}: {ex.Message}");
                    logger.LogWarning(ex, "Visual asset generation failed for plan {PlanId} scene {SceneNumber}", assembly.ContentGenerationPlanId, scene.SceneNumber);
                    if (wroteBackground || wroteOverlay) warnings.Add($"Partial visual outputs may exist for scene {scene.SceneNumber:000}.");
                }
            }
        }

        return new VisualAssetGenerationResponse(candidates.Count, sceneCount, generatedVisualCount, approvedVisualCount, failedVisualCount, generatedFiles, warnings, planned);
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolveCandidatesAsync(VisualAssetGenerationRequest request, string root, CancellationToken cancellationToken)
    {
        var query = db.ContentGenerationPlans.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.RegionId)) query = query.Where(p => p.RegionId == request.RegionId.Trim());
        if (request.PlanIds is { Count: > 0 })
        {
            var ids = request.PlanIds.ToHashSet();
            query = query.Where(p => ids.Contains(p.Id));
        }
        query = query.Where(p => p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null);
        var plans = await query.OrderByDescending(p => p.ScheduledUtc ?? DateTimeOffset.MinValue).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return plans.Where(p => File.Exists(Path.Combine(BuildPlanRoot(root, p.RegionId, p.Id.ToString("D")), "assembly", AssemblyFileName))).Take(request.MaxPlans ?? int.MaxValue).ToArray();
    }

    private static PackageFiles DiscoverPackageFiles(string planRoot)
    {
        var roots = PlanAssetRoots(planRoot).ToArray();
        return new(
            roots.SelectMany(root => Enumerate(root, "stellarium"))
                .Concat(roots.SelectMany(root => Enumerate(root, "screenshots")))
                .Concat(roots.Where(Directory.Exists)
                    .SelectMany(root => Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                    .Where(path => path.Contains("stellarium", StringComparison.OrdinalIgnoreCase) || path.Contains("capture", StringComparison.OrdinalIgnoreCase)))
                .Where(IsImage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            roots.SelectMany(root => Enumerate(root, "ai-image-prompts")).Where(path => IsJson(path) || IsImage(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            roots.SelectMany(root => Enumerate(root, "sky-map-cards")).Where(IsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            roots.SelectMany(root => Enumerate(root, "constellation-guides")).Where(IsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            roots.SelectMany(root => Enumerate(root, "text-cards")).Where(IsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            roots.SelectMany(root => Enumerate(root, "nasa-assets")).Where(IsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> PlanAssetRoots(string planRoot)
    {
        yield return planRoot;

        var parent = Directory.GetParent(planRoot);
        var regionRoot = parent?.Name.Equals("plans", StringComparison.OrdinalIgnoreCase) == true
            ? parent.Parent
            : null;
        if (regionRoot is not null) yield return Path.Combine(regionRoot.FullName, Path.GetFileName(planRoot));
    }

    private static IEnumerable<string> Enumerate(string planRoot, string directoryName)
    {
        var path = Path.Combine(planRoot, directoryName);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories) : [];
    }

    private async Task<IReadOnlyList<string>> DiscoverSameEventSkyMapCardsAsync(string root, ContentGenerationPlan candidate, CancellationToken cancellationToken)
    {
        if (candidate.AstronomyEventIntelligenceId is null) return [];

        var planIds = await db.ContentGenerationPlans
            .AsNoTracking()
            .Where(plan =>
                plan.Id != candidate.Id &&
                plan.RegionId == candidate.RegionId &&
                plan.AstronomyEventIntelligenceId == candidate.AstronomyEventIntelligenceId)
            .Select(plan => plan.Id)
            .ToArrayAsync(cancellationToken);

        return planIds
            .SelectMany(planId => EnumerateSkyMapCardsForPlan(root, candidate.RegionId, planId.ToString("D")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSkyMapCardsForPlan(string root, string regionId, string planId)
        => Enumerate(BuildPlanRoot(root, regionId, planId), "sky-map-cards")
            .Concat(Enumerate(Path.Combine(root, "assets", regionId, planId), "sky-map-cards"))
            .Where(IsJson);

    private static VisualSource SelectPrimarySource(string planRoot, SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene, PackageFiles package, IReadOnlyList<string> sameEventSkyMapCards)
    {
        var stellarium = PickStellariumSource(planRoot, scene, package);
        if (stellarium is not null) return new(StellariumCaptureType, stellarium);

        if (ShouldPreferFinderMap(assembly, scene))
            return SelectFinderMapSource(planRoot, assembly, scene, package, sameEventSkyMapCards);

        var ai = PickAiPromptSource(planRoot, scene, package);
        var sky = PickByLayerOrPackage(planRoot, scene, ["SkyMapVisual", "SkyMapCard"], package.SkyMapCards, scene.SceneNumber);
        var guide = PickByLayerOrPackage(planRoot, scene, ["ConstellationGuideVisual", "ConstellationGuide"], package.ConstellationGuides, scene.SceneNumber);

        if (ai is not null) return new(AiPromptVisualType, ai);
        if (sky is not null) return new(SkyMapVisualType, sky);
        if (guide is not null) return new(ConstellationGuideVisualType, guide);

        var nasa = PickByLayerOrPackage(planRoot, scene, ["NasaMetadataVisual", "NasaAsset"], package.NasaAssets, scene.SceneNumber);
        if (nasa is not null) return new(NasaMetadataVisualType, nasa);
        var text = PickByLayerOrPackage(planRoot, scene, ["TextOverlayVisual", "TextOverlayCard"], package.TextCards, scene.SceneNumber);
        if (text is not null) return new(TextOverlayVisualType, text);
        return new(AiPromptVisualType, string.Empty);
    }

    private static VisualSource SelectFinderMapSource(string planRoot, SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene, PackageFiles package, IReadOnlyList<string> sameEventSkyMapCards)
    {
        var sceneSky = PickSceneLayerOrSceneFile(planRoot, scene, ["SkyMapVisual", "SkyMapCard"], package.SkyMapCards, scene.SceneNumber);
        if (sceneSky is not null) return new(SkyMapVisualType, sceneSky);

        var samePlanSky = PickReusableAssemblyLayerOrFile(planRoot, assembly, ["SkyMapVisual", "SkyMapCard"], package.SkyMapCards, scene.SceneNumber);
        if (samePlanSky is not null) return new(SkyMapVisualType, samePlanSky);

        var sameEventSky = PickReusableSceneFile(sameEventSkyMapCards, scene.SceneNumber);
        if (sameEventSky is not null) return new(SkyMapVisualType, sameEventSky);

        var guide = PickByLayerOrPackage(planRoot, scene, ["ConstellationGuideVisual", "ConstellationGuide"], package.ConstellationGuides, scene.SceneNumber);
        if (guide is not null) return new(ConstellationGuideVisualType, guide);

        var ai = PickAiPromptSource(planRoot, scene, package);
        return new(AiPromptVisualType, ai ?? string.Empty);
    }

    private static bool ShouldPreferFinderMap(SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene)
    {
        if (!assembly.ContentCategory.Equals("RareEventAlert", StringComparison.OrdinalIgnoreCase)) return false;
        if (scene.SceneNumber is 2 or 3) return true;
        var name = scene.SceneName;
        return name.Contains("watch", StringComparison.OrdinalIgnoreCase)
            || name.Contains("viewing", StringComparison.OrdinalIgnoreCase)
            || name.Contains("guidance", StringComparison.OrdinalIgnoreCase)
            || name.Contains("finder", StringComparison.OrdinalIgnoreCase);
    }

    private static string? PickStellariumSource(string planRoot, SceneAssemblyScene scene, PackageFiles package)
    {
        var layerImages = scene.Layers.Select(l => ResolveAssetPath(planRoot, l.AssetPath)).Where(IsImage).ToArray();
        return PickSceneFile(package.StellariumImages.Concat(layerImages.Where(p => p.Contains("stellarium", StringComparison.OrdinalIgnoreCase))), scene.SceneNumber);
    }

    private static string? PickAiPromptSource(string planRoot, SceneAssemblyScene scene, PackageFiles package)
    {
        var layerJson = scene.Layers
            .Where(l => new[] { "AiPromptVisual", "AiHeroImage", "AiCinematicImage", "PlannedVisual" }.Contains(l.AssetType, StringComparer.OrdinalIgnoreCase))
            .Select(l => ResolveAssetPath(planRoot, l.AssetPath))
            .Where(path => IsJson(path) && File.Exists(path));
        return PickSceneFile(layerJson.Concat(package.AiPrompts.Where(path => IsJson(path) && File.Exists(path))), scene.SceneNumber);
    }

    private static VisualSource? SelectOverlaySource(string planRoot, SceneAssemblyScene scene, PackageFiles package)
    {
        var layer = scene.Layers.FirstOrDefault(l => l.LayerType.Equals("Overlay", StringComparison.OrdinalIgnoreCase));
        if (layer is not null) return new(TextOverlayVisualType, ResolveAssetPath(planRoot, layer.AssetPath));
        var text = PickSceneFile(package.TextCards, scene.SceneNumber);
        return text is null ? null : new VisualSource(TextOverlayVisualType, text);
    }

    private static string? PickByLayerOrPackage(string planRoot, SceneAssemblyScene scene, string[] assetTypes, IReadOnlyList<string> packageFiles, int sceneNumber)
    {
        var layer = scene.Layers.FirstOrDefault(l => assetTypes.Contains(l.AssetType, StringComparer.OrdinalIgnoreCase));
        if (layer is not null) return ResolveAssetPath(planRoot, layer.AssetPath);
        return PickSceneFile(packageFiles, sceneNumber);
    }

    private static string? PickSceneLayerOrSceneFile(string planRoot, SceneAssemblyScene scene, string[] assetTypes, IReadOnlyList<string> packageFiles, int sceneNumber)
    {
        var layer = scene.Layers.FirstOrDefault(l => assetTypes.Contains(l.AssetType, StringComparer.OrdinalIgnoreCase));
        if (layer is not null) return ResolveAssetPath(planRoot, layer.AssetPath);
        return PickExactSceneFile(packageFiles, sceneNumber);
    }

    private static string? PickReusableAssemblyLayerOrFile(string planRoot, SceneAssemblyPlanDocument assembly, string[] assetTypes, IReadOnlyList<string> packageFiles, int sceneNumber)
    {
        var layer = assembly.Scenes
            .SelectMany(scene => scene.Layers
                .Where(layer => assetTypes.Contains(layer.AssetType, StringComparer.OrdinalIgnoreCase))
                .Select(layer => new { scene.SceneNumber, Path = ResolveAssetPath(planRoot, layer.AssetPath) }))
            .OrderBy(candidate => ReuseDistance(candidate.SceneNumber, sceneNumber))
            .FirstOrDefault();
        return layer?.Path ?? PickReusableSceneFile(packageFiles, sceneNumber);
    }

    private static string? PickSceneFile(IEnumerable<string> files, int sceneNumber)
        => PickExactSceneFile(files, sceneNumber) ?? files.FirstOrDefault();

    private static string? PickExactSceneFile(IEnumerable<string> files, int sceneNumber)
    {
        var token = $"scene-{sceneNumber:000}";
        return files.FirstOrDefault(f => Path.GetFileName(f).Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? PickReusableSceneFile(IEnumerable<string> files, int sceneNumber)
        => files
            .Select((path, index) => new { Path = path, Index = index, SceneNumber = TryReadSceneNumber(path) })
            .OrderBy(candidate => candidate.SceneNumber.HasValue ? ReuseDistance(candidate.SceneNumber.Value, sceneNumber) : int.MaxValue)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

    private static int ReuseDistance(int candidateSceneNumber, int requestedSceneNumber)
    {
        if (candidateSceneNumber == requestedSceneNumber) return 0;
        if (candidateSceneNumber < requestedSceneNumber) return (requestedSceneNumber - candidateSceneNumber) * 2 - 1;
        return (candidateSceneNumber - requestedSceneNumber) * 2;
    }

    private static int? TryReadSceneNumber(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), "scene[-_](?<scene>[0-9]{1,3})", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["scene"].Value, out var sceneNumber) ? sceneNumber : null;
    }

    private async Task RenderBackgroundAsync(string outputPath, SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene, VisualSource source, IReadOnlyList<string> objects, CancellationToken ct)
    {
        if (IsImage(source.Path))
        {
            using var loaded = await Image.LoadAsync<Rgba32>(source.Path, ct);
            loaded.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(1920, 1080), Mode = ResizeMode.Crop }));
            await loaded.SaveAsPngAsync(outputPath, new PngEncoder(), ct);
            return;
        }

        var doc = await TryReadJsonAsync(source.Path, ct);
        var title = CleanText(FindString(doc, "title", "titleText", "headline", "sceneTitle") ?? scene.SceneName, scene.SceneName);
        var subtitle = CleanText(FindString(doc, "subtitle", "subtitleText", "shortFact", "description", "instruction") ?? CinematicSubtitle(source.Type, objects), CinematicSubtitle(source.Type, objects));
        var fact = CleanText(FindString(doc, "fact", "keyMessage", "summary", "caption") ?? assembly.Title, assembly.Title);

        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#050713"));
        image.Mutate(ctx =>
        {
            DrawSpaceBackground(ctx, 1920, 1080, scene.SceneNumber);
            if (source.Type == SkyMapVisualType) DrawSkyMap(ctx, objects);
            else if (source.Type == ConstellationGuideVisualType) DrawConstellation(ctx, objects);
            else if (source.Type == NasaMetadataVisualType) DrawNasaInfo(ctx, title, fact);
            else DrawPlanetaryPoster(ctx, objects);
            DrawTitleBlock(ctx, title, subtitle, source.Type == NasaMetadataVisualType ? FindString(doc, "credit", "credits", "source") : null);
            DrawVignette(ctx, 1920, 1080);
        });
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), ct);
    }

    private async Task RenderOverlayAsync(string outputPath, SceneAssemblyScene scene, VisualSource source, IReadOnlyList<string> objects, CancellationToken ct)
    {
        var doc = await TryReadJsonAsync(source.Path, ct);
        var title = CleanText(FindString(doc, "title", "titleText", "headline") ?? scene.SceneName, scene.SceneName);
        var subtitle = CleanText(FindString(doc, "subtitle", "subtitleText") ?? string.Join(" near ", objects.Take(2)), string.Join(" near ", objects.Take(2)));
        var message = CleanText(FindString(doc, "keyMessage", "message", "summary", "caption") ?? "Look low above the horizon during the best viewing window.", "Look low above the horizon during the best viewing window.");
        using var image = new Image<Rgba32>(1920, 1080, Color.Transparent);
        image.Mutate(ctx =>
        {
            var panel = new RectangleF(120, 680, 1120, 260);
            ctx.Fill(Color.Black.WithAlpha(0.48f), panel);
            ctx.Draw(Color.ParseHex("#86D7FF").WithAlpha(0.38f), 2, panel);
            ctx.DrawText(new RichTextOptions(Font(52, FontStyle.Bold)) { Origin = new PointF(155, 716), WrappingLength = 1020 }, title, Color.White);
            ctx.DrawText(new RichTextOptions(Font(32)) { Origin = new PointF(158, 790), WrappingLength = 990 }, subtitle, Color.ParseHex("#CFEAFF"));
            ctx.DrawText(new RichTextOptions(Font(30)) { Origin = new PointF(158, 848), WrappingLength = 990 }, message, Color.ParseHex("#FFE2A6"));
        });
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), ct);
    }

    private static void DrawSpaceBackground(IImageProcessingContext ctx, int width, int height, int seed)
    {
        ctx.Fill(Color.ParseHex("#06091B"), new RectangleF(0, 0, width, height));
        ctx.Fill(Color.ParseHex("#18396E").WithAlpha(0.45f), new EllipsePolygon(width * .70f, height * .26f, width * .52f));
        ctx.Fill(Color.ParseHex("#5F2E8F").WithAlpha(0.25f), new EllipsePolygon(width * .28f, height * .34f, width * .38f));
        var random = new Random(seed * 4591);
        for (var i = 0; i < 360; i++)
        {
            var x = random.NextSingle() * width; var y = random.NextSingle() * height; var r = random.NextSingle() * 1.8f + .5f;
            ctx.Fill(Color.White.WithAlpha(random.NextSingle() * .60f + .25f), new EllipsePolygon(x, y, r));
        }
    }

    private static void DrawPlanetaryPoster(IImageProcessingContext ctx, IReadOnlyList<string> objects)
    {
        ctx.Fill(Color.ParseHex("#F7D08A").WithAlpha(.95f), new EllipsePolygon(1380, 420, 70));
        ctx.Fill(Color.ParseHex("#F7D08A").WithAlpha(.22f), new EllipsePolygon(1380, 420, 122));
        ctx.Fill(Color.ParseHex("#DCA24B"), new EllipsePolygon(1550, 570, 46));
        ctx.DrawLine(Color.ParseHex("#B7E9FF").WithAlpha(.40f), 2, new PointF(220, 820), new PointF(1720, 640));
        ctx.DrawText(new RichTextOptions(Font(28, FontStyle.Bold)) { Origin = new PointF(1290, 320) }, objects.ElementAtOrDefault(0) ?? "Venus", Color.White);
        ctx.DrawText(new RichTextOptions(Font(28, FontStyle.Bold)) { Origin = new PointF(1480, 640) }, objects.ElementAtOrDefault(1) ?? "Jupiter", Color.White);
    }

    private static void DrawSkyMap(IImageProcessingContext ctx, IReadOnlyList<string> objects)
    {
        for (var i = 0; i < 5; i++) ctx.DrawLine(Color.ParseHex("#7AB8FF").WithAlpha(.25f), 2, new PointF(300 + i * 260, 780), new PointF(480 + i * 180, 250));
        ctx.DrawLine(Color.ParseHex("#F8C16A").WithAlpha(.78f), 4, new PointF(260, 790), new PointF(1660, 790));
        ctx.DrawText(new RichTextOptions(Font(28)) { Origin = new PointF(780, 820) }, "Western horizon", Color.ParseHex("#FFE2A6"));
        DrawPlanetaryPoster(ctx, objects);
    }

    private static void DrawConstellation(IImageProcessingContext ctx, IReadOnlyList<string> objects)
    {
        var points = new[] { new PointF(820, 320), new PointF(970, 420), new PointF(1120, 360), new PointF(1240, 520), new PointF(1030, 650), new PointF(880, 560) };
        for (var i = 0; i < points.Length; i++)
        {
            ctx.Fill(Color.White, new EllipsePolygon(points[i].X, points[i].Y, 5));
            if (i > 0) ctx.DrawLine(Color.ParseHex("#9DDCFF").WithAlpha(.72f), 3, points[i - 1], points[i]);
        }
        ctx.DrawLine(Color.ParseHex("#F8C16A").WithAlpha(.78f), 4, new PointF(520, 740), new PointF(820, 600));
        ctx.DrawText(new RichTextOptions(Font(30, FontStyle.Bold)) { Origin = new PointF(1260, 545) }, objects.FirstOrDefault() ?? "Guide stars", Color.White);
    }

    private static void DrawNasaInfo(IImageProcessingContext ctx, string title, string fact)
    {
        ctx.Fill(Color.Black.WithAlpha(.42f), new RectangleF(1120, 180, 560, 500));
        ctx.Draw(Color.ParseHex("#FFFFFF").WithAlpha(.35f), 2, new RectangleF(1120, 180, 560, 500));
        ctx.DrawText(new RichTextOptions(Font(34, FontStyle.Bold)) { Origin = new PointF(1160, 230), WrappingLength = 480 }, title, Color.White);
        ctx.DrawText(new RichTextOptions(Font(27)) { Origin = new PointF(1160, 350), WrappingLength = 480 }, fact, Color.ParseHex("#D5F3FF"));
    }

    private static void DrawTitleBlock(IImageProcessingContext ctx, string title, string subtitle, string? credit)
    {
        ctx.Fill(Color.Black.WithAlpha(.34f), new RectangleF(90, 100, 1160, 285));
        ctx.DrawLine(Color.ParseHex("#F4B35F").WithAlpha(.85f), 5, new PointF(120, 135), new PointF(610, 135));
        ctx.DrawText(new RichTextOptions(Font(68, FontStyle.Bold)) { Origin = new PointF(120, 170), WrappingLength = 1060 }, title, Color.White);
        ctx.DrawText(new RichTextOptions(Font(34)) { Origin = new PointF(124, 285), WrappingLength = 1040 }, subtitle, Color.ParseHex("#CBE3FF"));
        if (!string.IsNullOrWhiteSpace(credit)) ctx.DrawText(new RichTextOptions(Font(20)) { Origin = new PointF(124, 965), WrappingLength = 900 }, CleanText(credit, string.Empty), Color.White.WithAlpha(.58f));
    }

    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(Color.Black.WithAlpha(.16f), new RectangleF(0, 0, width, 110));
        ctx.Fill(Color.Black.WithAlpha(.22f), new RectangleF(0, height - 150, width, 150));
    }

    private static IReadOnlyList<string> ExtractObjects(SceneAssemblyScene scene, string sourcePath)
    {
        var text = string.Join(' ', scene.SceneName, sourcePath, string.Join(' ', scene.RenderNotes));
        var found = new List<string>();
        foreach (var name in new[] { "Venus", "Jupiter", "Moon", "Saturn", "Mars", "Mercury", "Orion", "Pleiades", "Milky Way" })
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase)) found.Add(name);
        if (found.Count == 0) found.AddRange(["Venus", "Jupiter"]);
        return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
    }

    private static VisualAvailability DiscoverVisualAvailability(string planRoot, SceneAssemblyScene scene, PackageFiles package)
        => new(
            PickStellariumSource(planRoot, scene, package) is not null,
            PickAiPromptSource(planRoot, scene, package) is not null,
            PickByLayerOrPackage(planRoot, scene, ["SkyMapVisual", "SkyMapCard"], package.SkyMapCards, scene.SceneNumber) is not null,
            PickByLayerOrPackage(planRoot, scene, ["ConstellationGuideVisual", "ConstellationGuide"], package.ConstellationGuides, scene.SceneNumber) is not null,
            PickByLayerOrPackage(planRoot, scene, ["NasaMetadataVisual", "NasaAsset"], package.NasaAssets, scene.SceneNumber) is not null);

    private static async Task<IReadOnlyList<string>> ValidateVisualApprovalAsync(SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene, VisualSource source, IReadOnlyList<string> objects, VisualAvailability availability, CancellationToken ct)
    {
        var issues = new List<string>();
        if (source.Type == TextOverlayVisualType && availability.HasAstronomyVisual)
            issues.Add("TextOverlayVisual cannot be the primary background while AiPromptVisual, SkyMapVisual, ConstellationGuideVisual, NasaMetadataVisual, or StellariumCapture is available.");

        if (source.Type == AiPromptVisualType)
        {
            if (string.IsNullOrWhiteSpace(source.Path)) issues.Add("AiPromptVisual sourcePath is required and must point to the AI prompt JSON.");
            else if (!IsJson(source.Path)) issues.Add("AiPromptVisual sourcePath must point to the AI prompt JSON, not a rendered image or other asset.");
        }

        var doc = await TryReadJsonAsync(source.Path, ct);
        var renderText = new[]
        {
            CleanText(FindString(doc, "title", "titleText", "headline", "sceneTitle") ?? scene.SceneName, scene.SceneName),
            CleanText(FindString(doc, "subtitle", "subtitleText", "shortFact", "description", "instruction") ?? CinematicSubtitle(source.Type, objects), CinematicSubtitle(source.Type, objects)),
            CleanText(FindString(doc, "fact", "keyMessage", "summary", "caption") ?? assembly.Title, assembly.Title),
            source.Type == NasaMetadataVisualType ? CleanText(FindString(doc, "credit", "credits", "source"), string.Empty) : string.Empty
        };

        foreach (var text in renderText.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (Regex.IsMatch(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"))
                issues.Add("Generated image text contains GUID-like text after sanitization.");
            foreach (var term in ForbiddenTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) issues.Add($"Generated image text contains forbidden review term '{term}'.");
            if (text.Contains('{') && text.Contains('}')) issues.Add("Generated image text contains JSON-like debug text after sanitization.");
        }

        return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<JsonDocument?> TryReadJsonAsync(string path, CancellationToken ct)
    {
        if (!IsJson(path) || !File.Exists(path)) return null;
        try { return JsonDocument.Parse(await File.ReadAllTextAsync(path, ct)); } catch { return null; }
    }

    private static string? FindString(JsonDocument? doc, params string[] names)
    {
        if (doc is null) return null;
        return FindString(doc.RootElement, names);
    }

    private static string? FindString(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var nested = FindString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static string CleanText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : Regex.Replace(value, "\\s+", " ").Trim();
        text = Regex.Replace(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", string.Empty);
        foreach (var term in ForbiddenTerms) text = Regex.Replace(text, Regex.Escape(term), string.Empty, RegexOptions.IgnoreCase);
        text = text.Replace('{', ' ').Replace('}', ' ').Replace('[', ' ').Replace(']', ' ');
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Length > 130 ? text[..130].Trim() : text;
    }

    private static string CinematicSubtitle(string sourceType, IReadOnlyList<string> objects) => sourceType switch
    {
        SkyMapVisualType => "A clean sky map for the best viewing window.",
        ConstellationGuideVisualType => "Guide lines show how the scene relates to nearby stars.",
        NasaMetadataVisualType => "A space-science context card for the featured sky event.",
        TextOverlayVisualType => "Key viewing guidance for tonight's sky.",
        _ => $"A cinematic view featuring {string.Join(" and ", objects.Take(2))}."
    };

    private static bool IsImage(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsJson(string? path) => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
    private static string ResolveAssetPath(string planRoot, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return string.Empty;
        if (Path.IsPathRooted(assetPath)) return assetPath;
        var rooted = Path.Combine(planRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(rooted) ? rooted : assetPath;
    }

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string BuildPlanRoot(string root, string regionId, string planId) => Path.Combine(root, "assets", regionId, "plans", planId);
    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct) where T : class => File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), JsonOptions) : null;

    private static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = SystemFonts.Collection.Families.FirstOrDefault();
        return family.CreateFont(size, style);
    }

    private sealed record VisualSource(string Type, string Path);
    private sealed record VisualAvailability(bool HasStellariumCapture, bool HasAiPromptVisual, bool HasSkyMapVisual, bool HasConstellationGuideVisual, bool HasNasaMetadataVisual)
    {
        public bool HasAstronomyVisual => HasStellariumCapture || HasAiPromptVisual || HasSkyMapVisual || HasConstellationGuideVisual || HasNasaMetadataVisual;
    }
    private sealed record PackageFiles(IReadOnlyList<string> StellariumImages, IReadOnlyList<string> AiPrompts, IReadOnlyList<string> SkyMapCards, IReadOnlyList<string> ConstellationGuides, IReadOnlyList<string> TextCards, IReadOnlyList<string> NasaAssets);
}
