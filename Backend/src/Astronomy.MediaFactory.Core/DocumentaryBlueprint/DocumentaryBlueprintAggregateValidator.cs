using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintAggregateValidator
{
    public IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> Validate(
        DocumentaryBlueprintAggregate aggregate,
        DocumentaryBlueprintVariantArtifact? longArtifact = null,
        DocumentaryBlueprintVariantArtifact? shortArtifact = null)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var embeddedLong = aggregate.LongVariant;
        var embeddedShort = aggregate.ShortVariant;
        var errors = new List<DocumentaryBlueprintProjectionDiagnostic>();
        void Add(string code, string message) => errors.Add(new(code, message));

        if (string.IsNullOrWhiteSpace(aggregate.AggregateId) || string.IsNullOrWhiteSpace(aggregate.SourceIntentChecksum))
            Add("P4P_AGGREGATE_INVALID", "Aggregate identity or source intent checksum is blank.");
        if (aggregate.SourceIntentId != embeddedLong.SourceIntentId || aggregate.SourceIntentId != embeddedShort.SourceIntentId ||
            aggregate.SourceIntentChecksum != embeddedLong.SourceIntentChecksum || aggregate.SourceIntentChecksum != embeddedShort.SourceIntentChecksum ||
            string.IsNullOrWhiteSpace(embeddedLong.SourceVariantIntentChecksum) || string.IsNullOrWhiteSpace(embeddedShort.SourceVariantIntentChecksum))
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate source intent authority differs from a variant.");
        if (embeddedLong.Variant != "Long" || embeddedShort.Variant != "Short" ||
            embeddedLong.VariantArtifactId == embeddedShort.VariantArtifactId)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Exactly one distinctly identified Long and Short projection is required.");
        if (aggregate.ProfileId != embeddedLong.ProfileId || aggregate.ProfileId != embeddedShort.ProfileId ||
            aggregate.ProfileVersion != embeddedLong.ProfileVersion || aggregate.ProfileVersion != embeddedShort.ProfileVersion ||
            aggregate.Language != embeddedLong.Language || aggregate.Language != embeddedShort.Language ||
            aggregate.SourceLineage != embeddedLong.SourceLineage || aggregate.SourceLineage != embeddedShort.SourceLineage)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate profile or language differs from a variant.");
        if (embeddedLong.Blueprint.Scenes.Select(x => x.SceneId).Intersect(
                embeddedShort.Blueprint.Scenes.Select(x => x.SceneId), StringComparer.Ordinal).Any())
            Add("P4P_VARIANT_IDENTITY_OVERLAP", "Long and Short scene identities overlap.");
        if ((longArtifact is not null && JsonSerializer.Serialize(embeddedLong) != JsonSerializer.Serialize(longArtifact)) ||
            (shortArtifact is not null && JsonSerializer.Serialize(embeddedShort) != JsonSerializer.Serialize(shortArtifact)))
            Add("P4P_EXTERNAL_PROJECTION_MISMATCH", "External variant is not an exact projection of its embedded authority.");
        if (embeddedLong.ActualSceneCount != embeddedLong.ExpectedSceneCount ||
            embeddedShort.ActualSceneCount != embeddedShort.ExpectedSceneCount ||
            embeddedLong.SceneTraceability.Count != embeddedLong.ActualSceneCount || embeddedShort.SceneTraceability.Count != embeddedShort.ActualSceneCount ||
            embeddedLong.TotalAllocatedDurationSeconds != embeddedLong.DurationBudgetSeconds ||
            embeddedShort.TotalAllocatedDurationSeconds != embeddedShort.DurationBudgetSeconds ||
            aggregate.AggregateDurationSummary.LongDurationSeconds != embeddedLong.TotalAllocatedDurationSeconds ||
            aggregate.AggregateDurationSummary.ShortDurationSeconds != embeddedShort.TotalAllocatedDurationSeconds ||
            aggregate.AggregateDurationSummary.TotalDurationSeconds != embeddedLong.TotalAllocatedDurationSeconds + embeddedShort.TotalAllocatedDurationSeconds)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Scene counts or duration totals do not reconcile.");
        var questions = embeddedLong.QuestionCoverage.CoveredQuestions.Concat(embeddedShort.QuestionCoverage.CoveredQuestions)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var knowledge = embeddedLong.KnowledgeCoverage.CoveredKnowledgeReferences.Concat(embeddedShort.KnowledgeCoverage.CoveredKnowledgeReferences)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        if (!aggregate.AggregateCoverage.CoveredQuestions.SequenceEqual(questions) ||
            !aggregate.AggregateCoverage.CoveredKnowledgeReferences.SequenceEqual(knowledge))
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate coverage is not the deterministic variant union.");
        if (!DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(embeddedLong) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(embeddedShort) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate))
            Add("P4P_CHECKSUM_INVALID", "An aggregate or variant checksum is invalid.");
        return errors;
    }
}
