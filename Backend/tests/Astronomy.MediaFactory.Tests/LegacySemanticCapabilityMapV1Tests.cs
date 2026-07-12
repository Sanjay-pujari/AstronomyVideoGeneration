using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

namespace Astronomy.MediaFactory.Tests;

public sealed class LegacySemanticCapabilityMapV1Tests
{
    private readonly SemanticCapabilityCatalogV1 _catalog = new();

    [Fact]
    public void Every_Current_Legacy_Term_Maps_Exactly_Once()
    {
        Assert.All(LegacySemanticCapabilityMapV1.Entries.GroupBy(e => e.LegacyTerm, StringComparer.OrdinalIgnoreCase), g => Assert.Single(g));
        Assert.Equal(72, LegacySemanticCapabilityMapV1.Entries.Length);
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

    private LegacySemanticCapabilityResolution AssertMaps(string term, string expected)
    {
        var result = _catalog.ResolveLegacyTerm(term);
        Assert.NotEqual(LegacySemanticCapabilityResolutionStatus.UnsupportedLegacyTerm, result.Status);
        Assert.Equal(expected, result.CanonicalCapabilityId!.Value.Value);
        return result;
    }
}
