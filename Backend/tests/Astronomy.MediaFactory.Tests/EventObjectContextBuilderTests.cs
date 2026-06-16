using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventObjectContextBuilderTests
{
    [Theory]
    [InlineData("PlanetConjunction", new[] { "Jupiter", "Venus" }, "JUPITER + VENUS")]
    [InlineData("MoonPlanetPairing", new[] { "Moon", "Venus" }, "MOON + VENUS")]
    [InlineData("PlanetConjunction", new[] { "Mars", "Jupiter" }, "MARS + JUPITER")]
    [InlineData("PlanetGrouping", new[] { "Mercury", "Venus", "Mars" }, "MERCURY + VENUS + MARS")]
    [InlineData("PlanetGrouping", new[] { "Mercury", "Venus", "Mars", "Jupiter" }, "MERCURY + VENUS + MARS + MORE")]
    public void BuildsDynamicHeadlinesFromResolvedObjectNames(string eventType, string[] resolvedObjectNames, string expectedHeadline)
    {
        var context = EventObjectContextBuilder.FromJsonValues(eventType, null, resolvedObjectNames, [], [], []);

        Assert.Equal(expectedHeadline, context.ObjectHeadlineText);
        Assert.Equal(resolvedObjectNames, context.ObjectNames);
        Assert.True(context.ObjectNameValidationPassed);
    }

    [Fact]
    public void RemovesViewerInstructionSentencesFromObjectNames()
    {
        var context = EventObjectContextBuilder.FromJsonValues(
            "PlanetConjunction",
            null,
            ["Look for Jupiter and Venus close together.", "Look toward the western sky after sunset.", "Mars", "Jupiter"],
            [],
            [],
            []);

        Assert.Equal(["Mars", "Jupiter"], context.ObjectNames);
        Assert.Contains(context.RemovedInvalidObjectNameCandidates, v => v.Contains("Look for", StringComparison.OrdinalIgnoreCase));
        Assert.False(context.ObjectNameValidationPassed);
    }

    [Fact]
    public void MeteorShowerUsesShowerNameWithoutPlanetPairHeadline()
    {
        var context = EventObjectContextBuilder.FromJsonValues("MeteorShower", "Geminids Meteor Shower", [], [], [], []);

        Assert.Equal("GEMINIDS METEOR SHOWER", context.ObjectHeadlineText);
        Assert.DoesNotContain("+", context.ObjectHeadlineText);
        Assert.False(context.HasPlanet);
    }
}
