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
        Assert.Contains("complete final astronomy thumbnail", prompt.Prompt);
        Assert.Contains("observation cards", prompt.NegativePrompt);
        Assert.Contains("icons", prompt.NegativePrompt);
        Assert.Contains("buttons", prompt.NegativePrompt);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void ThumbnailPromptWriterV9_JupiterVenus_RestoresV92PromptDensity(string language)
    {
        var landscape = new ThumbnailPromptWriterV9().Write(BuildJupiterVenusContract("landscape", language)).Prompt;
        var square = new ThumbnailPromptWriterV9().Write(BuildJupiterVenusContract("square", language)).Prompt;
        var portrait = new ThumbnailPromptWriterV9().Write(BuildJupiterVenusContract("portrait", language)).Prompt;
        var dateLabel = language == "hi" ? "तारीख" : "Date";
        var timeLabel = language == "hi" ? "सबसे अच्छा समय" : "Best Time";
        var directionLabel = language == "hi" ? "दिशा" : "Direction";
        var separationLabel = language == "hi" ? "दूरी" : "Separation";
        var equipmentLabel = language == "hi" ? "उपकरण" : "Equipment";
        var objectLabel = language == "hi" ? "वस्तु लेबल" : "Object labels";

        Assert.Contains("premium landscape glass observation card", landscape);
        Assert.Contains(dateLabel, landscape);
        Assert.Contains(timeLabel, landscape);
        Assert.Contains(directionLabel, landscape);
        Assert.Contains(separationLabel, landscape);
        Assert.Contains(equipmentLabel, landscape);
        Assert.Contains(objectLabel, landscape);
        if (language == "en")
        {
            Assert.Contains("Date: Jun 9, 2026", landscape);
            Assert.Contains("Best Time: 7:23 PM IST", landscape);
            Assert.Contains("Direction: West", landscape);
            Assert.Contains("Equipment: Naked Eye; Binoculars Optional", landscape);
            Assert.Contains("Separation: 1.63° Apart", landscape);
        }
        Assert.Contains("Do not render action-prompt text, long date ranges, technical wording, internal IDs, or footer tips", landscape);
        Assert.Contains("complete final astronomy thumbnail", landscape);
        Assert.DoesNotContain($"{(language == "hi" ? "उपशीर्षक" : "Subtitle")}:", landscape);
        Assert.DoesNotContain("Look tonight", landscape, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Watch tonight", landscape, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Today", landscape, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tomorrow", landscape, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("medium-density square guide fields", square);
        Assert.Contains(dateLabel, square);
        Assert.Contains(timeLabel, square);
        Assert.Contains(directionLabel, square);
        Assert.Contains(separationLabel, square);
        Assert.DoesNotContain("only one tiny hint", square);
        Assert.DoesNotContain("two compact facts", square);

        Assert.Contains("mobile-clean vertical guide fields", portrait);
        Assert.Contains(dateLabel, portrait);
        Assert.Contains(timeLabel, portrait);
        Assert.Contains(directionLabel, portrait);
        Assert.DoesNotContain(equipmentLabel, portrait);
        Assert.Contains("keep planets circular", portrait);
        Assert.DoesNotContain("only one tiny hint", portrait);
    }


    [Fact]
    public void ThumbnailFieldFormatter_MapsGuideCardDateWithoutDerivingFromBestTime()
    {
        var guideCard = new PlanetaryThumbnailGuideCardDto("Jun 9, 2026", "Jun 9, 2026 7:23 PM", "West After Sunset", "Naked eye; binoculars optional", "1.63°");
        var observation = new ThumbnailObservation(null, "", "", "", "", guideCard);

        var fields = ThumbnailFieldFormatter.Format(observation, "en");

        Assert.Equal("Jun 9, 2026", fields.Date);
        Assert.Equal("7:23 PM", fields.BestTime);
        Assert.Equal("West", fields.Direction);
        Assert.Equal("Naked Eye; Binoculars Optional", fields.Equipment);
        Assert.Equal("1.63° Apart", fields.Separation);
    }

    private static ThumbnailPromptContract BuildJupiterVenusContract(string aspect, string language)
    {
        var (name, ratio, w, h) = aspect switch { "portrait" => ("portrait", "9:16", 1080, 1920), "square" => ("square", "1:1", 1200, 1200), _ => ("landscape", "16:9", 1280, 720) };
        var hi = language == "hi";
        var title = hi ? "बृहस्पति शुक्र युति" : "Jupiter Venus Conjunction";
        var shortTitle = hi ? "बृहस्पति + शुक्र" : "Jupiter + Venus";
        var observation = new ProductionObservationInfo("PlanetConjunction", "Planetary", "IN-RJ", "Udaipur", "IST", null, true, "Visible", "Show", "2026-06-07", "19:30 IST", "2026-06-07 19:00–20:30 IST", "2026-06-07 19:00–20:30 IST", hi ? "पश्चिमी आकाश" : "western sky after sunset", "25° altitude", [], "Close conjunction", "test", "High", [], []);
        var guideCard = new PlanetaryThumbnailGuideCardDto("Jun 9, 2026", "Jun 9, 2026 7:23 PM", "West After Sunset", "Naked eye; binoculars optional", "1.63°");
        return new ThumbnailPromptContract("1.0", new ThumbnailEventIdentity("JUPITER_VENUS_2026", "Jupiter Venus Conjunction", "Viewing guide", "Planetary", "Conjunction"), new ThumbnailDisplay(title, title, shortTitle, [title]), new ThumbnailObjects(["Jupiter", "Venus"], [], new Dictionary<string, string> { ["Jupiter"] = hi ? "बृहस्पति" : "Jupiter", ["Venus"] = hi ? "शुक्र" : "Venus" }), new ThumbnailObservation(observation, "2026-06-07", "2026-06-07 19:00–20:30 IST", hi ? "पश्चिमी आकाश" : "western sky after sunset", "minimum angular separation 1.63 degrees; visible if skies are clear", guideCard), new ThumbnailVisual("cinematic", "wonder", "Observation guide", "recognition"), new ThumbnailPlatform("Thumbnail", ratio, name, w, h), new ThumbnailPromptInstructions("clean astronomy final thumbnail", ThumbnailArtworkPromptRules.NegativePrompt, ["Jupiter", "Venus"], []), new ThumbnailBrand("AI integrated typography", "Discovery Dark Gold", [language]), new ThumbnailValidation(["layout"], ["scientific truth"], ["safe area"]), new ThumbnailPromptDiagnostics("test", "test", "Planetary", "summary", DateTimeOffset.UtcNow));
    }

    private static ThumbnailPromptContract BuildContract(string family, string aspect, string language)
    {
        var (name, ratio, w, h) = aspect switch { "portrait" => ("portrait", "9:16", 1080, 1920), "square" => ("square", "1:1", 1200, 1200), _ => ("landscape", "16:9", 1280, 720) };
        var solar = family.Contains("Solar", StringComparison.OrdinalIgnoreCase);
        var title = language == "hi" ? family switch { "Moon" => "चंद्रमा", "Meteor" => "उल्का वर्षा", "Solar Eclipse" => "सूर्य ग्रहण", _ => "ग्रह युति" } : family;
        return new ThumbnailPromptContract("1.0", new ThumbnailEventIdentity(family, family, "Viewing guide", family, family), new ThumbnailDisplay(title, title, title, [title]), new ThumbnailObjects([family], [], new Dictionary<string, string> { [family] = title }), new ThumbnailObservation(null, "Jun 9, 2026", "7:23 PM IST", "West", "Visible if skies are clear", new PlanetaryThumbnailGuideCardDto("Jun 9, 2026", "7:23 PM IST", "West", "Naked eye", null)), new ThumbnailVisual("cinematic", "wonder", "Observation guide", "recognition"), new ThumbnailPlatform("Thumbnail", ratio, name, w, h), new ThumbnailPromptInstructions("clean astronomy artwork", ThumbnailArtworkPromptRules.NegativePrompt, [family], []), new ThumbnailBrand("Renderer typography only", "Discovery Dark Gold", [language]), new ThumbnailValidation(["layout"], [solar ? "Solar safety required" : "scientific truth"], ["safe area"]), new ThumbnailPromptDiagnostics("test", "test", family, "summary", DateTimeOffset.UtcNow));
    }
}
