using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7SemanticSourceContextIntegrationTests
{
    [Fact]
    public void JupiterVenusTypedProductionRequestWiresSemanticSourceContext()
    {
        var request = new ContentPlanProductionPipelineRequest(
            Guid.NewGuid(), "Astronomy", "Jupiter Venus conjunction over Udaipur", "Jupiter + Venus", "PLANET_CONJUNCTION", "IN-RJ-UDAIPUR", "en",
            ["Jupiter"], ["Venus"],
            DateTimeOffset.Parse("2026-08-12T13:00:00Z"), DateTimeOffset.Parse("2026-08-12T14:00:00Z"), DateTimeOffset.Parse("2026-08-12T15:00:00Z"), DateTimeOffset.Parse("2026-08-11T10:00:00Z"),
            "source-event-1", "long", ["long"], 90, 70, 80, 85, "Verified", "UnitTest", "PlanetPairing", null, null, null, null, null, null, null, null, null, [], [], [], "Asia/Kolkata", 1.63m);
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("PLANET_CONJUNCTION", null, null, null)).Profile;
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(request.EventType, null, null, [], null));
        var contract = Json("{\"beats\":[{\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"timing\",\"narrativeRole\":\"Timing\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"observation\",\"narrativeRole\":\"Observation\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"science\",\"narrativeRole\":\"Science\",\"allocatedFacts\":{}}]}");

        var result = new RequiredSemanticFactResolver().Resolve(new RequiredSemanticFactResolutionInput(profile, contract, contract, null, null, null, null, null, LanguageProfileResolver.Resolve("en"), request, identity));
        var facts = result.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)).ToArray();

        Assert.Equal("PlanetPairing", identity.EventType);
        Assert.Equal("PlanetPairing", profile.FamilyId);
        Assert.Contains(facts, f => f.FactType == "PrimaryObjects" && f.CanonicalValue.ToString()!.Contains("Jupiter") && f.CanonicalValue.ToString()!.Contains("Venus"));
        Assert.Contains(facts, f => f.FactType == "EventIdentity");
        Assert.Contains(facts, f => f.FactType == "ObservationTiming" && f.SourceField.Contains("EventWindow"));
        Assert.Contains(facts, f => f.FactType == "ApparentAlignmentExplanation");
        Assert.Contains(facts, f => f.FactType == "PhysicalProximityClarification");
        Assert.Contains(facts, f => f.FactType == "LocationContext" && f.SourceField.Contains("ObservationLocation"));
        Assert.Contains(result.Beats.SelectMany(b => b.MissingRequiredFacts), f => f == "ObservationDirection");
        Assert.DoesNotContain(facts, f => f.FactType == "ObservationDirection");
        Assert.Contains(result.Beats.SelectMany(b => b.CapabilityResolutions), r => r.Capability == "AstronomicalObjects" && r.Candidates.Any(c => c.Source == "ProductionEventIntelligence"));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
