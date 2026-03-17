using Astronomy.MediaFactory.Core;
namespace Astronomy.MediaFactory.Publishing;
public sealed class YouTubePublishingService : IYouTubePublishingService
{
    public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, CancellationToken cancellationToken)
        => Task.FromResult<string?>($"yt-{Path.GetFileNameWithoutExtension(videoPath)}");
}
