using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public interface INasaAssetDownloader
{
    Task<NasaImageDownloadResult> DownloadAsync(string sourceUrl, string targetPath, string providerName, CancellationToken cancellationToken);
}

public sealed class NasaAssetDownloader(HttpClient httpClient, ILogger<NasaAssetDownloader> logger) : INasaAssetDownloader
{
    private const long MaximumDownloadBytes = 50L * 1024L * 1024L;

    public async Task<NasaImageDownloadResult> DownloadAsync(string sourceUrl, string targetPath, string providerName, CancellationToken cancellationToken)
    {
        logger.LogInformation("{Provider}_IMAGE_DOWNLOAD_START sourceUrl={SourceUrl} targetPath={TargetPath}", providerName, sourceUrl, targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NASA image download returned HTTP {(int)response.StatusCode}.");

        var length = response.Content.Headers.ContentLength;
        if (length > MaximumDownloadBytes)
            throw new InvalidOperationException($"NASA image download exceeds maximum size of {MaximumDownloadBytes} bytes.");

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(targetPath))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        var info = new FileInfo(targetPath);
        logger.LogInformation("{Provider}_IMAGE_SAVED path={Path}", providerName, targetPath);
        if (info.Length > MaximumDownloadBytes)
        {
            File.Delete(targetPath);
            throw new InvalidOperationException($"NASA image download exceeded maximum size of {MaximumDownloadBytes} bytes.");
        }

        var dimensions = ImageDimensionReader.Read(targetPath);
        logger.LogInformation("{Provider}_IMAGE_DIMENSIONS_READ width={Width} height={Height} path={Path}", providerName, dimensions.Width, dimensions.Height, targetPath);
        logger.LogInformation("{Provider}_IMAGE_DOWNLOAD_COMPLETE sourceUrl={SourceUrl} path={Path} size={Size} width={Width} height={Height}", providerName, sourceUrl, targetPath, info.Length, dimensions.Width, dimensions.Height);
        return new NasaImageDownloadResult(targetPath, info.Length, dimensions.Width, dimensions.Height, sourceUrl);
    }
}
