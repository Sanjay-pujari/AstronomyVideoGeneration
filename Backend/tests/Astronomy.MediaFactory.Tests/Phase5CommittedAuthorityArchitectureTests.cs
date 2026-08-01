namespace Astronomy.MediaFactory.Tests;

public sealed class Phase5CommittedAuthorityArchitectureTests
{
    private static string Pipeline => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
    private static string Evaluator => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint", "Phase5CommittedAuthorityEvaluator.cs"));

    [Fact]
    public void production_pipeline_does_not_use_ExistingBlueprintCertificationArtifactsAreValid_as_authority() =>
        Assert.DoesNotContain("ExistingBlueprintCertificationArtifactsAreValid", Pipeline);

    [Fact]
    public void phase5_manifest_entries_are_relative()
    {
        Assert.Contains("relativePath = \"05-editorial/blueprint-certification.json\"", Pipeline);
        Assert.DoesNotContain("relativePath = NormalizePath(Path.Combine(context.OutputRoot", Pipeline);
    }

    [Fact]
    public void phase5_committed_evaluator_is_registered_once()
    {
        var composition = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Equal(1, Count(composition, "IPhase5CommittedAuthorityEvaluator, Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint.Phase5CommittedAuthorityEvaluator"));
    }

    [Fact]
    public void phase5_publication_file_system_is_registered_once()
    {
        var composition = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Equal(1, Count(composition, "IPhase5PublicationFileSystem, Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint.Phase5PublicationFileSystem"));
    }

    [Fact]
    public void transaction_mutators_use_the_fault_injectable_boundary()
    {
        foreach (var file in new[] { "Phase5PublicationTransactionCoordinator.cs", "Phase5PublicationRecoveryService.cs" })
        {
            var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint", file));
            Assert.Contains("IPhase5PublicationFileSystem fileSystem", source);
            Assert.DoesNotContain("Directory.Move(", source);
            Assert.DoesNotContain("File.Move(", source);
            Assert.DoesNotContain("File.Delete(", source);
            Assert.DoesNotContain("Directory.Delete(", source);
        }
    }

    [Fact]
    public void phase5_success_depends_on_committed_state_evaluation()
    {
        Assert.Contains("phase5CommittedAuthorityEvaluator.EvaluateAsync", Pipeline);
        Assert.Contains("Phase 5 committed-state readback failed", Pipeline);
    }

    [Fact]
    public void phase5_committed_evaluator_reports_relative_artifact_paths()
    {
        Assert.Contains("05-editorial/{item.File}", Evaluator);
        Assert.Contains("Path.IsPathRooted(value) ? string.Empty", Evaluator);
    }

    [Fact]
    public void phase6_does_not_require_optional_certification_diagnostics()
    {
        Assert.Contains("File.Exists(diagnosticsPath)?", Pipeline);
        Assert.Contains("CertificationDiagnostics?", File.ReadAllText(RepositoryTestPaths.CoreSource("DocumentaryBlueprint", "StoryFrameAuthorityContracts.cs")));
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
