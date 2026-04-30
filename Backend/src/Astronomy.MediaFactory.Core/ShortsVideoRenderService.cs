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
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly ILogger<ShortsVideoRenderService> _logger;
    private readonly IContentMonetizationService? _contentMonetizationService;
    private readonly IAnalyticsFeedbackProvider? _analyticsFeedbackProvider;
    private readonly IPromptFeedbackService? _promptFeedbackService;

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
        IContentMonetizationService? contentMonetizationService = null,
        IAnalyticsFeedbackProvider? analyticsFeedbackProvider = null,
        IPromptFeedbackService? promptFeedbackService = null)
    {
        _shortsScriptGenerationService = shortsScriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _visualAssetProvider = visualAssetProvider;
        _videoRenderService = videoRenderService;
        _blobStorageService = blobStorageService;
        _metadataOptimizationService = metadataOptimizationService;
        _logger = logger;
        _contentMonetizationService = contentMonetizationService;
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
        _promptFeedbackService = promptFeedbackService;
    }

    public async Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
    {
        if (_promptFeedbackService is not null)
        {
            context.PromptFeedbackContext = await _promptFeedbackService.BuildContextAsync(new PromptFeedbackRequest
            {
                ContentType = contentType,
                IsShortForm = true,
                TopicSelectionPlan = context.TopicSelectionPlan
            }, cancellationToken);
        }

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
            FeedbackKeywords = feedbackSignals.TopKeywords,
            FeedbackContext = context.PromptFeedbackContext
        }, cancellationToken);

        if (_contentMonetizationService is not null)
        {
            try
            {
                var monetizationPlan = await _contentMonetizationService.BuildPlanAsync(new MonetizationInput
                {
                    ContentType = contentType,
                    Context = context,
                    Metadata = optimizedMetadata,
                    AnalyticsFeedback = feedbackSignals,
                    IsShortForm = true
                }, cancellationToken);

                optimizedMetadata = new OptimizedVideoMetadata
                {
                    PrimaryTitle = optimizedMetadata.PrimaryTitle,
                    AlternateTitles = optimizedMetadata.AlternateTitles,
                    OptimizedDescription = string.IsNullOrWhiteSpace(monetizationPlan.FinalDescription) ? optimizedMetadata.OptimizedDescription : monetizationPlan.FinalDescription,
                    Tags = optimizedMetadata.Tags,
                    Hashtags = optimizedMetadata.Hashtags,
                    ThumbnailTextSuggestions = optimizedMetadata.ThumbnailTextSuggestions,
                    HookLine = optimizedMetadata.HookLine
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Short-form monetization generation failed. Continuing with optimized short metadata.");
            }
        }

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
        var segments = await BuildNarrationSegmentsAsync(scriptBody, outputDirectory, cancellationToken);
        var shortAudioPath = await _speechSynthesisService.SynthesizeAsync(scriptBody, outputDirectory, cancellationToken);

        var visualCandidates = sourceVisuals.Where(File.Exists).ToList();
        if (visualCandidates.Count == 0)
        {
            var generatedVisuals = await _visualAssetProvider.PrepareVisualsAsync(context, outputDirectory, cancellationToken);
            visualCandidates.AddRange(generatedVisuals.Where(File.Exists));
        }

        if (visualCandidates.Count == 0)
        {
            _logger.LogError("Short-form render failed because no visuals were available for {ContentType} on {Date}.", contentType, context.Date);
            throw new InvalidOperationException("Short-form rendering requires at least one visual asset, but none were available from the source list or fallback generator.");
        }

        var shortVideoPath = Path.Combine(outputDirectory, "short-video.mp4");
        var manifest = new RenderManifest
        {
            Title = shortScript.OptimizedMetadata?.PrimaryTitle ?? shortScript.Title,
            AudioPath = shortAudioPath,
            OutputPath = shortVideoPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            EnableVerticalCrop = true,
            Scenes = segments.Select((segment, index) => new RenderScene
            {
                Caption = segment.Text,
                VisualPath = visualCandidates[index % visualCandidates.Count],
                AudioPath = segment.AudioPath,
                DurationSeconds = segment.DurationSeconds
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

        return new ShortVideoRenderResult
        {
            Script = shortScript,
            AudioPath = shortAudioPath,
            VideoPath = videoPath,
            BlobUrl = blobUrl,
            PublishStatus = publishToYouTube ? "ReadyToPublish" : "Skipped"
        };
    }

    private async Task<List<NarrationSegment>> BuildNarrationSegmentsAsync(string scriptBody, string outputDirectory, CancellationToken cancellationToken)
    {
        var sentences = scriptBody
            .Split(['.', '!', '?'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static sentence => !string.IsNullOrWhiteSpace(sentence))
            .Select(static sentence => sentence.Trim())
            .ToList();

        if (sentences.Count == 0)
        {
            sentences.Add(scriptBody.Trim());
        }

        var segments = new List<NarrationSegment>(sentences.Count);
        for (var i = 0; i < sentences.Count; i++)
        {
            var segmentDirectory = Path.Combine(outputDirectory, "segments", $"{i + 1:000}");
            var audioPath = await _speechSynthesisService.SynthesizeAsync(sentences[i], segmentDirectory, cancellationToken);
            segments.Add(new NarrationSegment
            {
                Text = sentences[i],
                AudioPath = audioPath,
                DurationSeconds = EstimateDurationSeconds(sentences[i])
            });
        }

        return segments;
    }

    private static int EstimateDurationSeconds(string text)
    {
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedSeconds = (int)Math.Ceiling(wordCount / 2.7d);
        return Math.Max(1, estimatedSeconds);
    }
}
