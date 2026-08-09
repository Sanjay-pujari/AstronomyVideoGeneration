using System.Security.Cryptography;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13PhysicalMetadataTests
{
    [Fact]
    public async Task GalleryRendererReturnsPhysicalMetadataForEveryPage()
    {
        var records = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => CreateAndRead(i)));
        Assert.Equal(6, records.Length);
        Assert.All(records, metadata => Assert.NotNull(metadata));
    }

    [Fact]
    public async Task GalleryPhysicalMetadataUsesDecodedDimensions()
    {
        var metadata = await CreateAndRead(1);
        Assert.Equal((1920, 1080, "PNG", "image/png"), (metadata.Width, metadata.Height, metadata.Format, metadata.MimeType));
    }

    [Fact]
    public async Task GalleryPhysicalMetadataContainsSha256()
    {
        var metadata = await CreateAndRead(1);
        Assert.Matches("^[0-9a-f]{64}$", metadata.PhysicalSha256);
    }

    [Fact]
    public async Task GalleryPhysicalMetadataContainsByteLength() => Assert.True((await CreateAndRead(1)).ByteLength > 0);

    [Fact]
    public void GalleryManifestUsesValidatedPhysicalMetadata()
    {
        var source = File.ReadAllText(SourcePath("src/Astronomy.MediaFactory.Rendering/Phase13GalleryAuthority.cs"));
        Assert.Contains("physicalMetadata = metadata", source);
        Assert.Contains("physicalSha256 = physical.PhysicalSha256", source);
    }

    [Fact]
    public void GalleryRejectsMissingGeneratedFileMetadata() =>
        Assert.ThrowsAsync<InvalidOperationException>(() => Phase13GalleryAuthority.ReadPhysicalMetadataAsync("missing.png", "13-gallery/gallery-01.png", default)).GetAwaiter().GetResult();

    [Fact]
    public async Task GalleryRejectsWrongPhysicalDimensions()
    {
        var path = Temp("wrong.png");
        using (var image = new Image<Rgba32>(10, 10)) await image.SaveAsPngAsync(path);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Phase13GalleryAuthority.ReadPhysicalMetadataAsync(path, "13-gallery/gallery-01.png", default));
        Assert.Contains("physical dimensions 10x10", ex.Message);
    }

    [Fact]
    public async Task GalleryRejectsEmptyPhysicalFile()
    {
        var path = Temp("empty.png");
        await File.WriteAllBytesAsync(path, []);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Phase13GalleryAuthority.ReadPhysicalMetadataAsync(path, "13-gallery/gallery-01.png", default));
        Assert.Contains("is empty", ex.Message);
    }

    [Theory]
    [InlineData("P13_GENERATED_FILE_METADATA_INVALID: page 3", "P13_GENERATED_FILE_METADATA_INVALID")]
    [InlineData("unstructured failure", "P13_GALLERY_EXECUTION_FAILED")]
    public void Phase13FailureReturnsStructuredReasonCode(string reason, string expected) =>
        Assert.Equal(expected, ProductionPipelineExecutionService.ResolvePhase13ReasonCode(ProductionPhaseStatus.Failed, reason));

    [Fact]
    public void Phase13ValidationFailureProducesPhaseExecutionResult() => AssertFailureMappingSource();
    [Fact]
    public void Phase13FailureReportsExecutedPhase13() => AssertFailureMappingSource();
    [Fact]
    public void Phase13FailureSetsLastFailedPhase13() => AssertFailureMappingSource();
    [Fact]
    public void Phase13FailureDoesNotEscapeBatchExecutionWithoutPhaseResult() => AssertFailureMappingSource();

    private static void AssertFailureMappingSource()
    {
        var source = File.ReadAllText(SourcePath("src/Astronomy.MediaFactory.Infrastructure/Persistence/ProductionPipelineExecutionService.cs"));
        Assert.Contains("phaseExecutionBegan: true", source);
        Assert.Contains("catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)", source);
        Assert.Contains("phaseResults.Add(result)", source);
    }

    private static async Task<Phase13GalleryAuthority.GeneratedFileMetadata> CreateAndRead(int slot)
    {
        var path = Temp($"gallery-{slot:00}.png");
        using (var image = new Image<Rgba32>(1920, 1080)) await image.SaveAsPngAsync(path);
        var metadata = await Phase13GalleryAuthority.ReadPhysicalMetadataAsync(path, $"13-gallery/gallery-{slot:00}.png", default);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(), metadata.PhysicalSha256);
        return metadata;
    }

    private static string Temp(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "phase13-metadata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, name);
    }

    private static string SourcePath(string relative) => Path.Combine(FindBackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astronomy.MediaFactory.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend root not found.");
    }
}
