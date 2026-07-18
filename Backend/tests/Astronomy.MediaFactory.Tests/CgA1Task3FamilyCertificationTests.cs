using System.Text.Json;
using Astronomy.MediaFactory.Core.Certification;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public sealed class CgA1Task3FamilyCertificationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void MeteorProfile_Resolves_ByEventType_NotContentStrategy(string language)
    {
        var registry = new FamilyCertificationProfileRegistry([new MeteorShowerCertificationProfile(), new PlanetConjunctionCertificationProfile()]);
        var profile = registry.Resolve("MeteorShower");
        profile.FamilyId.Should().Be("MeteorShower");
        profile.CanonicalSemanticValueId.Should().Be("MeteorActivity");
        profile.GetRequiredFacts(Context("unused", "MeteorShower", language)).Select(f => f.FactId).Should().Contain(["EventIdentity", "EventWindow", "ObservationDirection", "MeteorActivity", "DomainScientificKnowledge"]);
        typeof(IFamilyCertificationProfileRegistry).GetMethods().SelectMany(m => m.GetParameters()).Select(p => p.Name).Should().NotContain("contentStrategy");
    }

    [Theory]
    [InlineData("PlanetConjunction", "en")]
    [InlineData("PLANET_CONJUNCTION", "hi")]
    public void PlanetConjunctionProfile_Resolves_And_DoesNotUseMeteorRules(string eventType, string language)
    {
        var profile = new FamilyCertificationProfileRegistry([new MeteorShowerCertificationProfile(), new PlanetConjunctionCertificationProfile()]).Resolve(eventType);
        profile.FamilyId.Should().Be("PlanetConjunction");
        profile.CanonicalSemanticValueId.Should().Be("PlanetPairing");
        profile.GetRequiredFacts(Context("unused", eventType, language)).Select(f => f.FactId).Should().Contain(["EventIdentity", "AstronomicalObjects", "EventWindow", "DomainScientificKnowledge"]);
        profile.GetRequiredFacts(Context("unused", eventType, language)).Select(f => f.FactId).Should().NotContain("MeteorActivity");
    }


    [Fact]
    public void CertificationCatalog_HasUniqueFacts_And_EnglishHindiParity()
    {
        var catalog = new CertificationSemanticFactCatalog();
        catalog.Facts.Select(f => f.FactId).Should().OnlyHaveUniqueItems();
        var en = new MeteorShowerCertificationProfile(catalog).GetRequiredFacts(Context("unused", "MeteorShower", "en")).Select(f => f.FactId);
        var hi = new MeteorShowerCertificationProfile(catalog).GetRequiredFacts(Context("unused", "MeteorShower", "hi")).Select(f => f.FactId);
        en.Should().Equal(hi);
        catalog.ResolveCanonicalValue("PLANET_CONJUNCTION").Should().Be("PlanetPairing");
        catalog.ResolveDisplayName("EventWindow").Should().Be("Event window");
        catalog.ResolveRequiredStatus("MeteorShower", "MeteorActivity").Should().BeTrue();
    }

    [Fact]
    public async Task SemanticEvidenceReader_PrefersStructuredCanonical_And_RecordsFallback()
    {
        using var temp = Temp();
        Directory.CreateDirectory(Path.Combine(temp.Path, "narration-v5"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "required-semantic-fact-diagnostics.json"), "{\"familyId\":\"PlanetConjunction\",\"canonicalSemanticValueId\":\"PlanetPairing\",\"text\":\"MeteorActivity\"}");
        var structured = await new SemanticCertificationEvidenceReader().ReadAsync(Context(temp.Path, "PlanetConjunction", "en"), CancellationToken.None);
        structured.CanonicalSemanticValueId.Should().Be("PlanetPairing");
        structured.Diagnostics.Should().NotContain(d => d.Contains("fallback:canonical-semantic-value", StringComparison.OrdinalIgnoreCase));

        using var fallbackTemp = Temp();
        Directory.CreateDirectory(Path.Combine(fallbackTemp.Path, "narration-v5"));
        await File.WriteAllTextAsync(Path.Combine(fallbackTemp.Path, "narration-v5", "narration-context.json"), "{\"familyId\":\"MeteorShower\"}");
        var fallback = await new SemanticCertificationEvidenceReader().ReadAsync(Context(fallbackTemp.Path, "MeteorShower", "en"), CancellationToken.None);
        fallback.CanonicalSemanticValueId.Should().Be("MeteorActivity");
        fallback.Diagnostics.Should().Contain(d => d == "fallback:canonical-semantic-value:text-matching");
    }

    [Fact]
    public async Task Phase7_QualityParsing_UsesTypedProperties_NotJsonFormatting()
    {
        using var temp = Temp(); WriteSuccessArtifacts(temp.Path, "MeteorShower", "MeteorActivity");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "narration-validation-diagnostics.json"), "{\"requiredFactsPreserved\" : false, \"finalDecision\" : \"Publish\"}");
        var certifier = new Phase7Certifier(new PhaseArtifactRegistry(), new CertificationArtifactVerifier(), new FamilyCertificationProfileRegistry([new MeteorShowerCertificationProfile(), new PlanetConjunctionCertificationProfile()]), new SemanticCertificationEvidenceReader(), new ForbiddenConceptValidator(), new StoryBeatCoverageValidator());
        var result = await certifier.CertifyAsync(Context(temp.Path, "MeteorShower", "en"), CancellationToken.None);
        result.QualityStatus.Should().Be(CertificationStatus.Failed);
    }

    [Fact]
    public async Task StoryBeatValidator_UsesStructuredRoles_WhenAvailable()
    {
        using var temp = Temp(); WriteSuccessArtifacts(temp.Path, "MeteorShower", "MeteorActivity");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "narration-plan.json"), "{\"beats\":[" + string.Join(',', new[] { "Hook", "Orientation", "Timing", "Observation", "Science", "Closing" }.Select(r => $"{{\"storyRole\":\"{r}\"}}")) + "]}");
        var evidence = await new SemanticCertificationEvidenceReader().ReadAsync(Context(temp.Path, "MeteorShower", "en"), CancellationToken.None);
        new StoryBeatCoverageValidator().Validate(new MeteorShowerCertificationProfile(), Context(temp.Path, "MeteorShower", "en"), evidence).Should().BeEmpty();
    }

    [Fact]
    public async Task SemanticEvidenceReader_NormalizesLifecycleEvidence()
    {
        using var temp = Temp(); WriteSuccessArtifacts(temp.Path, "MeteorShower", "MeteorActivity");
        var evidence = await new SemanticCertificationEvidenceReader().ReadAsync(Context(temp.Path, "MeteorShower", "en"), CancellationToken.None);
        evidence.CanonicalIdentityPresent.Should().BeTrue();
        evidence.CanonicalFamilyValuePresent.Should().BeTrue();
        evidence.CanonicalSemanticValueId.Should().Be("MeteorActivity");
        evidence.Facts.Single(f => f.FactId == "MeteorActivity").Should().Match<SemanticFactCertificationResult>(f => f.Resolved && f.Projected && f.Retained && f.BeatAssigned && f.NarrationEvidenceFound);
    }

    [Fact]
    public async Task ForbiddenConceptValidator_ScansOnlyApprovedUserFacingFields()
    {
        using var temp = Temp(); Directory.CreateDirectory(Path.Combine(temp.Path, "narration-v5", "long"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "long", "narration.json"), "{\"text\":\"Jupiter conjunction in the western sky\",\"adapterId\":\"MeteorActivitySourceAdapterV1\"}");
        var hits = await new ForbiddenConceptValidator().ValidateAsync(Context(temp.Path, "MeteorShower", "en"), new MeteorShowerCertificationProfile(), CancellationToken.None);
        hits.Should().Contain(h => h.ConceptId == "planet-conjunction-leakage" && h.MatchedTerm.Equals("Jupiter", StringComparison.OrdinalIgnoreCase));
        hits.Should().NotContain(h => h.MatchedTerm.Contains("MeteorActivitySourceAdapterV1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Phase7_SemanticPass_CanCoexistWithQualityFailure()
    {
        using var temp = Temp(); WriteSuccessArtifacts(temp.Path, "MeteorShower", "MeteorActivity");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "narration-validation-diagnostics.json"), "{\"requiredFactsPreserved\":true,\"longNarrationQualityAccepted\":false,\"finalDecision\":\"Do Not Publish\"}");
        var certifier = new Phase7Certifier(new PhaseArtifactRegistry(), new CertificationArtifactVerifier(), new FamilyCertificationProfileRegistry([new MeteorShowerCertificationProfile(), new PlanetConjunctionCertificationProfile()]), new SemanticCertificationEvidenceReader(), new ForbiddenConceptValidator(), new StoryBeatCoverageValidator());
        var result = await certifier.CertifyAsync(Context(temp.Path, "MeteorShower", "en"), CancellationToken.None);
        result.StructuralStatus.Should().NotBe(CertificationStatus.Failed);
        result.SemanticStatus.Should().Be(CertificationStatus.Passed);
        result.QualityStatus.Should().Be(CertificationStatus.Failed);
    }

    [Fact]
    public async Task Phase7_Fails_For_CrossFamilyLeakage()
    {
        using var temp = Temp(); WriteSuccessArtifacts(temp.Path, "PlanetConjunction", "PlanetPairing");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "narration-v5", "long", "narration.json"), "{\"text\":\"meteor radiant and shooting stars per hour\"}");
        var certifier = new Phase7Certifier(new PhaseArtifactRegistry(), new CertificationArtifactVerifier(), new FamilyCertificationProfileRegistry([new MeteorShowerCertificationProfile(), new PlanetConjunctionCertificationProfile()]), new SemanticCertificationEvidenceReader(), new ForbiddenConceptValidator(), new StoryBeatCoverageValidator());
        var result = await certifier.CertifyAsync(Context(temp.Path, "PlanetConjunction", "en"), CancellationToken.None);
        result.SemanticStatus.Should().Be(CertificationStatus.Failed);
        result.Issues.Should().Contain(i => i.Category == CertificationIssueCategory.CrossFamilyLeakage && i.Code == "P7.ForbiddenConceptDetected");
    }

    private static void WriteSuccessArtifacts(string root, string family, string canonical)
    {
        Directory.CreateDirectory(Path.Combine(root, "narration-v5", "long")); Directory.CreateDirectory(Path.Combine(root, "narration-v5", "short")); Directory.CreateDirectory(Path.Combine(root, "narration-v5", "scene-fact-cards", "long")); Directory.CreateDirectory(Path.Combine(root, "narration-v5", "scene-fact-cards", "short")); Directory.CreateDirectory(Path.Combine(root, "narration-v5", "documentary-script", "long")); Directory.CreateDirectory(Path.Combine(root, "narration-v5", "documentary-script", "short")); Directory.CreateDirectory(Path.Combine(root, "validation"));
        var facts = family == "MeteorShower" ? new[] { "EventIdentity", "EventWindow", "ObservationDirection", "MeteorActivity", "DomainScientificKnowledge" } : new[] { "EventIdentity", "AstronomicalObjects", "EventWindow", "DomainScientificKnowledge" };
        var items = string.Join(',', facts.Select(f => $"{{\"factId\":\"{f}\",\"projected\":true,\"retained\":true,\"beatAssigned\":true,\"beatId\":\"Long-1-{BeatFor(f)}\",\"sceneId\":\"scene-1\",\"confidence\":90}}"));
        File.WriteAllText(Path.Combine(root, "narration-v5", "required-semantic-fact-diagnostics.json"), $"{{\"familyId\":\"{family}\",\"canonicalSemanticValueId\":\"{canonical}\",\"facts\":[{items}]}}");
        File.WriteAllText(Path.Combine(root, "narration-v5", "event-identity-diagnostics.json"), $"{{\"canonicalIdentityPresent\":true,\"familyId\":\"{family}\",\"eventIdentity\":\"Geminids\"}}");
        File.WriteAllText(Path.Combine(root, "narration-v5", "narration-context.json"), $"{{\"familyId\":\"{family}\",\"{canonical}\":true,\"facts\":[{items}]}}");
        File.WriteAllText(Path.Combine(root, "narration-v5", "scene-fact-cards", "long", "scene-fact-cards.json"), $"{{\"cards\":[{items}]}}");
        File.WriteAllText(Path.Combine(root, "narration-v5", "long", "narration.json"), $"{{\"text\":\"{string.Join(' ', facts)} Hook Orientation Timing Observation Science Closing\"}}");
        File.WriteAllText(Path.Combine(root, "validation", "phase-07-validation.json"), "{\"requiredFactsPreserved\":true,\"finalDecision\":\"Publish\"}");
    }
    private static string BeatFor(string fact) => fact switch { "EventWindow" => "Timing", "ObservationDirection" => "Orientation", "MeteorActivity" => "Observation", "DomainScientificKnowledge" => "Science", "AstronomicalObjects" => "Orientation", _ => "Hook" };
    private static FamilyCertificationContext Context(string root, string eventType, string language) => new() { OutputRoot = root, ValidationRoot = Path.Combine(root, "validation"), PlanId = "p", EventTitle = "Geminids", EventType = eventType, Language = language, RegionId = "US", RequestedStartPhase = 1, RequestedEndPhase = 7 };
    private static TempDir Temp() => new();
    private sealed class TempDir : IDisposable { public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")); public TempDir() => Directory.CreateDirectory(Path); public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); } }
}
