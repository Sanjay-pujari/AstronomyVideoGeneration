namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceStarter
{
    public DocumentaryNarrativeRevisionConvergenceState Start(DocumentaryNarrativeDraft originalDraft,
        DocumentaryNarrativeDraftValidationResult initialValidationResult,
        DocumentaryNarrativeRevisionConvergencePolicy policy,
        DocumentaryNarrativeRevisionConvergenceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(originalDraft);
        ArgumentNullException.ThrowIfNull(initialValidationResult);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!string.Equals(originalDraft.DraftId, initialValidationResult.DraftId, StringComparison.Ordinal))
            throw new ArgumentException("Initial validation must identify the original draft exactly.", nameof(initialValidationResult));
        var clean = initialValidationResult.Findings.Count == 0;
        return new DocumentaryNarrativeRevisionConvergenceState(
            $"{originalDraft.DraftId}.revision-convergence.{originalDraft.Version}", originalDraft,
            initialValidationResult, originalDraft, initialValidationResult, Array.Empty<DocumentaryNarrativeRevisionCycleResult>(),
            policy, metadata,
            clean ? DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully : DocumentaryNarrativeRevisionConvergenceStatus.NotStarted,
            clean ? DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft : DocumentaryNarrativeRevisionConvergenceNextAction.PlanNextRevisionCycle, 0);
    }
}
