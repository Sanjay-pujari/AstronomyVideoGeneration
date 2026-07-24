using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticCapabilityCatalogV1Tests
{
    private readonly SemanticCapabilityCatalogV1 _catalog = new();


    [Fact]
    public void Catalog_Constructs_Successfully()
    {
        var catalog = new SemanticCapabilityCatalogV1();
        Assert.NotEmpty(catalog.Definitions);
    }


    [Fact]
    public void Canonical_Count_Remains_19() => Assert.Equal(19, SemanticCapabilityVocabularyV1.CanonicalIds.Count);

    [Fact]
    public void Structured_Field_Terms_Are_Not_Aliases()
    {
        var aliases = _catalog.Definitions.SelectMany(d => d.AcceptedAliases.Where(a => !a.Equals(d.CapabilityId, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var term in new[] { "Direction", "Radiant", "LocationContext", "Region", "VisibilityRegion" })
        {
            Assert.DoesNotContain(term, aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Direction_Maps_To_ObservationDirection_Field() => AssertStructuredField("Direction", SemanticCapabilityVocabularyV1.ObservationDirection, "ObservationDirection.direction");

    [Fact]
    public void Radiant_Maps_To_MeteorActivity_Field() => AssertStructuredField("Radiant", SemanticCapabilityVocabularyV1.MeteorActivity, "MeteorActivity.radiant");

    [Fact]
    public void Location_Terms_Map_To_ObservationLocation_Field()
    {
        foreach (var term in new[] { "LocationContext", "Region", "VisibilityRegion" })
        {
            AssertStructuredField(term, SemanticCapabilityVocabularyV1.ObservationLocation, "ObservationLocation.locationName");
        }
    }

    [Fact]
    public void CulturalNameContext_Is_Canonical()
    {
        Assert.Contains(SemanticCapabilityVocabularyV1.CulturalNameContext, SemanticCapabilityVocabularyV1.CanonicalIds);
        Assert.True(_catalog.TryGet(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext), out var definition));
        Assert.Equal(SemanticCapabilityVocabularyV1.CulturalNameContext, definition.CapabilityId);
    }

    private void AssertStructuredField(string term, string expectedCapability, string expectedField)
    {
        var definition = _catalog.Definitions.Single(d => d.CapabilityId == expectedCapability);
        Assert.DoesNotContain(term, definition.AcceptedAliases, StringComparer.OrdinalIgnoreCase);
        var result = _catalog.ResolveLegacyTerm(term);
        Assert.Equal(LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration, result.Status);
        Assert.Equal(expectedCapability, result.CanonicalCapabilityId!.Value.Value);
        Assert.Equal(expectedField, result.StructuredFieldPath);
    }

    [Fact]
    public void Zhr_Terms_Are_Not_Both_Capability_Aliases_And_Legacy_Field_Mappings()
    {
        var meteor = _catalog.GetRequired(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.MeteorActivity));
        Assert.DoesNotContain("Zhr", meteor.AcceptedAliases, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZHR", meteor.AcceptedAliases, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZenithalHourlyRate", meteor.AcceptedAliases, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Zenithal Hourly Rate", meteor.AcceptedAliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(LegacySemanticCapabilityMapV1.Entries, e => e.LegacyTerm == "ZHR" && e.CanonicalCapabilityId!.Value == SemanticCapabilityVocabularyV1.MeteorActivity && e.StructuredFieldPath == "MeteorActivity.zhr");
        Assert.Contains(LegacySemanticCapabilityMapV1.Entries, e => e.LegacyTerm == "ZenithalHourlyRate" && e.CanonicalCapabilityId!.Value == SemanticCapabilityVocabularyV1.MeteorActivity && e.StructuredFieldPath == "MeteorActivity.zhr");
    }

    [Fact]
    public void Synthetic_Duplicate_Structured_Field_Ownership_Fails_Validation()
    {
        var definitions = _catalog.Definitions.Select(d => d.CapabilityId == SemanticCapabilityVocabularyV1.MeteorActivity ? d with { AcceptedAliases = [d.CapabilityId, "ZHR"] } : d).ToArray();
        var result = SemanticCapabilityCatalogV1.Validate(definitions, LegacySemanticCapabilityMapV1.Entries);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("also registered as alias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exactly_19_Canonical_V1_Capabilities_Are_Registered() => Assert.Equal(19, _catalog.Definitions.Count);

    [Fact]
    public void Every_Canonical_Id_Resolves_To_Itself()
    {
        foreach (var id in SemanticCapabilityVocabularyV1.CanonicalIds)
        {
            var result = _catalog.ResolveLegacyTerm(id);
            Assert.Equal(LegacySemanticCapabilityResolutionStatus.CanonicalMatch, result.Status);
            Assert.Equal(id, result.CanonicalCapabilityId!.Value.Value);
            Assert.True(_catalog.TryGet(new SemanticCapabilityId(id), out var definition));
            Assert.Equal(id, definition.CapabilityId);
        }
    }

    [Fact]
    public void Catalog_Validation_Succeeds_For_Approved_Definitions() => Assert.True(_catalog.Validate().IsValid, string.Join("; ", _catalog.Validate().Errors));

    [Fact]
    public void Unknown_Terms_Return_Unsupported_Not_Fallback()
    {
        var result = _catalog.ResolveLegacyTerm("TotallyUnknownCapability");
        Assert.Equal(LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, result.Status);
        Assert.Null(result.CanonicalCapabilityId);
    }

    [Fact]
    public void No_Reciprocal_Aliases_Duplicate_Owners_Or_Alias_Equals_Other_Canonical_Id()
    {
        var result = _catalog.Validate();
        Assert.DoesNotContain(result.Errors, e => e.Contains("Reciprocal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Duplicate alias", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Alias equals canonical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Synthetic_Duplicate_Alias_Fails_Validation()
    {
        var definitions = _catalog.Definitions.ToArray();
        var duplicate = definitions[0] with { AcceptedAliases = [definitions[0].CapabilityId, "SharedAlias"] };
        var duplicate2 = definitions[1] with { AcceptedAliases = [definitions[1].CapabilityId, "SharedAlias"] };
        var result = SemanticCapabilityCatalogV1.Validate([duplicate, duplicate2, .. definitions.Skip(2)], LegacySemanticCapabilityMapV1.Entries);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate alias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Definitions_And_Migration_Results_Json_RoundTrip()
    {
        var definition = _catalog.Definitions.First();
        Assert.Equal(definition, JsonSerializer.Deserialize<SemanticCapabilityDefinition>(JsonSerializer.Serialize(definition)));
        var resolution = _catalog.ResolveLegacyTerm("ZHR");
        Assert.Equal(resolution, JsonSerializer.Deserialize<LegacySemanticCapabilityResolution>(JsonSerializer.Serialize(resolution)));
    }

    [Fact]
    public void Collections_Are_Immutable_And_Structurally_Equal()
    {
        Assert.IsAssignableFrom<IReadOnlyCollection<SemanticCapabilityDefinition>>(_catalog.Definitions);
        Assert.False(_catalog.Definitions is ICollection<SemanticCapabilityDefinition> { IsReadOnly: false });
        Assert.Equal(new SemanticCapabilityCatalogV1().Definitions.ToArray(), _catalog.Definitions.ToArray());
    }


    [Theory]
    [InlineData("CulturalNameContext", "CulturalNameContext", null, LegacySemanticCapabilityResolutionStatus.CanonicalMatch)]
    [InlineData("Direction", "ObservationDirection", "ObservationDirection.direction", LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration)]
    [InlineData("ZHR", "MeteorActivity", "MeteorActivity.zhr", LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration)]
    [InlineData("LocationContext", "ObservationLocation", "ObservationLocation.locationName", LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration)]
    public void LegacyCapabilityResolver_Centralizes_Canonicalization(string term, string expectedCapability, string? expectedField, LegacySemanticCapabilityResolutionStatus expectedStatus)
    {
        var resolver = new LegacySemanticCapabilityResolverV1(_catalog);

        var resolution = resolver.Resolve(term);

        Assert.Equal(expectedStatus, resolution.Status);
        Assert.Equal(expectedCapability, resolution.CanonicalCapabilityId!.Value.Value);
        Assert.Equal(expectedField, resolution.StructuredFieldPath);
        Assert.Equal(expectedCapability, resolver.Canonicalize(new SemanticCapabilityId(term)).Value);
    }

    [Fact]
    public void LegacyCapabilityResolver_Reports_Unknown_Terms_As_Unsupported()
    {
        var resolver = new LegacySemanticCapabilityResolverV1(_catalog);

        var resolution = resolver.Resolve("TotallyUnknownCapability");

        Assert.Equal(LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, resolution.Status);
        Assert.Null(resolution.CanonicalCapabilityId);
    }

    [Fact]
    public void Adapter_Registry_Legacy_Lookup_Returns_Canonical_Adapters()
    {
        var registry = new SemanticSourceAdapterRegistryV1();

        var adapters = registry.GetAdapters(new SemanticCapabilityId("ZHR"));

        Assert.NotEmpty(adapters);
        Assert.All(adapters, adapter => Assert.Equal(SemanticCapabilityVocabularyV1.MeteorActivity, adapter.SupportedCapabilityId.Value));
    }

    [Fact]
    public void Policy_Catalog_Legacy_Lookup_Returns_Canonical_Policy()
    {
        var catalog = new SemanticSourcePolicyCatalogV1();

        Assert.True(catalog.TryGet(new SemanticCapabilityId("ZHR"), out var policy));
        Assert.Equal(SemanticCapabilityVocabularyV1.MeteorActivity, policy.SemanticCapabilityId.Value);
    }

    [Fact]
    public void Runtime_Catalog_References_Are_Restricted_To_Compatibility_Boundary()
    {
        var root = RepositoryTestPaths.Root();
        var forbiddenRuntimeFiles = new[]
        {
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"
        };
        foreach (var file in forbiddenRuntimeFiles.Where(f => File.Exists(Path.Combine(root, f))))
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            foreach (var token in new[] { "SemanticCapabilityCatalogV1", "ISemanticCapabilityCatalogV1", "LegacySemanticCapabilityMapV1" }) Assert.DoesNotContain(token, text);
        }

        var infra = Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Infrastructure");
        var approved = new[]
        {
            Path.Combine(infra, "Production/Narration/Semantics/Catalog/SemanticCapabilityCatalogV1.cs"),
            Path.Combine(infra, "Production/Narration/Semantics/Catalog/LegacySemanticCapabilityMapV1.cs")
        }.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = Directory.EnumerateFiles(infra, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("LegacySemanticCapabilityMapV1", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .ToArray();

        var unexpected = references
            .Where(file => !approved.Contains(file))
            .Select(file => Path.GetRelativePath(root, file))
            .OrderBy(file => file)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Unexpected direct LegacySemanticCapabilityMapV1 references:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unexpected));
    }
}
