namespace Astronomy.MediaFactory.Core;

public interface IConstellationGuideExecutionService
{
    Task<AssetExecutionResult> ExecutePreferredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
