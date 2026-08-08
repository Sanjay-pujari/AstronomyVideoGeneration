using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase11CertificationPropagationTests
{
    private static readonly string ExecutionSource = File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
    private static readonly string RegistrySource = File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("Persistence", "Phase1Authority.cs"));

    [Fact] public void SuccessfulPhase11PropagatesAuthorityChecksum() =>
        Assert.Contains("authorityChecksum = phase11Certification?.ManifestChecksum", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesPublicationCommitted() =>
        Assert.Contains("PublicationCommitted = phase11Certification?.PublicationCommitted", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesManifestValidationStatus() =>
        Assert.Contains("manifestValidationStatus = phase11Certification?.ManifestValidationStatus", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesValidationStatus() =>
        Assert.Contains("validationStatus = phase11Certification?.ValidationStatus", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesSemanticValidation() =>
        Assert.Contains("semanticValidationPassed = phase11Certification?.SemanticValidationPassed", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesChecksumValidation() =>
        Assert.Contains("checksumValidationPassed = phase11Certification?.ChecksumValidationPassed", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesCommittedStateValidation() =>
        Assert.Contains("CommittedStateValidationPassed = phase11Certification?.CommittedStateValidationPassed", ExecutionSource);
    [Fact] public void SuccessfulPhase11PropagatesDownstreamReady() =>
        Assert.Contains("downstreamReady = phase11Certification?.DownstreamReady", ExecutionSource);
    [Fact] public void SuccessfulPhase11PopulatesHeroAuthorityDiagnostics() =>
        Assert.Contains("phase11Certification?.HeroAuthorityDiagnostics", ExecutionSource);
    [Fact] public void GeneratedTrueForSuccessfulResponsiveHeroAuthority() =>
        Assert.Contains("generated = phase11Certification is not null ? true", ExecutionSource);

    [Fact] public void Phase11Owns11HeroRoot() =>
        Assert.Contains("Add(11,Path.Combine(root,\"11-hero\"),canDeleteOnOverwrite:false)", RegistrySource);
    [Fact] public void Phase11DoesNotTreat11HeroAsUpstream() => Phase11Owns11HeroRoot();
    [Fact] public void Phase11OverwriteCanReplace11Hero() =>
        Assert.Contains("publisher owns transactional replacement of 11-hero", RegistrySource);
    [Fact] public void Phase11CannotDeletePhase10() => AssertUpstreamRootOwnedByPhase(10, "10-scene-validation");
    [Fact] public void Phase11CannotDeletePhase9() => AssertUpstreamRootOwnedByPhase(9, "09-long-scenes");
    [Fact] public void Phase11CannotDeletePhase8() => AssertUpstreamRootOwnedByPhase(8, "08-scene-assets");
    [Fact] public void LegacyHeroRootIsCompatibilityOnly() =>
        Assert.Contains("Add(11,context.ExecutionContext.HeroRoot,compatibility:true)", RegistrySource);
    [Fact] public void StagingRemovedAfterSuccessfulCommit() =>
        Assert.Contains("Directory.Move(staging, root)", File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ResponsiveHeroAuthorityService.cs")));

    private static void AssertUpstreamRootOwnedByPhase(int phase, string root) =>
        Assert.Contains($"Add({phase},Path.Combine(root,\"{root}\"))", RegistrySource);
}
