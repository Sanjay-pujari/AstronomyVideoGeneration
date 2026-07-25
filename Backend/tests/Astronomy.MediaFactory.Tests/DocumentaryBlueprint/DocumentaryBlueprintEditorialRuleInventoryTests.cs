using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEditorialRuleInventoryTests
{
    [Fact] public void Inventory_is_exact_ordered_and_unique()
    {
        var inventory = DocumentaryBlueprintEditorialRuleCodes.Inventory;
        inventory.Select(x => x.Code).Should().Equal(Enumerable.Range(1, 16).Select(i => $"DBP-EDITORIAL-{i:000}"));
        inventory.Select(x => x.Code).Should().OnlyHaveUniqueItems();
        inventory.Select(x => x.Severity).Should().Equal(new[] { E,E,E,E,E,E,E,W,W,W,W,E,W,E,E,W });
    }
    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
    private const DocumentaryBlueprintValidationSeverity W = DocumentaryBlueprintValidationSeverity.Warning;
}
