using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7NarrationHandoffTests
{
    [Fact]
    public void NarrationSafeContext_ContainsMeteorProjectedFacts()
    {
        var (profile, resolution, normalized) = BuildMeteorNarrationSafeContext();
        var facts = normalized.SafeContexts.SelectMany(c => c.SpeakableFacts).ToArray();
        var factKeys = facts.Select(f => f.FactKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("MeteorShower", profile.FamilyId);
        Assert.Contains(resolution.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)), f => f.FactType == "Radiant");
        Assert.Contains(resolution.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)), f => f.FactType == "PeakWindow");
        Assert.Contains("Radiant", factKeys);
        Assert.Contains("PeakWindow", factKeys);
        Assert.Contains("EventIdentity", factKeys);
        Assert.Contains("DomainScientificKnowledge", factKeys);
        Assert.Contains(normalized.Diagnostics.SafeContextHandoffDiagnostics!, d => d.FactTypesCopied.Contains("Radiant", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(normalized.Diagnostics.SafeContextHandoffDiagnostics!, d => d.SafeContextJson.Contains("PeakWindow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NarrationRealizer_ConsumesNarrationSafeContextWithoutMissingRadiant()
    {
        var (profile, _, normalized) = BuildMeteorNarrationSafeContext();
        var language = LanguageProfileResolver.Resolve("en");
        var realizer = new NarrationRealizer();
        var results = normalized.SafeContexts.Select(c => realizer.Realize(c, profile, language)).ToArray();
        var issues = NarrationRealizationValidator.Validate(results, profile);
        var lookup = NarrationRealizationValidator.LastRequiredLookupDiagnostics;

        Assert.DoesNotContain(issues, i => i.DetectedIssue == "missing required profile fact");
        Assert.Contains(results.SelectMany(r => r.SpeakableFacts), f => f.FactType == "Radiant");
        Assert.Contains(results.SelectMany(r => r.SpeakableFacts), f => f.FactType == "PeakWindow");
        Assert.Contains(lookup, d => d.FieldRequested == "Radiant" && d.MatchingFactsFound.Contains("Radiant", StringComparer.OrdinalIgnoreCase));
    }

    private static (AstronomyFamilyProfile Profile, RequiredSemanticFactResolutionResult Resolution, NarrationInputNormalizationResult Normalized) BuildMeteorNarrationSafeContext()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("MeteorShower", null, null, [], null));
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity).Profile;
        var resolution = TestResolver.Resolve(
            profile,
            "{\"eventType\":\"MeteorShower\",\"eventTitle\":\"Geminids Meteor Shower Peak\",\"name\":\"Geminids\",\"eventDate\":\"2026-12-14\",\"eventDateOrWindow\":\"2026-12-13 to 2026-12-14\",\"radiant\":\"Gemini\",\"peakWindow\":\"2026-12-14 00:00-05:00 IST\",\"bestViewingWindowLocal\":\"2026-12-14 00:00-05:00 IST\",\"localPeakTime\":\"2026-12-14 02:00 IST\",\"direction\":\"east to overhead\",\"location\":\"Udaipur, India\",\"timezone\":\"Asia/Kolkata\"}",
            "{\"bestViewingWindowLocal\":\"2026-12-14 00:00-05:00 IST\",\"localPeakTime\":\"2026-12-14 02:00 IST\",\"direction\":\"east to overhead\",\"location\":\"Udaipur, India\",\"timezone\":\"Asia/Kolkata\"}");
        var longContract = TestJson.Json("{\"beats\":[{\"beatOrder\":1,\"narrativeRole\":\"Hook\",\"documentaryBeatId\":\"long-hook\"},{\"beatOrder\":2,\"narrativeRole\":\"Timing\",\"documentaryBeatId\":\"long-timing\"},{\"beatOrder\":3,\"narrativeRole\":\"Science\",\"documentaryBeatId\":\"long-science\"},{\"beatOrder\":4,\"narrativeRole\":\"Observation\",\"documentaryBeatId\":\"long-observation\"}]}");
        var shortContract = TestJson.Json("{\"beats\":[{\"beatOrder\":1,\"narrativeRole\":\"Hook\",\"documentaryBeatId\":\"short-hook\"},{\"beatOrder\":2,\"narrativeRole\":\"Timing\",\"documentaryBeatId\":\"short-timing\"},{\"beatOrder\":3,\"narrativeRole\":\"Science\",\"documentaryBeatId\":\"short-science\"}]}");
        var cards = new SceneFactCardSet("v", "o", "long", "en", []);
        var normalized = NarrationInputNormalizer.Normalize(longContract, shortContract, null, null, null, null, new DocumentaryPerformerSceneFactCards(cards, cards), resolution, "calm", "test", LanguageProfileResolver.Resolve("en"));
        return (profile, resolution, normalized);
    }
}
