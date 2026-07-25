using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialValidatorDeterminismTests
{
    [Fact] public void Equivalent_blueprints_produce_identical_ordered_json() { var v = new DocumentaryBlueprintEditorialValidator(); var a = v.Validate(OrionDocumentaryBlueprintValidationFixture.Empty()); var b = v.Validate(OrionDocumentaryBlueprintValidationFixture.Empty()); JsonSerializer.Serialize(a, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().Be(JsonSerializer.Serialize(b, new JsonSerializerOptions(JsonSerializerDefaults.Web))); a.Findings.Select(x => x.RuleCode).Should().Equal("DBP-EDITORIAL-001", "DBP-EDITORIAL-015"); }
    [Fact] public void Scene_findings_are_number_then_id_ordered() { var b = OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(2, DocumentarySceneRole.ReflectiveClosing, "z", duration: 0), OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.OpeningHook, "a", duration: 0)); var findings = new DocumentaryBlueprintEditorialValidator().Validate(b).Findings.Where(x => x.RuleCode == "DBP-EDITORIAL-016"); findings.Select(x => x.SceneId).Should().Equal("a", "z"); }
}
