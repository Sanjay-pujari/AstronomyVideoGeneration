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
        Assert.Equal(Path.Combine("hero", "diagnostics", "hero-layout-validation.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroLayoutValidation));
        Assert.Equal(Path.Combine("hero", "diagnostics", "hero-generation-diagnostics.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroGenerationDiagnostics));
        Assert.Equal(Path.Combine("hero", "diagnostics", "hero-scene-manifest.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroSceneManifest));
        Assert.Equal(Path.Combine("hero", "diagnostics", "visual-prompt-diagnostics.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.VisualPromptDiagnostics));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-prompt-comparison.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroPromptComparison));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-migration-report.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroMigrationReport));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-v3-prompt.txt"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroV3Prompt));
        Assert.Equal(Path.Combine("hero", "comparison", "hero-v4-prompt.txt"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroV4Prompt));
        Assert.Equal(Path.Combine("hero", "diagnostics", "HeroIntelligenceContract.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroIntelligenceContract));
        Assert.Equal(Path.Combine("hero", "hero-final.png"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroFinal));
        Assert.Equal(Path.Combine("hero", "HeroArtifactManifest.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.HeroArtifactManifest));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryIntelligenceContract.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryIntelligenceContract));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryEditorialSequence.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryEditorialSequence));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryReview.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryReview));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryInformationDensityReview.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryInformationDensityReview));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryNarrativeFlowReview.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryNarrativeFlowReview));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryEducationalStorytellingReview.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryEducationalStorytellingReview));
        Assert.Equal(Path.Combine("gallery", "diagnostics", "GalleryBenchmarkMetadata.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryBenchmarkMetadata));
        Assert.Equal(Path.Combine("gallery", "GalleryArtifactManifest.json"), OutputArtifactRegistry.GetRelativePath(OutputArtifactName.GalleryArtifactManifest));
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

    [Fact]
    public void Registry_produces_manifest_with_logical_names_and_physical_paths()
    {
        var manifest = OutputArtifactRegistry.CreateHeroArtifactManifest(root, new OutputArtifactsOptions { Mode = OutputArtifactMode.Debug });

        Assert.Contains(OutputArtifactName.HeroLayoutValidation.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.HeroIntelligenceContract.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.EditorialProductReview.ToString(), manifest.ExpectedArtifacts);
        Assert.Equal(OutputArtifactRegistry.GetPath(root, OutputArtifactName.HeroLayoutValidation), manifest.Artifacts[OutputArtifactName.HeroLayoutValidation.ToString()]);
        Assert.Equal(OutputArtifactRegistry.GetPath(root, OutputArtifactName.HeroSceneManifest), manifest.Artifacts[OutputArtifactName.HeroSceneManifest.ToString()]);
        Assert.Equal(OutputArtifactRegistry.GetPath(root, OutputArtifactName.HeroIntelligenceContract), manifest.Artifacts[OutputArtifactName.HeroIntelligenceContract.ToString()]);
        Assert.Equal(OutputArtifactRegistry.GetPath(root, OutputArtifactName.EditorialProductReview), manifest.Artifacts[OutputArtifactName.EditorialProductReview.ToString()]);
    }

    [Fact]
    public void Registry_resolves_manifest_path_before_legacy_layout()
    {
        var manifestPath = OutputArtifactRegistry.GetManifestPath(root);
        var customLayout = Path.Combine(root, "custom", "layout.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(customLayout)!);
        var manifest = new HeroArtifactManifest("test", "Development", [OutputArtifactName.HeroLayoutValidation.ToString()], new Dictionary<string, string>
        {
            [OutputArtifactName.HeroLayoutValidation.ToString()] = customLayout
        });
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

        Assert.Equal(customLayout, OutputArtifactRegistry.ResolvePathFromManifestOrLegacy(root, OutputArtifactName.HeroLayoutValidation));
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
        Assert.Contains(OutputArtifactName.HeroLayoutValidation, artifacts);
        Assert.Contains(OutputArtifactName.HeroGenerationDiagnostics, artifacts);
        Assert.Contains(OutputArtifactName.HeroSceneManifest, artifacts);
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
            artifacts.AddRange([OutputArtifactName.HeroReview, OutputArtifactName.HeroLayoutValidation, OutputArtifactName.HeroGenerationDiagnostics, OutputArtifactName.HeroSceneManifest, OutputArtifactName.VisualPromptDiagnostics]);
        if (options.ShouldWriteComparison)
            artifacts.AddRange([OutputArtifactName.HeroPromptComparison, OutputArtifactName.HeroMigrationReport, OutputArtifactName.HeroV3Prompt, OutputArtifactName.HeroV4Prompt]);
        return artifacts;
    }
}
