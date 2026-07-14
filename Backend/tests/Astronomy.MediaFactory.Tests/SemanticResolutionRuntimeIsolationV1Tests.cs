using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Evaluation;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticResolutionRuntimeIsolationV1Tests
{
    [Fact]
    public void Runtime_Files_Do_Not_Reference_Sprint4A_Types()
    {
        var root = RepositoryTestPaths.Root();
        var files = new[]{
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/SemanticCapabilityResolver.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/SemanticCapabilitySourceRegistry.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/RequiredSemanticFactResolver.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/AstronomyFamilyProfileResolver.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/ServiceCollectionExtensions.cs"};
        var forbidden = new[]{"ISemanticResolutionEngineV1","SemanticResolutionEngineV1","ISemanticCandidateCollectorV1","ISemanticCandidateEvaluatorV1","ISemanticCandidateSelectorV1","ResolvedSemanticFactV1"};
        foreach (var f in files.Where(f => File.Exists(Path.Combine(root, f))))
        {
            var text = File.ReadAllText(Path.Combine(root, f));
            foreach (var token in forbidden) Assert.DoesNotContain(token, text);
        }
    }

    [Fact]
    public void Sprint4A_Components_Remain_Separated_By_Interface()
    {
        var root = RepositoryTestPaths.Root();
        var collectorPath = Path.Combine(
            root,
            "Backend",
            "src",
            "Astronomy.MediaFactory.Infrastructure",
            "Production",
            "Narration",
            "Semantics",
            "Resolution",
            "V1",
            "Collection",
            "SemanticCandidateCollectorV1.cs");
        var selectorPath = Path.Combine(
            root,
            "Backend",
            "src",
            "Astronomy.MediaFactory.Infrastructure",
            "Production",
            "Narration",
            "Semantics",
            "Resolution",
            "V1",
            "Selection",
            "SemanticCandidateSelectorV1.cs");

        Assert.True(File.Exists(collectorPath), $"Expected collector source file to exist at '{collectorPath}'.");
        Assert.True(File.Exists(selectorPath), $"Expected selector source file to exist at '{selectorPath}'.");

        Assert.DoesNotContain("ISemanticCandidateSelectorV1", File.ReadAllText(collectorPath));
        Assert.DoesNotContain("TryExtract", File.ReadAllText(selectorPath));
        Assert.Contains("ISemanticCandidateCollectorV1", typeof(SemanticResolutionEngineV1).GetConstructors().Single().ToString());
    }

}