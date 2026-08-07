using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase8AuthorityArchitectureTests
{
    [Fact]
    public void AuthorityRequest_PreservesExactLineageAndAcceptedNarration()
    {
        var properties = typeof(Phase8SceneRequirement).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.Contains(nameof(Phase8SceneRequirement.SceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.BlueprintSceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.StoryFrameId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.AcceptedNarrationSceneId), properties);
        Assert.Contains(nameof(Phase8SceneRequirement.KnowledgeReferenceIds), properties);
    }

    [Fact]
    public void ProductionAuthorityPath_DoesNotOwnLegacyFiveOrNineCounts()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async", "private async Task<StoryFrameV4ComparisonExecutionResult>");
        Assert.Contains("authority.ShortScenes.Count", method);
        Assert.Contains("authority.LongScenes.Count", method);
        Assert.DoesNotContain("GetExpectedSceneIds", method);
        Assert.DoesNotContain("question-driven-narration-v2", method);
    }

    [Fact]
    public void AuthorityDrivenService_DoesNotLoadLegacyTimelineContext()
    {
        var source = ReadInfrastructure("SceneAssetsV3Service.cs");
        Assert.Contains("request.AuthorityInput is { } authority", source);
        Assert.Contains("BuildAuthorityBeats(context, scenes)", source);
        Assert.Contains("07-narration/accepted-release-candidate.json", source);
    }

    [Fact]
    public void Loader_UsesOnlyCommittedPhaseFourSixSevenPaths()
    {
        var source = ReadInfrastructure("Phase8AuthorityLoader.cs");
        Assert.Contains("04-blueprint", source);
        Assert.Contains("06-story-frames", source);
        Assert.Contains("accepted-release-candidate.json", source);
        Assert.DoesNotContain("narration-v5", source.ToLowerInvariant());
        Assert.DoesNotContain("question-driven-narration-v2", source.ToLowerInvariant());
    }

    [Fact]
    public void ManifestCarriesAuthorityProviderPhysicalAndSemanticEvidence()
    {
        var properties = typeof(SceneAssetManifestItem).GetProperties().Select(x => x.Name).ToHashSet();
        foreach (var name in new[] { "ProviderType", "ProviderStatus", "SourceInstructionId", "SourceKnowledgeReferenceIds", "Checksum", "SemanticIdentity", "Reused", "ProviderCalledThisExecution" })
            Assert.Contains(name, properties);
    }

    [Fact]
    public void PhaseNineConsumesCommittedLongAuthorityBeforeGeneration()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "PhaseValidateLongSceneImagesAsync", "PhaseValidateSceneAssetsAsync");
        Assert.Contains("scene-asset-manifest.json", method);
        Assert.Contains("PublicationState == \"Committed\"", method);
    }

    [Fact]
    public void PhaseTenUsesRequestedAuthorityVariants()
    {
        var source = ReadInfrastructure("ProductionPipelineExecutionService.cs");
        var method = Slice(source, "ValidateSceneAssetsV3WithDiagnosticsAsync", "PopulatePhase8FormatAwareDiagnostics");
        Assert.Contains("RequestedVariants.Contains(\"Short\"", method);
        Assert.Contains("RequestedVariants.Contains(\"Long\"", method);
    }

    private static string ReadInfrastructure(string name) => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", name));
    private static string Slice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal); var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0 && last > first); return source[first..last];
    }
}
