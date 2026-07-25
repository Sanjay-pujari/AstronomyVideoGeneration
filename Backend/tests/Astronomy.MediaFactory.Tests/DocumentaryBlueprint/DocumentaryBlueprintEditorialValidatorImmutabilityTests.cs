using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialValidatorImmutabilityTests
{
    [Fact] public void Validation_does_not_mutate_blueprint_or_nested_order() { var b = OrionDocumentaryBlueprintValidationFixture.Create(); var before = JsonSerializer.Serialize(b); var scenes = b.Scenes.Select(x => x.SceneId).ToArray(); var knowledge = b.Scenes.SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId).ToArray(); _ = new DocumentaryBlueprintEditorialValidator().Validate(b); JsonSerializer.Serialize(b).Should().Be(before); b.Scenes.Select(x => x.SceneId).Should().Equal(scenes); b.Scenes.SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId).Should().Equal(knowledge); }
}
