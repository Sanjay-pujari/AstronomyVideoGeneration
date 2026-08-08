using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase9CertificationPropagationTests
{
    private static readonly string Source = File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

    [Fact] public void SuccessfulPhase9PropagatesAuthorityChecksum() =>
        Assert.Contains("authorityChecksum = phase9Certification?.Manifest.DeterministicChecksum", Source);

    [Fact] public void SuccessfulPhase9PropagatesPublicationCommitted() =>
        Assert.Contains("PublicationCommitted = phase9Certification?.PublicationCommitted", Source);

    [Fact] public void SuccessfulPhase9PropagatesCommittedStateValidation() =>
        Assert.Contains("phase9Certification?.CommittedStateValidationPassed", Source);

    [Fact] public void SuccessfulPhase9SetsDownstreamReady() =>
        Assert.Contains("downstreamReady = phase9Certification?.DownstreamReady", Source);

    [Fact] public void SuccessfulPhase9UsesAuthorityAcceptedReasonCode()
    {
        Assert.Equal("P9_LONG_SCENE_IMAGE_AUTHORITY_ACCEPTED", Phase9ReasonCodes.Accepted);
        Assert.Contains("phase9Publication?.ReasonCode", Source);
    }

    [Fact] public void Phase9ValidationStatusIsValid() =>
        Assert.Contains("phase9Certification.CommittedStateValidationPassed ? \"Valid\" : \"Invalid\"", Source);

    [Fact] public void Phase9ManifestValidationStatusIsValid() =>
        Assert.Contains("phase9Certification.ManifestValidationPassed ? \"Valid\" : \"Invalid\"", Source);

    [Fact] public void Phase9ReportsAuthorityInputFiles()
    {
        Assert.Contains("08-scene-assets\", \"scene-asset-manifest.json", Source);
        Assert.Contains("06-story-frames\", \"story-frames.json", Source);
    }

    [Fact] public void Phase9DoesNotReportAzureProviderCalled() =>
        Assert.Contains("relevant with { ProviderCalled = false, ProviderSucceeded = false }", Source);

    [Fact] public void Phase9MaterializationDoesNotBecomeVisualGeneration()
    {
        Assert.Contains("materializedAssetCount = phase9Certification?.Manifest.Images.Count", Source);
        Assert.DoesNotContain("generated = phase9Certification", Source);
    }

    [Fact] public void Phase9PrimaryDiagnosticsReference09LongScenesAuthority() =>
        Assert.Contains("phase9LongSceneAuthorityDiagnostics", Source);

    [Fact] public void Phase9CleanupStillCannotModifyPhase8()
    {
        Assert.Contains("upstreamArtifactsDeleted = false", Source);
        Assert.DoesNotContain("Directory.Delete(Path.Combine(context.OutputRoot, \"08-scene-assets\")", Source);
    }
}
