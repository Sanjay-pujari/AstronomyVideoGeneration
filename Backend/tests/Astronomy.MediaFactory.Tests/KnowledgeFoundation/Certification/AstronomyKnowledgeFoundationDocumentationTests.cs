using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationDocumentationTests
{
    [Fact]
    public void Required_documentation_and_adr_files_exist_and_are_specific()
    {
        var root = KnowledgeFoundationCertificationFixture.RepoRoot();
        foreach (var file in KnowledgeFoundationCertificationFixture.RequiredDocs)
        {
            var path = Path.Combine(root, "docs", "architecture", "knowledge-foundation", file);
            Assert.True(File.Exists(path), path);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("TODO", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TBD", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Knowledge", text, StringComparison.Ordinal);
        }
        var adrDir = Path.Combine(root, "docs", "architecture", "decisions");
        foreach (var prefix in KnowledgeFoundationCertificationFixture.RequiredAdrs)
            Assert.Contains(Directory.GetFiles(adrDir, prefix + "-*.md"), p => File.ReadAllText(p).Contains("Milestone: CG-A2 Task 2.6", StringComparison.Ordinal));
    }

    [Fact]
    public void Documented_runtime_names_exist()
    {
        Assert.Equal("AddAstronomyKnowledgeFoundation", typeof(AstronomyKnowledgeFoundationRegistrationExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.GetParameters().Length == 1).Name);
        Assert.Equal("AstronomyKnowledgeCatalogQueryEngine", typeof(AstronomyKnowledgeCatalogQueryEngine).Name);
        Assert.Equal("AstronomyKnowledgeStatementQueryEngine", typeof(AstronomyKnowledgeStatementQueryEngine).Name);
        Assert.Equal(["Domain", "PayloadFamily", "KnowledgeType", "ValidationRule", "CrossDomainValidationRule", "GraphValidationRule", "StatementKind"], Enum.GetNames<AstronomyKnowledgeCatalogEntryKind>());
    }
}
