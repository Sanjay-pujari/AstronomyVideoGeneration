using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFrameIntegrationCommittedInputTests
{
    [Fact]
    public void ArtifactPaths_LongOnly_ContainsLongProjectionAndNotShort()
    {
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(CreateAuthority(["Long"]));
        Assert.Contains("04-blueprint/documentary-blueprint-long.json", paths);
        Assert.DoesNotContain("04-blueprint/documentary-blueprint-short.json", paths);
    }

    [Fact]
    public void ArtifactPaths_ShortOnly_ContainsShortProjectionAndNotLong()
    {
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(CreateAuthority(["Short"]));
        Assert.Contains("04-blueprint/documentary-blueprint-short.json", paths);
        Assert.DoesNotContain("04-blueprint/documentary-blueprint-long.json", paths);
    }

    [Fact]
    public void ArtifactPaths_BothVariants_ContainsBothProjectionAuthorities()
    {
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(CreateAuthority(["Long", "Short"]));
        Assert.Contains("04-blueprint/documentary-blueprint-long.json", paths);
        Assert.Contains("04-blueprint/documentary-blueprint-short.json", paths);
    }

    [Fact]
    public void ArtifactPaths_ContainsCommittedPhase4AndPhase5Evidence()
    {
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(CreateAuthority(["Long"]));
        Assert.Contains("04-blueprint/documentary-blueprint-aggregate.json", paths);
        Assert.Contains("validation/phase-04-validation.json", paths);
        Assert.Contains("validation/phase-05-validation.json", paths);
        Assert.Contains("05-editorial/blueprint-certification.json", paths);
        Assert.Contains("05-editorial/editorial-contract.json", paths);
        Assert.Contains("phase-manifest.json", paths);
    }

    [Fact]
    public void ArtifactPaths_ExcludesOptionalCertificationDiagnostics()
    {
        var authority = CreateAuthority(["Long"],
            ["05-editorial/blueprint-certification.json", "05-editorial/certification-diagnostics.json"]);
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(authority);
        Assert.DoesNotContain(paths, path => path.EndsWith(
            "certification-diagnostics.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ArtifactPaths_ExcludesLegacyStoryGraph()
    {
        var authority = CreateAuthority(["Long"],
            ["05-editorial/blueprint-certification.json", "editorial/story-graph.json"]);
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(authority);
        Assert.DoesNotContain("editorial/story-graph.json", paths);
    }

    [Theory]
    [InlineData("/absolute/file.json")]
    [InlineData("C:\\absolute\\file.json")]
    [InlineData("../traversal/file.json")]
    [InlineData("folder\\backslash.json")]
    [InlineData(".phase-06-staging-x/file.json")]
    [InlineData(".phase-06-backup-x/file.json")]
    [InlineData("")]
    [InlineData(" ")]
    public void ArtifactPaths_ExcludesUnsafeOrTransactionOwnedPaths(string unsafePath)
    {
        var authority = CreateAuthority(["Long"],
            ["05-editorial/blueprint-certification.json", unsafePath]);
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(authority);
        Assert.DoesNotContain(unsafePath, paths);
    }

    [Fact]
    public void ArtifactPaths_AreDistinctAndOrdinallySorted()
    {
        var authority = CreateAuthority(["Long"],
            [
                "05-editorial/editorial-contract.json",
                "05-editorial/blueprint-certification.json",
                "05-editorial/editorial-contract.json"
            ]);
        var paths = StoryFrameCommittedInputDiagnostics.ArtifactPaths(authority);
        Assert.Equal(paths.Distinct(StringComparer.Ordinal), paths);
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
    }

    [Fact]
    public async Task BuildAsync_PassesCommittedAuthorityToBuilderAndReportsItsArtifactPaths()
    {
        var input = CreateAuthority(["Long"]);
        var builder = new RecordingBuilder();
        var service = new StoryFrameIntegrationService(builder);
        var compatibility = service.GetCompatibilityContext();
        var request = new StoryFrameIntegrationRequest(
            "execution", "plan", "event", "en", "profile", input,
            compatibility.CurrentBuilderType, compatibility.CurrentBuilderVersion,
            compatibility.CurrentIntegrationServiceType, compatibility.CurrentIntegrationServiceVersion);

        var result = await service.BuildAsync(request, CancellationToken.None);

        Assert.Equal(1, builder.CallCount);
        Assert.Same(input, builder.InputAuthority);
        Assert.Equal(StoryFrameCommittedInputDiagnostics.ArtifactPaths(input), result.Diagnostics.InputArtifactPaths);
    }

    private static Phase6CommittedInputAuthority CreateAuthority(
        IReadOnlyList<string> requestedVariants,
        IReadOnlyList<string>? phase5Paths = null)
    {
        var fixture = Phase5CertificationFixture.Create();
        var aggregate = fixture.PublishedPhase4;
        var phase5 = new PublishedBlueprintCertification(
            fixture.Result.Certification, fixture.Result.EditorialContract, fixture.Result.Validation,
            fixture.Result.SceneIntents, fixture.Result.Coverage, fixture.Result.Transitions,
            fixture.Result.PauseTest, aggregate.AggregateId, aggregate.DeterministicChecksum,
            "1.0", "published");

        var entries = (phase5Paths ??
            ["05-editorial/blueprint-certification.json", "05-editorial/editorial-contract.json"])
            .Select(Entry).ToArray();

        return new Phase6CommittedInputAuthority(
            aggregate, "aggregate-id", Sha('a'), Sha('b'), Sha('c'), "profile", "1.0",
            ["validation/phase-04-validation.json"], ["phase-manifest.json"],
            phase5, "certification-id", Sha('d'), "editorial-contract-id", Sha('e'),
            "phase5-publication-id", ["validation/phase-05-validation.json"], entries,
            true, ["Long", "Short"], requestedVariants, true, true, true, true, true,
            true, true, [], []);
    }

    private static Phase5ArtifactInventoryEntry Entry(string path) =>
        new(path, "Supporting", Sha('f'), Sha('0'), 1, Sha('a'));

    private static string Sha(char value) => new(value, 64);

    private sealed class RecordingBuilder : ICertifiedStoryFrameBuilder
    {
        public string BuilderType => "recording-builder";
        public string BuilderVersion => "1";
        public int CallCount { get; private set; }
        public Phase6CommittedInputAuthority? InputAuthority { get; private set; }

        public Task<IReadOnlyList<StoryFrameAuthorityFrame>> BuildAsync(
            Phase6CommittedInputAuthority inputAuthority,
            CancellationToken cancellationToken)
        {
            CallCount++;
            InputAuthority = inputAuthority;
            return Task.FromResult<IReadOnlyList<StoryFrameAuthorityFrame>>([]);
        }
    }
}
