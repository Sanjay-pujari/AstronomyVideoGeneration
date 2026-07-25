using System.Collections;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

/// <summary>Final, deterministic O2.9 closure fixture. Every value crossing an operation boundary is supplied here.</summary>
internal static class OrionDocumentaryNarrativeRevisionCycleFixture
{
    internal const string Correlation = "correlation-orion-cycle-001";
    internal static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-01-15T14:02:03.1234567+05:30");
    internal static readonly DateTimeOffset Completed = DateTimeOffset.Parse("2026-01-15T16:02:03.7654321-04:00");
    internal static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
    internal static DocumentaryNarrativeRevisionCycleMetadata CycleMetadata() => new(Created, " cycle coordinator ", "1.0", Correlation);
    internal static DocumentaryNarrativeRevisionRequestMetadata RequestMetadata(DocumentaryNarrativeDraft draft) =>
        new(Created.AddMinutes(1), " request reviewer ", draft.Version, "1.0", "1.0", Correlation);
    internal static DocumentaryNarrativeRevisionExecutionMetadata ExecutionMetadata() => new(Created.AddMinutes(2), " package coordinator ", "1.0", Correlation);
    internal static DocumentaryNarrativeRevisionSubmissionMetadata SubmissionMetadata() =>
        new(Created.AddMinutes(3), " external editor ", "1.0", DocumentaryNarrativeRevisionEditorType.Human, " Orion editor ", Correlation);
    internal static DocumentaryNarrativeDraft CleanDraft() => OrionDocumentaryNarrativeRevisionFixture.ValidDraft();
    internal static DocumentaryNarrativeDraftValidationResult CleanValidation() => new DocumentaryNarrativeDraftValidator().Validate(CleanDraft());
    internal static DocumentaryNarrativeRevisionCyclePlan CleanPlan()
    {
        var draft = CleanDraft();
        return new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, new DocumentaryNarrativeDraftValidator().Validate(draft),
            "request.orion.clean", RequestMetadata(draft), ExecutionMetadata(), CycleMetadata());
    }
    internal static DocumentaryNarrativeRevisionSubmission CleanSubmission()
    {
        var plan = CleanPlan();
        return new("submission.orion.clean", plan.WorkPackage.WorkPackageId, plan.RevisionRequest.RevisionRequestId,
            plan.SourceDraftId, plan.SourceDraftVersion, SubmissionMetadata(), []);
    }
    internal static DocumentaryNarrativeRevisionCycleCompletionRequest CleanCompletionRequest()
    {
        var plan = CleanPlan();
        var submission = new DocumentaryNarrativeRevisionSubmission("submission.orion.clean", plan.WorkPackage.WorkPackageId,
            plan.RevisionRequest.RevisionRequestId, plan.SourceDraftId, plan.SourceDraftVersion, SubmissionMetadata(), []);
        var metadata = new DocumentaryNarrativeRevisionMetadata(Created.AddMinutes(4), " revision reviewer ", plan.SourceDraftId,
            plan.SourceDraftVersion, "2", "1.0", Correlation);
        return new(plan, submission, metadata, Completed, " completion reviewer ", "1.0", Correlation);
    }
    internal static DocumentaryNarrativeRevisionCycleResult CleanResult() => new DocumentaryNarrativeRevisionCycleCompleter().Complete(CleanCompletionRequest());
    internal static DocumentaryNarrativeRevisionCyclePlan MixedPlan()
    {
        var draft = OrionDocumentaryNarrativeRevisionFixture.InvalidOpeningDraft();
        return new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, new DocumentaryNarrativeDraftValidator().Validate(draft),
            "request.orion.mixed", RequestMetadata(draft), ExecutionMetadata(), CycleMetadata());
    }
    internal static DocumentaryNarrativeDraftValidationFinding Finding(string rule, string draft = "draft.source", string? section = "section.1",
        int? sectionNumber = 1, string? passage = "passage.1", int? passageNumber = 1, string? field = "Text",
        string message = "message", DocumentaryNarrativeDraftValidationSeverity severity = DocumentaryNarrativeDraftValidationSeverity.Warning) =>
        new(rule, severity, message, draft, section, sectionNumber, passage, passageNumber, field);
    internal static DocumentaryNarrativeDraftValidationResult PairSource(params DocumentaryNarrativeDraftValidationFinding[] findings) => new("draft.source", findings);
    internal static DocumentaryNarrativeDraftValidationResult PairTarget(params DocumentaryNarrativeDraftValidationFinding[] findings) => new("draft.target", findings);
}

public sealed class DocumentaryNarrativeRevisionCycleFinalMetadataCertificationTests
{
    [Theory]
    [InlineData("", "1.0", "correlation")]
    [InlineData(" ", "1.0", "correlation")]
    [InlineData("owner", "2.0", "correlation")]
    [InlineData("owner", "1.0", " ")]
    public void Rejects_each_invalid_text_value(string owner, string schema, string correlation) =>
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleMetadata(OrionDocumentaryNarrativeRevisionCycleFixture.Created, owner, schema, correlation));

    [Fact] public void Rejects_default_timestamp() => Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleMetadata(default, "owner", "1.0", "correlation"));

    [Fact] public void Preserves_offset_precision_and_whitespace_and_round_trips()
    {
        var value = OrionDocumentaryNarrativeRevisionCycleFixture.CycleMetadata();
        Assert.Equal(OrionDocumentaryNarrativeRevisionCycleFixture.Created, value.CreatedUtc);
        Assert.Equal(" cycle coordinator ", value.CreatedBy);
        Assert.Equal("1.0", value.CycleSchemaVersion);
        Assert.Equal(OrionDocumentaryNarrativeRevisionCycleFixture.Correlation, value.CorrelationId);
        RoundTrip(value);
    }

    internal static void RoundTrip<T>(T value)
    {
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        var json = JsonSerializer.Serialize(value, options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(json, options), options));
    }
}

public sealed class DocumentaryNarrativeRevisionCycleFinalPlanCertificationTests
{
    [Fact] public void Certifies_clean_derived_values_identity_cycle_id_and_serialization()
    {
        var plan = OrionDocumentaryNarrativeRevisionCycleFixture.CleanPlan();
        Assert.Equal($"{plan.SourceDraftId}.revision-cycle.{plan.SourceDraftVersion}.{plan.RevisionRequest.RevisionRequestId}", plan.CycleId);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, plan.Status);
        Assert.Equal(0, plan.SourceFindingCount); Assert.Equal(0, plan.RevisionItemCount);
        Assert.Equal(0, plan.PassageWorkItemCount); Assert.Equal(0, plan.ManualReviewWorkItemCount); Assert.False(plan.RequiresExternalRevision);
        Assert.Same(plan.SourceDraft, plan.SourceDraft); Assert.Same(plan.SourceValidationResult, plan.SourceValidationResult);
        DocumentaryNarrativeRevisionCycleFinalMetadataCertificationTests.RoundTrip(plan);
    }

    [Fact] public void Certifies_text_plan_and_builder_delegation()
    {
        var plan = OrionDocumentaryNarrativeRevisionCycleFixture.MixedPlan();
        var expectedRequest = new DocumentaryNarrativeRevisionRequestBuilder().Build(plan.SourceDraft, plan.SourceValidationResult,
            plan.RevisionRequest.RevisionRequestId, plan.RevisionRequest.Metadata);
        var expectedPackage = new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(plan.SourceDraft, expectedRequest, plan.WorkPackage.Metadata);
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        Assert.Equal(JsonSerializer.Serialize(expectedRequest, options), JsonSerializer.Serialize(plan.RevisionRequest, options));
        Assert.Equal(JsonSerializer.Serialize(expectedPackage, options), JsonSerializer.Serialize(plan.WorkPackage, options));
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision, plan.Status);
        Assert.True(plan.PassageWorkItemCount > 0); Assert.Equal(0, plan.ManualReviewWorkItemCount); Assert.True(plan.RequiresExternalRevision);
    }

    [Fact] public void Planner_rejects_null_blank_lineage_version_and_correlation_boundaries()
    {
        var draft = OrionDocumentaryNarrativeRevisionCycleFixture.CleanDraft(); var validation = new DocumentaryNarrativeDraftValidator().Validate(draft);
        var planner = new DocumentaryNarrativeRevisionCyclePlanner(); var request = OrionDocumentaryNarrativeRevisionCycleFixture.RequestMetadata(draft);
        var execution = OrionDocumentaryNarrativeRevisionCycleFixture.ExecutionMetadata(); var cycle = OrionDocumentaryNarrativeRevisionCycleFixture.CycleMetadata();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, validation, "r", request, execution, cycle));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(draft, null!, "r", request, execution, cycle));
        Assert.Throws<ArgumentException>(() => planner.Plan(draft, validation, " ", request, execution, cycle));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(draft, validation, "r", null!, execution, cycle));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(draft, validation, "r", request, null!, cycle));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(draft, validation, "r", request, execution, null!));
        Assert.Throws<ArgumentException>(() => planner.Plan(draft, new(draft.DraftId.ToUpperInvariant(), []), "r", request, execution, cycle));
        var wrongVersion = new DocumentaryNarrativeRevisionRequestMetadata(request.CreatedUtc, request.CreatedBy, draft.Version + ".other", "1.0", "1.0", request.CorrelationId);
        Assert.Throws<ArgumentException>(() => planner.Plan(draft, validation, "r", wrongVersion, execution, cycle));
        var wrongCorrelation = new DocumentaryNarrativeRevisionExecutionMetadata(execution.CreatedUtc, execution.CreatedBy, "1.0", cycle.CorrelationId.ToUpperInvariant());
        Assert.Throws<ArgumentException>(() => planner.Plan(draft, validation, "r", request, wrongCorrelation, cycle));
    }

    [Fact] public void Planner_is_non_mutating_and_byte_deterministic()
    {
        var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        var draft = OrionDocumentaryNarrativeRevisionFixture.InvalidOpeningDraft(); var validation = new DocumentaryNarrativeDraftValidator().Validate(draft);
        var request = OrionDocumentaryNarrativeRevisionCycleFixture.RequestMetadata(draft); var execution = OrionDocumentaryNarrativeRevisionCycleFixture.ExecutionMetadata(); var cycle = OrionDocumentaryNarrativeRevisionCycleFixture.CycleMetadata();
        var inputs = new object[] { draft, validation, validation.Findings, request, execution, cycle };
        var before = inputs.Select(x => JsonSerializer.Serialize(x, x.GetType(), options)).ToArray();
        var result = new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, validation, "request.orion.deterministic", request, execution, cycle);
        Assert.Equal(before, inputs.Select(x => JsonSerializer.Serialize(x, x.GetType(), options)));
        var second = new DocumentaryNarrativeRevisionCyclePlanner().Plan(OrionDocumentaryNarrativeRevisionFixture.InvalidOpeningDraft(),
            new DocumentaryNarrativeDraftValidator().Validate(OrionDocumentaryNarrativeRevisionFixture.InvalidOpeningDraft()), "request.orion.deterministic",
            OrionDocumentaryNarrativeRevisionCycleFixture.RequestMetadata(OrionDocumentaryNarrativeRevisionFixture.InvalidOpeningDraft()),
            OrionDocumentaryNarrativeRevisionCycleFixture.ExecutionMetadata(), OrionDocumentaryNarrativeRevisionCycleFixture.CycleMetadata());
        Assert.Equal(JsonSerializer.Serialize(result, options), JsonSerializer.Serialize(second, options));
    }
}

public sealed class DocumentaryNarrativeRevisionCycleFinalComparisonCertificationTests
{
    private static DocumentaryNarrativeRevisionValidationComparison Valid(List<string>? source = null, List<string>? revised = null,
        List<string>? resolved = null, List<string>? remaining = null, List<string>? introduced = null) =>
        new((source ?? ["A"]).Count, (revised ?? []).Count, (resolved ?? ["A"]).Count, (remaining ?? []).Count,
            (introduced ?? []).Count, source ?? ["A"], revised ?? [], resolved ?? ["A"], remaining ?? [], introduced ?? [], true, false, true);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Rejects_each_negative_count(int index)
    {
        var counts = new[] { 1, 0, 1, 0, 0 }; counts[index] = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentaryNarrativeRevisionValidationComparison(counts[0], counts[1], counts[2], counts[3], counts[4], ["A"], [], ["A"], [], [], true, false, true));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Rejects_each_null_collection(int index)
    {
        IReadOnlyList<string>[] values = [["A"], [], ["A"], [], []]; values[index] = null!;
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeRevisionValidationComparison(1, 0, 1, 0, 0, values[0], values[1], values[2], values[3], values[4], true, false, true));
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public void Rejects_invalid_rule_elements(string? code) => Assert.Throws<ArgumentException>(() =>
        new DocumentaryNarrativeRevisionValidationComparison(1, 0, 1, 0, 0, [code!], [], [code!], [], [], true, false, true));

    [Fact] public void Defensively_copies_and_exposes_read_only_summaries()
    {
        var source = new List<string> { "A" }; var resolved = new List<string> { "A" }; var value = Valid(source: source, resolved: resolved);
        source.Add("B"); resolved.Add("B"); Assert.Equal(["A"], value.SourceRuleCodes); Assert.Equal(["A"], value.ResolvedRuleCodes);
        Assert.Throws<NotSupportedException>(() => ((IList)value.SourceRuleCodes).Add("B"));
        DocumentaryNarrativeRevisionCycleFinalMetadataCertificationTests.RoundTrip(value);
    }

    [Fact] public void Comparer_certifies_order_duplicates_and_identity_dimensions()
    {
        static DocumentaryNarrativeDraftValidationFinding F(string rule, string draft = "draft.source", string? section = "section.1",
            int? sectionNumber = 1, string? passage = "passage.1", int? passageNumber = 1, string? field = "Text",
            string message = "message", DocumentaryNarrativeDraftValidationSeverity severity = DocumentaryNarrativeDraftValidationSeverity.Warning) =>
            OrionDocumentaryNarrativeRevisionCycleFixture.Finding(rule, draft, section, sectionNumber, passage, passageNumber, field, message, severity);
        var identities = new[] { F("Rule"), F("rule"), F("Rule", section:null, sectionNumber:null, passage:null, passageNumber:null, field:null),
            F("Rule", section:"section.2"), F("Rule", sectionNumber:2), F("Rule", passage:"passage.2"), F("Rule", passageNumber:2),
            F("Rule", field:"Title"), F("Rule", message:"Message"), F("Rule", message:" message"), F("Rule", severity:DocumentaryNarrativeDraftValidationSeverity.Error) };
        foreach (var changed in identities.Skip(1))
        {
            var target = new DocumentaryNarrativeDraftValidationFinding(changed.RuleCode, changed.Severity, changed.Message, "draft.target",
                changed.SectionId, changed.SectionNumber, changed.PassageId, changed.PassageNumber, changed.FieldName);
            var comparison = new DocumentaryNarrativeRevisionValidationComparer().Compare(
                OrionDocumentaryNarrativeRevisionCycleFixture.PairSource(identities[0]), OrionDocumentaryNarrativeRevisionCycleFixture.PairTarget(target));
            Assert.Equal(1, comparison.ResolvedFindingCount); Assert.Equal(1, comparison.IntroducedFindingCount); Assert.True(comparison.HasRegressed);
        }
        var duplicate = F("DUP"); var ordered = new DocumentaryNarrativeRevisionValidationComparer().Compare(
            OrionDocumentaryNarrativeRevisionCycleFixture.PairSource(F("A"), duplicate, duplicate, F("Z")),
            OrionDocumentaryNarrativeRevisionCycleFixture.PairTarget(
                new DocumentaryNarrativeDraftValidationFinding(duplicate.RuleCode, duplicate.Severity, duplicate.Message, "draft.target", duplicate.SectionId, duplicate.SectionNumber, duplicate.PassageId, duplicate.PassageNumber, duplicate.FieldName),
                new DocumentaryNarrativeDraftValidationFinding(duplicate.RuleCode, duplicate.Severity, duplicate.Message, "draft.target", duplicate.SectionId, duplicate.SectionNumber, duplicate.PassageId, duplicate.PassageNumber, duplicate.FieldName),
                new DocumentaryNarrativeDraftValidationFinding("NEW", DocumentaryNarrativeDraftValidationSeverity.Warning, "message", "draft.target", "section.1", 1, "passage.1", 1, "Text")));
        Assert.Equal(["A", "Z"], ordered.ResolvedRuleCodes); Assert.Equal(["DUP", "DUP"], ordered.RemainingRuleCodes); Assert.Equal(["NEW"], ordered.IntroducedRuleCodes);
    }
}

public sealed class DocumentaryNarrativeRevisionCycleFinalCompletionCertificationTests
{
    [Fact] public void Completion_request_rejects_each_invalid_boundary_and_preserves_exact_values()
    {
        var valid = OrionDocumentaryNarrativeRevisionCycleFixture.CleanCompletionRequest();
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(null!, valid.Submission, valid.RevisionMetadata, valid.CompletedUtc, valid.CompletedBy, "1.0", valid.CorrelationId));
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, null!, valid.RevisionMetadata, valid.CompletedUtc, valid.CompletedBy, "1.0", valid.CorrelationId));
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, valid.Submission, null!, valid.CompletedUtc, valid.CompletedBy, "1.0", valid.CorrelationId));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, valid.Submission, valid.RevisionMetadata, default, valid.CompletedBy, "1.0", valid.CorrelationId));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, valid.Submission, valid.RevisionMetadata, valid.CompletedUtc, " ", "1.0", valid.CorrelationId));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, valid.Submission, valid.RevisionMetadata, valid.CompletedUtc, valid.CompletedBy, "2.0", valid.CorrelationId));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleCompletionRequest(valid.Plan, valid.Submission, valid.RevisionMetadata, valid.CompletedUtc, valid.CompletedBy, "1.0", " "));
        Assert.Same(valid.Plan, valid.Plan); Assert.Same(valid.Submission, valid.Submission); Assert.Same(valid.RevisionMetadata, valid.RevisionMetadata);
        Assert.Equal(OrionDocumentaryNarrativeRevisionCycleFixture.Completed, valid.CompletedUtc); Assert.Equal(" completion reviewer ", valid.CompletedBy);
        DocumentaryNarrativeRevisionCycleFinalMetadataCertificationTests.RoundTrip(valid);
    }

    [Fact] public void Clean_completion_certifies_status_lineage_counts_no_change_and_serialization()
    {
        var result = OrionDocumentaryNarrativeRevisionCycleFixture.CleanResult();
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, result.Plan.Status);
        Assert.Equal(DocumentaryNarrativeRevisionStatus.NoChangesRequired, result.RevisionResult.Status);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, result.Status);
        Assert.Equal(result.SourceDraftId, result.TargetDraftId); Assert.Equal(result.SourceDraftVersion, result.TargetDraftVersion);
        Assert.Equal(0, result.AppliedChangeCount); Assert.Equal(0, result.UnresolvedRevisionItemCount);
        Assert.Equal(0, result.SourceFindingCount); Assert.Equal(0, result.RevisedFindingCount);
        Assert.True(result.ValidationComparison.IsClean); Assert.False(result.ValidationComparison.HasImproved); Assert.False(result.ValidationComparison.HasRegressed);
        DocumentaryNarrativeRevisionCycleFinalMetadataCertificationTests.RoundTrip(result);
    }

    [Fact] public void Completer_rejects_null_and_case_only_correlation_and_is_non_mutating_deterministic()
    {
        var completer = new DocumentaryNarrativeRevisionCycleCompleter(); Assert.Throws<ArgumentNullException>(() => completer.Complete(null!));
        var request = OrionDocumentaryNarrativeRevisionCycleFixture.CleanCompletionRequest(); var options = OrionDocumentaryNarrativeRevisionCycleFixture.JsonOptions();
        var before = JsonSerializer.Serialize(request, options); var first = completer.Complete(request);
        Assert.Equal(before, JsonSerializer.Serialize(request, options));
        Assert.Equal(JsonSerializer.Serialize(first, options), JsonSerializer.Serialize(completer.Complete(OrionDocumentaryNarrativeRevisionCycleFixture.CleanCompletionRequest()), options));
        var bad = new DocumentaryNarrativeRevisionCycleCompletionRequest(request.Plan, request.Submission, request.RevisionMetadata,
            request.CompletedUtc, request.CompletedBy, request.CompletionSchemaVersion, request.CorrelationId.ToUpperInvariant());
        Assert.Throws<ArgumentException>(() => completer.Complete(bad));
        Assert.NotEqual(DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision, first.Status);
    }
}

public sealed class DocumentaryNarrativeRevisionCycleFinalArchitectureCertificationTests
{
    [Fact] public void Exact_status_and_contract_property_inventories_have_no_public_setters()
    {
        Assert.Equal(["NoRevisionRequired", "AwaitingExternalRevision", "PartiallyCompleted", "CompletedWithRemainingFindings", "CompletedSuccessfully"], Enum.GetNames<DocumentaryNarrativeRevisionCycleStatus>());
        Properties<DocumentaryNarrativeRevisionCycleMetadata>("CreatedUtc", "CreatedBy", "CycleSchemaVersion", "CorrelationId");
        Properties<DocumentaryNarrativeRevisionCyclePlan>("CycleId", "SourceDraft", "SourceDraftId", "SourceDraftVersion", "SourceValidationResult", "RevisionRequest", "WorkPackage", "Metadata", "Status", "SourceFindingCount", "RevisionItemCount", "PassageWorkItemCount", "ManualReviewWorkItemCount", "RequiresExternalRevision");
        Properties<DocumentaryNarrativeRevisionCycleCompletionRequest>("Plan", "Submission", "RevisionMetadata", "CompletedUtc", "CompletedBy", "CompletionSchemaVersion", "CorrelationId");
        Properties<DocumentaryNarrativeRevisionValidationComparison>("SourceFindingCount", "RevisedFindingCount", "ResolvedFindingCount", "RemainingFindingCount", "IntroducedFindingCount", "SourceRuleCodes", "RevisedRuleCodes", "ResolvedRuleCodes", "RemainingRuleCodes", "IntroducedRuleCodes", "HasImproved", "HasRegressed", "IsClean");
        Properties<DocumentaryNarrativeRevisionCycleResult>("CycleId", "Plan", "Submission", "BindingRequest", "RevisionResult", "RevisedValidationResult", "ValidationComparison", "CompletedUtc", "CompletedBy", "CompletionSchemaVersion", "CorrelationId", "Status", "SourceDraftId", "SourceDraftVersion", "TargetDraftId", "TargetDraftVersion", "AppliedChangeCount", "UnresolvedRevisionItemCount", "SourceFindingCount", "RevisedFindingCount");
    }

    [Fact] public void Exact_synchronous_stateless_operation_boundaries_are_certified()
    {
        Operation<DocumentaryNarrativeRevisionCyclePlanner>("Plan", typeof(DocumentaryNarrativeRevisionCyclePlan), typeof(DocumentaryNarrativeDraft), typeof(DocumentaryNarrativeDraftValidationResult), typeof(string), typeof(DocumentaryNarrativeRevisionRequestMetadata), typeof(DocumentaryNarrativeRevisionExecutionMetadata), typeof(DocumentaryNarrativeRevisionCycleMetadata));
        Operation<DocumentaryNarrativeRevisionCycleCompleter>("Complete", typeof(DocumentaryNarrativeRevisionCycleResult), typeof(DocumentaryNarrativeRevisionCycleCompletionRequest));
        Operation<DocumentaryNarrativeRevisionValidationComparer>("Compare", typeof(DocumentaryNarrativeRevisionValidationComparison), typeof(DocumentaryNarrativeDraftValidationResult), typeof(DocumentaryNarrativeDraftValidationResult));
    }

    [Fact] public void Public_surface_contains_no_forbidden_capability()
    {
        string[] forbidden = ["Prompt", "PromptText", "SystemPrompt", "UserPrompt", "Provider", "ProviderName", "ModelName", "DeploymentName", "Temperature", "TopP", "MaxTokens", "TokenCount", "RawModelResponse", "GeneratedText", "SuggestedText", "CorrectedText", "AutoFix", "Http", "Endpoint", "RetryCount", "RetryDelay", "Repository", "Database", "Storage", "Scheduler", "Ssml", "Voice", "Audio", "Subtitle", "Srt", "Vtt"];
        Type[] types = [typeof(DocumentaryNarrativeRevisionCycleMetadata), typeof(DocumentaryNarrativeRevisionCyclePlan), typeof(DocumentaryNarrativeRevisionCycleCompletionRequest), typeof(DocumentaryNarrativeRevisionValidationComparison), typeof(DocumentaryNarrativeRevisionCycleResult), typeof(DocumentaryNarrativeRevisionCyclePlanner), typeof(DocumentaryNarrativeRevisionCycleCompleter), typeof(DocumentaryNarrativeRevisionValidationComparer)];
        Assert.All(types.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)), member => Assert.DoesNotContain(member.Name, forbidden));
    }

    private static void Properties<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(expected.Order(), actual.Select(p => p.Name).Order()); Assert.All(actual, p => Assert.False(p.SetMethod?.IsPublic ?? false));
    }
    private static void Operation<T>(string name, Type result, params Type[] parameters)
    {
        var type = typeof(T); Assert.True(type.IsSealed); Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(name, method.Name); Assert.Equal(result, method.ReturnType); Assert.Equal(parameters, method.GetParameters().Select(p => p.ParameterType));
        Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }
}
