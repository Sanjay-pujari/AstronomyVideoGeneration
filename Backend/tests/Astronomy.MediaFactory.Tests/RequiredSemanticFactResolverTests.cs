using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class RequiredSemanticFactResolverTests
{
    private static readonly AstronomyFamilyProfile Planetary = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"PlanetPairing\"}"), null);
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
        Assert.Contains(result.Beats[0].RequiredFacts, f => f.FactType == "EventIdentity" && f.SourceArtifact == "Production Event Intelligence");
    }

    [Fact]
    public void PlanetPairingMissingApparentAlignmentExplanationResolvesFromDomainKnowledge()
    {
        var result = Resolve(LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentAlignmentExplanation");
        Assert.Equal("DomainKnowledge", fact.FactOrigin);
        Assert.Equal("Astronomy Domain Knowledge Provider", fact.SourceArtifact);
        Assert.Equal("planet-pairing-apparent-line-of-sight-v1", fact.DerivationRuleId);
        Assert.False(fact.CanonicalValue is string);
    }


    [Fact]
    public void UpstreamApparentAlignmentExplanationWinsOverProvider()
    {
        var result = Resolve(LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\",\"ApparentAlignmentExplanation\":\"allocated upstream concept\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentAlignmentExplanation");
        Assert.Equal("Documentary Contract", fact.SourceArtifact);
        Assert.Equal("Source", fact.FactOrigin);
    }

    [Fact]
    public void PlanetPairingDoesNotFabricateAngularSeparation()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"));
        Assert.DoesNotContain(result.Beats[0].OptionalFacts, f => f.FactType == "AngularSeparation");
        Assert.Contains("AngularSeparation", result.Beats[0].OmittedOptionalFacts);
    }

    [Fact]
    public void ConstellationDoesNotUsePlanetPairingApparentAlignmentKnowledge()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"Constellation\"}"), null);
        var provider = new AstronomyDomainKnowledgeProvider();
        var resolved = provider.TryResolve(profile.FamilyId, "ApparentAlignmentExplanation", new AstronomyKnowledgeContext(profile.FamilyId, [], "en"), out _);
        Assert.False(resolved);
    }

    [Fact]
    public void TimingBeatDoesNotGloballyRequireApparentAlignmentExplanation()
    {
        var result = Resolve(LongWithBeat("Timing", "{\"EventDateOrWindow\":\"August 12\"}"));
        Assert.DoesNotContain("ApparentAlignmentExplanation", result.Beats[0].MissingRequiredFacts);
    }

    [Fact]
    public void MissingRequiredTimingFactBlocksTimingBeat()
    {
        var result = Resolve(LongWithBeat("Timing", "{\"PrimaryObjects\":\"Mars and Jupiter\"}"));
        Assert.True(result.Blocking);
        Assert.Contains("ObservationTiming", result.Beats[0].MissingRequiredFacts);
    }

    [Theory]
    [InlineData("{\"localPeakTime\":\"before dawn on November 16, around 5:30 AM\"}", "Production Event Intelligence", "localPeakTime")]
    [InlineData("{\"bestViewingWindowLocal\":\"2026-11-16 04:30–06:00 IST\"}", "Production Event Intelligence", "bestViewingWindowLocal")]
    [InlineData("{\"peakUtc\":\"2026-11-16T00:00:00Z\"}", "Production Event Intelligence", "peakUtc")]
    public void ObservationTimingResolvesFromSemanticAlternatives(string eventIntelJson, string expectedSource, string expectedField)
    {
        var result = Resolve(LongWithBeat("Timing", "{}"), eventIntel: Json(eventIntelJson));

        Assert.False(result.Blocking);
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ObservationTiming");
        Assert.Equal(expectedSource, fact.SourceArtifact);
        Assert.Contains(expectedField, fact.SourceField);
        Assert.DoesNotContain("ViewingWindow", result.Beats[0].MissingRequiredFacts);
    }

    [Fact]
    public void MissingOptionalBinocularGuidanceWarnsOnly()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        Assert.False(result.Blocking);
        Assert.Contains("BinocularGuidance", result.Beats[0].OmittedOptionalFacts);
        var binocular = Assert.Single(result.Beats[0].CapabilityResolutions, r => r.Capability == "ObservationMode");
        Assert.Null(binocular.SelectedSource);
        Assert.Contains(binocular.RejectedSources, r => r.Reason == "SourceValueMissing");
    }

    [Fact]
    public void GenericDomainKnowledgeDoesNotCreateEventSpecificEquipmentClaims()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        var text = JsonSerializer.Serialize(result.Diagnostics);
        Assert.DoesNotContain("magnification", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("surface detail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planetary disk", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed visibility", text, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains(result.Beats[0].Conflicts, c => c.FactType == "AngularRelationship");
    }

    [Fact]
    public void LongAndShortResolveIndependentRequirementSets()
    {
        var longC = LongWithBeat("Orientation", "{\"Direction\":\"SE\",\"Region\":\"Udaipur, Rajasthan\"}");
        var shortC = ShortWithBeat("Orientation", "{\"Direction\":\"SE\"}");
        var result = Resolve(longC, shortC);
        Assert.Contains(result.Beats, b => b.Format == "long" && b.RequiredFacts.Any(f => f.FactType == "LocationContext"));
        Assert.Contains(result.Beats, b => b.Format == "short" && !b.RequiredFacts.Any(f => f.FactType == "LocationContext"));
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
    public void ProviderFailureLeavesDescriptiveBlockingErrorWithoutFiller()
    {
        var resolver = new RequiredSemanticFactResolver(new EmptyDomainKnowledgeProvider());
        var result = resolver.Resolve(new RequiredSemanticFactResolutionInput(Planetary, LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"), LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"), null, null, null, null, null, English));
        Assert.True(result.Blocking);
        Assert.Contains("ApparentAlignmentExplanation", result.Beats[0].MissingRequiredFacts);
        Assert.DoesNotContain(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentAlignmentExplanation");
    }

    [Fact]
    public void DuplicateMissingFactReportsCollapsePerBeat()
    {
        var result = Resolve(LongWithBeat("Timing", "{}"));
        var issues = RequiredSemanticFactPhase7Validator.Validate(result).Concat(RequiredSemanticFactPhase7Validator.Validate(result)).DistinctBy(i => (i.Format, i.SceneId, i.BeatRole, i.Field)).ToArray();
        Assert.Single(issues.Where(i => i.Format == "long" && i.Field == "ObservationTiming"));
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

    private sealed class EmptyDomainKnowledgeProvider : IAstronomyDomainKnowledgeProvider
    {
        public bool TryResolve(string familyProfileId, string semanticFactType, AstronomyKnowledgeContext context, out ResolvedSemanticFact fact)
        {
            fact = default!;
            return false;
        }
    }
}
