using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class CategoryRequirementResolver : ICategoryRequirementResolver
{
    private static readonly IReadOnlyDictionary<string, CategoryPipelineRequirement> Matrix =
        new Dictionary<string, CategoryPipelineRequirement>(StringComparer.OrdinalIgnoreCase)
        {
            ["DailySkyGuide"] = Build(
                "DailySkyGuide", true, true, true, false, false, false, true, true,
                "Skyfield + CelestialObjectMaster", "Stellarium", "AzureSpeech", "MoonDominantOrMultiObjectCollage",
                ["location", "timezone", "targetDate", "sunset", "sunrise", "moonPhase", "moonIllumination", "visibleObjects", "bestViewingWindow", "objectRiseSetTransit"],
                ["StellariumSceneImages", "ThumbnailCandidate"]),
            ["WeeklySkyForecast"] = Build(
                "WeeklySkyForecast", true, true, true, false, false, true, true, true,
                "Skyfield Weekly Visibility", "Stellarium + WeeklySkyDiagram", "AzureSpeech", "CinematicWeeklySkyCollage",
                ["location", "timezone", "weekStartDate", "sevenDayMoonPhases", "sevenDayMoonIllumination", "sevenDayVisiblePlanets", "weeklyBestViewingWindows", "upcomingAstronomyEvents"],
                ["StellariumSceneImages", "WeeklySkyCalendarDiagram", "ThumbnailCandidate"]),
            ["RareEventAlert"] = Build(
                "RareEventAlert", true, true, true, true, false, true, true, true,
                "Skyfield + AstronomyEventTypeMaster + EventCatalog", "HybridStellariumAndAiVisuals", "AzureSpeech", "EventAlert",
                ["eventType", "eventTime", "eventVisibilityWindow", "location", "rarityScore", "bestViewingDirection", "affectedObjects", "safetyInstructionsIfRequired"],
                ["StellariumEventScene", "EventDiagram", "AiEventVisual", "ThumbnailCandidate"]),
            ["CosmicStoryShort"] = Build(
                "CosmicStoryShort", false, false, false, true, true, false, true, true,
                "CelestialObjectMaster + AI Story Expansion", "AiGeneratedCinematicVisuals", "AzureSpeech", "PlanetCloseupOrCinematic",
                ["objectFacts", "funFact", "scientificName", "objectType", "storyAngle", "visualMood"],
                ["AiCinematicImages", "OptionalNasaReferenceImage", "ThumbnailCandidate"]),
            ["MythologySkyStory"] = Build(
                "MythologySkyStory", false, false, false, true, false, false, true, true,
                "CelestialObjectMaster.MythologySummary + AI Story Expansion", "AiGeneratedMythologyVisuals", "AzureSpeech", "MythologyVisual",
                ["mythologySummary", "constellationStory", "culturalContext", "objectFacts", "moralOrMysteryAngle"],
                ["AiMythologyImages", "StorySceneImages", "ThumbnailCandidate"]),
            ["AstroPhotographyGuide"] = Build(
                "AstroPhotographyGuide", true, true, true, false, false, true, true, true,
                "Skyfield + CelestialObjectMaster + PhotographyRules", "Stellarium + PhotographyDiagrams", "AzureSpeech", "MinimalOrInstructional",
                ["targetObject", "altitude", "azimuth", "bestCaptureWindow", "moonIllumination", "weatherPlaceholder", "cameraTips", "lensRecommendation"],
                ["StellariumTargetFraming", "PhotographyPlanningDiagram", "ThumbnailCandidate"]),
            ["MonthlySkyReport"] = Build(
                "MonthlySkyReport", true, true, true, false, false, true, true, true,
                "Skyfield Monthly Visibility + EventCatalog", "Stellarium + MonthlyCalendarDiagram", "AzureSpeech", "CinematicMonthlySkyCollage",
                ["location", "timezone", "month", "monthlyMoonPhases", "keyAstronomyEvents", "visiblePlanets", "bestObservationDates", "meteorShowers"],
                ["StellariumMonthlyScenes", "MonthlySkyCalendarDiagram", "ThumbnailCandidate"]),
            ["AstronomyEducation"] = Build(
                "AstronomyEducation", false, false, false, true, true, true, true, true,
                "CelestialObjectMaster + AI Educational Explanation", "EducationalDiagrams + AiGeneratedVisuals", "AzureSpeech", "MinimalEducational",
                ["educationalTopic", "explanationLevel", "examples", "comparisonPoints", "diagrams"],
                ["EducationalDiagram", "AiExplainerImages", "OptionalNasaReferenceImage", "ThumbnailCandidate"])
        };

    public Task<CategoryPipelineRequirement> ResolveAsync(string contentCategoryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentCategoryCode))
            throw new ArgumentException("Content category code is required.", nameof(contentCategoryCode));

        return Task.FromResult(Matrix.TryGetValue(contentCategoryCode, out var requirement)
            ? requirement
            : BuildUnknown(contentCategoryCode));
    }

    private static CategoryPipelineRequirement Build(string code, bool requiresSkyfield, bool requiresStellarium, bool requiresSscScript,
        bool requiresAiImages, bool requiresNasaImages, bool requiresEducationalDiagrams, bool requiresVoiceNarration, bool requiresThumbnail,
        string primaryInformationSource, string primaryVisualSource, string narrationSource, string thumbnailStrategy,
        IReadOnlyList<string> requiredDataPoints, IReadOnlyList<string> visualAssetTypes) =>
        new(code, requiresSkyfield, requiresStellarium, requiresSscScript, requiresAiImages, requiresNasaImages,
            requiresEducationalDiagrams, requiresVoiceNarration, requiresThumbnail, primaryInformationSource,
            primaryVisualSource, narrationSource, thumbnailStrategy, requiredDataPoints, visualAssetTypes, []);

    private static CategoryPipelineRequirement BuildUnknown(string code) =>
        new(code, false, false, false, false, false, false, false, false, "Unknown", "Unknown", "Unknown", "Unknown", [], [],
            ["No category requirement definition found for this content category."]);
}

public sealed class VisualStrategyResolver(ICategoryRequirementResolver categoryRequirementResolver) : IVisualStrategyResolver
{
    public async Task<VisualStrategyPlan> ResolveAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.ContentCategoryCode))
            throw new ArgumentException("Content category code is required for visual strategy preview.", nameof(plan));

        var requirement = await categoryRequirementResolver.ResolveAsync(plan.ContentCategoryCode, cancellationToken);
        return new VisualStrategyPlan(
            plan.Id,
            requirement.ContentCategoryCode,
            requirement.PrimaryVisualSource,
            requirement.RequiresStellarium,
            requirement.RequiresSscScript,
            requirement.RequiresAiImages,
            requirement.RequiresNasaImages,
            requirement.RequiresEducationalDiagrams,
            requirement.RequiresVoiceNarration,
            requirement.RequiresThumbnail,
            requirement.VisualAssetTypes,
            requirement.RequiredDataPoints,
            requirement.Warnings);
    }
}
