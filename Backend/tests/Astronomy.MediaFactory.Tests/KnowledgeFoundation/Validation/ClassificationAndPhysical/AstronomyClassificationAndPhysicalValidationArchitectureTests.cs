namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationAndPhysicalValidationArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();
    [Fact] public void ExpectedProductionDirectoriesAndFilesExist()
    {
        var files = new[] {
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Classification/AstronomyClassificationValidationCodes.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Classification/AstronomyClassificationAssignmentValidationRule.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Classification/AstronomyClassificationDuplicateAssignmentValidationRule.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Classification/AstronomyClassificationPrimaryAssignmentValidationRule.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Physical/AstronomyPhysicalValidationCodes.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Physical/AstronomyPhysicalPropertyIdentityValidationRule.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Physical/AstronomyPhysicalPropertyValueValidationRule.cs",
            "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Physical/AstronomyPhysicalRangeValidationRule.cs" };
        Assert.True(Directory.Exists(Path.Combine(Root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Classification")));
        Assert.True(Directory.Exists(Path.Combine(Root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Physical")));
        foreach (var file in files) Assert.True(new FileInfo(Path.Combine(Root, file)).Length > 0, file);
    }
    [Fact] public void ProductionValidationCodeAvoidsForbiddenInfrastructureAndScientificExecutionTokens()
    {
        var directory = Path.Combine(Root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation");
        var forbidden = new[] { "DbContext", "IQueryable", "HttpClient", "BackgroundService", "IHostedService", "DateTimeOffset.UtcNow", "DateTime.UtcNow", "Activator.CreateInstance", "Type.GetType", "AssemblyQualifiedName", "FormatterServices", "BinaryFormatter", "ConvertUnit", "TransformCoordinate" };
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }
    [Fact] public void FrozenTypedDomainsDoNotReferenceValidationNamespaces()
    {
        foreach (var folder in new[] { "Classification", "Physical" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/TypedDomains", folder), "*.cs"))
            Assert.DoesNotContain("KnowledgeFoundation.Validation", File.ReadAllText(file), StringComparison.Ordinal);
    }
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "Backend/src/Astronomy.MediaFactory.Core"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
