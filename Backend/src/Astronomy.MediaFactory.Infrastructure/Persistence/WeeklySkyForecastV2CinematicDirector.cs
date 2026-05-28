using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SceneMoodClassifier { public string Classify(ResolvedWeeklyScene s) => s.SceneCode.Contains("moon", StringComparison.OrdinalIgnoreCase) ? "contemplative" : s.SceneCode.Contains("wide", StringComparison.OrdinalIgnoreCase) ? "expansive" : "balanced"; }
public sealed class VisualWeightCalculator { public (double SkyWeight, double HorizonWeight) Calculate(ResolvedWeeklyScene s) => s.SceneCode.Contains("wide", StringComparison.OrdinalIgnoreCase) ? (0.65, 0.35) : (0.8, 0.2); }
public sealed class CinematicStyleEngine { public string Resolve(ResolvedWeeklyScene s) => s.SceneCode.Contains("moon", StringComparison.OrdinalIgnoreCase) ? "intimate-lunar-hero" : s.SceneCode.Contains("hero_western_grouping", StringComparison.OrdinalIgnoreCase) ? "western-planetary-grouping" : "epic-night-orientation"; }

public sealed class CinematicDirectionPersister
{
    public async Task<string> PersistAsync(string root, CinematicDirectorResponse response, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(root, "cinematic");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cinematic-directions.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return path;
    }
}

public sealed record CinematicDirectorRequest(IReadOnlyList<ResolvedWeeklyScene> Scenes);

public sealed class WeeklyCinematicDirectorService(ILogger<WeeklyCinematicDirectorService> logger)
{
    public async Task<CinematicDirectorResponse> BuildAsync(CinematicDirectorRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("AI_CINEMATIC_DIRECTOR_START");
        var classifier = new SceneMoodClassifier();
        var weights = new VisualWeightCalculator();
        var styles = new CinematicStyleEngine();

        var directions = new List<CinematicSceneDirection>();
        foreach (var scene in request.Scenes)
        {
            var lower = scene.SceneCode.ToLowerInvariant();
            var isMoon = lower.Contains("moon");
            var isGrouping = lower.Contains("hero_western_grouping") || (scene.TargetObjects?.Count ?? 0) >= 2;

            var style = styles.Resolve(scene);
            var mood = classifier.Classify(scene);
            var (skyW, horizonW) = weights.Calculate(scene);

            var direction = isMoon
                ? new CinematicSceneDirection(scene.SceneCode, scene.SceneCode, "intimate-lunar-hero", "wonder", mood, 100, "MOON", scene.TargetObjects.Where(x => !x.Equals("MOON", StringComparison.OrdinalIgnoreCase)).ToList(), true, false, true, skyW, 0, "upper-middle-rule-of-thirds", 1.15, "upper-middle", "slow-drift", ["keep moon readable", "avoid clipping moon limb"], [])
                : isGrouping
                    ? new CinematicSceneDirection(scene.SceneCode, scene.SceneCode, "western-planetary-grouping", "awe", mood, 90, scene.TargetObjects.FirstOrDefault() ?? "SKY", scene.TargetObjects.Skip(1).ToList(), true, true, true, skyW, horizonW, "balanced-grouping", 1.25, "lower-middle", "gentle-pan", ["preserve grouping spacing", "keep horizon stable"], [])
                    : new CinematicSceneDirection(scene.SceneCode, scene.SceneCode, "epic-night-orientation", "scale", mood, 80, scene.TargetObjects.FirstOrDefault() ?? "SKY", scene.TargetObjects.Skip(1).ToList(), true, true, true, skyW, horizonW, "wide-context", 1.4, "horizon-lower-third", "slow-panorama", ["prioritize context", "retain constellation map"], []);

            directions.Add(direction);
            logger.LogInformation("AI_CINEMATIC_DIRECTION {SceneCode} {Style}", scene.SceneCode, direction.CinematicStyle);
        }

        var response = new CinematicDirectorResponse(directions);
        logger.LogInformation("AI_CINEMATIC_DIRECTOR_COMPLETE");
        await Task.CompletedTask;
        return response;
    }
}
