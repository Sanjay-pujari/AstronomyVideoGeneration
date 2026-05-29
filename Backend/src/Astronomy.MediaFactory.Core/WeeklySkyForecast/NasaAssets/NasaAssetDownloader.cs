using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public interface INasaAssetDownloader
{
    Task<NasaImageDownloadResult> DownloadAsync(string sourceUrl, string targetPath, CancellationToken cancellationToken);
}

public sealed class NasaAssetDownloader(HttpClient httpClient, ILogger<NasaAssetDownloader> logger) : INasaAssetDownloader
{
    private const long MaximumDownloadBytes = 50L * 1024L * 1024L;

    public async Task<NasaImageDownloadResult> DownloadAsync(string sourceUrl, string targetPath, CancellationToken cancellationToken)
    {
        logger.LogInformation("NASA_IMAGE_DOWNLOAD_START sourceUrl={SourceUrl} targetPath={TargetPath}", sourceUrl, targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NASA image download returned HTTP {(int)response.StatusCode}.");

        var length = response.Content.Headers.ContentLength;
        if (length > MaximumDownloadBytes)
            throw new InvalidOperationException($"NASA image download exceeds maximum size of {MaximumDownloadBytes} bytes.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
        var info = new FileInfo(targetPath);
        if (info.Length > MaximumDownloadBytes)
        {
            File.Delete(targetPath);
            throw new InvalidOperationException($"NASA image download exceeded maximum size of {MaximumDownloadBytes} bytes.");
        }

        var dimensions = ImageDimensionReader.Read(targetPath);
        logger.LogInformation("NASA_IMAGE_DOWNLOAD_COMPLETE sourceUrl={SourceUrl} path={Path} size={Size} width={Width} height={Height}", sourceUrl, targetPath, info.Length, dimensions.Width, dimensions.Height);
        return new NasaImageDownloadResult(targetPath, info.Length, dimensions.Width, dimensions.Height, sourceUrl);
    }
}
