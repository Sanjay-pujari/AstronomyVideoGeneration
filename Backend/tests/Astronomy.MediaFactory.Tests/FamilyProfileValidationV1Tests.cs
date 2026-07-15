using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyProfileValidationV1Tests
{
    private readonly AstronomyFamilyProfileCatalogV1 _catalog = new();
    [Fact] public void SyntheticUnknownCapabilityFailsValidation() { var p = MutateFirstReq(_catalog.GetRequired("FullMoon"), r => r with { SemanticCapabilityId = new SemanticCapabilityId("Bogus") }); Assert.False(AstronomyFamilyProfileCatalogV1.Validate([p], new AstronomyFamilyAliasCatalogV1([])).IsValid); }
    [Fact] public void SyntheticInvalidRequiredPolicyFailsValidation() { var p = MutateFirstReq(_catalog.GetRequired("FullMoon"), r => r with { MayOmit = true }); Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate([p], new AstronomyFamilyAliasCatalogV1([])).Errors, e => e.Contains("omittable")); }
    [Fact] public void SyntheticNonEventEventWindowRequirementFailsValidation() { var p = _catalog.GetRequired("Constellation"); var req = new FamilySemanticRequirementV1(new SemanticCapabilityId("EventWindow"), FamilyRequirementLevelV1.Required, FamilyMissingValueBehaviorV1.Block, ["Canonical"], 80, false, true); var beat = p.LongFormStructure.Beats[0] with { Requirements = p.LongFormStructure.Beats[0].Requirements.Add(req) }; var np = p with { LongFormStructure = p.LongFormStructure with { Beats = p.LongFormStructure.Beats.SetItem(0, beat) } }; Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate([np], new AstronomyFamilyAliasCatalogV1([])).Errors, e => e.Contains("Non-event")); }

    [Fact]
    public void PlanetPairingObservationDirectionIsOptionalWithOmitBehavior()
    {
        var profile = _catalog.GetRequired("PlanetPairing");
        var directionRequirements = profile.LongFormStructure.Beats.Concat(profile.ShortFormStructure.Beats)
            .SelectMany(beat => beat.Requirements.Select(req => new { beat.BeatRole, req }))
            .Where(x => x.req.SemanticCapabilityId.Value == "ObservationDirection")
            .ToArray();

        Assert.NotEmpty(directionRequirements);
        Assert.All(directionRequirements, x => Assert.Equal(FamilyRequirementLevelV1.Optional, x.req.RequirementLevel));
        Assert.All(directionRequirements, x => Assert.Equal(FamilyMissingValueBehaviorV1.OmitCapability, x.req.MissingValueBehavior));
        Assert.All(directionRequirements, x => Assert.True(x.req.MayOmit));
        Assert.All(directionRequirements, x => Assert.False(x.req.BlocksPhase7));
    }

    [Fact]
    public void OtherDirectionMandatoryFamiliesAreUnchanged()
    {
        foreach (var family in new[] { "PlanetGrouping", "MeteorShower" })
        {
            var profile = _catalog.GetRequired(family);
            Assert.Contains(profile.LongFormStructure.Beats.SelectMany(b => b.Requirements), r =>
                r.SemanticCapabilityId.Value == "ObservationDirection" &&
                r.RequirementLevel == FamilyRequirementLevelV1.Required &&
                !r.MayOmit &&
                r.BlocksPhase7);
        }
    }

    [Fact] public void RuntimeUsesV1FamilyCatalogThroughApprovedResolverBoundary()
    {
        var root = RepositoryTestPaths.Root();
        var infra = Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Infrastructure");
        var serviceCollection = File.ReadAllText(Path.Combine(infra, "Extensions/ServiceCollectionExtensions.cs"));
        Assert.Contains("IAstronomyFamilyProfileCatalogV1", serviceCollection);
        Assert.Contains("AstronomyFamilyProfileCatalogV1", serviceCollection);

        var familyResolver = File.ReadAllText(Path.Combine(infra, "Production/Narration/Semantics/AstronomyFamilyProfileResolver.cs"));
        Assert.Contains("IAstronomyFamilyProfileCatalogV1", familyResolver);

        var narrationGenerator = File.ReadAllText(Path.Combine(infra, "Orchestration/RC2/NarrationGeneratorV5.cs"));
        Assert.Contains("IAstronomyFamilyProfileResolver familyProfileResolver", narrationGenerator);
        Assert.DoesNotContain("IAstronomyFamilyProfileCatalogV1", narrationGenerator);
        Assert.DoesNotContain("AstronomyFamilyProfileCatalogV1", narrationGenerator);
        Assert.DoesNotContain("AstronomyFamilyProfileV1", narrationGenerator);

        var pipeline = File.ReadAllText(Path.Combine(infra, "Persistence/ProductionPipelineExecutionService.cs"));
        Assert.DoesNotContain("IAstronomyFamilyProfileCatalogV1", pipeline);
        Assert.DoesNotContain("AstronomyFamilyProfileCatalogV1", pipeline);
        Assert.DoesNotContain("AstronomyFamilyProfileV1", pipeline);

        var requiredResolver = File.ReadAllText(Path.Combine(infra, "Orchestration/RC2/NarrationGeneratorV5.cs"));
        var requiredResolverBody = requiredResolver[requiredResolver.IndexOf("public sealed class RequiredSemanticFactResolver", StringComparison.Ordinal)..requiredResolver.IndexOf("public static class NarrationRealizedContextMapper", StringComparison.Ordinal)];
        Assert.DoesNotContain("AstronomyFamilyProfileV1", requiredResolverBody);

        var adapter = File.ReadAllText(Path.Combine(infra, "Production/Narration/Semantics/Families/Compatibility/AstronomyFamilyProfileV1CompatibilityAdapter.cs"));
        Assert.Contains("AstronomyFamilyProfileV1", adapter);

        var inspectedMinimum = new[] { "Orchestration/RC2/NarrationGeneratorV5.cs", "Production/Narration/Semantics/AstronomyFamilyProfileResolver.cs", "Persistence/ProductionPipelineExecutionService.cs" };
        foreach (var f in inspectedMinimum) Assert.True(File.Exists(Path.Combine(infra, f)), $"Expected runtime file {f}");
    }
    private static AstronomyFamilyProfileV1 MutateFirstReq(AstronomyFamilyProfileV1 p, Func<FamilySemanticRequirementV1, FamilySemanticRequirementV1> mutate) { var beat = p.LongFormStructure.Beats[0]; var nb = beat with { Requirements = beat.Requirements.SetItem(0, mutate(beat.Requirements[0])) }; return p with { LongFormStructure = p.LongFormStructure with { Beats = p.LongFormStructure.Beats.SetItem(0, nb) } }; }
    private static bool IsInDirectory(string directory, string file) { var rel = Path.GetRelativePath(directory, file); return rel == "." || (!rel.StartsWith(".." + Path.DirectorySeparatorChar) && !rel.StartsWith(".." + Path.AltDirectorySeparatorChar) && rel != ".."); }
}
