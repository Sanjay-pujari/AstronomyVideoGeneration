using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationInputNormalizerTests
{
    public static IEnumerable<object[]> Matrix()
    {
        yield return ["Mars–Jupiter English", "en", "Mars and Jupiter", "2026-11-16T00:00:00+00:00", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["Mars–Jupiter Hindi", "hi", "Mars and Jupiter", "2026-11-16 05:30 +0530", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["Jupiter–Venus English", "en", "Jupiter and Venus", "2026-08-12T00:00:00+00:00", "W", "1.63", "IN-RJ-UDAIPUR"];
        yield return ["Jupiter–Venus Hindi", "hi", "Jupiter and Venus", "2026-08-12T00:00:00+00:00", "W", "1.63", "IN-RJ-UDAIPUR"];
        yield return ["publish-window JSON", "en", "Jupiter and Venus", "{\"recommendedPublishWindow\":\"2026-08-10T00:00:00Z\"}", "SE", "1.63", "IN-RJ-UDAIPUR"];
        yield return ["raw UTC only", "en", "Mars and Jupiter", "2026-11-16T00:00:00+00:00", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["verified local time", "en", "Mars and Jupiter", "2026-11-16 05:30 +0530", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["missing timezone", "en", "Mars and Jupiter", "2026-11-16", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["direction code only", "en", "Mars and Jupiter", "", "SE", "", ""];
        yield return ["no timing requirement", "en", "Mars and Jupiter", "", "SE", "1.19", "IN-RJ-UDAIPUR"];
        yield return ["constellation", "en", "Orion", "", "E", "", ""];
        yield return ["deep-sky no observing window", "hi", "Andromeda Galaxy", "", "E", "", ""];
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void SameNormalizerBuildsSafeContextForAstronomyFamilies(string name, string language, string objects, string time, string direction, string separation, string region)
    {
        using var contract = JsonDocument.Parse(BuildContract(objects, time, direction, separation, region));
        var cards = new SceneFactCardSet("v", "o", "long", language, [new SceneFactCard("scene-001", 1, "long", [], [], [], [], [], [], [], [], [], 10, "scene-001", "frame-001")]);
        var profile = LanguageProfileResolver.Resolve(language);

        var result = NarrationInputNormalizer.Normalize(contract.RootElement, contract.RootElement, null, null, null, null, new DocumentaryPerformerSceneFactCards(cards, cards), "calm", "test", profile);
        var prompt = new NarrationPromptComposer().Compose(new NarrationPromptComposerInput(result.Context, [], "prompt.md", "diag.json", LanguageProfile: profile)).PromptPreviewMarkdown;
        var text = JsonSerializer.Serialize(result.Context);

        Assert.NotEmpty(result.SafeContexts);
        Assert.All(result.Context.Formats, f => Assert.NotEmpty(f.Beats));
        Assert.Empty(NarrationContextPurityValidator.Validate(result.Context));
        Assert.Contains(profile.OutputInstruction, prompt);
        Assert.DoesNotContain("2026-", text);
        Assert.DoesNotContain("recommendedPublishWindow", text);
        Assert.DoesNotContain("IN-RJ-UDAIPUR", text);
        Assert.DoesNotContain("the favored viewing region", text);
        Assert.DoesNotContain("On before dawn", text);
        Assert.DoesNotContain("at at", text);
        Assert.DoesNotContain("on on", text);
        Assert.DoesNotContain("during on", text);
        Assert.DoesNotContain("1. 19", text);
        Assert.DoesNotContain("{\\\"", text);
        Assert.DoesNotContain("long-beat-001", text);
        Assert.NotNull(result.Diagnostics.NormalizedFields);
        Assert.NotNull(result.Diagnostics.OmittedFields);
        Assert.NotNull(result.Diagnostics.ExcludedPublishingFields);
        Assert.NotNull(result.Diagnostics.UnresolvedFields);
        Assert.NotNull(result.Diagnostics.FallbacksUsed);
        if (!string.IsNullOrWhiteSpace(region))
        {
            Assert.Contains(language == "hi" ? "उदयपुर, राजस्थान" : "Udaipur, Rajasthan", text);
            Assert.True(result.Diagnostics.RegionIdsResolved > 0);
        }
        if (!string.IsNullOrWhiteSpace(separation))
        {
            Assert.Contains(separation, text);
            Assert.Contains(language == "hi" ? "डिग्री" : "degrees", text);
        }
        Assert.NotSame(result.Context.Formats[0].Beats, result.Context.Formats[1].Beats);
        if (language == "hi")
        {
            Assert.Contains("आकाश", text);
            Assert.DoesNotContain("southeastern sky", text);
        }
        else
        {
            Assert.DoesNotContain("दक्षिण", text);
        }
    }

    private static string BuildContract(string objects, string time, string direction, string separation, string region)
    {
        static string J(string value) => JsonSerializer.Serialize(value ?? string.Empty);
        return $$"""
        { "beats": [ { "beatId": "long-beat-001", "sceneId": "scene-001", "beatOrder": 1,
          "knowledgeGoal": "Explain safely", "audienceOutcome": "Understand safely", "editorialIntent": "Use facts", "transitionGoal": "Continue",
          "allocatedFacts": {
            "PrimaryObjects": { "value": {{J(objects)}}, "status": "allocated" },
            "EventDate": { "value": {{J(time)}}, "status": "allocated" },
            "BestViewingTime": { "value": {{J(time)}}, "status": "allocated" },
            "Direction": { "value": {{J(direction)}}, "status": "allocated" },
            "AngularSeparation": { "value": {{J(separation)}}, "status": "allocated", "unit": "degrees" },
            "VisibilityRegion": { "value": {{J(region)}}, "status": "allocated" },
            "recommendedPublishWindow": { "value": "{\"recommendedPublishWindow\":\"2026-11-15T00:00:00Z\"}", "status": "allocated" }
          } } ] }
        """;
    }
}
