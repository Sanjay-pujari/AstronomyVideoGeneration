using System.Collections.Immutable;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Evaluation;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Event;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7EventIdentityProductionResolutionTests
{
    private const string ExternalEventId = "PLANET_CONJUNCTION_20260605_20260613_IN_RJ_UDAIPUR_JUPITER_AND_VENUS";

    [Fact]
    public void ProductionEngine_Resolves_EventIdentity_From_Real_Api_Context()
    {
        var policies = new SemanticSourcePolicyCatalogV1();
        var registry = new SemanticSourceAdapterRegistryV1([new EventIdentitySourceAdapterV1()]);
        var collector = new SemanticCandidateCollectorV1(policies, registry);
        var engine = new SemanticResolutionEngineV1(collector, new SemanticCandidateEvaluatorV1(), new SemanticConflictAnalyzerV1(), new SemanticCandidateSelectorV1());
        var request = BuildEventIdentityRequest();

        var collection = collector.Collect(request);
        var result = engine.Resolve(request);

        Assert.Contains("v1.event-identity.event-identity-context", collection.InvokedAdapterIds);
        Assert.True(collection.Candidates.Length > 0);
        Assert.True(result.Diagnostics.EligibleCandidateCount > 0);
        Assert.NotNull(result.Fact.TypedValue);
        Assert.True(result.Fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination);
        Assert.False(string.IsNullOrWhiteSpace(result.Fact.CanonicalValue));
        Assert.False(string.IsNullOrWhiteSpace(result.Fact.Provenance.First().SourceId));
        Assert.False(string.IsNullOrWhiteSpace(result.Fact.Provenance.First().SourcePropertyPath));
    }

    [Fact]
    public void Phase7_ApiComposition_Resolves_Required_EventIdentity()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var result = resolver.Resolve(BuildResolverInput());

        var allRequired = result.Beats.SelectMany(b => b.RequiredFacts).Where(f => f.FactType == "EventIdentity").ToArray();
        Assert.NotEmpty(allRequired);
        Assert.All(result.Beats, b => Assert.DoesNotContain("EventIdentity", b.MissingRequiredFacts));
        Assert.All(allRequired, f => Assert.False(string.IsNullOrWhiteSpace(f.CanonicalValue?.ToString())));

        var json = JsonSerializer.Serialize(result.Diagnostics);
        using var doc = JsonDocument.Parse(json);
        var eventIdentity = doc.RootElement.GetProperty("requiredFactResultDiagnostics").EnumerateArray()
            .Where(e => e.GetProperty("canonicalCapabilityId").GetString() == "EventIdentity")
            .ToArray();
        Assert.NotEmpty(eventIdentity);
        Assert.Single(eventIdentity.Select(e => e.GetProperty("finalDiagnostic").GetString()).Distinct());
        Assert.All(eventIdentity, e => Assert.Equal("Resolved", e.GetProperty("finalResolutionStatus").GetString()));
    }

    private static SemanticResolutionRequestV1 BuildEventIdentityRequest()
    {
        var context = BuildAdapterContext();
        Assert.NotNull(context.EventIdentity);
        Assert.Equal("PlanetPairing", context.EventIdentity.CanonicalEventType);
        Assert.Equal("PlanetPairing", context.EventIdentity.FamilyId);
        Assert.Equal("PLANET_CONJUNCTION", context.EventIdentity.SourceEventType);
        Assert.Equal(ExternalEventId, context.EventIdentity.SourceEventId);
        Assert.Equal("Jupiter", Assert.Single(context.EventIdentity.PrimaryObjects).Name);
        Assert.Equal("Venus", Assert.Single(context.EventIdentity.SecondaryObjects).Name);
        Assert.Equal("IN-RJ-UDAIPUR", context.EventIdentity.RegionId);
        Assert.Equal("en", context.EventIdentity.Language);
        return new SemanticResolutionRequestV1(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.EventIdentity), true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Strong, [SemanticEvidenceCategoryV1.EventIdentityContext, SemanticEvidenceCategoryV1.VerifiedEventData], context, "PlanetPairing", "long", null);
    }

    private static SemanticSourceAdapterContextV1 BuildAdapterContext()
    {
        var primary = new[] { Obj("Jupiter", "Primary") }.ToImmutableArray();
        var secondary = new[] { Obj("Venus", "Secondary") }.ToImmutableArray();
        var identity = new CanonicalAstronomyEventIdentity("PlanetPairing", "PlanetPairing", "PlanetPairing", "PLANET_CONJUNCTION", "ProductionPipelineRequest.EventType", ExternalEventId, primary, secondary, "IN-RJ-UDAIPUR", "en");
        var window = new EventWindowValue(DateTimeOffset.Parse("2026-06-05T00:00:00Z"), DateTimeOffset.Parse("2026-06-09T00:00:00Z"), DateTimeOffset.Parse("2026-06-13T00:00:00Z"), null, null, null, null, "Asia/Kolkata", "June 5–13, 2026 in Asia/Kolkata");
        return new SemanticSourceAdapterContextV1(identity, new ProductionEventIntelligenceSourceV1("PLANET_CONJUNCTION", "PlanetPairing", "PlanetPairing", primary, secondary, window, new AngularSeparationValue(1.63m, null, null, null, "apparent pairing", DateTimeOffset.Parse("2026-06-09T00:00:00Z")), Verified: true), new ObservationMetadataSourceV1(window, ObservationLocation: new ObservationLocationValue("IN-RJ-UDAIPUR", null, null, null, "Asia/Kolkata"), Verified: true), Language: "en", TimeZone: "Asia/Kolkata", LocationContext: new ObservationLocationValue("IN-RJ-UDAIPUR", null, null, null, "Asia/Kolkata"));
    }

    private static RequiredSemanticFactResolutionInput BuildResolverInput()
    {
        var request = new ContentPlanProductionPipelineRequest(Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"), "Astronomy", "Jupiter and Venus over Udaipur", "Jupiter Venus", "PLANET_CONJUNCTION", "IN-RJ-UDAIPUR", "en", ["Jupiter"], ["Venus"], DateTimeOffset.Parse("2026-06-05T00:00:00Z"), DateTimeOffset.Parse("2026-06-09T00:00:00Z"), DateTimeOffset.Parse("2026-06-13T00:00:00Z"), null, ExternalEventId, "long", ["long", "short"], 90, 70, 80, 85, "Verified", "Production API", "Conjunction", null, null, "IN-RJ-UDAIPUR", null, "June 5–13, 2026", null, null, null, [], [], "Asia/Kolkata", 1.63m);
        var profile = new AstronomyFamilyProfile("PlanetPairing", "Event", "standard", "standard", ["EventIdentity"], [], ["Hook"], ["Hook"], "", "", [], [], []);
        var beats = JsonDocument.Parse("{\"eventType\":\"PLANET_CONJUNCTION\",\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}").RootElement.Clone();
        var intelligence = JsonDocument.Parse("{\"eventType\":\"PLANET_CONJUNCTION\"}").RootElement.Clone();
        var observation = JsonDocument.Parse("{\"verificationStatus\":\"Verified\"}").RootElement.Clone();
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(request.EventType, request.EventType, request.EventType, [], request.EventType));
        return new RequiredSemanticFactResolutionInput(profile, beats, beats, null, null, intelligence, observation, null, LanguageProfileResolver.Resolve("en"), request, identity);
    }

    private static AstronomicalObjectValue Obj(string name, string role) => new(name, null, role, null, [new SemanticSourceProvenanceV1(SemanticSourcePolicyVocabularyV1.ProductionEventIntelligence, nameof(ContentPlanProductionPipelineRequest), $"ProductionPipelineRequest.{role}Objects", true)]);
}
