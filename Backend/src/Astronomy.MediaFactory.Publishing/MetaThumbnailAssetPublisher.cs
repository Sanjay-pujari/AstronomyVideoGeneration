using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Publishing;

public sealed class MetaThumbnailAssetPublisher : IMetaThumbnailAssetPublisher
{
    private readonly IPublicMediaStorageService _publicMediaStorageService;

    public MetaThumbnailAssetPublisher(IPublicMediaStorageService publicMediaStorageService)
    {
        _publicMediaStorageService = publicMediaStorageService;
    }

    public async Task<PublicMediaUploadResult> UploadThumbnailAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken)
    {
        // Reuse the public media storage path for Meta cover assets. The backing storage service
        // is responsible for returning a public HTTPS URL that the Graph API can read.
        return await _publicMediaStorageService.UploadPublicAssetAsync(localFilePath, pipelineRunId, "thumbnail-short.jpg", "image/jpeg", cancellationToken);
    }
}
