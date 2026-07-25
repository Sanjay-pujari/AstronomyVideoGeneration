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
