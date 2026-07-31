using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintAggregateValidator
{
    public IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> Validate(
        DocumentaryBlueprintAggregate aggregate,
        DocumentaryBlueprintVariantArtifact longArtifact,
        DocumentaryBlueprintVariantArtifact shortArtifact)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(longArtifact);
        ArgumentNullException.ThrowIfNull(shortArtifact);
        var errors = new List<DocumentaryBlueprintProjectionDiagnostic>();
        void Add(string code, string message) => errors.Add(new(code, message));

        if (string.IsNullOrWhiteSpace(aggregate.AggregateId) || string.IsNullOrWhiteSpace(aggregate.SourceIntentChecksum))
            Add("P4P_AGGREGATE_INVALID", "Aggregate identity or source intent checksum is blank.");
        if (aggregate.SourceIntentId != longArtifact.SourceIntentId || aggregate.SourceIntentId != shortArtifact.SourceIntentId ||
            aggregate.SourceIntentChecksum != longArtifact.SourceIntentChecksum || aggregate.SourceIntentChecksum != shortArtifact.SourceIntentChecksum)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate source intent authority differs from a variant.");
        if (longArtifact.Variant != "Long" || shortArtifact.Variant != "Short" ||
            longArtifact.VariantArtifactId == shortArtifact.VariantArtifactId)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Exactly one distinctly identified Long and Short projection is required.");
        if (aggregate.ProfileId != longArtifact.ProfileId || aggregate.ProfileId != shortArtifact.ProfileId ||
            aggregate.ProfileVersion != longArtifact.ProfileVersion || aggregate.ProfileVersion != shortArtifact.ProfileVersion ||
            aggregate.Language != longArtifact.Language || aggregate.Language != shortArtifact.Language)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate profile or language differs from a variant.");
        if (longArtifact.Blueprint.Scenes.Select(x => x.SceneId).Intersect(
                shortArtifact.Blueprint.Scenes.Select(x => x.SceneId), StringComparer.Ordinal).Any())
            Add("P4P_VARIANT_IDENTITY_OVERLAP", "Long and Short scene identities overlap.");
        if (JsonSerializer.Serialize(aggregate.LongBlueprint) != JsonSerializer.Serialize(longArtifact.Blueprint) ||
            JsonSerializer.Serialize(aggregate.ShortBlueprint) != JsonSerializer.Serialize(shortArtifact.Blueprint) ||
            aggregate.LongProjectionChecksum != longArtifact.DeterministicChecksum ||
            aggregate.ShortProjectionChecksum != shortArtifact.DeterministicChecksum)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Embedded blueprints or projection checksums do not reconcile.");
        if (longArtifact.ActualSceneCount != longArtifact.ExpectedSceneCount ||
            shortArtifact.ActualSceneCount != shortArtifact.ExpectedSceneCount ||
            longArtifact.TotalAllocatedDurationSeconds != longArtifact.DurationBudgetSeconds ||
            shortArtifact.TotalAllocatedDurationSeconds != shortArtifact.DurationBudgetSeconds ||
            aggregate.AggregateDurationSummary.LongDurationSeconds != longArtifact.TotalAllocatedDurationSeconds ||
            aggregate.AggregateDurationSummary.ShortDurationSeconds != shortArtifact.TotalAllocatedDurationSeconds)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Scene counts or duration totals do not reconcile.");
        var questions = longArtifact.QuestionCoverage.CoveredQuestions.Concat(shortArtifact.QuestionCoverage.CoveredQuestions)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var knowledge = longArtifact.KnowledgeCoverage.CoveredKnowledgeReferences.Concat(shortArtifact.KnowledgeCoverage.CoveredKnowledgeReferences)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        if (!aggregate.AggregateCoverage.CoveredQuestions.SequenceEqual(questions) ||
            !aggregate.AggregateCoverage.CoveredKnowledgeReferences.SequenceEqual(knowledge))
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Aggregate coverage is not the deterministic variant union.");
        if (!DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(longArtifact) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(shortArtifact) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate))
            Add("P4P_CHECKSUM_INVALID", "An aggregate or variant checksum is invalid.");
        return errors;
    }
}
