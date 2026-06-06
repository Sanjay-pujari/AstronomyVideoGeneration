namespace Astronomy.MediaFactory.Core;

public interface ISkyMapCardExecutionService
{
    Task<AssetExecutionResult> ExecutePreferredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
