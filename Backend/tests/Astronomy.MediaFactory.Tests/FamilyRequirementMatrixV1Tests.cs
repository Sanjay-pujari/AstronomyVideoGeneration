using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyRequirementMatrixV1Tests
{
    private readonly AstronomyFamilyProfileCatalogV1 _catalog = new();
    private static IEnumerable<FamilySemanticRequirementV1> Requirements(AstronomyFamilyProfileV1 p) => p.LongFormStructure.Beats.Concat(p.ShortFormStructure.Beats).SelectMany(b => b.Requirements);
    private static bool Has(AstronomyFamilyProfileV1 p, string cap, FamilyRequirementLevelV1? level = null) => Requirements(p).Any(r => r.SemanticCapabilityId.Value == cap && (level is null || r.RequirementLevel == level));
    [Fact] public void NamedFullMoonIncludesOptionalCulturalContextButDoesNotRequireIt() { var p = _catalog.GetRequired("NamedFullMoon"); Assert.True(Has(p, SemanticCapabilityVocabularyV1.CulturalContext, FamilyRequirementLevelV1.Optional)); Assert.False(Has(p, SemanticCapabilityVocabularyV1.CulturalContext, FamilyRequirementLevelV1.Required)); }
    [Fact] public void FullMoonDoesNotIncludeRequiredCulturalContext() => Assert.False(Has(_catalog.GetRequired("FullMoon"), SemanticCapabilityVocabularyV1.CulturalContext, FamilyRequirementLevelV1.Required));
    [Fact] public void MeteorShowerUsesMeteorActivityNotZhr() { var p = _catalog.GetRequired("MeteorShower"); Assert.True(Has(p, SemanticCapabilityVocabularyV1.MeteorActivity)); Assert.DoesNotContain(Requirements(p), r => r.SemanticCapabilityId.Value.Contains("Zhr", StringComparison.OrdinalIgnoreCase)); }
    [Fact] public void ConstellationDoesNotRequireEventWindow() => Assert.False(Has(_catalog.GetRequired("Constellation"), SemanticCapabilityVocabularyV1.EventWindow, FamilyRequirementLevelV1.Required));
    [Fact] public void DeepSkyObjectDoesNotRequireEventWindow() => Assert.False(Has(_catalog.GetRequired("DeepSkyObject"), SemanticCapabilityVocabularyV1.EventWindow, FamilyRequirementLevelV1.Required));
    [Fact] public void SolarEclipseRequiresBlockingSafetyGuidance() => Assert.Contains(Requirements(_catalog.GetRequired("SolarEclipse")), r => r.SemanticCapabilityId.Value == SemanticCapabilityVocabularyV1.SafetyGuidance && r.RequirementLevel == FamilyRequirementLevelV1.Required && r.BlocksPhase7);
    [Fact] public void LunarEclipseDoesNotRequireSafetyGuidance() => Assert.False(Has(_catalog.GetRequired("LunarEclipse"), SemanticCapabilityVocabularyV1.SafetyGuidance, FamilyRequirementLevelV1.Required));
    [Fact] public void EveryRequirementReferencesCanonicalCapability() { var ids = SemanticCapabilityVocabularyV1.CanonicalIds; foreach (var r in _catalog.Profiles.SelectMany(Requirements)) Assert.Contains(r.SemanticCapabilityId.Value, ids); }
    [Fact] public void RequiredRequirementsCannotBeOmitted() { foreach (var r in _catalog.Profiles.SelectMany(Requirements).Where(r => r.RequirementLevel == FamilyRequirementLevelV1.Required)) Assert.False(r.MayOmit); }
    [Fact] public void OptionalRequirementsHaveExplicitOmissionBehavior() { foreach (var r in _catalog.Profiles.SelectMany(Requirements).Where(r => r.RequirementLevel == FamilyRequirementLevelV1.Optional)) Assert.NotEqual(FamilyMissingValueBehaviorV1.Block, r.MissingValueBehavior); }
    [Fact] public void BeatIdsAndOrdersAreUnique() { foreach (var p in _catalog.Profiles) foreach (var s in new[] { p.LongFormStructure, p.ShortFormStructure }) { Assert.Equal(s.Beats.Length, s.Beats.Select(b => b.BeatId).Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.Equal(s.Beats.Length, s.Beats.Select(b => b.Order).Distinct().Count()); } }
    [Fact]
    public void EverySupportedFamilyRequiredCapabilityHasPolicyAndApprovedSource()
    {
        var policies = new SemanticSourcePolicyCatalogV1();
        foreach (var profile in _catalog.Profiles.Where(p => p.ActiveInV1))
        foreach (var requirement in Requirements(profile).Where(r => r.RequirementLevel == FamilyRequirementLevelV1.Required).GroupBy(r => r.SemanticCapabilityId.Value).Select(g => g.First()))
        {
            var policy = policies.GetRequired(requirement.SemanticCapabilityId);
            Assert.Contains(policy.ApprovedSources, source => source.ActiveInV1 && !source.CompatibilityOnly);
        }
    }

    [Fact]
    public void FamilyCoverageCertificationMatrixHasNoMissingCanonicalRequirements()
    {
        var requiredFamilies = new[] { "PlanetPairing", "MeteorShower", "NamedFullMoon", "SolarEclipse", "LunarEclipse", "PlanetGrouping", "Constellation", "DeepSkyObject" };
        var policies = new SemanticSourcePolicyCatalogV1();
        foreach (var family in requiredFamilies)
        {
            var profile = _catalog.GetRequired(family);
            var missing = Requirements(profile)
                .Where(r => r.RequirementLevel == FamilyRequirementLevelV1.Required)
                .Select(r => r.SemanticCapabilityId)
                .Distinct()
                .Where(id => !policies.TryGet(id, out var policy) || !policy.ApprovedSources.Any(source => source.ActiveInV1 && !source.CompatibilityOnly))
                .Select(id => id.Value)
                .ToArray();
            Assert.Empty(missing);
        }
    }
}
