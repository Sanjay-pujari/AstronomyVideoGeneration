using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

namespace Astronomy.MediaFactory.Tests;

public sealed class LegacySemanticCapabilityMapV1Tests
{
    private readonly SemanticCapabilityCatalogV1 _catalog = new();

    [Fact]
    public void Every_Current_Legacy_Term_Maps_Exactly_Once()
    {
        Assert.All(LegacySemanticCapabilityMapV1.Entries.GroupBy(e => e.LegacyTerm, StringComparer.OrdinalIgnoreCase), g => Assert.Single(g));
        Assert.Equal(74, LegacySemanticCapabilityMapV1.Entries.Length);
    }

    [Theory]
    [InlineData("EventDate")][InlineData("EventDateOrWindow")][InlineData("Date")][InlineData("EventTiming")][InlineData("ObservationTiming")][InlineData("StartTime")][InlineData("PeakTime")][InlineData("EndTime")][InlineData("PeakWindow")][InlineData("LocalPeakTime")][InlineData("PeakUTC")][InlineData("ViewingWindow")][InlineData("BestViewingWindowLocal")][InlineData("MoonriseTime")][InlineData("TimeOfDay")]
    public void Event_Timing_Legacy_Terms_Map_To_EventWindow_Subfields(string term) => AssertMaps(term, SemanticCapabilityVocabularyV1.EventWindow);

    [Fact] public void AngularRelationship_Maps_Only_To_AngularSeparation() => AssertMaps("AngularRelationship", SemanticCapabilityVocabularyV1.AngularSeparation);

    [Theory][InlineData("ObservationMode")][InlineData("VisibilityMethod")]
    public void ObservationMode_VisibilityMethod_Map_To_ObservationEquipment(string term) => AssertMaps(term, SemanticCapabilityVocabularyV1.ObservationEquipment);

    [Theory][InlineData("Zhr")][InlineData("ZHR")][InlineData("ZenithalHourlyRate")][InlineData("Zenithal Hourly Rate")]
    public void Zhr_Aliases_Map_To_MeteorActivity_Zhr(string term)
    {
        var result = AssertMaps(term, SemanticCapabilityVocabularyV1.MeteorActivity);
        Assert.Equal("MeteorActivity.zhr", result.StructuredFieldPath);
        Assert.Equal(LegacySemanticCapabilityMigrationDisposition.StructuredField, result.MigrationDisposition);
        Assert.Equal(LegacySemanticCapabilityResolutionStatus.StructuredFieldMigration, result.Status);
    }

    [Fact]
    public void All_Zhr_Forms_Resolve_To_Same_Canonical_Capability_And_Field()
    {
        var results = new[] { "Zhr", "ZHR", "ZenithalHourlyRate", "Zenithal Hourly Rate" }.Select(term => _catalog.ResolveLegacyTerm(term)).ToArray();
        Assert.All(results, result => Assert.Equal(SemanticCapabilityVocabularyV1.MeteorActivity, result.CanonicalCapabilityId!.Value.Value));
        Assert.All(results, result => Assert.Equal("MeteorActivity.zhr", result.StructuredFieldPath));
        Assert.Single(results.Select(result => result.CanonicalCapabilityId!.Value.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Single(results.Select(result => result.StructuredFieldPath).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_Legacy_Normalized_Term_Is_Unique()
    {
        static string Normalize(string value) => value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        Assert.All(LegacySemanticCapabilityMapV1.Entries.GroupBy(e => Normalize(e.LegacyTerm)), g => Assert.Single(g));
    }

    [Fact]
    public void Case_Only_Duplicate_Mappings_Fail_Validation()
    {
        var result = SemanticCapabilityCatalogV1.Validate(_catalog.Definitions, [LegacySemanticCapabilityMapV1.Path("CaseOnly", SemanticCapabilityVocabularyV1.MeteorActivity, "MeteorActivity.zhr"), LegacySemanticCapabilityMapV1.Path("caseonly", SemanticCapabilityVocabularyV1.MeteorActivity, "MeteorActivity.zhr")]);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate legacy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Whitespace_Equivalent_Duplicate_Mappings_Fail_Validation()
    {
        var result = SemanticCapabilityCatalogV1.Validate(_catalog.Definitions, [LegacySemanticCapabilityMapV1.Path("ZenithalHourlyRate", SemanticCapabilityVocabularyV1.MeteorActivity, "MeteorActivity.zhr"), LegacySemanticCapabilityMapV1.Path("Zenithal Hourly Rate", SemanticCapabilityVocabularyV1.MeteorActivity, "MeteorActivity.zhr")]);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate legacy", StringComparison.OrdinalIgnoreCase));
    }

    [Theory][InlineData("CulturalNameContext")][InlineData("Mythology")][InlineData("WolfMoon")][InlineData("SnowMoon")]
    public void Named_Full_Moon_Cultural_Terms_Map_To_CulturalContext(string term) => AssertMaps(term, SemanticCapabilityVocabularyV1.CulturalContext);

    [Theory][InlineData("EclipseType")][InlineData("Magnitude")]
    public void Eclipse_Terms_Map_To_EclipseCircumstances(string term) => AssertMaps(term, SemanticCapabilityVocabularyV1.EclipseCircumstances);

    [Fact]
    public void Duration_ReappearanceTime_Map_Deterministically()
    {
        Assert.Equal("OccultationContacts.duration", AssertMaps("Duration", SemanticCapabilityVocabularyV1.OccultationContacts).StructuredFieldPath);
        Assert.Equal("OccultationContacts.reappearanceTime", AssertMaps("ReappearanceTime", SemanticCapabilityVocabularyV1.OccultationContacts).StructuredFieldPath);
    }

    [Fact]
    public void Synthetic_Ambiguous_Legacy_Mapping_Fails_Validation()
    {
        var result = SemanticCapabilityCatalogV1.Validate(_catalog.Definitions, [.. LegacySemanticCapabilityMapV1.Entries, LegacySemanticCapabilityMapV1.Entry("EventType", SemanticCapabilityVocabularyV1.EditorialContext)]);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate legacy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Capability_Inventory_Classifies_Every_Legacy_Profile_Reference()
    {
        var eventTypes = new[] { "PlanetPairing", "PlanetaryConjunction", "Occultation", "Eclipse", "MeteorShower", "NamedFullMoon", "FullMoon", "Constellation", "PlanetProfile", "Comet", "DeepSkyObject", "BlackHoleOrScientificExplainer" };
        var profiles = eventTypes.Select(eventType => Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.AstronomyFamilyProfileCatalog.Resolve(TestJson.Json($"{{\"eventType\":\"{eventType}\"}}"), null));
        var referenced = profiles.SelectMany(p => p.RequiredFactTypes.Concat(p.OptionalFactTypes).Concat(p.ScientificConcepts).Concat(p.ProhibitedAssumptions).Concat(p.ValidationRules)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var classified = LegacySemanticCapabilityMapV1.Entries.Select(e => e.LegacyTerm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inventory = referenced.Select(term => (Term: term, Result: _catalog.ResolveLegacyTerm(term))).ToArray();

        Assert.DoesNotContain(inventory, row => row.Result.Status == LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm && !classified.Contains(row.Term));
        Assert.Contains(inventory, row => row.Term == "BestSeason" && row.Result.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.Future);
        Assert.Contains(inventory, row => row.Term == "DeepSkyObjects" && row.Result.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.Future);
        Assert.Contains(inventory, row => row.Term == "Distance" && row.Result.MigrationDisposition == LegacySemanticCapabilityMigrationDisposition.StructuredField && row.Result.StructuredFieldPath == "ObjectKnowledge.distance");
    }

    private LegacySemanticCapabilityResolution AssertMaps(string term, string expected)
    {
        var result = _catalog.ResolveLegacyTerm(term);
        Assert.NotEqual(LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, result.Status);
        Assert.Equal(expected, result.CanonicalCapabilityId!.Value.Value);
        return result;
    }
}
