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
            PlanId: Guid.NewGuid(),
            Category: "Astronomy",
            Title: "Jupiter Venus conjunction over Udaipur",
            ShortTitle: "Jupiter + Venus",
            EventType: "PLANET_CONJUNCTION",
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PrimaryObjects: ["Jupiter"],
            SecondaryObjects: ["Venus"],
            StartUtc: DateTimeOffset.Parse("2026-08-12T13:00:00Z"),
            PeakUtc: DateTimeOffset.Parse("2026-08-12T14:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-08-12T15:00:00Z"),
            ScheduledUtc: DateTimeOffset.Parse("2026-08-11T10:00:00Z"),
            SourceExternalEventId: "source-event-1",
            PlannedFormat: "long",
            RequestedOutputs: ["long"],
            VisibilityScore: 90,
            RarityScore: 70,
            AudienceInterestScore: 80,
            ContentOpportunityScore: 85,
            VerificationStatus: "Verified",
            VerificationSource: "UnitTest",
            ContentStrategy: "PlanetPairing",
            LocalPeakTime: null,
            SkyDirectionHint: null,
            VisibilityRegion: null,
            MoonInterference: null,
            BestViewingWindowLocal: null,
            RadiantVisibilityNote: null,
            MoonIlluminationPercent: null,
            RecommendedPublishWindow: null,
            RecommendedContentTypes: [],
            Warnings: [],
            SourceNotes: [],
            TimeZone: "Asia/Kolkata",
            AngularSeparationDegrees: 1.63m);
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("PLANET_CONJUNCTION", null, null, null)).Profile;
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(request.EventType, null, null, [], null));
        var contract = Json("{\"beats\":[{\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"timing\",\"narrativeRole\":\"Timing\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"observation\",\"narrativeRole\":\"Observation\",\"allocatedFacts\":{}},{\"documentaryBeatId\":\"science\",\"narrativeRole\":\"Science\",\"allocatedFacts\":{}}]}");

        var result = new RequiredSemanticFactResolver().Resolve(new RequiredSemanticFactResolutionInput(
            FamilyProfile: profile,
            LongDocumentaryContract: contract,
            ShortDocumentaryContract: contract,
            EditorialContract: null,
            StoryGraph: null,
            ProductionEventIntelligence: null,
            ObservationMetadata: null,
            QuestionAnswerSet: null,
            LanguageProfile: LanguageProfileResolver.Resolve("en"),
            ProductionPipelineRequest: request,
            CanonicalEventIdentity: identity));
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
