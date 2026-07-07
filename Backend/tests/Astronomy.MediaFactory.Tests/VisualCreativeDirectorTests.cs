using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class VisualCreativeDirectorTests
{
    private readonly VisualCreativeDirector director = new(NullLogger<VisualCreativeDirector>.Instance);

    [Fact]
    public async Task PlanetPairing_creates_jupiter_venus_style_cdl()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));
        AssertDirectiveContains(result.Cdl!, "heroSubject", "Jupiter");
        Assert.Contains("Venus", result.CreativeDirectionContract!.VisualIntent.SecondarySubjects);
        AssertDirectiveContains(result.Cdl!, "astronomicalRendering", "perfectly circular");
    }

    [Fact]
    public async Task PlanetPairing_rendering_rules_include_primary_and_secondary_subjects()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        var subjects = result.CreativeDirectionContract!.PlanetRenderingRules.Subjects.Select(s => s.BodyName).ToArray();
        Assert.Contains("Jupiter", subjects);
        Assert.Contains("Venus", subjects);
    }


    [Fact]
    public async Task PlanetPairing_treats_conjunction_relationship_as_hero()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        Assert.Contains("conjunction is the hero", result.CreativeDirectionContract!.VisualIntent.PrimarySubject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jupiter + Venus", result.CreativeDirectionContract.VisualIntent.PrimarySubject, StringComparison.OrdinalIgnoreCase);
        AssertDirectiveContains(result.Cdl!, "composition", "balanced visual prominence");
        AssertDirectiveContains(result.Cdl!, "visualHierarchy", "Story first");
    }


    [Fact]
    public async Task PlanetPairing_refinement_recommends_balanced_relationship_prominence()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        var review = Assert.IsType<PlanetRelationshipReview>(result.CreativeDirectionContract!.ExtensionFields["planetRelationshipReview"]);
        Assert.True(review.RelationshipScore >= .95);
        Assert.True(review.VisualBalanceScore >= .9);
        Assert.Contains("visual partnership", review.PlanetProminenceAssessment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Relationship > Balance > Wonder > Scale", review.RelationshipClarity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shared visual center", review.VisualBalance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conjunction is the hero", review.StoryCommunication, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("twilight", review.DocumentaryAuthenticity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line-of-sight", review.ScientificPlausibility, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("huge primary", string.Join(" ", review.CreativeNotes), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("relationship", string.Join(" ", review.CreativeRecommendations), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runnerStatus=metadata-only", review.BenchmarkPreparation);
        Assert.Contains("negative space", review.CompositionRecommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Editorial_composition_context_is_optional_for_clean_close_approach()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        AssertDirectiveContains(result.Cdl!, "documentaryContext", "No foreground by default");
        Assert.Equal("PlanetPairing_CloseApproach", result.CreativeDirectionContract!.ExtensionFields["compositionTemplateUsed"]);
    }


    [Fact]
    public async Task DocumentaryAtmosphereDirector_recommends_authentic_twilight_sky_and_environment()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        var review = Assert.IsType<DocumentaryAtmosphereReview>(result.CreativeDirectionContract!.ExtensionFields["documentaryAtmosphereReview"]);
        Assert.False(review.GeneratesPrompts);
        Assert.Contains("civil or nautical twilight", review.TwilightAuthenticity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physically plausible atmospheric scattering", review.TwilightAuthenticity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean sky", review.SkyRealism, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subtle stars", review.SkyRealism, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only if it helps", review.EnvironmentQuality, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fantasy orange explosions", review.AvoidPatterns);
        Assert.Contains("random HDR", review.AvoidPatterns);
        Assert.True(review.DocumentaryScore >= .9);
        Assert.True(review.ScientificAtmosphereScore >= .9);
        Assert.Equal("hero-documentary-atmosphere", review.BenchmarkPreparation.BenchmarkFamily);
        Assert.False(review.BenchmarkPreparation.RunnerImplemented);
        AssertDirectiveContains(result.Cdl!, "atmosphere", "realistic evening gradient");
    }


    [Fact]
    public async Task HumanContextDirector_recommends_subtle_context_scale_and_observation_realism()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"], location: "observatory mountain ridge"));

        var review = Assert.IsType<HumanContextReview>(result.CreativeDirectionContract!.ExtensionFields["humanContextReview"]);
        Assert.False(review.GeneratesPrompts);
        Assert.Contains("observatory silhouette", review.RecommendedContextCues);
        Assert.Contains("mountain ridge", review.RecommendedContextCues);
        Assert.Contains("communicate scale", review.ScaleCommunication, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never compete", review.ScaleCommunication, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("believable observation locations", review.ObservationRealism, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean horizons", review.ObservationRealism, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("documentary photographer", review.StorySupport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crowded cities", review.AvoidPatterns);
        Assert.Equal("hero-human-context", review.BenchmarkPreparation.BenchmarkFamily);
        Assert.False(review.BenchmarkPreparation.RunnerImplemented);
        AssertDirectiveContains(result.Cdl!, "humanContext", "I could go outside tonight and observe this");
    }

    [Fact]
    public async Task PlanetGrouping_creates_multi_object_hierarchy()
    {
        var result = await Create(Request("planet-grouping", primary: ["Venus", "Mars", "Saturn"]));
        AssertDirectiveContains(result.Cdl!, "visualHierarchy", "hierarchy");
        Assert.Equal(3, result.CreativeDirectionContract!.PlanetRenderingRules.Subjects.Count);
    }

    [Fact]
    public async Task MeteorShower_creates_sky_radiant_streak_intent()
    {
        var result = await Create(Request("meteor-shower", family: ContractEventFamily.MeteorShower));
        AssertDirectiveContains(result.Cdl!, "creativeIntent", "radiant");
        AssertDirectiveContains(result.Cdl!, "supportingSubjects", "streak");
    }

    [Fact]
    public async Task NamedFullMoon_creates_moon_focused_cdl()
    {
        var result = await Create(Request("named-full-moon", family: ContractEventFamily.LunarEvent, eventName: "Strawberry Full Moon"));
        AssertDirectiveContains(result.Cdl!, "heroSubject", "Moon");
        AssertDirectiveContains(result.Cdl!, "visualHierarchy", "circular Moon");
    }

    [Fact]
    public async Task SolarEclipse_creates_eclipse_corona_safe_cdl()
    {
        var result = await Create(Request("solar-eclipse", family: ContractEventFamily.SolarEvent));
        AssertDirectiveContains(result.Cdl!, "supportingSubjects", "corona");
        AssertDirectiveContains(result.Cdl!, "negativeConstraints", "unsafe solar viewing");
    }

    [Fact]
    public async Task LunarEclipse_creates_umbra_red_moon_safe_cdl()
    {
        var result = await Create(Request("lunar-eclipse", family: ContractEventFamily.LunarEvent));
        AssertDirectiveContains(result.Cdl!, "supportingSubjects", "umbra");
        AssertDirectiveContains(result.Cdl!, "visualHierarchy", "red Moon");
    }

    [Fact]
    public async Task Unknown_family_returns_generic_cdl_with_warning()
    {
        var result = await Create(Request("mystery-sky-event", family: ContractEventFamily.Unknown));
        AssertDirectiveContains(result.Cdl!, "creativeIntent", "generic premium astronomy documentary");
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_director.unknown_family" && d.Severity == DiagnosticSeverity.Warning);
    }



    [Fact]
    public async Task VisualStoryModel_generates_planet_pairing_story_without_largest_planet_priority()
    {
        var result = await Create(Request("PlanetPairing", primary: ["Jupiter"], supporting: ["Venus"]));

        Assert.NotNull(result.VisualStory);
        Assert.Equal("Relationship", result.VisualStory!.PrimaryVisualSubject);
        Assert.Equal("This is an apparent conjunction.", result.VisualStory.ViewerTakeaway);
        Assert.Equal("Wonder.", result.VisualStory.EmotionalHook);
        Assert.Equal("Balanced pairing", result.VisualStory.RecommendedComposition);
        Assert.Contains("do not prioritize the largest planet", result.VisualStory.VisualRelationship, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VisualStoryModel_falls_back_for_unknown_family_and_serializes()
    {
        var result = await Create(Request("mystery-sky-event", family: ContractEventFamily.Unknown));

        var json = JsonSerializer.Serialize(result.VisualStory, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<VisualStory>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal("4.3A", reparsed!.StoryVersion);
        Assert.Equal("Observable sky event", reparsed.PrimaryVisualSubject);
    }

    [Theory]
    [InlineData("landscape", "wide documentary composition")]
    [InlineData("portrait", "large hero objects")]
    [InlineData("square", "balanced centered composition")]
    public async Task VisualStoryModel_includes_platform_recommendations(string key, string expected)
    {
        var result = await Create(Request("PlanetPairing", primary: ["Jupiter"], supporting: ["Venus"]));

        Assert.True(result.VisualStory!.RecommendedPlatformVariations.ContainsKey(key));
        Assert.Contains(expected, result.VisualStory.RecommendedPlatformVariations[key].Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreativeKnowledgeLibrary_retrieves_planet_pairing_knowledge()
    {
        var knowledge = new CreativeKnowledgeLibrary().Get(CreativeKnowledgeFamily.PlanetPairing);

        Assert.Equal(CreativeKnowledgeFamily.PlanetPairing, knowledge.Family);
        Assert.Contains("relationship", knowledge.StoryGoal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("largest-object-wins staging", knowledge.AvoidPatterns);
        Assert.Contains("huge primary with tiny secondary planet", knowledge.AvoidPatterns);
        Assert.Contains("Relationship > Balance > Wonder > Scale", knowledge.CompositionStrategy, StringComparison.OrdinalIgnoreCase);
        Assert.True(knowledge.Domains.ContainsKey(CreativeKnowledgeDomain.ViewerPsychology));
    }

    [Fact]
    public void CreativeKnowledgeLibrary_resolves_family_from_context()
    {
        var knowledge = new CreativeKnowledgeLibrary().Resolve(new VisualIntelligenceOrchestrationContext
        {
            EventFamily = ContractEventFamily.MeteorShower,
            EventType = "perseid meteor shower"
        });

        Assert.Equal(CreativeKnowledgeFamily.MeteorShower, knowledge.Family);
        Assert.Contains("radiant", knowledge.StoryGoal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreativeKnowledgeLibrary_falls_back_to_generic_knowledge()
    {
        var diagnostics = new List<DiagnosticMessage>();
        var knowledge = new CreativeKnowledgeLibrary().Resolve(new VisualIntelligenceOrchestrationContext { EventFamily = ContractEventFamily.Unknown }, diagnostics: diagnostics);

        Assert.Equal(CreativeKnowledgeFamily.GenericAstronomy, knowledge.Family);
        Assert.Contains(diagnostics, d => d.Code == "creative_knowledge.fallback" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task VisualCreativeDirector_includes_creative_knowledge_in_contract_extensions()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter"], supporting: ["Venus"]));

        Assert.Equal("PlanetPairing", result.CreativeDirectionContract!.ExtensionFields["creativeKnowledgeFamily"]);
        Assert.Contains("What makes", result.CreativeDirectionContract.ExtensionFields["viewerQuestion"]!.ToString());
        Assert.Contains(result.Diagnostics, d => d.Code == "creative_knowledge.resolved");
    }

    [Fact]
    public async Task Json_serialization_remains_valid()
    {
        var result = await Create(Request("planet-pairing", primary: ["Jupiter", "Venus"]));
        var json = JsonSerializer.Serialize(result.CreativeDirectionContract, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<CreativeDirectionContract>(json, VisualIntelligenceJson.CreateSerializerOptions());
        Assert.NotNull(reparsed);
        Assert.Equal(ContractEventFamily.PlanetConjunction, reparsed!.EventFamily);
    }

    private Task<VisualCreativeDirectorResult> Create(VisualIntelligenceOrchestrationRequest request) =>
        director.CreateDirectionAsync(new VisualIntelligenceOrchestrationContext
        {
            CorrelationId = request.CorrelationId ?? "test",
            EventFamily = request.EventFamily,
            EventType = request.EventType,
            EventName = request.EventName,
            Language = request.Language,
            Platform = request.Platform,
            AspectRatio = request.AspectRatio,
            RequestedAssetType = request.RequestedAssetType,
            Location = request.Location,
            Region = request.Region,
            PrimaryObjects = request.PrimaryObjects,
            SupportingObjects = request.SupportingObjects,
            FeatureFlags = new VisualIntelligenceFlagSnapshot { UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true }
        });

    private static VisualIntelligenceOrchestrationRequest Request(string eventType, ContractEventFamily family = ContractEventFamily.PlanetConjunction, string eventName = "", List<string>? primary = null, List<string>? supporting = null, string location = "") => new()
    {
        CorrelationId = "director-test",
        EventFamily = family,
        EventType = eventType,
        EventName = eventName,
        Language = "en",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        RequestedAssetType = "thumbnail",
        Location = location,
        PrimaryObjects = primary ?? [],
        SupportingObjects = supporting ?? []
    };

    private static void AssertDirectiveContains(CDL cdl, string name, string expected) =>
        Assert.Contains(cdl.Directives, d => d.Name == name && d.Value.Contains(expected, StringComparison.OrdinalIgnoreCase));
}
