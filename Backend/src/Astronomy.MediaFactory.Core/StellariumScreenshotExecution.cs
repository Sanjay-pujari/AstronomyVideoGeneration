namespace Astronomy.MediaFactory.Core;

public interface IStellariumScreenshotExecutionService
{
    Task<AssetExecutionResult> ExecutePreferredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
