namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyCrossDomainValidationArchitectureTests
{
    [Fact]
    public void CrossDomainProductionFile_ExistsAndAvoidsForbiddenRuntimeDependencies()
    {
        var root = FindRepositoryRoot();
        var dir = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation", "CrossDomain");
        Assert.True(Directory.Exists(dir));
        var file = Path.Combine(dir, "AstronomyCrossDomainValidation.cs");
        Assert.True(File.Exists(file));
        var text = File.ReadAllText(file);
        foreach (var token in new[] { "DbContext", "Repository", "Controller", "HttpClient", "BackgroundService", "IHostedService", "DateTime.UtcNow", "DateTimeOffset.UtcNow", "Activator.CreateInstance", "BinaryFormatter", "Skyfield", "SPICE", "Horizons" })
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null && !Directory.Exists(Path.Combine(current, ".git"))) current = Directory.GetParent(current)?.FullName;
        return current ?? throw new InvalidOperationException("Repository root not found.");
    }
}
