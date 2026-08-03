namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgePipelineIsolationTests
{
    private static string PipelineSource => File.ReadAllText(
        RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

    [Fact]
    public void RetryFailedOnly_Phase7StillInvokesKnowledgeService()
    {
        Assert.Contains("phase.No is not (1 or 2 or 4 or 6 or 7)", PipelineSource, StringComparison.Ordinal);
        Assert.Contains("7 => await ExecutePhase7KnowledgeAsync(context,cancellationToken)", PipelineSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyNarrationExists_Phase7DoesNotUseGenericSkip()
    {
        var source = PipelineSource;
        Assert.Contains("7 => false,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("7 => File.Exists(BuildNarrationV5Path(context))", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("07-narration/knowledge")]
    [InlineData("validation/phase-07-knowledge-validation.json")]
    [InlineData("phase-manifest.json")]
    [InlineData(".phase-07-knowledge-publication.json")]
    public void Phase7OverwriteCleanup_DocumentsTransactionOwnedExclusions(string committedPath)
    {
        var source = PipelineSource;
        Assert.Contains(committedPath, source, StringComparison.Ordinal);
        Assert.Contains("IPhase7KnowledgeTransactionCoordinator", source, StringComparison.Ordinal);
    }
}
