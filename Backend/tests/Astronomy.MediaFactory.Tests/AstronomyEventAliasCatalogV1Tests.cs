using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyEventAliasCatalogV1Tests
{
    [Fact]
    public void DefaultCatalogValidationSucceedsAndCanonicalIdsAreUnique()
    {
        var catalog = new AstronomyEventAliasCatalogV1();
        var validation = catalog.Validate();

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        Assert.Equal(catalog.SupportedCanonicalEventTypes.Count, catalog.SupportedCanonicalEventTypes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DuplicateAliasValidationFails()
    {
        var catalog = new AstronomyEventAliasCatalogV1(
        [
            new("Solar Eclipse", "SolarEclipse"),
            new("Solar Eclipse", "SolarEclipse")
        ]);

        var validation = catalog.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Duplicate alias 'Solar Eclipse'"));
    }

    [Fact]
    public void AmbiguousAliasValidationFails()
    {
        var catalog = new AstronomyEventAliasCatalogV1(
        [
            new("Eclipse", "SolarEclipse"),
            new("Eclipse", "LunarEclipse")
        ]);

        var validation = catalog.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Ambiguous alias 'Eclipse'"));
    }

    [Fact]
    public void AliasCycleValidationFails()
    {
        var catalog = new AstronomyEventAliasCatalogV1(
        [
            new("CycleA", "CycleB"),
            new("CycleB", "CycleA")
        ]);

        var validation = catalog.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Alias cycle detected"));
    }

    [Fact]
    public void UnsupportedAliasTargetValidationFails()
    {
        var catalog = new AstronomyEventAliasCatalogV1([new("Mystery Alias", "MysteryCanonical")]);

        var validation = catalog.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("unsupported canonical id 'MysteryCanonical'"));
    }

    [Fact]
    public void SupportedAliasesAreDeterministicAndExplicit()
    {
        var catalog = new AstronomyEventAliasCatalogV1();

        Assert.Equal("PlanetPairing", catalog.Normalize("PlanetaryConjunction").CanonicalEventType);
        Assert.Equal("SolarEclipse", catalog.Normalize("Solar Eclipse").CanonicalEventType);
        Assert.False(catalog.Normalize("A title about a Solar Eclipse").Supported);
    }
}
