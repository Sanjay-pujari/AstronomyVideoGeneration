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

public sealed class OutputArtifactRegistryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "artifact-registry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Registry_resolves_hero_artifacts_to_v2_layout()
    {
        Assert.Equal(Path.Combine("hero", "diagnostics", "hero-review.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroReview));
        Assert.Equal(Path.Combine("hero", "diagnostics", "hero-generation-diagnostics.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroGenerationDiagnostics));
        Assert.Equal(Path.Combine("hero", "diagnostics", "visual-prompt-diagnostics.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.VisualPromptDiagnostics));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-prompt-comparison.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroPromptComparison));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-migration-report.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroMigrationReport));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-v3-prompt.txt"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroV3Prompt));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-v4-prompt.txt"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroV4Prompt));
        Assert.Equal(Path.Combine("hero", "hero-final.png"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroFinal));
    }

    [Fact]
    public void Registry_prefers_new_layout_when_new_and_legacy_files_exist()
    {
        var newPath = OutputArtifactRegistry.GetPath(root, OutputArtifactName.HeroReview);
        var legacyPath = OutputArtifactRegistry.GetLegacyPath(root, OutputArtifactName.HeroReview);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy");
        File.WriteAllText(newPath, "new");

        Assert.Equal(newPath, OutputArtifactRegistry.ResolveExistingPath(root, OutputArtifactName.HeroReview));
    }

    [Fact]
    public void Registry_supports_legacy_layout_when_new_file_is_absent()
    {
        var legacyPath = OutputArtifactRegistry.GetLegacyPath(root, OutputArtifactName.HeroReview);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy");

        Assert.Equal(legacyPath, OutputArtifactRegistry.ResolveExistingPath(root, OutputArtifactName.HeroReview));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

public sealed class Phase11HeroArtifactValidationPolicyTests
{
    [Theory]
    [InlineData(OutputArtifactMode.Production, false, false, false, false)]
    [InlineData(OutputArtifactMode.Development, false, false, true, true)]
    [InlineData(OutputArtifactMode.Debug, false, false, true, true)]
    [InlineData(OutputArtifactMode.Production, true, true, true, true)]
    public void Validation_policy_matches_output_artifact_mode(OutputArtifactMode mode, bool writeDiagnostics, bool writeComparison, bool expectDiagnostics, bool expectComparison)
    {
        var options = new OutputArtifactsOptions { Mode = mode, WriteDiagnostics = writeDiagnostics, WriteComparison = writeComparison };

        Assert.Equal(expectDiagnostics, options.ShouldWriteDiagnostics);
        Assert.Equal(expectComparison, options.ShouldWriteComparison);
    }

    [Fact]
    public void Production_validation_does_not_require_optional_diagnostics_or_comparison_artifacts()
    {
        var artifacts = ResolveExpectedArtifacts(new OutputArtifactsOptions { Mode = OutputArtifactMode.Production, WriteDiagnostics = false, WriteComparison = false });

        Assert.Equal([OutputArtifactName.HeroFinal], artifacts);
    }

    [Fact]
    public void Development_validation_requires_diagnostics_and_comparison_artifacts()
    {
        var artifacts = ResolveExpectedArtifacts(new OutputArtifactsOptions { Mode = OutputArtifactMode.Development });

        Assert.Contains(OutputArtifactName.HeroFinal, artifacts);
        Assert.Contains(OutputArtifactName.HeroReview, artifacts);
        Assert.Contains(OutputArtifactName.HeroGenerationDiagnostics, artifacts);
        Assert.Contains(OutputArtifactName.VisualPromptDiagnostics, artifacts);
        Assert.Contains(OutputArtifactName.HeroPromptComparison, artifacts);
        Assert.Contains(OutputArtifactName.HeroMigrationReport, artifacts);
        Assert.Contains(OutputArtifactName.HeroV3Prompt, artifacts);
        Assert.Contains(OutputArtifactName.HeroV4Prompt, artifacts);
    }

    [Fact]
    public void Missing_required_production_artifact_is_detected()
    {
        var root = Path.Combine(Path.GetTempPath(), "artifact-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var missing = ResolveExpectedArtifacts(new OutputArtifactsOptions { Mode = OutputArtifactMode.Production, WriteDiagnostics = false, WriteComparison = false })
                .Select(a => OutputArtifactRegistry.GetPath(root, a))
                .Where(path => !File.Exists(path))
                .ToArray();

            Assert.Single(missing);
            Assert.EndsWith(Path.Combine("hero", "hero-final.png"), missing[0]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyList<OutputArtifactName> ResolveExpectedArtifacts(OutputArtifactsOptions options)
    {
        var artifacts = new List<OutputArtifactName> { OutputArtifactName.HeroFinal };
        if (options.ShouldWriteDiagnostics)
            artifacts.AddRange([OutputArtifactName.HeroReview, OutputArtifactName.HeroGenerationDiagnostics, OutputArtifactName.VisualPromptDiagnostics]);
        if (options.ShouldWriteComparison)
            artifacts.AddRange([OutputArtifactName.HeroPromptComparison, OutputArtifactName.HeroMigrationReport, OutputArtifactName.HeroV3Prompt, OutputArtifactName.HeroV4Prompt]);
        return artifacts;
    }
}
