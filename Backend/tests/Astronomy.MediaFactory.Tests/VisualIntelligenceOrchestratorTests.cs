using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
    public async Task Enabled_path_returns_real_cdl_and_contract()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions
        {
            Enabled = true,
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
        Assert.Equal(ContractEventFamily.PlanetConjunction, result.CreativeDirectionContract.EventFamily);
        Assert.Contains(result.Cdl!.Directives, d => d.Name == "creativeIntent");
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_director.cdl_generated");
    }

    [Fact]
    public async Task Failure_path_returns_fallback_result_with_diagnostics_without_throwing()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true,
            UseVisualCreativeDirector = true, UseCDL = true }, new ThrowingVisualCreativeDirector());

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

        Assert.False(options.Enabled);
        Assert.False(options.WriteDiagnostics);
        Assert.True(options.ObservationMode);
        Assert.False(options.UseVisualCreativeDirector);
        Assert.False(options.UseCDL);
        Assert.False(options.UseCreativeDirectionContract);
        Assert.False(options.UsePromptComposerV2);
        Assert.False(options.UseProviderProfiles);
        Assert.False(options.UseQualityScoring);
        Assert.False(options.UseQualityScoringBlocking);
        Assert.False(options.UseExperimentalRenderingRules);
        Assert.False(snapshot.Enabled);
        Assert.False(snapshot.WriteDiagnostics);
        Assert.True(snapshot.ObservationMode);
        Assert.False(snapshot.UseVisualCreativeDirector);
        Assert.False(snapshot.UseQualityScoringBlocking);
    }

    [Fact]
    public async Task Diagnostics_are_included_on_context_and_result()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true,
            UseVisualCreativeDirector = true, UseCDL = true });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest() with { EventFamily = ContractEventFamily.Unknown, EventType = "conjunction" });

        Assert.NotEmpty(result.Diagnostics);
        Assert.NotEmpty(result.Context.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.context_created");
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.event_family_placeholder");
    }

    [Fact]
    public async Task Diagnostics_writing_disabled_by_default_creates_no_files()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, UseVisualCreativeDirector = true, UseCDL = true, DiagnosticsOutputPath = path });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.False(Directory.Exists(path));
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.diagnostics_disabled");
    }

    [Fact]
    public async Task Diagnostics_writing_creates_expected_json_files_when_enabled()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions
        {
            Enabled = true,
            WriteDiagnostics = true,
            DiagnosticsOutputPath = path,
            UseVisualCreativeDirector = true,
            UseCDL = true,
            UseCreativeDirectionContract = true,
            UsePromptComposerV2 = true,
            UseProviderProfiles = true,
            UseQualityScoring = true
        });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        var folder = Path.Combine(path, "test-correlation");
        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.True(File.Exists(Path.Combine(folder, "CDL.json")));
        Assert.True(File.Exists(Path.Combine(folder, "CreativeDirectionContract.json")));
        Assert.True(File.Exists(Path.Combine(folder, "PromptPackage.json")));
        Assert.True(File.Exists(Path.Combine(folder, "QualityReport.json")));
        Assert.True(File.Exists(Path.Combine(folder, "CreativeKnowledgeReview.json")));
        Assert.True(File.Exists(Path.Combine(folder, "EditorialDecision.json")));
        Assert.True(File.Exists(Path.Combine(folder, "EditorialReasoningReview.json")));
        Assert.True(File.Exists(Path.Combine(folder, "OrchestrationSummary.json")));
    }


    [Fact]
    public async Task Diagnostics_writing_creates_creative_knowledge_review()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        var review = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path, "test-correlation", "CreativeKnowledgeReview.json")));
        Assert.Equal("PlanetPairing", review.RootElement.GetProperty("knowledgeUsed").GetString());
        Assert.Contains("relationship", review.RootElement.GetProperty("storyGoal").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What makes", review.RootElement.GetProperty("viewerQuestion").GetString());
        Assert.NotEmpty(review.RootElement.GetProperty("compositionStrategy").GetString());
        Assert.NotEmpty(review.RootElement.GetProperty("editorialNotes").EnumerateArray());
    }

    [Fact]
    public async Task Empty_diagnostics_path_resolves_to_run_output_folder()
    {
        var runOutputFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true });

        await orchestrator.OrchestrateAsync(DefaultRequest() with { RunOutputFolder = runOutputFolder });

        Assert.True(File.Exists(Path.Combine(runOutputFolder, "diagnostics", "visual-intelligence", "OrchestrationSummary.json")));
    }

    [Fact]
    public async Task Empty_diagnostics_path_uses_app_context_fallback_without_run_output_folder()
    {
        var correlationId = $"fallback-{Guid.NewGuid():N}";
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true });

        await orchestrator.OrchestrateAsync(DefaultRequest() with { CorrelationId = correlationId, RunOutputFolder = null });

        var folder = Path.Combine(AppContext.BaseDirectory, "diagnostics", "visual-intelligence", correlationId);
        var summary = ReadSummary(Path.Combine(folder, "OrchestrationSummary.json"));
        Assert.True(summary.RootElement.GetProperty("diagnosticsPathFallbackApplied").GetBoolean());
    }

    [Fact]
    public async Task All_feature_flags_false_writes_summary_only()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        var files = Directory.GetFiles(Path.Combine(path, "test-correlation")).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        Assert.Equal(["OrchestrationSummary.json"], files);
        var summary = ReadSummary(Path.Combine(path, "test-correlation", "OrchestrationSummary.json"));
        Assert.Equal("Disabled", summary.RootElement.GetProperty("status").GetString());
        Assert.Contains("UseVisualCreativeDirector", summary.RootElement.GetProperty("disabledFeatureFlags").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Cdl_contract_stages_write_only_cdl_contract_and_summary()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        var files = Directory.GetFiles(Path.Combine(path, "test-correlation")).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        Assert.Equal(["CDL.json", "CreativeDirectionContract.json", "CreativeKnowledgeReview.json", "EditorialDecision.json", "EditorialReasoningReview.json", "HeroCreativeReview.json", "OrchestrationSummary.json"], files);
    }

    [Fact]
    public async Task Prompt_composer_stage_additionally_writes_prompt_package()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true, UsePromptComposerV2 = true });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.True(File.Exists(Path.Combine(path, "test-correlation", "PromptPackage.json")));
        Assert.False(File.Exists(Path.Combine(path, "test-correlation", "QualityReport.json")));
        Assert.True(File.Exists(Path.Combine(path, "test-correlation", "HeroCreativeReview.json")));
    }

    [Fact]
    public async Task Quality_scoring_stage_additionally_writes_quality_report()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true, UsePromptComposerV2 = true, UseQualityScoring = true });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.True(File.Exists(Path.Combine(path, "test-correlation", "QualityReport.json")));
        Assert.True(File.Exists(Path.Combine(path, "test-correlation", "HeroCreativeReview.json")));
    }

    [Fact]
    public async Task Diagnostics_write_failure_does_not_fail_orchestrator()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "not a directory");
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.diagnostics_write_failed");
    }

    [Fact]
    public async Task Summary_uses_resolved_event_family_after_visual_director_resolution()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest() with { EventFamily = ContractEventFamily.Unknown, EventType = "PlanetPairing", PrimaryObjects = ["Jupiter"], SupportingObjects = ["Venus"] });

        var summary = ReadSummary(Path.Combine(path, "test-correlation", "OrchestrationSummary.json"));
        Assert.Equal(ContractEventFamily.PlanetConjunction, result.Context.EventFamily);
        Assert.Equal("planetConjunction", summary.RootElement.GetProperty("eventFamily").GetString());
    }

    [Fact]
    public async Task Summary_contains_generated_artifacts_timestamps_and_duration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, DiagnosticsOutputPath = path, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true, UsePromptComposerV2 = true });

        await orchestrator.OrchestrateAsync(DefaultRequest());

        var summary = ReadSummary(Path.Combine(path, "test-correlation", "OrchestrationSummary.json"));
        var artifacts = summary.RootElement.GetProperty("generatedArtifacts").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(["CDL.json", "CreativeDirectionContract.json", "EditorialDecision.json", "PromptPackage.json", "CreativeKnowledgeReview.json", "EditorialReasoningReview.json", "HeroCreativeReview.json", "OrchestrationSummary.json"], artifacts);
        Assert.NotEqual(default, summary.RootElement.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.NotEqual(default, summary.RootElement.GetProperty("completedAtUtc").GetDateTimeOffset());
        Assert.True(summary.RootElement.GetProperty("durationMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task Prompt_package_and_quality_report_are_observation_only()
    {
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions
        {
            Enabled = true,
            UseVisualCreativeDirector = true,
            UseCDL = true,
            UseCreativeDirectionContract = true,
            UsePromptComposerV2 = true,
            UseProviderProfiles = true,
            UseQualityScoring = true,
            UseQualityScoringBlocking = true
        });

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.NotNull(result.PromptPackage);
        Assert.NotNull(result.QualityReport);
        Assert.False(result.FallbackApplied);
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.observation_advisory_only");
        Assert.NotEqual(PublicationDecisionStatus.Block, result.QualityReport!.RecommendedDecision);
    }

    [Fact]
    public async Task Azure_provider_profile_does_not_call_azure()
    {
        var adapter = new CountingProviderAdapter();
        var orchestrator = CreateOrchestrator(new VisualIntelligenceOptions
        {
            Enabled = true,
            DefaultProvider = ImageProviderType.AzureImage,
            UseVisualCreativeDirector = true,
            UseCDL = true,
            UseCreativeDirectionContract = true,
            UsePromptComposerV2 = true,
            UseProviderProfiles = true
        }, promptComposer: CreatePromptComposer(new VisualIntelligenceOptions { UsePromptComposerV2 = true }, adapter));

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.NotNull(result.PromptPackage);
        Assert.Equal(1, adapter.CallCount);
        Assert.Contains(result.PromptPackage!.Diagnostics, d => d.Code.Contains("provider_adapter", StringComparison.OrdinalIgnoreCase));
        Assert.False((bool)result.PromptPackage.ProviderParameters["azureSdkCallMade"]!);
        Assert.False((bool)result.PromptPackage.ProviderParameters["imageGenerationCallMade"]!);
    }

    [Fact]
    public void Default_appsettings_keep_visual_intelligence_disabled()
    {
        var options = BindVisualIntelligenceOptions("appsettings.json");

        Assert.False(options.Enabled);
        Assert.False(options.WriteDiagnostics);
        Assert.True(options.ObservationMode);
        Assert.Equal(ImageProviderType.Unknown, options.DefaultProvider);
        Assert.False(options.UseVisualCreativeDirector);
        Assert.False(options.UseCDL);
        Assert.False(options.UseCreativeDirectionContract);
        Assert.False(options.UsePromptComposerV2);
        Assert.False(options.UseProviderProfiles);
        Assert.False(options.UseQualityScoring);
        Assert.False(options.UseQualityScoringBlocking);
        Assert.False(options.UseExperimentalRenderingRules);
    }

    [Fact]
    public async Task Production_default_config_remains_no_op()
    {
        var options = BindVisualIntelligenceOptions("appsettings.json");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = CreateOrchestrator(options);

        var result = await orchestrator.OrchestrateAsync(DefaultRequest() with { RunOutputFolder = path });

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Disabled, result.Status);
        Assert.Null(result.PromptPackage);
        Assert.False(Directory.Exists(Path.Combine(path, "diagnostics", "visual-intelligence")));
        Assert.Contains(result.Diagnostics, d => d.Code == "visual_intelligence.diagnostics_disabled");
    }

    [Fact]
    public async Task Development_config_resolves_azure_image_provider_profile()
    {
        var options = BindVisualIntelligenceOptions("appsettings.Development.json");
        var orchestrator = CreateOrchestrator(options, promptComposer: CreatePromptComposer(options, new ProviderAdapterResolver([new AzurePromptProviderAdapter(), new GenericProviderAdapter()])));

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.True(options.Enabled);
        Assert.True(options.WriteDiagnostics);
        Assert.True(options.UseProviderProfiles);
        Assert.Equal(ImageProviderType.AzureImage, options.DefaultProvider);
        Assert.NotNull(result.PromptPackage);
        Assert.Equal(ImageProviderType.AzureImage, result.PromptPackage!.ProviderName);
        Assert.Equal(VisualIntelligenceContractVersions.AzureImageProviderProfileVersion, result.PromptPackage.ProviderProfileVersion);
    }

    [Fact]
    public async Task Development_provider_profiles_avoid_generic_fallback_warning()
    {
        var options = BindVisualIntelligenceOptions("appsettings.Development.json");
        var orchestrator = CreateOrchestrator(options, promptComposer: CreatePromptComposer(options, new ProviderAdapterResolver([new AzurePromptProviderAdapter(), new GenericProviderAdapter()])));

        var result = await orchestrator.OrchestrateAsync(DefaultRequest());

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "image_provider_profile.generic_fallback_used");
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.azure_image.resolved");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.used");
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

    private static VisualIntelligenceOrchestrator CreateOrchestrator(VisualIntelligenceOptions options, IVisualCreativeDirector? director = null, IPromptComposerV2? promptComposer = null) =>
        new(Options.Create(options),
            director ?? new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance),
            promptComposer ?? CreatePromptComposer(options),
            NullLogger<VisualIntelligenceOrchestrator>.Instance);

    private static IPromptComposerV2 CreatePromptComposer(VisualIntelligenceOptions options, IProviderAdapter? adapter = null) =>
        new PromptComposerV2(
            Options.Create(options),
            new PromptSectionBuilder(),
            new PromptOptimizer(),
            adapter ?? new GenericProviderAdapter(),
            new PromptPackageBuilder(),
            new ImageProviderProfileRegistry([new GenericImageProviderProfile(), new AzureImageProviderProfile()]));

    private static VisualIntelligenceOrchestrationRequest DefaultRequest() => new()
    {
        CorrelationId = "test-correlation",
        EventFamily = ContractEventFamily.PlanetConjunction,
        EventType = "planet-conjunction",
        Language = "en",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        RequestedAssetType = "thumbnail"
    };

    private static JsonDocument ReadSummary(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static VisualIntelligenceOptions BindVisualIntelligenceOptions(string fileName)
    {
        var apiPath = Path.Combine(FindRepositoryRoot().FullName, "Backend", "src", "Astronomy.MediaFactory.Api");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile(fileName, optional: false)
            .Build();
        return configuration.GetSection(VisualIntelligenceOptions.SectionName).Get<VisualIntelligenceOptions>() ?? new VisualIntelligenceOptions();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Api", "appsettings.json")))
            directory = directory.Parent;
        return directory ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class CountingProviderAdapter : IProviderAdapter
    {
        private readonly AzurePromptProviderAdapter inner = new();
        public int CallCount { get; private set; }
        public bool CanAdapt(IImageProviderProfile profile) => inner.CanAdapt(profile);
        public ProviderPrompt Adapt(PromptSections sections, IImageProviderProfile profile)
        {
            CallCount++;
            return inner.Adapt(sections, profile);
        }
    }

    private sealed class ThrowingVisualCreativeDirector : IVisualCreativeDirector
    {
        public Task<VisualCreativeDirectorResult> CreateDirectionAsync(VisualIntelligenceOrchestrationContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("director failed");
    }
}
