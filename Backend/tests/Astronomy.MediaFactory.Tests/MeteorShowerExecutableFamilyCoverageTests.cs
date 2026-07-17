using System.Reflection;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using RequiredSemanticFactResolutionResult = Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.RequiredSemanticFactResolutionResult;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class MeteorShowerExecutableFamilyCoverageTests
{
    [Fact]
    public void Geminids_CanonicalParentsResolve_AndProjectLegacyChildren()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var engine = services.GetRequiredService<ISemanticResolutionEngineV1>();
        var context = ExecutableFamilySemanticCoverageV1Tests.MeteorContext();

        var identity = Resolve(engine, SemanticCapabilityVocabularyV1.EventIdentity, context);
        var name = LegacyRequiredSemanticFactCompatibilityMapper.Map(identity.Fact, "Name", null, "Required", "en");
        Assert.Equal("Geminids", name?.SpeakableValue);

        var activity = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, context);
        Assert.Contains("meteor-activity", activity.Fact.WinningAdapterId, StringComparison.OrdinalIgnoreCase);
        var radiant = LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "Radiant", null, "Required", "en");
        Assert.Equal("Gemini", radiant?.SpeakableValue);
        var peak = LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "PeakWindow", null, "Required", "en");
        Assert.Contains("December 13", peak?.SpeakableValue);

        var science = Resolve(engine, SemanticCapabilityVocabularyV1.DomainScientificKnowledge, context);
        var importance = LegacyRequiredSemanticFactCompatibilityMapper.Map(science.Fact, "ScientificImportance", null, "Required", "en");
        Assert.DoesNotContain("planet", importance?.SpeakableValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("debris stream", importance?.SpeakableValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Perseids_UsesSameFamilyAdaptersAndProjectionRules_AsGeminids()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var engine = services.GetRequiredService<ISemanticResolutionEngineV1>();
        var geminids = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, ExecutableFamilySemanticCoverageV1Tests.MeteorContext("Geminids", "Gemini"));
        var perseids = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, ExecutableFamilySemanticCoverageV1Tests.MeteorContext("Perseids", "Perseus"));
        Assert.Equal(geminids.Fact.WinningAdapterId, perseids.Fact.WinningAdapterId);
        Assert.Equal(geminids.Fact.WinningSourceId, perseids.Fact.WinningSourceId);
        Assert.Equal("Gemini", LegacyRequiredSemanticFactCompatibilityMapper.Map(geminids.Fact, "Radiant", null, "Required", "en")?.SpeakableValue);
        Assert.Equal("Perseus", LegacyRequiredSemanticFactCompatibilityMapper.Map(perseids.Fact, "Radiant", null, "Required", "en")?.SpeakableValue);
    }


    [Fact]
    public void RealProductionComposition_GeminidsMeteorActivityResolvesAndProjects()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var adapter = Assert.Single(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.MeteorActivity)));
        Assert.Equal("v1.meteor-activity.production-event-intelligence", adapter.AdapterId);
        Assert.Equal(SemanticSourcePolicyVocabularyV1.ProductionEventIntelligence, adapter.SourceId);

        var v1Profile = new AstronomyFamilyProfileCatalogV1().GetRequired("MeteorShower");
        var compatibility = new AstronomyFamilyProfileV1CompatibilityAdapter().Convert(v1Profile, new FamilyProfileCompatibilityContext("MeteorShower", "MeteorShower", "MeteorShower", false));
        var profile = Assert.IsType<AstronomyFamilyProfile>(compatibility.LegacyProfile);
        var request = new ContentPlanProductionPipelineRequest(
            PlanId: Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"), Category: "Astronomy", Title: "Geminids Meteor Shower Peak", ShortTitle: "Geminids", EventType: "MeteorShower", RegionId: "US", Language: "en", PrimaryObjects: ["Geminids"], SecondaryObjects: ["Meteors"], StartUtc: DateTimeOffset.Parse("2026-12-13T00:00:00Z"), PeakUtc: DateTimeOffset.Parse("2026-12-14T07:00:00Z"), EndUtc: DateTimeOffset.Parse("2026-12-15T12:00:00Z"), ScheduledUtc: DateTimeOffset.Parse("2026-12-13T12:00:00Z"), SourceExternalEventId: "geminids-2026", PlannedFormat: "long", RequestedOutputs: ["long", "short"], VisibilityScore: 90, RarityScore: 70, AudienceInterestScore: 85, ContentOpportunityScore: 90, VerificationStatus: "Verified", VerificationSource: "ProductionParityTest", ContentStrategy: "MeteorShower", LocalPeakTime: "after midnight", SkyDirectionHint: "east to overhead", VisibilityRegion: "United States", MoonInterference: "low moon interference", BestViewingWindowLocal: "midnight to pre-dawn", RadiantVisibilityNote: "Moonlight estimate computed by Skyfield at the provided meteor peak instant.", MoonIlluminationPercent: 10m, RecommendedPublishWindow: null, RecommendedContentTypes: [], Warnings: [], SourceNotes: [], TimeZone: "America/New_York", AngularSeparationDegrees: null);
        var input = new RequiredSemanticFactResolutionInput(profile, Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), null, null, Json("{\"eventType\":\"MeteorShower\"}"), null, null, LanguageProfileResolver.Resolve("en"), request, CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("MeteorShower", "MeteorShower", "MeteorShower", [], "MeteorShower")));
        var method = resolver.GetType().GetMethod("CreateAdapterContext", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var context = Assert.IsType<SemanticSourceAdapterContextV1>(method!.Invoke(resolver, [input]));
        Assert.NotNull(context.ProductionEventIntelligence?.MeteorActivity);

        var activity = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, context);
        Assert.Contains(adapter.AdapterId, activity.Diagnostics.InvokedAdapterIds);
        Assert.True(activity.Diagnostics.CandidateCount > 0);
        var value = Assert.IsType<MeteorActivityValue>(activity.Fact.TypedValue!.Value);
        Assert.Equal("Gemini", value.RadiantConstellation);
        Assert.NotNull(value.PeakWindow);
        Assert.Equal("Gemini", LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "Radiant", null, "Required", "en")?.SpeakableValue);
        Assert.False(string.IsNullOrWhiteSpace(LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "PeakWindow", null, "Required", "en")?.SpeakableValue));
    }

    [Fact]
    public void RequiredSemanticFactResolver_MeteorShowerRequestsMeteorActivity()
    {
        var result = ResolveProductionGeminids();
        var json = System.Text.Json.JsonSerializer.Serialize(result.Diagnostics);
        Assert.Contains("MeteorActivity", json);
        Assert.DoesNotContain("canonicalCapabilityRequested\":\"Radiant", json);
        Assert.DoesNotContain("canonicalCapabilityRequested\":\"PeakWindow", json);
        Assert.Contains("legacyRequiredFact\":\"Radiant", json);
        Assert.Contains("legacyRequiredFact\":\"PeakWindow", json);
    }

    [Fact]
    public void RequiredSemanticFactResolver_MeteorActivityProjectsRadiantAndPeakWindow()
    {
        var result = ResolveProductionGeminids();
        var facts = result.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)).ToArray();
        var radiant = Assert.Single(facts.Where(f => f.FactType == "Radiant").DistinctBy(f => f.FactType));
        var peak = Assert.Single(facts.Where(f => f.FactType == "PeakWindow").DistinctBy(f => f.FactType));
        Assert.Equal("Gemini", radiant.SpeakableValue);
        Assert.False(string.IsNullOrWhiteSpace(peak.SpeakableValue));
        Assert.Equal("MeteorActivity", radiant.SemanticMeaning);
        Assert.Equal("MeteorActivity", peak.SemanticMeaning);
        Assert.Equal("V1Projection.MeteorActivity.Radiant", radiant.DerivationRuleId);
        Assert.Equal("V1Projection.MeteorActivity.PeakWindow", peak.DerivationRuleId);
        Assert.NotEmpty(radiant.SourceInputs ?? []);
        Assert.NotEmpty(peak.SourceInputs ?? []);
    }

    private static RequiredSemanticFactResolutionResult ResolveProductionGeminids()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("MeteorShower", null, null, null, null, null)).Profile;
        var request = new ContentPlanProductionPipelineRequest(
            PlanId: Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"), Category: "Astronomy", Title: "Geminids Meteor Shower Peak", ShortTitle: "Geminids", EventType: "MeteorShower", RegionId: "US", Language: "en", PrimaryObjects: ["Geminids"], SecondaryObjects: ["Meteors"], StartUtc: DateTimeOffset.Parse("2026-12-13T00:00:00Z"), PeakUtc: DateTimeOffset.Parse("2026-12-14T07:00:00Z"), EndUtc: DateTimeOffset.Parse("2026-12-15T12:00:00Z"), ScheduledUtc: DateTimeOffset.Parse("2026-12-13T12:00:00Z"), SourceExternalEventId: "geminids-2026", PlannedFormat: "long", RequestedOutputs: ["long", "short"], VisibilityScore: 90, RarityScore: 70, AudienceInterestScore: 85, ContentOpportunityScore: 90, VerificationStatus: "Verified", VerificationSource: "ProductionParityTest", ContentStrategy: "MeteorShower", LocalPeakTime: "after midnight", SkyDirectionHint: "east to overhead", VisibilityRegion: "United States", MoonInterference: "low moon interference", BestViewingWindowLocal: "midnight to pre-dawn", RadiantVisibilityNote: null, MoonIlluminationPercent: 10m, RecommendedPublishWindow: null, RecommendedContentTypes: [], Warnings: [], SourceNotes: [], TimeZone: "America/New_York", AngularSeparationDegrees: null);
        return resolver.Resolve(new RequiredSemanticFactResolutionInput(profile, Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), null, null, Json("{\"eventType\":\"MeteorShower\"}"), null, null, LanguageProfileResolver.Resolve("en"), request, CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("MeteorShower", "MeteorShower", "MeteorShower", [], "MeteorShower"))));
    }

    private static System.Text.Json.JsonElement Json(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    private static SemanticResolutionResultV1 Resolve(ISemanticResolutionEngineV1 engine, string capability, Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts.SemanticSourceAdapterContextV1 context)
    {
        var result = engine.Resolve(new SemanticResolutionRequestV1(new SemanticCapabilityId(capability), true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>(), context, "MeteorShower"));
        Assert.True(result.Fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination, result.Fact.DiagnosticMessage);
        return result;
    }
}
