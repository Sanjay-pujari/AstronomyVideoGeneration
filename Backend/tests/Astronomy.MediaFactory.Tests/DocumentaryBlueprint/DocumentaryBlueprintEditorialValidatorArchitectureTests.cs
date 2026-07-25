using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialValidatorArchitectureTests
{
    [Fact] public void Validator_is_sealed_synchronous_and_dependency_free() { var t = typeof(DocumentaryBlueprintEditorialValidator); t.IsSealed.Should().BeTrue(); t.GetConstructors().Single().GetParameters().Should().BeEmpty(); t.GetMethod("Validate")!.ReturnType.Should().Be(typeof(DocumentaryBlueprintValidationResult)); typeof(Task).IsAssignableFrom(t.GetMethod("Validate")!.ReturnType).Should().BeFalse(); }
    [Fact] public void Contracts_are_read_only_and_have_no_forbidden_fields() { var types = new[] { typeof(DocumentaryBlueprintValidationFinding), typeof(DocumentaryBlueprintValidationResult) }; types.SelectMany(t => t.GetProperties()).Should().OnlyContain(p => p.SetMethod is null); var forbidden = new[] { "Narration", "NarrationText", "VoiceOver", "VoiceOverText", "Script", "ScriptText", "Prompt", "PromptText", "GeneratedText", "LlmResponse", "ReplacementText", "SuggestedText", "AutoFix" }; types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name).Should().NotIntersectWith(forbidden); }
}
