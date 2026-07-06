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
        Assert.Contains("UseHeroPromptV4=false", result.Recommendation);
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

    private static CreativeDirectionContract Contract() => new()
    {
        ContractId = "contract-hero-test",
        EventFamily = EventFamily.PlanetConjunction,
        TargetPlatform = Platform.Hero,
        AspectRatio = AspectRatio.Landscape16x9,
        VisualIntent = new VisualIntent { PrimarySubject = "Moon near Saturn", Mood = "premium documentary", Composition = "hero subject with observation card safe area" },
        BrandRules = new BrandRules { BrandName = "Drashyam", VisualTone = "premiumDocumentary", StylePrinciples = ["premium", "scientifically grounded"] },
        PlanetRenderingRules = new PlanetRenderingRules { Subjects = [new PlanetRenderingSubjectRule { BodyName = "Saturn", RequiredShape = "circular geometry" }] },
        TypographyRules = new TypographyRules { AllowedTextElements = ["title", "subtitle"], ForbiddenText = ["watermark"] },
        ObservationCardRules = new ObservationCardRules { AllowedFields = ["time", "direction"], Placement = "lower third" },
        ProviderHints = new ProviderHints { PreferredProvider = ImageProviderType.AzureImage },
        NegativeConstraints = new NegativeConstraints { Scientific = ["no fake geometry"], Typography = ["no embedded background text"] }
    };
}
