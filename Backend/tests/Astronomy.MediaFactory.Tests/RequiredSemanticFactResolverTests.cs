using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class RequiredSemanticFactResolverTests
{
    private static readonly AstronomyFamilyProfile Planetary = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"PlanetaryConjunction\"}"), null);
    private static readonly LanguageProfile English = LanguageProfileResolver.Resolve("en");
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void RequiredFactAvailableInDocumentaryContractWins()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "PrimaryObjects");
        Assert.Equal("Documentary Contract", fact.SourceArtifact);
    }

    [Fact]
    public void FactAbsentInContractFallsBackToEventIntelligence()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\"}"), eventIntel: Json("{\"eventType\":\"Planetary conjunction\"}"));
        Assert.Contains(result.Beats[0].RequiredFacts, f => f.FactType == "EventType" && f.SourceArtifact == "Production Event Intelligence");
    }

    [Fact]
    public void ApparentAlignmentExplanationCanBeDerivedWithTraceability()
    {
        var result = Resolve(LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentAlignmentExplanation");
        Assert.Equal("Derived", fact.FactOrigin);
        Assert.Equal("planetary-conjunction-apparent-alignment-v1", fact.DerivationRuleId);
        Assert.Contains("PrimaryObjects", fact.SourceInputs!);
    }

    [Fact]
    public void MissingRequiredTimingFactBlocksTimingBeat()
    {
        var result = Resolve(LongWithBeat("Timing", "{\"PrimaryObjects\":\"Mars and Jupiter\"}"));
        Assert.True(result.Blocking);
        Assert.Contains("EventDateOrWindow", result.Beats[0].MissingRequiredFacts);
    }

    [Fact]
    public void MissingOptionalBinocularGuidanceWarnsOnly()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        Assert.False(result.Blocking);
        Assert.Contains("BinocularGuidance", result.Beats[0].OmittedOptionalFacts);
    }

    [Fact]
    public void ConstellationProfileDoesNotRequireEventDate()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"Constellation\"}"), null);
        var result = Resolve(LongWithBeat("Science", "{\"Name\":\"Orion\",\"SkyRegion\":\"equatorial sky\",\"IdentificationPattern\":\"three belt stars\",\"MajorStars\":\"Betelgeuse and Rigel\",\"ScientificIdentity\":\"IAU constellation\"}"), profile: profile);
        Assert.False(result.Blocking);
        Assert.DoesNotContain("EventDate", string.Join(",", result.Beats[0].MissingRequiredFacts));
    }

    [Fact]
    public void DeepSkyObjectDoesNotRequireLocalViewingTimeWhenNotObservationTiming()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"DeepSkyObject\"}"), null);
        var result = Resolve(LongWithBeat("Science", "{\"ObjectName\":\"M31\",\"ObjectType\":\"galaxy\",\"SkyLocation\":\"Andromeda\",\"ScientificImportance\":\"nearest large spiral galaxy\"}"), profile: profile);
        Assert.False(result.Blocking);
        Assert.Empty(result.Beats[0].MissingRequiredFacts.Where(f => f.Contains("Time")));
    }

    [Fact]
    public void ConflictingAngularSeparationSelectsAuthorityAndWarns()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\",\"AngularSeparation\":{\"value\":\"1.19\",\"unit\":\"degrees\"}}"), observation: Json("{\"angularSeparation\":\"1.25\"}"));
        Assert.Contains(result.Beats[0].Conflicts, c => c.FactType == "AngularSeparation");
    }

    [Fact]
    public void LongAndShortResolveIndependentRequirementSets()
    {
        var longC = LongWithBeat("Orientation", "{\"Direction\":\"SE\",\"Region\":\"Udaipur, Rajasthan\"}");
        var shortC = ShortWithBeat("Orientation", "{\"Direction\":\"SE\"}");
        var result = Resolve(longC, shortC);
        Assert.Contains(result.Beats, b => b.Format == "long" && b.RequiredFacts.Any(f => f.FactType == "Region"));
        Assert.Contains(result.Beats, b => b.Format == "short" && !b.RequiredFacts.Any(f => f.FactType == "Region"));
    }

    [Fact]
    public void HindiAndEnglishResolveSameSemanticFacts()
    {
        var contract = LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}");
        var en = Resolve(contract, language: LanguageProfileResolver.Resolve("en"));
        var hi = Resolve(contract, language: LanguageProfileResolver.Resolve("hi"));
        Assert.Equal(en.Beats[0].RequiredFacts.Select(f => f.FactType), hi.Beats[0].RequiredFacts.Select(f => f.FactType));
    }

    [Fact]
    public void MissingFactNeverProducesGenericFillerAndBlocksRealization()
    {
        var result = Resolve(LongWithBeat("Timing", "{}"));
        var issues = RequiredSemanticFactPhase7Validator.Validate(result);
        Assert.True(result.Blocking);
        Assert.Contains(issues, i => i.DetectedIssue == "missing required semantic fact");
    }

    [Fact]
    public void Phase7CannotReportSuccessWhenRequiredDiagnosticsBlock()
    {
        var result = Resolve(LongWithBeat("Timing", "{}"));
        Assert.True(result.Blocking);
        Assert.NotEmpty(RequiredSemanticFactPhase7Validator.Validate(result));
    }


    [Theory]
    [InlineData("PlanetPairing", "PlanetPairing")]
    [InlineData("SolarEclipse", "Eclipse")]
    [InlineData("LunarOccultation", "Occultation")]
    [InlineData("Constellation", "Constellation")]
    [InlineData("Galaxy", "DeepSkyObject")]
    [InlineData("Nebula", "DeepSkyObject")]
    [InlineData("Planet", "PlanetProfile")]
    public void FamilyProfileResolver_UsesAuthoritativeEventTypeMapping(string eventType, string expectedProfile)
    {
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(eventType, null, null, null));

        Assert.Equal(expectedProfile, result.Resolved.ResolvedProfileId);
        Assert.False(result.Resolved.FallbackUsed);
    }

    [Theory]
    [InlineData("Mars and Jupiter Close Pairing")]
    [InlineData("Jupiter and Venus Close Pairing")]
    public void PlanetPairingRunsResolvePlanetPairing(string title)
    {
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("PlanetPairing", title, null, null));

        Assert.Equal("PlanetPairing", result.Profile.FamilyId);
        Assert.NotEqual("Constellation", result.Profile.FamilyId);
    }

    [Fact]
    public void PlanetPairingAndConstellationNeverCrossValidate()
    {
        var planetPairing = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("PlanetPairing", null, null, null));
        var constellation = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("Constellation", null, null, null));

        Assert.Equal("PlanetPairing", planetPairing.Profile.FamilyId);
        Assert.Equal("Constellation", constellation.Profile.FamilyId);
    }

    [Fact]
    public void UnknownFamilyProfileFailsWithoutDefault()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("UnmappedEvent", null, null, null)));

        Assert.Contains("Unable to resolve astronomy family profile.", ex.Message);
        Assert.Contains("No matching profile found.", ex.Message);
    }

    private static RequiredSemanticFactResolutionResult Resolve(JsonElement longContract, JsonElement? shortContract = null, JsonElement? eventIntel = null, JsonElement? observation = null, AstronomyFamilyProfile? profile = null, LanguageProfile? language = null)
        => new RequiredSemanticFactResolver().Resolve(new RequiredSemanticFactResolutionInput(profile ?? Planetary, longContract, shortContract ?? longContract, null, null, eventIntel, observation, null, language ?? English));

    private static JsonElement LongWithBeat(string role, string facts) => Json("{\"beats\":[{\"documentaryBeatId\":\"long-beat-001\",\"narrativeRole\":" + JsonSerializer.Serialize(role) + ",\"allocatedFacts\":" + facts + "}]}");
    private static JsonElement ShortWithBeat(string role, string facts) => Json("{\"beats\":[{\"documentaryBeatId\":\"short-beat-001\",\"narrativeRole\":" + JsonSerializer.Serialize(role) + ",\"allocatedFacts\":" + facts + "}]}");
}
