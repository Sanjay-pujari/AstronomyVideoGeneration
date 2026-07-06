using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class OutputArtifactsOptionsTests
{
    [Fact]
    public void Development_mode_writes_diagnostics_and_comparison()
    {
        var options = new OutputArtifactsOptions { Mode = OutputArtifactMode.Development };

        Assert.True(options.ShouldWriteDiagnostics);
        Assert.True(options.ShouldWriteComparison);
    }

    [Fact]
    public void Production_mode_keeps_hero_root_clean_by_default()
    {
        var options = new OutputArtifactsOptions
        {
            Mode = OutputArtifactMode.Production,
            WriteDiagnostics = false,
            WriteComparison = false
        };

        Assert.False(options.ShouldWriteDiagnostics);
        Assert.False(options.ShouldWriteComparison);
    }

    [Fact]
    public void Ci_mode_writes_diagnostics_but_skips_comparison_images()
    {
        var options = new OutputArtifactsOptions { Mode = OutputArtifactMode.CI, WriteDiagnostics = true, WriteComparison = true };

        Assert.True(options.ShouldWriteDiagnostics);
        Assert.False(options.ShouldWriteComparison);
    }
}
