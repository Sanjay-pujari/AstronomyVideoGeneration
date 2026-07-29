namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Shared complete-set validation for in-memory, staged, and resumed Phase 5 authority.</summary>
public static class DocumentaryBlueprintCertificationArtifactValidator
{
    public static IReadOnlyList<string> Validate(DocumentaryBlueprintCertificationIntegrationResult result, DocumentaryBlueprintCertificationRequest request)
    {
        var errors = new List<string>();
        var c = result.Certification; var e = result.EditorialContract; var d = result.Diagnostics;
        if (c.ExecutionId != request.ExecutionId || c.PlanId != request.PlanId || c.EventId != request.EventId) errors.Add("Certification identity does not match the current execution.");
        if (!string.Equals(c.Language, request.Language, StringComparison.OrdinalIgnoreCase) || c.Profile != request.Profile) errors.Add("Certification language/profile does not match.");
        if (c.SourcePhase4Checksum != DocumentaryBlueprintCertificationChecksum.SourcePhase4(request)) errors.Add("Certification source Phase 4 checksum is stale.");
        if (c.SourceMasterBlueprintChecksum != request.Master.Metadata.Checksum || c.SourceLongBlueprintChecksum != request.Long.Metadata.Checksum || c.SourceShortBlueprintChecksum != request.Short.Metadata.Checksum) errors.Add("Certification source blueprint checksums do not match.");
        if (c.SemanticChecksum != DocumentaryBlueprintCertificationChecksum.Calculate(c)) errors.Add("Certification semantic checksum is invalid.");
        if (!c.Passed || c.CertificationStatus == DocumentaryBlueprintCertificationStatus.Rejected || c.BlockingIssues.Count != 0) errors.Add("Certification was rejected.");
        if (e.ExecutionId != request.ExecutionId || e.EventId != request.EventId || e.SourceCertificationId != c.CertificationId || e.SourceCertificationChecksum != c.SemanticChecksum || e.SourcePhase4Checksum != c.SourcePhase4Checksum) errors.Add("Editorial contract lineage is invalid.");
        if (e.Checksum != DocumentaryBlueprintCertificationChecksum.Calculate(e)) errors.Add("Editorial contract checksum is invalid.");
        if (d.ExecutionId != request.ExecutionId || d.SourcePhase4Checksum != c.SourcePhase4Checksum || d.BlockingIssueCount != c.BlockingIssues.Count || d.WarningCount != c.NonBlockingWarnings.Count) errors.Add("Certification diagnostics do not reconcile.");
        if (d.CertifiedSceneCount != c.SceneLevelOutcomes.Count(x => x.Certified) || d.RejectedSceneCount != c.SceneLevelOutcomes.Count(x => !x.Certified)) errors.Add("Certification scene diagnostics do not reconcile.");
        return errors;
    }
}
