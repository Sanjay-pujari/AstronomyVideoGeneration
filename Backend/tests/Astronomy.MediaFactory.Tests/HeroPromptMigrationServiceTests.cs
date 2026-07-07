using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class HeroPromptMigrationServiceTests
{
    [Fact]
    public async Task Generates_v4_prompt_and_comparison_artifacts_without_changing_legacy_prompt()
    {
        var root = Path.Combine(Path.GetTempPath(), "hero-prompt-migration-" + Guid.NewGuid().ToString("N"));
        var legacy = "Visual intent: CinematicHero. Preserve circular planet geometry. Leave safe overlay space.";
        var service = CreateService(useHeroPromptV4: false);

        var result = await service.GenerateAsync(new HeroPromptMigrationRequest
        {
            CreativeDirectionContract = Contract(),
            LegacyPrompt = legacy,
            HeroDirectory = root,
            RequestedProvider = ImageProviderType.AzureImage
        });

        Assert.False(new VisualIntelligenceOptions().UseHeroPromptV4);
        Assert.False(Options.Create(new VisualIntelligenceOptions { UsePromptComposerV2 = true }).Value.UseHeroPromptV4);
        Assert.NotEmpty(result.V4Prompt);
        Assert.Equal(legacy, result.LegacyPrompt);
        Assert.Equal(legacy, await File.ReadAllTextAsync(Path.Combine(root, "hero-v3-prompt.txt")));
        Assert.True(File.Exists(Path.Combine(root, "hero-v4-prompt.txt")));
        Assert.True(File.Exists(Path.Combine(root, "hero-prompt-comparison.json")));
        Assert.True(File.Exists(Path.Combine(root, "hero-migration-report.json")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "hero-migration-report.json")));
        Assert.True(report.RootElement.GetProperty("productionHeroUnchanged").GetBoolean());
        Assert.False(report.RootElement.GetProperty("useHeroPromptV4").GetBoolean());
    }

    [Fact]
    public async Task Comparison_reports_preserved_constraints_for_visual_intelligence_prompt()
    {
        var service = CreateService(useHeroPromptV4: false);
        var result = await service.GenerateAsync(new HeroPromptMigrationRequest { CreativeDirectionContract = Contract(), LegacyPrompt = "legacy hero prompt" });

        Assert.True(result.Comparison.AstronomyConstraintsPreserved);
        Assert.True(result.Comparison.RenderingConstraintsPreserved);
        Assert.True(result.Comparison.BrandConstraintsPreserved);
        Assert.True(result.Comparison.TypographyPreserved);
        Assert.True(result.Comparison.ObservationCardPreserved);
        Assert.True(result.Comparison.LegacyPromptLength > 0);
        Assert.True(result.Comparison.V4PromptLength > 0);
        Assert.NotEmpty(result.Comparison.ReadabilityImprovement);
        Assert.Contains("UseHeroPromptV4=false", result.Recommendation);
    }

    [Fact]
    public async Task V4_prompt_is_natural_language_and_preserves_constraints_without_replacing_production_prompt()
    {
        var legacy = "legacy production hero prompt uses AzureHeroPromptBuilderV2";
        var result = await CreateService(useHeroPromptV4: false).GenerateAsync(new HeroPromptMigrationRequest { CreativeDirectionContract = Contract(), LegacyPrompt = legacy });

        Assert.Equal(legacy, result.LegacyPrompt);
        Assert.Contains("Create a premium astronomy hero image", result.V4Prompt);
        Assert.Contains("Primary subject:", result.V4Prompt);
        Assert.Contains("Render Saturn", result.V4Prompt);
        Assert.Contains("physically plausible illumination", result.V4Prompt);
        Assert.Contains("perfectly circular", result.V4Prompt);
        Assert.Contains("premium astronomy documentary", result.V4Prompt);
        Assert.Contains("Typography guidance:", result.V4Prompt);
        Assert.Contains("Observation guidance:", result.V4Prompt);
        Assert.Contains("Negative constraints:", result.V4Prompt);
        Assert.Contains("fake geometry", result.V4Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PromptSections", result.V4Prompt);
        Assert.DoesNotContain("CreativeDirectionContract", result.V4Prompt);
        Assert.DoesNotContain("providerHints", result.V4Prompt);
    }


    [Fact]
    public async Task V4_prompt_uses_hero_intelligence_contract_fields()
    {
        var legacy = "legacy production hero prompt";
        var contract = IntelligenceContract();

        var result = await CreateService(useHeroPromptV4: false).GenerateAsync(new HeroPromptMigrationRequest
        {
            CreativeDirectionContract = Contract(),
            HeroIntelligenceContract = contract,
            LegacyPrompt = legacy
        });

        Assert.Equal(legacy, result.LegacyPrompt);
        Assert.Contains(contract.ViewerQuestion, result.V4Prompt);
        Assert.Contains(contract.PrimaryStory, result.V4Prompt);
        Assert.Contains(contract.CompositionGoal, result.V4Prompt);
        Assert.Contains(contract.EditorialGoal, result.V4Prompt);
        Assert.Contains(contract.VisualRelationship, result.V4Prompt);
        Assert.Contains("relationship between objects", result.V4Prompt);
        Assert.DoesNotContain("HeroIntelligenceContract", result.V4Prompt);
        Assert.DoesNotContain(";;", result.V4Prompt);
    }

    [Fact]
    public async Task Comparison_json_contains_quality_cleanup_metrics()
    {
        var root = Path.Combine(Path.GetTempPath(), "hero-prompt-quality-" + Guid.NewGuid().ToString("N"));
        var result = await CreateService(useHeroPromptV4: false).GenerateAsync(new HeroPromptMigrationRequest { CreativeDirectionContract = Contract(), LegacyPrompt = "legacy hero prompt", HeroDirectory = root });

        using var comparison = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "hero-prompt-comparison.json")));
        Assert.True(comparison.RootElement.TryGetProperty("legacyPromptLength", out _));
        Assert.True(comparison.RootElement.TryGetProperty("v4PromptLength", out _));
        Assert.True(comparison.RootElement.TryGetProperty("duplicateReductionCount", out _));
        Assert.True(comparison.RootElement.TryGetProperty("estimatedTokenReduction", out _));
        Assert.True(comparison.RootElement.TryGetProperty("semanticSectionsMerged", out _));
        Assert.True(comparison.RootElement.TryGetProperty("readabilityImprovement", out _));
        Assert.True(comparison.RootElement.TryGetProperty("recommendation", out _));
        Assert.True(result.Comparison.V4PromptLength > 0);
    }

    private static IHeroPromptMigrationService CreateService(bool useHeroPromptV4)
    {
        var options = new VisualIntelligenceOptions { UsePromptComposerV2 = true, UseHeroPromptV4 = useHeroPromptV4 };
        var registry = new ImageProviderProfileRegistry([new AzureImageProviderProfile(), new GenericImageProviderProfile()]);
        return new HeroPromptMigrationService(
            Options.Create(options),
            new PromptComposerV2(Options.Create(options), new PromptSectionBuilder(), new PromptOptimizer(), new AzurePromptProviderAdapter(), new PromptPackageBuilder(), registry),
            NullLogger<HeroPromptMigrationService>.Instance);
    }

    private static HeroIntelligenceContract IntelligenceContract() => new()
    {
        ProductId = "hero-story-1",
        StoryId = "story-1",
        PlanId = "plan-1",
        EventType = "planet-conjunction",
        EventFamily = "PlanetConjunction",
        EditorialDecisionId = "story-1",
        VisualStoryId = "story-1",
        CompositionId = "composition-hero",
        EditorialStrategyId = "editorial-hero",
        ViewerQuestion = "Why do Jupiter and Venus look so close tonight?",
        PrimaryStory = "Jupiter and Venus form an apparent close pairing in the evening sky.",
        ViewerTakeaway = "The planets look close from Earth but are not physically close.",
        EmotionalHook = "Wonder.",
        CompositionGoal = "Balanced planets communicating their relationship.",
        EditorialGoal = "Stop scrolling.",
        ViewerEmotion = "Wonder.",
        VisualRelationship = "The apparent closeness is the subject; neither planet dominates.",
        DocumentaryTone = "documentary",
        RecommendedComposition = "Balanced planets communicating their relationship.",
        RecommendedTypography = "Existing Hero typography.",
        RecommendedInformationDensity = "Low",
        RecommendedVisualBalance = "Shared negative space.",
        PlatformRecommendations = new Dictionary<string, string> { ["landscape"] = "Use shared negative space around both planets." },
        ConfidenceSummary = new HeroIntelligenceConfidenceSummary(0.9, 0.9, 0.9, 0.9, null),
        CreativeConfidence = 0.9,
        Versions = new Dictionary<string, string> { ["editorialProductContract"] = "4.5D" }
    };

    private static CreativeDirectionContract Contract() => new()
    {
        ContractId = "contract-hero-test",
        EventFamily = EventFamily.PlanetConjunction,
        TargetPlatform = Platform.Hero,
        AspectRatio = AspectRatio.Landscape16x9,
        VisualIntent = new VisualIntent { PrimarySubject = "Moon near Saturn", Mood = "premium documentary", Composition = "RuleOfThirds" },
        BrandRules = new BrandRules { BrandName = "Drashyam", VisualTone = "premiumDocumentary", StylePrinciples = ["premium", "premium", "scientifically grounded"] },
        PlanetRenderingRules = new PlanetRenderingRules { Subjects = [new PlanetRenderingSubjectRule { BodyName = "Saturn", RequiredShape = "circular geometry", ColorBehavior = "naturally illuminated", SurfaceDetail = "realistic cloud bands where visible", Illumination = "physically plausible illumination", ForbiddenArtifacts = ["fake glow", "distorted planets", "cartoon planets"] }] },
        TypographyRules = new TypographyRules { AllowedTextElements = ["title", "subtitle"], ForbiddenText = ["watermark"] },
        ObservationCardRules = new ObservationCardRules { AllowedFields = ["time", "direction"], Placement = "lower third" },
        ProviderHints = new ProviderHints { PreferredProvider = ImageProviderType.AzureImage },
        NegativeConstraints = new NegativeConstraints { Scientific = ["no fake geometry"], Typography = ["no embedded background text"] }
    };
}
