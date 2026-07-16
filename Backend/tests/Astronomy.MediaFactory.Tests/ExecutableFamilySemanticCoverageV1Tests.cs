using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ExecutableFamilySemanticCoverageV1Tests
{
    [Theory]
    [InlineData("MeteorShower", SemanticCapabilityVocabularyV1.EventIdentity)]
    [InlineData("MeteorShower", SemanticCapabilityVocabularyV1.MeteorActivity)]
    [InlineData("MeteorShower", SemanticCapabilityVocabularyV1.DomainScientificKnowledge)]
    public void ActiveFamilyRequiredCapabilities_ReachExecutableLevel6(string family, string capability)
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        var policies = provider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        var registry = provider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var engine = provider.GetRequiredService<ISemanticResolutionEngineV1>();
        var id = new SemanticCapabilityId(capability);
        Assert.True(policies.TryGet(id, out var policy));
        var approved = policy!.ApprovedSources.Where(s => s.ActiveInV1).Select(s => s.SourceId).ToArray();
        Assert.NotEmpty(approved);
        Assert.NotEmpty(registry.Adapters.Where(a => a.SupportedCapabilityId.Equals(id) && approved.Contains(a.SourceId)));

        var result = engine.Resolve(new SemanticResolutionRequestV1(id, true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>(), MeteorContext(), family));
        Assert.True(result.Fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination, result.Fact.DiagnosticMessage);
        Assert.True(result.Diagnostics.CandidateCount > 0);
        Assert.NotEmpty(result.Diagnostics.InvokedAdapterIds);
        var realized = SemanticFactValueRealizer.Instance.Realize(result.Fact, capability, null, LanguageProfileResolver.Resolve("en"));
        Assert.True(realized.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(realized.SpeakableValue));
        Assert.DoesNotContain(result.Diagnostics.CandidateEvaluations, e => e.Eligible && e.ValidationIssues.Contains("FamilyIncompatibleCandidate"));
    }

    internal static SemanticSourceAdapterContextV1 MeteorContext(string shower = "Geminids", string radiant = "Gemini")
    {
        var window = new EventWindowValue(DateTimeOffset.Parse("2026-12-13T00:00:00Z"), DateTimeOffset.Parse("2026-12-14T07:00:00Z"), DateTimeOffset.Parse("2026-12-15T12:00:00Z"), null, null, null, null, "UTC", "overnight December 13–14");
        var meteor = new MeteorActivityValue(radiant, window, window, 120, null, "3200 Phaethon", "Earth crosses a debris stream; the radiant is a perspective point.", shower, radiant, "low moon interference", "best after midnight", "east after midnight");
        return new SemanticSourceAdapterContextV1(
            new CanonicalAstronomyEventIdentity("MeteorShower", "MeteorShower", "MeteorShower", "METEOR_SHOWER", "test", null, ImmutableArray.Create(new AstronomicalObjectValue(shower, "MeteorShower", "Primary", null, [])), [], null, "en", shower, shower, $"{shower} meteor shower"),
            new ProductionEventIntelligenceSourceV1("MeteorShower", "MeteorShower", "MeteorShower", ImmutableArray.Create(new AstronomicalObjectValue(shower, "MeteorShower", "Primary", null, [])), EventWindow: window, MeteorActivity: meteor),
            new ObservationMetadataSourceV1(window, ObservationDirection: new ObservationDirectionValue("East", null, null, null, "look east after midnight")),
            AstronomyDomainKnowledge: new AstronomyDomainKnowledgeSourceV1(DomainKnowledge: new DomainScientificKnowledgeValue("Earth crosses a debris stream.", "The radiant is a perspective effect.", "Meteor showers reveal Earth crossing a debris stream, with rates shaped by radiant altitude and moonlight.", "Avoid guaranteed counts.")),
            Language: "en");
    }
}
