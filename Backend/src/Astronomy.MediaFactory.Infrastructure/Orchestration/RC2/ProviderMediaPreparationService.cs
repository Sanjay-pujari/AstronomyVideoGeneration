using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

/// <summary>Creates traceable provider derivatives without changing Phase 20 evidence.</summary>
public sealed class ProviderMediaPreparationService
{
    public const string InstagramFeedProfile = "meta-instagram-feed-1080x1350-jpeg-q90-v1";

    public async Task<PreparedProviderMedia> PrepareAsync(
        Guid planId, string publishingPackageId, string phase20AuthorityChecksum,
        Phase20PublishingArtifact source, string sourcePath, Rc2PublishingTarget target,
        string stagingRoot, CancellationToken cancellationToken = default)
    {
        if (target is not (Rc2PublishingTarget.InstagramPost or Rc2PublishingTarget.InstagramCarousel))
            throw new NotSupportedException($"No provider-media profile is registered for {target}.");

        var identityInput = $"{source.Sha256.ToLowerInvariant()}|{target}|{InstagramFeedProfile}";
        var derivativeId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput))).ToLowerInvariant();
        var directory = Path.Combine(stagingRoot, planId.ToString("D"), target.ToString());
        var outputPath = Path.Combine(directory, $"{derivativeId}.jpg");
        var metadataPath = outputPath + ".metadata.json";
        Directory.CreateDirectory(directory);

        if (!File.Exists(outputPath))
        {
            var temporaryPath = outputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using var image = await Image.LoadAsync(sourcePath, cancellationToken);
                image.Mutate(operation => operation.AutoOrient().Resize(new ResizeOptions
                {
                    Size = new Size(1080, 1350),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Lanczos3,
                    Compand = true
                }));
                image.Metadata.ExifProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IptcProfile = null;
                await image.SaveAsJpegAsync(temporaryPath, new JpegEncoder
                {
                    Quality = 90,
                    Interleaved = true,
                    ColorType = JpegEncodingColor.YCbCrRatio420
                }, cancellationToken);
                File.Move(temporaryPath, outputPath, false);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        await ValidateInstagramDerivativeAsync(outputPath, cancellationToken);
        await using var output = File.OpenRead(outputPath);
        var derivativeSha = Convert.ToHexString(await SHA256.HashDataAsync(output, cancellationToken)).ToLowerInvariant();
        var result = new PreparedProviderMedia(outputPath, metadataPath, derivativeId, derivativeSha,
            output.Length, 1080, 1350, "image/jpeg", InstagramFeedProfile, source.Sha256);
        var evidence = new ProviderMediaEvidence(planId, phase20AuthorityChecksum, publishingPackageId,
            source.Role, source.Sha256, target.ToString(), InstagramFeedProfile, derivativeId,
            derivativeSha, result.ByteLength, result.Width, result.Height, result.MimeType);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(evidence,
            new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return result;
    }

    public static async Task ValidateInstagramDerivativeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw Invalid();
        try
        {
            var info = await Image.IdentifyAsync(path, cancellationToken);
            var ratio = info is null || info.Height == 0 ? 0 : (double)info.Width / info.Height;
            if (info?.Metadata.DecodedImageFormat is not JpegFormat || info.Width is < 320 or > 1440 || ratio is < 0.8 or > 1.91)
                throw Invalid();
        }
        catch (UnknownImageFormatException) { throw Invalid(); }
    }

    private static Rc2PublishingControlException Invalid() => new("RC2_PUBLISH_PROVIDER_MEDIA_INVALID",
        "Prepared Instagram media must be a decodable JPEG between 320 and 1440 pixels wide with aspect ratio 4:5 through 1.91:1.");
}

public sealed record PreparedProviderMedia(string Path, string MetadataPath, string DerivativeId,
    string Sha256, long ByteLength, int Width, int Height, string MimeType, string ProfileVersion, string SourceSha256);

public sealed record ProviderMediaEvidence(Guid PlanId, string Phase20AuthorityChecksum,
    string PublishingPackageId, string SourceRole, string SourceSha256, string Target,
    string NormalizationProfileVersion, string DerivativeId, string DerivativeSha256,
    long ByteLength, int Width, int Height, string MimeType);
