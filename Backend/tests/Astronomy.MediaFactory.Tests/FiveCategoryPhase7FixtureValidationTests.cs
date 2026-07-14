using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class FiveCategoryPhase7FixtureValidationTests
{
    public static IEnumerable<object[]> Fixtures => Phase7ProductionFixtures.All.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ActualRawEventCodeResolvesCanonicallyToActiveFamilyAndTypedContext(Phase7ProductionFixture fixture)
    {
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(fixture.Request.EventType, fixture.Request.Category, fixture.Request.PlannedFormat, fixture.Request.BestViewingWindowLocal)).Profile;
        var v1Identity = new Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity.CanonicalAstronomyEventIdentityResolverV1().Resolve(fixture.Request.EventType, fixture.ProductionSource);
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(fixture.Request.EventType, fixture.Request.EventType, fixture.Request.EventType, [], null));
        var contract = Json("{\"beats\":[{\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"timing\",\"narrativeRole\":\"Timing\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"observation\",\"narrativeRole\":\"Observation\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"science\",\"narrativeRole\":\"Science\",\"allocatedFacts\":{}}]}");

        var result = new RequiredSemanticFactResolver().Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, Json("{\"editorialContractId\":\"fixture-editorial\"}"), null, null, Json("{\"source\":\"fixture-observation-metadata\"}"), null, LanguageProfileResolver.Resolve(fixture.Request.Language), fixture.Request, identity));
        var serializedDiagnostics = JsonSerializer.Serialize(result.Diagnostics);

        Assert.Equal(fixture.ExpectedCanonicalIdentity, v1Identity.CanonicalEventType);
        Assert.Equal(fixture.ExpectedFamily, v1Identity.CanonicalFamily);
        Assert.False(string.IsNullOrWhiteSpace(profile.FamilyId));
        Assert.Contains("sourceContextPresence", serializedDiagnostics);
        Assert.Contains("productionPipelineRequest", serializedDiagnostics);
        Assert.Contains("canonicalEventIdentity", serializedDiagnostics);
        Assert.Contains(result.Beats.SelectMany(b => b.CapabilityResolutions), r => r.Candidates.Count > 0 || r.RejectedSources.Count > 0 || r.Status.Contains("Unsupported", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

public sealed record Phase7ProductionFixture(string EventName, string ProductionSource, ContentPlanProductionPipelineRequest Request, string ExpectedCanonicalIdentity, string ExpectedFamily);

public static class Phase7ProductionFixtures
{
    public static IReadOnlyList<Phase7ProductionFixture> All { get; } =
    [
        Fixture("Jupiter–Venus conjunction", "production planning path: PLANET_CONJUNCTION detection/content-plan request", "PLANET_CONJUNCTION", "PlanetPairing", ["Jupiter"], ["Venus"], "IN-RJ-UDAIPUR", "Asia/Kolkata", 1.63m),
        Fixture("Mars–Jupiter conjunction", "production planning path: PLANET_CONJUNCTION detection/content-plan request", "PLANET_CONJUNCTION", "PlanetPairing", ["Mars"], ["Jupiter"], "US-CA-SF", "America/Los_Angeles", 0.75m),
        Fixture("Geminids meteor shower", "production planning path: Meteor Shower content-plan request", "Meteor Shower", "MeteorShower", ["Geminids Meteor Shower"], [], "GLOBAL", "UTC", null),
        Fixture("Wolf Moon / Named Full Moon", "production planning path: Named Full Moon content-plan request", "Named Full Moon", "NamedFullMoon", ["Moon"], [], "GLOBAL", "UTC", null),
        Fixture("Solar Eclipse", "production planning path: ManualContentPlanCreation solar eclipse request", "Solar Eclipse", "SolarEclipse", ["Sun", "Moon"], [], "US-TX", "America/Chicago", null)
    ];

    private static Phase7ProductionFixture Fixture(string name, string source, string rawCode, string family, IReadOnlyList<string> primary, IReadOnlyList<string> secondary, string region, string timezone, decimal? separation)
    {
        var id = Guid.NewGuid();
        var request = new ContentPlanProductionPipelineRequest(id, "Astronomy", name, name, rawCode, region, "en", primary, secondary, DateTimeOffset.Parse("2026-08-12T01:00:00Z"), DateTimeOffset.Parse("2026-08-12T02:00:00Z"), DateTimeOffset.Parse("2026-08-12T03:00:00Z"), DateTimeOffset.Parse("2026-08-10T12:00:00Z"), $"fixture-{id:N}", "long", ["long", "short"], 80, 70, 75, 77, "Verified", source, family, null, name.Contains("conjunction", StringComparison.OrdinalIgnoreCase) ? "western sky" : null, region, null, "after sunset", name.Contains("Geminids", StringComparison.OrdinalIgnoreCase) ? "radiant only when typed source supplies it" : null, null, null, ["Short", "Long"], [], [source], timezone, separation);
        return new(name, source, request, family, family);
    }
}
