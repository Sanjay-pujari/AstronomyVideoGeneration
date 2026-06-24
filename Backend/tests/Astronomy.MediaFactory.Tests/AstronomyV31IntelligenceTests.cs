using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyV31IntelligenceTests
{
    public static IEnumerable<object[]> Families() => new[]
    {
        new object[] { EventFamily.PlanetGrouping, "PlanetConjunction" },
        new object[] { EventFamily.Meteor, "MeteorShower" },
        new object[] { EventFamily.Moon, "NamedFullMoon" },
        new object[] { EventFamily.Eclipse, "SolarEclipse" }
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void FactExpansion_And_NarrationIntelligence_Are_FamilyAware_For_English_And_Hindi(EventFamily family, string eventType)
    {
        var options = Options.Create(new AstronomyV3Options { MaxInterestingFactsPerVideo = 2, AudienceLevel = "Beginner", NarrationTone = "Documentary" });
        var facts = new FactExpansionService(options).ExpandFacts(Event(eventType), family, Metadata(), "en-US");
        var intelligence = new NarrationIntelligenceService(options).BuildContext(Event(eventType), family, "Hook", facts);
        var hindi = new HindiNaturalizationService().Naturalize("Open with curiosity, not a literal translation.", "Hook", family, Metadata(), facts);
        var prompt = AstronomyV31PromptTemplate.BuildNarrationPrompt(Event(eventType), family, "Hook", facts, intelligence, Metadata(), "hi-IN");

        Assert.False(string.IsNullOrWhiteSpace(facts.WhyImportant));
        Assert.False(string.IsNullOrWhiteSpace(facts.HowRare));
        Assert.False(string.IsNullOrWhiteSpace(facts.ObservationRelevance));
        Assert.NotEmpty(facts.ViewerInterestFacts);
        Assert.Contains("documentary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avoid literal Hindi translation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one MP3", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Beginner", intelligence.AudienceLevel);
        Assert.NotEmpty(intelligence.InterestingFactCandidates);
        Assert.Contains("आज", hindi);
        Assert.DoesNotContain("JupiterVenus", facts.WhyImportant + prompt + hindi, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Geminids", facts.WhyImportant + prompt + hindi, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WolfMoon", facts.WhyImportant + prompt + hindi, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Phase18V31Validation_Requires_SceneLevelTts_And_Preserved_HindiPurposes(EventFamily family, string eventType)
    {
        var facts = new FactExpansionService().ExpandFacts(Event(eventType), family, Metadata());
        var purposes = new[] { "Hook", "Cause", "Guide", "InterestingFact", "FinalReminder" };
        var en = purposes.Select((p, i) => new AstronomyV31Scene($"{i + 1:000}-{p}", p, $"English {p} narration with a distinct hook transition and fact {i}.", $"tts/en/short/{i + 1:000}-{p}.mp3", [$"display cue {i}"])).ToArray();
        var hi = purposes.Select((p, i) => new AstronomyV31Scene($"{i + 1:000}-{p}", p, new HindiNaturalizationService().Naturalize(en[i].NarrationText, p, family, Metadata(), facts), $"tts/hi/short/{i + 1:000}-{p}.mp3", [$"display cue {i}"])).ToArray();
        var mp3s = en.Concat(hi).Select(s => s.AudioPath).ToArray();

        var result = AstronomyV31Validation.ValidatePhase18V31(en, hi, mp3s, new SubtitleTtsOptions { TtsMode = "SceneLevel", SubtitleMaxCharsPerLine = 42, SubtitleMaxLines = 2, SubtitleMaxWordsPerCue = 8 }, facts, family, purposes.Length);

        Assert.True(result.Passed, string.Join("; ", result.Errors));
        Assert.Equal(purposes, hi.Select(s => s.ScenePurpose).ToArray());
        Assert.Equal(mp3s.Length, en.Length + hi.Length);
        Assert.DoesNotContain(mp3s, p => p.Contains("cue-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(hi.Length, hi.Select(s => s.NarrationText).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Phase18V31Validation_Rejects_CueLevelMp3_And_RepeatedHindi()
    {
        var facts = new FactExpansionService().ExpandFacts(Event("MeteorShower"), EventFamily.Meteor, Metadata());
        var en = new[] { new AstronomyV31Scene("001", "Hook", "English hook.", "tts/en/short/001.mp3", ["a", "b"]), new AstronomyV31Scene("002", "Guide", "English guide.", "tts/en/short/002.mp3", ["c"]) };
        var hi = new[] { new AstronomyV31Scene("001", "Hook", "दोहराया गया पाठ", "tts/hi/short/001.mp3", ["a"]), new AstronomyV31Scene("002", "Guide", "दोहराया गया पाठ", "tts/hi/short/002.mp3", ["b"]) };
        var result = AstronomyV31Validation.ValidatePhase18V31(en, hi, [.. en.Select(s => s.AudioPath), .. hi.Select(s => s.AudioPath), "tts/hi/short/cue-001.mp3"], new SubtitleTtsOptions(), facts, EventFamily.Meteor, 2);
        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("Cue-level", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("Hindi scenes", StringComparison.OrdinalIgnoreCase));
    }

    private static AstronomyEvent Event(string eventType) => new() { EventType = eventType, Title = $"Sample {eventType}", StartUtc = new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero), EndUtc = new DateTimeOffset(2026, 8, 12, 20, 0, 0, TimeSpan.Zero), RarityScore = 0.4 };
    private static EventMetadata Metadata() => new("after sunset", "western sky", "naked eye", null, null, null, null, new Dictionary<string, string> { ["viewer"] = "The best narration uses the provided visibility details instead of inventing precision." });
}
