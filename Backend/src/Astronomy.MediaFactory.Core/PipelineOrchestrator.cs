using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineOrchestrator
{
    private readonly IAstronomyContextProvider _contextProvider;
    private readonly ITopicRankingService _topicRankingService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IScriptGenerationService _scriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IAzureBlobStorageService _azureBlobStorageService;
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly IShortsVideoRenderService _shortsVideoRenderService;
    private readonly IPipelineRepository _repository;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IAstronomyContextProvider contextProvider,
        ITopicRankingService topicRankingService,
        IVisualAssetProvider visualAssetProvider,
        IScriptGenerationService scriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVideoRenderService videoRenderService,
        IAzureBlobStorageService azureBlobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IShortsVideoRenderService shortsVideoRenderService,
        IPipelineRepository repository,
        IOptions<YouTubeOptions> youTubeOptions,
        ILogger<PipelineOrchestrator> logger)
    {
        _contextProvider = contextProvider;
        _topicRankingService = topicRankingService;
        _visualAssetProvider = visualAssetProvider;
        _scriptGenerationService = scriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _videoRenderService = videoRenderService;
        _azureBlobStorageService = azureBlobStorageService;
        _youTubePublishingService = youTubePublishingService;
        _shortsVideoRenderService = shortsVideoRenderService;
        _repository = repository;
        _youTubeOptions = youTubeOptions.Value;
        _logger = logger;
    }

    public async Task<PipelineRun> RunAsync(RunPipelineRequest request, CancellationToken cancellationToken)
    {
        var run = new PipelineRun
        {
            RunDate = request.Date,
            ContentType = request.ContentType,
            LocationName = request.LocationName,
            TimeZone = request.TimeZone,
            PublishToYouTube = request.PublishToYouTube,
            Status = PipelineRunStatus.Queued
        };

        await _repository.CreateAsync(run, cancellationToken);
        run.Status = PipelineRunStatus.Running;
        run.StartedUtc = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var outputDir = Path.Combine("media-output", request.ContentType.ToString(), request.Date.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
            Directory.CreateDirectory(outputDir);

            var context = await _contextProvider.BuildContextAsync(request.Date, request.ContentType, request.LocationName, request.TimeZone, cancellationToken);
            _ = await _topicRankingService.RankAsync(context, request.ContentType, cancellationToken);
            var script = await _scriptGenerationService.GenerateAsync(request.ContentType, context, cancellationToken);
            var audioPath = await _speechSynthesisService.SynthesizeAsync(script.ScriptBody, outputDir, cancellationToken);
            var visuals = await _visualAssetProvider.PrepareVisualsAsync(context, outputDir, cancellationToken);

            await _repository.AddScriptAsync(new GeneratedScript
            {
                PipelineRunId = run.Id,
                ContentType = request.ContentType,
                ScriptDate = request.Date,
                Prompt = script.Prompt,
                ScriptBody = script.ScriptBody,
                Title = script.Title,
                Description = script.Description,
                TagsCsv = string.Join(",", script.Tags),
                EstimatedDurationSeconds = script.EstimatedDurationSeconds
            }, cancellationToken);

            await _repository.AddAssetAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "audio",
                FileName = Path.GetFileName(audioPath),
                LocalPath = audioPath,
                SizeBytes = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0
            }, cancellationToken);

            foreach (var visual in visuals)
            {
                await _repository.AddAssetAsync(new MediaAsset
                {
                    PipelineRunId = run.Id,
                    AssetType = "visual",
                    FileName = Path.GetFileName(visual),
                    LocalPath = visual,
                    SizeBytes = File.Exists(visual) ? new FileInfo(visual).Length : 0
                }, cancellationToken);
            }

            var totalScenes = Math.Max(1, visuals.Count);
            var durationPerScene = Math.Max(6, script.EstimatedDurationSeconds / totalScenes);
            var manifest = new RenderManifest
            {
                Title = script.Title,
                AudioPath = audioPath,
                OutputPath = Path.Combine(outputDir, "final-video.mp4"),
                Scenes = visuals.Select((v, i) => new RenderScene
                {
                    Caption = i < context.VisualIdeas.Count
                        ? context.VisualIdeas[i].Title
                        : $"Scene {i + 1}",
                    VisualPath = v,
                    DurationSeconds = durationPerScene
                }).ToList()
            };

            var videoPath = await _videoRenderService.RenderAsync(manifest, cancellationToken);
            var thumbnailPath = visuals.FirstOrDefault(File.Exists);
            await _repository.AddAssetAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "video",
                FileName = Path.GetFileName(videoPath),
                LocalPath = videoPath,
                SizeBytes = File.Exists(videoPath) ? new FileInfo(videoPath).Length : 0
            }, cancellationToken);

            BlobUploadResult blobUploadResult = new();
            try
            {
                blobUploadResult = await _azureBlobStorageService.UploadAsync(new BlobUploadRequest
                {
                    BasePath = $"{request.ContentType}/{request.Date:yyyy-MM-dd}/{run.Id:N}",
                    VideoPath = videoPath,
                    AudioPath = audioPath,
                    ThumbnailPath = thumbnailPath
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob upload failed for pipeline run {PipelineRunId}. Continuing with local artifacts.", run.Id);
            }

            var publishStatus = "Published";
            if (request.PublishToYouTube)
            {
                try
                {
                    run.YouTubeVideoId = await _youTubePublishingService.UploadAsync(
                        videoPath,
                        script.Title,
                        script.Description,
                        script.Tags,
                        _youTubeOptions.PrivacyStatus,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(run.YouTubeVideoId))
                        publishStatus = "UploadFailed";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YouTube upload failed for pipeline run {PipelineRunId}.", run.Id);
                    publishStatus = "UploadFailed";
                }
            }

            var publishedVideo = new PublishedVideo
            {
                Title = script.Title,
                YouTubeVideoId = run.YouTubeVideoId,
                BlobUrl = blobUploadResult.VideoUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = publishStatus
            };

            await _repository.AddPublishedVideoAsync(publishedVideo, cancellationToken);

            try
            {
                var shortsOutputDir = Path.Combine(outputDir, "shorts");
                Directory.CreateDirectory(shortsOutputDir);
                var shortResult = await _shortsVideoRenderService.RenderAsync(request.ContentType, context, visuals, shortsOutputDir, request.PublishToYouTube, cancellationToken);

                await _repository.AddAssetAsync(new MediaAsset
                {
                    PipelineRunId = run.Id,
                    AssetType = "short-video",
                    FileName = Path.GetFileName(shortResult.VideoPath),
                    LocalPath = shortResult.VideoPath,
                    PublicUrl = shortResult.BlobUrl,
                    SizeBytes = File.Exists(shortResult.VideoPath) ? new FileInfo(shortResult.VideoPath).Length : 0
                }, cancellationToken);

                await _repository.AddShortVideoAsync(new ShortVideo
                {
                    ParentVideoId = publishedVideo.Id,
                    YouTubeVideoId = shortResult.YouTubeVideoId,
                    Duration = shortResult.Script.EstimatedDurationSeconds,
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shorts generation failed for pipeline run {PipelineRunId}. Main video remains unaffected.", run.Id);
            }


            run.Status = PipelineRunStatus.Succeeded;
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline run failed.");
            run.Status = PipelineRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
