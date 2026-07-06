using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class EditorialReasoningEngineTests
{
    [Fact]
    public void Planet_pairing_reasoning_answers_story_question()
    {
        var decision = new EditorialReasoningEngine().Decide(PlanetPairingContext());

        Assert.Equal("The unusual closeness of two bright planets.", decision.PrimaryStory);
        Assert.Equal("The planets only appear close from Earth's perspective.", decision.ViewerTakeaway);
        Assert.Equal("Witness one of the brightest conjunctions of the year.", decision.EmotionalHook);
        Assert.Equal("Relationship > Scale", decision.RecommendedVisualRelationship);
        Assert.Equal("Balanced pairing", decision.RecommendedComposition);
        Assert.Equal("Relationship first", decision.RecommendedViewerFocus);
        Assert.Equal("4.2A", decision.ReasoningVersion);
        Assert.True(decision.Confidence >= .9);
    }

    [Fact]
    public void Unknown_family_falls_back_to_generic_editorial_reasoning()
    {
        var decision = new EditorialReasoningEngine().Decide(new VisualIntelligenceOrchestrationContext
        {
            CorrelationId = "unknown",
            EventFamily = ContractEventFamily.Unknown,
            EventType = "unclassified-sky-event"
        });

        Assert.Contains("GenericAstronomy", decision.StoryId);
        Assert.Equal("Clarity > spectacle", decision.RecommendedVisualRelationship);
        Assert.Equal("Story first", decision.RecommendedViewerFocus);
        Assert.True(decision.Confidence < .8);
    }

    [Fact]
    public void Knowledge_lookup_uses_creative_knowledge_library()
    {
        var diagnostics = new List<DiagnosticMessage>();
        var decision = new EditorialReasoningEngine(new CreativeKnowledgeLibrary()).Decide(PlanetPairingContext(), diagnostics: diagnostics);

        Assert.Contains("PlanetPairing", decision.StoryId);
        Assert.Contains(diagnostics, d => d.Code == "creative_knowledge.resolved");
        Assert.Contains(diagnostics, d => d.Code == "editorial_reasoning.decision_created");
    }

    [Fact]
    public void Editorial_decision_serializes_as_immutable_dto()
    {
        var decision = new EditorialReasoningEngine().Decide(PlanetPairingContext());

        var json = JsonSerializer.Serialize(decision, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true));
        var roundTrip = JsonSerializer.Deserialize<EditorialDecision>(json, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true));

        Assert.Contains("storyId", json);
        Assert.Equal(decision.PrimaryStory, roundTrip!.PrimaryStory);
        Assert.Equal(decision.EditorialPriority, roundTrip.EditorialPriority);
    }

    [Fact]
    public async Task Diagnostics_generation_writes_editorial_reasoning_review()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = new VisualIntelligenceOrchestrator(
            Options.Create(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true }),
            new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance),
            new PromptComposerV2(Options.Create(new VisualIntelligenceOptions()), new PromptSectionBuilder(), new PromptOptimizer(), new GenericProviderAdapter(), new PromptPackageBuilder(), new ImageProviderProfileRegistry([new GenericImageProviderProfile()])),
            NullLogger<VisualIntelligenceOrchestrator>.Instance);

        await orchestrator.OrchestrateAsync(new VisualIntelligenceOrchestrationRequest
        {
            CorrelationId = "editorial-review",
            EventFamily = ContractEventFamily.PlanetConjunction,
            EventType = "planet-conjunction",
            EventName = "Venus Jupiter conjunction"
        });

        var review = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path, "editorial-review", "EditorialReasoningReview.json")));
        Assert.Equal("PlanetPairing", review.RootElement.GetProperty("knowledgeUsed").GetString());
        Assert.Equal("The unusual closeness of two bright planets.", review.RootElement.GetProperty("story").GetString());
        Assert.Equal("Balanced pairing", review.RootElement.GetProperty("recommendedComposition").GetString());
        Assert.Equal("Relationship > Scale", review.RootElement.GetProperty("visualRelationship").GetString());
        Assert.True(review.RootElement.GetProperty("reasoningConfidence").GetDouble() >= .9);
    }

    [Fact]
    public async Task Production_default_pipeline_remains_disabled()
    {
        var orchestrator = new VisualIntelligenceOrchestrator(
            Options.Create(new VisualIntelligenceOptions()),
            new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance),
            new PromptComposerV2(Options.Create(new VisualIntelligenceOptions()), new PromptSectionBuilder(), new PromptOptimizer(), new GenericProviderAdapter(), new PromptPackageBuilder(), new ImageProviderProfileRegistry([new GenericImageProviderProfile()])),
            NullLogger<VisualIntelligenceOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync(new VisualIntelligenceOrchestrationRequest { CorrelationId = "prod-default", EventType = "planet-conjunction" });

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Disabled, result.Status);
        Assert.Null(result.EditorialDecision);
        Assert.Null(result.PromptPackage);
    }

    private static VisualIntelligenceOrchestrationContext PlanetPairingContext() => new()
    {
        CorrelationId = "pairing",
        EventFamily = ContractEventFamily.PlanetConjunction,
        EventType = "planet-conjunction",
        EventName = "Venus Jupiter conjunction",
        PrimaryObjects = ["Venus"],
        SupportingObjects = ["Jupiter"]
    };
}
