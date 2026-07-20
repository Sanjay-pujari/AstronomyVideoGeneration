using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedKnowledgeIntegrationArchitectureTests
{
    [Fact]
    public void IntegrationProductionFiles_DoNotReferenceForbiddenInfrastructureOrUnsafeDeserialization()
    {
        var root = FindRepositoryRoot();
        var integrationRoot = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", "Integration");
        var forbidden = new[] { "DbContext", "IQueryable", "Repository", "Controller", "HttpClient", "BackgroundService", "IHostedService", "Quartz", "Hangfire", "Kafka", "RabbitMQ", "ServiceBus", "Mongo", "Cosmos", "BinaryFormatter", "NetDataContractSerializer", "TypeNameHandling", "AssemblyQualifiedName", "Type.GetType", "Activator.CreateInstance", "FormatterServices", "DateTimeOffset.UtcNow", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "Publishing", "Rendering", "CertificationCoordinator" };

        Assert.True(Directory.Exists(integrationRoot), $"Typed knowledge integration directory was not found: {integrationRoot}");
        var files = Directory.EnumerateFiles(integrationRoot, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(files);
        files.Select(Path.GetFileName).Should().Contain([
            "AstronomyTypedPayloadDescriptor.cs",
            "AstronomyTypedPayloadRegistry.cs",
            "AstronomyBuiltInTypedPayloadDescriptors.cs",
            "AstronomyTypedKnowledgePayloadJsonConverter.cs",
            "AstronomyTypedKnowledgeJsonOptionsExtensions.cs",
            "AstronomyTypedKnowledgeServiceCollectionExtensions.cs"]);

        var hits = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => new { file, line, index }))
            .Where(entry => forbidden.Any(term => entry.line.Contains(term, StringComparison.Ordinal)))
            .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.index + 1}:{entry.line}")
            .ToArray();

        hits.Should().BeEmpty();
    }

    [Fact]
    public void FrozenTypedDomainFiles_DoNotReferenceIntegrationOrSerializationInfrastructure()
    {
        var root = FindRepositoryRoot();
        var typedRoot = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains");
        var forbidden = new[] { ".Integration", "JsonConverter", "JsonSerializerOptions", "IServiceCollection" };
        Assert.True(Directory.Exists(typedRoot), $"Typed knowledge domain directory was not found: {typedRoot}");
        var files = Directory.EnumerateFiles(typedRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "Integration" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(files);

        var hits = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => new { file, line, index }))
            .Where(entry => forbidden.Any(term => entry.line.Contains(term, StringComparison.Ordinal)))
            .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.index + 1}:{entry.line}")
            .ToArray();

        hits.Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null && !Directory.Exists(Path.Combine(current, "Backend", "src", "Astronomy.MediaFactory.Core"))) current = Directory.GetParent(current)?.FullName;
        return current ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
