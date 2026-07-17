using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class MeteorShowerProductionParityTests(ITestOutputHelper output)
{
    [Fact]
    public void GeminidsProductionRequest_CharacterizesMeteorActivityLifecycleThroughSemanticResolution()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        var registry = scope.ServiceProvider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var input = BuildProductionInput();
        var context = (SemanticSourceAdapterContextV1)resolver.GetType().GetMethod("CreateAdapterContext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(resolver, [input])!;
        var adapters = registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.MeteorActivity)).ToArray();
        var adapterResults = adapters.Select(adapter => new { adapter.AdapterId, Result = adapter.TryExtract(context) }).ToArray();
        var canonical = engine.Resolve(new SemanticResolutionRequestV1(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.MeteorActivity), true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>().ToImmutableArray(), context, "MeteorShower"));
        var projected = new[] { "Radiant", "PeakWindow" }
            .Select(fact => new { Fact = fact, Projection = LegacyRequiredSemanticFactCompatibilityMapper.Map(canonical.Fact, fact, null, "Required", "en") })
            .ToArray();
        var resolverResult = resolver.Resolve(input);
        var retained = resolverResult.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)).Where(f => string.Equals(f.SemanticMeaning, "MeteorActivity", StringComparison.OrdinalIgnoreCase)).ToArray();

        var snapshot = new
        {
            adapterContext = new
            {
                meteorActivityPresent = context.ProductionEventIntelligence?.MeteorActivity is not null,
                context.EventIdentity?.SourceEventType,
                context.EventIdentity?.SourceEventId,
                contentStrategy = input.ProductionPipelineRequest?.ContentStrategy,
                context.TimeZone,
                primaryObjects = context.ProductionEventIntelligence?.PrimaryObjects.Select(o => o.Name).ToArray() ?? [],
                secondaryObjects = context.ProductionEventIntelligence?.SecondaryObjects.Select(o => o.Name).ToArray() ?? []
            },
            adaptersExecuted = adapterResults.Select(a => a.AdapterId).ToArray(),
            candidatesProduced = adapterResults.Count(a => a.Result.Candidate is not null),
            canonicalCapabilityResult = new { canonical.Fact.Status, canonical.Fact.WinningAdapterId, canonical.Diagnostics.CandidateCount },
            projectedLegacyFacts = projected.Select(p => new { p.Fact, Present = p.Projection is not null, p.Projection?.SpeakableValue, p.Projection?.DerivationRuleId }).ToArray(),
            retainedBeatFacts = retained.Select(f => new { f.FactType, f.SpeakableValue, f.DerivationRuleId }).ToArray()
        };
        output.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotNull(context.ProductionEventIntelligence?.MeteorActivity);
        Assert.Contains(adapterResults, a => string.Equals(a.AdapterId, "v1.meteor-activity.production-event-intelligence", StringComparison.Ordinal));
        Assert.True(adapterResults.Any(a => a.Result.Candidate is not null), "Expected the production MeteorActivity adapter to produce a candidate.");
        Assert.Equal(SemanticResolutionStatusV1.Resolved, canonical.Fact.Status);
        Assert.All(projected, p => Assert.NotNull(p.Projection));
        Assert.Contains(retained, f => f.FactType == "Radiant");
        Assert.Contains(retained, f => f.FactType == "PeakWindow");
    }

    private static RequiredSemanticFactResolutionInput BuildProductionInput()
    {
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("MeteorShower", null, null, null, null, null)).Profile;
        var request = new ContentPlanProductionPipelineRequest(
            PlanId: Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"),
            Category: "RareEventAlert",
            Title: "Geminids Meteor Shower Peak",
            ShortTitle: "Geminids",
            EventType: "MeteorShower",
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PrimaryObjects: ["Geminids"],
            SecondaryObjects: ["Meteors"],
            StartUtc: DateTimeOffset.Parse("2026-12-13T00:00:00Z"),
            PeakUtc: DateTimeOffset.Parse("2026-12-14T07:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-12-15T12:00:00Z"),
            ScheduledUtc: DateTimeOffset.Parse("2026-12-13T12:00:00Z"),
            SourceExternalEventId: "meteor-shower-geminids-2026",
            PlannedFormat: "long",
            RequestedOutputs: ["long", "short"],
            VisibilityScore: 90,
            RarityScore: 70,
            AudienceInterestScore: 85,
            ContentOpportunityScore: 90,
            VerificationStatus: "Approximate",
            VerificationSource: "ProductionParityTest",
            ContentStrategy: "LocalViewingGuide",
            LocalPeakTime: null,
            SkyDirectionHint: null,
            VisibilityRegion: "IN-RJ-UDAIPUR",
            MoonInterference: null,
            BestViewingWindowLocal: null,
            RadiantVisibilityNote: null,
            MoonIlluminationPercent: null,
            RecommendedPublishWindow: null,
            RecommendedContentTypes: [],
            Warnings: [],
            SourceNotes: [],
            TimeZone: null,
            AngularSeparationDegrees: null);
        return new RequiredSemanticFactResolutionInput(profile, Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), null, null, Json("{\"eventType\":\"MeteorShower\"}"), null, null, LanguageProfileResolver.Resolve("en"), request, CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("MeteorShower", "RareEventAlert", "MeteorShower", [], "MeteorShower")));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
