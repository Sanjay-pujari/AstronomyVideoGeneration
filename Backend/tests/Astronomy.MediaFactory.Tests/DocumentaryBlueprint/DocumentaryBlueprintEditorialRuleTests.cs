using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialRuleTests
{
    private readonly DocumentaryBlueprintEditorialValidator _validator = new();
    public static IEnumerable<object[]> Cases()
    {
        yield return [OrionDocumentaryBlueprintValidationFixture.Empty(), "DBP-EDITORIAL-001", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(0, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-002", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook), OrionDocumentaryBlueprintValidationFixture.Scene(3, DocumentarySceneRole.ReflectiveClosing)), "DBP-EDITORIAL-003", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing), OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-004", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [])), "DBP-EDITORIAL-005", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [new("k", "s", "p", false)])), "DBP-EDITORIAL-006", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, knowledge: [new("k", "s", "p", true), new("k", "s", "p", false)])), "DBP-EDITORIAL-007", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, question: "same"), OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing, question: "same")), "DBP-EDITORIAL-008", W];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.ScientificExplanation)), "DBP-EDITORIAL-009", W];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook)), "DBP-EDITORIAL-010", W];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, priority: EditorialPriority.Critical, outcome: new("v", "n", false, false, true, false, false))), "DBP-EDITORIAL-011", W];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.PracticalObservation, outcome: new("v", "n", true, true, true, false, false))), "DBP-EDITORIAL-012", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.ReflectiveClosing, outcome: new("v", "n", true, true, true, false, false))), "DBP-EDITORIAL-013", W];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, visuals: [new("v", "t", null, null, true)])), "DBP-EDITORIAL-014", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, duration: 0)), "DBP-EDITORIAL-015", E];
        yield return [OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, duration: 0), OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing)), "DBP-EDITORIAL-016", W];
    }
    [Theory, MemberData(nameof(Cases))] public void Approved_rule_reports_stable_finding(DocumentaryBlueprint blueprint, string code, DocumentaryBlueprintValidationSeverity severity)
    { var findings = _validator.Validate(blueprint).Findings.Where(x => x.RuleCode == code).ToArray(); findings.Should().HaveCount(1); findings[0].Severity.Should().Be(severity); findings[0].BlueprintId.Should().Be(blueprint.BlueprintId); findings[0].Message.Should().NotBeNullOrWhiteSpace(); }
    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
    private const DocumentaryBlueprintValidationSeverity W = DocumentaryBlueprintValidationSeverity.Warning;
}
