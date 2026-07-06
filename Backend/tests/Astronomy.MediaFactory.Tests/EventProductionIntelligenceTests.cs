using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventProductionIntelligenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [Fact]
    public void AstronomyAdapter_NormalizesMeteorShowerWithoutGeminidsHardcoding()
    {
        var adapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new PlanetGroupingStrategy(),
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ]));

        var planRequest = new ContentPlanProductionPipelineRequest(
            Guid.NewGuid(),
            "RareEventAlert",
            "Perseids Meteor Shower Peak",
            "Perseids",
            "MeteorShower",
            "US-CA-SF",
            "en",
            ["Perseids"],
            ["Meteors"],
            DateTimeOffset.Parse("2026-08-12T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-12T18:00:00Z"),
            DateTimeOffset.Parse("2026-08-13T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-10T12:00:00Z"),
            "perseids-2026",
            "ShortAndLong",
            ["ShortVideo", "LongVideo"],
            9m,
            8m,
            8m,
            9m,
            "Verified",
            "source",
            "RareEventAlert",
            "2026-08-12 11:00 AM PDT",
            "northeast to overhead after midnight",
            "Northern Hemisphere",
            "Low",
            "2026-08-13 00:00–05:00 PDT",
            "Radiant rises in the northeast after midnight.",
            12m,
            "publish evening before",
            ["ShortVideo"],
            [],
            []);

        var result = adapter.Normalize(new ProductionPipelineRequest(planRequest, Guid.NewGuid(), "/tmp/out", false, true));

        Assert.Equal("Astronomy", result.Domain);
        Assert.Equal("MeteorShower", result.EventType);
        Assert.Equal("Perseids Meteor Shower Peak", result.Title);
        Assert.Contains("meteor streaks", result.VisualMotifs);
        Assert.Contains("Venus", result.ForbiddenTerms);
        Assert.Contains("Jupiter", result.ForbiddenTerms);
        Assert.Contains("2026-08-13 00:00–05:00 PDT", result.BestViewingWindowLocal);
        Assert.DoesNotContain("Geminids", string.Join(" ", result.VisualMotifs.Concat(result.SceneStrategy)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AstronomyAdapter_NormalizesPlanetPairingWithoutMeteorLeakage()
    {
        var adapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new PlanetGroupingStrategy(),
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ]));

        var planRequest = new ContentPlanProductionPipelineRequest(
            Guid.NewGuid(),
            "RareEventAlert",
            "Venus and Jupiter Close Pairing",
            "Venus + Jupiter",
            "PlanetPairing",
            "IN-RJ-UDAIPUR",
            "en",
            ["Venus"],
            ["Jupiter"],
            DateTimeOffset.Parse("2026-06-20T13:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T14:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T16:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T12:00:00Z"),
            "venus-jupiter-pairing-2026",
            "ShortAndLong",
            ["HeroAsset", "Thumbnail", "Gallery", "SceneAssets", "LongVideo", "ShortVideo"],
            9m, 8m, 8m, 9m,
            "Verified",
            "source",
            "PlanetPairing",
            "2026-06-20 08:00 PM IST",
            "western horizon",
            "India",
            null,
            "2026-06-20 19:30–20:30 IST",
            "Venus is brighter; Jupiter appears nearby.",
            null,
            "publish same evening",
            ["ShortVideo", "LongVideo"],
            [],
            []);

        var result = adapter.Normalize(new ProductionPipelineRequest(planRequest, Guid.NewGuid(), "/tmp/out", false, true));

        Assert.Equal("PlanetPairing", result.EventType);
        Assert.Equal("Planetary Encounter", result.StoryTheme);
        Assert.Equal("Twilight sky with two bright planets", result.VisualTheme);
        Assert.Equal("Planet markers with direction and altitude", result.SkyGuideTheme);
        Assert.Equal("Close apparent meeting of two planets", result.NarrationTheme);
        Assert.Equal("Event Intelligence / Event Profile / Event Content Strategy::PlanetPairing", result.EventSpecificStrategySource);
        Assert.Contains("Venus", result.RequiredVisualObjects!);
        Assert.Contains("Jupiter", result.RequiredVisualObjects!);
        Assert.Contains("meteor", result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("meteor shower", result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("debris stream", result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Phaethon", result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Geminids", result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);

        var generatedStrategyText = string.Join(" ", result.VisualMotifs.Concat(result.SceneStrategy).Concat(result.ThumbnailCopyCandidates ?? []).Concat(result.HeroCopyCandidates ?? []));
        foreach (var forbidden in result.ForbiddenTerms)
        {
            Assert.DoesNotContain(forbidden, generatedStrategyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AstronomyAdapter_PlanetConjunctionEnrichesVenusJupiterBeforePhase3()
    {
        var adapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new PlanetGroupingStrategy(),
            new ConjunctionStrategy(),
            new GenericAstronomyEventStrategy()
        ]));

        var planRequest = new ContentPlanProductionPipelineRequest(
            Guid.NewGuid(),
            "PlanetConjunction",
            "Venus and Jupiter conjunction",
            "Venus + Jupiter",
            "PLANET_CONJUNCTION",
            "IN-RJ-UDAIPUR",
            "en",
            ["Venus"],
            ["Jupiter"],
            DateTimeOffset.Parse("2026-06-20T13:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T14:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T16:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T12:00:00Z"),
            "venus-jupiter-conjunction-2026",
            "ShortAndLong",
            ["SceneAssets", "LongVideo", "ShortVideo"],
            9m, 8m, 8m, 9m,
            "Verified",
            "source",
            "Planetary conjunction in the western evening sky",
            null,
            null,
            "India",
            null,
            null,
            null,
            null,
            "publish same evening",
            ["ShortVideo", "LongVideo"],
            [],
            [],
            "Asia/Kolkata",
            1.4m);

        var result = adapter.Normalize(new ProductionPipelineRequest(planRequest, Guid.NewGuid(), "/tmp/out", false, true));

        Assert.Equal("Conjunction", result.StrategyId);
        Assert.Equal("JUPITER + VENUS", result.ShortTitle);
        Assert.True(result.ShortTitle.Length <= 50);
        Assert.Equal("Planetary conjunction", result.StoryTheme);
        Assert.Equal("two bright planets close together in twilight sky", result.VisualTheme);
        Assert.Equal("Venus and Jupiter markers with direction and altitude", result.SkyGuideTheme);
        Assert.Equal("calm documentary explanation of apparent planetary alignment", result.NarrationTheme);
        Assert.Equal(1.4m, result.AngularSeparationDegrees);
        Assert.Contains("Venus", result.ResolvedObjectNames!);
        Assert.Contains("Jupiter", result.ResolvedObjectNames!);
        Assert.Contains("Venus", result.RequiredVisualObjects!);
        Assert.Contains("Jupiter", result.RequiredVisualObjects!);
        Assert.NotNull(result.LocalPeakTime);
        Assert.NotNull(result.BestViewingWindowLocal);
        Assert.Equal("Look toward the western sky after sunset", result.SkyDirectionHint);
        foreach (var forbidden in new[] { "meteor", "meteor shower", "radiant", "Phaethon", "debris stream", "Geminids" })
            Assert.Contains(forbidden, result.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        var generatedText = string.Join(" ", result.VisualTheme, result.NarrationTheme, string.Join(" ", result.SceneStrategy), string.Join(" ", result.VisualMotifs));
        Assert.DoesNotContain("Geminids", generatedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debris stream", generatedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AstronomyAdapter_PlanetPairingSupportDoesNotChangeMeteorShowerStrategyOutput()
    {
        var meteorOnlyAdapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new GenericAstronomyEventStrategy()
        ]));
        var multiEventAdapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new GenericAstronomyEventStrategy()
        ]));
        var request = new ProductionPipelineRequest(BuildGeminidsPlanRequest(), Guid.NewGuid(), "/tmp/out", false, true);

        var baseline = meteorOnlyAdapter.Normalize(request);
        var multiEvent = multiEventAdapter.Normalize(request);

        Assert.Equal(baseline.EventType, multiEvent.EventType);
        Assert.Equal(baseline.VisualMotifs, multiEvent.VisualMotifs);
        Assert.Equal(baseline.SceneStrategy, multiEvent.SceneStrategy);
        Assert.Equal(baseline.ForbiddenTerms, multiEvent.ForbiddenTerms);
        Assert.Equal(baseline.RequiredVisualObjects, multiEvent.RequiredVisualObjects);
        Assert.Equal(baseline.RequiredNarrationFacts, multiEvent.RequiredNarrationFacts);
        Assert.Equal(baseline.ThumbnailCopyCandidates, multiEvent.ThumbnailCopyCandidates);
        Assert.Equal(baseline.HeroCopyCandidates, multiEvent.HeroCopyCandidates);
    }

    private static ContentPlanProductionPipelineRequest BuildGeminidsPlanRequest()
        => new(
            Guid.NewGuid(), "RareEventAlert", "Geminids Meteor Shower Peak", "Geminids", "MeteorShower", "IN-RJ-UDAIPUR", "en",
            ["Geminids"], ["Meteors"], DateTimeOffset.Parse("2026-12-13T18:00:00Z"), DateTimeOffset.Parse("2026-12-13T18:30:00Z"), DateTimeOffset.Parse("2026-12-14T06:00:00Z"), DateTimeOffset.Parse("2026-12-10T12:00:00Z"),
            "geminids-2026", "ShortAndLong", ["ShortVideo", "LongVideo"], 10m, 9m, 9m, 10m, "Verified", "source", "RareEventAlert", "2026-12-14 12:00 AM IST", "east to overhead", "India", "Low", "2026-12-14 00:00–05:00 IST", "Radiant high after midnight.", 10m, "publish evening before", ["ShortVideo"], [], []);


    [Theory]
    [InlineData("open the file", "file", true)]
    [InlineData("profile", "file", false)]
    [InlineData("wildlife", "file", false)]
    [InlineData("filed", "file", false)]
    [InlineData("lifestyle", "file", false)]
    public void ProductionQualityContainsToken_UsesStandaloneTokenBoundaries(string text, string token, bool expected)
    {
        var method = typeof(ProductionPipelineQualityValidator).GetMethod("ContainsToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("ContainsToken helper was not found.");

        var actual = (bool)method.Invoke(null, [text, token])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StrategyResolver_ExposesRequiredProductionStrategies()
    {
        IMediaEventStrategy[] strategies =
        [
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new PlanetGroupingStrategy(),
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ];

        Assert.Contains(strategies, s => s is MeteorShowerStrategy);
        Assert.Contains(strategies, s => s is PlanetPairingStrategy);
        Assert.Contains(strategies, s => s is PlanetGroupingStrategy);
        Assert.Contains(strategies, s => s is ConjunctionStrategy);
        Assert.Contains(strategies, s => s is NamedFullMoonStrategy);
        Assert.Contains(strategies, s => s is NewMoonStrategy);
        Assert.Contains(strategies, s => s is LunarEclipseStrategy);
        Assert.Contains(strategies, s => s is SolarEclipseStrategy);
    }
    [Fact]
    public void AstronomyAdapter_RoutesPlanetGroupingToDedicatedStrategy()
    {
        var adapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new PlanetGroupingStrategy(),
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ]));

        var planRequest = new ContentPlanProductionPipelineRequest(
            Guid.NewGuid(),
            "PlanetGrouping",
            "Planet grouping over Udaipur",
            "Planet grouping",
            "PLANET_GROUPING",
            "IN-RJ-UDAIPUR",
            "en",
            ["Venus"],
            ["Jupiter", "Mars"],
            DateTimeOffset.Parse("2026-06-20T13:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T14:30:00Z"),
            DateTimeOffset.Parse("2026-06-20T16:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T12:00:00Z"),
            "planet-grouping-2026",
            "ShortAndLong",
            ["ShortVideo", "HeroAsset", "Thumbnail"],
            8m,
            7m,
            8m,
            8m,
            "Verified",
            "source",
            "PlanetGrouping",
            "2026-06-20 08:00 PM IST",
            "western horizon",
            "India",
            null,
            "2026-06-20 20:00–21:30 IST",
            null,
            null,
            "publish same evening",
            ["ShortVideo"],
            [],
            []);

        var result = adapter.Normalize(new ProductionPipelineRequest(planRequest, Guid.NewGuid(), "/tmp/out", false, true));

        Assert.Equal("PLANET_GROUPING", result.EventType);
        Assert.Equal("PlanetGrouping", result.StrategyId);
        Assert.Contains("Venus", result.RequiredVisualObjects!);
        Assert.Contains("Jupiter", result.RequiredVisualObjects!);
        Assert.Contains("Mars", result.RequiredVisualObjects!);
        Assert.DoesNotContain("planet grouping", result.RequiredVisualObjects!, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("guided scan path", result.RequiredVisualObjects!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("multi-planet grouping", result.VisualMotifs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("guided scan path", result.VisualMotifs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PlanetGroupingSceneStrategy", string.Join(" ", result.SceneStrategy));
        Assert.Contains("PlanetGroupingHeroStrategy", result.HeroCopyCandidates!);
        Assert.Contains("PlanetGrouping", result.ThumbnailCopyCandidates!);
        Assert.DoesNotContain("cinematic night sky", result.VisualMotifs, StringComparer.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData("Timing card: 2026-12-14 00:00–05:00 IST.", true)]
    [InlineData("Timing card: 00:00–05:00 IST — best viewing window.", true)]
    [InlineData("Best viewing window: Midnight to pre-dawn under dark skies.", true)]
    [InlineData("Peak calculation: 11:30 +05:30.", false)]
    public void MeteorBestViewingWindowValidation_AcceptsNormalizedViewingWindowButRejectsDaytimePeak(string text, bool expected)
    {
        var method = typeof(ProductionPipelineQualityValidator).GetMethod("HasBestViewingWindowEvidence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("HasBestViewingWindowEvidence helper was not found.");
        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "MeteorShower",
            "Perseids Meteor Shower Peak",
            "Perseids",
            DateTimeOffset.Parse("2026-12-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-14T06:00:00Z"),
            "11:30 +05:30",
            "2026-12-14 00:00–05:00 IST",
            "east to overhead",
            "India",
            ["Perseids"],
            [],
            null,
            "Low",
            10m,
            "Perseids Meteor Shower Peak",
            [],
            ["meteor streaks"],
            ["Use bestViewingWindowLocal"],
            [],
            ["Venus", "Jupiter"]);

        var actual = (bool)method.Invoke(null, [intelligence, text])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QuestionDrivenVisualSpec_SerializesMeteorTimingStrategyValidationFacts()
    {
        var spec = new QuestionDrivenVisualSpec(
            "event-1",
            "IN-RJ-UDAIPUR",
            "en",
            3,
            "When",
            "Timing",
            "When should I watch?",
            "Watch from midnight to pre-dawn.",
            "Best viewing window is 2026-12-14 00:00–05:00 IST.",
            "00:00–05:00 IST",
            6,
            "meteor shower timing",
            ["2026-12-14", "00:00–05:00 IST", "Midnight to pre-dawn"],
            ["time:2026-12-14 00:00–05:00 IST marker"],
            ["Meteor timing cues"],
            DateTimeOffset.Parse("2026-06-11T00:00:00Z"),
            "MeteorShower",
            false,
            "2026-12-14 00:00–05:00 IST",
            new Dictionary<string, string>
            {
                ["bestViewingWindowLocal"] = "2026-12-14 00:00–05:00 IST",
                ["eventType"] = "MeteorShower",
                ["requiredTimingCue"] = "00:00–05:00 IST"
            });

        var json = System.Text.Json.JsonSerializer.Serialize(spec, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("bestViewingWindowLocal", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strategyValidationFacts", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("00:00–05:00 IST", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionQualityValidator_UsesSceneValidationStrategyForGeminids()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-strategy-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "Geminids MeteorShower scene plan with meteor streaks, radiant hint, dark sky, east to overhead direction, and best viewing window 2026-12-14 00:00–05:00 IST.");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.enriched.json"), "Geminids enriched plan: bestViewingWindowLocal 2026-12-14 00:00–05:00 IST, radiant high after midnight, dark sky.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "title":"Geminids Meteor Shower Peak",
  "visual":"Geminids meteor streaks across a dark sky with radiant hint",
  "time":"2026-12-14 00:00–05:00 IST",
  "forbiddenVisualObjects":["Venus","Jupiter"],
  "validationForbiddenTerms":["conjunction"],
  "resolverConfiguration":"avoid unrelated planets like Venus and Jupiter",
  "strategyValidationFacts":{
    "visualSourceType":"Hybrid",
    "assetKey":"Meteor.RealisticStreaks",
    "generatedRealisticPrompt":"realistic meteor streaks from a radiant in a dark sky",
    "objectVisualSource":"meteor streaks:AICinematic realistic meteor streaks",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic"
  }
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), "{\"checks\":[\"meteor streaks visible\",\"radiant hint visible\",\"dark sky readable\",\"no forbidden object leakage\"]}");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Watch the Geminids from midnight to pre-dawn under a dark sky; meteors radiate from Gemini.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nGeminids meteor streaks are best from 00:00–05:00 IST.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "MeteorShower",
            "Geminids Meteor Shower Peak",
            "Geminids",
            DateTimeOffset.Parse("2026-12-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-14T06:00:00Z"),
            "11:30 +05:30",
            "2026-12-14 00:00–05:00 IST",
            "east to overhead",
            "India",
            ["Geminids"],
            [],
            null,
            "Low",
            10m,
            "Geminids Meteor Shower Peak",
            [],
            ["meteor streaks", "radiant hint", "dark sky"],
            ["Use bestViewingWindowLocal"],
            [],
            ["Venus", "Jupiter"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new MeteorShowerSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
    }
    [Theory]
    [InlineData("Snow Moon", "Snow Moon", true)]
    [InlineData("Snow-Moon", "Snow Moon", true)]
    [InlineData("Snow    Moon", "Snow Moon", true)]
    [InlineData("Snowmoon", "Snow Moon", false)]
    public void ProductionQualityContainsToken_IgnoresPunctuationAndExtraWhitespace(string text, string token, bool expected)
    {
        var method = typeof(ProductionPipelineQualityValidator).GetMethod("ContainsToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("ContainsToken helper was not found.");

        var actual = (bool)method.Invoke(null, [text, token])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ProductionQualityValidator_AcceptsSnowMoonShortTitleMetadataForPhase10()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-snow-moon-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "NamedFullMoon scene plan for the Snow Moon with Moon/Snow Moon visual evidence and viewing window 2026-02-01 18:00–23:00 UTC.");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.enriched.json"), "Snow Moon enriched plan: a full Moon over a winter horizon, no unrelated planet leakage, viewing window 2026-02-01 18:00–23:00 UTC.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Snow Moon Full Moon: what to watch.",
  "captionText":"Snow Moon Full Moon: what to watch.",
  "overlayText":"Snow Moon",
  "visualSourceResolution":{
    "metadata":{
      "eventShortTitle":"Snow Moon",
      "eventTitle":"Snow Moon Full Moon"
    },
    "sourceType":"Hybrid",
    "realisticObjectRequired":true,
    "primitivePlaceholderUsed":false
  },
  "strategyValidationFacts":{
    "visualSourceType":"Hybrid",
    "assetKey":"Moon.FullMoon",
    "generatedRealisticPrompt":"realistic full Moon texture with craters and maria",
    "objectVisualSource":"Moon:LocalAsset Moon.FullMoon; ScientificAsset Moon.FullMoon; AICinematic realistic Moon",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic",
    "eventShortTitle":"Snow Moon",
    "eventTitle":"Snow Moon Full Moon"
  },
  "backgroundPrompt":"large visible Moon/Snow Moon above a snowy horizon",
  "accessibilityCues":["Moon/Snow Moon is the dominant object"]
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), """
{
  "shortTitle":"Snow Moon",
  "checks":["Moon/Snow Moon visible", "full Moon glow readable", "no forbidden object leakage"]
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Watch the Snow Moon during the evening viewing window.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nSnow Moon full Moon is visible from 18:00–23:00 UTC.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "NamedFullMoon",
            "Snow Moon Full Moon",
            "Snow Moon",
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T18:00:00Z"),
            "18:00 UTC",
            "2026-02-01 18:00–23:00 UTC",
            "eastern sky",
            "Global",
            ["Moon"],
            [],
            null,
            "Low",
            100m,
            "Snow Moon Full Moon",
            [],
            ["Moon", "full moon glow"],
            ["Use shortTitle metadata"],
            [],
            ["Venus", "Jupiter"],
            RequiredVisualObjects: ["Moon"],
            ForbiddenObjectNames: ["Venus", "Jupiter"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new NamedFullMoonSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);

        using var validation = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "production-quality-validation-before-assembly.json")));
        Assert.True(validation.RootElement.GetProperty("titleFoundInCaptionText").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("titleFoundInViewerTakeaway").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("titleFoundInOverlayText").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("titleFoundInMetadata").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("titleFoundInReview").GetBoolean());

        var sceneDiagnostics = validation.RootElement.GetProperty("titleValidationDiagnostics").GetProperty("scenes").EnumerateArray().Single();
        Assert.Equal(1, sceneDiagnostics.GetProperty("sceneNumber").GetInt32());
        Assert.Equal(Path.Combine(sceneRoot, "scene-001-infographic-spec.json").Replace('\\', '/'), sceneDiagnostics.GetProperty("specPathUsed").GetString());
        Assert.Equal(Path.Combine(sceneRoot, "scene-001-review.json").Replace('\\', '/'), sceneDiagnostics.GetProperty("reviewPathUsed").GetString());
        Assert.Equal(Path.Combine(sceneRoot, "scene-001-narration.txt").Replace('\\', '/'), sceneDiagnostics.GetProperty("narrationPathUsed").GetString());
        Assert.Equal(Path.Combine(sceneRoot, "scene-001.srt").Replace('\\', '/'), sceneDiagnostics.GetProperty("srtPathUsed").GetString());
        Assert.Equal("Snow Moon Full Moon: what to watch.", sceneDiagnostics.GetProperty("captionTextLoaded").GetString());
        Assert.Equal("Snow Moon Full Moon: what to watch.", sceneDiagnostics.GetProperty("viewerTakeawayLoaded").GetString());
        Assert.Equal("Snow Moon", sceneDiagnostics.GetProperty("overlayTextLoaded").GetString());
        Assert.Equal("Snow Moon Full Moon", sceneDiagnostics.GetProperty("eventTitleLoaded").GetString());
        Assert.Equal("Snow Moon", sceneDiagnostics.GetProperty("eventShortTitleLoaded").GetString());
        Assert.True(sceneDiagnostics.GetProperty("titleValidationPassed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(sceneDiagnostics.GetProperty("titleMatchedValue").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sceneDiagnostics.GetProperty("titleMatchedSource").GetString()));
        Assert.Contains("Snow Moon Full Moon", sceneDiagnostics.GetProperty("titleAliasesRaw").EnumerateArray().Select(alias => alias.GetString()));
        Assert.Contains("Snow Moon", sceneDiagnostics.GetProperty("titleAliasesRaw").EnumerateArray().Select(alias => alias.GetString()));
        Assert.Contains("snow moon full moon", sceneDiagnostics.GetProperty("titleAliasesNormalized").EnumerateArray().Select(alias => alias.GetString()));
        Assert.Contains("Snow Moon Full Moon: what to watch.", sceneDiagnostics.GetProperty("candidateTextRaw").EnumerateArray().Select(candidate => candidate.GetString()));
        Assert.Contains("snow moon full moon what to watch", sceneDiagnostics.GetProperty("candidateTextNormalized").EnumerateArray().Select(candidate => candidate.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sceneDiagnostics.GetProperty("matchedAlias").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sceneDiagnostics.GetProperty("matchedCandidate").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sceneDiagnostics.GetProperty("matchedSource").GetString()));

        var titleDiagnostics = validation.RootElement.GetProperty("titleValidationDiagnostics");
        Assert.Contains("Snow Moon Full Moon", titleDiagnostics.GetProperty("titleAliasesRaw").EnumerateArray().Select(alias => alias.GetString()));
        Assert.Contains("snow moon full moon", titleDiagnostics.GetProperty("titleAliasesNormalized").EnumerateArray().Select(alias => alias.GetString()));
        Assert.Contains("Snow Moon Full Moon: what to watch.", titleDiagnostics.GetProperty("candidateTextRaw").EnumerateArray().Select(candidate => candidate.GetString()));
        Assert.Contains("snow moon full moon what to watch", titleDiagnostics.GetProperty("candidateTextNormalized").EnumerateArray().Select(candidate => candidate.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(titleDiagnostics.GetProperty("titleMatchedCandidate").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(titleDiagnostics.GetProperty("matchedAlias").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(titleDiagnostics.GetProperty("matchedCandidate").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(titleDiagnostics.GetProperty("matchedSource").GetString()));
    }


    [Fact]
    public async Task ProductionQualityValidator_PrefersSceneApprovalStagingRootForPhase10()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase10-output-{Guid.NewGuid():N}");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"phase10-staging-{Guid.NewGuid():N}", "question-engine", "scene-approval-v3");
        Directory.CreateDirectory(Path.Combine(outputRoot, "validation"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "scene-approval-v3", "short"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "scene-approval-v3", "long"));
        Directory.CreateDirectory(stagingRoot);

        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "question-driven-scene-plan.json"), "NamedFullMoon scene plan for Moon with viewing window 2026-02-01 18:00–23:00 UTC.");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Snow Moon viewing guide.",
  "captionText":"Snow Moon viewing guide.",
  "overlayText":["Moon"],
  "strategyValidationFacts":{
    "visualSourceType":"Hybrid",
    "assetKey":"Moon.FullMoon",
    "generatedRealisticPrompt":"realistic full Moon texture with craters and maria",
    "objectVisualSource":"Moon:LocalAsset Moon.FullMoon; ScientificAsset Moon.FullMoon; AICinematic realistic Moon",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic"
  },
  "backgroundPrompt":"large visible Moon above an eastern horizon",
  "accessibilityCues":["Moon is the dominant object"]
}
""");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "validation", "phase-08-validation.json"), JsonSerializer.Serialize(new { sceneApprovalStagingRoot = stagingRoot }, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "scene-001-review.json"), "Moon visible with full moon glow");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "scene-001-narration.txt"), "Watch the full Moon during the evening viewing window.");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "scene-approval-v3", "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nFull Moon is visible from 18:00–23:00 UTC.\n");
        Directory.CreateDirectory(Path.Combine(stagingRoot, "short"));
        Directory.CreateDirectory(Path.Combine(stagingRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Snow Moon viewing guide.",
  "captionText":"Snow Moon viewing guide.",
  "overlayText":["Moon"],
  "visualSourceResolution":{
    "metadata":{
      "eventShortTitle":"Snow Moon",
      "eventTitle":"Snow Moon Full Moon"
    },
    "sourceType":"Hybrid",
    "realisticObjectRequired":true,
    "primitivePlaceholderUsed":false
  },
  "strategyValidationFacts":{
    "visualSourceType":"Hybrid",
    "assetKey":"Moon.FullMoon",
    "generatedRealisticPrompt":"realistic full Moon texture with craters and maria",
    "objectVisualSource":"Moon:LocalAsset Moon.FullMoon; ScientificAsset Moon.FullMoon; AICinematic realistic Moon",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic"
  },
  "backgroundPrompt":"large visible Moon above an eastern horizon",
  "accessibilityCues":["Moon is the dominant object"]
}
""");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "NamedFullMoon",
            "Snow Moon Full Moon",
            "Snow Moon",
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T18:00:00Z"),
            "18:00 UTC",
            "2026-02-01 18:00–23:00 UTC",
            "eastern sky",
            "Global",
            ["Moon"],
            [],
            null,
            "Low",
            100m,
            "Full Moon viewing guide",
            [],
            ["Moon", "full moon glow"],
            [],
            [],
            ["Venus", "Jupiter"],
            RequiredVisualObjects: ["Moon"],
            ForbiddenObjectNames: ["Venus", "Jupiter"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new NamedFullMoonSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "scene-001-review.json"), "{\"checks\":[\"Snow Moon visible with Moon asset\"]}");

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, outputRoot, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        using var validation = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputRoot, "production-quality-validation-before-assembly.json")));
        Assert.True(validation.RootElement.GetProperty("titleFoundInCaptionText").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("titleFoundInMetadata").GetBoolean());

        var titleSources = validation.RootElement.GetProperty("titleValidationSourceDiagnostics").EnumerateArray().ToArray();
        var metadataSources = titleSources.Single(source => source.GetProperty("field").GetString() == "titleFoundInMetadata");
        var metadataSourcePaths = metadataSources.GetProperty("sourceFilePaths").EnumerateArray().Select(source => source.GetString()).ToArray();
        Assert.Contains(Path.Combine(stagingRoot, "scene-001-infographic-spec.json").Replace('\\', '/'), metadataSourcePaths);
        Assert.DoesNotContain(metadataSourcePaths, sourcePath => sourcePath!.Contains(Path.Combine(outputRoot, "scene-approval-v3"), StringComparison.OrdinalIgnoreCase));

        var visualDiagnostics = validation.RootElement.GetProperty("phase10VisualSourceInputDiagnostics");
        var scene = visualDiagnostics.GetProperty("scenes").EnumerateArray().Single();
        Assert.Equal(Path.Combine(stagingRoot, "scene-001-infographic-spec.json").Replace('\\', '/'), scene.GetProperty("specPathUsed").GetString());
        Assert.Equal(Path.Combine(stagingRoot, "scene-001-review.json").Replace('\\', '/'), scene.GetProperty("reviewPathUsed").GetString());
        Assert.Equal("Moon.FullMoon", scene.GetProperty("assetKey").GetString());
        Assert.False(scene.GetProperty("primitivePlaceholderUsed").GetBoolean());
        Assert.True(scene.GetProperty("visualValidationPassed").GetBoolean());
    }

    [Fact]
    public async Task ProductionQualityValidator_FailsPrimitivePlaceholderWhenRealisticObjectRequiredForPhase10()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-placeholder-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "NamedFullMoon scene plan for Snow Moon with Moon and viewing window 2026-02-01 18:00–23:00 UTC.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Snow Moon Full Moon: what to watch.",
  "captionText":"Snow Moon Full Moon: what to watch.",
  "overlayText":["Snow Moon", "Moon"],
  "strategyValidationFacts":{
    "visualSourceType":"Hybrid",
    "assetKey":"",
    "generatedRealisticPrompt":"primitive circle Moon placeholder",
    "objectVisualSource":"",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"true",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic"
  },
  "backgroundPrompt":"Moon placeholder",
  "accessibilityCues":["Moon is shown"]
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), "Snow Moon Moon visible");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Watch the Snow Moon during the evening viewing window.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nSnow Moon full Moon is visible from 18:00–23:00 UTC.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "NamedFullMoon",
            "Snow Moon Full Moon",
            "Snow Moon",
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T18:00:00Z"),
            "18:00 UTC",
            "2026-02-01 18:00–23:00 UTC",
            "eastern sky",
            "Global",
            ["Moon"],
            [],
            null,
            "Low",
            100m,
            "Snow Moon Full Moon",
            [],
            ["Moon", "full moon glow"],
            ["Use shortTitle metadata"],
            [],
            ["Venus", "Jupiter"],
            RequiredVisualObjects: ["Moon"],
            ForbiddenObjectNames: ["Venus", "Jupiter"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new NamedFullMoonSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("primitivePlaceholderUsed=true", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ProductionQualityValidator_PlanetGroupingValidatesIndividualVisibleObjectsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-planet-grouping-visible-objects-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "PlanetGrouping scene plan for Saturn, Mars, Jupiter, and Venus in the western sky.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "strategyId":"PlanetGrouping",
  "viewerTakeaway":"Four planets are visible together.",
  "captionText":"Follow the scan path across the western sky.",
  "overlayText":["Saturn", "Mars", "Jupiter", "Venus"],
  "labels":["Saturn", "Mars", "Jupiter", "Venus"],
  "requiredVisualObjects":["Saturn, Mars, Jupiter, Venus", "planet grouping", "guided scan path", "grouping arc"],
  "visibleObjects":["Saturn", "Mars", "Jupiter", "Venus"],
  "visualMotifs":["planet grouping", "guided scan path", "grouping arc"],
  "visualSourceResolution":{
    "sourceType":"Hybrid",
    "requiredDrawableObjects":["Saturn", "Mars", "Jupiter", "Venus"],
    "assetKey":"Planet.Saturn, Planet.Mars, Planet.Jupiter, Planet.Venus",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "objectVisualSources":[
      {"objectType":"Saturn", "assetKey":"Planet.Saturn", "objectVisualSource":"LocalAsset:Planet.Saturn"},
      {"objectType":"Mars", "assetKey":"Planet.Mars", "objectVisualSource":"LocalAsset:Planet.Mars"},
      {"objectType":"Jupiter", "assetKey":"Planet.Jupiter", "objectVisualSource":"LocalAsset:Planet.Jupiter"},
      {"objectType":"Venus", "assetKey":"Planet.Venus", "objectVisualSource":"LocalAsset:Planet.Venus"}
    ]
  }
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), """
{
  "reviewLabels":["Saturn", "Mars", "Jupiter", "Venus"],
  "ocrText":"Saturn Mars Jupiter Venus"
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Saturn, Mars, Jupiter, and Venus share the western sky.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), """
1
00:00:00,000 --> 00:00:05,000
Saturn, Mars, Jupiter, and Venus share the western sky.
""");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "PlanetGrouping",
            "Planet grouping over Udaipur",
            "Planet grouping",
            DateTimeOffset.Parse("2026-06-20T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T14:30:00Z"),
            "20:00 IST",
            "2026-06-20 20:00–21:30 IST",
            "western sky",
            "India",
            ["Saturn", "Mars"],
            ["Jupiter", "Venus"],
            null,
            "Low",
            20m,
            "Saturn, Mars, Jupiter, and Venus planet grouping",
            [],
            ["planet grouping", "guided scan path", "grouping arc"],
            [],
            [],
            [],
            StrategyId: "PlanetGrouping",
            ResolvedObjectNames: ["Saturn", "Mars", "Jupiter", "Venus"],
            RequiredVisualObjects: ["Saturn, Mars, Jupiter, Venus", "planet grouping", "guided scan path", "grouping arc"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.DoesNotContain(result.Errors, error => error.Contains("Saturn, Mars, Jupiter, Venus", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, error => error.Contains("planet grouping", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, error => error.Contains("guided scan path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, error => error.Contains("grouping arc", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ProductionQualityValidator_AcceptsAssetKeyWithoutObjectVisualSourceForRequiredCelestialObject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-missing-object-source-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "Mars and Jupiter close pairing scene plan with Mars and Jupiter.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Mars and Jupiter close pairing.",
  "captionText":"Mars and Jupiter close pairing.",
  "overlayText":["Mars", "Jupiter"],
  "strategyValidationFacts":{
    "visualSourceType":"ComputedAstronomyScene",
    "assetKey":"Planet.Mars, Planet.Jupiter",
    "generatedRealisticPrompt":"real-looking planet textures for Mars and Jupiter",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false",
    "allowPrimitivePlaceholder":"false",
    "primitivePlaceholderAllowed":"false",
    "celestialObjectQuality":"Realistic",
    "objectSourcePriority":"LocalAsset, ScientificAsset, AICinematic"
  },
  "accessibilityCues":["Mars and Jupiter are shown"]
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), "Mars Jupiter visible");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Mars and Jupiter are close together.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nMars and Jupiter are close together.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            "PlanetPairing",
            "Mars and Jupiter Close Pairing",
            "Mars and Jupiter",
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T18:00:00Z"),
            "18:00 UTC",
            "2026-02-01 18:00–23:00 UTC",
            "eastern sky",
            "Global",
            ["Mars"],
            ["Jupiter"],
            null,
            "Low",
            10m,
            "Mars and Jupiter close pairing",
            [],
            ["Mars", "Jupiter"],
            [],
            [],
            [],
            RequiredVisualObjects: ["Mars", "Jupiter"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
    }


    [Fact]
    public async Task ProductionQualityValidator_FailsGeminidsForbiddenTermInOverlayOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-geminids-forbidden-overlay-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", "scene-001-final.png"), "fake");
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "Geminids MeteorShower scene plan with meteor streaks, radiant hint, dark sky, and best viewing window 2026-12-14 00:00–05:00 IST.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "viewerTakeaway":"Geminids meteor shower peak.",
  "captionText":"Geminids meteor streaks from the radiant.",
  "overlayText":["Geminids", "Venus"],
  "visualSourceResolution":{
    "requiredDrawableObjects":["meteor streaks"],
    "assetKey":"Meteor.RealisticStreaks",
    "objectVisualSource":"meteor streaks:AICinematic realistic meteor streaks",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false"
  }
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), """
{ "renderedLabels":["meteor streaks","radiant","dark sky"] }
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Watch the Geminids from midnight to pre-dawn under a dark sky; meteors radiate from Gemini.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nGeminids meteor streaks are best from 00:00–05:00 IST.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy", "MeteorShower", "Geminids Meteor Shower Peak", "Geminids",
            DateTimeOffset.Parse("2026-12-14T00:00:00Z"), DateTimeOffset.Parse("2026-12-14T06:00:00Z"),
            "11:30 +05:30", "2026-12-14 00:00–05:00 IST", "east to overhead", "India",
            ["Geminids"], [], null, "Low", 10m, "Geminids Meteor Shower Peak", [], ["meteor streaks", "radiant hint", "dark sky"], [], [], ["Venus", "Jupiter"],
            ForbiddenObjectNames: ["Venus", "Jupiter"], RequiredVisualObjects: ["Meteors"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new MeteorShowerSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Venus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionQualityValidator_AcceptsMeteorRequiredObjectAliasesAndPurposeAwareScenes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase10-geminids-aliases-{Guid.NewGuid():N}");
        var sceneRoot = Path.Combine(root, "scene-approval-v3");
        Directory.CreateDirectory(sceneRoot);
        Directory.CreateDirectory(Path.Combine(sceneRoot, "short"));
        Directory.CreateDirectory(Path.Combine(sceneRoot, "long"));
        for (var i = 1; i <= 2; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(sceneRoot, "short", $"scene-{i:000}-final.png"), "fake");
            await File.WriteAllTextAsync(Path.Combine(sceneRoot, "long", $"scene-{i:000}-final.png"), "fake");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "question-driven-scene-plan.json"), "Geminids MeteorShower scene plan with meteor streaks, radiant hint, dark sky, and best viewing window 2026-12-14 00:00–05:00 IST.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), """
{
  "scenePurpose":"WHAT",
  "viewerTakeaway":"Geminids shower peaks tonight.",
  "captionText":"Meteor streaks radiate from Gemini.",
  "overlayText":["Geminids", "meteor streaks"],
  "visualSourceResolution":{
    "requiredDrawableObjects":["meteor streaks"],
    "assetKey":"Meteor.RealisticStreaks",
    "objectVisualSource":"meteor streaks:AICinematic realistic meteor streaks",
    "realisticObjectRequired":"true",
    "primitivePlaceholderUsed":"false"
  }
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-review.json"), """
{ "renderedLabels":["meteor streaks","radiant","dark sky"] }
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-002-infographic-spec.json"), """
{
  "scenePurpose":"WHEN",
  "viewerTakeaway":"Best viewing window is 2026-12-14 00:00–05:00 IST.",
  "captionText":"Geminids viewing is best after midnight under a dark sky.",
  "overlayText":["Geminids", "00:00–05:00 IST"],
  "visualSourceResolution":{
    "assetKey":"Sky.Dark",
    "objectVisualSource":"dark sky:AICinematic",
    "realisticObjectRequired":"false",
    "primitivePlaceholderUsed":"false"
  }
}
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-002-review.json"), """
{ "renderedLabels":["dark sky","radiant direction"] }
""");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-narration.txt"), "Watch the Geminids from midnight to pre-dawn under a dark sky; meteors radiate from Gemini.");
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001.srt"), "1\n00:00:00,000 --> 00:00:05,000\nGeminids meteor streaks are best from 00:00–05:00 IST.\n");

        var intelligence = new ProductionEventIntelligence(
            "Astronomy", "MeteorShower", "Geminids Meteor Shower Peak", "Geminids",
            DateTimeOffset.Parse("2026-12-14T00:00:00Z"), DateTimeOffset.Parse("2026-12-14T06:00:00Z"),
            "11:30 +05:30", "2026-12-14 00:00–05:00 IST", "east to overhead", "India",
            ["Geminids"], [], null, "Low", 10m, "Geminids Meteor Shower Peak", [], ["meteor streaks", "radiant hint", "dark sky"], [], [], ["Venus", "Jupiter"],
            ForbiddenObjectNames: ["Venus", "Jupiter"], RequiredVisualObjects: ["Meteors"]);

        var validator = new ProductionPipelineQualityValidator(new EventSceneValidationStrategyResolver([
            new MeteorShowerSceneValidationStrategy(),
            new GenericEventSceneValidationStrategy()
        ]));

        var result = await validator.ValidateBeforeVideoAssemblyAsync(intelligence, root, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        using var validation = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "production-quality-validation-before-assembly.json")));
        var diagnosticsText = validation.RootElement.GetProperty("phase10VisualSourceInputDiagnostics").GetRawText();
        Assert.Contains("requiredObjectMatchedAlias", diagnosticsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meteor streaks", diagnosticsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requiredObjectValidationSkippedBecause", diagnosticsText, StringComparison.OrdinalIgnoreCase);
    }

}
