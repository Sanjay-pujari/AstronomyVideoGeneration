using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Diagnostics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7ObjectKnowledgeProductionCompositionTests
{
    [Fact]
    public void ProductionComposition_OrionPhase7_UsesObjectKnowledgeAggregateProjection()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>();
        Assert.IsType<RequiredSemanticFactResolver>(generator.RuntimeRequiredSemanticFactResolver);

        var request = new ContentPlanProductionPipelineRequest(Guid.NewGuid(), "Astronomy", "Orion constellation", "Orion", "Constellation", "US", "en", ["Orion"], [], null, null, null, null, "orion", "long", ["long", "short"], 90, 80, 90, 90, "Verified", "Test", "Constellation", null, null, null, "United States", null, null, null, null, [], [], "UTC", null);
        var input = new RequiredSemanticFactResolutionInput(
            AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("Constellation", null, null, null, null, null)).Profile,
            Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"long-orientation\",\"narrativeRole\":\"Orientation\",\"allocatedFacts\":{}}]}"),
            Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"short-orientation\",\"narrativeRole\":\"Orientation\",\"allocatedFacts\":{}}]}"),
            null, null, Json("{\"eventType\":\"Constellation\"}"), null, null, LanguageProfileResolver.Resolve("en"), request,
            CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("Constellation", "Constellation", "Constellation", [], "Constellation")));

        var result = generator.RuntimeRequiredSemanticFactResolver.Resolve(input);
        var diagnostics = JsonSerializer.Serialize(result.Diagnostics);

        Assert.Equal("ObjectKnowledgeAggregateProjectionV1", RuntimeCompositionDiagnostics.ObjectKnowledgeAggregateProjectionVersion);
        Assert.Contains("\"aggregateProjectionBranchEntered\":true", diagnostics.Replace(" ", string.Empty));
        Assert.Contains("\"projectionSucceeded\":true", diagnostics.Replace(" ", string.Empty));
        Assert.Contains("\"factAddedToBeat\":true", diagnostics.Replace(" ", string.Empty));
        Assert.Contains("\"factMatchedDuringRequirednessCheck\":true", diagnostics.Replace(" ", string.Empty));
        Assert.DoesNotContain(result.Beats.SelectMany(b => b.MissingRequiredFacts), m => m.Contains("ObjectKnowledge", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
