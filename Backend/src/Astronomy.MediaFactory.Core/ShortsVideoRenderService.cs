using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class ShortsVideoRenderService : IShortsVideoRenderService
{
    private readonly IShortsScriptGenerationService _shortsScriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IAzureBlobStorageService _blobStorageService;
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly ILogger<ShortsVideoRenderService> _logger;
    private readonly IAnalyticsFeedbackProvider? _analyticsFeedbackProvider;

    public ShortsVideoRenderService(
        IShortsScriptGenerationService shortsScriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVisualAssetProvider visualAssetProvider,
        IVideoRenderService videoRenderService,
        IAzureBlobStorageService blobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IMetadataOptimizationService metadataOptimizationService,
        IOptions<YouTubeOptions> youTubeOptions,
        ILogger<ShortsVideoRenderService> logger,
        IAnalyticsFeedbackProvider? analyticsFeedbackProvider = null)
    {
        _shortsScriptGenerationService = shortsScriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _visualAssetProvider = visualAssetProvider;
        _videoRenderService = videoRenderService;
        _blobStorageService = blobStorageService;
        _youTubePublishingService = youTubePublishingService;
        _metadataOptimizationService = metadataOptimizationService;
        _youTubeOptions = youTubeOptions.Value;
        _logger = logger;
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
    }

    public async Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
    {
        var shortScript = await _shortsScriptGenerationService.GenerateShortAsync(contentType, context, cancellationToken);
        var feedbackSignals = _analyticsFeedbackProvider is null
            ? new FeedbackSignals()
            : await _analyticsFeedbackProvider.GetSignalsAsync(10, cancellationToken);
        var sourceHook = feedbackSignals.BestHooks.FirstOrDefault() ?? shortScript.Hook;
        var optimizedMetadata = await _metadataOptimizationService.OptimizeForShortAsync(new MetadataOptimizationInput
        {
            ContentType = contentType,
            Context = context,
            SourceTitle = shortScript.Title,
            SourceDescription = shortScript.ShortScript,
            SourceTags = shortScript.Tags,
            SourceScript = shortScript.ShortScript,
            SourceHookLine = sourceHook,
            FeedbackKeywords = feedbackSignals.TopKeywords
        }, cancellationToken);
        shortScript = new ShortScriptResult
        {
            Hook = shortScript.Hook,
            ShortScript = shortScript.ShortScript,
            Title = shortScript.Title,
            Tags = shortScript.Tags,
            EstimatedDurationSeconds = shortScript.EstimatedDurationSeconds,
            OptimizedMetadata = optimizedMetadata
        };
        var scriptBody = $"{shortScript.OptimizedMetadata?.HookLine ?? shortScript.Hook} {shortScript.ShortScript}";
        var shortAudioPath = await _speechSynthesisService.SynthesizeAsync(scriptBody, outputDirectory, cancellationToken);

        var visualCandidates = sourceVisuals.Where(File.Exists).ToList();
        if (visualCandidates.Count == 0)
        {
            var generatedVisuals = await _visualAssetProvider.PrepareVisualsAsync(context, outputDirectory, cancellationToken);
            visualCandidates.AddRange(generatedVisuals.Where(File.Exists));
        }

        var totalScenes = Math.Max(1, visualCandidates.Count);
        var perScene = Math.Max(3, shortScript.EstimatedDurationSeconds / totalScenes);
        var shortVideoPath = Path.Combine(outputDirectory, "short-video.mp4");
        var manifest = new RenderManifest
        {
            Title = shortScript.OptimizedMetadata?.PrimaryTitle ?? shortScript.Title,
            AudioPath = shortAudioPath,
            OutputPath = shortVideoPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            EnableVerticalCrop = true,
            Scenes = visualCandidates.Select((v, index) => new RenderScene
            {
                Caption = index == 0 ? shortScript.Hook : $"Quick fact #{index + 1}",
                VisualPath = v,
                DurationSeconds = perScene
            }).ToList()
        };

        var videoPath = await _videoRenderService.RenderAsync(manifest, cancellationToken);

        string? blobUrl = null;
        try
        {
            var blobResult = await _blobStorageService.UploadAsync(new BlobUploadRequest
            {
                BasePath = $"shorts/{contentType}/{context.Date:yyyy-MM-dd}",
                VideoPath = videoPath,
                AudioPath = shortAudioPath,
                ThumbnailPath = visualCandidates.FirstOrDefault()
            }, cancellationToken);
            blobUrl = blobResult.VideoUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shorts blob upload failed. Continuing with local artifacts.");
        }

        string? youtubeVideoId = null;
        var publishStatus = publishToYouTube ? "Published" : "Skipped";
        if (publishToYouTube)
        {
            try
            {
                var chosenTitle = shortScript.OptimizedMetadata?.PrimaryTitle ?? shortScript.Title;
                var title = chosenTitle.Length > 90 ? chosenTitle[..90] : chosenTitle;
                var tags = (shortScript.OptimizedMetadata?.Tags ?? shortScript.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (!tags.Contains("shorts", StringComparer.OrdinalIgnoreCase)) tags.Add("shorts");
                youtubeVideoId = await _youTubePublishingService.UploadAsync(videoPath, title, shortScript.OptimizedMetadata?.OptimizedDescription ?? shortScript.ShortScript, tags.ToArray(), _youTubeOptions.PrivacyStatus, cancellationToken);
                if (string.IsNullOrWhiteSpace(youtubeVideoId))
                {
                    publishStatus = "UploadFailed";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shorts YouTube upload failed.");
                publishStatus = "UploadFailed";
            }
        }

        return new ShortVideoRenderResult
        {
            Script = shortScript,
            AudioPath = shortAudioPath,
            VideoPath = videoPath,
            BlobUrl = blobUrl,
            YouTubeVideoId = youtubeVideoId,
            PublishStatus = publishStatus
        };
    }
}
