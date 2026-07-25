using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
using DocumentaryBlueprintModel = Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialRuleTests
{
    private readonly DocumentaryBlueprintEditorialValidator _validator = new();
    public static IEnumerable<object[]> Cases()
    {
        yield return [OrionDocumentaryBlueprintValidationFixture.Empty(), "DBP-EDITORIAL-001", E, (string?)null, (int?)null, (string?)null];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(0, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-002", E, "scene.orion.0", 0, "SceneNumber"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook), OrionDocumentaryBlueprintValidationFixture.Scene(3, DocumentarySceneRole.ReflectiveClosing)), "DBP-EDITORIAL-003", E, (string?)null, (int?)null, (string?)null];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing), OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-004", E, (string?)null, (int?)null, (string?)null];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [])), "DBP-EDITORIAL-005", E, "scene.orion.1", 1, "KnowledgeReferences"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [new("k", "s", "p", false)])), "DBP-EDITORIAL-006", E, "scene.orion.1", 1, "KnowledgeReferences"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [new("k", "s", "p", true), new("k", "s", "p", false)])), "DBP-EDITORIAL-007", E, "scene.orion.1", 1, "KnowledgeReferences"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, question: "same"), OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing, question: "same")), "DBP-EDITORIAL-008", W, (string?)null, (int?)null, (string?)null];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.ScientificExplanation)), "DBP-EDITORIAL-009", W, "scene.orion.1", 1, "SceneRole"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-010", W, "scene.orion.1", 1, "SceneRole"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, priority: EditorialPriority.Critical, outcome: new("v", "n", false, false, true, false, false))), "DBP-EDITORIAL-011", W, "scene.orion.1", 1, "EditorialOutcome"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.PracticalObservation, outcome: new("v", "n", true, true, true, false, false))), "DBP-EDITORIAL-012", E, "scene.orion.1", 1, "EditorialOutcome"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.ReflectiveClosing, outcome: new("v", "n", true, true, true, false, false))), "DBP-EDITORIAL-013", W, "scene.orion.1", 1, "EditorialOutcome"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, visuals: [new("v", "t", null, null, true)])), "DBP-EDITORIAL-014", E, "scene.orion.1", 1, "VisualOpportunities"];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, duration: 0)), "DBP-EDITORIAL-015", E, (string?)null, (int?)null, (string?)null];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, duration: 0), OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing)), "DBP-EDITORIAL-016", W, "scene.orion.1", 1, "EstimatedDurationSeconds"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Approved_rule_reports_stable_finding(DocumentaryBlueprintModel blueprint, string code,
        DocumentaryBlueprintValidationSeverity severity, string? sceneId, int? sceneNumber, string? fieldName)
    {
        var findings = _validator.Validate(blueprint).Findings.Where(x => x.RuleCode == code).ToArray();

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(severity);
        findings[0].BlueprintId.Should().Be(blueprint.BlueprintId);
        findings[0].Message.Should().NotBeNullOrWhiteSpace();
        findings[0].SceneId.Should().Be(sceneId);
        findings[0].SceneNumber.Should().Be(sceneNumber);
        findings[0].FieldName.Should().Be(fieldName);
    }
    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
    private const DocumentaryBlueprintValidationSeverity W = DocumentaryBlueprintValidationSeverity.Warning;
}
