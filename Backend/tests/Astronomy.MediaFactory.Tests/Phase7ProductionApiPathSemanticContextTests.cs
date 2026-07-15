using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7ProductionApiPathSemanticContextTests
{
    [Fact]
    public async Task RealPhase7EntryPoint_PreservesTypedSemanticSources()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>();
        var catalog = scope.ServiceProvider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        var registry = scope.ServiceProvider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        Assert.NotEmpty(catalog.Policies);
        Assert.NotEmpty(registry.Adapters);
        Assert.IsType<SemanticResolutionEngineV1>(engine);

        var outputRoot = Path.Combine(Path.GetTempPath(), "phase7-parity-" + Guid.NewGuid().ToString("N"));
        WriteProductionArtifacts(outputRoot);
        var productionRequest = BuildJupiterVenusRequest();
        var request = new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: productionRequest.RegionId,
            Language: productionRequest.Language,
            DryRun: false,
            UseProductionPipeline: true,
            StartPhaseNo: 7,
            EndPhaseNo: 7,
            PlanId: productionRequest.PlanId);
        var response = new BatchGenerateFromPlansResponse(
            Success: true,
            DryRun: false,
            RequestedTitleCount: 1,
            SelectedPlanCount: 1,
            MaxPlans: 1,
            SelectedPlans: [],
            Steps: [],
            Warnings: [],
            Errors: [],
            UseProductionPipeline: true,
            PlanId: productionRequest.PlanId,
            Title: productionRequest.Title,
            OutputRoot: outputRoot,
            ProductionPipelineRequest: productionRequest);

        Exception? phase7Exception = null;
        try
        {
            await generator.BuildAndWriteDiagnosticsAsync(request, response, CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (!ex.Message.Contains("Required semantic fact resolution failed", StringComparison.OrdinalIgnoreCase))
        {
            phase7Exception = ex;
            // This regression targets the pre-prompt resolver boundary; later prompt/narration validations are outside this parity assertion.
        }

        Assert.False(
            ExceptionOriginatesFromSemanticRegistry(phase7Exception),
            "Phase 7 must not throw from SemanticSourceAdapterRegistryV1.CanonicalizeCapabilityId or SemanticSourceAdapterRegistryV1.GetAdapters.");

        var diagnosticsPath = Path.Combine(outputRoot, "narration-v5", "required-semantic-fact-diagnostics.json");
        Assert.True(File.Exists(diagnosticsPath), "Phase 7 must write required semantic diagnostics before prompt generation.");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
        var semantic = doc.RootElement.GetProperty("semanticResolutionDiagnostics");
        Assert.True(semantic.GetProperty("policyCount").GetInt32() > 0);
        Assert.True(semantic.GetProperty("adapterCount").GetInt32() > 0);
        var presence = semantic.GetProperty("sourceContextPresence");
        Assert.True(presence.GetProperty("productionPipelineRequest").GetProperty("present").GetBoolean());
        Assert.Equal(1, presence.GetProperty("productionPipelineRequest").GetProperty("primaryObjectCount").GetInt32());
        Assert.Equal(1, presence.GetProperty("productionPipelineRequest").GetProperty("secondaryObjectCount").GetInt32());
        Assert.True(presence.GetProperty("productionEventIntelligence").GetProperty("present").GetBoolean());
        Assert.True(presence.GetProperty("observationMetadata").GetProperty("present").GetBoolean());
        Assert.True(presence.GetProperty("canonicalEventIdentity").GetProperty("present").GetBoolean());
        Assert.Equal("PlanetPairing", presence.GetProperty("canonicalEventIdentity").GetProperty("eventType").GetString());
        Assert.True(presence.GetProperty("familyProfile").GetProperty("present").GetBoolean());
        Assert.Equal("PlanetPairing", presence.GetProperty("familyProfile").GetProperty("familyId").GetString());

        var allBeats = semantic.GetProperty("beats").EnumerateArray().ToArray();
        var resolved = allBeats.SelectMany(b => b.GetProperty("resolvedRequiredCapabilities").EnumerateArray().Select(x => x.GetString())).Where(x => x is not null).ToArray();
        Assert.Contains("PrimaryObjects", resolved);
        Assert.Contains("EventIdentity", resolved);
        Assert.Contains("ObservationTiming", resolved);
        Assert.Contains("AngularRelationship", resolved);
        Assert.Contains("LocationContext", resolved);
        Assert.Contains("ApparentAlignmentExplanation", resolved);
        Assert.Contains("PhysicalProximityClarification", resolved);
        var missing = allBeats.SelectMany(b => b.GetProperty("missingRequiredFacts").EnumerateArray().Select(x => x.GetString())).Where(x => x is not null).Distinct().ToArray();
        Assert.DoesNotContain("PrimaryObjects", missing);
        Assert.DoesNotContain("EventIdentity", missing);
        Assert.DoesNotContain("ObservationTiming", missing);
        Assert.DoesNotContain("LocationContext", missing);
        Assert.DoesNotContain("ApparentAlignmentExplanation", missing);
        Assert.DoesNotContain("PhysicalProximityClarification", missing);
        Assert.DoesNotContain("No source policy", semantic.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(semantic.GetProperty("semanticCapabilityDiagnostics").EnumerateArray(), d => d.GetProperty("adaptersExecuted").GetArrayLength() > 0 && d.GetProperty("candidatesFound").GetInt32() > 0);
    }

    private static bool ExceptionOriginatesFromSemanticRegistry(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var stackTrace = current.StackTrace ?? string.Empty;
            if (stackTrace.Contains("SemanticSourceAdapterRegistryV1.CanonicalizeCapabilityId", StringComparison.Ordinal) ||
                stackTrace.Contains("SemanticSourceAdapterRegistryV1.GetAdapters", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ContentPlanProductionPipelineRequest BuildJupiterVenusRequest() => new(
        PlanId: Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"), Category: "Astronomy", Title: "Jupiter Venus conjunction over Udaipur", ShortTitle: "Jupiter Venus", EventType: "PLANET_CONJUNCTION", RegionId: "IN-RJ-UDAIPUR", Language: "en", PrimaryObjects: ["Jupiter"], SecondaryObjects: ["Venus"], StartUtc: DateTimeOffset.Parse("2026-08-12T13:00:00Z"), PeakUtc: DateTimeOffset.Parse("2026-08-12T14:00:00Z"), EndUtc: DateTimeOffset.Parse("2026-08-12T15:00:00Z"), ScheduledUtc: DateTimeOffset.Parse("2026-08-11T10:00:00Z"), SourceExternalEventId: "source-event-1", PlannedFormat: "long", RequestedOutputs: ["long", "short"], VisibilityScore: 90, RarityScore: 70, AudienceInterestScore: 80, ContentOpportunityScore: 85, VerificationStatus: "Verified", VerificationSource: "ProductionParityTest", ContentStrategy: "Conjunction", LocalPeakTime: null, SkyDirectionHint: null, VisibilityRegion: null, MoonInterference: null, BestViewingWindowLocal: null, RadiantVisibilityNote: null, MoonIlluminationPercent: null, RecommendedPublishWindow: null, RecommendedContentTypes: [], Warnings: [], SourceNotes: [], TimeZone: "Asia/Kolkata", AngularSeparationDegrees: 1.63m);

    private static void WriteProductionArtifacts(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "editorial"));
        Directory.CreateDirectory(Path.Combine(root, "creative"));
        Directory.CreateDirectory(Path.Combine(root, "plan-input"));
        Directory.CreateDirectory(Path.Combine(root, "question-engine"));
        File.WriteAllText(Path.Combine(root, "editorial", "editorial-contract.json"), "{\"language\":\"en\",\"eventType\":\"PLANET_CONJUNCTION\",\"requiredNarrationFacts\":[]}");
        File.WriteAllText(Path.Combine(root, "creative", "creative-storyboard.json"), "{\"language\":\"en\",\"storyArc\":\"Hook → Timing → Science → Observation\",\"scenes\":[{\"sceneOrder\":1,\"sceneId\":\"scene-1\",\"purpose\":\"Hook\"}]}");
        var beats = "{\"eventType\":\"PLANET_CONJUNCTION\",\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}},{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"timing\",\"narrativeRole\":\"Timing\",\"allocatedFacts\":{}},{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"observation\",\"narrativeRole\":\"Observation\",\"allocatedFacts\":{}},{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"science\",\"narrativeRole\":\"Science\",\"allocatedFacts\":{}}]}";
        File.WriteAllText(Path.Combine(root, "creative", "documentary-contract.long.json"), beats);
        File.WriteAllText(Path.Combine(root, "creative", "documentary-contract.short.json"), beats);
        File.WriteAllText(Path.Combine(root, "plan-input", "production-event-intelligence.json"), "{\"eventType\":\"PLANET_CONJUNCTION\",\"primaryObjects\":[\"Jupiter\"],\"secondaryObjects\":[\"Venus\"],\"startUtc\":\"2026-08-12T13:00:00Z\",\"peakUtc\":\"2026-08-12T14:00:00Z\",\"endUtc\":\"2026-08-12T15:00:00Z\",\"angularSeparationDegrees\":1.63,\"verificationStatus\":\"Verified\",\"familyId\":\"PlanetPairing\",\"profileId\":\"PlanetPairing\"}");
        File.WriteAllText(Path.Combine(root, "editorial", "observation-metadata.json"), "{\"eventWindow\":{\"startUtc\":\"2026-08-12T13:00:00Z\",\"peakUtc\":\"2026-08-12T14:00:00Z\",\"endUtc\":\"2026-08-12T15:00:00Z\",\"timeZone\":\"Asia/Kolkata\"},\"observationLocation\":{\"regionId\":\"IN-RJ-UDAIPUR\",\"timeZone\":\"Asia/Kolkata\"},\"angularSeparationDegrees\":1.63,\"verificationStatus\":\"Verified\"}");
        File.WriteAllText(Path.Combine(root, "editorial", "story-graph.json"), "{\"nodes\":[]}");
        File.WriteAllText(Path.Combine(root, "question-engine", "question-answer-set.json"), "{\"answers\":[]}");
    }
}
