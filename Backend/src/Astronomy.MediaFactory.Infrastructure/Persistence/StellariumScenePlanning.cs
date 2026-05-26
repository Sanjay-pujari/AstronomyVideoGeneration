using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideStellariumScenePlanner(IOptions<StellariumOptions> options) : IStellariumScenePlanner
{
    private readonly StellariumOptions _options = options.Value;

    public Task<StellariumSceneCapturePlan> BuildScenePlanAsync(ContentGenerationPlan plan, AstronomyVisibilityResult visibilityResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(visibilityResult);

        var warnings = new List<string>();
        if (!string.Equals(plan.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase)) warnings.Add("DailySkyGuide planner invoked for non-DailySkyGuide category.");

        var mode = _options.DailySkyGuideSceneGenerationMode;
        var objectFocused = BuildObjectFocusedScenes(plan, visibilityResult, warnings);
        var compositionScenes = BuildCompositionScenes(plan, visibilityResult, warnings);

        var scenes = mode switch
        {
            SceneGenerationMode.ObjectFocused => objectFocused,
            SceneGenerationMode.CompositionFocused => compositionScenes,
            _ => compositionScenes.Take(Math.Min(2, _options.MaxCompositionScenes))
                .Concat(objectFocused.Where(x => string.Equals(x.SceneType, "ObjectFocus", StringComparison.OrdinalIgnoreCase) || string.Equals(x.SceneType, "MoonFocus", StringComparison.OrdinalIgnoreCase)).Take(_options.MaxFocusedScenes))
                .Concat(compositionScenes.Skip(2).Take(1))
                .Select((x, i) => x with { SortOrder = i + 1 })
                .ToList()
        };

        return Task.FromResult(new StellariumSceneCapturePlan(
            plan.Id, plan.ContentCategoryCode, visibilityResult.RegionId, visibilityResult.LocationName, visibilityResult.Latitude, visibilityResult.Longitude,
            visibilityResult.Timezone, visibilityResult.TargetDate, scenes.Take(Math.Max(_options.MaxCompositionScenes + _options.MaxFocusedScenes, 1)).ToList(), warnings));
    }

    private List<StellariumSceneCaptureItem> BuildObjectFocusedScenes(ContentGenerationPlan plan, AstronomyVisibilityResult visibilityResult, List<string> warnings)
    {
        var start = visibilityResult.BestViewingStartUtc;
        var end = visibilityResult.BestViewingEndUtc;
        var middle = start.Add((end - start) / 2);
        var nearEnd = end.AddMinutes(-20); if (nearEnd < start) nearEnd = end;

        var primary = visibilityResult.VisibleObjects.FirstOrDefault(x => !string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode) && string.Equals(x.ObjectCode, plan.PrimaryCelestialObjectCode, StringComparison.OrdinalIgnoreCase))
            ?? visibilityResult.VisibleObjects.FirstOrDefault(x => x.Visible) ?? visibilityResult.VisibleObjects.FirstOrDefault();

        var moonVisible = visibilityResult.VisibleObjects.Any(x => x.Visible && string.Equals(x.ObjectCode, "Moon", StringComparison.OrdinalIgnoreCase));
        var scenes = new List<StellariumSceneCaptureItem>
        {
            new($"{plan.ContentCategoryCode}_IntroWideSky", "WideSky", $"Evening sky over {visibilityResult.LocationName}", null, null, start, "Wide", 90, true, true, true, false, false, "IntroBackground", 1, null)
        };

        var sceneOrder = 2;
        if (moonVisible) scenes.Add(new($"{plan.ContentCategoryCode}_MoonFocus", "MoonFocus", $"Moon focus for {visibilityResult.LocationName}", "Moon", "Moon", middle, "Close", 20, true, false, true, false, false, "ThumbnailCandidate", sceneOrder++, new(){{"Reason","Moon visible"}}));
        if (primary is not null && !string.Equals(primary.ObjectCode, "Moon", StringComparison.OrdinalIgnoreCase)) scenes.Add(new($"{plan.ContentCategoryCode}_PrimaryObject", "ObjectFocus", $"Focus on {primary.ObjectName}", primary.ObjectCode, primary.ObjectName, middle, "AutoObjectTracking", 35, true, true, true, false, false, "MainObjectVisual", sceneOrder++, new(){{"PrimaryObject","true"}}));

        var secondary = visibilityResult.VisibleObjects.FirstOrDefault(x => x.Visible && !string.Equals(x.ObjectCode, primary?.ObjectCode, StringComparison.OrdinalIgnoreCase));
        scenes.Add(new($"{plan.ContentCategoryCode}_BestVisibleObjects", "PlanetFocus", "Best visible planets and objects", secondary?.ObjectCode, secondary?.ObjectName, nearEnd, "Medium", 55, true, true, true, true, false, "SupportingSkyMap", sceneOrder++, null));
        scenes.Add(new($"{plan.ContentCategoryCode}_OutroWideSky", "WideSky", $"Late evening sky over {visibilityResult.LocationName}", null, null, end, "Wide", 100, true, false, true, false, false, "OutroBackground", sceneOrder, null));

        if (scenes.Count < 3) warnings.Add("Scene planning fallback applied to ensure minimum scene count.");
        return scenes.OrderBy(x => x.SortOrder).ToList();
    }

    private List<StellariumSceneCaptureItem> BuildCompositionScenes(ContentGenerationPlan plan, AstronomyVisibilityResult visibilityResult, List<string> warnings)
    {
        var visible = visibilityResult.VisibleObjects.Where(x => x.Visible).ToList();
        var groups = visible.GroupBy(x => NormalizeDirection(x.ViewingDirection)).Take(_options.MaxCompositionScenes).ToList();
        var list = new List<StellariumSceneCaptureItem>();
        var order = 1;
        foreach (var g in groups)
        {
            var objects = g.Select(x => x.ObjectName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fov = Math.Clamp(35 + (objects.Length * 8), 40, 110);
            var sample = g.OrderByDescending(x => x.AltitudeDegrees ?? 0).First();
            var code = $"{plan.ContentCategoryCode}_Composition_{NormalizeDirection(g.Key)}_{order:00}";
            list.Add(new(code, "Composition", $"{g.Key} sky composition", sample.ObjectCode, sample.ObjectName, visibilityResult.BestViewingStartUtc.AddMinutes(order * 10), "WideComposition", fov,
                true, true, true, false, false, order == 1 ? "ThumbnailCandidate" : "SupportingSkyMap", order, new()
                {
                    ["SceneGenerationMode"] = SceneGenerationMode.CompositionFocused.ToString(),
                    ["CompositionType"] = order == 1 ? "EveningSkyComposition" : "WesternSkyOverview",
                    ["IncludedObjects"] = string.Join(",", objects),
                    ["HighlightStrategy"] = "GroupLabelsStable"
                }));
            order++;
        }

        if (list.Count == 0)
        {
            warnings.Add("No visible objects available for composition grouping; using wide fallback scene.");
            list.Add(new($"{plan.ContentCategoryCode}_Composition_Fallback", "Composition", "WideSkyClosingScene", null, null, visibilityResult.BestViewingStartUtc, "WideComposition", 95, true, true, true, false, false, "ThumbnailCandidate", 1, new() { ["IncludedObjects"] = string.Empty }));
        }

        var report = new
        {
            sceneGenerationMode = _options.DailySkyGuideSceneGenerationMode.ToString(),
            groupedObjectCount = visible.Count,
            generatedCompositionScenes = list.Count,
            sceneReduction = new { fromEstimatedObjectScenes = Math.Max(visible.Count, 1), toCompositionScenes = list.Count }
        };
        var reportPath = Path.Combine("outputs", "content-plans", plan.Id.ToString(), "composition-scene-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report));
        foreach (var item in list) item.Metadata?["MetadataPath"] = reportPath;
        return list;
    }

    private static string NormalizeDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return "west";
        var normalized = direction.Trim().ToLowerInvariant();
        if (normalized.Contains("west")) return "west";
        if (normalized.Contains("east")) return "east";
        if (normalized.Contains("south")) return "south";
        if (normalized.Contains("north")) return "north";
        return normalized;
    }
}

public sealed class StellariumScenePlannerResolver(IEnumerable<IStellariumScenePlanner> planners) : IStellariumScenePlannerResolver
{
    public IStellariumScenePlanner? Resolve(string contentCategoryCode)
    {
        if (string.Equals(contentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase)) return planners.FirstOrDefault(x => x is DailySkyGuideStellariumScenePlanner);
        return null;
    }
}
