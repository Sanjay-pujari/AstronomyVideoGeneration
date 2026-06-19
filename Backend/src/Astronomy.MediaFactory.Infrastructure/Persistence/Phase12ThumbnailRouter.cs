using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ThumbnailV8AiNativeRenderer;

public static class Phase12ThumbnailRouter
{
    public static bool IsThumbnailV8Enabled(ThumbnailOptions? options, ThumbnailAssetGenerationRequest? request)
        => string.Equals(options?.ThumbnailVersion, "V8", StringComparison.OrdinalIgnoreCase)
            || options?.UseThumbnailV8 == true
            || options?.UseV8AiNative == true
            || request?.EnableThumbnailV8 == true;

    public static async Task<ThumbnailAssetGenerationResponse?> RouteAsync(
        ThumbnailOptions? options,
        ThumbnailAssetGenerationRequest request,
        Func<Task<ThumbnailAssetGenerationResponse>> renderV8Async,
        Func<Task<ThumbnailAssetGenerationResponse>> renderV7Async,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsThumbnailV8Enabled(options, request))
        {
            Console.WriteLine("PHASE 12 THUMBNAIL ROUTER: V8 selected");
            var response = await renderV8Async();
            EnsureNoV7Selection(response);
            return response;
        }

        if (options?.EnableThumbnailV7 != false)
            return await renderV7Async();

        return null;
    }

    private static void EnsureNoV7Selection(ThumbnailAssetGenerationResponse response)
    {
        var selected = new[]
        {
            response.RequestedRenderer,
            response.ActualRendererUsed,
            response.OutputWriteSource,
            response.RendererSelectionReason,
            response.ThumbnailVisualSourceMode
        };

        if (selected.Any(value => value?.Contains("V7", StringComparison.OrdinalIgnoreCase) == true))
            throw new InvalidOperationException("Thumbnail V8 routing violation: selected renderer/template/layout contains V7.");
    }
}
