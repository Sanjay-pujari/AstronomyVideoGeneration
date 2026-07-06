using System.Text.Json;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class CreativeQualityScoringEngineTests
{
    [Fact]
    public async Task Scoring_disabled_returns_skipped_empty_result()
    {
        var report = await Engine(new VisualIntelligenceOptions()).ScoreAsync(new CreativeQualityScoringRequest { Context = Context(useQuality: false) });

        Assert.Equal("skipped", report.Mode);
        Assert.Equal(PublicationDecisionStatus.Skipped, report.PublicationDecision);
        Assert.Empty(report.CategoryScores);
        Assert.Contains(report.Diagnostics, d => d.Code == "quality_scoring.skipped");
    }

    [Fact]
    public async Task Valid_cdl_contract_and_prompt_package_produce_good_score()
    {
        var (cdl, contract, prompt) = await ValidArtifacts();

        var report = await Engine(Options()).ScoreAsync(Request(cdl, contract, prompt));

        Assert.True(report.OverallScore >= 0.90, $"overall={report.OverallScore}");
        Assert.Equal(PublicationDecisionStatus.Approved, report.PublicationDecision);
        Assert.Contains(report.CategoryScores, s => s.Name == CreativeQualityCategory.AstronomicalAccuracy && s.Passed);
        Assert.True((bool)report.ExtensionFields["activePipelineBlockingApplied"]! == false);
        Assert.False((bool)report.ExtensionFields["azureCallMade"]!);
        Assert.False((bool)report.ExtensionFields["imageGenerationCallMade"]!);
    }

    [Fact]
    public async Task Missing_hero_subject_reduces_score_and_warns()
    {
        var (cdl, contract, prompt) = await ValidArtifacts();
        cdl = cdl with { Directives = cdl.Directives.Where(d => d.Name != "heroSubject").ToList() };
        contract = contract with { VisualIntent = contract.VisualIntent with { PrimarySubject = "" } };

        var report = await Engine(Options()).ScoreAsync(Request(cdl, contract, prompt));

        Assert.True(report.OverallScore < 1.0);
        Assert.Contains(report.Warnings, w => w.Contains("Hero subject", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Recommendations, r => r.Contains("hero subject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_prompt_package_warns_when_prompt_composer_v2_enabled()
    {
        var (cdl, contract, _) = await ValidArtifacts();

        var report = await Engine(Options()).ScoreAsync(Request(cdl, contract, null));

        Assert.Contains(report.Diagnostics, d => d.Code == "quality_scoring.missing_prompt_package");
        Assert.Contains(report.Warnings, w => w.Contains("Prompt package", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Publication_decision_is_advisory_even_when_blocking_requested()
    {
        var report = await Engine(Options(blocking: true)).ScoreAsync(new CreativeQualityScoringRequest { Context = Context(useQuality: true, blocking: true) });

        Assert.NotEqual(PublicationDecisionStatus.Unknown, report.PublicationDecision);
        Assert.True((bool)report.ExtensionFields["blockingRequested"]!);
        Assert.False((bool)report.ExtensionFields["activePipelineBlockingApplied"]!);
        Assert.Contains(report.Diagnostics, d => d.Code == "quality_scoring.advisory_decision");
    }

    [Fact]
    public async Task QualityReport_serializes_and_deserializes()
    {
        var (cdl, contract, prompt) = await ValidArtifacts();
        var report = await Engine(Options()).ScoreAsync(Request(cdl, contract, prompt));

        var json = JsonSerializer.Serialize(report, VisualIntelligenceJson.CreateSerializerOptions());
        var roundTrip = JsonSerializer.Deserialize<QualityReport>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.Contains("\"categoryScores\"", json);
        Assert.Contains("\"publicationDecision\"", json);
        Assert.Equal(report.PublicationDecision, roundTrip!.PublicationDecision);
        Assert.Equal(CreativeQualityCategory.OverallProductionQuality, roundTrip.CategoryScores.Last().Name);
    }

    private static CreativeQualityScoringEngine Engine(VisualIntelligenceOptions options) => new(Options.Create(options), NullLogger<CreativeQualityScoringEngine>.Instance);
    private static VisualIntelligenceOptions Options(bool blocking = false) => new() { UseQualityScoring = true, UsePromptComposerV2 = true, UseQualityScoringBlocking = blocking };
    private static CreativeQualityScoringRequest Request(CDL? cdl, CreativeDirectionContract? contract, PromptPackage? prompt) => new() { Context = Context(useQuality: true), Cdl = cdl, CreativeDirectionContract = contract, PromptPackage = prompt };

    private static VisualIntelligenceOrchestrationContext Context(bool useQuality, bool blocking = false) => new()
    {
        CorrelationId = "quality-test",
        EventFamily = ContractEventFamily.PlanetConjunction,
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        FeatureFlags = new VisualIntelligenceFlagSnapshot { UseQualityScoring = useQuality, UsePromptComposerV2 = true, UseQualityScoringBlocking = blocking }
    };

    private static async Task<(CDL Cdl, CreativeDirectionContract Contract, PromptPackage Prompt)> ValidArtifacts()
    {
        var context = Context(useQuality: true) with { FeatureFlags = new VisualIntelligenceFlagSnapshot { UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true, UsePromptComposerV2 = true, UseQualityScoring = true } };
        var direction = await new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance).CreateDirectionAsync(context);
        var composer = new PromptComposerV2(Options.Create(new VisualIntelligenceOptions { UsePromptComposerV2 = true }), new PromptSectionBuilder(), new PromptOptimizer(), new GenericProviderAdapter(), new PromptPackageBuilder(), new ImageProviderProfileRegistry([new GenericImageProviderProfile()]));
        var prompt = await composer.ComposeAsync(direction.Cdl, direction.CreativeDirectionContract);
        return (direction.Cdl!, direction.CreativeDirectionContract!, prompt.PromptPackage!);
    }
}
