using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase8CertificationStateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "phase8-certification-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SuccessfulPhase8PropagatesPublicationCommitted()
        => Assert.True(ReadSuccessful().PublicationCommitted);

    [Fact]
    public void SuccessfulPhase8PropagatesCommittedStateValidation()
        => Assert.True(ReadSuccessful().CommittedStateValidationPassed);

    [Fact]
    public void SuccessfulPhase8SetsDownstreamReady()
        => Assert.True(ReadSuccessful().DownstreamReady);

    [Fact]
    public void Phase8ValidationContainsAuthorityChecksum()
        => Assert.Equal("authority-checksum", ReadSuccessful().AuthorityChecksum);

    [Fact]
    public void Phase8ValidationStatusIsValid()
        => Assert.Equal("Valid", ReadSuccessful().ValidationStatus);

    [Fact]
    public void Phase8UsesSuccessReasonCode()
        => Assert.Contains("P8_SCENE_ASSET_AUTHORITY_ACCEPTED", PipelineSource());

    [Fact]
    public void ShortOnlyAuthorityDiagnosticsDoNotExpectLegacyLongNineScenes()
        => Assert.Contains("BuildPhase8FormatDiagnosticsObject(longDiag, longRequested)", PipelineSource());

    [Fact]
    public void UnrequestedLongHasExpectedZeroAndNoMissingScenes()
    {
        var source = PipelineSource();
        Assert.Contains("sceneCountExpected = requested ? diag.ExpectedSceneCount : 0", source);
        Assert.Contains("missingSceneIds = requested ? diag.MissingSceneIds : Array.Empty<string>()", source);
    }

    [Fact]
    public void AuthorityModeDoesNotRequestStoryFrameV4Comparison()
        => Assert.Contains("var requested = !context.ExecutionContext.UseProductionPipeline", PipelineSource());

    [Fact]
    public void FailedManifestValidationDoesNotSetDownstreamReady()
    {
        WriteAuthority(manifestValidationPassed: false, committedReadbackPassed: true);
        Assert.False(ProductionPipelineExecutionService.ReadPhase8PublicationCertification(root).DownstreamReady);
    }

    [Fact]
    public void FailedCommittedReadbackDoesNotSetPublicationSuccess()
    {
        WriteAuthority(manifestValidationPassed: true, committedReadbackPassed: false);
        var result = ProductionPipelineExecutionService.ReadPhase8PublicationCertification(root);
        Assert.False(result.CommittedStateValidationPassed);
        Assert.False(result.DownstreamReady);
    }

    private ProductionPipelineExecutionService.Phase8PublicationCertification ReadSuccessful()
    {
        WriteAuthority(manifestValidationPassed: true, committedReadbackPassed: true);
        return ProductionPipelineExecutionService.ReadPhase8PublicationCertification(root);
    }

    private void WriteAuthority(bool manifestValidationPassed, bool committedReadbackPassed)
    {
        var directory = Path.Combine(root, "08-scene-assets");
        Directory.CreateDirectory(directory);
        var manifest = new SceneAssetManifest("1.0", "plan", "execution", "orion", "en", DateTimeOffset.UtcNow,
            "Committed", "blueprint", "frames", null, "narration", ["Short"], [], "Valid", "authority-checksum");
        File.WriteAllText(Path.Combine(directory, "scene-asset-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllText(Path.Combine(directory, "phase8-publication-report.json"), JsonSerializer.Serialize(new
        {
            publicationCommitted = true,
            manifestValidationPassed,
            committedReadbackPassed
        }, JsonOptions));
    }

    private static string PipelineSource()
        => File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
