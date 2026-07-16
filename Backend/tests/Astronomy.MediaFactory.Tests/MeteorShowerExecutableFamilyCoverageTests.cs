using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class MeteorShowerExecutableFamilyCoverageTests
{
    [Fact]
    public void Geminids_CanonicalParentsResolve_AndProjectLegacyChildren()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var engine = services.GetRequiredService<ISemanticResolutionEngineV1>();
        var context = ExecutableFamilySemanticCoverageV1Tests.MeteorContext();

        var identity = Resolve(engine, SemanticCapabilityVocabularyV1.EventIdentity, context);
        var name = LegacyRequiredSemanticFactCompatibilityMapper.Map(identity.Fact, "Name", null, "Required", "en");
        Assert.Equal("Geminids", name?.SpeakableValue);

        var activity = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, context);
        Assert.Contains("meteor-activity", activity.Fact.WinningAdapterId, StringComparison.OrdinalIgnoreCase);
        var radiant = LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "Radiant", null, "Required", "en");
        Assert.Equal("Gemini", radiant?.SpeakableValue);
        var peak = LegacyRequiredSemanticFactCompatibilityMapper.Map(activity.Fact, "PeakWindow", null, "Required", "en");
        Assert.Contains("December 13", peak?.SpeakableValue);

        var science = Resolve(engine, SemanticCapabilityVocabularyV1.DomainScientificKnowledge, context);
        var importance = LegacyRequiredSemanticFactCompatibilityMapper.Map(science.Fact, "ScientificImportance", null, "Required", "en");
        Assert.DoesNotContain("planet", importance?.SpeakableValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("debris stream", importance?.SpeakableValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Perseids_UsesSameFamilyAdaptersAndProjectionRules_AsGeminids()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var engine = services.GetRequiredService<ISemanticResolutionEngineV1>();
        var geminids = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, ExecutableFamilySemanticCoverageV1Tests.MeteorContext("Geminids", "Gemini"));
        var perseids = Resolve(engine, SemanticCapabilityVocabularyV1.MeteorActivity, ExecutableFamilySemanticCoverageV1Tests.MeteorContext("Perseids", "Perseus"));
        Assert.Equal(geminids.Fact.WinningAdapterId, perseids.Fact.WinningAdapterId);
        Assert.Equal(geminids.Fact.WinningSourceId, perseids.Fact.WinningSourceId);
        Assert.Equal("Gemini", LegacyRequiredSemanticFactCompatibilityMapper.Map(geminids.Fact, "Radiant", null, "Required", "en")?.SpeakableValue);
        Assert.Equal("Perseus", LegacyRequiredSemanticFactCompatibilityMapper.Map(perseids.Fact, "Radiant", null, "Required", "en")?.SpeakableValue);
    }

    private static SemanticResolutionResultV1 Resolve(ISemanticResolutionEngineV1 engine, string capability, Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts.SemanticSourceAdapterContextV1 context)
    {
        var result = engine.Resolve(new SemanticResolutionRequestV1(new SemanticCapabilityId(capability), true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>(), context, "MeteorShower"));
        Assert.True(result.Fact.Status is SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination, result.Fact.DiagnosticMessage);
        return result;
    }
}
