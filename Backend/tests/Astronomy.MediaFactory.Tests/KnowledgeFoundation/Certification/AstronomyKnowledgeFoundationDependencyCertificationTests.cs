namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationDependencyCertificationTests
{
    [Fact]
    public void Production_files_do_not_use_prohibited_runtime_architecture_tokens()
    {
        var root = Path.Combine(KnowledgeFoundationCertificationFixture.RepoRoot(), "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation");
        var prohibited = new[] { "DbContext", "DbSet", "AddDbContext", "SaveChanges", "Repository<", "Controller", "ApiController", "HttpClient", "BackgroundService", "IHostedService", "Task.Run", "AsParallel", "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "Guid.NewGuid", "Random(", "Activator.CreateInstance", "Assembly.GetTypes", "AppDomain.CurrentDomain", "BuildServiceProvider", "IServiceScopeFactory", "DynamicInvoke", "JsonSerializer.Deserialize", "Newtonsoft", "InferKnowledge", "GenerateKnowledge", "PredictEvent", "Ephemeris", "RepairGraph", "FixGraph" };
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in prohibited) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }
}
