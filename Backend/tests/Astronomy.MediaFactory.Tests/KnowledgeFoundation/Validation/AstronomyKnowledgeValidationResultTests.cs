using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationResultTests
{
    [Fact]
    public void Success_IsValidAndEmpty() { Assert.True(AstronomyKnowledgeValidationResult.Success.IsValid); Assert.Empty(AstronomyKnowledgeValidationResult.Success.Issues); }
    [Fact]
    public void Constructor_CountsValidityCopiesDeduplicatesAndOrders()
    {
        var warning = Fixtures.Issue("test.warning", AstronomyKnowledgeValidationSeverity.Warning, "b");
        var critical = Fixtures.Issue("test.critical", AstronomyKnowledgeValidationSeverity.Critical, "a");
        var error = Fixtures.Issue("test.error", AstronomyKnowledgeValidationSeverity.Error, "c");
        var source = new List<AstronomyKnowledgeValidationIssue> { warning, error, critical, warning };
        var result = new AstronomyKnowledgeValidationResult(source);
        source.Clear();
        Assert.False(result.IsValid); Assert.True(result.HasWarnings); Assert.True(result.HasErrors); Assert.True(result.HasCriticalIssues);
        Assert.Equal(3, result.Issues.Count); Assert.Equal(AstronomyKnowledgeValidationSeverity.Critical, result.Issues[0].Severity);
    }
    [Fact]
    public void Merge_RemovesDuplicatesAndPreservesOrder()
    {
        var a = new AstronomyKnowledgeValidationResult(new[] { Fixtures.Issue("test.warning", AstronomyKnowledgeValidationSeverity.Warning) });
        var b = new AstronomyKnowledgeValidationResult(new[] { Fixtures.Issue("test.error", AstronomyKnowledgeValidationSeverity.Error), Fixtures.Issue("test.warning", AstronomyKnowledgeValidationSeverity.Warning) });
        Assert.Equal(new[] { AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeValidationSeverity.Warning }, a.Merge(b).Issues.Select(i => i.Severity));
    }
    [Fact]
    public void Constructor_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeValidationResult(null!));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationResult(new AstronomyKnowledgeValidationIssue?[] { null! }!));
    }
}
