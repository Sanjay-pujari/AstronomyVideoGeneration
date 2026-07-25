using System.Text.Json;
using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static partial class OrionDocumentaryNarrativeRevisionCycleFixture
{
    private static DocumentaryNarrativeDraftValidationFinding ScenarioFinding(DocumentaryNarrativeDraft draft, int passageIndex, string rule)
    {
        var pair = draft.Sections.SelectMany(s => s.Passages.Select(p => (Section: s, Passage: p))).ElementAt(passageIndex);
        return new(rule, DocumentaryNarrativeDraftValidationSeverity.Warning, $"deterministic {rule} finding", draft.DraftId,
            pair.Section.SectionId, pair.Section.SectionNumber, pair.Passage.PassageId, pair.Passage.PassageNumber, "Text");
    }

    private static DocumentaryNarrativeRevisionCyclePlan ScenarioPlan(string id, params (int Passage, string Rule)[] specifications)
    {
        var draft = CleanDraft();
        var validation = new DocumentaryNarrativeDraftValidationResult(draft.DraftId,
            specifications.Select(x => ScenarioFinding(draft, x.Passage, x.Rule)).ToArray());
        return new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, validation, $"request.orion.{id}",
            RequestMetadata(draft), ExecutionMetadata(), CycleMetadata());
    }

    private static DocumentaryNarrativeRevisionSubmission ScenarioSubmission(DocumentaryNarrativeRevisionCyclePlan plan,
        string id, Func<DocumentaryNarrativePassageRevisionWorkItem, bool> include,
        Func<DocumentaryNarrativePassageRevisionWorkItem, string> revisedText)
    {
        var passages = plan.WorkPackage.PassageWorkItems.Where(include).Select(work =>
            new DocumentaryNarrativePassageRevisionSubmission(work.WorkItemId, work.PassageId, work.OriginalText,
                revisedText(work), work.RevisionItemIds)).ToArray();
        return new($"submission.orion.{id}", plan.WorkPackage.WorkPackageId, plan.RevisionRequest.RevisionRequestId,
            plan.SourceDraftId, plan.SourceDraftVersion, SubmissionMetadata(), passages);
    }

    private static DocumentaryNarrativeRevisionCycleCompletionRequest ScenarioCompletion(
        DocumentaryNarrativeRevisionCyclePlan plan, DocumentaryNarrativeRevisionSubmission submission) =>
        new(plan, submission, new DocumentaryNarrativeRevisionMetadata(Created.AddMinutes(4), " revision reviewer ",
            plan.SourceDraftId, plan.SourceDraftVersion, "2", "1.0", Correlation), Completed,
            " completion reviewer ", "1.0", Correlation);

    internal static DocumentaryNarrativeRevisionCyclePlan PartialMixedPlan() => ScenarioPlan("partial-mixed",
        (0, "DND-QUALITY-011"), (2, "DND-QUALITY-008"), (1, "DND-QUALITY-014"));
    internal static DocumentaryNarrativeRevisionSubmission PartialMixedSubmission()
    { var p = PartialMixedPlan(); return ScenarioSubmission(p, "partial-mixed", w => w.SequenceNumber == 1, w => w.OriginalText + " Indeed."); }
    internal static DocumentaryNarrativeRevisionCycleCompletionRequest PartialMixedCompletionRequest()
    { var p = PartialMixedPlan(); return ScenarioCompletion(p, ScenarioSubmission(p, "partial-mixed", w => w.SequenceNumber == 1, w => w.OriginalText + " Indeed.")); }
    internal static DocumentaryNarrativeRevisionCycleResult PartialMixedResult() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(PartialMixedCompletionRequest());

    internal static DocumentaryNarrativeRevisionCyclePlan ManualOnlyPlan() => ScenarioPlan("manual-only", (0, "DND-QUALITY-014"), (2, "DND-QUALITY-018"));
    internal static DocumentaryNarrativeRevisionSubmission ManualOnlySubmission()
    { var p = ManualOnlyPlan(); return ScenarioSubmission(p, "manual-only", _ => false, w => w.OriginalText); }
    internal static DocumentaryNarrativeRevisionCycleCompletionRequest ManualOnlyCompletionRequest()
    { var p = ManualOnlyPlan(); return ScenarioCompletion(p, ScenarioSubmission(p, "manual-only", _ => false, w => w.OriginalText)); }
    internal static DocumentaryNarrativeRevisionCycleResult ManualOnlyResult() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(ManualOnlyCompletionRequest());

    internal static DocumentaryNarrativeRevisionCyclePlan CompleteStillInvalidPlan() => ScenarioPlan("remaining", (0, "DND-QUALITY-011"));
    internal static DocumentaryNarrativeRevisionSubmission CompleteStillInvalidSubmission()
    { var p = CompleteStillInvalidPlan(); return ScenarioSubmission(p, "remaining", _ => true, _ => "Only three words."); }
    internal static DocumentaryNarrativeRevisionCycleCompletionRequest CompleteStillInvalidCompletionRequest()
    { var p = CompleteStillInvalidPlan(); return ScenarioCompletion(p, ScenarioSubmission(p, "remaining", _ => true, _ => "Only three words.")); }
    internal static DocumentaryNarrativeRevisionCycleResult CompletedWithRemainingFindingsResult() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(CompleteStillInvalidCompletionRequest());

    internal static DocumentaryNarrativeRevisionCyclePlan CompleteSuccessfulPlan() => ScenarioPlan("successful", (0, "DND-QUALITY-011"), (2, "DND-QUALITY-008"));
    internal static DocumentaryNarrativeRevisionSubmission CompleteSuccessfulSubmission()
    { var p = CompleteSuccessfulPlan(); return ScenarioSubmission(p, "successful", _ => true, w => w.OriginalText + " Indeed."); }
    internal static DocumentaryNarrativeRevisionCycleCompletionRequest CompleteSuccessfulCompletionRequest()
    { var p = CompleteSuccessfulPlan(); return ScenarioCompletion(p, ScenarioSubmission(p, "successful", _ => true, w => w.OriginalText + " Indeed.")); }
    internal static DocumentaryNarrativeRevisionCycleResult CompletedSuccessfullyResult() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(CompleteSuccessfulCompletionRequest());
}

public sealed class DocumentaryNarrativeRevisionCycleTargetedScenarioTests
{
    [Fact]
    public void Partial_mixed_completion_applies_only_submitted_passage_and_cleanliness_does_not_override_unresolved_work()
    {
        var request = OrionDocumentaryNarrativeRevisionCycleFixture.PartialMixedCompletionRequest();
        var result = new DocumentaryNarrativeRevisionCycleCompleter().Complete(request);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision, request.Plan.Status);
        Assert.True(request.Plan.RequiresExternalRevision);
        Assert.Equal(2, request.Plan.PassageWorkItemCount); Assert.True(request.Plan.ManualReviewWorkItemCount > 0);
        Assert.Equal(DocumentaryNarrativeRevisionStatus.PartiallyRevised, result.RevisionResult.Status);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.PartiallyCompleted, result.Status);
        Assert.Equal(1, result.AppliedChangeCount); Assert.True(result.UnresolvedRevisionItemCount > 0);
        Assert.Equal(request.Plan.RevisionRequest.Items.Where(i => !result.RevisionResult.Changes.SelectMany(c => c.RevisionItemIds).Contains(i.RevisionItemId)), result.RevisionResult.UnresolvedItems);
        Assert.Contains(result.RevisionResult.UnresolvedItems, i => i.RequiresPassageText);
        Assert.Contains(result.RevisionResult.UnresolvedItems, i => !i.RequiresPassageText);
        Assert.True(result.ValidationComparison.IsClean); Assert.Empty(result.RevisedValidationResult.Findings);
        AssertPassages(request, result, 1);
    }

    [Fact]
    public void Manual_only_completion_preserves_text_and_has_no_synthetic_inputs_or_changes()
    {
        var request = OrionDocumentaryNarrativeRevisionCycleFixture.ManualOnlyCompletionRequest();
        var result = new DocumentaryNarrativeRevisionCycleCompleter().Complete(request);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision, request.Plan.Status);
        Assert.True(request.Plan.RequiresExternalRevision); Assert.Equal(0, request.Plan.PassageWorkItemCount);
        Assert.True(request.Plan.ManualReviewWorkItemCount > 0); Assert.Empty(request.Submission.PassageSubmissions);
        Assert.Empty(result.BindingRequest.PassageRevisionInputs); Assert.Empty(result.RevisionResult.Changes);
        Assert.Equal(DocumentaryNarrativeRevisionStatus.PartiallyRevised, result.RevisionResult.Status);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.PartiallyCompleted, result.Status);
        Assert.Equal(request.Plan.RevisionRequest.Items, result.RevisionResult.UnresolvedItems);
        Assert.Equal(request.Plan.ManualReviewWorkItemCount, result.UnresolvedRevisionItemCount);
        Assert.Empty(result.RevisedValidationResult.Findings); AssertPassages(request, result, 0);
    }

    [Fact]
    public void Complete_submission_with_remaining_findings_has_exact_deterministic_comparison()
    {
        var result = OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult();
        Assert.Equal(DocumentaryNarrativeRevisionStatus.Revised, result.RevisionResult.Status); Assert.Equal(0, result.UnresolvedRevisionItemCount);
        Assert.NotEmpty(result.RevisedValidationResult.Findings);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings, result.Status);
        var c = result.ValidationComparison;
        Assert.Equal(result.SourceFindingCount, c.SourceFindingCount); Assert.Equal(result.RevisedFindingCount, c.RevisedFindingCount);
        Assert.Equal(["DND-QUALITY-011"], c.SourceRuleCodes); Assert.Equal(["DND-QUALITY-009"], c.RevisedRuleCodes);
        Assert.Equal(["DND-QUALITY-011"], c.ResolvedRuleCodes); Assert.Empty(c.RemainingRuleCodes); Assert.Equal(["DND-QUALITY-009"], c.IntroducedRuleCodes);
        Assert.Equal((1, 1, 1, 0, 1), (c.SourceFindingCount, c.RevisedFindingCount, c.ResolvedFindingCount, c.RemainingFindingCount, c.IntroducedFindingCount));
        Assert.False(c.HasImproved); Assert.True(c.HasRegressed); Assert.False(c.IsClean);
    }

    [Fact]
    public void Successful_completion_is_clean_complete_and_preserves_exact_lineage()
    {
        var request = OrionDocumentaryNarrativeRevisionCycleFixture.CompleteSuccessfulCompletionRequest();
        var result = new DocumentaryNarrativeRevisionCycleCompleter().Complete(request);
        Assert.Equal(DocumentaryNarrativeRevisionStatus.Revised, result.RevisionResult.Status); Assert.Empty(result.RevisionResult.UnresolvedItems);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully, result.Status); Assert.Empty(result.RevisedValidationResult.Findings);
        Assert.True(result.ValidationComparison.IsClean); Assert.True(result.ValidationComparison.HasImproved); Assert.False(result.ValidationComparison.HasRegressed);
        Assert.Equal(request.Plan.SourceDraftId + ".revision.2", result.TargetDraftId); Assert.Equal("2", result.TargetDraftVersion);
        Assert.Equal(request.Plan.SourceDraftId, result.SourceDraftId); Assert.Equal(request.Plan.SourceDraftVersion, result.SourceDraftVersion);
        AssertPassages(request, result, request.Plan.PassageWorkItemCount);
    }

    [Fact]
    public void Four_status_precedence_rules_are_exact_and_completion_never_awaits_external_revision()
    {
        var results = new[] { OrionDocumentaryNarrativeRevisionCycleFixture.CleanResult(), OrionDocumentaryNarrativeRevisionCycleFixture.PartialMixedResult(),
            OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult(), OrionDocumentaryNarrativeRevisionCycleFixture.CompletedSuccessfullyResult() };
        Assert.Equal([DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, DocumentaryNarrativeRevisionCycleStatus.PartiallyCompleted,
            DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings, DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully], results.Select(x => x.Status));
        Assert.DoesNotContain(results, x => x.Status == DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision);
    }

    private static void AssertPassages(DocumentaryNarrativeRevisionCycleCompletionRequest request, DocumentaryNarrativeRevisionCycleResult result, int changed)
    {
        var source = request.Plan.SourceDraft.Sections.SelectMany(s => s.Passages).ToDictionary(p => p.PassageId);
        var target = result.RevisionResult.RevisedDraft.Sections.SelectMany(s => s.Passages).ToDictionary(p => p.PassageId);
        Assert.Equal(changed, source.Keys.Count(id => source[id].Text != target[id].Text));
        Assert.All(result.RevisionResult.Changes, c => Assert.Single(result.RevisionResult.Changes.Where(x => x.PassageId == c.PassageId)));
    }
}

public sealed class DocumentaryNarrativeRevisionCycleTargetedQualityTests
{
    public static IEnumerable<object[]> ScenarioRequests()
    {
        yield return [new Func<DocumentaryNarrativeRevisionCycleCompletionRequest>(OrionDocumentaryNarrativeRevisionCycleFixture.PartialMixedCompletionRequest)];
        yield return [new Func<DocumentaryNarrativeRevisionCycleCompletionRequest>(OrionDocumentaryNarrativeRevisionCycleFixture.ManualOnlyCompletionRequest)];
        yield return [new Func<DocumentaryNarrativeRevisionCycleCompletionRequest>(OrionDocumentaryNarrativeRevisionCycleFixture.CompleteStillInvalidCompletionRequest)];
        yield return [new Func<DocumentaryNarrativeRevisionCycleCompletionRequest>(OrionDocumentaryNarrativeRevisionCycleFixture.CompleteSuccessfulCompletionRequest)];
    }

    [Theory, MemberData(nameof(ScenarioRequests))]
    public void Completer_does_not_mutate_any_nested_input(Func<DocumentaryNarrativeRevisionCycleCompletionRequest> create)
    {
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions(); var request = create();
        object[] inputs = [request, request.Plan, request.Plan.SourceDraft, request.Plan.SourceValidationResult, request.Plan.RevisionRequest,
            request.Plan.WorkPackage, request.Submission, request.Submission.PassageSubmissions,
            request.Submission.PassageSubmissions.SelectMany(x => x.ResolvedRevisionItemIds).ToArray(), request.RevisionMetadata];
        var before = inputs.Select(x => JsonSerializer.Serialize(x, x.GetType(), options)).ToArray();
        _ = new DocumentaryNarrativeRevisionCycleCompleter().Complete(request);
        Assert.Equal(before, inputs.Select(x => JsonSerializer.Serialize(x, x.GetType(), options)));
    }

    [Theory, MemberData(nameof(ScenarioRequests))]
    public void Independently_reconstructed_scenarios_are_byte_deterministic(Func<DocumentaryNarrativeRevisionCycleCompletionRequest> create)
    {
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        var first = new DocumentaryNarrativeRevisionCycleCompleter().Complete(create());
        var second = new DocumentaryNarrativeRevisionCycleCompleter().Complete(create());
        Assert.Equal(JsonSerializer.Serialize(first.BindingRequest, options), JsonSerializer.Serialize(second.BindingRequest, options));
        Assert.Equal(JsonSerializer.Serialize(first.RevisionResult, options), JsonSerializer.Serialize(second.RevisionResult, options));
        Assert.Equal(JsonSerializer.Serialize(first.RevisedValidationResult, options), JsonSerializer.Serialize(second.RevisedValidationResult, options));
        Assert.Equal(JsonSerializer.Serialize(first.ValidationComparison, options), JsonSerializer.Serialize(second.ValidationComparison, options));
        Assert.Equal(JsonSerializer.Serialize(first, options), JsonSerializer.Serialize(second, options));
        Assert.Equal(first.Status, second.Status); Assert.Equal(first.TargetDraftId, second.TargetDraftId); Assert.Equal(first.TargetDraftVersion, second.TargetDraftVersion);
        Assert.Equal(first.RevisionResult.Changes.Select(x => x.PassageId), second.RevisionResult.Changes.Select(x => x.PassageId));
        Assert.Equal(first.RevisionResult.UnresolvedItems.Select(x => x.RevisionItemId), second.RevisionResult.UnresolvedItems.Select(x => x.RevisionItemId));
        Assert.Equal(first.RevisedValidationResult.Findings.Select(x => x.RuleCode), second.RevisedValidationResult.Findings.Select(x => x.RuleCode));
        Assert.Equal(first.ValidationComparison.RevisedRuleCodes, second.ValidationComparison.RevisedRuleCodes);
    }

    [Theory, MemberData(nameof(ScenarioRequests))]
    public void Final_results_round_trip_with_byte_identical_web_json(Func<DocumentaryNarrativeRevisionCycleCompletionRequest> create)
    {
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        var result = new DocumentaryNarrativeRevisionCycleCompleter().Complete(create()); var json = JsonSerializer.Serialize(result, options);
        var copy = JsonSerializer.Deserialize<DocumentaryNarrativeRevisionCycleResult>(json, options)!;
        Assert.Equal(json, JsonSerializer.Serialize(copy, options)); Assert.Equal(result.Status, copy.Status); Assert.Equal(result.CycleId, copy.CycleId);
        Assert.Equal(result.SourceDraftId, copy.SourceDraftId); Assert.Equal(result.TargetDraftId, copy.TargetDraftId);
        Assert.Equal(result.CorrelationId, copy.CorrelationId); Assert.Equal(result.CompletedUtc, copy.CompletedUtc);
    }
}

public sealed class DocumentaryNarrativeRevisionCycleCompleterTargetedBoundaryTests
{
    [Theory]
    [InlineData("work")][InlineData("request")][InlineData("draft")][InlineData("version")][InlineData("submission-correlation")]
    [InlineData("metadata-draft")][InlineData("metadata-version")][InlineData("metadata-correlation")][InlineData("completion-correlation")]
    [InlineData("case-lineage")][InlineData("case-correlation")]
    public void Complete_rejects_each_isolated_structural_mismatch(string mismatch)
    {
        var valid = OrionDocumentaryNarrativeRevisionCycleFixture.CompleteSuccessfulCompletionRequest(); var s = valid.Submission;
        var sm = mismatch is "submission-correlation" or "case-correlation"
            ? new DocumentaryNarrativeRevisionSubmissionMetadata(s.Metadata.CreatedUtc, s.Metadata.CreatedBy, "1.0", s.Metadata.EditorType, s.Metadata.EditorName,
                mismatch == "case-correlation" ? s.Metadata.CorrelationId.ToUpperInvariant() : "different") : s.Metadata;
        var submission = new DocumentaryNarrativeRevisionSubmission(s.SubmissionId,
            mismatch == "work" ? "different" : s.WorkPackageId, mismatch == "request" ? "different" : s.RevisionRequestId,
            mismatch is "draft" ? "different" : mismatch == "case-lineage" ? s.DraftId.ToUpperInvariant() : s.DraftId,
            mismatch == "version" ? "different" : s.DraftVersion, sm, s.PassageSubmissions);
        var m = valid.RevisionMetadata;
        var metadata = new DocumentaryNarrativeRevisionMetadata(m.CreatedUtc, m.CreatedBy,
            mismatch == "metadata-draft" ? "different" : m.SourceDraftId, mismatch == "metadata-version" ? "different" : m.SourceDraftVersion,
            m.TargetDraftVersion, "1.0", mismatch == "metadata-correlation" ? "different" : m.CorrelationId);
        var request = new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, submission, metadata, valid.CompletedUtc,
            valid.CompletedBy, "1.0", mismatch == "completion-correlation" ? "different" : valid.CorrelationId);
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(request));
    }
}

public sealed class DocumentaryNarrativeRevisionCycleResultTargetedLineageTests
{
    private static DocumentaryNarrativeRevisionCycleResult Valid() => OrionDocumentaryNarrativeRevisionCycleFixture.CompletedSuccessfullyResult();
    private static void Reject(DocumentaryNarrativeRevisionCycleResult v, DocumentaryNarrativeRevisionSubmission? submission = null,
        DocumentaryNarrativeRevisionBindingRequest? binding = null, DocumentaryNarrativeRevisionResult? revision = null,
        DocumentaryNarrativeDraftValidationResult? validation = null, DocumentaryNarrativeRevisionValidationComparison? comparison = null,
        string? correlation = null, DocumentaryNarrativeRevisionCycleStatus? status = null) =>
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleResult(v.Plan, submission ?? v.Submission, binding ?? v.BindingRequest,
            revision ?? v.RevisionResult, validation ?? v.RevisedValidationResult, comparison ?? v.ValidationComparison, v.CompletedUtc,
            v.CompletedBy, v.CompletionSchemaVersion, correlation ?? v.CorrelationId, status ?? v.Status));

    [Theory] [InlineData("work")][InlineData("request")][InlineData("draft")][InlineData("version")][InlineData("correlation")]
    public void Rejects_submission_lineage_and_correlation_mismatches(string kind)
    {
        var v = Valid(); var s = v.Submission;
        var metadata = kind == "correlation" ? new DocumentaryNarrativeRevisionSubmissionMetadata(s.Metadata.CreatedUtc, s.Metadata.CreatedBy, "1.0", s.Metadata.EditorType, s.Metadata.EditorName, "different") : s.Metadata;
        Reject(v, submission: new DocumentaryNarrativeRevisionSubmission(s.SubmissionId, kind == "work" ? "different" : s.WorkPackageId,
            kind == "request" ? "different" : s.RevisionRequestId, kind == "draft" ? "different" : s.DraftId,
            kind == "version" ? "different" : s.DraftVersion, metadata, s.PassageSubmissions));
    }

    [Theory] [InlineData("request")][InlineData("source")][InlineData("version")]
    public void Rejects_revision_result_lineage(string kind)
    {
        var v = Valid(); var r = v.RevisionResult;
        var bad = new DocumentaryNarrativeRevisionResult(kind == "request" ? "different" : r.RevisionRequestId,
            kind == "source" ? "different" : r.SourceDraftId, kind == "version" ? "different" : r.SourceDraftVersion,
            r.TargetDraftId, r.TargetDraftVersion, r.Status, r.RevisedDraft, r.Changes, r.UnresolvedItems);
        Reject(v, revision: bad);
    }

    [Fact] public void Rejects_revised_validation_target_mismatch() { var v = Valid(); Reject(v, validation: new("different", [])); }

    [Fact]
    public void Rejects_binding_source_and_request_lineage()
    {
        var v = Valid(); var alternate = OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();
        Reject(v, binding: alternate);
        var clean = OrionDocumentaryNarrativeRevisionFixture.NoChangeBindingRequest(); Reject(v, binding: clean);
    }

    [Fact]
    public void Rejects_comparison_source_and_revised_count_inconsistency()
    {
        var v = Valid();
        var zero = new DocumentaryNarrativeRevisionValidationComparison(0, 0, 0, 0, 0, [], [], [], [], [], false, false, true);
        Reject(v, comparison: zero);
        var one = new DocumentaryNarrativeRevisionValidationComparison(v.SourceFindingCount, 1, v.SourceFindingCount, 0, 1,
            v.Plan.SourceValidationResult.Findings.Select(x => x.RuleCode).ToArray(), ["X"], v.Plan.SourceValidationResult.Findings.Select(x => x.RuleCode).ToArray(), [], ["X"], false, true, false);
        Reject(v, comparison: one);
    }

    [Fact] public void Rejects_cycle_correlation_mismatch() { var v = Valid(); Reject(v, correlation: "different"); }

    [Theory]
    [InlineData("plan")][InlineData("request")][InlineData("package")][InlineData("binding")]
    public void Rejects_every_embedded_correlation_link_independently(string artifact)
    {
        var v = Valid();
        object metadata = artifact switch
        {
            "plan" => v.Plan.Metadata,
            "request" => v.Plan.RevisionRequest.Metadata,
            "package" => v.Plan.WorkPackage.Metadata,
            _ => v.BindingRequest.Metadata
        };
        var field = metadata.GetType().GetField("<CorrelationId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(metadata, "different");
        Reject(v);
    }

    [Theory]
    [InlineData(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired)]
    [InlineData(DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision)]
    [InlineData(DocumentaryNarrativeRevisionCycleStatus.PartiallyCompleted)]
    [InlineData(DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings)]
    public void Rejects_every_incorrect_status_for_successful_result(DocumentaryNarrativeRevisionCycleStatus status) { var v = Valid(); Reject(v, status: status); }

    [Fact]
    public void Every_valid_scenario_rejects_all_of_its_incorrect_statuses()
    {
        DocumentaryNarrativeRevisionCycleResult[] values = [OrionDocumentaryNarrativeRevisionCycleFixture.CleanResult(),
            OrionDocumentaryNarrativeRevisionCycleFixture.PartialMixedResult(), OrionDocumentaryNarrativeRevisionCycleFixture.ManualOnlyResult(),
            OrionDocumentaryNarrativeRevisionCycleFixture.CompletedWithRemainingFindingsResult(), Valid()];
        foreach (var value in values)
            foreach (var status in Enum.GetValues<DocumentaryNarrativeRevisionCycleStatus>().Where(s => s != value.Status)) Reject(value, status: status);
    }
}
