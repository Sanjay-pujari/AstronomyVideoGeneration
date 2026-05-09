using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void DailySkyGuide_prompt_includes_English_localization_instruction()
    {
        var context = CreateContext("en");
        var prompt = new PromptBuilder().Build(ContentType.DailySkyGuide, context);

        Assert.Contains("language code: en", prompt);
        Assert.Contains("English", prompt);
    }

    [Fact]
    public void DailySkyGuide_prompt_includes_Hindi_localization_instruction()
    {
        var context = CreateContext("hi");
        var prompt = new PromptBuilder().Build(ContentType.DailySkyGuide, context);

        Assert.Contains("language code: hi", prompt);
        Assert.Contains("Hindi", prompt);
        Assert.Contains("बृहस्पति (Jupiter)", prompt);
    }

    [Fact]
    public void SpecialEventGuide_prompt_supports_Hindi_localization()
    {
        var context = CreateContext("hi");
        context.SpecialEvent = new SpecialEventContext
        {
            EventId = "perseids",
            EventType = "meteor_shower",
            EventTitle = "Perseids meteor shower",
            EventDescription = "A high-value meteor shower event.",
            ContentOpportunityScore = 0.9
        };

        var prompt = new PromptBuilder().Build(ContentType.SpecialEventGuide, context);

        Assert.Contains("SpecialEventGuide", prompt);
        Assert.Contains("language code: hi", prompt);
        Assert.Contains("बृहस्पति (Jupiter)", prompt);
    }

    [Fact]
    public async Task Seo_metadata_generates_English_by_default()
    {
        var result = await new SeoMetadataGeneratorService().GenerateAsync(CreateSeoRequest("en"), default);

        Assert.Contains("Tonight's Sky", result.Title);
        Assert.Contains("Location:", result.Description);
    }

    [Fact]
    public async Task Seo_metadata_generates_Hindi_when_requested()
    {
        var result = await new SeoMetadataGeneratorService().GenerateAsync(CreateSeoRequest("hi"), default);

        Assert.Contains("आज रात", result.Title);
        Assert.Contains("स्थान:", result.Description);
    }

    [Fact]
    public void Unsupported_language_falls_back_to_English()
    {
        var resolved = LocalizationResolver.Resolve("fr", new LocalizationOptions
        {
            Enabled = true,
            DefaultLanguage = "en",
            SupportedLanguages = ["en", "hi"],
            FallbackLanguage = "en"
        });

        Assert.Equal("fr", resolved.RequestedLanguage);
        Assert.Equal("en", resolved.ResolvedLanguage);
        Assert.True(resolved.FallbackUsed);
    }

    private static AstronomyContext CreateContext(string language)
    {
        var context = new AstronomyContext
        {
            Date = new DateOnly(2026, 5, 4),
            LocationName = "Udaipur",
            TimeZone = "Asia/Kolkata",
            Localization = new LocalizationContext(language, language, false)
        };
        context.Events.Add(new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Evening", Direction = "West", ObservationTool = "Naked eye", Details = "Bright planet", Score = 0.9 });
        return context;
    }

    private static SeoMetadataRequest CreateSeoRequest(string language)
        => new()
        {
            SceneObservationContext =
            [
                new SceneObservationContext { ObjectName = "Jupiter", LocalObservationTime = new DateTime(2026, 5, 4, 20, 15, 0), Timezone = "Asia/Kolkata", DirectionLabel = "West", AltitudeDegrees = 31.2 }
            ],
            SelectedVisibleObjects = ["Jupiter"],
            LocationName = "Udaipur",
            TargetDate = new DateOnly(2026, 5, 4),
            IsShortForm = false,
            Language = language
        };
}
