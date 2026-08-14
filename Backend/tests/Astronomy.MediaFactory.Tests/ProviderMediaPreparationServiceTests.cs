using System.Security.Cryptography;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProviderMediaPreparationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"provider-media-{Guid.NewGuid():N}");

    [Fact]
    public async Task Instagram_portrait_is_normalized_without_modifying_governed_source_and_is_reused()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "phase20", "hero-portrait.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        using (var image = new Image<Rgba32>(1080, 1920, Color.MidnightBlue))
            await image.SaveAsync(sourcePath, new PngEncoder());
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var sourceSha = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var artifact = new Phase20PublishingArtifact("HeroPortrait", "hero-portrait.png",
            sourceBytes.Length, sourceSha, null);
        var service = new ProviderMediaPreparationService();
        var staging = Path.Combine(_root, "publishing-staging");

        var first = await service.PrepareAsync(Guid.Parse("baa5af31-4ba9-4d1d-8ef3-0796210a9ed2"),
            "package-20", "authority-checksum", artifact, sourcePath,
            Rc2PublishingTarget.InstagramPost, staging);
        var firstWrite = File.GetLastWriteTimeUtc(first.Path);
        var second = await service.PrepareAsync(Guid.Parse("baa5af31-4ba9-4d1d-8ef3-0796210a9ed2"),
            "package-20", "authority-checksum", artifact, sourcePath,
            Rc2PublishingTarget.InstagramPost, staging);

        Assert.Equal((1080, 1350, "image/jpeg"), (first.Width, first.Height, first.MimeType));
        Assert.True(first.ByteLength > 0);
        Assert.Equal(sourceSha, first.SourceSha256);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        Assert.Equal(first.DerivativeId, second.DerivativeId);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(second.Path));
        Assert.Contains("\"Phase20AuthorityChecksum\": \"authority-checksum\"", await File.ReadAllTextAsync(first.MetadataPath));
        await ProviderMediaPreparationService.ValidateInstagramDerivativeAsync(first.Path);
    }

    [Fact]
    public void Staging_is_outside_phase20_plan_tree()
    {
        var plan = Path.Combine(_root, "media-output", "plans", "plan", "phase20");
        var staging = Rc2PublishingExecutionService.FindPublishingStagingRoot(plan);
        Assert.Equal(Path.Combine(_root, "media-output", "publishing-staging"), staging);
        Assert.False(staging.StartsWith(Path.GetFullPath(plan) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
