using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class CategoryRequirementResolver : ICategoryRequirementResolver
{
    private static readonly IReadOnlyDictionary<string, CategoryPipelineRequirement> Matrix =
        new Dictionary<string, CategoryPipelineRequirement>(StringComparer.OrdinalIgnoreCase)
        {
            ["DailySkyGuide"] = Build("DailySkyGuide", true, true, true, false, false, false, "Stellarium", ["location", "timezone", "targetDate", "sunset", "sunrise", "moonPhase", "visibleObjects", "bestViewingWindow"]),
            ["WeeklySkyForecast"] = Build("WeeklySkyForecast", true, true, true, false, false, false, "Stellarium", ["7DayMoonPhases", "7DayVisiblePlanets", "weeklyBestViewingWindows", "upcomingEvents"]),
            ["RareEventAlert"] = Build("RareEventAlert", true, true, true, true, false, false, "Hybrid", ["eventType", "eventTime", "visibilityWindow", "location", "rarityScore", "bestViewingDirection"]),
            ["CosmicStoryShort"] = Build("CosmicStoryShort", false, false, false, true, true, false, "AiGeneratedVisuals", ["objectFacts", "storyAngle", "visualMood"]),
            ["MythologySkyStory"] = Build("MythologySkyStory", false, false, false, true, false, false, "AiGeneratedMythologyVisuals", ["mythologySummary", "constellationStory", "culturalContext"]),
            ["AstroPhotographyGuide"] = Build("AstroPhotographyGuide", true, true, true, false, false, true, "StellariumAndDiagrams", ["targetObject", "altitude", "moonIllumination", "bestCaptureWindow", "cameraTips"]),
            ["MonthlySkyReport"] = Build("MonthlySkyReport", true, true, true, false, false, true, "StellariumAndCalendar", ["monthlyMoonPhases", "keyEvents", "visiblePlanets", "bestObservationDates"]),
            ["AstronomyEducation"] = Build("AstronomyEducation", false, false, false, true, false, true, "EducationalDiagrams", ["educationalTopic", "explanationLevel", "diagrams"])
        };

    public Task<CategoryPipelineRequirement> ResolveAsync(string contentCategoryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentCategoryCode))
        {
            return Task.FromResult(BuildUnknown("Unknown"));
        }

        if (Matrix.TryGetValue(contentCategoryCode, out var requirement))
        {
            return Task.FromResult(requirement);
        }

        return Task.FromResult(BuildUnknown(contentCategoryCode));
    }

    private static CategoryPipelineRequirement Build(
        string code,
        bool requiresSkyfield,
        bool requiresStellarium,
        bool requiresSscScript,
        bool requiresAiImages,
        bool requiresNasaImages,
        bool requiresEducationalDiagrams,
        string primaryVisualSource,
        IReadOnlyList<string> requiredDataPoints) =>
        new(
            code,
            requiresSkyfield,
            requiresStellarium,
            requiresSscScript,
            requiresAiImages,
            requiresNasaImages,
            requiresEducationalDiagrams,
            RequiresVoiceNarration: true,
            RequiresThumbnail: true,
            primaryVisualSource,
            requiredDataPoints,
            []);

    private static CategoryPipelineRequirement BuildUnknown(string code) =>
        new(
            code,
            RequiresSkyfield: false,
            RequiresStellarium: false,
            RequiresSscScript: false,
            RequiresAiImages: false,
            RequiresNasaImages: false,
            RequiresEducationalDiagrams: false,
            RequiresVoiceNarration: true,
            RequiresThumbnail: true,
            PrimaryVisualSource: "Unknown",
            RequiredDataPoints: [],
            Warnings: [$"No requirement matrix entry found for content category '{code}'."]);
}

public sealed class VisualStrategyResolver(ICategoryRequirementResolver categoryRequirementResolver) : IVisualStrategyResolver
{
    public async Task<VisualStrategyPlan> ResolveAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        var requirement = await categoryRequirementResolver.ResolveAsync(plan.ContentCategoryCode, cancellationToken);
        var assets = new List<string>();

        if (requirement.RequiresStellarium)
        {
            assets.Add("StellariumSceneImages");
        }

        if (requirement.RequiresAiImages)
        {
            assets.Add("CinematicAiImages");
        }

        if (requirement.RequiresEducationalDiagrams)
        {
            assets.Add("EducationalDiagrams");
        }

        if (requirement.RequiresNasaImages)
        {
            assets.Add("NasaReferenceImages");
        }

        if (requirement.RequiresThumbnail)
        {
            assets.Add("ThumbnailCandidate");
        }

        return new VisualStrategyPlan(
            requirement.ContentCategoryCode,
            requirement.PrimaryVisualSource,
            requirement.RequiresStellarium,
            requirement.RequiresSscScript,
            requirement.RequiresAiImages,
            requirement.RequiresNasaImages,
            requirement.RequiresEducationalDiagrams,
            assets,
            requirement.Warnings);
    }
}
