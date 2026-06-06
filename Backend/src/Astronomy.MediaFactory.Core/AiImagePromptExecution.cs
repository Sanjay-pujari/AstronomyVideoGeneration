namespace Astronomy.MediaFactory.Core;

public interface IAiImagePromptExecutionService
{
    Task<AssetExecutionResult> ExecuteOptionalAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
