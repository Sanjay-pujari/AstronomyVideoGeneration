using System.Globalization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class StructuredFieldProjectionRegressionTests
{
    [Fact]
    public void EventWindow_Projection_Preserves_Property_Provenance()
    {
        var peak = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);
        var fact = Resolved(
            SemanticCapabilityVocabularyV1.EventWindow,
            new EventWindowValue(null, peak, null, null, null, null, null, null, null),
            "EventWindowValue",
            "ObservationMetadata",
            "v1.event-window.observation-metadata",
            "ObservationMetadata.EventWindow");

        var legacy = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "PeakUTC", "beat-1", "Required", "en")!;

        Assert.Equal("PeakUTC", legacy.FactType);
        Assert.Equal("EventWindow", legacy.SemanticMeaning);
        Assert.Equal("ObservationMetadata", legacy.SourceArtifact);
        Assert.Equal("ObservationMetadata.EventWindow.PeakUtc", legacy.SourceField);
        Assert.Contains("EventWindow.peakUtc", legacy.SourceInputs!);
        Assert.Equal("UTC", legacy.Unit);
    }

    [Fact]
    public void Missing_Optional_MeteorActivityZhr_Does_Not_Project_Filler()
    {
        var fact = Resolved(
            SemanticCapabilityVocabularyV1.MeteorActivity,
            new MeteorActivityValue("Perseids", null, null, null, null, null),
            "MeteorActivityValue",
            "ProductionEventIntelligence",
            "v1.meteor-activity.production-event-intelligence",
            "ProductionEventIntelligence.MeteorActivity");

        var legacy = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "ZHR", "beat-1", "Optional", "en");

        Assert.Null(legacy);
    }

    [Fact]
    public void Distance_Projects_From_ObjectKnowledge_Distance()
    {
        var value = new ObjectKnowledgeValue("M31", [new ObjectKnowledgeFactV1("Distance", "2.5 million light-years", new SemanticSourceProvenanceV1("AstronomyObjectKnowledgeProvider", "ObjectKnowledgeValue", "AstronomyObjectKnowledge.ObjectKnowledge.Distance", true))]);
        var fact = Resolved(SemanticCapabilityVocabularyV1.ObjectKnowledge, value, "ObjectKnowledgeValue", "AstronomyObjectKnowledgeProvider", "v1.object-knowledge.object-provider", "AstronomyObjectKnowledge.ObjectKnowledge");

        var legacy = LegacyRequiredSemanticFactCompatibilityMapper.Map(fact, "Distance", "beat-1", "Required", "en")!;

        Assert.Equal("Distance", legacy.FactType);
        Assert.Equal("ObjectKnowledge", legacy.SemanticMeaning);
        Assert.Equal("2.5 million light-years", legacy.CanonicalValue);
        Assert.Equal("AstronomyObjectKnowledge.ObjectKnowledge.Distance", legacy.SourceField);
    }

    private static ResolvedSemanticFactV1 Resolved(string capability, object value, string typeName, string sourceId, string adapterId, string sourcePath) => new(
        new SemanticCapabilityId(capability),
        SemanticResolutionStatusV1.Resolved,
        true,
        new SemanticSourceValueV1(value, typeName),
        value.ToString(),
        value.ToString(),
        $"{adapterId}:{sourcePath}",
        adapterId,
        sourceId,
        SemanticEvidenceCategoryV1.VerifiedEventData,
        SemanticEvidenceStrengthV1.Strong,
        .95m,
        [new(sourceId, typeName, sourcePath, true)],
        [],
        [],
        [],
        "FirstApprovedByPriority",
        [],
        [],
        "Resolved",
        "Resolved");
}
