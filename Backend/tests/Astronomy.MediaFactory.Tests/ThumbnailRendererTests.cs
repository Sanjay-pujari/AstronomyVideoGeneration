using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailRendererTests
{
    public static IEnumerable<object[]> RegressionCases =>
        new[] { "Moon", "Meteor", "Planet Pairing", "Solar Eclipse" }
            .SelectMany(family => new[] { "landscape", "portrait", "square" }
                .SelectMany(aspect => new[] { "en", "hi" }
                    .Select(language => new object[] { family, aspect, language })));

    [Theory]
    [MemberData(nameof(RegressionCases))]
    public async Task ThumbnailRenderer_RendersDeterministicPresentation_AndLayoutManifest(string family, string aspect, string language)
    {
        var contract = BuildContract(family, aspect, language);
        var profile = ThumbnailCompositionProfiles.Resolve(contract);
        var strategy = PlatformStorytellingStrategies.Resolve(contract);
        var temp = Path.Combine(Path.GetTempPath(), "thumbnail-renderer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var artwork = Path.Combine(temp, "artwork.png");
        using (var image = new Image<Rgba32>(contract.Platform.Width, contract.Platform.Height, new Rgba32(3, 8, 24)))
            await image.SaveAsPngAsync(artwork);

        var result = await new ThumbnailRenderer().RenderAsync(new ThumbnailRendererInput(artwork, Path.Combine(temp, "thumbnail.png"), Path.Combine(temp, "thumbnail-render-layout.json"), contract, strategy, profile, ThumbnailTheme.DiscoveryDarkGold), CancellationToken.None);

        Assert.True(File.Exists(result.ImagePath));
        Assert.True(File.Exists(result.LayoutJsonPath));
        Assert.Contains(result.Components, c => c.Component == "Title" && c.Renderer == "TypographyRenderer");
        Assert.Contains(result.Components, c => c.Component == "Observation" && c.Renderer == "ObservationCardRenderer");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.LayoutJsonPath));
        Assert.Equal("Discovery Dark Gold", json.RootElement.GetProperty("theme").GetString());
        Assert.True(json.RootElement.GetProperty("components").GetArrayLength() >= 7);
    }

    [Fact]
    public void ThumbnailPromptBuilder_AddsArtworkOnlyRules()
    {
        var contract = BuildContract("Moon", "landscape", "en");
        var prompt = new ThumbnailPromptBuilder().Build(contract);
        Assert.Contains("clean cinematic background artwork only", prompt.Prompt);
        Assert.Contains("observation cards", prompt.NegativePrompt);
        Assert.Contains("icons", prompt.NegativePrompt);
        Assert.Contains("buttons", prompt.NegativePrompt);
    }

    private static ThumbnailPromptContract BuildContract(string family, string aspect, string language)
    {
        var (name, ratio, w, h) = aspect switch { "portrait" => ("portrait", "9:16", 1080, 1920), "square" => ("square", "1:1", 1200, 1200), _ => ("landscape", "16:9", 1280, 720) };
        var solar = family.Contains("Solar", StringComparison.OrdinalIgnoreCase);
        var title = language == "hi" ? family switch { "Moon" => "चंद्रमा", "Meteor" => "उल्का वर्षा", "Solar Eclipse" => "सूर्य ग्रहण", _ => "ग्रह युति" } : family;
        return new ThumbnailPromptContract("1.0", new ThumbnailEventIdentity(family, family, "Viewing guide", family, family), new ThumbnailDisplay(title, title, title, [title]), new ThumbnailObjects([family], [], new Dictionary<string, string> { [family] = title }), new ThumbnailObservation(null, "Tonight", "After sunset", "West", "Visible if skies are clear"), new ThumbnailVisual("cinematic", "wonder", "Observation guide", "recognition"), new ThumbnailPlatform("Thumbnail", ratio, name, w, h), new ThumbnailPromptInstructions("clean astronomy artwork", ThumbnailArtworkPromptRules.NegativePrompt, [family], []), new ThumbnailBrand("Renderer typography only", "Discovery Dark Gold", [language]), new ThumbnailValidation(["layout"], [solar ? "Solar safety required" : "scientific truth"], ["safe area"]), new ThumbnailPromptDiagnostics("test", "test", family, "summary", DateTimeOffset.UtcNow));
    }
}
