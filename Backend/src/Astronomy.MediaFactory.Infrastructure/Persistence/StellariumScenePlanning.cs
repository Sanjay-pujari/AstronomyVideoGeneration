using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideStellariumScenePlanner : IStellariumScenePlanner
{
    public Task<StellariumSceneCapturePlan> BuildScenePlanAsync(ContentGenerationPlan plan, AstronomyVisibilityResult visibilityResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(visibilityResult);

        var warnings = new List<string>();
        if (!string.Equals(plan.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("DailySkyGuide planner invoked for non-DailySkyGuide category.");
        }

        var start = visibilityResult.BestViewingStartUtc;
        var end = visibilityResult.BestViewingEndUtc;
        var middle = start.Add((end - start) / 2);
        var nearEnd = end.AddMinutes(-20);
        if (nearEnd < start) nearEnd = end;

        var primary = visibilityResult.VisibleObjects.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode) &&
            string.Equals(x.ObjectCode, plan.PrimaryCelestialObjectCode, StringComparison.OrdinalIgnoreCase))
            ?? visibilityResult.VisibleObjects.FirstOrDefault(x => x.Visible)
            ?? visibilityResult.VisibleObjects.FirstOrDefault();

        var moonVisible = visibilityResult.VisibleObjects.Any(x => x.Visible && string.Equals(x.ObjectCode, "Moon", StringComparison.OrdinalIgnoreCase));
        var scenes = new List<StellariumSceneCaptureItem>
        {
            new($"{plan.ContentCategoryCode}_IntroWideSky", "WideSky", $"Evening sky over {visibilityResult.LocationName}", null, null, start, "Wide", 90,
                true, true, true, false, false, "IntroBackground", 1, null)
        };

        var sceneOrder = 2;
        if (moonVisible)
        {
            scenes.Add(new($"{plan.ContentCategoryCode}_MoonFocus", "MoonFocus", $"Moon focus for {visibilityResult.LocationName}", "Moon", "Moon", middle, "Close", 20,
                true, false, true, false, false, "ThumbnailCandidate", sceneOrder++, new Dictionary<string, string>{{"Reason","Moon visible"}}));
        }

        if (primary is not null && !string.Equals(primary.ObjectCode, "Moon", StringComparison.OrdinalIgnoreCase))
        {
            scenes.Add(new($"{plan.ContentCategoryCode}_PrimaryObject", "ObjectFocus", $"Focus on {primary.ObjectName}", primary.ObjectCode, primary.ObjectName, middle, "AutoObjectTracking", 35,
                true, true, true, false, false, "MainObjectVisual", sceneOrder++, new Dictionary<string, string>{{"PrimaryObject","true"}}));
        }

        var secondary = visibilityResult.VisibleObjects.FirstOrDefault(x => x.Visible && !string.Equals(x.ObjectCode, primary?.ObjectCode, StringComparison.OrdinalIgnoreCase));
        scenes.Add(new($"{plan.ContentCategoryCode}_BestVisibleObjects", "PlanetFocus", "Best visible planets and objects", secondary?.ObjectCode, secondary?.ObjectName, nearEnd, "Medium", 55,
            true, true, true, true, false, "SupportingSkyMap", sceneOrder++, null));

        scenes.Add(new($"{plan.ContentCategoryCode}_OutroWideSky", "WideSky", $"Late evening sky over {visibilityResult.LocationName}", null, null, end, "Wide", 100,
            true, false, true, false, false, "OutroBackground", sceneOrder, null));

        if (scenes.Count < 3)
        {
            warnings.Add("Scene planning fallback applied to ensure minimum scene count.");
        }

        return Task.FromResult(new StellariumSceneCapturePlan(
            plan.Id,
            plan.ContentCategoryCode,
            visibilityResult.RegionId,
            visibilityResult.LocationName,
            visibilityResult.Latitude,
            visibilityResult.Longitude,
            visibilityResult.Timezone,
            visibilityResult.TargetDate,
            scenes.OrderBy(x => x.SortOrder).Take(5).ToList(),
            warnings));
    }
}

public sealed class StellariumScenePlannerResolver(IEnumerable<IStellariumScenePlanner> planners) : IStellariumScenePlannerResolver
{
    public IStellariumScenePlanner? Resolve(string contentCategoryCode)
    {
        if (string.Equals(contentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
        {
            return planners.FirstOrDefault(x => x is DailySkyGuideStellariumScenePlanner);
        }

        return null;
    }
}
