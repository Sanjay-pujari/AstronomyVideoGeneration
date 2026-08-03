using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeScopeComparerTests
{
    private readonly Phase7KnowledgeScopeComparer comparer = new();

    [Fact]
    public void DifferentNormalizedValuesDoNotCreateDifferentScopes() =>
        Assert.Equal(Phase7KnowledgeScopeComparison.SameScope, comparer.Compare(new(), new()));

    [Fact]
    public void GeneralSeasonalAndSpecificLocationTimeIsSpecialization() =>
        Assert.Equal(Phase7KnowledgeScopeComparison.EventIsSpecialization,
            comparer.Compare(new(), new("Observation", "Delhi", StartUtc: DateTimeOffset.Parse("2026-01-01T20:00:00Z"), EndUtc: DateTimeOffset.Parse("2026-01-01T22:00:00Z"))));

    [Fact]
    public void DifferentLocationsWithExplicitScopesCanBeIncomparable() =>
        Assert.Equal(Phase7KnowledgeScopeComparison.DistinctNonConflictingScopes,
            comparer.Compare(new("Observation", "Delhi"), new("Observation", "Sydney")));

    [Fact]
    public void ComparisonMetadataCannotEstablishScope()
    {
        var values = new Phase7KnowledgeComparisonMetadata("1344", "decimal", "light-year", true, 10, .9m);
        Assert.Equal(Phase7KnowledgeScopeComparison.SameScope, comparer.Compare(new(), new()));
        Assert.False(typeof(Phase7KnowledgeAuthorityScope).GetProperties().Any(p =>
            typeof(Phase7KnowledgeComparisonMetadata).GetProperties().Select(x => x.Name).Contains(p.Name)));
        Assert.NotNull(values);
    }
}
