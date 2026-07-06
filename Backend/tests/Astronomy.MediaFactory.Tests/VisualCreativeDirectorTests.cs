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
        Assert.Contains("relationship-first", review.PlanetProminenceAssessment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avoid a dominant giant planet", string.Join(" ", review.CreativeNotes), StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("largest-object-wins", knowledge.AvoidPatterns);
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
            PrimaryObjects = request.PrimaryObjects,
            SupportingObjects = request.SupportingObjects,
            FeatureFlags = new VisualIntelligenceFlagSnapshot { UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true }
        });

    private static VisualIntelligenceOrchestrationRequest Request(string eventType, ContractEventFamily family = ContractEventFamily.PlanetConjunction, string eventName = "", List<string>? primary = null, List<string>? supporting = null) => new()
    {
        CorrelationId = "director-test",
        EventFamily = family,
        EventType = eventType,
        EventName = eventName,
        Language = "en",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        RequestedAssetType = "thumbnail",
        PrimaryObjects = primary ?? [],
        SupportingObjects = supporting ?? []
    };

    private static void AssertDirectiveContains(CDL cdl, string name, string expected) =>
        Assert.Contains(cdl.Directives, d => d.Name == name && d.Value.Contains(expected, StringComparison.OrdinalIgnoreCase));
}
