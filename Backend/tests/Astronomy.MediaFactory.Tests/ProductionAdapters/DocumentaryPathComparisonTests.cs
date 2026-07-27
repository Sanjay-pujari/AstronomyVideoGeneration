using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryPathComparisonTests
{
    [Fact]
    public void OrdinalIgnoreCase_models_windows_path_casing()
    {
        var root = Path.Combine(Path.GetTempPath(), "Workspace");
        var child = Path.Combine(Path.GetTempPath(), "workspace", "attempt", "image.png");
        DocumentaryPathComparison.IsBelow(root, child, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Containment_requires_a_directory_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace");
        var sibling = Path.Combine(Path.GetTempPath(), "workspace-other", "image.png");
        DocumentaryPathComparison.IsBelow(root, sibling, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void Platform_comparison_accepts_an_owned_child()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace");
        DocumentaryPathComparison.IsBelow(root, Path.Combine(root, "attempt", "image.png")).Should().BeTrue();
    }
}
