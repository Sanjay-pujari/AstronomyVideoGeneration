using Astronomy.MediaFactory.ProductionAdapters;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostArchitectureTests
{
    [Fact]
    public void Production_host_respects_architecture_boundaries()
    {
        var coreReferences = typeof(Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryMediaPipelineValidator)
            .Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Astronomy.MediaFactory.ProductionAdapters", coreReferences);

        var source = File.ReadAllText(FindRepositoryFile(
            "Backend/src/Astronomy.MediaFactory.ProductionAdapters/ExecutionHost.cs"));
        foreach (var forbidden in new[]
        {
            "Process.Start", "SpeechSynthesizer", "BlobClient", "YouTube", "Meta client",
            "RegisterAsync", "FinalizeArtifactAsync", ".Result", ".Wait(", "GetAwaiter().GetResult("
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_10_tests_do_not_contain_placeholder_certification_assertions()
    {
        var directory = Path.GetDirectoryName(FindRepositoryFile(
            "Backend/tests/Astronomy.MediaFactory.Tests/ProductionAdapters/DocumentaryProductionExecutionHostTestFixtures.cs"))!;
        var files = Directory.GetFiles(directory, "DocumentaryProductionExecutionHost*.cs");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "Certification" + "Contract", "Assert.True(" + "true)",
                "true.Should()." + "BeTrue()", "await Task." + "Yield()"
            })
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Repository file was not found: {relativePath}");
    }
}
