using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintCertificationArtifactValidatorTests
{
    [Fact] public void Validator_accepts_complete_certified_authority() => Assert.Empty(Validate().Errors);
    [Fact] public void Validator_accepts_certified_with_warnings_authority() => Assert.Empty(Validate(warning: true).Errors);
    [Fact] public void Validator_rejects_rejected_certification() => AssertError(Mutate(c => c with { Passed = false, CertificationStatus = DocumentaryBlueprintCertificationStatus.Rejected, BlockingIssues = ["blocked"], CertifiedVariants = [], RejectedVariants = ["Long", "Short"], SceneLevelOutcomes = c.SceneLevelOutcomes.Select(x => x with { Certified = false }).ToArray() }), "Certification was rejected.");
    [Fact] public void Validator_rejects_passed_certification_with_blocking_issues() => AssertError(Mutate(c => c with { BlockingIssues = ["blocked"] }), "Passed certification cannot contain blocking issues.");
    [Fact] public void Validator_rejects_rejected_status_with_passed_true() => AssertError(Mutate(c => c with { CertificationStatus = DocumentaryBlueprintCertificationStatus.Rejected }), "Rejected certification cannot have Passed=true.");
    [Fact] public void Validator_rejects_certified_status_with_passed_false() => AssertError(Mutate(c => c with { Passed = false }), "Certified certification cannot have Passed=false.");
    [Fact] public void Validator_rejects_empty_certification_id() => AssertError(Mutate(c => c with { CertificationId = "" }), "CertificationId is required.");
    [Fact] public void Validator_rejects_stale_source_phase4_checksum() => AssertError(Mutate(c => c with { SourcePhase4Checksum = "stale" }), "Certification source Phase 4 checksum is stale.");
    [Fact] public void Validator_rejects_stale_master_checksum() => AssertError(Mutate(c => c with { SourceMasterBlueprintChecksum = "stale" }), "Certification source master blueprint checksum is stale.");
    [Fact] public void Validator_rejects_stale_long_checksum() => AssertError(Mutate(c => c with { SourceLongBlueprintChecksum = "stale" }), "Certification source long blueprint checksum is stale.");
    [Fact] public void Validator_rejects_stale_short_checksum() => AssertError(Mutate(c => c with { SourceShortBlueprintChecksum = "stale" }), "Certification source short blueprint checksum is stale.");
    [Fact] public void Validator_rejects_variant_overlap() => AssertError(Mutate(c => c with { RejectedVariants = ["Long"] }), "Certified and rejected variants overlap.");
    [Fact] public void Validator_rejects_unclassified_requested_variant() => AssertError(Mutate(c => c with { CertifiedVariants = ["Long"] }), "Every requested variant must be classified exactly once.");
    [Fact] public void Validator_rejects_duplicate_scene_outcome() => AssertError(Mutate(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Append(c.SceneLevelOutcomes[0]).ToArray() }), "duplicate scene IDs");
    [Fact] public void Validator_rejects_unknown_scene_outcome() => AssertError(Mutate(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Append(c.SceneLevelOutcomes[0] with { SceneId = "unknown" }).ToArray() }), "do not exactly cover");
    [Fact] public void Validator_rejects_missing_scene_outcome() => AssertError(Mutate(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Skip(1).ToArray() }), "do not exactly cover");
    [Fact] public void Validator_rejects_non_contiguous_scene_sequence() => AssertError(Mutate(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Select((x, i) => i == 0 ? x with { Sequence = 3 } : x).ToArray() }), "not positive and contiguous");
    [Fact] public void Validator_rejects_editorial_lineage_mismatch() => AssertError(MutateEditorial(e => e with { SourceCertificationId = "stale" }), "Editorial contract lineage is invalid.");
    [Fact] public void Validator_rejects_editorial_checksum_mismatch()
    {
        var fixture = Phase5CertificationFixture.Create();
        var result = fixture.Result with { EditorialContract = fixture.Result.EditorialContract with { Checksum = "corrupt" } };
        Assert.Contains("Editorial contract checksum is invalid.", DocumentaryBlueprintCertificationArtifactValidator.Validate(result, fixture.Request));
    }
    [Fact] public void Validator_rejects_editorial_variant_mismatch() => AssertError(MutateEditorial(e => e with { AllowedVariants = ["Long"] }), "Editorial allowed variants");
    [Fact] public void Validator_rejects_editorial_scene_order_mismatch() => AssertError(MutateEditorial(e => e with { SceneOrder = e.SceneOrder.Reverse().ToArray() }), "Editorial scene order");
    [Fact] public void Validator_rejects_diagnostics_count_mismatch() => AssertError(MutateDiagnostics(d => d with { CertifiedSceneCount = d.CertifiedSceneCount + 1 }), "Certification scene diagnostics");
    [Fact] public void Validator_rejects_diagnostics_source_checksum_mismatch() => AssertError(MutateDiagnostics(d => d with { SourcePhase4Checksum = "stale" }), "diagnostics source identity");

    private static (DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request, IReadOnlyList<string> Errors) Validate(bool warning = false)
    {
        var f = Phase5CertificationFixture.Create(warning);
        return (f.Result, f.Request, DocumentaryBlueprintCertificationArtifactValidator.Validate(f.Result, f.Request));
    }
    private static (DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request, IReadOnlyList<string> Errors) Mutate(Func<DocumentaryBlueprintCertification, DocumentaryBlueprintCertification> mutation)
    {
        var f = Phase5CertificationFixture.Create();
        var certification = mutation(f.Result.Certification);
        certification = certification with { SemanticChecksum = DocumentaryBlueprintCertificationChecksum.Calculate(certification) };
        var result = f.Result with { Certification = certification };
        return (result, f.Request, DocumentaryBlueprintCertificationArtifactValidator.Validate(result, f.Request));
    }
    private static (DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request, IReadOnlyList<string> Errors) MutateEditorial(Func<DocumentaryBlueprintEditorialContract, DocumentaryBlueprintEditorialContract> mutation)
    {
        var f = Phase5CertificationFixture.Create(); var editorial = mutation(f.Result.EditorialContract);
        editorial = editorial with { Checksum = DocumentaryBlueprintCertificationChecksum.Calculate(editorial) };
        var result = f.Result with { EditorialContract = editorial };
        return (result, f.Request, DocumentaryBlueprintCertificationArtifactValidator.Validate(result, f.Request));
    }
    private static (DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request, IReadOnlyList<string> Errors) MutateDiagnostics(Func<DocumentaryBlueprintCertificationDiagnostics, DocumentaryBlueprintCertificationDiagnostics> mutation)
    { var f = Phase5CertificationFixture.Create(); var result = f.Result with { Diagnostics = mutation(f.Result.Diagnostics) }; return (result, f.Request, DocumentaryBlueprintCertificationArtifactValidator.Validate(result, f.Request)); }
    private static void AssertError((DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request, IReadOnlyList<string> Errors) value, string text) => Assert.Contains(value.Errors, x => x.Contains(text, StringComparison.Ordinal));
}

public sealed class DocumentaryBlueprintCertificationChecksumTests
{
    [Fact] public void Certification_checksum_is_stable_across_generated_utc() => Stable(c => c with { GeneratedUtc = c.GeneratedUtc.AddYears(1) });
    [Fact] public void Certification_checksum_is_stable_across_semantically_unordered_collection_order() => Stable(c => c with { CertifiedVariants = c.CertifiedVariants.Reverse().ToArray(), SceneLevelOutcomes = c.SceneLevelOutcomes.Reverse().ToArray(), CoverageOutcomes = c.CoverageOutcomes.Reverse().ToArray() });
    [Fact] public void Certification_checksum_changes_when_status_changes() => Changes(c => c with { CertificationStatus = DocumentaryBlueprintCertificationStatus.Rejected });
    [Fact] public void Certification_checksum_changes_when_blocking_issue_changes() => Changes(c => c with { BlockingIssues = ["changed"] });
    [Fact] public void Certification_checksum_changes_when_scene_certification_changes() => Changes(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Select((x, i) => i == 0 ? x with { Certified = !x.Certified } : x).ToArray() });
    [Fact] public void Certification_checksum_changes_when_scene_sequence_changes() => Changes(c => c with { SceneLevelOutcomes = c.SceneLevelOutcomes.Select((x, i) => i == 0 ? x with { Sequence = 99 } : x).ToArray() });
    [Fact] public void Certification_checksum_changes_when_knowledge_reference_changes() => Changes(c => c with { KnowledgeReferenceOutcomes = [.. c.KnowledgeReferenceOutcomes, "new"] });
    [Fact] public void Certification_checksum_changes_when_coverage_outcome_changes() => Changes(c => c with { CoverageOutcomes = [.. c.CoverageOutcomes, "new"] });
    [Fact] public void Editorial_checksum_is_stable_across_generated_utc() => EditorialStable(e => e with { GeneratedUtc = e.GeneratedUtc.AddDays(1) });
    [Fact] public void Editorial_checksum_is_stable_across_dictionary_insertion_order() => EditorialStable(e => e with { NarrativeStages = e.NarrativeStages.Reverse().ToDictionary(x => x.Key, x => x.Value) });
    [Fact] public void Editorial_checksum_changes_when_scene_order_changes() => EditorialChanges(e => e with { SceneOrder = e.SceneOrder.Reverse().ToArray() });
    [Fact] public void Editorial_checksum_changes_when_eligibility_changes() => EditorialChanges(e => e with { NarrationEligible = !e.NarrationEligible });
    [Fact] public void Source_phase4_checksum_is_stable_across_requested_variant_order() { var f = Phase5CertificationFixture.Create(); Assert.Equal(DocumentaryBlueprintCertificationChecksum.SourcePhase4(f.Request), DocumentaryBlueprintCertificationChecksum.SourcePhase4(f.Request with { RequestedVariants = f.Request.RequestedVariants.Reverse().ToArray() })); }
    [Fact] public void Source_phase4_checksum_changes_when_master_checksum_changes() => SourceChanges(r => r with { Master = r.Master with { Metadata = r.Master.Metadata with { Checksum = "changed" } } });
    [Fact] public void Source_phase4_checksum_changes_when_builder_version_changes() => SourceChanges(r => r with { Phase4Diagnostics = r.Phase4Diagnostics with { BuilderVersion = "changed" } });

    private static void Stable(Func<DocumentaryBlueprintCertification, DocumentaryBlueprintCertification> m) { var c = Phase5CertificationFixture.Create().Result.Certification; Assert.Equal(DocumentaryBlueprintCertificationChecksum.Calculate(c), DocumentaryBlueprintCertificationChecksum.Calculate(m(c))); }
    private static void Changes(Func<DocumentaryBlueprintCertification, DocumentaryBlueprintCertification> m) { var c = Phase5CertificationFixture.Create().Result.Certification; Assert.NotEqual(DocumentaryBlueprintCertificationChecksum.Calculate(c), DocumentaryBlueprintCertificationChecksum.Calculate(m(c))); }
    private static void EditorialStable(Func<DocumentaryBlueprintEditorialContract, DocumentaryBlueprintEditorialContract> m) { var e = Phase5CertificationFixture.Create().Result.EditorialContract; Assert.Equal(DocumentaryBlueprintCertificationChecksum.Calculate(e), DocumentaryBlueprintCertificationChecksum.Calculate(m(e))); }
    private static void EditorialChanges(Func<DocumentaryBlueprintEditorialContract, DocumentaryBlueprintEditorialContract> m) { var e = Phase5CertificationFixture.Create().Result.EditorialContract; Assert.NotEqual(DocumentaryBlueprintCertificationChecksum.Calculate(e), DocumentaryBlueprintCertificationChecksum.Calculate(m(e))); }
    private static void SourceChanges(Func<DocumentaryBlueprintCertificationRequest, DocumentaryBlueprintCertificationRequest> m) { var r = Phase5CertificationFixture.Create().Request; Assert.NotEqual(DocumentaryBlueprintCertificationChecksum.SourcePhase4(r), DocumentaryBlueprintCertificationChecksum.SourcePhase4(m(r))); }
}

internal static class Phase5CertificationFixture
{
    public static (DocumentaryBlueprintCertificationIntegrationResult Result, DocumentaryBlueprintCertificationRequest Request) Create(bool warning = false)
    {
        var master = Artifact("Master", warning ? ["review terminology"] : []);
        var request = new DocumentaryBlueprintCertificationRequest("execution", "plan", "orion", "en-US", "LongVideo", master, Artifact("Long"), Artifact("Short"), Diagnostics(master), ["Long", "Short"]);
        var result = new DocumentaryBlueprintCertificationIntegrationService(new DocumentaryProductionCertifier()).CertifyAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return (result, request);
    }
    private static DocumentaryBlueprintArtifact Artifact(string variant, IReadOnlyList<string>? warnings = null)
    {
        var blueprint = OrionDocumentaryBlueprintFixture.CreateOrdered();
        var coverage = new BlueprintCoverage(["question.1", "question.2"], [], [], ["objective.1"], [],
            blueprint.Scenes.ToDictionary(x => x.SceneId, x => (IReadOnlyList<string>)[x.SceneNumber == 1 ? "question.1" : "question.2"]),
            blueprint.Scenes.ToDictionary(x => x.SceneId, x => (IReadOnlyList<global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference>)x.KnowledgeReferences.Select(k => new global::Astronomy.MediaFactory.Core.ViewerKnowledgeReference(k.KnowledgeEntryId, k.Section, "test", "Resolved")).ToArray()), new Dictionary<string, string>());
        var metadata = new BlueprintArtifactMetadata("execution", "orion", "en-US", "LongVideo", variant, "1", "", DateTimeOffset.UnixEpoch, "phase3", "intel");
        var artifact = new DocumentaryBlueprintArtifact(metadata, blueprint, coverage, warnings ?? []);
        return artifact with { Metadata = metadata with { Checksum = DocumentaryBlueprintChecksum.Calculate(artifact) } };
    }
    private static BlueprintBuildDiagnostics Diagnostics(DocumentaryBlueprintArtifact master) => new(nameof(DocumentaryBlueprintBuilder), "1.0", "integration", [], new Dictionary<string, string>(), 2, 1, 2, 2, 2, master.Coverage, 4, [], [], [], [], [], 0);
}
