using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventProductionIntelligenceTests
{
    [Fact]
    public void AstronomyAdapter_NormalizesMeteorShowerWithoutGeminidsHardcoding()
    {
        var adapter = new AstronomyEventProductionIntelligenceAdapter(new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
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
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ];

        Assert.Contains(strategies, s => s is MeteorShowerStrategy);
        Assert.Contains(strategies, s => s is PlanetPairingStrategy);
        Assert.Contains(strategies, s => s is ConjunctionStrategy);
        Assert.Contains(strategies, s => s is NamedFullMoonStrategy);
        Assert.Contains(strategies, s => s is NewMoonStrategy);
        Assert.Contains(strategies, s => s is LunarEclipseStrategy);
        Assert.Contains(strategies, s => s is SolarEclipseStrategy);
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
        await File.WriteAllTextAsync(Path.Combine(sceneRoot, "scene-001-infographic-spec.json"), "{\"title\":\"Geminids Meteor Shower Peak\",\"visual\":\"meteor streaks across a dark sky with radiant hint\",\"time\":\"2026-12-14 00:00–05:00 IST\"}");
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
  "resolver":{
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
    }

}
