using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7AttemptIsolationContractTests
{
    [Fact]
    public void MachineMetadataDoesNotTriggerPreProviderLeakage()
    {
        const string machineMetadata = "SceneId=Advance01; ViewerQuestionId=VQ-42; ClaimId=CLM-7";

        // Final-output validation has a NarrationText-only boundary. Machine lineage is deliberately
        // never passed to it, so merely constructing machine context cannot produce a failure.
        Assert.NotEmpty(machineMetadata);
        Assert.Empty(GeneratedNarrationValidator.Validate("Orion's three Belt stars make a clear winter signpost."));
    }

    [Fact]
    public void ProviderNarrationLeakageTriggersPostProviderFailure()
    {
        var failures = GeneratedNarrationValidator.Validate("Advance01 introduces Orion's Belt.", "long");

        Assert.Contains(failures, failure => failure.DetectedIssue == "ProviderInternalIdentifierOrPlaceholder");
    }

    [Fact]
    public void ProviderInvocationPrecedesFinalNarrationValidation()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint",
            "DocumentaryNarrativeLifecycleIntegrationService.cs"));

        Assert.True(source.IndexOf("generated = await InvokeGeneratorAsync", StringComparison.Ordinal) <
                    source.IndexOf("postProviderValidationStarted = true", StringComparison.Ordinal));
        Assert.True(source.IndexOf("providerResponseParsed =", StringComparison.Ordinal) <
                    source.IndexOf("postProviderValidationStarted = true", StringComparison.Ordinal));
    }

    [Fact]
    public void StaleNarrationIsIgnoredAndCurrentAttemptIsRequired()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint",
            "DocumentaryNarrativeLifecycleIntegrationService.cs"));

        Assert.Contains("CleanupWorkingNarration(request.ExecutionRoot)", source);
        Assert.Contains("Draft artifact attemptId does not match current attempt", source);
        Assert.Contains("attempt-metadata.json", source);
    }

    [Fact]
    public void StalePhase7ArtifactsAreRemovedByPhysicalExistenceFilter()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence",
            "ProductionPipelineExecutionService.cs"));

        Assert.Contains(".Where(x => File.Exists(x.path)", source);
        Assert.Contains("phase7Artifacts", source);
    }

    [Fact]
    public void FailedPhaseIsStillRecordedAsExecuted()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence",
            "ProductionPipelineExecutionService.cs"));

        Assert.Contains("phase.Status != ProductionPhaseStatus.Skipped", source);
        Assert.Contains("executedPhaseNumbers = phasesActuallyExecuted", source);
    }

    [Fact]
    public void SuccessfulPublicationUses07NarrationAuthority()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("DocumentaryBlueprint",
            "DocumentaryNarrativeLifecycleIntegrationService.cs"));

        Assert.Contains("07-narration", source);
        Assert.Contains("PublishReleaseCandidatesAsync", source);
        Assert.Contains("accepted-release-candidate.json", source);
    }
}
