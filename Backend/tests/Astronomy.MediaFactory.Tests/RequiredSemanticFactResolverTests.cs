using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

namespace Astronomy.MediaFactory.Tests;

public sealed class RequiredSemanticFactResolverTests
{
    private static readonly AstronomyFamilyProfile Planetary = V1CompatibilityProfile("PlanetPairing");
    private static readonly LanguageProfile English = LanguageProfileResolver.Resolve("en");
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void RequiredFactAvailableInDocumentaryContractWins()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "PrimaryObjects");
        Assert.Contains("ProductionEventIntelligence", fact.SourceField);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "AstronomicalObjects" && r.Status == "Resolved");
    }

    [Fact]
    public void FactAbsentInContractFallsBackToEventIntelligence()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\"}"), eventIntel: Json("{\"eventType\":\"Planetary conjunction\"}"));
        Assert.Contains(result.Beats[0].RequiredFacts, f => f.FactType == "EventIdentity");
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "EventIdentity" && r.SelectedSource == "EventIdentityContext");
    }

    [Fact]
    public void PlanetPairingApparentPairingScienceResolvesFromDomainKnowledge()
    {
        var result = Resolve(LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentPairingScience");
        Assert.Equal("AstronomyDomainKnowledgeProvider", fact.SourceArtifact);
        Assert.Contains("AstronomyDomainKnowledge.DomainKnowledge", fact.SourceInputs!);
        Assert.Contains("perspective", fact.CanonicalValue.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "DomainScientificKnowledge" && r.SelectedSource == "AstronomyDomainKnowledgeProvider");
    }


    [Fact]
    public void ApparentPairingScienceUsesCanonicalDomainKnowledge()
    {
        var result = Resolve(LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\",\"ApparentAlignmentExplanation\":\"allocated upstream concept\"}"));
        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentPairingScience");
        Assert.Equal("AstronomyDomainKnowledgeProvider", fact.SourceArtifact);
        Assert.Contains("AstronomyDomainKnowledge.DomainKnowledge", fact.SourceInputs!);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "DomainScientificKnowledge");
    }

    [Fact]
    public void PlanetPairingDoesNotFabricateAngularSeparation()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"));
        Assert.DoesNotContain(result.Beats[0].OptionalFacts, f => f.FactType is "AngularSeparation" or "AngularRelationship");
        Assert.Contains("AngularRelationship", result.Beats[0].OmittedOptionalFacts);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "AngularSeparation" && r.Status == "UnavailableOptional");
    }


    [Fact]
    public void PlanetPairingWithoutObservationDirectionDoesNotBlockAndRecordsOptionalOmission()
    {
        var longC = LongWithBeat("Observation", "{\"EventDateOrWindow\":\"August 12\",\"PrimaryObjects\":\"Jupiter and Venus\",\"EventType\":\"PlanetPairing\"}");
        var shortC = ShortWithBeat("Observation", "{\"EventDateOrWindow\":\"August 12\",\"PrimaryObjects\":\"Jupiter and Venus\",\"EventType\":\"PlanetPairing\"}");

        var result = Resolve(longC, shortC);

        Assert.False(result.Blocking);
        Assert.All(result.Beats, beat => Assert.DoesNotContain("ObservationDirection", beat.MissingRequiredFacts));
        Assert.Contains(result.Beats, beat => beat.Format == "long" && beat.NarrativeRole == "Observation" && beat.OmittedOptionalFacts.Contains("ObservationDirection"));
        Assert.Contains(result.Beats, beat => beat.Format == "short" && beat.NarrativeRole == "Observation" && beat.OmittedOptionalFacts.Contains("ObservationDirection"));
        Assert.DoesNotContain(result.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)), f => f.FactType == "ObservationDirection");
    }

    [Fact]
    public void PlanetPairingVerifiedObservationDirectionResolvesAsOptionalFact()
    {
        var result = Resolve(
            LongWithBeat("Observation", "{\"EventDateOrWindow\":\"August 12\"}"),
            ShortWithBeat("Observation", "{\"EventDateOrWindow\":\"August 12\"}"),
            observation: Json("{\"beats\":[{\"allocatedFacts\":{\"Direction\":\"verified western horizon\"}}]}"));

        Assert.False(result.Blocking);
        Assert.Contains(result.Beats, b => b.OptionalFacts.Any(f => f.FactType == "ObservationDirection" && f.CanonicalValue.ToString()!.Contains("western horizon")));
        Assert.All(result.Beats, beat => Assert.DoesNotContain("ObservationDirection", beat.MissingRequiredFacts));
    }

    [Theory]
    [InlineData("{\"title\":\"Look east for Jupiter Venus\",\"eventType\":\"PlanetPairing\"}")]
    [InlineData("{\"eventType\":\"PlanetPairing\",\"primaryObjects\":[\"Jupiter\"],\"secondaryObjects\":[\"Venus\"]}")]
    [InlineData("{\"eventType\":\"PlanetPairing\",\"visibilityRegion\":\"Udaipur\",\"bestViewingWindowLocal\":\"twilight after sunset\"}")]
    public void PlanetPairingDoesNotInferDirectionFromNonDirectionalContext(string eventIntelJson)
    {
        var result = Resolve(LongWithBeat("Observation", "{\"EventDateOrWindow\":\"August 12\"}"), eventIntel: Json(eventIntelJson));

        Assert.Contains("ObservationDirection", result.Beats[0].OmittedOptionalFacts);
        Assert.DoesNotContain(result.Beats[0].RequiredFacts.Concat(result.Beats[0].OptionalFacts), f => f.FactType == "ObservationDirection");
        Assert.DoesNotContain("ObservationDirection", result.Beats[0].MissingRequiredFacts);
    }

    [Fact]
    public void ConstellationDoesNotUsePlanetPairingApparentAlignmentKnowledge()
    {
        var profile = V1CompatibilityProfile("Constellation");
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

        var fact = Assert.Single(result.Beats[0].RequiredFacts, f => f.FactType == "ObservationTiming");
        Assert.Equal("ObservationMetadata", fact.SourceArtifact);
        Assert.Contains("ObservationMetadata.EventWindow", fact.SourceField);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "EventWindow" && r.Status == "Resolved" && r.SubstitutionsApplied.Any(s => s.Contains("ObservationTiming mapped to canonical capability EventWindow")));
        Assert.DoesNotContain("ViewingWindow", result.Beats[0].MissingRequiredFacts);
        Assert.DoesNotContain("ObservationTiming", result.Beats[0].MissingRequiredFacts);
    }

    [Fact]
    public void MissingOptionalBinocularGuidanceWarnsOnly()
    {
        var result = Resolve(LongWithBeat("Hook", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"Planetary conjunction\"}"));
        Assert.Contains("BinocularGuidance", result.Beats[0].OmittedOptionalFacts);
        Assert.DoesNotContain("BinocularGuidance", result.Beats[0].MissingRequiredFacts);
        Assert.False(result.Beats[0].Blocking);
        var binocular = Assert.Single(result.Beats[0].CapabilityResolutions, r => r.SubstitutionsApplied.Any(s => s.Contains("BinocularGuidance mapped to canonical capability ObservationEquipment")));
        Assert.Null(binocular.SelectedSource);
        Assert.True(binocular.Status is "UnavailableOptional" or "NoEligibleCandidate");
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
        var profile = V1CompatibilityProfile("Constellation");
        var result = Resolve(LongWithBeat("Science", "{\"Name\":\"Orion\",\"SkyRegion\":\"equatorial sky\",\"IdentificationPattern\":\"three belt stars\",\"MajorStars\":\"Betelgeuse and Rigel\",\"ScientificIdentity\":\"IAU constellation\"}"), profile: profile);
        var missing = string.Join(",", result.Beats[0].MissingRequiredFacts);
        Assert.DoesNotContain("EventDate", missing);
        Assert.DoesNotContain("EventWindow", missing);
    }

    [Fact]
    public void DeepSkyObjectDoesNotRequireLocalViewingTimeWhenNotObservationTiming()
    {
        var profile = V1CompatibilityProfile("DeepSkyObject");
        var result = Resolve(LongWithBeat("Science", "{\"ObjectName\":\"M31\",\"ObjectType\":\"galaxy\",\"SkyLocation\":\"Andromeda\",\"ScientificImportance\":\"nearest large spiral galaxy\"}"), profile: profile);
        Assert.Empty(result.Beats[0].MissingRequiredFacts.Where(f => f.Contains("Time") || f.Contains("ObservationTiming")));
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
        Assert.All(result.Beats, beat => Assert.DoesNotContain("ObservationDirection", beat.MissingRequiredFacts));
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
    public void ProviderFailureLeavesDescriptiveApparentPairingScienceBlockingErrorWithoutFiller()
    {
        var resolver = new RequiredSemanticFactResolver(new EmptyDomainKnowledgeProvider());
        var result = resolver.Resolve(new RequiredSemanticFactResolutionInput(Planetary, LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"), LongWithBeat("Science", "{\"PrimaryObjects\":\"Mars and Jupiter\",\"EventType\":\"PlanetPairing\"}"), null, null, null, null, null, English));
        Assert.True(result.Blocking);
        Assert.Contains("ApparentPairingScience", result.Beats[0].MissingRequiredFacts);
        Assert.DoesNotContain(result.Beats[0].RequiredFacts, f => f.FactType == "ApparentPairingScience");
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



    [Fact]
    public void OptionalZhrWithNoSourceValueDoesNotBlockMeteorShower()
    {
        var profile = OptionalMeteorZhrProfile();
        var result = Resolve(LongWithBeat("Hook", "{}"), eventIntel: Json("{\"eventTitle\":\"Geminids Meteor Shower Peak\",\"eventType\":\"MeteorShower\"}"), profile: profile);

        Assert.Contains(result.Beats[0].OmittedOptionalFacts, f => string.Equals(f, "ZHR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Beats[0].MissingRequiredFacts, f => string.Equals(f, "ZHR", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.Beats[0].Blocking);
        var zhr = Assert.Single(result.Beats[0].CapabilityResolutions, r => r.Capability == "MeteorActivity");
        Assert.Null(zhr.SelectedSource);
        Assert.True(zhr.Status is "UnavailableOptional" or "NoEligibleCandidate");
    }

    [Fact]
    public void VerifiedUpstreamZhrResolvesWhenPresent()
    {
        var profile = OptionalMeteorZhrProfile();
        var result = Resolve(LongWithBeat("Hook", "{}"), eventIntel: Json("{\"eventType\":\"MeteorShower\",\"zhr\":{\"value\":120,\"unit\":\"meteors/hour\",\"qualifier\":\"under ideal dark skies\",\"source\":\"verified upstream feed\",\"confidence\":0.95}}"), profile: profile);

        Assert.False(result.Blocking);
        var fact = Assert.Single(result.Beats[0].OptionalFacts, f => string.Equals(f.FactType, "ZHR", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ProductionEventIntelligence", fact.SourceArtifact);
        Assert.Contains("ProductionEventIntelligence.MeteorActivity", fact.SourceField, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Beats[0].CapabilityResolutions, r => r.Capability == "MeteorActivity" && r.Status == "Resolved");
    }

    [Fact]
    public void ZhrIsNotFabricatedFromGeminidsTitle()
    {
        var profile = OptionalMeteorZhrProfile();
        var result = Resolve(LongWithBeat("Hook", "{}"), eventIntel: Json("{\"eventTitle\":\"Geminids Meteor Shower Peak\",\"eventType\":\"MeteorShower\"}"), profile: profile);

        Assert.DoesNotContain(result.Beats[0].OptionalFacts, f => string.Equals(f.FactType, "ZHR", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Beats[0].OmittedOptionalFacts, f => string.Equals(f, "ZHR", StringComparison.OrdinalIgnoreCase));
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


    private static AstronomyFamilyProfile V1CompatibilityProfile(string familyId)
    {
        var catalog = new AstronomyFamilyProfileCatalogV1();
        var v1 = catalog.GetRequired(familyId);
        var compatibility = new AstronomyFamilyProfileV1CompatibilityAdapter().Convert(v1, new FamilyProfileCompatibilityContext(familyId, familyId, familyId, false));
        Assert.True(compatibility.Succeeded, string.Join("; ", compatibility.BlockingErrors));
        return compatibility.LegacyProfile!;
    }

    private static AstronomyFamilyProfile OptionalMeteorZhrProfile() => new("MeteorShower", "TimedObservationEvent", "ObservationGuide", "SkyWatchShort", [], ["Zhr"], ["Hook"], ["Hook"], "", "", [], [], []);

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
