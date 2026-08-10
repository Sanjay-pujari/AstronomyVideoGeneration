namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13MatureGalleryRoutingTests
{
    private static string Authority => Source("Phase13GalleryAuthority.cs");
    private static string Adapter => Source("MatureGalleryCandidateGenerator.cs");

    [Fact]
    public void Phase13RestoredPathUsesMatureGalleryPlanner()
    {
        Assert.Contains("MatureGalleryCandidateGenerator.BuildPlans(hydration)", Authority);
        Assert.Contains("AstroPulseGalleryService.BuildTopics(contract)", Adapter);
    }

    [Fact]
    public void Phase13RestoredPathDoesNotInvokeSquareGalleryRoleResolver()
    {
        var activeRoute = Authority[..Authority.IndexOf("internal static string BuildMatureGalleryPrompt", StringComparison.Ordinal)];
        Assert.DoesNotContain("ResolveRolePlan(roles", activeRoute);
        Assert.Contains("abandonedSquareRolePlannerActivated = false", activeRoute);
    }

    [Fact]
    public void Phase13RestoredPathUsesSixMatureGalleryRoles() => Assert.Contains(
        "[\"Opening view\", \"What happens\", \"Where to look\", \"When to observe\", \"Key objects\", \"Viewing checklist\"]", Authority);

    [Fact]
    public void Phase13DoesNotRequireIdentificationSemanticBucket() => AssertBucketDoesNotGate("IdentificationFacts");

    [Fact]
    public void Phase13DoesNotRequireDeepSkySemanticBucket() => AssertBucketDoesNotGate("DeepSkyFacts");

    [Fact]
    public void Phase13DoesNotRequireObservationSemanticBucketBeforeTopicPlanning() => AssertBucketDoesNotGate("ObservationFacts");

    [Fact]
    public void OrionWithoutDirectionOrTimingAdaptsMatureRoles()
    {
        Assert.Contains("How to recognize", Adapter);
        Assert.Contains("Evergreen observing context", Adapter);
        Assert.Contains("No certified local time or viewing window", Adapter);
    }

    private static void AssertBucketDoesNotGate(string bucket)
    {
        Assert.DoesNotContain(bucket, Adapter);
        Assert.Contains("GalleryContentResolver.Resolve(galleryContext)", Adapter);
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "Astronomy.MediaFactory.Rendering", file));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend repository root not found.");
    }
}
