using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2IntelligenceTests
{
    [Fact]
    public async Task V2_Intelligence_Generates_Cinematic_Blueprint()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"weekly-preview-{Guid.NewGuid():N}");
        var service = new WeeklySkyForecastV2IntelligenceService(new StubContextBuilder(), new WeeklySkyForecastV2EventIntelligenceBuilder(), new WeeklySkyForecastV2EditorialIntelligenceBuilder(), new WeeklySkyForecastV2CinematicEditorialRefiner(), new WeeklySkyForecastV2NarrativeAbstractionBuilder(), new WeeklySkyForecastV2NarrationPlanner(), new WeeklySkyForecastV2NarrationTextGenerator(), new WeeklySkyForecastV2AssetResolver(), new WeeklySkyForecastV2EditorialNormalizer(), Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }));
        var response = await service.PreviewAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), Diagnostics: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.CinematicStoryBlueprint);
        Assert.NotNull(response.NarrativeAbstractionPackage);
        Assert.NotNull(response.NarrationPlan);
        Assert.NotNull(response.GeneratedNarrationPackage);
        Assert.NotNull(response.NarrationQuality);
        Assert.NotNull(response.VisualRequirementPackage);
        Assert.NotNull(response.HybridScenePlanPackage);
        Assert.NotNull(response.NormalizedEditorialPackage);
        Assert.NotNull(response.SceneChoreographyPackage);
        Assert.NotNull(response.CinematicChoreographyPackage);
        Assert.NotNull(response.RenderExecutionPackage);
        Assert.NotNull(response.RenderPreparationPackage);
        Assert.NotNull(response.ExecutionValidation);
        Assert.True(response.ExecutionValidation!.OverlaysValidated);
        Assert.True(response.ExecutionValidation.TransitionsValidated);
        Assert.True(response.ExecutionValidation.TimelineValidated);
        Assert.True(response.ExecutionValidation.RendererContractsValidated);
        Assert.True(response.ExecutionValidation.ThumbnailContractsValidated);
        Assert.Equal(100d, response.ExecutionValidation.NarrationTimelineCoveragePercent);
        Assert.Empty(response.ExecutionValidation.MissingExecutionFields);
        Assert.True(response.LegacyEditorialPackageDeprecated);
        Assert.NotNull(response.PreviewStability);
        Assert.Empty(response.PreviewStability!.BlockingIssues);
        Assert.Empty(response.PreviewStability.AffectedFieldPaths);
        Assert.NotNull(response.EditorialStoryPackage);
        Assert.DoesNotContain("Same viewing window grouping", response.CinematicStoryBlueprint!.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Moon", response.CinematicStoryBlueprint.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.CinematicStoryBlueprint.NarrativeBeats.Count >= 6);
        Assert.Equal(3, response.CinematicStoryBlueprint.ShortsBlueprints.Count);
        Assert.Equal(7, response.NarrativeAbstractionPackage!.NarrativeFlow.Count);
        Assert.Equal(3, response.NarrativeAbstractionPackage.ShortsNarrativePlan.Count);
        Assert.Equal(3, response.NarrationPlan!.ShortsPlan.Shorts.Count);
        Assert.Equal(3, response.GeneratedNarrationPackage!.ShortNarrations.Count);
        Assert.DoesNotContain("same viewing window", response.GeneratedNarrationPackage.LongFormNarration.FullNarration, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.NarrationQuality!.ShortCtaUniquenessValid);
        Assert.True(response.NarrationQuality.IsValid);
        Assert.Empty(response.NarrationQuality.ForbiddenPhraseHits);
        Assert.InRange(response.GeneratedNarrationPackage.LongFormNarration.EstimatedDurationSeconds, 85, 125);
        Assert.InRange(response.VisualRequirementPackage!.VisualRequirements.Count, 4, 6);
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "Hybrid");
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "Stellarium");
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "CelestialAsset");
        Assert.Equal(response.NarrationPlan.LongFormPlan.Segments.Count, response.VisualRequirementPackage.SegmentVisualMappings.Count);
        Assert.All(response.NarrationPlan.LongFormPlan.Segments, seg => Assert.Contains(response.VisualRequirementPackage.SegmentVisualMappings, m => m.SegmentCode == seg.SegmentCode));
        Assert.InRange(response.HybridScenePlanPackage!.ScenePlans.Count, 4, 6);
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "Hybrid");
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "Stellarium");
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "CelestialAsset");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "MOON");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "JUPITER");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "VENUS");
        Assert.All(response.VisualRequirementPackage.SegmentVisualMappings, map => Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualCode == map.VisualCode));
        Assert.True(response.PreviewStability!.IsStable);
        Assert.Equal("Venus, Jupiter and the Moon share the evening sky", response.CinematicStoryBlueprint.HeroStory.Title);
        Assert.Equal(new DateOnly(2026, 5, 25), response.CinematicStoryBlueprint.HeroStory.PeakDate);
        Assert.Equal([new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 26)], response.CinematicStoryBlueprint.HeroStory.SupportingDates);
        Assert.Equal(4, response.CinematicStoryBlueprint.SupportingStories.Count);
        Assert.Contains(response.CinematicStoryBlueprint.SupportingStories, s => s.Title == "Best skywatching night: May 25");
        Assert.Contains(response.CinematicStoryBlueprint.SupportingStories, s => s.Title == "Jupiter’s strongest planet presence");
        Assert.Contains(response.CinematicStoryBlueprint.SupportingStories, s => s.Title == "The Moon’s calm visual highlight");
        Assert.Contains(response.CinematicStoryBlueprint.SupportingStories, s => s.Title == "Simple viewing and photography tip");
        Assert.DoesNotContain(response.CinematicStoryBlueprint.SupportingStories, s => s.Title.Contains("continuous evening sky story", StringComparison.OrdinalIgnoreCase));
        Assert.All(response.CinematicStoryBlueprint.CinematicMoments.Where(m => m.TargetDate == new DateOnly(2026, 5, 25) && m.BestTimeUtc.HasValue), m => Assert.Equal(new DateOnly(2026, 5, 25), DateOnly.FromDateTime(m.BestTimeUtc!.Value)));
        Assert.NotEmpty(response.RenderExecutionPackage.OverlayExecutionDirectives);
        Assert.All(response.RenderExecutionPackage.OverlayExecutionDirectives, o => Assert.False(string.IsNullOrWhiteSpace(o.OverlayText)));
        Assert.NotEmpty(response.RenderExecutionPackage.TransitionExecutionDirectives);
        Assert.All(response.RenderExecutionPackage.TransitionExecutionDirectives, t => Assert.True(t.DurationSeconds > 0));
        Assert.NotEmpty(response.RenderExecutionPackage.MotionExecutionDirectives);
        Assert.Equal("ThumbnailCompositor", response.RenderExecutionPackage.ThumbnailExecutionContract.RendererType);
        Assert.True(response.PreviewStability.ReadyForAssetResolution);
        Assert.True(response.PreviewStability.ReadyForRenderPreparation);
        Assert.False(response.PreviewStability.ReadyForRendering);
        Assert.True(response.RenderExecutionPackage!.ExecutionScenes.Count is >= 4 and <= 6);
        Assert.All(response.RenderExecutionPackage.ExecutionScenes, s => Assert.Contains(response.RenderExecutionPackage.RenderSourceDecisions, d => d.SceneCode == s.SceneCode));
        Assert.NotNull(response.RenderExecutionPackage.ThumbnailExecutionContract);
        Assert.Equal("WeeklySkyForecastThumbnail", response.RenderExecutionPackage.ThumbnailExecutionContract.OutputRole);
        Assert.Contains(response.RenderExecutionPackage.StellariumExecutionDirectives, d => d.SceneCode == "best_night_wide_scene" && d.Required);
        Assert.All(response.RenderPreparationPackage!.SceneRenderRequests, r => Assert.True(r.RendererDecisionLocked));
        Assert.Contains(response.RenderPreparationPackage.SceneRenderRequests, r => r.SceneCode == "moon_jupiter_hero_scene");
        Assert.DoesNotContain(response.RenderPreparationPackage.StellariumRenderPlan.Jobs, j => j.SceneCode == "moon_jupiter_hero_scene");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "moon_hero_image");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "jupiter_hero_image");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "venus_glow_point");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "twilight_starfield_bg");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "tripod_phone_overlay");
        Assert.Contains(response.RenderPreparationPackage.AssetResolutionPlan.Items, a => a.AssetCode == "thumbnail_overlay_assets");
        Assert.Equal(6, response.RenderPreparationPackage.SceneRenderRequests.Count);
        var thumbnailRequest = Assert.Single(response.RenderPreparationPackage.SceneRenderRequests.Where(r => r.SceneCode == "thumbnail_story_scene"));
        Assert.True(thumbnailRequest.IsThumbnailOnly);
        var closingReuseRequest = Assert.Single(response.RenderPreparationPackage.SceneRenderRequests.Where(r => r.SceneCode == "best_night_wide_closing_reuse"));
        Assert.NotEqual(closingReuseRequest.RequestId, response.RenderPreparationPackage.SceneRenderRequests.First(r => r.SceneCode == "best_night_wide_scene").RequestId);
        Assert.All(response.RenderPreparationPackage.SceneRenderRequests, r => Assert.False(string.IsNullOrWhiteSpace(r.OutputPath)));
        Assert.All(response.RenderPreparationPackage.SceneRenderRequests, r => Assert.False(string.IsNullOrWhiteSpace(r.MetadataOutputPath)));
        Assert.All(response.RenderPreparationPackage.SceneRenderRequests, r => Assert.False(string.IsNullOrWhiteSpace(r.DebugOutputPath)));
        var directories = response.RenderPreparationPackage.WorkingDirectoryPlan;
        var pathList = new[] { directories.RootPath, directories.SceneRendersPath, directories.OverlaysPath, directories.AudioPath, directories.ThumbnailsPath, directories.TimelinePath, directories.FinalPath, directories.MetadataPath, directories.DebugPath, directories.StellariumPath, directories.AssetsPath };
        Assert.All(pathList, p => Assert.True(Path.IsPathRooted(p)));
        Assert.Equal(pathList.Length, pathList.Distinct(StringComparer.Ordinal).Count());
        Assert.True(response.RenderPreparationPackage.RenderPreparationValidation.IsValid);
        Assert.True(response.RenderPreparationPackage.RenderPreparationValidation.ReadyForSceneRendering);
        Assert.False(response.RenderPreparationPackage.RenderPreparationValidation.ReadyForRendering);
        Assert.True(response.RenderPreparationPackage.RenderPreparationValidation.WorkingDirectoryPlanValid);
        Assert.True(response.RenderPreparationPackage.RenderPreparationFreezeStatus.IsFrozen);
        Assert.True(response.RenderPreparationPackage.RenderPreparationFreezeStatus.IsReadyForPhase6B);
        Assert.True(response.ReadyForRenderPreparation);
        Assert.True(response.ReadyForSceneRendering);
        Assert.False(response.ReadyForRendering);
        Assert.InRange(response.SceneChoreographyPackage!.ResolvedScenes.Count, 4, 6);
        Assert.InRange(response.CinematicChoreographyPackage!.Scenes.Count, 4, 6);
        Assert.All(response.CinematicChoreographyPackage.Scenes, s => Assert.Contains(response.CinematicChoreographyPackage.SceneTimeline, t => t.SceneCode == s.SceneCode));
        Assert.All(response.CinematicChoreographyPackage.Scenes, s => Assert.Contains(response.CinematicChoreographyPackage.CameraTimeline, t => t.SceneCode == s.SceneCode));
        Assert.All(response.CinematicChoreographyPackage.Scenes, s => Assert.Contains(response.CinematicChoreographyPackage.RenderContracts, c => c.SceneCode == s.SceneCode));
        Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.SceneCode == "hero_western_grouping_scene");
        Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.SceneCode == "best_night_wide_scene");
        Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.SceneCode == "moon_jupiter_hero_scene");
        Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.SceneCode == "viewing_tip_wide_scene");
        Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.SceneCode == "thumbnail_story_scene");
        Assert.All(response.NarrationPlan.LongFormPlan.Segments, seg => Assert.Contains(response.CinematicChoreographyPackage.Scenes, s => s.NarrationSegmentCodes.Contains(seg.SegmentCode)));
        var bestNightScene = response.CinematicChoreographyPackage.Scenes.First(s => s.SceneCode == "best_night_wide_scene");
        if (bestNightScene.TechnicalBestTimeUtc is not null)
        {
            Assert.Equal(new DateOnly(2026, 5, 25), DateOnly.FromDateTime(bestNightScene.TechnicalBestTimeUtc.Value));
        }
        Assert.DoesNotContain(response.CinematicStoryBlueprint.OpeningHook, new[] { "Same viewing window grouping", "High-value weekly observation event", "visibility momentum", "backup opportunities", "observation event", "grouping event" }, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(response.NormalizedEditorialPackage!.HeroNormalizedEvent.PeakDate, new DateOnly(2026, 5, 25));
        Assert.Equal("Venus, Jupiter and the Moon share the evening sky", response.NormalizedEditorialPackage.HeroNormalizedEvent.Title);
        Assert.DoesNotContain(response.EventIntelligence.Where(x => x.Source != "grouping_trace_same_window").Select(x => x.Title), t => t.Contains("Evening sky lineup", StringComparison.OrdinalIgnoreCase));
        Assert.All(response.RenderExecutionPackage.ExecutionScenes, s =>
        {
            if (s.TechnicalBestTimeUtc is not null)
                Assert.Equal(s.TargetDate, DateOnly.FromDateTime(s.TechnicalBestTimeUtc.Value));
        });
        Assert.NotNull(response.NarrativeAbstractionPackage.ThumbnailNarrativeDirection);
        Assert.DoesNotContain(response.GeneratedNarrationPackage.ShortNarrations.Select(x => x.NarrationText), t => t.StartsWith("Tonight", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(response.RenderExecutionPackage.ExecutionScenes.Count, response.RenderExecutionPackage.MotionExecutionDirectives.Count);
        Assert.Contains(response.RenderExecutionPackage.OverlayExecutionDirectives, o => o.SceneCode == "hero_western_grouping_scene" && o.OverlayType == "ObjectLabels" && !o.Required);
        Assert.Contains(response.RenderExecutionPackage.OverlayExecutionDirectives, o => o.SceneCode == "best_night_wide_scene" && o.OverlayType == "DirectionArrow");
        Assert.Contains(response.RenderExecutionPackage.OverlayExecutionDirectives, o => o.SceneCode == "best_night_wide_scene" && o.OverlayType == "TimeAnnotation");
        Assert.Contains(response.RenderExecutionPackage.OverlayExecutionDirectives, o => o.SceneCode == "viewing_tip_wide_scene" && o.OverlayType == "FramingGuide");
        Assert.Contains(response.RenderExecutionPackage.OverlayExecutionDirectives, o => o.SceneCode == "thumbnail_story_scene" && o.SafeArea == "mobile-safe");
        Assert.Contains(response.RenderExecutionPackage.TransitionExecutionDirectives, t => t.TransitionType.Contains("intro fade-in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.RenderExecutionPackage.TransitionExecutionDirectives, t => t.FromSceneCode == "hero_western_grouping_scene" && t.ToSceneCode == "best_night_wide_scene");
        Assert.Contains(response.RenderExecutionPackage.TransitionExecutionDirectives, t => t.FromSceneCode == "best_night_wide_scene" && t.ToSceneCode == "moon_jupiter_hero_scene");
        Assert.Contains(response.RenderExecutionPackage.TransitionExecutionDirectives, t => t.FromSceneCode == "moon_jupiter_hero_scene" && t.ToSceneCode == "viewing_tip_wide_scene");
        Assert.Contains(response.RenderExecutionPackage.TransitionExecutionDirectives, t => t.ToSceneCode == "outro");
        Assert.Equal(response.RenderExecutionPackage.ExecutionScenes.Count, response.RenderExecutionPackage.RendererExecutionContracts.Count);
        Assert.All(response.RenderExecutionPackage.RendererExecutionContracts, c => Assert.True(c.RendererDecisionLocked));
        Assert.Contains(response.RenderExecutionPackage.ExecutionTimeline, x => x.SceneCode == "thumbnail_story_scene" && x.IsThumbnailOnly);
        Assert.Equal(110, response.RenderExecutionPackage.ExecutionTimeline.Where(x => !x.IsThumbnailOnly).OrderBy(x => x.StartSecond).Last().EndSecond);
        Assert.DoesNotContain(response.RenderExecutionPackage.ExecutionScenes, x => x.SceneCode == "thumbnail_story_scene");
        Assert.True(response.Phase5FoundationStatus!.IsFrozen);
        Assert.True(response.Phase5FoundationStatus.IsReadyForPhase6);
        Assert.True(response.RenderPreparationFreezeStatus!.IsFrozen);
        Assert.True(response.RenderPreparationFreezeStatus.IsReadyForPhase6B);
        Assert.True(response.PreviewStability.ReadyForRenderPreparation);
        Assert.False(response.PreviewStability.ReadyForRendering);
        Assert.DoesNotContain("DailySkyGuide", response.Category, StringComparison.OrdinalIgnoreCase);
        Assert.All(response.SceneChoreographyPackage.ResolvedScenes, s => Assert.False(string.IsNullOrWhiteSpace(s.CameraPlan.PrimaryBehavior)));
        Assert.All(response.SceneChoreographyPackage.ResolvedScenes, s => Assert.False(string.IsNullOrWhiteSpace(s.MotionPlan)));
        Assert.All(response.SceneChoreographyPackage.ResolvedScenes, s => Assert.Contains(response.SceneChoreographyPackage.RenderContracts, c => c.SceneCode == s.SceneCode));
        Assert.All(response.SceneChoreographyPackage.ResolvedScenes, s => Assert.Contains(response.SceneChoreographyPackage.SceneTimeline, t => t.SceneCode == s.SceneCode));
        Assert.Contains(response.SceneChoreographyPackage.ResolvedAssets, a => a.FallbackPath.Contains("GeneratedImage", StringComparison.OrdinalIgnoreCase));
        Assert.All(response.SceneChoreographyPackage.ResolvedScenes.Where(s => s.RequiresStellarium), s => Assert.Contains("best_night", s.SceneCode, StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(response.RenderPreparationPackage.WorkingDirectoryPlan.RootPath));
        Assert.Empty(Directory.GetFiles(workingDirectory, "*.ssc", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(workingDirectory, "*.mp4", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(workingDirectory, "*.wav", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task V2_Cinematic_Refiner_Collapses_Repeated_Grouping_And_Keeps_Unique_Moments_And_Shorts()
    {
        var intelligence = BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext()));
        var editorial = await new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(intelligence, CancellationToken.None);
        var cinematic = await new WeeklySkyForecastV2CinematicEditorialRefiner().RefineAsync(editorial, intelligence with { EditorialStoryPackage = editorial }, CancellationToken.None);
        var narrative = await new WeeklySkyForecastV2NarrativeAbstractionBuilder().BuildAsync(cinematic, editorial, intelligence with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, CancellationToken.None);

        Assert.True(cinematic.HeroStory.SupportingDates.Count > 1);
        Assert.Equal(cinematic.CinematicMoments.Count, cinematic.CinematicMoments.Select(x => x.VisualUniquenessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, cinematic.ShortsBlueprints.Count);
        Assert.Equal(3, cinematic.ShortsBlueprints.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(cinematic.OpeningHook, new[] { "conjunction", "exact alignment", "rare alignment", "almost touching" }, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(narrative.CinematicVisualPlan.Count, narrative.CinematicVisualPlan.Select(x => x.VisualUniquenessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, narrative.ShortsNarrativePlan.Select(x => x.DistinctStoryAngle).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(narrative.StoryHeadline, new[] { "same viewing window grouping", "grouping event", "visibility momentum" }, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(narrative.OpeningNarrationHook, new[] { "conjunction", "exact alignment", "rare alignment", "nearly touching", "extremely close" }, StringComparer.OrdinalIgnoreCase);
    }

    private static WeeklySkyForecastV2IntelligenceResponse BuildResponse(IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> events)
    {
        return new WeeklySkyForecastV2IntelligenceResponse(null, "WeeklySkyForecast", true, new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "IN-RJ-UDAIPUR",
            new WeeklySkyForecastV2SkyfieldSummary(7, 21, 0, 1, "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25)),
            events,
            new WeeklyStoryArc("h", "s", "t", "o", ["a"], "c", ["MOON"], ["2026-05-24"], ["x"]),
            null!,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            ["Hybrid"],
            [],
            []);
    }

    [Fact]
    public async Task V2_NarrationPlan_Has_Expected_Segments_Durations_And_Strategies()
    {
        var intelligence = BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext()));
        var editorial = await new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(intelligence, CancellationToken.None);
        var cinematic = await new WeeklySkyForecastV2CinematicEditorialRefiner().RefineAsync(editorial, intelligence with { EditorialStoryPackage = editorial }, CancellationToken.None);
        var narrative = await new WeeklySkyForecastV2NarrativeAbstractionBuilder().BuildAsync(cinematic, editorial, intelligence with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, CancellationToken.None);
        var narration = await new WeeklySkyForecastV2NarrationPlanner().BuildAsync(narrative, cinematic, intelligence.SkyfieldSummary, intelligence.Region, intelligence.WeekStartDate, "en", CancellationToken.None);

        Assert.NotNull(narration);
        Assert.InRange(narration.LongFormPlan.SegmentCount, 6, 7);
        Assert.Equal(new[] { "OpeningHook", "HeroSkyStory", "WhyThisWeekMatters", "BestObservationNight", "MoonPlanetHighlight", "ViewingPhotographyTip", "ClosingCTA" }, narration.LongFormPlan.Segments.Select(x => x.SegmentCode).ToArray());
        Assert.Equal(3, narration.ShortsPlan.Shorts.Count);
        Assert.All(narration.LongFormPlan.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.RecommendedVisualStrategy)));
        Assert.All(narration.LongFormPlan.Segments, s => Assert.DoesNotContain(s.NarrationPromptHints, h => h.Contains("conjunction", StringComparison.OrdinalIgnoreCase) && h.Contains("claim", StringComparison.OrdinalIgnoreCase)));
        Assert.InRange(narration.LongFormPlan.TargetDurationSeconds, 90, 150);
        Assert.All(narration.LongFormPlan.Segments, s => Assert.True(s.EstimatedDurationSeconds > 0));
        Assert.All(narration.LongFormPlan.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceBeatCode)));
        Assert.Equal(narration.LongFormPlan.Segments.Count, narration.LongFormPlan.Segments.Select(s => string.Join("|", s.NarrationPromptHints)).Distinct().Count());
    }

    private static WeeklySkyForecastContext BuildContext()
    {
        var start = new DateOnly(2026, 5, 22);
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var d = start.AddDays(i);
            var t = DateTime.Parse($"{d:yyyy-MM-dd}T18:00:00Z");
            return new DailySkyForecastContextItem(d, t, t, "Waxing", 33, null, null,
                [new WeeklySkyForecastVisibleObjectItem("MOON", "Moon", "Moon", true, null, null, null, 55, t, 90, 80, "W", "Good"), new WeeklySkyForecastVisibleObjectItem("JUPITER", "Jupiter", "Planet", true, null, null, null, 60, t.AddMinutes(20), 92, 88, "W", "Great"), new WeeklySkyForecastVisibleObjectItem("VENUS", "Venus", "Planet", true, null, null, null, 40, t.AddMinutes(30), 85, 82, "W", "Great")],
                [], t, t.AddHours(2), 95 - i, "Excellent");
        }).ToList();

        return new WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", start, start.AddDays(6), "en", days, [], [new RecommendedObservationNight(new DateOnly(2026, 5, 24), 95, "Best", ["MOON", "JUPITER", "VENUS"], DateTime.Parse("2026-05-24T18:00:00Z"), DateTime.Parse("2026-05-24T20:00:00Z"))], "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), []);
    }

    private sealed class StubContextBuilder : IWeeklySkyForecastContextBuilder
    {
        public Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken) => Task.FromResult(BuildContext());
    }
}
