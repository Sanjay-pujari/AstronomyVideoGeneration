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
}
