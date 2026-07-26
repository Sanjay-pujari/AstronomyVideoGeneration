using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryMediaPipelineArchitectureTests
{
    [Fact]
    public void Production_boundary_has_no_adapters_or_test_fakes()
    {
        var assembly=typeof(DocumentaryMediaPipelineOrchestrator).Assembly;
        Assert.DoesNotContain(assembly.GetTypes(),type=>type.Name.Contains("FakeProvider",StringComparison.OrdinalIgnoreCase));
        var forbiddenReferences=new[]{"Azure","OpenAI","FFmpeg","YouTube","EntityFramework","SqlClient","RabbitMQ","Azure.Storage"};
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),reference=>forbiddenReferences.Any(x=>reference.Name!.Contains(x,StringComparison.OrdinalIgnoreCase)));

        var source=FindSourceDirectory();
        var forbiddenTokens=new[]{"System.IO.File","System.IO.Directory","FileStream","HttpClient","System.Diagnostics.Process",
            "Environment.GetEnvironmentVariable","ConfigurationManager","IServiceProvider","GetService("};
        foreach(var file in Directory.EnumerateFiles(source,"DocumentaryMediaPipeline*.cs"))
        {
            var text=File.ReadAllText(file);
            Assert.DoesNotContain(forbiddenTokens,token=>text.Contains(token,StringComparison.Ordinal));
        }
    }

    private static string FindSourceDirectory()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
        {
            var candidate=Path.Combine(directory.FullName,"Backend","src","Astronomy.MediaFactory.Core","DocumentaryBlueprint");
            if(Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the repository's O2.18 production source directory.");
    }
}
