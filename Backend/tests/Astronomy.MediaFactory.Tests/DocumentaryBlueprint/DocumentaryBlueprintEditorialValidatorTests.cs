using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintEditorialValidatorTests
{
    private readonly DocumentaryBlueprintEditorialValidator _validator = new();
    [Fact] public void Valid_orion_blueprint_has_no_findings() { var b = OrionDocumentaryBlueprintValidationFixture.Create(); var r = _validator.Validate(b); r.BlueprintId.Should().Be(b.BlueprintId); r.IsValid.Should().BeTrue(); r.Findings.Should().BeEmpty(); }
    [Fact] public void Null_blueprint_is_rejected() => FluentActions.Invoking(() => _validator.Validate(null!)).Should().Throw<ArgumentNullException>();
    [Fact] public void Warnings_do_not_invalidate() { var b = OrionDocumentaryBlueprintValidationFixture.Create(OrionDocumentaryBlueprintValidationFixture.Scene(1, DocumentarySceneRole.ScientificExplanation)); var r = _validator.Validate(b); r.WarningCount.Should().BePositive(); r.ErrorCount.Should().Be(0); r.IsValid.Should().BeTrue(); }
    [Fact] public void Errors_invalidate() { var r = _validator.Validate(OrionDocumentaryBlueprintValidationFixture.Empty()); r.ErrorCount.Should().BePositive(); r.IsValid.Should().BeFalse(); }
}
