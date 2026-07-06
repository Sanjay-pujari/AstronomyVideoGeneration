using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class PromptComposerV2Tests
{
    [Fact]
    public async Task SectionBuilder_maps_planet_pairing_cdl_into_sections()
    {
        var direction = await CreateDirectionAsync();
        var sections = new PromptSectionBuilder().Build(direction.Cdl, direction.CreativeDirectionContract);

        Assert.Contains("heroSubject", sections.Sections.Keys);
        Assert.Contains(sections.Sections["heroSubject"], v => v.Contains("Jupiter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("astronomicalRendering", sections.Sections.Keys);
        Assert.Contains(sections.Sections["astronomicalRendering"], v => v.Contains("circular", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sections.Diagnostics, d => d.Code == "prompt_sections.built");
    }

    [Fact]
    public async Task Negative_constraints_are_preserved()
    {
        var direction = await CreateDirectionAsync();
        var contract = direction.CreativeDirectionContract! with { NegativeConstraints = new NegativeConstraints { Scientific = ["no fake glow", "no distortion"] } };

        var sections = new PromptSectionBuilder().Build(direction.Cdl, contract);

        Assert.Contains(sections.Sections["negativeConstraints"], v => v.Contains("no fake glow", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sections.Sections["negativeConstraints"], v => v.Contains("no distortion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optimizer_removes_duplicates_but_preserves_critical_rules()
    {
        var sections = new PromptSections { Sections = new Dictionary<string, List<string>> { ["negativeConstraints"] = ["no fake glow", "no fake glow", "no distortion"], ["astronomicalRendering"] = ["perfectly circular planet geometry", "realistic rendering"] } };

        var optimized = new PromptOptimizer().Optimize(sections, new GenericImageProviderProfile());

        Assert.Equal(2, optimized.Sections["negativeConstraints"].Count);
        Assert.Contains(optimized.Sections["negativeConstraints"], v => v == "no fake glow");
        Assert.Contains(optimized.Sections["astronomicalRendering"], v => v.Contains("perfectly circular", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(optimized.Diagnostics, d => d.Code == "prompt_optimizer.duplicates_removed");
    }

    [Fact]
    public void GenericProviderAdapter_creates_prompt_text_and_embeds_negative_when_unsupported()
    {
        var sections = new PromptSections { Sections = new Dictionary<string, List<string>> { ["sceneSummary"] = ["premium astronomy scene"], ["negativeConstraints"] = ["no fake glow"] } };

        var result = new GenericProviderAdapter().Adapt(sections, new GenericImageProviderProfile());

        Assert.Contains("sceneSummary", result.Prompt);
        Assert.Contains("negativeConstraints", result.Prompt);
        Assert.Equal(string.Empty, result.NegativePrompt);
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.negative_prompt_unsupported");
    }

    [Fact]
    public void Negative_prompt_appears_only_when_capability_supports_it()
    {
        var sections = new PromptSections { Sections = new Dictionary<string, List<string>> { ["sceneSummary"] = ["scene"], ["negativeConstraints"] = ["no distortion"] } };
        var adapter = new GenericProviderAdapter();

        var unsupported = adapter.Adapt(sections, new GenericImageProviderProfile());
        var supported = adapter.Adapt(sections, new TestProviderProfile());

        Assert.Equal(string.Empty, unsupported.NegativePrompt);
        Assert.Equal("no distortion", supported.NegativePrompt);
        Assert.DoesNotContain("negativeConstraints", supported.Prompt);
    }

    [Fact]
    public void AzureProviderAdapter_preserves_sections_and_inlines_negative_constraints()
    {
        var sections = new PromptSections
        {
            Sections = new Dictionary<string, List<string>>
            {
                ["sceneSummary"] = ["premium astronomy scene"],
                ["heroSubject"] = ["Jupiter and Venus"],
                ["supportingSubjects"] = ["moonlit horizon"],
                ["composition"] = ["rule of thirds"],
                ["astronomicalRendering"] = ["Jupiter remains circular"],
                ["brandStyle"] = ["Drashyam", "premiumDocumentary"],
                ["typography"] = ["minimalEssentialTextOnly"],
                ["observationCard"] = ["lowerThirdSafeZone"],
                ["qualityTargets"] = ["observation", "overall threshold: 0.82"],
                ["negativeConstraints"] = ["no fake glow", "no distorted planets"]
            }
        };

        var result = new AzurePromptProviderAdapter().Adapt(sections, new AzureImageProviderProfile());

        Assert.Contains("Scene summary: premium astronomy scene", result.Prompt);
        Assert.Contains("Hero subject: Jupiter and Venus", result.Prompt);
        Assert.Contains("Supporting subjects: moonlit horizon", result.Prompt);
        Assert.Contains("Composition: rule of thirds", result.Prompt);
        Assert.Contains("Astronomical rendering rules: Jupiter remains circular", result.Prompt);
        Assert.Contains("Brand style: Drashyam; premiumDocumentary", result.Prompt);
        Assert.Contains("Observation card: lowerThirdSafeZone", result.Prompt);
        Assert.Contains("Quality targets: observation; overall threshold: 0.82", result.Prompt);
        Assert.Contains("Avoid / do not include: no fake glow; no distorted planets", result.Prompt);
        Assert.Equal(string.Empty, result.NegativePrompt);
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.used");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.negative_prompt_not_supported");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.constraints_inlined");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.prompt_length_optimized");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.formatting_applied");
    }

    [Fact]
    public async Task PromptComposer_can_produce_AzureImage_prompt_package_without_provider_call()
    {
        var direction = await CreateDirectionAsync();
        var composer = CreateAzureComposer(usePromptComposer: true);

        var result = await composer.ComposeAsync(direction.Cdl, direction.CreativeDirectionContract, ImageProviderType.AzureImage);

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.NotNull(result.PromptPackage);
        Assert.Equal(ImageProviderType.AzureImage, result.PromptPackage!.ProviderName);
        Assert.Equal(VisualIntelligenceContractVersions.AzureImageProviderProfileVersion, result.PromptPackage.ProviderProfileVersion);
        Assert.Contains("Hero subject:", result.PromptPackage.PositivePrompt);
        Assert.Contains("Astronomical rendering rules:", result.PromptPackage.PositivePrompt);
        Assert.Contains("Brand style:", result.PromptPackage.PositivePrompt);
        Assert.Contains("Avoid / do not include:", result.PromptPackage.PositivePrompt);
        Assert.Equal(string.Empty, result.PromptPackage.NegativePrompt);
        Assert.Contains(result.PromptPackage.ProviderParameters, kv => kv.Key == "adapter" && (string)kv.Value! == "azureImage");
        Assert.Contains(result.PromptPackage.ProviderParameters, kv => kv.Key == "azureSdkCallMade" && (bool)kv.Value! == false);
        Assert.Contains(result.PromptPackage.ProviderParameters, kv => kv.Key == "imageGenerationCallMade" && (bool)kv.Value! == false);
        Assert.Contains(result.Diagnostics, d => d.Code == "prompt_composer_v2.provider_profile_resolved");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.azure_image.used");
    }

    [Fact]
    public async Task Unknown_provider_falls_back_to_generic_profile()
    {
        var direction = await CreateDirectionAsync();
        var composer = CreateComposer(usePromptComposer: true);

        var result = await composer.ComposeAsync(direction.Cdl, direction.CreativeDirectionContract, ImageProviderType.AzureImage2);

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.Equal(VisualIntelligenceContractVersions.GenericProviderProfileVersion, result.PromptPackage!.ProviderProfileVersion);
        Assert.Contains(result.Diagnostics, d => d.Code == "image_provider_profile.generic_fallback_used");
    }

    [Fact]
    public async Task PromptPackage_contains_version_metadata()
    {
        var direction = await CreateDirectionAsync();
        var result = await CreateComposer(usePromptComposer: true).ComposeAsync(direction.Cdl, direction.CreativeDirectionContract);

        Assert.Equal(VisualIntelligenceContractVersions.PromptComposerVersion, result.PromptPackage!.PromptComposerVersion);
        Assert.Equal(VisualIntelligenceContractVersions.CdlVersion, result.PromptPackage.CdlVersion);
        Assert.Equal(VisualIntelligenceContractVersions.BrandVersion, result.PromptPackage.BrandVersion);
        Assert.Equal(VisualIntelligenceContractVersions.RenderingRulesVersion, result.PromptPackage.RenderingVersion);
        Assert.Equal(VisualIntelligenceContractVersions.QualityReportVersion, result.PromptPackage.QualityTargetVersion);
    }

    [Fact]
    public async Task UsePromptComposerV2_false_results_in_disabled_behavior()
    {
        var result = await CreateComposer(usePromptComposer: false).ComposeAsync(new CDL(), new CreativeDirectionContract());

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Disabled, result.Status);
        Assert.Null(result.PromptPackage);
        Assert.Contains(result.Diagnostics, d => d.Code == "prompt_composer_v2.disabled");
    }

    [Fact]
    public async Task PromptComposer_makes_no_azure_or_image_provider_call()
    {
        var direction = await CreateDirectionAsync();
        var composer = CreateComposer(usePromptComposer: true);

        var result = await composer.ComposeAsync(direction.Cdl, direction.CreativeDirectionContract, ImageProviderType.AzureImage2);

        Assert.NotNull(result.PromptPackage);
        Assert.Contains(result.PromptPackage!.ProviderParameters, kv => kv.Key == "adapter" && (string)kv.Value! == "generic");
        Assert.Contains(result.Diagnostics, d => d.Code == "provider_adapter.generic_used");
    }

    private static IPromptComposerV2 CreateComposer(bool usePromptComposer)
    {
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile()]);
        return new PromptComposerV2(Options.Create(new VisualIntelligenceOptions { UsePromptComposerV2 = usePromptComposer }), new PromptSectionBuilder(), new PromptOptimizer(), new GenericProviderAdapter(), new PromptPackageBuilder(), registry);
    }

    private static IPromptComposerV2 CreateAzureComposer(bool usePromptComposer)
    {
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile(), new AzureImageProviderProfile()]);
        var adapter = new ProviderAdapterResolver([new AzurePromptProviderAdapter(), new GenericProviderAdapter()]);
        return new PromptComposerV2(Options.Create(new VisualIntelligenceOptions { UsePromptComposerV2 = usePromptComposer }), new PromptSectionBuilder(), new PromptOptimizer(), adapter, new PromptPackageBuilder(), registry);
    }

    private static Task<VisualCreativeDirectorResult> CreateDirectionAsync() => new VisualCreativeDirector(Microsoft.Extensions.Logging.Abstractions.NullLogger<VisualCreativeDirector>.Instance).CreateDirectionAsync(new VisualIntelligenceOrchestrationContext
    {
        CorrelationId = "prompt-composer-test",
        EventFamily = ContractEventFamily.PlanetConjunction,
        EventType = "planet-pairing",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        PrimaryObjects = ["Jupiter", "Venus"],
        FeatureFlags = new VisualIntelligenceFlagSnapshot { UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true }
    });

    private sealed class TestProviderProfile : IImageProviderProfile
    {
        public string ProviderName => "test";
        public ImageProviderType ProviderType => ImageProviderType.ExternalProvider;
        public string ProviderProfileVersion => "test-profile-v1";
        public ImageProviderCapabilities Capabilities { get; } = new() { SupportsNegativePrompt = true };
        public string DefaultPromptStrategy => "plainText";
        public IReadOnlyList<string> ProviderNotes => [];
        public IReadOnlyList<DiagnosticMessage> Diagnostics => [];
    }
}
