using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class VisualIntelligenceOrchestratorTests
{
    [Fact]
    public async Task All_flags_disabled_returns_disabled_with_diagnostics()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions());

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Disabled, result.Status);
        Assert.False(result.FallbackApplied);
        Assert.Null(result.Cdl);
        Assert.Null(result.CreativeDirectionContract);
        Assert.False(result.Context.FeatureFlags.UseVisualCreativeDirector);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.disabled");
    }

    [Fact]
    public async Task Enabled_path_returns_placeholder_cdl_and_contract()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions
        {
            UseVisualCreativeDirector = true,
            UseCDL = true,
            UseCreativeDirectionContract = true
        });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.NotNull(result.Cdl);
        Assert.NotNull(result.CreativeDirectionContract);
        Assert.Equal("3.2D", result.Cdl!.CdlVersion);
        Assert.Equal("3.2G", result.CreativeDirectionContract!.ContractVersion);
        Assert.Equal(EventFamily.PlanetConjunction, result.CreativeDirectionContract.EventFamily);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.stub");
    }

    [Fact]
    public async Task Failure_path_returns_fallback_result_with_diagnostics_without_throwing()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { UseVisualCreativeDirector = true, UseCDL = true }, new ThrowingVisualCreativeDirector());

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.FallbackApplied, result.Status);
        Assert.True(result.FallbackApplied);
        Assert.Equal("director failed", result.FallbackReason);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.fallback" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Default_flags_are_false_in_snapshot_and_options()
    {
        var options = new VisualIntelligenceOptions();
        var snapshot = VisualIntelligenceFlagSnapshot.FromOptions(options);

        Assert.False(options.UseVisualCreativeDirector);
        Assert.False(options.UseCDL);
        Assert.False(options.UseCreativeDirectionContract);
        Assert.False(options.UsePromptComposerV2);
        Assert.False(options.UseProviderProfiles);
        Assert.False(options.UseQualityScoring);
        Assert.False(options.UseQualityScoringBlocking);
        Assert.False(options.UseExperimentalRenderingRules);
        Assert.False(snapshot.UseVisualCreativeDirector);
        Assert.False(snapshot.UseQualityScoringBlocking);
    }

    [Fact]
    public async Task Diagnostics_are_included_on_context_and_result()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { UseVisualCreativeDirector = true, UseCDL = true });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest() with { EventFamily = EventFamily.Unknown, EventType = "conjunction" });

        Assert.NotEmpty(result.Diagnostics);
        Assert.NotEmpty(result.Context.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.context_created");
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.event_family_placeholder");
    }

    [Fact]
    public void Visual_intelligence_registration_is_additive_and_does_not_replace_pipeline_services()
    {
        var services = new ServiceCollection();
        services.AddScoped<IVisualAssetProvider, StellariumVisualGenerationService>();

        services.AddVisualIntelligenceOrchestration();

        Assert.Contains(services, d => d.ServiceType == typeof(IVisualAssetProvider) && d.ImplementationType == typeof(StellariumVisualGenerationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IVisualIntelligenceOrchestrator) && d.ImplementationType == typeof(VisualIntelligenceOrchestrator));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IVisualAssetProvider) && d.ImplementationType == typeof(VisualIntelligenceOrchestrator));
    }

    private static VisualIntelligenceOrchestrator CreateOrchestrator(VisualIntelligenceOptions options, IVisualCreativeDirector? director = null) =>
        new(Options.Create(options), director ?? new StubVisualCreativeDirector(), NullLogger<VisualIntelligenceOrchestrator>.Instance);

    private static VisualIntelligenceOrchestrationRequest DefaultRequest() => new()
    {
        CorrelationId = "test-correlation",
        EventFamily = EventFamily.PlanetConjunction,
        EventType = "planet-conjunction",
        Language = "en",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        RequestedAssetType = "thumbnail"
    };

    private sealed class ThrowingVisualCreativeDirector : IVisualCreativeDirector
    {
        public Task<VisualCreativeDirectorResult> CreateDirectionAsync(VisualIntelligenceOrchestrationContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("director failed");
    }
}
