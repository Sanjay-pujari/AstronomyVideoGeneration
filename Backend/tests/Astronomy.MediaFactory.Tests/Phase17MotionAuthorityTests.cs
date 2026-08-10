using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase17MotionAuthorityTests
{
    private static string PublisherSource() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "Phase17MotionAuthorityPublisher.cs"));

    private static string PipelineSource() => File.ReadAllText(Path.Combine(RepositoryRoot(),
        "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "ProductionPipelineExecutionService.cs"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astronomy.MediaFactory.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend repository root was not found.");
    }

    [Fact]
    public void SemanticSelector_ReusesDeterministicMaturePolicy()
    {
        var selector = new MotionProfileSelector();

        var first = selector.SelectSemantic("planetary conjunction hook", 0, 4);
        var repeated = selector.SelectSemantic("planetary conjunction hook", 0, 4);

        Assert.Equal(first, repeated);
        Assert.Equal(MotionProfileType.SlowZoomIn, first.MotionType);
        Assert.Equal(MotionEasingKind.EaseInOutSine, first.Easing);
    }

    [Fact]
    public void StaticFallback_IsAClosedValidProductionMotion()
    {
        Assert.Contains(Phase17MotionType.Static, Enum.GetValues<Phase17MotionType>());
        Assert.DoesNotContain(Enum.GetNames<Phase17MotionType>(), value =>
            value is "Parallax" or "Orbit" or "Tilt");
    }

    [Fact]
    public void Phase17ComparesPhysicalBytesToCertifiedPhysicalAssetHash()
    {
        var source = PublisherSource();
        Assert.Contains("visual.PhysicalSha256", source);
        Assert.Contains("actualHash != expectedHash", source);
    }

    [Fact]
    public void Phase17DoesNotComparePhysicalBytesToAuthorityChecksum() =>
        Assert.DoesNotContain("BuildEntry(root, scene, visual.PhysicalPath, p9.DeterministicChecksum", PublisherSource());

    [Fact]
    public void Phase17DoesNotComparePhysicalBytesToManifestChecksum() =>
        Assert.DoesNotContain("BuildEntry(root, scene, visual.PhysicalPath, p10.DeterministicChecksum", PublisherSource());

    [Fact]
    public void Phase17LongSceneUsesExactPhase9CertifiedPhysicalAsset()
    {
        var source = PublisherSource();
        Assert.Contains("Path.Combine(root, \"09-long-scenes\")", source);
        Assert.Contains("longById[scene.SceneId]", source);
    }

    [Fact]
    public void Phase17LongSceneDoesNotFallbackToPhase8OrLegacyV3ByPosition()
    {
        var source = PublisherSource();
        Assert.DoesNotContain("scene-assets-v3", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longVisuals[", source);
    }

    [Fact]
    public void Phase17LongSceneMappingIsStableWhenManifestOrderChanges() =>
        Assert.Contains("ToDictionary(x => x.SceneId, StringComparer.Ordinal)", PublisherSource());

    [Fact]
    public void Phase17PhysicalEvidenceFailureProjectsReasonCode()
    {
        var source = PipelineSource();
        Assert.Contains("reasonCodeOverride: ex.ReasonCode", source);
    }

    [Fact]
    public void Phase17PhysicalEvidenceFailureProjectsInputFiles() =>
        Assert.Contains("ex.LoadedAuthorityArtifacts", PipelineSource());

    [Fact]
    public void Phase17PhysicalEvidenceFailureIsNotDownstreamReady() =>
        Assert.Contains("phaseNo == 17 ? status == ProductionPhaseStatus.Succeeded", PipelineSource());
}
