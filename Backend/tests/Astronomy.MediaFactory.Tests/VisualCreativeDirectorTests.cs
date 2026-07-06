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
