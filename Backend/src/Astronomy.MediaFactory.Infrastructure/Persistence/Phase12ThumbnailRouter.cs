using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ThumbnailV9AiFinalThumbnailComposer;

public static class Phase12ThumbnailRouter
{
    private const bool Phase12ThumbnailV9DefaultEnabled = true;

    public static bool IsThumbnailV9Enabled(ThumbnailOptions? options, ThumbnailAssetGenerationRequest? request)
        => request?.EnableThumbnailV8 == true
            || options?.UseThumbnailV8 == true
            || options?.UseV8AiNative == true
            || string.Equals(options?.ThumbnailVersion, "V9", StringComparison.OrdinalIgnoreCase)
            || Phase12ThumbnailV9DefaultEnabled;


    public static bool IsThumbnailV8Enabled(ThumbnailOptions? options, ThumbnailAssetGenerationRequest? request)
        => IsThumbnailV9Enabled(options, request);

    public static async Task<ThumbnailAssetGenerationResponse?> RouteAsync(
        ThumbnailOptions? options,
        ThumbnailAssetGenerationRequest request,
        Func<Task<ThumbnailAssetGenerationResponse>> renderV8Async,
        Func<Task<ThumbnailAssetGenerationResponse>> renderV7Async,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsThumbnailV9Enabled(options, request))
        {
            Console.WriteLine("PHASE 12 THUMBNAIL ROUTER: V9 selected");
            var response = await renderV8Async();
            EnsureNoV7Selection(response);
            return response;
        }

        throw new InvalidOperationException("Phase 12 thumbnail routing failed: Thumbnail V9 is the default renderer and V7/V8 fallback is not allowed.");
    }

    private static void EnsureNoV7Selection(ThumbnailAssetGenerationResponse response)
    {
        var selected = new[]
        {
            response.RequestedRenderer,
            response.ActualRendererUsed,
            response.OutputWriteSource,
            response.RendererSelectionReason,
            response.ThumbnailVisualSourceMode,
            response.ThumbnailLayoutValidationPath
        };

        if (selected.Any(value => value?.Contains("V7", StringComparison.OrdinalIgnoreCase) == true))
            throw new InvalidOperationException("Thumbnail V9 routing violation: selected renderer/template/layout contains V7.");
    }
}
