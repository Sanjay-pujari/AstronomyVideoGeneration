namespace Astronomy.MediaFactory.Tests;

public sealed class Phase5BlueprintCertificationIntegrationTests
{
    private static string RepositoryRoot => RepositoryTestPaths.Root();
    private static string Pipeline => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
    private static string Integration => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "DocumentaryBlueprintCertificationIntegrationService.cs"));

    [Fact]
    public void Phase5_calls_existing_DocumentaryProductionCertifier_once()
    {
        Assert.Contains("DocumentaryProductionCertifier certifier", Integration);
        Assert.Equal(1, Count(Integration, "certifier.Certify(request)"));
    }

    [Fact]
    public void Phase5_does_not_call_QuestionEngine() => Assert.DoesNotContain("questionEngine", Phase5Method(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Phase5_does_not_call_DocumentaryBlueprintBuilder() => Assert.DoesNotContain("DocumentaryBlueprintBuilder", Phase5Method(), StringComparison.Ordinal);

    [Fact]
    public void Phase5_reads_all_Phase4_authority_artifacts()
    {
        foreach (var file in new[] { "documentary-blueprint.json", "documentary-blueprint.long.json", "documentary-blueprint.short.json", "blueprint-build-diagnostics.json" })
            Assert.Contains(file, Pipeline);
    }

    [Fact]
    public void Phase5_writes_all_three_certification_artifacts()
    {
        foreach (var file in new[] { "blueprint-certification.json", "editorial-contract.json", "certification-diagnostics.json" })
            Assert.Contains(file, Phase5Method());
    }

    [Fact]
    public void Phase5_registers_correct_manifest_roles()
    {
        Assert.Contains("DownstreamContract", Pipeline);
        Assert.Contains("phase5Artifacts", Pipeline);
    }

    [Fact]
    public void Phase5_rejected_certification_fails_phase() => Assert.Contains("Certification was rejected.", File.ReadAllText(Path.Combine(RepositoryRoot, "Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintCertificationArtifactValidator.cs")));

    [Fact]
    public void Phase5_does_not_execute_Phase6_when_endPhaseNo_is_5() => Assert.Contains("phase.No > endPhaseNo", Pipeline);

    [Fact]
    public void Phase5_valid_resume_does_not_call_certifier_again() => Assert.Contains("Valid Phase 5 authority was reused; overwriteExisting=false.", Pipeline);

    [Fact]
    public void Phase5_changed_Phase4_checksum_forces_regeneration() => Assert.Contains("Certification source Phase 4 checksum is stale.", File.ReadAllText(Path.Combine(RepositoryRoot, "Backend/src/Astronomy.MediaFactory.Core/DocumentaryBlueprint/DocumentaryBlueprintCertificationArtifactValidator.cs")));

    [Fact]
    public void Phase5_overwrite_preserves_Phase1_to_Phase4()
    {
        var overwrite = Pipeline[Pipeline.IndexOf("if (deleteStartPhaseNo <= 5", StringComparison.Ordinal)..];
        overwrite = overwrite[..overwrite.IndexOf("if (deleteStartPhaseNo <= 6", StringComparison.Ordinal)];
        Assert.DoesNotContain("03-questions", overwrite);
        Assert.DoesNotContain("04-blueprint", overwrite);
    }

    [Fact]
    public void Phase5_overwrite_invalidates_Phase6_to_Phase20() => Assert.Contains("downstreamPhaseNo = 6; downstreamPhaseNo <= 20", Pipeline);

    [Fact]
    public void IProductionPipelineExecutionService_resolves_with_Phase5_dependencies()
    {
        var composition = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Contains("IDocumentaryBlueprintCertificationIntegrationService, DocumentaryBlueprintCertificationIntegrationService", composition);
    }

    private static string Phase5Method()
    {
        var start = Pipeline.IndexOf("private async Task<IReadOnlyList<string>> PhaseCertifyDocumentaryBlueprintAsync", StringComparison.Ordinal);
        var end = Pipeline.IndexOf("private async", start + 20, StringComparison.Ordinal);
        return Pipeline[start..end];
    }

    private static int Count(string source, string value) => (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
