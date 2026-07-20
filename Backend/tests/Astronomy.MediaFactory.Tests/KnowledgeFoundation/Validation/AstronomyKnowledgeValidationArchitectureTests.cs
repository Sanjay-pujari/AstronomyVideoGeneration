namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationArchitectureTests
{
    [Fact]
    public void ValidationFoundationFiles_ExistAndAvoidInfrastructureTerms()
    {
        var root = FindRepositoryRoot();
        var validation = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "Validation");
        Assert.True(Directory.Exists(validation));
        var expected = new[] { "AstronomyKnowledgeValidationSeverity.cs", "AstronomyKnowledgeValidationIssue.cs", "AstronomyKnowledgeValidationResult.cs", "AstronomyKnowledgeValidationContext.cs", "IAstronomyKnowledgeValidationRule.cs", "IAstronomyTypedKnowledgeValidator.cs", "IAstronomyKnowledgeValidationRuleRegistry.cs", "AstronomyKnowledgeValidationRuleDescriptor.cs", "AstronomyKnowledgeValidationRuleRegistry.cs", "AstronomyTypedKnowledgeValidator.cs", "AstronomyKnowledgeValidationCodes.cs", "AstronomyKnowledgeValidationExtensions.cs", Path.Combine("DependencyInjection", "AstronomyKnowledgeValidationServiceCollectionExtensions.cs") };
        foreach (var relative in expected) { var file = Path.Combine(validation, relative); Assert.True(File.Exists(file), file); Assert.NotEqual(0, new FileInfo(file).Length); }
        var forbidden = new[] { "DbContext", "IQueryable", "Repository", "Controller", "HttpClient", "BackgroundService", "IHostedService", "Quartz", "Hangfire", "Kafka", "RabbitMQ", "ServiceBus", "Mongo", "Cosmos", "DateTimeOffset.UtcNow", "DateTime.UtcNow", "Activator.CreateInstance", "Type.GetType", "AssemblyQualifiedName", "FormatterServices", "BinaryFormatter", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "Rendering", "Publishing", "CertificationCoordinator" };
        foreach (var file in Directory.EnumerateFiles(validation, "*.cs", SearchOption.AllDirectories)) { var text = File.ReadAllText(file); foreach (var term in forbidden) Assert.DoesNotContain(term, text, StringComparison.Ordinal); }
    }

    [Fact]
    public void TypedDomainFiles_DoNotDependOnValidationFoundation()
    {
        var root = FindRepositoryRoot();
        var typed = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains");
        var forbidden = new[] { "KnowledgeFoundation.Validation", "IAstronomyTypedKnowledgeValidator", "IAstronomyKnowledgeValidationRule", "AstronomyKnowledgeValidationResult" };
        foreach (var file in Directory.EnumerateFiles(typed, "*.cs", SearchOption.AllDirectories)) { var text = File.ReadAllText(file); foreach (var term in forbidden) Assert.DoesNotContain(term, text, StringComparison.Ordinal); }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Core"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root could not be discovered.");
    }
}
