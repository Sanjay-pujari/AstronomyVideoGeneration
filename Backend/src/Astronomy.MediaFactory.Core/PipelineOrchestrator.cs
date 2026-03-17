using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
namespace Astronomy.MediaFactory.Core;

public sealed class PipelineOrchestrator
{
    private readonly IAstronomyContextProvider _contextProvider;
    private readonly ITopicRankingService _topicRankingService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IScriptGenerationService _scriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IArchivalService _archivalService;
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly IPipelineRepository _repository;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IAstronomyContextProvider contextProvider,
        ITopicRankingService topicRankingService,
        IVisualAssetProvider visualAssetProvider,
        IScriptGenerationService scriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVideoRenderService videoRenderService,
        IArchivalService archivalService,
        IYouTubePublishingService youTubePublishingService,
        IPipelineRepository repository,
        ILogger<PipelineOrchestrator> logger)
    {
        _contextProvider = contextProvider;
        _topicRankingService = topicRankingService;
        _visualAssetProvider = visualAssetProvider;
        _scriptGenerationService = scriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _videoRenderService = videoRenderService;
        _archivalService = archivalService;
        _youTubePublishingService = youTubePublishingService;
        _repository = repository;
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

            var manifest = new RenderManifest
            {
                Title = script.Title,
                AudioPath = audioPath,
                OutputPath = Path.Combine(outputDir, "final-video.mp4"),
                Scenes = visuals.Select((v, i) => new RenderScene
                {
                    Caption = $"Scene {i + 1}",
                    VisualPath = v,
                    DurationSeconds = Math.Max(8, script.EstimatedDurationSeconds / Math.Max(1, visuals.Count))
                }).ToList()
            };

            var videoPath = await _videoRenderService.RenderAsync(manifest, cancellationToken);
            await _repository.AddAssetAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "video",
                FileName = Path.GetFileName(videoPath),
                LocalPath = videoPath,
                SizeBytes = File.Exists(videoPath) ? new FileInfo(videoPath).Length : 0
            }, cancellationToken);

            var blobPath = $"{request.ContentType}/{request.Date:yyyy-MM-dd}/{run.Id:N}/{Path.GetFileName(videoPath)}";
            _ = await _archivalService.ArchiveAsync(videoPath, blobPath, cancellationToken);
            if (request.PublishToYouTube)
                run.YouTubeVideoId = await _youTubePublishingService.UploadAsync(videoPath, script.Title, script.Description, script.Tags, cancellationToken);

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
