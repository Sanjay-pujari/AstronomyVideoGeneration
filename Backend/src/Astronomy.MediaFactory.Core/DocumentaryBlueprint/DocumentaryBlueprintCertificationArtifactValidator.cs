namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Shared complete-set validation for in-memory, staged, and resumed Phase 5 authority.</summary>
public static class DocumentaryBlueprintCertificationArtifactValidator
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] RequiredStages = ["Phase4Authority", "ProductionCertification", "EditorialContract", "CompleteSet"];

    public static IReadOnlyList<string> Validate(DocumentaryBlueprintCertificationIntegrationResult result, DocumentaryBlueprintCertificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var c = result.Certification;
        var e = result.EditorialContract;
        var d = result.Diagnostics;
        var artifacts = new[] { request.Master, request.Long, request.Short };
        var requested = request.RequestedVariants.ToHashSet(Comparer);
        var knownVariants = artifacts.Select(x => x.Metadata.Variant).ToHashSet(Comparer);

        Required(c.CertificationId, "CertificationId", errors);
        Required(c.ExecutionId, "ExecutionId", errors);
        Required(c.PlanId, "PlanId", errors);
        Required(c.EventId, "EventId", errors);
        Required(c.Language, "Language", errors);
        Required(c.Profile, "Profile", errors);
        Required(c.SourcePhase4Checksum, "SourcePhase4Checksum", errors);
        Required(c.SourceMasterBlueprintChecksum, "SourceMasterBlueprintChecksum", errors);
        Required(c.SourceLongBlueprintChecksum, "SourceLongBlueprintChecksum", errors);
        Required(c.SourceShortBlueprintChecksum, "SourceShortBlueprintChecksum", errors);
        Required(c.CertificationVersion, "CertificationVersion", errors);
        Required(c.CertifierType, "CertifierType", errors);

        if (c.ExecutionId != request.ExecutionId || c.PlanId != request.PlanId || c.EventId != request.EventId)
            errors.Add("Certification identity does not match the current execution.");
        if (!string.Equals(c.Language, request.Language, StringComparison.OrdinalIgnoreCase) || c.Profile != request.Profile)
            errors.Add("Certification language/profile does not match.");
        if (c.SourcePhase4Checksum != DocumentaryBlueprintCertificationChecksum.SourcePhase4(request)) errors.Add("Certification source Phase 4 checksum is stale.");
        if (c.SourceMasterBlueprintChecksum != request.Master.Metadata.Checksum) errors.Add("Certification source master blueprint checksum is stale.");
        if (c.SourceLongBlueprintChecksum != request.Long.Metadata.Checksum) errors.Add("Certification source long blueprint checksum is stale.");
        if (c.SourceShortBlueprintChecksum != request.Short.Metadata.Checksum) errors.Add("Certification source short blueprint checksum is stale.");
        if (c.SemanticChecksum != DocumentaryBlueprintCertificationChecksum.Calculate(c)) errors.Add("Certification semantic checksum is invalid.");

        if (c.Passed && c.CertificationStatus == DocumentaryBlueprintCertificationStatus.Rejected) errors.Add("Rejected certification cannot have Passed=true.");
        if (!c.Passed && c.CertificationStatus != DocumentaryBlueprintCertificationStatus.Rejected) errors.Add("Certified certification cannot have Passed=false.");
        if (c.Passed && c.BlockingIssues.Count != 0) errors.Add("Passed certification cannot contain blocking issues.");
        if (!c.Passed && c.BlockingIssues.Count == 0) errors.Add("Rejected certification must contain a blocking issue.");
        if (c.CertificationStatus == DocumentaryBlueprintCertificationStatus.Certified && c.NonBlockingWarnings.Count != 0) errors.Add("Certified status cannot contain warnings.");
        if (c.CertificationStatus == DocumentaryBlueprintCertificationStatus.CertifiedWithWarnings && c.NonBlockingWarnings.Count == 0) errors.Add("CertifiedWithWarnings status requires a warning.");
        if (!c.Passed || c.CertificationStatus == DocumentaryBlueprintCertificationStatus.Rejected || c.BlockingIssues.Count != 0) errors.Add("Certification was rejected.");

        ValidateVariants(c, requested, errors);
        ValidateScenes(c, artifacts, knownVariants, errors);
        SetEquals(c.CoverageOutcomes, request.Master.Coverage.CoveredViewerQuestionIds, "Certification coverage outcomes do not reconcile with Phase 4 authority.", errors);
        var references = artifacts.SelectMany(x => x.Blueprint.Scenes).SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId);
        SetEquals(c.KnowledgeReferenceOutcomes, references, "Certification knowledge-reference outcomes do not reconcile with Phase 4 authority.", errors);
        if (request.Master.Coverage.CoveredViewerQuestionIds.Count != 0 && c.CoverageOutcomes.Count == 0) errors.Add("Certification coverage outcomes are required for viewer questions.");
        if (c.EditorialOutcomes.Count == 0 || c.EditorialOutcomes.Any(string.IsNullOrWhiteSpace)) errors.Add("Certification editorial outcomes are required.");

        Required(e.ContractId, "Editorial ContractId", errors);
        if (e.ExecutionId != c.ExecutionId || e.EventId != c.EventId || e.Language != c.Language || e.Profile != c.Profile ||
            e.SourceCertificationId != c.CertificationId || e.SourceCertificationChecksum != c.SemanticChecksum || e.SourcePhase4Checksum != c.SourcePhase4Checksum)
            errors.Add("Editorial contract lineage is invalid.");
        if (e.Checksum != DocumentaryBlueprintCertificationChecksum.Calculate(e)) errors.Add("Editorial contract checksum is invalid.");
        SetEquals(e.AllowedVariants, c.CertifiedVariants, "Editorial allowed variants do not match certified variants.", errors);
        if (e.AllowedVariants.Intersect(c.RejectedVariants, Comparer).Any()) errors.Add("Editorial contract allows a rejected variant.");
        var certifiedSceneIds = c.SceneLevelOutcomes.Where(x => x.Certified).Select(x => x.SceneId);
        SetEquals(e.CertifiedSceneIds, certifiedSceneIds, "Editorial certified scene IDs do not match certification outcomes.", errors);
        var masterSceneIds = request.Master.Blueprint.Scenes.OrderBy(x => x.SceneNumber).Select(x => x.SceneId).ToArray();
        SequenceEquals(e.SceneOrder, masterSceneIds, "Editorial scene order does not match the certified master scene order.", errors);
        if (e.SceneOrder.Distinct(StringComparer.Ordinal).Count() != e.SceneOrder.Count) errors.Add("Editorial scene order contains duplicate scene IDs.");
        SetEquals(e.NarrativeStages.Keys, e.SceneOrder, "Editorial narrative-stage keys do not match scene order.", errors);
        SetEquals(e.SceneRoles.Keys, e.SceneOrder, "Editorial scene-role keys do not match scene order.", errors);
        foreach (var scene in request.Master.Blueprint.Scenes)
        {
            if (e.NarrativeStages.GetValueOrDefault(scene.SceneId) != scene.NarrativeStage.ToString()) errors.Add($"Editorial narrative stage is stale for scene '{scene.SceneId}'.");
            if (e.SceneRoles.GetValueOrDefault(scene.SceneId) != scene.SceneRole.ToString()) errors.Add($"Editorial scene role is stale for scene '{scene.SceneId}'.");
        }
        SetEquals(e.MandatoryViewerQuestions, request.Master.Blueprint.Scenes.Select(x => x.ViewerQuestion.Text), "Editorial viewer questions do not reconcile with Phase 4 authority.", errors);
        SetEquals(e.LearningObjectives, request.Master.Coverage.CoveredLearningObjectiveIds, "Editorial learning objectives do not reconcile with Phase 4 authority.", errors);
        SetEquals(e.KnowledgeReferenceConstraints, request.Master.Blueprint.Scenes.SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId), "Editorial knowledge constraints do not reconcile with Phase 4 authority.", errors);
        DictionaryEquals(e.DeferredItems, request.Master.Coverage.DeferralReasons, "Editorial deferred items do not reconcile with Phase 4 authority.", errors);
        SetEquals(e.ApprovedEditorialWarnings, c.NonBlockingWarnings, "Editorial warnings do not reconcile with certification.", errors);
        SetEquals(e.BlockingConstraints, c.BlockingIssues, "Editorial blocking constraints do not reconcile with certification.", errors);
        if (e.NarrationEligible != c.Passed || e.StoryFrameEligible != c.Passed) errors.Add("Editorial downstream eligibility does not match certification status.");
        if (c.Passed && e.DownstreamRequirements.Count == 0) errors.Add("Passed certification requires downstream requirements.");
        if (ContainsAbsolutePath(e)) errors.Add("Editorial contract contains a machine-specific absolute path.");

        ValidateDiagnostics(d, c, request, errors);
        return errors;
    }

    private static void ValidateVariants(DocumentaryBlueprintCertification c, HashSet<string> requested, List<string> errors)
    {
        if (c.CertifiedVariants.Count != c.CertifiedVariants.Distinct(Comparer).Count() || c.RejectedVariants.Count != c.RejectedVariants.Distinct(Comparer).Count()) errors.Add("Certification contains duplicate variants.");
        if (c.CertifiedVariants.Intersect(c.RejectedVariants, Comparer).Any()) errors.Add("Certified and rejected variants overlap.");
        if (c.CertifiedVariants.Concat(c.RejectedVariants).Any(x => !requested.Contains(x))) errors.Add("Certification contains an unknown variant.");
        SetEquals(c.CertifiedVariants.Concat(c.RejectedVariants), requested, "Every requested variant must be classified exactly once.", errors);
    }

    private static void ValidateScenes(DocumentaryBlueprintCertification c, DocumentaryBlueprintArtifact[] artifacts, HashSet<string> knownVariants, List<string> errors)
    {
        foreach (var group in c.SceneLevelOutcomes.GroupBy(x => x.Variant, Comparer))
        {
            if (!knownVariants.Contains(group.Key)) { errors.Add($"Scene outcome contains unknown variant '{group.Key}'."); continue; }
            if (group.Select(x => x.SceneId).Distinct(StringComparer.Ordinal).Count() != group.Count()) errors.Add($"Scene outcomes contain duplicate scene IDs for variant '{group.Key}'.");
            var sequence = group.Select(x => x.Sequence).Order().ToArray();
            if (!sequence.SequenceEqual(Enumerable.Range(1, sequence.Length))) errors.Add($"Scene outcome sequence is not positive and contiguous for variant '{group.Key}'.");
            var variantPassed = c.CertifiedVariants.Contains(group.Key, Comparer) || (group.Key.Equals("Master", StringComparison.OrdinalIgnoreCase) && c.Passed);
            if (group.Any(x => x.Certified != variantPassed)) errors.Add($"Scene certification state disagrees with variant '{group.Key}'.");
        }
        foreach (var artifact in artifacts)
        {
            var expected = artifact.Blueprint.Scenes.Select(x => x.SceneId);
            var actual = c.SceneLevelOutcomes.Where(x => Comparer.Equals(x.Variant, artifact.Metadata.Variant)).Select(x => x.SceneId);
            SetEquals(actual, expected, $"Scene outcomes do not exactly cover Phase 4 variant '{artifact.Metadata.Variant}'.", errors);
        }
    }

    private static void ValidateDiagnostics(DocumentaryBlueprintCertificationDiagnostics d, DocumentaryBlueprintCertification c, DocumentaryBlueprintCertificationRequest request, List<string> errors)
    {
        if (d.ExecutionId != request.ExecutionId || d.SourcePhase4Checksum != c.SourcePhase4Checksum) errors.Add("Certification diagnostics source identity does not reconcile.");
        if (d.CertifierType != c.CertifierType || d.CertifierVersion != c.CertificationVersion) errors.Add("Certification diagnostics certifier identity does not reconcile.");
        Required(d.IntegrationServiceType, "Diagnostics IntegrationServiceType", errors);
        Required(d.IntegrationServiceVersion, "Diagnostics IntegrationServiceVersion", errors);
        var names = new[] { "documentary-blueprint.json", "documentary-blueprint.long.json", "documentary-blueprint.short.json", "blueprint-build-diagnostics.json" };
        if (d.InputArtifactPaths.Count != 4 || names.Any(n => !d.InputArtifactPaths.Any(p => Path.GetFileName(p.Replace('\\', '/')) == n))) errors.Add("Certification diagnostics must identify exactly four Phase 4 input artifacts.");
        if (d.InputArtifactPaths.Any(Path.IsPathRooted)) errors.Add("Certification diagnostics input paths must be workspace-relative.");
        var artifacts = new Dictionary<string, DocumentaryBlueprintArtifact>(StringComparer.OrdinalIgnoreCase) { ["master"] = request.Master, ["long"] = request.Long, ["short"] = request.Short };
        if (artifacts.Any(x => d.InputArtifactChecksums.GetValueOrDefault(x.Key) != x.Value.Metadata.Checksum)) errors.Add("Certification diagnostics input checksums do not reconcile.");
        if (artifacts.Any(x => d.InputSceneCounts.GetValueOrDefault(x.Key, -1) != x.Value.Blueprint.Scenes.Count)) errors.Add("Certification diagnostics input scene counts do not reconcile.");
        if (d.InputCoverageCount != request.Master.Coverage.CoveredViewerQuestionIds.Count) errors.Add("Certification diagnostics coverage count does not reconcile.");
        if (d.CertifiedSceneCount != c.SceneLevelOutcomes.Count(x => x.Certified) || d.RejectedSceneCount != c.SceneLevelOutcomes.Count(x => !x.Certified)) errors.Add("Certification scene diagnostics do not reconcile.");
        if (d.BlockingIssueCount != c.BlockingIssues.Count || d.WarningCount != c.NonBlockingWarnings.Count) errors.Add("Certification diagnostics issue counts do not reconcile.");
        SetEquals(d.CertifiedVariants, c.CertifiedVariants, "Certification diagnostics certified variants do not reconcile.", errors);
        SetEquals(d.RejectedVariants, c.RejectedVariants, "Certification diagnostics rejected variants do not reconcile.", errors);
        if (RequiredStages.Any(x => !d.ValidationStagesExecuted.Contains(x, StringComparer.Ordinal))) errors.Add("Certification diagnostics validation stages are incomplete.");
        if (d.BuildDurationMilliseconds < 0) errors.Add("Certification diagnostics build duration cannot be negative.");
    }

    private static void Required(string? value, string field, List<string> errors) { if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field} is required."); }
    private static void SetEquals(IEnumerable<string> actual, IEnumerable<string> expected, string message, List<string> errors)
    { if (!actual.ToHashSet(Comparer).SetEquals(expected)) errors.Add(message); }
    private static void SequenceEquals(IEnumerable<string> actual, IEnumerable<string> expected, string message, List<string> errors)
    { if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) errors.Add(message); }
    private static void DictionaryEquals(IReadOnlyDictionary<string, string> actual, IReadOnlyDictionary<string, string> expected, string message, List<string> errors)
    { if (actual.Count != expected.Count || expected.Any(x => actual.GetValueOrDefault(x.Key) != x.Value)) errors.Add(message); }
    private static bool ContainsAbsolutePath(DocumentaryBlueprintEditorialContract e) =>
        e.DownstreamRequirements.Concat(e.ApprovedEditorialWarnings).Concat(e.BlockingConstraints).Concat(e.DeferredItems.Values).Any(Path.IsPathRooted);
}
