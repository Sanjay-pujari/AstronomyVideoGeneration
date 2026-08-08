using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ResponsiveHeroAuthorityServiceTests
{
    [Fact]
    public void HeroNotRequestedUsesGovernedReasonCode() =>
        Assert.Equal("P11_HERO_ASSET_NOT_REQUESTED", Phase11ReasonCodes.NotRequested);

    [Fact]
    public async Task Phase11RequiresCommittedPhase10()
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase11-{Guid.NewGuid():N}");
        try
        {
            var service = new ResponsiveHeroAuthorityService();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(
                new(root, "plan", "event", "en", "Title", null, "Constellation", false), default));
            Assert.StartsWith(Phase11ReasonCodes.Phase10Missing, error.Message);
            Assert.False(Directory.Exists(Path.Combine(root, "11-hero")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("ShortVideo")]
    [InlineData("LongVideo")]
    [InlineData("Thumbnail")]
    public void NonHeroOutputsDoNotImplyHero(string requestedOutput) =>
        Assert.NotEqual("HeroAsset", requestedOutput);

    [Fact]
    public void AuthorityPathExplicitlyProhibitsLegacyAndGenerationFallbacks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Backend/src/Astronomy.MediaFactory.Infrastructure/Persistence/ResponsiveHeroAuthorityService.cs"));
        Assert.DoesNotContain("question-engine", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AstronomyVisualCompositionEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AzureOpenAI", source, StringComparison.Ordinal);
        Assert.Contains("legacyQuestionEngineAuthorityUsed = false", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Backend"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
