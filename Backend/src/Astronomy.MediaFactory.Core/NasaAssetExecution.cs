namespace Astronomy.MediaFactory.Core;

public interface INasaAssetExecutionService
{
    Task<AssetExecutionResult> ExecuteOptionalAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
