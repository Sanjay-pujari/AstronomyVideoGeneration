using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class VisualSourceResolverTests
{
    [Fact]
    public void Resolve_NamedFullMoon_RequiresMoonAndDisallowsGenericFallback()
    {
        var result = new DefaultVisualSourceResolver().Resolve(BuildRequest("NamedFullMoon", "Wolf Moon Full Moon", "Wolf Moon", primaryObjects: ["Moon"], requiredVisualObjects: ["Moon"]));

        Assert.Equal(VisualSourceType.Hybrid, result.SourceType);
        Assert.False(result.GenericFallbackAllowed);
        Assert.Contains("Moon", result.RequiredDrawableObjects);
        Assert.Contains("Moon.FullMoon", result.ScientificAssetKeys);
        Assert.Contains("realistic full Moon visual source", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crater texture", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Wolf Moon", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RealisticObjectRequired);
        Assert.False(result.AllowPrimitivePlaceholder);
        Assert.Equal(VisualMinimumQuality.Realistic, result.MinimumVisualQuality);
        Assert.Contains(VisualPreferredAssetKind.ScientificRealImage, result.PreferredAssetKind ?? []);
    }

    [Fact]
    public void Resolve_PlanetPairing_UsesActualObjectLabelsOnly()
    {
        var result = new DefaultVisualSourceResolver().Resolve(BuildRequest("PlanetPairing", "Mars and Jupiter Close Pairing", "Mars and Jupiter", primaryObjects: ["Mars"], secondaryObjects: ["Jupiter"], forbiddenObjectNames: ["Venus"]));

        Assert.True(result.SourceType is VisualSourceType.ComputedAstronomyScene or VisualSourceType.Hybrid);
        Assert.False(result.GenericFallbackAllowed);
        Assert.Contains("Mars", result.RequiredDrawableObjects);
        Assert.Contains("Jupiter", result.RequiredDrawableObjects);
        Assert.Contains("Venus", result.ForbiddenObjectNames);
        Assert.DoesNotContain("Venus", result.RequiredDrawableObjects);
        Assert.Contains("labels matching their exact names", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("real-looking planet textures", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mars must look like Mars", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jupiter must show banded cloud texture", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.AllowPrimitivePlaceholder);
    }

    [Fact]
    public void Resolve_Fallback_AllowsGenericOnlyWhenNoRequiredVisualObjectsExist()
    {
        var resolver = new DefaultVisualSourceResolver();

        var generic = resolver.Resolve(BuildRequest("AstronomyEvent", "General night sky explainer", "Night sky"));
        var required = resolver.Resolve(BuildRequest("AstronomyEvent", "Comet explainer", "Comet", requiredVisualObjects: ["Comet"]));

        Assert.Equal(VisualSourceType.GenericFallback, generic.SourceType);
        Assert.True(generic.GenericFallbackAllowed);
        Assert.NotEqual(VisualSourceType.GenericFallback, required.SourceType);
        Assert.False(required.GenericFallbackAllowed);
        Assert.Contains("Comet", required.RequiredDrawableObjects);
        Assert.Contains("nucleus", required.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coma", required.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tail", required.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DeepSkyObject_ForbidsGenericGlowPlaceholder()
    {
        var result = new DefaultVisualSourceResolver().Resolve(BuildRequest("DeepSkyObject", "Orion Nebula", "Orion Nebula", requiredVisualObjects: ["Orion Nebula"]));

        Assert.Equal(VisualSourceType.AICinematicScene, result.SourceType);
        Assert.True(result.RealisticObjectRequired);
        Assert.False(result.AllowPrimitivePlaceholder);
        Assert.Contains("astrophotography detail", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not render a generic glow circle", result.AiCinematicPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private static VisualSourceResolutionRequest BuildRequest(
        string eventType,
        string title,
        string shortTitle,
        IReadOnlyList<string>? primaryObjects = null,
        IReadOnlyList<string>? secondaryObjects = null,
        IReadOnlyList<string>? requiredVisualObjects = null,
        IReadOnlyList<string>? forbiddenObjectNames = null)
    {
        var intelligence = new ProductionEventIntelligence(
            Domain: "Astronomy",
            EventType: eventType,
            Title: title,
            ShortTitle: shortTitle,
            EventDate: null,
            PeakUtc: null,
            LocalPeakTime: null,
            BestViewingWindowLocal: null,
            SkyDirectionHint: null,
            VisibilityRegion: null,
            PrimaryObjects: primaryObjects ?? [],
            SecondaryObjects: secondaryObjects ?? [],
            ViewingQuality: null,
            MoonInterference: null,
            MoonIlluminationPercent: null,
            ScientificContext: null,
            ViewerInstructions: [],
            VisualMotifs: [],
            SceneStrategy: [],
            QualityWarnings: [],
            ForbiddenTerms: [],
            StrategyId: eventType,
            ForbiddenObjectNames: forbiddenObjectNames,
            RequiredVisualObjects: requiredVisualObjects);

        var scene = new EnrichedQuestionSceneDto(1, "What", "Hook", "What is happening?", title, "CasualSkyWatcher", "Beginner", shortTitle, "Narrate", "Show the event", "Render the event", "Overlay labels", "Accessible labels", true);
        var narration = new QuestionDrivenNarrationSceneDto(1, "What", "Hook", "What is happening?", shortTitle, title, "Narrate", title, 5, "Clear", shortTitle);
        return new VisualSourceResolutionRequest(intelligence, eventType, scene, narration, requiredVisualObjects ?? []);
    }
}
