using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintValidationResultTests
{
    [Theory] [InlineData("")] [InlineData(" ")] public void Finding_rejects_blank_code(string value) => FluentActions.Invoking(() => Finding(value)).Should().Throw<ArgumentException>();
    [Fact] public void Finding_rejects_undefined_severity() => FluentActions.Invoking(() => new DocumentaryBlueprintValidationFinding("code", (DocumentaryBlueprintValidationSeverity)42, "message", "blueprint")).Should().Throw<ArgumentOutOfRangeException>();
    [Fact] public void Finding_rejects_blank_values() { FluentActions.Invoking(() => new DocumentaryBlueprintValidationFinding("code", E, " ", "blueprint")).Should().Throw<ArgumentException>(); FluentActions.Invoking(() => new DocumentaryBlueprintValidationFinding("code", E, "message", " ")).Should().Throw<ArgumentException>(); FluentActions.Invoking(() => new DocumentaryBlueprintValidationFinding("code", E, "message", "blueprint", " ")).Should().Throw<ArgumentException>(); FluentActions.Invoking(() => new DocumentaryBlueprintValidationFinding("code", E, "message", "blueprint", fieldName: " ")).Should().Throw<ArgumentException>(); }
    [Fact] public void Result_defensively_copies_and_derives_counts() { var list = new List<DocumentaryBlueprintValidationFinding> { Finding("error") }; var result = new DocumentaryBlueprintValidationResult("blueprint", list); list.Clear(); result.Findings.Should().HaveCount(1); result.ErrorCount.Should().Be(1); result.WarningCount.Should().Be(0); result.IsValid.Should().BeFalse(); ((IList<DocumentaryBlueprintValidationFinding>)result.Findings).Invoking(x => x.Add(Finding("x"))).Should().Throw<NotSupportedException>(); }
    [Fact] public void Result_rejects_invalid_collections() { FluentActions.Invoking(() => new DocumentaryBlueprintValidationResult("blueprint", null!)).Should().Throw<ArgumentNullException>(); FluentActions.Invoking(() => new DocumentaryBlueprintValidationResult("blueprint", [null!])).Should().Throw<ArgumentException>(); }
    private static DocumentaryBlueprintValidationFinding Finding(string code) => new(code, E, "message", "blueprint");
    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
}
