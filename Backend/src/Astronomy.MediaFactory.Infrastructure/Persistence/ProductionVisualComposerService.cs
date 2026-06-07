using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
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

public sealed class ProductionVisualComposerService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    IAICinematicImageGenerator imageGenerator,
    ILogger<ProductionVisualComposerService> logger) : IProductionVisualComposerService
{
    private const string OutputDirectoryName = "production-visuals";
    private const string AssemblyFileName = "assembly/scene-assembly-plan.json";
    private const string PolishedNarrationPath = "narration/narration-polished.json";
    private const string ProviderWarning = "AI image provider not configured. Production image cannot be generated.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ForbiddenImageTerms = ["GUID", "TextOverlayCard", "SkyMapCard", "PlannedVisual", "prompt", "raw JSON", "database ID", "file name", "asset type"];

    public async Task<ProductionVisualGenerationResponse> GenerateProductionVisualsAsync(ProductionVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (request.MaxPlans is < 1) throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var plans = await ResolvePlansAsync(request, cancellationToken);
        var generatedFiles = new List<string>();
        var warnings = new List<string>();
        var planned = new List<ProductionVisualPlanItem>();
        var sceneCount = 0;
        var aiImageCount = 0;
        var finalImageCount = 0;
        var approvedImageCount = 0;
        var failedImageCount = 0;

        foreach (var plan in plans)
        {
            var planRoot = BuildPlanRoot(root, plan.RegionId, plan.Id.ToString("D"));
            var sceneHints = await LoadSceneHintsAsync(planRoot, cancellationToken);
            var sidecarContext = await LoadSidecarContextAsync(planRoot, cancellationToken);
            var eventContext = BuildEventContext(plan, sceneHints, sidecarContext);
            var sceneNumbers = await ResolveSceneNumbersAsync(planRoot, sceneHints, cancellationToken);

            foreach (var sceneNumber in sceneNumbers.OrderBy(x => x).Take(4))
            {
                sceneCount++;
                var outputDir = Path.Combine(planRoot, OutputDirectoryName);
                var specPath = Path.Combine(outputDir, $"scene-{sceneNumber:000}-visual-spec.json");
                var backgroundPath = Path.Combine(outputDir, $"scene-{sceneNumber:000}-background-ai.png");
                var finalPath = Path.Combine(outputDir, $"scene-{sceneNumber:000}-final.png");
                var scene = sceneHints.GetValueOrDefault(sceneNumber, SceneHint.Empty(sceneNumber));
                var spec = BuildVisualSpec(sceneNumber, plan, eventContext, scene);
                var itemWarnings = new List<string>();

                planned.Add(new ProductionVisualPlanItem(
                    plan.Id.ToString("D"),
                    plan.RegionId,
                    sceneNumber,
                    specPath,
                    backgroundPath,
                    finalPath,
                    spec.ImagePrompt,
                    spec.OverlayText,
                    spec.LocalAssetObjects,
                    itemWarnings));

                if (request.DryRun) continue;
                Directory.CreateDirectory(outputDir);

                if (File.Exists(finalPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing production visual for plan {plan.Id:D} scene {sceneNumber:000}. Set overwriteExisting=true to replace it.");
                    if (IsUsableImage(finalPath)) approvedImageCount++;
                    continue;
                }

                try
                {
                    await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, JsonOptions), cancellationToken);
                    generatedFiles.Add(specPath);

                    var backgroundGeneratedByAi = await EnsureBackgroundAsync(spec, backgroundPath, request.OverwriteExisting, itemWarnings, cancellationToken);
                    if (backgroundGeneratedByAi) aiImageCount++;
                    if (File.Exists(backgroundPath)) generatedFiles.Add(backgroundPath);

                    await ComposeFinalFrameAsync(backgroundPath, finalPath, spec, cancellationToken);
                    finalImageCount++;
                    generatedFiles.Add(finalPath);
                    approvedImageCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedImageCount++;
                    itemWarnings.Add(ex.Message);
                    warnings.Add($"Plan {plan.Id:D} scene {sceneNumber:000}: {ex.Message}");
                    logger.LogWarning(ex, "Production visual generation failed for plan {PlanId} scene {SceneNumber}", plan.Id, sceneNumber);
                }

                warnings.AddRange(itemWarnings.Select(w => $"Plan {plan.Id:D} scene {sceneNumber:000}: {w}"));
            }
        }

        if (!request.DryRun && warnings.Any(w => w.Contains(ProviderWarning, StringComparison.OrdinalIgnoreCase)) == false && !imageGenerator.IsConfigured)
            warnings.Add(ProviderWarning);

        return new ProductionVisualGenerationResponse(
            plans.Count,
            sceneCount,
            sceneCount,
            aiImageCount,
            finalImageCount,
            approvedImageCount,
            failedImageCount,
            generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            planned);
    }

    private async Task<bool> EnsureBackgroundAsync(SceneVisualSpec spec, string backgroundPath, bool overwriteExisting, List<string> warnings, CancellationToken cancellationToken)
    {
        if (File.Exists(backgroundPath) && !overwriteExisting) return false;

        if (imageGenerator.IsConfigured)
        {
            var result = await imageGenerator.GenerateAsync(new AICinematicAssetRequest(
                $"production-scene-{spec.SceneNumber:000}",
                $"scene-{spec.SceneNumber:000}",
                spec.ScenePurpose,
                "RareEventAlert",
                $"production-scene-{spec.SceneNumber:000}",
                "SceneBackground",
                "wonder",
                spec.SceneNumber is 1 or 4 ? "Cinematic" : "Instructional",
                spec.VisualStyle,
                spec.ImagePrompt,
                "debug text, json, labels, watermarks, overlaid words, UI cards, fake metadata, distorted planets",
                1920,
                1080,
                backgroundPath),
                cancellationToken);

            warnings.AddRange(result.Warnings);
            if (result.ProviderConfigured && File.Exists(result.ImagePath ?? backgroundPath)) return true;
        }

        warnings.Add(ProviderWarning);
        await RenderFallbackAstronomyBackgroundAsync(backgroundPath, spec, cancellationToken);
        return false;
    }

    private static async Task ComposeFinalFrameAsync(string backgroundPath, string finalPath, SceneVisualSpec spec, CancellationToken cancellationToken)
    {
        using var image = IsUsableImage(backgroundPath)
            ? await Image.LoadAsync<Rgba32>(backgroundPath, cancellationToken)
            : new Image<Rgba32>(1920, 1080, Color.ParseHex("#061124"));
        image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(1920, 1080), Mode = ResizeMode.Crop }));

        var titleFont = ResolveFont(66, FontStyle.Bold);
        var subtitleFont = ResolveFont(38, FontStyle.Bold);
        var labelFont = ResolveFont(30, FontStyle.Bold);
        var smallFont = ResolveFont(26, FontStyle.Regular);
        var localObjectAssets = LoadLocalObjectAssets(spec).ToArray();

        image.Mutate(ctx =>
        {
            DrawSkyOverlay(ctx, spec);
            DrawObjectOverlay(ctx, spec, localObjectAssets);
            DrawInformationPanel(ctx, spec, titleFont, subtitleFont, labelFont, smallFont);
            DrawVignette(ctx);
        });

        foreach (var asset in localObjectAssets) asset.Image.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");
        await image.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
    }

    private static void DrawSkyOverlay(IImageProcessingContext ctx, SceneVisualSpec spec)
    {
        var horizonY = 850f;
        ctx.DrawLine(Color.ParseHex("#F6C177").WithAlpha(0.82f), 5, new PointF(150, horizonY), new PointF(1770, horizonY));
        ctx.DrawText(new RichTextOptions(ResolveFont(28, FontStyle.Bold)) { Origin = new PointF(820, horizonY + 28), WrappingLength = 320 }, string.IsNullOrWhiteSpace(spec.Direction) ? "WESTERN HORIZON" : spec.Direction.ToUpperInvariant(), Color.ParseHex("#FFE7B1"));
        ctx.DrawLine(Color.ParseHex("#8FD2FF").WithAlpha(0.5f), 3, new PointF(960, 735), new PointF(960, 842));
        ctx.DrawLine(Color.ParseHex("#8FD2FF").WithAlpha(0.5f), 3, new PointF(930, 765), new PointF(960, 735));
        ctx.DrawLine(Color.ParseHex("#8FD2FF").WithAlpha(0.5f), 3, new PointF(990, 765), new PointF(960, 735));
        if (spec.OverlayText.Any(x => x.Contains("constellation", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.DrawLine(Color.White.WithAlpha(0.26f), 2, new PointF(1320, 320), new PointF(1450, 390));
            ctx.DrawLine(Color.White.WithAlpha(0.26f), 2, new PointF(1450, 390), new PointF(1540, 300));
        }
    }

    private static void DrawObjectOverlay(IImageProcessingContext ctx, SceneVisualSpec spec, IReadOnlyList<LocalObjectAsset> localObjectAssets)
    {
        var venus = spec.Objects.FirstOrDefault(o => o.Contains("venus", StringComparison.OrdinalIgnoreCase));
        var jupiter = spec.Objects.FirstOrDefault(o => o.Contains("jupiter", StringComparison.OrdinalIgnoreCase));
        var left = venus ?? spec.Objects.FirstOrDefault() ?? "Bright planet";
        var right = jupiter ?? spec.Objects.Skip(1).FirstOrDefault() ?? "Second planet";

        DrawAssetOrMarker(ctx, localObjectAssets, left, new PointF(760, 420), 74, Color.ParseHex("#FFF2BA"));
        DrawAssetOrMarker(ctx, localObjectAssets, right, new PointF(1090, 355), 98, Color.ParseHex("#F6D3A0"));
        ctx.DrawLine(Color.White.WithAlpha(0.30f), 2, new PointF(785, 418), new PointF(1060, 360));
    }

    private static void DrawAssetOrMarker(IImageProcessingContext ctx, IReadOnlyList<LocalObjectAsset> localObjectAssets, string label, PointF center, int size, Color color)
    {
        var asset = localObjectAssets.FirstOrDefault(a => label.Contains(a.ObjectName, StringComparison.OrdinalIgnoreCase) || a.ObjectName.Contains(label, StringComparison.OrdinalIgnoreCase));
        if (asset is not null)
        {
            ctx.DrawImage(asset.Image, new Point((int)(center.X - size / 2f), (int)(center.Y - size / 2f)), 0.95f);
            ctx.Draw(Color.White.WithAlpha(0.72f), 2, new EllipsePolygon(center.X, center.Y, size / 2f + 7));
            ctx.DrawText(new RichTextOptions(ResolveFont(30, FontStyle.Bold)) { Origin = new PointF(center.X + size / 2f + 22, center.Y - 20), WrappingLength = 300 }, label, Color.White);
            return;
        }

        DrawPlanetMarker(ctx, center, Math.Max(16, size / 4f), label, color);
    }

    private static void DrawPlanetMarker(IImageProcessingContext ctx, PointF center, float radius, string label, Color color)
    {
        ctx.Fill(color.WithAlpha(0.20f), new EllipsePolygon(center.X, center.Y, radius * 3.0f));
        ctx.Fill(color, new EllipsePolygon(center.X, center.Y, radius));
        ctx.Draw(Color.White.WithAlpha(0.86f), 2, new EllipsePolygon(center.X, center.Y, radius + 7));
        ctx.DrawText(new RichTextOptions(ResolveFont(30, FontStyle.Bold)) { Origin = new PointF(center.X + radius + 22, center.Y - 20), WrappingLength = 300 }, label, Color.White);
    }

    private static void DrawInformationPanel(IImageProcessingContext ctx, SceneVisualSpec spec, Font titleFont, Font subtitleFont, Font labelFont, Font smallFont)
    {
        ctx.Fill(Color.Black.WithAlpha(0.50f), new RectangleF(92, 82, 890, 290));
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(0.32f), 2, new RectangleF(92, 82, 890, 290));
        ctx.DrawText(new RichTextOptions(titleFont) { Origin = new PointF(126, 112), WrappingLength = 820 }, CleanOverlay(spec.EventTitle, 44), Color.White);
        var subtitle = spec.SceneNumber == 1 ? "Look west after sunset" : FirstUseful(spec.OverlayText, spec.BestViewingTime, "Best after sunset");
        ctx.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(130, 236), WrappingLength = 790 }, CleanOverlay(subtitle, 64), Color.ParseHex("#F6C177"));
        ctx.DrawText(new RichTextOptions(smallFont) { Origin = new PointF(132, 304), WrappingLength = 800 }, CleanOverlay(string.Join(" • ", spec.Objects.Take(3)), 84), Color.ParseHex("#CFE9FF"));

        ctx.Fill(Color.Black.WithAlpha(0.44f), new RectangleF(1210, 112, 570, 262));
        ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(0.36f), 2, new RectangleF(1210, 112, 570, 262));
        ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(1240, 140), WrappingLength = 500 }, "Viewing guide", Color.ParseHex("#FFE7B1"));
        ctx.DrawText(new RichTextOptions(smallFont) { Origin = new PointF(1240, 198), WrappingLength = 500 }, CleanOverlay($"Where: {spec.Direction}\nWhen: {spec.BestViewingTime}\nWhy: bright planets close in the evening sky", 150), Color.White);
    }


    private static IEnumerable<LocalObjectAsset> LoadLocalObjectAssets(SceneVisualSpec spec)
    {
        foreach (var objectName in spec.Objects.Select(NormalizeObjectAssetName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ResolveLocalObjectAssetPath(objectName);
            if (path is null) continue;
            LocalObjectAsset asset;
            try
            {
                var image = Image.Load<Rgba32>(path);
                image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(112, 112), Mode = ResizeMode.Max }));
                asset = new LocalObjectAsset(objectName, image);
            }
            catch
            {
                // If a bundled object PNG cannot be decoded, the compositor still draws a clean marker.
                continue;
            }

            yield return asset;
        }
    }

    private static string? ResolveLocalObjectAssetPath(string objectName)
    {
        var relative = Path.Combine("assets", "celestial", objectName, "hero-transparent.png");
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relative),
            Path.Combine(Directory.GetCurrentDirectory(), relative),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "src", "Astronomy.MediaFactory.Api", relative)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalizeObjectAssetName(string value)
        => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static async Task RenderFallbackAstronomyBackgroundAsync(string backgroundPath, SceneVisualSpec spec, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#071126"));
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.ParseHex("#182D60").WithAlpha(0.65f), new EllipsePolygon(1420, 235, 760));
            ctx.Fill(Color.ParseHex("#5B2A86").WithAlpha(0.30f), new EllipsePolygon(420, 260, 520));
            ctx.Fill(Color.ParseHex("#F2A65A").WithAlpha(0.24f), new EllipsePolygon(1030, 970, 920));
            var random = new Random(spec.SceneNumber * 7919);
            for (var i = 0; i < 310; i++)
            {
                var x = random.NextSingle() * 1920;
                var y = random.NextSingle() * 780;
                var r = 0.7f + random.NextSingle() * 2.4f;
                ctx.Fill(Color.White.WithAlpha(0.25f + random.NextSingle() * 0.62f), new EllipsePolygon(x, y, r));
            }
            ctx.Fill(Color.ParseHex("#12213D").WithAlpha(0.90f), new RectangularPolygon(0, 850, 1920, 230));
        });
        Directory.CreateDirectory(Path.GetDirectoryName(backgroundPath) ?? ".");
        await image.SaveAsPngAsync(backgroundPath, new PngEncoder(), cancellationToken);
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolvePlansAsync(ProductionVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        var query = db.ContentGenerationPlans
            .AsNoTracking()
            .Include(p => p.AstronomyEventIntelligence)!.ThenInclude(e => e!.Objects)
            .Include(p => p.AstronomyContentOpportunity)!.ThenInclude(o => o!.AstronomyEventIntelligence)!.ThenInclude(e => e!.Objects)
            .Where(p => p.RegionId == request.RegionId);

        if (request.PlanIds is { Count: > 0 }) query = query.Where(p => request.PlanIds.Contains(p.Id));
        else query = query.Where(p => p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null);

        return await query.OrderByDescending(p => p.ScheduledUtc ?? DateTimeOffset.MinValue).ThenBy(p => p.Id).Take(request.MaxPlans ?? int.MaxValue).ToListAsync(cancellationToken);
    }


    private static async Task<PlanSidecarContext> LoadSidecarContextAsync(string planRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(planRoot)) return PlanSidecarContext.Empty;

        var roots = new[] { "sky-map-cards", "constellation-guides", "ai-image-prompts", "asset-jobs", "stellarium" }
            .Select(name => Path.Combine(planRoot, name))
            .Where(Directory.Exists)
            .ToArray();
        var objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fragments = new List<string>();
        string? direction = null;
        string? bestViewingTime = null;
        string? visibilityNotes = null;

        foreach (var file in roots.SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)).Take(80))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, cancellationToken));
                direction ??= FindString(doc.RootElement, ["direction", "observationDirection", "lookDirection", "horizon", "azimuthDirection"]);
                bestViewingTime ??= FindString(doc.RootElement, ["bestViewingTime", "bestTime", "viewingTime", "localViewingTime", "timeWindow"]);
                visibilityNotes ??= FindString(doc.RootElement, ["visibilityNotes", "viewingGuidance", "summary", "description"]);
                foreach (var name in FindStringArray(doc.RootElement, ["objects", "objectNames", "targets", "celestialObjects", "planets", "constellations"]))
                    objects.Add(name);
                fragments.AddRange(new[] { direction, bestViewingTime, visibilityNotes }.Where(x => !string.IsNullOrWhiteSpace(x))!);
            }
            catch
            {
                // Sidecar files are best-effort context sources; unreadable files should not block composition.
            }
        }

        return new PlanSidecarContext(objects.ToArray(), direction ?? string.Empty, bestViewingTime ?? string.Empty, visibilityNotes ?? string.Empty, string.Join(' ', fragments));
    }

    private static EventContext BuildEventContext(ContentGenerationPlan plan, IReadOnlyDictionary<int, SceneHint> sceneHints, PlanSidecarContext sidecar)
    {
        var intelligence = plan.AstronomyEventIntelligence ?? plan.AstronomyContentOpportunity?.AstronomyEventIntelligence;
        var objects = intelligence?.Objects.Select(o => o.ObjectName).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? ParseStringArray(plan.PlannedObjectNamesJson).ToArray();
        if (objects.Length == 0 && sidecar.Objects.Count > 0) objects = sidecar.Objects.ToArray();
        if (objects.Length == 0) objects = ExtractKnownObjects($"{plan.Title} {intelligence?.Title} {intelligence?.Summary} {sidecar.SearchText} {string.Join(' ', sceneHints.Values.Select(s => s.Narration))}").ToArray();
        var eventTitle = FirstNonEmpty(plan.Title, intelligence?.Title, plan.AstronomyContentOpportunity?.Title, string.Join(" and ", objects), "Tonight's sky event");
        var location = FirstNonEmpty(intelligence?.LocationName, intelligence?.RegionId, plan.RegionId);
        var direction = FirstNonEmpty(FindJsonString(intelligence?.RawDataJson, ["direction", "observationDirection", "lookDirection", "azimuthDirection"]), sidecar.Direction, FindDirection(eventTitle + " " + intelligence?.Summary + " " + sidecar.SearchText), "western sky");
        var bestTime = FirstNonEmpty(FindJsonString(intelligence?.RawDataJson, ["bestViewingTime", "bestTime", "viewingTime", "localViewingTime"]), sidecar.BestViewingTime, FormatViewingTime(intelligence?.PeakUtc, intelligence?.TimeZone), "after sunset");
        var notes = FirstNonEmpty(intelligence?.Summary, intelligence?.Description, sidecar.VisibilityNotes, plan.PlanningReason, "A bright, easy-to-find evening sky event.");
        return new EventContext(FirstNonEmpty(intelligence?.EventType, plan.PrimaryAstronomyEventTypeCode, "Sky event"), eventTitle, objects, location, direction, bestTime, notes);
    }

    private static SceneVisualSpec BuildVisualSpec(int sceneNumber, ContentGenerationPlan plan, EventContext evt, SceneHint scene)
    {
        var purpose = ResolvePurpose(sceneNumber, scene.Purpose);
        var eventTitle = evt.Objects.Any(o => o.Contains("venus", StringComparison.OrdinalIgnoreCase)) && evt.Objects.Any(o => o.Contains("jupiter", StringComparison.OrdinalIgnoreCase))
            ? "Venus and Jupiter Tonight"
            : evt.EventTitle;
        var subtitle = sceneNumber switch
        {
            1 => $"Look {evt.Direction} {evt.BestViewingTime}",
            2 => $"Best viewing: {evt.BestViewingTime}",
            3 => $"1. Find the horizon  2. Face {evt.Direction}  3. Let eyes adjust",
            4 => "Clear skies reminder",
            _ => evt.Notes
        };
        var style = sceneNumber is 2 or 3 ? "cinematic astronomy infographic with clean sky-map overlay" : "cinematic astronomy infographic";
        var objectsText = evt.Objects.Count > 0 ? string.Join(" and ", evt.Objects.Take(4)) : "the featured celestial objects";
        var prompt = $"Production-quality viewer-facing astronomy scene for {eventTitle}. Show {objectsText} in the {evt.Direction} near the horizon at {evt.BestViewingTime} for viewers in {evt.Location}. {purpose} scene. Cinematic realistic night-sky background, warm horizon glow, accurate uncluttered composition, no text, no UI, no cards, no debug metadata.";
        var localAssets = evt.Objects.Where(HasLikelyLocalAsset).ToArray();
        return new SceneVisualSpec(
            sceneNumber,
            purpose,
            CleanOverlay(eventTitle, 80),
            evt.Objects.Count > 0 ? evt.Objects : ["Venus", "Jupiter"],
            evt.Location,
            evt.Direction,
            evt.BestViewingTime,
            style,
            prompt,
            [CleanOverlay(subtitle, 120), CleanOverlay(evt.Notes, 120)],
            localAssets,
            true,
            true,
            true);
    }

    private static async Task<Dictionary<int, SceneHint>> LoadSceneHintsAsync(string planRoot, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, SceneHint>();
        await LoadNarrationHintsAsync(planRoot, result, cancellationToken);
        await LoadAssemblyHintsAsync(planRoot, result, cancellationToken);
        return result;
    }

    private static async Task LoadAssemblyHintsAsync(string planRoot, Dictionary<int, SceneHint> result, CancellationToken cancellationToken)
    {
        var assemblyPath = Path.Combine(planRoot, AssemblyFileName.Replace('/', Path.DirectorySeparatorChar));
        var assembly = await ReadJsonAsync<SceneAssemblyPlanDocument>(assemblyPath, cancellationToken);
        if (assembly is null) return;
        foreach (var scene in assembly.Scenes)
        {
            var existing = result.GetValueOrDefault(scene.SceneNumber, SceneHint.Empty(scene.SceneNumber));
            result[scene.SceneNumber] = existing with { Title = FirstNonEmpty(existing.Title, scene.SceneName), Purpose = FirstNonEmpty(existing.Purpose, scene.SceneName) };
        }
    }

    private static async Task LoadNarrationHintsAsync(string planRoot, Dictionary<int, SceneHint> result, CancellationToken cancellationToken)
    {
        var path = Path.Combine(planRoot, PolishedNarrationPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        CollectSceneHints(doc.RootElement, result);
    }

    private static void CollectSceneHints(JsonElement element, Dictionary<int, SceneHint> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var sceneNumber = FindInt(element, ["sceneNumber", "scene", "sceneIndex"]);
            if (sceneNumber.HasValue)
            {
                var number = sceneNumber.Value <= 0 ? sceneNumber.Value + 1 : sceneNumber.Value;
                var current = result.GetValueOrDefault(number, SceneHint.Empty(number));
                result[number] = current with
                {
                    Title = FirstNonEmpty(current.Title, FindDirectString(element, ["sceneTitle", "title", "sceneName"])),
                    Narration = FirstNonEmpty(current.Narration, FindDirectString(element, ["polishedNarration", "finalNarration", "narrationText", "scriptText", "text"])),
                    Purpose = FirstNonEmpty(current.Purpose, FindDirectString(element, ["scenePurpose", "purpose", "role"]))
                };
            }
            foreach (var property in element.EnumerateObject()) CollectSceneHints(property.Value, result);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectSceneHints(item, result);
        }
    }

    private static async Task<IReadOnlyList<int>> ResolveSceneNumbersAsync(string planRoot, IReadOnlyDictionary<int, SceneHint> sceneHints, CancellationToken cancellationToken)
    {
        if (sceneHints.Count > 0) return sceneHints.Keys.OrderBy(x => x).ToArray();
        var assembly = await ReadJsonAsync<SceneAssemblyPlanDocument>(Path.Combine(planRoot, AssemblyFileName.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
        return assembly?.Scenes.Select(s => s.SceneNumber).OrderBy(x => x).ToArray() ?? [1, 2, 3, 4];
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions); }
        catch { return default; }
    }

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string BuildPlanRoot(string root, string regionId, string planId) => Path.Combine(root, "assets", regionId, "plans", planId);
    private static bool IsUsableImage(string path) => File.Exists(path) && new FileInfo(path).Length > 1024;
    private static string ResolvePurpose(int sceneNumber, string? hint) => !string.IsNullOrWhiteSpace(hint) && !ContainsInternal(hint) ? hint : sceneNumber switch { 1 => "Hook", 2 => "WhatToWatch", 3 => "ViewingGuide", 4 => "Close", _ => "WhatToWatch" };
    private static bool ContainsInternal(string value) => ForbiddenImageTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(value, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
    private static string CleanOverlay(string? value, int max) { var clean = Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim(); return clean.Length <= max ? clean : clean[..Math.Max(0, max - 1)].TrimEnd() + "…"; }
    private static string FirstUseful(IReadOnlyList<string> values, params string[] fallback) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? fallback.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    private static string FormatViewingTime(DateTimeOffset? peakUtc, string? timeZone) => peakUtc.HasValue ? $"around {peakUtc.Value:HH:mm} UTC" : string.Empty;
    private static string FindDirection(string text) => Regex.Match(text ?? string.Empty, "\\b(west(?:ern)?|east(?:ern)?|south(?:ern)?|north(?:ern)?)\\b", RegexOptions.IgnoreCase) is { Success: true } m ? m.Value + " sky" : string.Empty;
    private static IEnumerable<string> ExtractKnownObjects(string text) => new[] { "Venus", "Jupiter", "Mars", "Saturn", "Moon", "Mercury" }.Where(o => text.Contains(o, StringComparison.OrdinalIgnoreCase));
    private static bool HasLikelyLocalAsset(string objectName) => ExtractKnownObjects(objectName).Any();
    private static IReadOnlyList<string> ParseStringArray(string? json) { if (string.IsNullOrWhiteSpace(json)) return []; try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? []; } catch { return []; } }
    private static string? FindJsonString(string? json, IReadOnlyList<string> names) { if (string.IsNullOrWhiteSpace(json)) return null; try { using var doc = JsonDocument.Parse(json); return FindString(doc.RootElement, names); } catch { return null; } }
    private static string? FindString(JsonElement element, IReadOnlyList<string> names) { if (element.ValueKind == JsonValueKind.Object) { var direct = FindDirectString(element, names); if (!string.IsNullOrWhiteSpace(direct)) return direct; foreach (var p in element.EnumerateObject()) { var found = FindString(p.Value, names); if (!string.IsNullOrWhiteSpace(found)) return found; } } else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) { var found = FindString(item, names); if (!string.IsNullOrWhiteSpace(found)) return found; } return null; }
    private static string? FindDirectString(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (names.Any(n => property.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        return null;
    }

    private static IReadOnlyList<string> FindStringArray(JsonElement element, IReadOnlyList<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Any(n => property.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                if (property.Value.ValueKind == JsonValueKind.Array)
                    return property.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                if (property.Value.ValueKind == JsonValueKind.String) return [property.Value.GetString()!];
            }
            foreach (var property in element.EnumerateObject())
            {
                var found = FindStringArray(property.Value, names);
                if (found.Count > 0) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindStringArray(item, names);
                if (found.Count > 0) return found;
            }
        }
        return [];
    }

    private static int? FindInt(JsonElement element, IReadOnlyList<string> names) { if (element.ValueKind != JsonValueKind.Object) return null; foreach (var p in element.EnumerateObject()) { if (!names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) continue; if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var number)) return number; if (p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number; } return null; }
    private static Font ResolveFont(float size, FontStyle style = FontStyle.Regular) { var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase)); if (string.IsNullOrWhiteSpace(family.Name)) family = SystemFonts.Collection.Families.FirstOrDefault(); return family.CreateFont(size, style); }
    private static void DrawVignette(IImageProcessingContext ctx) => ctx.Draw(Color.Black.WithAlpha(0.36f), 76, new RectangleF(-32, -32, 1984, 1144));

    private sealed record EventContext(string EventType, string EventTitle, IReadOnlyList<string> Objects, string Location, string Direction, string BestViewingTime, string Notes);
    private sealed record PlanSidecarContext(IReadOnlyList<string> Objects, string Direction, string BestViewingTime, string VisibilityNotes, string SearchText) { public static PlanSidecarContext Empty { get; } = new([], string.Empty, string.Empty, string.Empty, string.Empty); }
    private sealed record SceneHint(int SceneNumber, string Title, string Narration, string Purpose) { public static SceneHint Empty(int sceneNumber) => new(sceneNumber, string.Empty, string.Empty, string.Empty); }
    private sealed record LocalObjectAsset(string ObjectName, Image<Rgba32> Image);
}
