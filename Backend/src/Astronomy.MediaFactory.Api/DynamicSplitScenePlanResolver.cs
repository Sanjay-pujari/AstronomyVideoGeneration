using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Api;

public sealed record GeneratedSplitSceneMetadata(
    string SceneCode,
    string SourceSceneCode,
    IReadOnlyList<string> TargetObjects,
    string? PrimaryObject,
    string SceneType,
    string RenderIntent,
    int DurationSeconds,
    DateOnly TargetDate,
    DateTime? SelectedObservationUtc,
    string ExpectedSscScriptPath,
    string ExpectedOutputImagePath);

public static class DynamicSplitScenePlanResolver
{
    public static WeeklyScenePlan? Resolve(
        string sceneCode,
        IReadOnlyDictionary<string, WeeklyScenePlan> scenePlansByCode,
        IReadOnlyDictionary<string, GeneratedSplitSceneMetadata> splitMetadataBySceneCode,
        out string? sourceSceneCode,
        out string metadataSource)
    {
        sourceSceneCode = null;
        if (scenePlansByCode.TryGetValue(sceneCode, out var scenePlan))
        {
            metadataSource = "scene-plan";
            return scenePlan;
        }

        if (splitMetadataBySceneCode.TryGetValue(sceneCode, out var splitMetadata))
        {
            sourceSceneCode = splitMetadata.SourceSceneCode;
            if (scenePlansByCode.TryGetValue(splitMetadata.SourceSceneCode, out var sourcePlan))
            {
                metadataSource = "source-scene-plan";
                return sourcePlan;
            }

            metadataSource = "generated-split-metadata";
            return new WeeklyScenePlan(
                splitMetadata.SceneCode,
                splitMetadata.SceneCode,
                0,
                splitMetadata.SceneType,
                "Stellarium",
                splitMetadata.RenderIntent,
                splitMetadata.TargetDate,
                splitMetadata.SelectedObservationUtc,
                splitMetadata.TargetObjects,
                splitMetadata.DurationSeconds,
                $"Dynamic split scene derived from {splitMetadata.SourceSceneCode}",
                "static",
                "default",
                [],
                "cut",
                "cut",
                false,
                splitMetadata.RenderIntent,
                [],
                true,
                false,
                false);
        }

        metadataSource = "not-found";
        return null;
    }
}
