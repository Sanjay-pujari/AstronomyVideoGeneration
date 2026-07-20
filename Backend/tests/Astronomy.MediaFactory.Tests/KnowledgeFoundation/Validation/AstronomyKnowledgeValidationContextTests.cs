using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationContextTests
{
    [Fact]
    public void Constructor_CopiesNormalizesAndSortsTagsAndItems()
    {
        var tags = new List<string> { " beta ", "alpha", "beta" };
        var items = new Dictionary<string, object?> { [" source "] = "test" };
        var context = new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId(" run "), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), tags: tags, items: items);
        tags.Add("zzz"); items["other"] = 1;
        Assert.Equal(new[] { "alpha", "beta" }, context.Tags); Assert.True(context.Items.ContainsKey("source")); Assert.False(context.Items.ContainsKey("other"));
    }
    [Fact]
    public void Constructor_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId(" "), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId("run"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId("run"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), mode: (AstronomyKnowledgeValidationMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId("run"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), minimumSeverity: (AstronomyKnowledgeValidationSeverity)99));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationContext(new AstronomyKnowledgeValidationRunId("run"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), items: new Dictionary<string, object?> { [" "] = 1 }));
    }
}
