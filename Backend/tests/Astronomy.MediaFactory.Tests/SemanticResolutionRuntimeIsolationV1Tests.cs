using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Evaluation;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticResolutionRuntimeIsolationV1Tests
{
    [Fact]
    public void RuntimeUsesSemanticResolutionEngineWithoutBypassingItsInternalLayers()
    {
        var root = RepositoryTestPaths.Root();
        var narration = File.ReadAllText(Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs"));
        var resolverBody = narration[narration.IndexOf("public sealed class RequiredSemanticFactResolver", StringComparison.Ordinal)..narration.IndexOf("public static class NarrationRealizedContextMapper", StringComparison.Ordinal)];
        Assert.Contains("ISemanticResolutionEngineV1", resolverBody);
        foreach (var token in new[]{"ISemanticCandidateCollectorV1","ISemanticCandidateEvaluatorV1","ISemanticConflictAnalyzerV1","ISemanticCandidateSelectorV1"})
            Assert.DoesNotContain(token, resolverBody);
        Assert.DoesNotContain("TryExtract", resolverBody);
        Assert.Contains("_semanticResolutionEngine.Resolve", resolverBody);

        var orchestrationFiles = new[]{
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"
        };
        foreach (var f in orchestrationFiles.Where(f => File.Exists(Path.Combine(root, f))))
        {
            var text = File.ReadAllText(Path.Combine(root, f));
            foreach (var token in new[]{"ISemanticCandidateCollectorV1","ISemanticCandidateEvaluatorV1","ISemanticConflictAnalyzerV1","ISemanticCandidateSelectorV1","ISemanticResolutionEngineV1","SemanticResolutionRequestV1","ResolvedSemanticFactV1"})
                Assert.DoesNotContain(token, text);
        }

        var engine = File.ReadAllText(Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/Resolution/V1/Engine/SemanticResolutionEngineV1.cs"));
        Assert.Contains("ISemanticCandidateSelectorV1 selector", engine);
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
