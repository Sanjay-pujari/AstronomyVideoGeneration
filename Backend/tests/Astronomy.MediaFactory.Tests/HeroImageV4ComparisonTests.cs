using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class HeroImageV4ComparisonTests
{
    [Fact]
    public async Task Flag_false_generates_no_comparison_artifacts()
    {
        using var temp = new TempRun();
        await RunAsync(temp.Root, useComparison: false, generator: new FakeGenerator(configured: true));

        Assert.False(Directory.Exists(Path.Combine(temp.Root, "hero", "comparison")));
        var reviewPath = Path.Combine(temp.Root, "hero", "diagnostics", "HeroCreativeReview.json");
        Assert.True(File.Exists(reviewPath));
        var review = await File.ReadAllTextAsync(reviewPath);
        Assert.Contains("compositionTemplateUsed", review);
        Assert.Contains("relationshipScore", review);
        Assert.Contains("documentaryScore", review);
        Assert.Contains("astronomyScore", review);
        Assert.Contains("visualHierarchyScore", review);
        Assert.Contains("storytellingNotes", review);
        Assert.Contains("recommendations", review);
    }

    [Fact]
    public async Task Flag_true_generates_comparison_artifacts_without_changing_production_hero()
    {
        using var temp = new TempRun();
        var productionHeroPath = Path.Combine(temp.Root, "hero", "hero.png");
        var before = await File.ReadAllBytesAsync(productionHeroPath);

        await RunAsync(temp.Root, useComparison: true, generator: new FakeGenerator(configured: true));

        var comparison = Path.Combine(temp.Root, "hero", "comparison");
        Assert.True(File.Exists(Path.Combine(comparison, "hero-v3-prompt.txt")));
        Assert.True(File.Exists(Path.Combine(comparison, "hero-v4-prompt.txt")));
        Assert.True(File.Exists(Path.Combine(comparison, "hero-v3.png")));
        Assert.True(File.Exists(Path.Combine(comparison, "hero-v4.png")));
        Assert.True(File.Exists(Path.Combine(comparison, "hero-side-by-side.png")));
        Assert.True(File.Exists(Path.Combine(comparison, "hero-comparison.json")));
        Assert.True(File.Exists(Path.Combine(comparison, "HeroCreativeReview.json")));
        Assert.Equal(before, await File.ReadAllBytesAsync(productionHeroPath));
        Assert.NotEqual(Path.GetFullPath(productionHeroPath), Path.GetFullPath(Path.Combine(comparison, "hero-v4.png")));
        Assert.Contains("ManualReviewRequired", await File.ReadAllTextAsync(Path.Combine(comparison, "hero-comparison.json")));
        Assert.Contains("compositionTemplateUsed", await File.ReadAllTextAsync(Path.Combine(comparison, "HeroCreativeReview.json")));
        var v4Prompt = await File.ReadAllTextAsync(Path.Combine(comparison, "hero-v4-prompt.txt"));
        Assert.DoesNotContain("HeroSubject", v4Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Supporting subject: Jupiter Venus", v4Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; ;", v4Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("preserve circular geometry; ; ; ;", v4Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jupiter and Venus together as the hero", v4Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"v4Generated\": true", await File.ReadAllTextAsync(Path.Combine(comparison, "hero-comparison.json")));
    }

    [Fact]
    public async Task Comparison_failure_is_non_blocking()
    {
        using var temp = new TempRun(createHero: false);
        var result = await RunAsync(temp.Root, useComparison: true, generator: new FakeGenerator(configured: true));

        Assert.Equal(VisualIntelligenceOrchestrationStatus.Success, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "hero_image_v4_comparison.failed");
    }

    private static async Task<VisualIntelligenceOrchestrationResult> RunAsync(string root, bool useComparison, IAICinematicImageGenerator generator)
    {
        var options = Options.Create(new VisualIntelligenceOptions
        {
            Enabled = true,
            WriteDiagnostics = false,
            UseVisualCreativeDirector = true,
            UseCDL = true,
            UseCreativeDirectionContract = true,
            UsePromptComposerV2 = true,
            UseProviderProfiles = true,
            DefaultProvider = ImageProviderType.AzureImage,
            UseHeroImageV4Comparison = useComparison
        });
        var registry = new ImageProviderProfileRegistry([new GenericImageProviderProfile(), new AzureImageProviderProfile()]);
        var composer = new PromptComposerV2(options, new PromptSectionBuilder(), new PromptOptimizer(), new ProviderAdapterResolver([new AzurePromptProviderAdapter(), new GenericProviderAdapter()]), new PromptPackageBuilder(), registry);
        var orchestrator = new VisualIntelligenceOrchestrator(options, new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance), composer, null, NullLogger<VisualIntelligenceOrchestrator>.Instance, generator);
        return await orchestrator.OrchestrateAsync(new VisualIntelligenceOrchestrationRequest
        {
            CorrelationId = "hero-v4-comparison-test",
            ContentGenerationPlanId = Guid.NewGuid(),
            EventFamily = ContractEventFamily.PlanetConjunction,
            EventType = "PlanetConjunction",
            EventName = "Jupiter and Venus",
            Platform = Platform.Hero,
            AspectRatio = AspectRatio.Landscape16x9,
            PrimaryObjects = ["Jupiter", "Venus"],
            RunOutputFolder = root,
            RequestedProvider = ImageProviderType.AzureImage
        });
    }

    private sealed class FakeGenerator(bool configured) : IAICinematicImageGenerator
    {
        public bool IsConfigured => configured;
        public string DeploymentName => configured ? "fake-azure-image" : string.Empty;
        public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath)!);
            using var image = new Image<Rgba32>(160, 90, Color.DarkBlue);
            await image.SaveAsPngAsync(request.PlannedImagePath, cancellationToken);
            return new AICinematicProviderResult("Generated", request.PlannedImagePath, true, []);
        }
    }

    private sealed class TempRun : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hero-v4-comparison-" + Guid.NewGuid().ToString("N"));
        public TempRun(bool createHero = true)
        {
            if (!createHero) { Directory.CreateDirectory(Root); return; }
            var hero = Path.Combine(Root, "hero");
            Directory.CreateDirectory(hero);
            using var image = new Image<Rgba32>(160, 90, Color.Black);
            image.SaveAsPng(Path.Combine(hero, "hero.png"));
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
