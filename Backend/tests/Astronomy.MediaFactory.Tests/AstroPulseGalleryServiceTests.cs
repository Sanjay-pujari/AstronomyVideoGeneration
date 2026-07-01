using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstroPulseGalleryServiceTests
{
    [Fact]
    public async Task GenerateGalleryAsync_RequiresConfiguredAzureImage2()
    {
        var root = Path.Combine(Path.GetTempPath(), $"astropulse-gallery-{Guid.NewGuid():N}");
        var service = new AstroPulseGalleryService(Options.Create(new AzureOpenAIForImageOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateGalleryAsync(root, AstroPulseGalleryAspect.Landscape, CancellationToken.None));

        Assert.Contains("Phase 13 Gallery V3 requires Azure Image2 configuration", ex.Message);
    }

    [Fact]
    public void GalleryV3_ResultContract_IncludesValidationPath()
    {
        var result = new AstroPulseGalleryResult("gallery", ["gallery/gallery-01.png"], "gallery/gallery-review.json", "gallery/gallery-manifest.json", "gallery/gallery-generation-diagnostics.json", "gallery/phase-13-validation.json");

        Assert.Equal("gallery/phase-13-validation.json", result.ValidationPath);
    }
    [Theory]
    [MemberData(nameof(RequiredGalleryCoverage))]
    public void GalleryV3_Topics_PreserveEducationalSequence_ForRequiredEventLanguageAndAspectCoverage(string eventType, string language, AstroPulseGalleryAspect aspect)
    {
        var context = new AstroPulseGalleryService.GalleryContext(eventType, eventType, "story", "visual", "July 1, 2026", "9 PM", "US", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues(eventType, eventType, [eventType], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);

        Assert.True(aspect.Width > 0);
        Assert.True(aspect.Height > 0);
        Assert.Equal(6, topics.Count);
        Assert.Equal(Enumerable.Range(1, 6), topics.Select(t => t.Number));
        Assert.All(topics, topic => Assert.Contains("Educational role", topic.AzureImage2Prompt));
        Assert.All(topics, topic => Assert.Contains("one educational idea per slide", topic.AzureImage2Prompt));
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("hi", true)]
    public void GalleryV3_Topics_LocalizeMetadataAndOverlayText(string language, bool expectHindi)
    {
        var context = new AstroPulseGalleryService.GalleryContext("Meteor Shower", "Meteor Shower", "story", "visual", "July 1, 2026", "9 PM", "US", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("Meteor Shower", "Meteor Shower", ["Perseids"], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var text = string.Join(" ", topics.SelectMany(t => t.TextBlocks));

        Assert.Equal(expectHindi, text.Any(c => c >= '\u0900' && c <= '\u097F'));
    }

    [Theory]
    [InlineData("en", "Date: Dec 14, 2026", "Time: After midnight to pre-dawn")]
    [InlineData("hi", "तारीख: 14 दिस॰ 2026", "समय: आधी रात के बाद से भोर तक")]
    public void GalleryV3_Topics_FormatMeteorTimestampsAsObservationWindowText(string language, string expectedDate, string expectedTime)
    {
        var context = new AstroPulseGalleryService.GalleryContext("Meteor Shower", "Meteor Shower", "story", "visual", "2026-12-14T06:00:00+00:00", "2026-12-14T18:00:00+00:00", "India", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("Meteor Shower", "Meteor Shower", ["Perseids"], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var text = string.Join(" ", topics.SelectMany(t => t.TextBlocks));

        Assert.Contains(expectedDate, text);
        Assert.Contains(expectedTime, text);
        Assert.DoesNotContain("2026-12-14T", text);
        Assert.DoesNotContain("+00:00", text);
        Assert.DoesNotContain("11:30 AM IST", text);
        Assert.DoesNotContain("सुबह 11:30 बजे IST", text);
    }


    [Fact]
    public void GalleryV3_Topics_MeteorDaytimePeakUsesNightObservationGuidance()
    {
        var context = new AstroPulseGalleryService.GalleryContext("MeteorShower", "Geminids Meteor Shower Peak", "story", "visual", "2026-12-14T06:00:00+00:00", "2026-12-14T06:00:00+00:00", "India", "en", "en", "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("MeteorShower", "Geminids Meteor Shower Peak", ["Geminids"], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var text = string.Join(" ", topics.SelectMany(t => t.TextBlocks));

        Assert.Contains("Date: Dec 14, 2026", text);
        Assert.Contains("Time: After midnight to pre-dawn", text);
        Assert.DoesNotContain("11:30 AM IST", text);
    }

    [Fact]
    public void GalleryV3_Topics_NonMeteorFamilyStillUsesEventSpecificLocalTime()
    {
        var context = new AstroPulseGalleryService.GalleryContext("SolarEclipse", "Solar Eclipse", "story", "visual", "2026-08-12T17:30:00+00:00", "2026-08-12T17:30:00+00:00", "India", "en", "en", "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("SolarEclipse", "Solar Eclipse", ["Sun", "Moon"], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var text = string.Join(" ", topics.SelectMany(t => t.TextBlocks));

        Assert.Contains("Date: Aug 12, 2026", text);
        Assert.Contains("Time: 11:00 PM IST", text);
        Assert.DoesNotContain("After midnight to pre-dawn", text);
    }

    [Theory]
    [MemberData(nameof(GalleryAspects))]
    public void GalleryV3_Aspects_CoverLandscapePortraitAndSquare(AstroPulseGalleryAspect aspect)
    {
        Assert.True(aspect.Width > 0);
        Assert.True(aspect.Height > 0);
    }


    [Theory]
    [InlineData("MeteorShower", "en", false)]
    [InlineData("MeteorShower", "hi", true)]
    [InlineData("SolarEclipse", "hi", true)]
    public void GalleryV3_Phase13Only_PreservesRequestedLanguageForAcceptanceEvents(string eventType, string language, bool expectHindi)
    {
        var context = new AstroPulseGalleryService.GalleryContext(eventType, eventType, "story", "visual", "2026-12-14T06:00:00+00:00", "2026-12-14T18:00:00+00:00", "India", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues(eventType, eventType, [eventType], [], [], []), []);

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var overlayText = string.Join(" ", topics.SelectMany(t => t.TextBlocks).Concat(topics.Select(t => t.LocalizedEducationalRole)).Concat(topics.Select(t => t.FooterLabel)));

        Assert.Equal(expectHindi, overlayText.Any(c => c >= '\u0900' && c <= '\u097F'));
        if (expectHindi)
        {
            Assert.DoesNotContain("Opening view", overlayText);
            Assert.DoesNotContain("What happens", overlayText);
            Assert.DoesNotContain("Where to look", overlayText);
            Assert.DoesNotContain("When to observe", overlayText);
            Assert.DoesNotContain("Key objects", overlayText);
            Assert.DoesNotContain("Viewing checklist", overlayText);
            Assert.DoesNotContain("2026-12-14T", overlayText);
            Assert.Contains("तारीख", overlayText);
            Assert.Contains("समय", overlayText);
        }
    }

    [Theory]
    [InlineData("Wolf Moon", "en", "Wolf Moon", "cold winter")]
    [InlineData("Wolf Moon", "hi", "वुल्फ पूर्णिमा", "cold winter")]
    [InlineData("Strawberry Moon", "en", "Strawberry Moon", "rose-gold")]
    [InlineData("Strawberry Moon", "hi", "स्ट्रॉबेरी पूर्णिमा", "rose-gold")]
    public void GalleryV3_Phase13Only_MoonEventsUseSpecificTitlesAndVisualCues(string eventTitle, string language, string expectedTitle, string expectedPromptCue)
    {
        var context = new AstroPulseGalleryService.GalleryContext("FullMoon", eventTitle, "story", "visual", "2026-01-03T06:00:00+00:00", "2026-01-03T18:00:00+00:00", "India", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("FullMoon", eventTitle, [eventTitle, "Moon"], [], [], []), [], eventTitle, "FullMoon");

        var topics = AstroPulseGalleryService.BuildTopics(context);
        var overlayText = string.Join(" ", topics.SelectMany(t => t.TextBlocks));
        var promptText = string.Join(" ", topics.Select(t => t.AzureImage2Prompt));
        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(AstroPulseGalleryService.BuildGalleryLocalizationDiagnostics(context, topics, AstroPulseGalleryAspect.Landscape));

        Assert.Contains(expectedTitle, overlayText);
        Assert.DoesNotContain("Moon Guide", overlayText);
        Assert.DoesNotContain("चंद्रमा गाइड", overlayText);
        Assert.Contains(expectedPromptCue, promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localizedEventTitle", diagnosticsJson);
        Assert.Contains("eventSubtype", diagnosticsJson);
        Assert.Contains("moonSubtypeVisualAttributes", diagnosticsJson);
    }

    [Theory]
    [InlineData("hi", "मंगल और बृहस्पति की करीबी जोड़ी", "मंगल", "बृहस्पति")]
    [InlineData("en", "Mars and Jupiter Close Pairing", "Mars", "Jupiter")]
    public void GalleryContentContract_PlanetPairing_ResolvesLocalizedTitleObjectsAndProvider(string language, string expectedTitle, string expectedMars, string expectedJupiter)
    {
        var context = new AstroPulseGalleryService.GalleryContext("PlanetPairing", "Mars and Jupiter Close Pairing", "story", "visual", "2026-08-12T00:30:00+00:00", "2026-08-12T05:30:00+05:30", "East", language, language, "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("PlanetPairing", "Mars and Jupiter Close Pairing", ["Mars", "Jupiter"], [], [], []), [], "Mars and Jupiter Close Pairing", "PlanetGrouping");

        var contract = AstroPulseGalleryService.ResolveGalleryContentContractForTesting(context);
        var promptText = string.Join(" ", contract.PromptHints);

        Assert.Equal("PlanetPairingGalleryContentProvider", contract.Diagnostics["selectedProvider"]);
        Assert.Equal(expectedTitle, contract.LocalizedTitle);
        Assert.Contains(expectedMars, contract.LocalizedPrimaryObjects);
        Assert.Contains(expectedJupiter, contract.LocalizedPrimaryObjects);
        Assert.Contains("Mars reddish-orange", promptText);
        Assert.Contains("Jupiter bright cream/white", promptText);
        Assert.DoesNotContain("meteor", promptText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GalleryContentContract_SolarEclipseHindi_UsesSpecificProviderAndLocalizedObjects()
    {
        var context = new AstroPulseGalleryService.GalleryContext("SolarEclipse", "Solar Eclipse", "story", "visual", "2026-08-12T17:30:00+00:00", "2026-08-12T17:30:00+00:00", "India", "hi", "hi", "Asia/Kolkata", EventObjectContextBuilder.FromJsonValues("SolarEclipse", "Solar Eclipse", ["Sun", "Moon"], [], [], []), []);

        var contract = AstroPulseGalleryService.ResolveGalleryContentContractForTesting(context);

        Assert.Equal("SolarEclipseGalleryContentProvider", contract.Diagnostics["selectedProvider"]);
        Assert.Equal("सूर्य ग्रहण", contract.LocalizedTitle);
        Assert.Contains("सूर्य", contract.LocalizedPrimaryObjects);
        Assert.Contains("चंद्रमा", contract.LocalizedPrimaryObjects);
    }

    [Fact]
    public async Task GalleryV3_LoadContext_RequestLanguageOverridesEnglishIntelligenceForHindiPhase13()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), $"astropulse-gallery-context-{Guid.NewGuid():N}");
        var galleryRoot = Path.Combine(planRoot, "gallery");
        var inputRoot = Path.Combine(planRoot, "plan-input");
        Directory.CreateDirectory(galleryRoot);
        Directory.CreateDirectory(inputRoot);
        await File.WriteAllTextAsync(Path.Combine(inputRoot, "content-plan-production-request.json"), """
        { "language": "hi", "eventType": "MeteorShower", "title": "Meteor Shower" }
        """);
        await File.WriteAllTextAsync(Path.Combine(inputRoot, "production-event-intelligence.json"), """
        { "language": "en", "eventType": "MeteorShower", "title": "Meteor Shower", "eventDate": "2026-12-14T06:00:00+00:00", "localPeakTime": "2026-12-14T18:00:00+00:00", "resolvedObjectNames": ["Perseids"] }
        """);

        var context = AstroPulseGalleryService.LoadGalleryContextForTesting(galleryRoot);
        var topics = AstroPulseGalleryService.BuildTopics(context);
        var diagnostics = AstroPulseGalleryService.BuildGalleryLocalizationDiagnostics(context, topics, AstroPulseGalleryAspect.Landscape);
        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(diagnostics);

        Assert.Equal("hi", context.RequestedLanguage);
        Assert.Equal("hi", context.Language);
        Assert.Contains("उल्का वर्षा", diagnosticsJson);
        Assert.Contains("NotoSansDevanagari-Bold.ttf", diagnosticsJson);
    }

    public static IEnumerable<object[]> GalleryAspects()
    {
        yield return [AstroPulseGalleryAspect.Landscape];
        yield return [AstroPulseGalleryAspect.Portrait];
        yield return [AstroPulseGalleryAspect.Square];
    }

    public static IEnumerable<object[]> RequiredGalleryCoverage()
    {
        var events = new[] { "Solar Eclipse", "Lunar Eclipse", "Meteor Shower", "Planet Conjunction", "Planet Grouping" };
        var languages = new[] { "en", "hi" };
        var aspects = new[] { AstroPulseGalleryAspect.Landscape, AstroPulseGalleryAspect.Portrait, AstroPulseGalleryAspect.Square };
        foreach (var eventType in events)
        foreach (var language in languages)
        foreach (var aspect in aspects)
            yield return [eventType, language, aspect];
    }

}
