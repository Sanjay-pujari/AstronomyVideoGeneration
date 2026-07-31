using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed record Phase5CompatibilityContext(string ExecutionId, string PlanId, string EventId,
    string Language, string Profile, IReadOnlyList<string> RequestedVariants);

public interface IDocumentaryBlueprintPhase5CompatibilityAdapter
{
    DocumentaryBlueprintCertificationRequest Adapt(DocumentaryBlueprintAggregate aggregate, Phase5CompatibilityContext context);
}

/// <summary>
/// Pure, in-memory bridge for the retained Phase 5 contract.  The Master value is a
/// non-authoritative compatibility view of the aggregate's Long projection; it is
/// never a Phase 4 publication or manifest entry.
/// </summary>
public sealed class DocumentaryBlueprintPhase5CompatibilityAdapter : IDocumentaryBlueprintPhase5CompatibilityAdapter
{
    public DocumentaryBlueprintCertificationRequest Adapt(DocumentaryBlueprintAggregate aggregate, Phase5CompatibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(context);
        if (!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.LongVariant) ||
            !DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(aggregate.ShortVariant))
            throw new InvalidDataException("Published DocumentaryBlueprintAggregate checksum is invalid.");
        if (aggregate.ExecutionId != context.ExecutionId || aggregate.PlanId != context.PlanId ||
            aggregate.EventId != context.EventId || !string.Equals(aggregate.Language, context.Language, StringComparison.OrdinalIgnoreCase) ||
            aggregate.ProfileId != context.Profile)
            throw new InvalidDataException("Published DocumentaryBlueprintAggregate identity does not match the Phase 5 request.");

        var longArtifact = Project(aggregate.LongVariant);
        var shortArtifact = Project(aggregate.ShortVariant);
        // Phase 5 historically used Master for common coverage and editorial ordering.
        // It is deliberately only a compatibility container over the canonical Long view.
        var masterValue = longArtifact with { Metadata = longArtifact.Metadata with { Variant = "Master", Checksum = string.Empty } };
        var master = masterValue with { Metadata = masterValue.Metadata with { Checksum = DocumentaryBlueprintChecksum.Calculate(masterValue) } };
        var diagnostics = new BlueprintBuildDiagnostics(nameof(DocumentaryBlueprintPhase5CompatibilityAdapter), "1.0",
            "PublishedDocumentaryBlueprintAggregate", ["04-blueprint/documentary-blueprint.json",
                "04-blueprint/documentary-blueprint.long.json", "04-blueprint/documentary-blueprint.short.json",
                "04-blueprint/blueprint-build-diagnostics.json"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["aggregate"] = aggregate.DeterministicChecksum,
                ["master"] = master.Metadata.Checksum, ["long"] = longArtifact.Metadata.Checksum, ["short"] = shortArtifact.Metadata.Checksum },
            aggregate.AggregateCoverage.CoveredQuestions.Count, CoveredObjectives(aggregate).Count,
            master.Blueprint.Scenes.Count, longArtifact.Blueprint.Scenes.Count, shortArtifact.Blueprint.Scenes.Count,
            master.Coverage, aggregate.AggregateCoverage.CoveredKnowledgeReferences.Count, [], [], [], [],
            [aggregate.AggregateId, aggregate.SourceIntentId, aggregate.SourceIntentChecksum], 0);
        return new(context.ExecutionId, context.PlanId, context.EventId, context.Language, context.Profile,
            master, longArtifact, shortArtifact, diagnostics, context.RequestedVariants, aggregate);
    }

    private static DocumentaryBlueprintArtifact Project(DocumentaryBlueprintVariantArtifact variant)
    {
        var coveredQuestions = variant.QuestionCoverage.CoveredQuestions;
        var deferred = variant.DeferredQuestions.Select(x => x.QuestionId).ToArray();
        var sections = variant.Blueprint.Scenes.ToDictionary(x => x.SceneId,
            x => (IReadOnlyList<string>)[x.ViewerQuestion.Text], StringComparer.Ordinal);
        var knowledge = variant.Blueprint.Scenes.ToDictionary(x => x.SceneId,
            x => (IReadOnlyList<ViewerKnowledgeReference>)[], StringComparer.Ordinal);
        var coverage = new BlueprintCoverage(coveredQuestions, deferred, [], CoveredObjectives(variant), [], sections, knowledge,
            variant.DeferredQuestions.ToDictionary(x => x.QuestionId, x => x.ReasonCode, StringComparer.Ordinal));
        var metadata = new BlueprintArtifactMetadata(variant.ExecutionId, variant.EventId, variant.Language, variant.ProfileId,
            variant.Variant, variant.ContractVersion, string.Empty, DateTimeOffset.UnixEpoch,
            variant.SourceLineage.Phase3QuestionBankChecksum, variant.SourceLineage.Phase2SemanticChecksum);
        var value = new DocumentaryBlueprintArtifact(metadata, variant.Blueprint, coverage, []);
        return value with { Metadata = metadata with { Checksum = DocumentaryBlueprintChecksum.Calculate(value) } };
    }

    private static IReadOnlyList<string> CoveredObjectives(DocumentaryBlueprintAggregate aggregate) =>
        aggregate.LongVariant.SceneTraceability.Concat(aggregate.ShortVariant.SceneTraceability)
            .Select(x => x.LearningObjectiveId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static IReadOnlyList<string> CoveredObjectives(DocumentaryBlueprintVariantArtifact variant) =>
        variant.SceneTraceability.Select(x => x.LearningObjectiveId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
