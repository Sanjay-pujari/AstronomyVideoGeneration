using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyProfileValidationV1Tests
{
    private readonly AstronomyFamilyProfileCatalogV1 _catalog = new();
    [Fact] public void SyntheticUnknownCapabilityFailsValidation() { var p = MutateFirstReq(_catalog.GetRequired("FullMoon"), r => r with { SemanticCapabilityId = new SemanticCapabilityId("Bogus") }); Assert.False(AstronomyFamilyProfileCatalogV1.Validate([p], new AstronomyFamilyAliasCatalogV1([])).IsValid); }
    [Fact] public void SyntheticInvalidRequiredPolicyFailsValidation() { var p = MutateFirstReq(_catalog.GetRequired("FullMoon"), r => r with { MayOmit = true }); Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate([p], new AstronomyFamilyAliasCatalogV1([])).Errors, e => e.Contains("omittable")); }
    [Fact] public void SyntheticNonEventEventWindowRequirementFailsValidation() { var p = _catalog.GetRequired("Constellation"); var req = new FamilySemanticRequirementV1(new SemanticCapabilityId("EventWindow"), FamilyRequirementLevelV1.Required, FamilyMissingValueBehaviorV1.Block, ["Canonical"], 80, false, true); var beat = p.LongFormStructure.Beats[0] with { Requirements = p.LongFormStructure.Beats[0].Requirements.Add(req) }; var np = p with { LongFormStructure = p.LongFormStructure with { Beats = p.LongFormStructure.Beats.SetItem(0, beat) } }; Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate([np], new AstronomyFamilyAliasCatalogV1([])).Errors, e => e.Contains("Non-event")); }
    [Fact] public void RuntimeCodeDoesNotReferenceFamilyV1Types()
    {
        var root = FindRepositoryRoot();
        var infra = Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Infrastructure");
        var excluded = Path.Combine(infra, "Production/Narration/Semantics/Families");
        var tokens = new[] { "AstronomyFamilyProfileCatalogV1", "IAstronomyFamilyProfileCatalogV1", "AstronomyFamilyAliasCatalogV1", "AstronomyFamilyProfileV1" };
        var inspectedMinimum = new[] { "Orchestration/RC2/NarrationGeneratorV5.cs", "Production/Narration/Semantics/AstronomyFamilyProfileResolver.cs", "Production/Narration/Semantics/RequiredSemanticFactResolver.cs", "Persistence/ProductionPipelineExecutionService.cs" };
        foreach (var f in inspectedMinimum) Assert.True(File.Exists(Path.Combine(infra, f)), $"Expected runtime file {f}");
        foreach (var file in Directory.EnumerateFiles(infra, "*.cs", SearchOption.AllDirectories).Where(f => !IsInDirectory(excluded, f)))
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens) Assert.DoesNotContain(token, text);
        }
    }
    private static AstronomyFamilyProfileV1 MutateFirstReq(AstronomyFamilyProfileV1 p, Func<FamilySemanticRequirementV1, FamilySemanticRequirementV1> mutate) { var beat = p.LongFormStructure.Beats[0]; var nb = beat with { Requirements = beat.Requirements.SetItem(0, mutate(beat.Requirements[0])) }; return p with { LongFormStructure = p.LongFormStructure with { Beats = p.LongFormStructure.Beats.SetItem(0, nb) } }; }
    private static bool IsInDirectory(string directory, string file) { var rel = Path.GetRelativePath(directory, file); return rel == "." || (!rel.StartsWith(".." + Path.DirectorySeparatorChar) && !rel.StartsWith(".." + Path.AltDirectorySeparatorChar) && rel != ".."); }
    private static string FindRepositoryRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null) { if (Directory.Exists(Path.Combine(d.FullName, "Backend/src"))) return d.FullName; d = d.Parent; } return Directory.GetCurrentDirectory(); }
}
