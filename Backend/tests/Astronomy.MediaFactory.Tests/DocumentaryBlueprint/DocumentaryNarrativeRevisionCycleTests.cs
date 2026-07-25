using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionCycleMetadataTests
{
    [Fact] public void Requires_external_values_and_exact_schema()
    {
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleMetadata(default, "owner", "1.0", "c"));
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCycleMetadata(DateTimeOffset.Parse("2026-01-15Z"), "owner", "2.0", "c"));
    }
}

public sealed class DocumentaryNarrativeRevisionValidationComparisonTests
{
    private static DocumentaryNarrativeDraftValidationFinding Finding(string code, string draft, string message = "message") =>
        new(code, DocumentaryNarrativeDraftValidationSeverity.Warning, message, draft, "section", 1, "passage", 1, "Text");

    [Fact] public void Treats_findings_as_ordered_multisets()
    {
        var source = new DocumentaryNarrativeDraftValidationResult("source", [Finding("A", "source"), Finding("A", "source")]);
        var revised = new DocumentaryNarrativeDraftValidationResult("target", [Finding("A", "target")]);
        var result = new DocumentaryNarrativeRevisionValidationComparer().Compare(source, revised);
        Assert.Equal(1, result.ResolvedFindingCount); Assert.Equal(1, result.RemainingFindingCount); Assert.Equal(0, result.IntroducedFindingCount);
        Assert.Equal(["A"], result.ResolvedRuleCodes); Assert.True(result.HasImproved); Assert.False(result.HasRegressed);
    }

    [Fact] public void Equal_count_replacement_is_regression_not_improvement()
    {
        var source = new DocumentaryNarrativeDraftValidationResult("source", [Finding("A", "source")]);
        var revised = new DocumentaryNarrativeDraftValidationResult("target", [Finding("B", "target")]);
        var result = new DocumentaryNarrativeRevisionValidationComparer().Compare(source, revised);
        Assert.Equal(1, result.ResolvedFindingCount); Assert.Equal(1, result.IntroducedFindingCount);
        Assert.False(result.HasImproved); Assert.True(result.HasRegressed); Assert.False(result.IsClean);
    }

    [Fact] public void Clean_result_resolves_every_finding()
    {
        var source = new DocumentaryNarrativeDraftValidationResult("source", [Finding("A", "source"), Finding("B", "source")]);
        var result = new DocumentaryNarrativeRevisionValidationComparer().Compare(source, new("target", []));
        Assert.Equal(2, result.ResolvedFindingCount); Assert.True(result.HasImproved); Assert.True(result.IsClean);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, false, false, true)]
    [InlineData(0, 1, 0, 0, 0, false, true, false)]
    public void Rejects_inconsistent_count_decompositions(int source, int revised, int resolved, int remaining,
        int introduced, bool improved, bool regressed, bool clean) =>
        Assert.Throws<ArgumentException>(() => Comparison(source, revised, resolved, remaining, introduced, improved, regressed, clean));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    public void Rejects_inconsistent_boolean_summaries(bool improved, bool regressed, bool clean) =>
        Assert.Throws<ArgumentException>(() => Comparison(1, 0, 1, 0, 0, improved, regressed, clean));

    private static DocumentaryNarrativeRevisionValidationComparison Comparison(int source, int revised, int resolved,
        int remaining, int introduced, bool improved, bool regressed, bool clean) => new(source, revised, resolved,
            remaining, introduced, Enumerable.Repeat("S", source).ToArray(), Enumerable.Repeat("V", revised).ToArray(),
            Enumerable.Repeat("R", resolved).ToArray(), Enumerable.Repeat("M", remaining).ToArray(),
            Enumerable.Repeat("I", introduced).ToArray(), improved, regressed, clean);
}

public sealed class DocumentaryNarrativeRevisionCyclePlannerTests
{
    private const string Correlation = "cycle-correlation";
    [Fact] public void Plans_clean_cycle_deterministically()
    {
        var draft = OrionDocumentaryNarrativeRevisionFixture.ValidDraft(); var validation = new DocumentaryNarrativeDraftValidator().Validate(draft);
        DocumentaryNarrativeRevisionCyclePlan Make() => new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, validation, "request.clean",
            new(DateTimeOffset.Parse("2026-01-15T14:00:00Z"), "reviewer", draft.Version, "1.0", "1.0", Correlation),
            new(DateTimeOffset.Parse("2026-01-15T14:01:00Z"), "coordinator", "1.0", Correlation),
            new(DateTimeOffset.Parse("2026-01-15T14:02:00Z"), "coordinator", "1.0", Correlation));
        var plan = Make(); Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, plan.Status);
        Assert.False(plan.RequiresExternalRevision); Assert.Empty(plan.RevisionRequest.Items); Assert.Same(draft, plan.SourceDraft);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); Assert.Equal(JsonSerializer.Serialize(Make(), options), JsonSerializer.Serialize(Make(), options));
    }

    [Fact] public void Rejects_mismatched_correlation_at_boundary()
    {
        var draft = OrionDocumentaryNarrativeRevisionFixture.ValidDraft(); var validation = new DocumentaryNarrativeDraftValidator().Validate(draft);
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, validation, "request.clean",
            new(DateTimeOffset.Parse("2026-01-15T14:00:00Z"), "reviewer", draft.Version, "1.0", "1.0", "request-correlation"),
            new(DateTimeOffset.Parse("2026-01-15T14:01:00Z"), "coordinator", "1.0", "execution-correlation"),
            new(DateTimeOffset.Parse("2026-01-15T14:02:00Z"), "coordinator", "1.0", "cycle-correlation")));
    }
}

public sealed class DocumentaryNarrativeRevisionCycleInvariantTests
{
    private const string Correlation = "correlation";

    [Fact] public void Plan_requires_an_exact_request_correlation()
    {
        var plan = CleanPlan();
        var metadata = new DocumentaryNarrativeRevisionRequestMetadata(plan.RevisionRequest.Metadata.CreatedUtc,
            plan.RevisionRequest.Metadata.CreatedBy, plan.SourceDraftVersion, plan.RevisionRequest.Metadata.ValidationSchemaVersion,
            "1.0", "Correlation");
        var request = new DocumentaryNarrativeRevisionRequest(plan.RevisionRequest.RevisionRequestId, plan.SourceDraftId,
            plan.SourceDraftVersion, plan.SourceValidationResult, metadata, plan.RevisionRequest.Items);
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCyclePlan(plan.CycleId, plan.SourceDraft,
            plan.SourceValidationResult, request, plan.WorkPackage, plan.Metadata, plan.Status));
    }

    [Fact] public void Plan_requires_an_exact_work_package_correlation()
    {
        var plan = CleanPlan();
        var package = plan.WorkPackage;
        var metadata = new DocumentaryNarrativeRevisionExecutionMetadata(package.Metadata.CreatedUtc,
            package.Metadata.CreatedBy, "1.0", "Correlation");
        var changed = new DocumentaryNarrativeRevisionWorkPackage(package.WorkPackageId, package.RevisionRequestId,
            package.DraftId, package.DraftVersion, package.SubjectId, package.SubjectName, package.PublicationFormat,
            package.PrimaryLanguage, metadata, package.PassageWorkItems, package.ManualReviewWorkItems);
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionCyclePlan(plan.CycleId, plan.SourceDraft,
            plan.SourceValidationResult, plan.RevisionRequest, changed, plan.Metadata, plan.Status));
    }

    [Fact] public void Result_rejects_wrong_correlation_and_completed_status()
    {
        var result = CleanResult();
        Assert.Throws<ArgumentException>(() => Reconstruct(result, correlation: "Correlation"));
        Assert.Throws<ArgumentException>(() => Reconstruct(result,
            status: DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully));
        Assert.Throws<ArgumentException>(() => Reconstruct(result,
            status: DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision));
    }

    [Fact] public void Valid_identical_correlation_chain_constructs_a_clean_result()
    {
        var result = CleanResult();
        Assert.Equal(Correlation, result.CorrelationId);
        Assert.Equal(DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired, result.Status);
        Assert.True(result.ValidationComparison.IsClean);
    }

    private static DocumentaryNarrativeRevisionCyclePlan CleanPlan()
    {
        var draft = OrionDocumentaryNarrativeRevisionFixture.ValidDraft();
        var validation = new DocumentaryNarrativeDraftValidator().Validate(draft);
        return new DocumentaryNarrativeRevisionCyclePlanner().Plan(draft, validation, "request.clean",
            new(DateTimeOffset.Parse("2026-01-15T14:00:00.1234567+05:30"), "reviewer", draft.Version, "1.0", "1.0", Correlation),
            new(DateTimeOffset.Parse("2026-01-15T14:01:00.1234567+05:30"), "coordinator", "1.0", Correlation),
            new(DateTimeOffset.Parse("2026-01-15T14:02:00.1234567+05:30"), "coordinator", "1.0", Correlation));
    }

    private static DocumentaryNarrativeRevisionCycleResult CleanResult()
    {
        var plan = CleanPlan();
        var submission = new DocumentaryNarrativeRevisionSubmission("submission.clean", plan.WorkPackage.WorkPackageId,
            plan.RevisionRequest.RevisionRequestId, plan.SourceDraftId, plan.SourceDraftVersion,
            new(DateTimeOffset.Parse("2026-01-15T15:00:00Z"), "editor", "1.0",
                DocumentaryNarrativeRevisionEditorType.Human, "editor", Correlation), []);
        var revisionMetadata = new DocumentaryNarrativeRevisionMetadata(DateTimeOffset.Parse("2026-01-15T15:01:00Z"),
            "reviewer", plan.SourceDraftId, plan.SourceDraftVersion, plan.SourceDraftVersion + ".revised", "1.0", Correlation);
        return new DocumentaryNarrativeRevisionCycleCompleter().Complete(new(plan, submission, revisionMetadata,
            DateTimeOffset.Parse("2026-01-15T15:02:00Z"), "reviewer", "1.0", Correlation));
    }

    private static DocumentaryNarrativeRevisionCycleResult Reconstruct(DocumentaryNarrativeRevisionCycleResult value,
        string? correlation = null, DocumentaryNarrativeRevisionCycleStatus? status = null) => new(value.Plan,
            value.Submission, value.BindingRequest, value.RevisionResult, value.RevisedValidationResult,
            value.ValidationComparison, value.CompletedUtc, value.CompletedBy, value.CompletionSchemaVersion,
            correlation ?? value.CorrelationId, status ?? value.Status);
}

public sealed class DocumentaryNarrativeRevisionCycleArchitectureTests
{
    [Fact] public void Operations_have_exact_stateless_boundaries()
    {
        AssertOperation(typeof(DocumentaryNarrativeRevisionCyclePlanner), "Plan", 6);
        AssertOperation(typeof(DocumentaryNarrativeRevisionCycleCompleter), "Complete", 1);
    }
    [Fact] public void Inventories_are_exact()
    {
        Assert.Equal(new[] { "NoRevisionRequired", "AwaitingExternalRevision", "PartiallyCompleted", "CompletedWithRemainingFindings", "CompletedSuccessfully" }, Enum.GetNames<DocumentaryNarrativeRevisionCycleStatus>());
        AssertProperties<DocumentaryNarrativeRevisionCycleMetadata>("CreatedUtc", "CreatedBy", "CycleSchemaVersion", "CorrelationId");
        AssertProperties<DocumentaryNarrativeRevisionCyclePlan>("CycleId", "SourceDraft", "SourceDraftId", "SourceDraftVersion", "SourceValidationResult", "RevisionRequest", "WorkPackage", "Metadata", "Status", "SourceFindingCount", "RevisionItemCount", "PassageWorkItemCount", "ManualReviewWorkItemCount", "RequiresExternalRevision");
    }
    private static void AssertOperation(Type type, string name, int parameterCount)
    {
        Assert.True(type.IsSealed); Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Equal(name, method.Name); Assert.Equal(parameterCount, method.GetParameters().Length);
    }
    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.Equal(names.Order(), properties.Select(x => x.Name).Order()); Assert.All(properties, x => Assert.False(x.SetMethod?.IsPublic ?? false));
    }
}
