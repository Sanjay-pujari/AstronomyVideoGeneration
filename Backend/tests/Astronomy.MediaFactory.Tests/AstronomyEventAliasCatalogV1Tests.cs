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
    [Theory]
    [InlineData("Constellation")]
    [InlineData("CONSTELLATION")]
    [InlineData("constellation")]
    public void ConstellationCaseVariantsResolveAsCanonicalTerminalValues(string eventType)
    {
        var result = new AstronomyEventAliasCatalogV1().Normalize(eventType);

        Assert.True(result.Supported);
        Assert.Equal("Constellation", result.CanonicalEventType);
        Assert.Empty(result.AppliedAliases);
        Assert.DoesNotContain(result.DiagnosticMessages, d => d.Contains("alias cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("PlanetPairing", false)]
    [InlineData("PLANET_CONJUNCTION", true)]
    [InlineData("PlanetConjunction", true)]
    [InlineData("PlanetaryConjunction", true)]
    public void PlanetPairingCanonicalAndAliasesResolveToSameCanonicalValue(string eventType, bool aliasApplied)
    {
        var result = new AstronomyEventAliasCatalogV1().Normalize(eventType);

        Assert.True(result.Supported);
        Assert.Equal("PlanetPairing", result.CanonicalEventType);
        Assert.Equal(aliasApplied, result.AppliedAliases.Count > 0);
        Assert.DoesNotContain(result.DiagnosticMessages, d => d.Contains("alias cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectSelfAliasRegistrationIsRejected()
    {
        var validation = new AstronomyEventAliasCatalogV1([new("PlanetPairing", "PlanetPairing")]).Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Self alias rejected"));
    }

    [Fact]
    public void CaseInsensitiveSelfAliasRegistrationIsRejected()
    {
        var validation = new AstronomyEventAliasCatalogV1([new("CONSTELLATION", "Constellation")]).Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Self alias rejected"));
        Assert.Contains(validation.Errors, e => e.Contains("Canonical value must not be registered as an alias"));
    }

    [Fact]
    public void TwoNodeAliasCycleRegistrationIsRejected()
    {
        var validation = new AstronomyEventAliasCatalogV1([new("CycleA", "CycleB"), new("CycleB", "CycleA")]).Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Alias cycle detected"));
    }

    [Fact]
    public void UnknownInputProducesOnlyItsOwnUnsupportedDiagnostic()
    {
        var result = new AstronomyEventAliasCatalogV1().Normalize("UNKNOWN_SKY_EVENT");

        Assert.False(result.Supported);
        Assert.Null(result.CanonicalEventType);
        var diagnostic = Assert.Single(result.DiagnosticMessages);
        Assert.Equal("Unsupported astronomy event type 'UNKNOWN_SKY_EVENT'.", diagnostic);
        Assert.DoesNotContain("CONSTELLATION", diagnostic);
        Assert.DoesNotContain("PlanetPairing", diagnostic);
        Assert.DoesNotContain("alias cycle", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

}
