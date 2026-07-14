using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

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
    public void Exactly_18_Canonical_V1_Capabilities_Are_Registered() => Assert.Equal(18, _catalog.Definitions.Count);

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

    [Fact]
    public void Runtime_Code_Does_Not_Reference_V1_Catalog_Types()
    {
        var root = RepositoryTestPaths.Root();
        var files = new[]
        {
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Orchestration/RC2/NarrationGeneratorV5.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/SemanticCapabilityResolver.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/SemanticCapabilitySourceRegistry.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Narration/Semantics/RequiredSemanticFactResolver.cs",
            "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"
        };
        var forbidden = new[] { "SemanticCapabilityCatalogV1", "ISemanticCapabilityCatalogV1", "LegacySemanticCapabilityMapV1" };
        foreach (var file in files.Where(f => File.Exists(Path.Combine(root, f))))
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            foreach (var token in forbidden) Assert.DoesNotContain(token, text);
        }
    }
}
