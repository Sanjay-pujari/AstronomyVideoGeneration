using System.Runtime.Serialization;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
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
    public void Validator_UsesVariantSpecificCommittedSceneCollections()
    {
        var source = File.ReadAllText(RepositoryTestPaths.CoreSource(
            "DocumentaryBlueprint", "StoryFrameAuthorityContracts.cs"));
        Assert.Contains("request.LongScenes", source, StringComparison.Ordinal);
        Assert.Contains("request.ShortScenes", source, StringComparison.Ordinal);
        Assert.Contains("CommittedScenes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_DoesNotUseSharedEditorialSceneOrderForVariantMembership()
    {
        var source = File.ReadAllText(RepositoryTestPaths.CoreSource(
            "DocumentaryBlueprint", "StoryFrameAuthorityContracts.cs"));
        var index = source.IndexOf(
            "IReadOnlyList<CertifiedStoryFrameSceneAuthority> CommittedScenes",
            StringComparison.Ordinal);
        Assert.True(index >= 0);
        Assert.DoesNotContain("e.SceneOrder.Select", source[index..], StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsCrossVariantOrUnknownScenes()
    {
        var source = File.ReadAllText(RepositoryTestPaths.CoreSource(
            "DocumentaryBlueprint", "StoryFrameAuthorityContracts.cs"));
        Assert.Contains("uncertified or cross-variant scene", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_CanonicalOrderUsesCommittedVariantSequence()
    {
        var source = File.ReadAllText(RepositoryTestPaths.CoreSource(
            "DocumentaryBlueprint", "StoryFrameAuthorityContracts.cs"))
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains("CommittedScenes(v).OrderBy(x=>x.SequenceNumber)", source, StringComparison.Ordinal);
    }

    private static Phase6CommittedInputAuthority CreateAuthority(
        IReadOnlyList<string> requestedVariants,
        IReadOnlyList<string>? phase5Paths = null)
    {
#pragma warning disable SYSLIB0050
        var aggregate = (DocumentaryBlueprintAggregate)
            FormatterServices.GetUninitializedObject(typeof(DocumentaryBlueprintAggregate));
        var phase5 = (PublishedBlueprintCertification)
            FormatterServices.GetUninitializedObject(typeof(PublishedBlueprintCertification));
#pragma warning restore SYSLIB0050

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
}
