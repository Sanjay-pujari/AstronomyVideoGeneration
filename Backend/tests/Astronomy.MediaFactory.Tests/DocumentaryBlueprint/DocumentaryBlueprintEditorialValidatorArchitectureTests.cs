using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
using DocumentaryBlueprintModel = Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintEditorialValidatorArchitectureTests
{
    [Fact] public void Validator_is_sealed_synchronous_and_dependency_free() { var t = typeof(DocumentaryBlueprintEditorialValidator); t.IsSealed.Should().BeTrue(); t.GetConstructors().Single().GetParameters().Should().BeEmpty(); t.GetMethod("Validate")!.ReturnType.Should().Be(typeof(DocumentaryBlueprintValidationResult)); typeof(Task).IsAssignableFrom(t.GetMethod("Validate")!.ReturnType).Should().BeFalse(); }
    [Fact] public void Contracts_are_read_only_and_have_no_forbidden_fields() { var types = new[] { typeof(DocumentaryBlueprintValidationFinding), typeof(DocumentaryBlueprintValidationResult) }; types.SelectMany(t => t.GetProperties()).Should().OnlyContain(p => p.SetMethod == null); var forbidden = new[] { "Narration", "NarrationText", "VoiceOver", "VoiceOverText", "Script", "ScriptText", "Prompt", "PromptText", "GeneratedText", "LlmResponse", "ReplacementText", "SuggestedText", "AutoFix" }; types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name).Should().NotIntersectWith(forbidden); }

    [Theory]
    [MemberData(nameof(ContractInventories))]
    public void O21_and_O22_public_property_inventories_remain_exact(Type type, string[] expected)
    {
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Order()
            .Should().Equal(expected);
    }

    public static IEnumerable<object[]> ContractInventories()
    {
        string[] blueprintProperties = ["BlueprintId", "KnowledgeId", "Metadata", "PrimaryLanguage", "PublicationFormat", "Scenes", "SubjectId", "SubjectName", "Version"];
        string[] sceneProperties = ["EditorialOutcome", "EditorialPriority", "EstimatedDurationSeconds", "KnowledgeReferences", "NarrativeStage", "SceneId", "SceneNumber", "SceneObjective", "SceneRole", "Title", "Transition", "ViewerQuestion", "VisualOpportunities"];
        yield return [typeof(DocumentaryBlueprintModel), blueprintProperties];
        yield return [typeof(DocumentarySceneBlueprint), sceneProperties];
        yield return [typeof(DocumentaryBlueprintBuildRequest), blueprintProperties];
        yield return [typeof(DocumentarySceneBlueprintInput), sceneProperties];
    }

    [Fact]
    public void O22_builder_signature_and_dependency_boundary_remain_exact()
    {
        var type = typeof(DocumentaryBlueprintBuilder);
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle().Subject;
        var build = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().ContainSingle().Subject;

        type.IsSealed.Should().BeTrue();
        constructor.GetParameters().Should().BeEmpty();
        build.Name.Should().Be("Build");
        build.GetParameters().Should().ContainSingle().Which.ParameterType.Should().Be(typeof(DocumentaryBlueprintBuildRequest));
        build.ReturnType.Should().Be(typeof(DocumentaryBlueprintModel));
        typeof(Task).IsAssignableFrom(build.ReturnType).Should().BeFalse();
    }
}
