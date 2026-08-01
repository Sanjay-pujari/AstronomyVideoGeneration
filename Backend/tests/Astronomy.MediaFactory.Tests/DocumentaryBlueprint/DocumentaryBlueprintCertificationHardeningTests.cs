using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
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

    private static void Stable(Func<DocumentaryBlueprintCertification, DocumentaryBlueprintCertification> m) { var c = Phase5CertificationFixture.Create().Result.Certification; Assert.Equal(DocumentaryBlueprintCertificationChecksum.Calculate(c), DocumentaryBlueprintCertificationChecksum.Calculate(m(c))); }
    private static void Changes(Func<DocumentaryBlueprintCertification, DocumentaryBlueprintCertification> m) { var c = Phase5CertificationFixture.Create().Result.Certification; Assert.NotEqual(DocumentaryBlueprintCertificationChecksum.Calculate(c), DocumentaryBlueprintCertificationChecksum.Calculate(m(c))); }
    private static void EditorialStable(Func<DocumentaryBlueprintEditorialContract, DocumentaryBlueprintEditorialContract> m) { var e = Phase5CertificationFixture.Create().Result.EditorialContract; Assert.Equal(DocumentaryBlueprintCertificationChecksum.Calculate(e), DocumentaryBlueprintCertificationChecksum.Calculate(m(e))); }
    private static void EditorialChanges(Func<DocumentaryBlueprintEditorialContract, DocumentaryBlueprintEditorialContract> m) { var e = Phase5CertificationFixture.Create().Result.EditorialContract; Assert.NotEqual(DocumentaryBlueprintCertificationChecksum.Calculate(e), DocumentaryBlueprintCertificationChecksum.Calculate(m(e))); }
}

public sealed class Phase5CertificationFixtureTests
{
    [Fact]
    public void Fixture_builds_valid_published_phase4_authority()
    {
        var authority = Phase5CertificationFixture.Create().PublishedPhase4;

        Assert.NotNull(authority);
        Assert.False(string.IsNullOrWhiteSpace(authority.AggregateId));
        Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(authority));
        Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(authority.LongVariant));
        Assert.True(DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(authority.ShortVariant));
    }

    [Fact]
    public void Fixture_builds_phase5_request_from_published_phase4_authority()
    {
        var fixture = Phase5CertificationFixture.Create();
        var authority = fixture.PublishedPhase4;

        Assert.Same(authority, fixture.Request.PublishedAggregate);
        Assert.Equal((authority.ExecutionId, authority.PlanId, authority.EventId, authority.Language, authority.ProfileId),
            (fixture.Request.ExecutionId, fixture.Request.PlanId, fixture.Request.EventId, fixture.Request.Language, fixture.Request.Profile));
        Assert.Equal(authority.LongVariant.DeterministicChecksum, fixture.Result.Validation.SourceLongChecksum);
        Assert.Equal(authority.ShortVariant.DeterministicChecksum, fixture.Result.Validation.SourceShortChecksum);
        Assert.Equal(["Long", "Short"], fixture.Request.RequestedVariants);
    }

    [Fact]
    public void Fixture_base_candidate_passes_artifact_validation()
    {
        var fixture = Phase5CertificationFixture.Create();
        Assert.Empty(DocumentaryBlueprintCertificationArtifactValidator.Validate(fixture.Result, fixture.Request));
    }

    [Fact]
    public void Fixture_warning_candidate_remains_certified()
    {
        var certification = Phase5CertificationFixture.Create(warning: true).Result.Certification;
        Assert.True(certification.Passed);
        Assert.NotEqual(DocumentaryBlueprintCertificationStatus.Rejected, certification.CertificationStatus);
        Assert.NotEmpty(certification.NonBlockingWarnings);
        Assert.Empty(certification.BlockingIssues);
    }

    [Fact]
    public async Task Integration_service_still_rejects_missing_published_phase4_authority()
    {
        var fixture = Phase5CertificationFixture.Create();
        var service = Phase5CertificationFixture.CreateService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CertifyAsync(fixture.Request with { PublishedAggregate = null }, CancellationToken.None));
        Assert.Equal("Phase 5 requires PublishedDocumentaryBlueprintAggregate.", exception.Message);
    }
}

internal sealed record Phase5CertificationFixtureResult(
    DocumentaryBlueprintAggregate PublishedPhase4,
    DocumentaryBlueprintCertificationRequest Request,
    DocumentaryBlueprintCertificationIntegrationResult Result);

internal static class Phase5CertificationFixture
{
    public static Phase5CertificationFixtureResult Create(bool warning = false)
    {
        var published = PublishedAuthority();
        var request = new DocumentaryBlueprintPhase5CompatibilityAdapter().Adapt(published,
            new("execution", "plan", "orion", "en-US", "LongVideo", ["Long", "Short"]));
        if (warning)
        {
            var masterValue = request.Master with { Warnings = ["review terminology"],
                Metadata = request.Master.Metadata with { Checksum = string.Empty } };
            request = request with { Master = masterValue with { Metadata = masterValue.Metadata with {
                Checksum = DocumentaryBlueprintChecksum.Calculate(masterValue) } } };
        }
        var result = CreateService().CertifyAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return new(published, request, result);
    }

    internal static DocumentaryBlueprintCertificationIntegrationService CreateService() => new(new DocumentaryProductionCertifier(),
        new DocumentaryBlueprintEditorialValidator(), new DocumentaryBlueprintCoverageEvaluator(),
        new DocumentaryBlueprintTransitionEvaluator(), new DocumentaryBlueprintPauseTestEvaluator());

    private static DocumentaryBlueprintAggregate PublishedAuthority()
    {
        var lineage = new DocumentarySourceLineage("execution", "plan", "phase2.json", "phase2", null,
            "knowledge.json", "knowledge", "questions.json", "phase3", "objectives.json", "objectives",
            "plan.json", "question-plan", "en-US", "LongVideo", "1.0");
        var longVariant = Variant("Long", lineage);
        var shortVariant = Variant("Short", lineage);
        var aggregate = new DocumentaryBlueprintAggregate("1.0", "1.0", "1.0", "aggregate-orion",
            "execution", "plan", "orion", "en-US", "LongVideo", "1.0", "intent-orion", "intent-checksum",
            lineage, longVariant, shortVariant,
            new(["question.1", "question.2"], [], [], ["orion.fact.belt-distance", "orion.fact.belt-scale", "orion.recognition.belt"]),
            new(longVariant.TotalAllocatedDurationSeconds, shortVariant.TotalAllocatedDurationSeconds,
                longVariant.TotalAllocatedDurationSeconds + shortVariant.TotalAllocatedDurationSeconds), [], string.Empty);
        return aggregate with { DeterministicChecksum = DocumentaryBlueprintProjectionChecksum.CalculateAggregate(aggregate) };
    }

    private static DocumentaryBlueprintVariantArtifact Variant(string variant, DocumentarySourceLineage lineage)
    {
        var source = OrionDocumentaryBlueprintFixture.CreateOrdered();
        var scenes = source.Scenes.Select((scene, index) => new DocumentarySceneBlueprint(scene.SceneId, scene.SceneNumber,
            scene.Title, scene.NarrativeStage, index == 0 ? DocumentarySceneRole.Orientation : DocumentarySceneRole.PracticalObservation,
            scene.ViewerQuestion, scene.SceneObjective, scene.EditorialOutcome, scene.EditorialPriority, scene.KnowledgeReferences,
            scene.VisualOpportunities, scene.Transition, scene.EstimatedDurationSeconds)).ToArray();
        var blueprint = new global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint(
            $"documentary.orion.{variant.ToLowerInvariant()}.v1", source.KnowledgeId, source.SubjectId,
            source.SubjectName, variant == "Long" ? BlueprintPublicationFormat.LongDocumentary : BlueprintPublicationFormat.ShortDocumentary,
            source.PrimaryLanguage, source.Version, source.Metadata, scenes);
        var traces = scenes.Select((scene, index) => new DocumentarySceneBlueprintTraceability(scene.SceneId,
            $"opportunity.{variant.ToLowerInvariant()}.{index + 1}", $"opportunity-checksum-{index + 1}", $"question.{index + 1}", [],
            $"objective.{index + 1}", QuestionEvidenceStatus.ResolvedGrounded, $"slot.{index + 1}", 1, 300, [], [],
            scene.KnowledgeReferences.Select(reference => new DocumentaryKnowledgeSelection($"selection.{variant}.{reference.KnowledgeEntryId}", variant,
                $"opportunity.{variant.ToLowerInvariant()}.{index + 1}", $"question.{index + 1}", reference.KnowledgeEntryId, "knowledge.json",
                reference.KnowledgeEntryId, "knowledge-checksum", reference.Section, "Selected", reference.IsPrimary,
                QuestionEvidenceStatus.ResolvedGrounded)).ToArray())).ToArray();
        var coverage = new DocumentaryCoverageSummary(["question.1", "question.2"], [], [], [], [],
            scenes.SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId).Distinct().ToArray());
        var value = new DocumentaryBlueprintVariantArtifact("1.0", "1.0", "1.0", $"variant-{variant.ToLowerInvariant()}",
            "execution", "plan", "orion", "en-US", "LongVideo", "1.0", variant, "intent-orion", $"intent-{variant.ToLowerInvariant()}",
            "intent-checksum", $"intent-{variant.ToLowerInvariant()}-checksum", lineage, blueprint, traces, coverage, coverage, [], [],
            scenes.Length, scenes.Length, scenes.Sum(x => x.EstimatedDurationSeconds), scenes.Sum(x => x.EstimatedDurationSeconds), string.Empty);
        return value with { DeterministicChecksum = DocumentaryBlueprintProjectionChecksum.CalculateVariant(value) };
    }
}
