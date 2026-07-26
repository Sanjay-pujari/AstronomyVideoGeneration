using System.Globalization;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryProductionPackageValidator
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    internal static void ValidateComplete(string packageId,DocumentaryNarrativeReleaseCandidate candidate,DocumentaryProductionPackageManifest manifest,DocumentaryProductionPackageMetadata metadata)
    {
        DocumentaryNarrativeReleaseCandidateValidator.Validate(candidate);
        var correlation=candidate.Metadata.CorrelationId;
        if(packageId!=$"{candidate.ReleaseCandidateId}.production-package"||manifest.PackageId!=packageId||
           !string.Equals(correlation,candidate.AcceptanceDecision.Metadata.CorrelationId,StringComparison.Ordinal)||
           !string.Equals(correlation,candidate.ConvergenceState.Metadata.CorrelationId,StringComparison.Ordinal)||
           !string.Equals(correlation,metadata.CorrelationId,StringComparison.Ordinal)||!string.Equals(correlation,manifest.CorrelationId,StringComparison.Ordinal)||
           !candidate.IsAccepted||!candidate.IsClean||!candidate.IsFullyResolved||candidate.FinalFindingCount!=0||
           candidate.AcceptanceDecision.PrimaryReason!=DocumentaryNarrativeAcceptanceReason.ConvergedAndClean||candidate.AcceptanceDecision.SupportingReasons.Count!=0||
           candidate.ConvergenceState.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||candidate.ConvergenceState.NextAction!=DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft||
           !ManifestMatches(manifest,candidate,packageId))
            throw new ArgumentException("Production package identity, evidence, or correlation is inconsistent.");
    }

    internal static bool ManifestMatches(DocumentaryProductionPackageManifest manifest,
        DocumentaryNarrativeReleaseCandidate candidate,string packageId)
    {
        if (!string.Equals(manifest.PackageId,packageId,StringComparison.Ordinal) || manifest.Entries.Count != 6)
            return false;
        var expected = new (DocumentaryProductionPackageSection Section,string Type,string Identity,string Version)[]
        {
            (DocumentaryProductionPackageSection.AcceptedNarrative,nameof(DocumentaryNarrativeDraft),candidate.DraftId,candidate.DraftVersion),
            (DocumentaryProductionPackageSection.FinalValidationEvidence,nameof(DocumentaryNarrativeDraftValidationResult),candidate.FinalValidationResult.DraftId,candidate.DraftVersion),
            (DocumentaryProductionPackageSection.RevisionHistory,"DocumentaryNarrativeRevisionCycleHistory",$"{candidate.ConvergenceId}.cycles",candidate.CompletedCycleCount.ToString(CultureInfo.InvariantCulture)),
            (DocumentaryProductionPackageSection.ConvergenceEvidence,nameof(DocumentaryNarrativeRevisionConvergenceState),candidate.ConvergenceId,candidate.ConvergenceState.Metadata.ConvergenceSchemaVersion),
            (DocumentaryProductionPackageSection.AcceptanceEvidence,nameof(DocumentaryNarrativeAcceptanceDecision),$"{candidate.ConvergenceId}.acceptance",candidate.AcceptanceDecision.Metadata.AcceptanceSchemaVersion),
            (DocumentaryProductionPackageSection.PackageManifest,nameof(DocumentaryProductionPackageManifest),manifest.ManifestId,manifest.ManifestSchemaVersion)
        };
        return expected.Select((value,index)=>(value,index)).All(pair =>
        {
            var entry=manifest.Entries[pair.index]; var value=pair.value;
            return entry.Section==value.Section && entry.Sequence==pair.index && entry.IsRequired &&
                string.Equals(entry.ArtifactType,value.Type,StringComparison.Ordinal) &&
                string.Equals(entry.ArtifactIdentity,value.Identity,StringComparison.Ordinal) &&
                string.Equals(entry.ArtifactVersion,value.Version,StringComparison.Ordinal);
        });
    }

    internal static bool ConvergenceStatesAreEquivalent(DocumentaryNarrativeRevisionConvergenceState left,
        DocumentaryNarrativeRevisionConvergenceState right)
    {
        DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(left);
        DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(right);
        return string.Equals(left.ConvergenceId,right.ConvergenceId,StringComparison.Ordinal) &&
            string.Equals(left.OriginalDraftId,right.OriginalDraftId,StringComparison.Ordinal) &&
            string.Equals(left.OriginalDraftVersion,right.OriginalDraftVersion,StringComparison.Ordinal) &&
            string.Equals(left.CurrentDraftId,right.CurrentDraftId,StringComparison.Ordinal) &&
            string.Equals(left.CurrentDraftVersion,right.CurrentDraftVersion,StringComparison.Ordinal) && JsonEqual(left,right);
    }

    internal static bool AcceptanceDecisionsAreEquivalent(DocumentaryNarrativeAcceptanceDecision left,
        DocumentaryNarrativeAcceptanceDecision right) =>
        string.Equals(left.ConvergenceId,right.ConvergenceId,StringComparison.Ordinal) &&
        string.Equals(left.CurrentDraftId,right.CurrentDraftId,StringComparison.Ordinal) &&
        string.Equals(left.CurrentDraftVersion,right.CurrentDraftVersion,StringComparison.Ordinal) && JsonEqual(left,right);

    internal static bool RevisionCyclesAreEquivalent(IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> left,
        IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> right) => left.Count==right.Count &&
        left.Select((cycle,index)=>(cycle,index)).All(pair =>
            string.Equals(pair.cycle.CycleId,right[pair.index].CycleId,StringComparison.Ordinal) && JsonEqual(pair.cycle,right[pair.index]));

    private static bool JsonEqual<T>(T left,T right) => string.Equals(JsonSerializer.Serialize(left,WebJson),JsonSerializer.Serialize(right,WebJson),StringComparison.Ordinal);
}
