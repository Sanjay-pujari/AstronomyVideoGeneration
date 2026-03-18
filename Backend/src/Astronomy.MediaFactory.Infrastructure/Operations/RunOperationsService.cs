using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class RunOperationsService : IRunOperationsService
{
    private readonly MediaFactoryDbContext _db;
    private readonly IPipelineJobQueue _queue;
    private readonly IAzureBlobStorageService _blobStorageService;
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly IYouTubeThumbnailPublisher? _thumbnailPublisher;
    private readonly IAstronomyContextProvider _contextProvider;
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly IShortsVideoRenderService _shortsVideoRenderService;
    private readonly IContentMonetizationService? _contentMonetizationService;
    private readonly IShortFormPublishingService? _shortFormPublishingService;
    private readonly IOperationalAlertNotifier? _alertNotifier;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<RunOperationsService> _logger;

    public RunOperationsService(
        MediaFactoryDbContext db,
        IPipelineJobQueue queue,
        IAzureBlobStorageService blobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IAstronomyContextProvider contextProvider,
        IMetadataOptimizationService metadataOptimizationService,
        IShortsVideoRenderService shortsVideoRenderService,
        ILogger<RunOperationsService> logger,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        IYouTubeThumbnailPublisher? thumbnailPublisher = null,
        IOperationalAlertNotifier? alertNotifier = null,
        IContentMonetizationService? contentMonetizationService = null,
        IShortFormPublishingService? shortFormPublishingService = null)
    {
        _db = db;
        _queue = queue;
        _blobStorageService = blobStorageService;
        _youTubePublishingService = youTubePublishingService;
        _contextProvider = contextProvider;
        _metadataOptimizationService = metadataOptimizationService;
        _shortsVideoRenderService = shortsVideoRenderService;
        _logger = logger;
        _contentMonetizationService = contentMonetizationService;
        _youTubeOptions = youTubeOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _thumbnailPublisher = thumbnailPublisher;
        _alertNotifier = alertNotifier;
        _shortFormPublishingService = shortFormPublishingService;
    }

    public async Task<OpsActionResult> ReplayRunAsync(Guid runId, ReplayPipelineRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var run = await RequireRunAsync(runId, cancellationToken);
        return await TrackOperationAsync(RecoveryOperationType.ReplayPipeline, runId, null, request.RequestedBy, request.Notes, async operation =>
        {
            var eligibility = RecoveryRequestValidator.GetReplayEligibility(run, request);
            if (!eligibility.CanReplay)
                throw new InvalidOperationException(eligibility.RejectionReason!);

            var queuedJob = await _queue.EnqueueAsync(new EnqueuePipelineJobRequest(
                PipelineJobType.GenerateMainVideo,
                run.RunDate,
                run.ContentType,
                run.LocationName,
                run.TimeZone,
                request.PublishToYouTubeOverride || run.PublishToYouTube,
                request.UseTopicPlannerOverride,
                DateTimeOffset.UtcNow), cancellationToken);

            operation.ResultSummary = $"Queued replay job {queuedJob.Id} for run {run.Id}.";
            return new OpsActionResult(operation.Id, operation.ResultSummary, [queuedJob.Id]);
        }, cancellationToken);
    }

    public async Task<OpsActionResult> RetryPublishAsync(Guid runId, RetryPublishRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var run = await RequireRunAsync(runId, cancellationToken);
        return await TrackOperationAsync(RecoveryOperationType.RetryPublish, runId, null, request.RequestedBy, request.Notes, async operation =>
        {
            var publishedVideo = await GetPublishedVideoAsync(run.Id, cancellationToken);
            var script = await _db.GeneratedScripts.AsNoTracking().OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.PipelineRunId == run.Id, cancellationToken)
                ?? throw new InvalidOperationException("Cannot retry publish because no generated script was found for the run.");
            var videoAsset = await GetRequiredAssetAsync(run.Id, "video", cancellationToken);
            var thumbnailAsset = await GetOptionalAssetAsync(run.Id, "thumbnail", cancellationToken);

            if (!File.Exists(videoAsset.LocalPath))
                throw new InvalidOperationException($"Cannot retry publish because the rendered video file is missing at '{videoAsset.LocalPath}'.");

            var title = string.IsNullOrWhiteSpace(script.OptimizedTitle) ? script.Title : script.OptimizedTitle;
            var description = string.IsNullOrWhiteSpace(script.OptimizedDescription) ? script.Description : script.OptimizedDescription;
            var tags = SplitCsv(script.OptimizedTagsCsv ?? script.TagsCsv);
            var affectedIds = new List<Guid>();

            _logger.LogInformation("Starting retry publish flow for run {PipelineRunId}. ThumbnailOnly={RetryThumbnailOnly} ForceRepublish={ForceRepublish}", run.Id, request.RetryThumbnailOnly, request.ForceRepublish);

            if (request.RetryThumbnailOnly)
            {
                if (_thumbnailPublisher is null)
                    throw new InvalidOperationException("Thumbnail retry is unavailable because no YouTube thumbnail publisher is configured.");
                if (publishedVideo?.YouTubeVideoId is null)
                    throw new InvalidOperationException("Thumbnail retry requires an existing YouTube video id.");
                if (thumbnailAsset is null || string.IsNullOrWhiteSpace(thumbnailAsset.LocalPath) || !File.Exists(thumbnailAsset.LocalPath))
                    throw new InvalidOperationException("Thumbnail retry requires a stored thumbnail asset.");

                var uploaded = await _thumbnailPublisher.UploadThumbnailAsync(publishedVideo.YouTubeVideoId, thumbnailAsset.LocalPath, cancellationToken);
                publishedVideo.ThumbnailUploadedToYouTube = uploaded;
                publishedVideo.Status = uploaded ? "Published" : "ThumbnailUploadFailed";
                affectedIds.Add(publishedVideo.Id);
                operation.ResultSummary = uploaded
                    ? $"Retried thumbnail upload for run {run.Id}."
                    : $"Thumbnail upload retry for run {run.Id} completed without confirmation.";
            }
            else
            {
                var shouldUpload = request.ForceRepublish || string.IsNullOrWhiteSpace(publishedVideo?.YouTubeVideoId);
                if (!shouldUpload && publishedVideo?.ThumbnailUploadedToYouTube == false && _thumbnailPublisher is not null && thumbnailAsset is not null && File.Exists(thumbnailAsset.LocalPath))
                {
                    publishedVideo.ThumbnailUploadedToYouTube = await _thumbnailPublisher.UploadThumbnailAsync(publishedVideo.YouTubeVideoId!, thumbnailAsset.LocalPath, cancellationToken);
                }
                else
                {
                    await EnsurePublishCooldownElapsedAsync(run.Id, operation.Id, cancellationToken);
                    run.YouTubeVideoId = await _youTubePublishingService.UploadAsync(videoAsset.LocalPath, title, description, tags, _youTubeOptions.PrivacyStatus, cancellationToken);
                    if (string.IsNullOrWhiteSpace(run.YouTubeVideoId))
                        throw new InvalidOperationException("YouTube upload retry completed without returning a video id.");

                    if (publishedVideo is null)
                    {
                        publishedVideo = new PublishedVideo
                        {
                            PipelineRunId = run.Id,
                            Title = title,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        await _db.PublishedVideos.AddAsync(publishedVideo, cancellationToken);
                    }

                    publishedVideo.Title = title;
                    publishedVideo.OptimizedTitle = script.OptimizedTitle;
                    publishedVideo.OptimizedDescription = description;
                    publishedVideo.OptimizedTagsCsv = string.Join(",", tags);
                    publishedVideo.YouTubeVideoId = run.YouTubeVideoId;
                    publishedVideo.Status = "Published";
                    publishedVideo.ThumbnailPath = thumbnailAsset?.LocalPath;
                    publishedVideo.PipelineRunId = run.Id;
                    if (_thumbnailPublisher is not null && thumbnailAsset is not null && File.Exists(thumbnailAsset.LocalPath))
                    {
                        publishedVideo.ThumbnailUploadedToYouTube = await _thumbnailPublisher.UploadThumbnailAsync(run.YouTubeVideoId, thumbnailAsset.LocalPath, cancellationToken);
                    }
                }

                if (publishedVideo is not null)
                    affectedIds.Add(publishedVideo.Id);

                operation.ResultSummary = $"Retried publish for run {run.Id} using stored assets.";
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Completed retry publish flow for run {PipelineRunId}. Summary: {Summary}", run.Id, operation.ResultSummary);
            return new OpsActionResult(operation.Id, operation.ResultSummary!, affectedIds);
        }, cancellationToken);
    }

    public async Task<OpsActionResult> RetryArchiveAsync(Guid runId, RetryArchiveRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        _ = await RequireRunAsync(runId, cancellationToken);
        return await TrackOperationAsync(RecoveryOperationType.RetryArchive, runId, null, request.RequestedBy, request.Notes, async operation =>
        {
            var videoAsset = await GetRequiredAssetAsync(runId, "video", cancellationToken);
            var audioAsset = await GetRequiredAssetAsync(runId, "audio", cancellationToken);
            var thumbnailAsset = await GetOptionalAssetAsync(runId, "thumbnail", cancellationToken);

            if (!File.Exists(videoAsset.LocalPath) || !File.Exists(audioAsset.LocalPath))
                throw new InvalidOperationException("Archive retry requires local video and audio assets to still be available.");

            var upload = await _blobStorageService.UploadAsync(new BlobUploadRequest
            {
                BasePath = $"recovery/{runId:N}",
                VideoPath = videoAsset.LocalPath,
                AudioPath = audioAsset.LocalPath,
                ThumbnailPath = thumbnailAsset is not null && File.Exists(thumbnailAsset.LocalPath) ? thumbnailAsset.LocalPath : null
            }, cancellationToken);

            videoAsset.PublicUrl = upload.VideoUrl;
            videoAsset.BlobPath = upload.VideoUrl;
            audioAsset.PublicUrl = upload.AudioUrl;
            audioAsset.BlobPath = upload.AudioUrl;
            if (thumbnailAsset is not null)
            {
                thumbnailAsset.PublicUrl = upload.ThumbnailUrl;
                thumbnailAsset.BlobPath = upload.ThumbnailUrl;
            }

            var publishedVideo = await GetPublishedVideoAsync(runId, cancellationToken);
            if (publishedVideo is not null)
            {
                publishedVideo.BlobUrl = upload.VideoUrl;
                publishedVideo.ThumbnailUrl = upload.ThumbnailUrl;
            }

            await _db.SaveChangesAsync(cancellationToken);
            operation.ResultSummary = $"Retried blob archival for run {runId}.";
            return new OpsActionResult(operation.Id, operation.ResultSummary, [videoAsset.Id, audioAsset.Id]);
        }, cancellationToken);
    }

    public async Task<OpsActionResult> RegenerateShortsAsync(Guid runId, RegenerateShortsRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var run = await RequireRunAsync(runId, cancellationToken);
        return await TrackOperationAsync(RecoveryOperationType.RegenerateShorts, runId, null, request.RequestedBy, request.Notes, async operation =>
        {
            if (run.Status != PipelineRunStatus.Succeeded && !request.Force)
                throw new InvalidOperationException("Short regeneration is only allowed for completed runs unless Force=true.");

            var publishedVideo = await GetPublishedVideoAsync(run.Id, cancellationToken)
                ?? throw new InvalidOperationException("Cannot regenerate shorts because no parent published video record was found.");
            var visuals = await _db.MediaAssets.AsNoTracking()
                .Where(x => x.PipelineRunId == run.Id && x.AssetType == "visual")
                .OrderBy(x => x.CreatedUtc)
                .Select(x => x.LocalPath)
                .ToListAsync(cancellationToken);

            if (visuals.Count == 0 || visuals.Any(path => !File.Exists(path)))
                throw new InvalidOperationException("Cannot regenerate shorts because the original visual assets are missing.");

            var context = await _contextProvider.BuildContextAsync(run.RunDate, run.ContentType, run.LocationName, run.TimeZone, cancellationToken);
            var outputDirectory = Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"), "shorts-recovery");
            Directory.CreateDirectory(outputDirectory);
            var shortResult = await _shortsVideoRenderService.RenderAsync(run.ContentType, context, visuals, outputDirectory, request.PublishToYouTube, cancellationToken);

            await _db.MediaAssets.AddAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "short-video",
                FileName = Path.GetFileName(shortResult.VideoPath),
                LocalPath = shortResult.VideoPath,
                PublicUrl = shortResult.BlobUrl,
                SizeBytes = File.Exists(shortResult.VideoPath) ? new FileInfo(shortResult.VideoPath).Length : 0
            }, cancellationToken);

            var shortVideo = new ShortVideo
            {
                ParentVideoId = publishedVideo.Id,
                YouTubeVideoId = shortResult.YouTubeVideoId,
                Duration = shortResult.Script.EstimatedDurationSeconds,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _db.ShortVideos.AddAsync(shortVideo, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            if (_shortFormPublishingService is not null)
            {
                var publicationResults = await _shortFormPublishingService.PublishAsync(new ShortFormPublicationRequest
                {
                    ParentShortVideoId = shortVideo.Id,
                    ContentType = run.ContentType,
                    PublishToYouTube = request.PublishToYouTube,
                    Title = shortResult.Script.OptimizedMetadata?.PrimaryTitle ?? shortResult.Script.Title,
                    Caption = shortResult.Script.OptimizedMetadata?.OptimizedDescription ?? shortResult.Script.ShortScript,
                    HookLine = shortResult.Script.OptimizedMetadata?.HookLine ?? shortResult.Script.Hook,
                    Tags = shortResult.Script.OptimizedMetadata?.Tags ?? shortResult.Script.Tags,
                    Hashtags = shortResult.Script.OptimizedMetadata?.Hashtags ?? [],
                    VideoPath = shortResult.VideoPath,
                    ThumbnailPath = publishedVideo.ThumbnailPath
                }, cancellationToken);

                foreach (var publication in publicationResults)
                {
                    await _db.PlatformPublicationRecords.AddAsync(new PlatformPublicationRecord
                    {
                        ParentShortVideoId = shortVideo.Id,
                        Platform = publication.Platform,
                        ExternalPostId = publication.ExternalPostId,
                        ExternalUrl = publication.ExternalUrl,
                        Status = publication.Status,
                        PublishedAt = publication.PublishedAt,
                        ErrorMessage = publication.ErrorMessage
                    }, cancellationToken);
                }

                shortVideo.YouTubeVideoId = publicationResults
                    .FirstOrDefault(x => x.Platform == ShortFormPlatform.YouTubeShorts && x.Status == PlatformPublicationStatus.Published)
                    ?.ExternalPostId;

                await _db.SaveChangesAsync(cancellationToken);
            }

            operation.ResultSummary = $"Regenerated shorts for run {run.Id}.";
            return new OpsActionResult(operation.Id, operation.ResultSummary, [shortVideo.Id]);
        }, cancellationToken);
    }

    public async Task<OpsActionResult> RerunMetadataOptimizationAsync(Guid runId, RerunMetadataOptimizationRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var run = await RequireRunAsync(runId, cancellationToken);
        return await TrackOperationAsync(RecoveryOperationType.RerunMetadataOptimization, runId, null, request.RequestedBy, request.Notes, async operation =>
        {
            var script = await _db.GeneratedScripts.OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.PipelineRunId == run.Id, cancellationToken)
                ?? throw new InvalidOperationException("Cannot rerun metadata optimization because no generated script exists for the run.");

            var context = await _contextProvider.BuildContextAsync(run.RunDate, run.ContentType, run.LocationName, run.TimeZone, cancellationToken);
            var optimized = await _metadataOptimizationService.OptimizeForVideoAsync(new MetadataOptimizationInput
            {
                ContentType = run.ContentType,
                Context = context,
                SourceTitle = script.Title,
                SourceDescription = script.Description,
                SourceTags = SplitCsv(script.TagsCsv),
                SourceScript = script.ScriptBody,
                SourceHookLine = script.HookLine
            }, cancellationToken);

            if (_contentMonetizationService is not null)
            {
                try
                {
                    var monetizationPlan = await _contentMonetizationService.BuildPlanAsync(new MonetizationInput
                    {
                        ContentType = run.ContentType,
                        Context = context,
                        Metadata = optimized
                    }, cancellationToken);

                    optimized = new OptimizedVideoMetadata
                    {
                        PrimaryTitle = optimized.PrimaryTitle,
                        AlternateTitles = optimized.AlternateTitles,
                        OptimizedDescription = string.IsNullOrWhiteSpace(monetizationPlan.FinalDescription) ? optimized.OptimizedDescription : monetizationPlan.FinalDescription,
                        Tags = optimized.Tags,
                        Hashtags = optimized.Hashtags,
                        ThumbnailTextSuggestions = optimized.ThumbnailTextSuggestions,
                        HookLine = optimized.HookLine
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Monetization regeneration failed for run {PipelineRunId}. Keeping optimized metadata only.", run.Id);
                }
            }

            script.OptimizedTitle = optimized.PrimaryTitle;
            script.AlternateTitlesCsv = string.Join("|", optimized.AlternateTitles);
            script.OptimizedDescription = optimized.OptimizedDescription;
            script.OptimizedTagsCsv = string.Join(",", optimized.Tags);
            script.OptimizedHashtagsCsv = string.Join(",", optimized.Hashtags);
            script.ThumbnailTextSuggestionsCsv = string.Join("|", optimized.ThumbnailTextSuggestions);
            script.HookLine = optimized.HookLine;
            script.Touch();

            var affectedIds = new List<Guid> { script.Id };
            if (request.ApplyToPublishedVideo)
            {
                var publishedVideo = await GetPublishedVideoAsync(run.Id, cancellationToken);
                if (publishedVideo is not null)
                {
                    publishedVideo.Title = optimized.PrimaryTitle;
                    publishedVideo.OptimizedTitle = optimized.PrimaryTitle;
                    publishedVideo.OptimizedDescription = optimized.OptimizedDescription;
                    publishedVideo.OptimizedTagsCsv = string.Join(",", optimized.Tags);
                    affectedIds.Add(publishedVideo.Id);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            operation.ResultSummary = $"Reran metadata optimization for run {run.Id}.";
            return new OpsActionResult(operation.Id, operation.ResultSummary, affectedIds);
        }, cancellationToken);
    }

    public async Task<OpsActionResult> RequeueJobAsync(Guid jobId, RequeueJobRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var job = await _db.PipelineJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Pipeline job {jobId} was not found.");

        return await TrackOperationAsync(RecoveryOperationType.RequeueJob, job.ParentPipelineRunId, job.Id, request.RequestedBy, request.Notes, async operation =>
        {
            if (job.Status == PipelineJobStatus.Running)
                throw new InvalidOperationException("Running jobs must be marked stale or failed before they can be requeued.");
            if (job.Status == PipelineJobStatus.Succeeded && !request.Force)
                throw new InvalidOperationException("Successful jobs require Force=true before they can be requeued.");

            job.Status = PipelineJobStatus.Pending;
            job.ScheduledAt = DateTimeOffset.UtcNow;
            job.StartedAt = null;
            job.FinishedAt = null;
            job.NextAttemptAt = null;
            job.ErrorMessage = null;
            job.IsStale = false;
            job.StaleDetectedAt = null;
            job.RecoveryNotes = AppendNote(job.RecoveryNotes, $"Requeued by {request.RequestedBy}: {request.Notes}");
            job.Touch();
            await _db.SaveChangesAsync(cancellationToken);

            operation.ResultSummary = $"Requeued job {job.Id}.";
            return new OpsActionResult(operation.Id, operation.ResultSummary, [job.Id]);
        }, cancellationToken);
    }

    public async Task<StaleJobRecoverySummary> RecoverStaleJobsAsync(RecoverStaleJobsRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        return await TrackOperationAsync(RecoveryOperationType.RecoverStaleJobs, null, null, request.RequestedBy, request.Notes, async operation =>
        {
            var thresholdMinutes = request.ThresholdMinutes ?? _maintenanceOptions.StaleJobThresholdMinutes;
            var now = DateTimeOffset.UtcNow;
            var staleBefore = now.AddMinutes(-thresholdMinutes);
            var candidates = await GetStaleJobCandidatesAsync(staleBefore, cancellationToken);

            var markedIds = new List<Guid>();
            var requeuedIds = new List<Guid>();
            foreach (var job in candidates)
            {
                MarkJobAsStale(job, request, thresholdMinutes, now);
                markedIds.Add(job.Id);

                if (request.RequeueRecoveredJobs)
                {
                    RequeueRecoveredJob(job, now);
                    requeuedIds.Add(job.Id);
                }
            }

            var recoveredRunIds = new List<Guid>();
            if (request.RecoverIncompleteRuns)
            {
                var incompleteRuns = await _db.PipelineRuns
                    .Where(x => x.Status == PipelineRunStatus.Succeeded)
                    .Where(x => _db.MediaAssets.Any(a => a.PipelineRunId == x.Id && a.AssetType == "video"))
                    .Where(x => !_db.PublishedVideos.Any(p => p.PipelineRunId == x.Id && p.Status == "Published")
                        || _db.PublishedVideos.Any(p => p.PipelineRunId == x.Id && (p.Status == "UploadFailed" || p.BlobUrl == null)))
                    .ToListAsync(cancellationToken);

                foreach (var run in incompleteRuns)
                {
                    recoveredRunIds.Add(run.Id);
                    var needsPublishRetry = await _db.PublishedVideos.AnyAsync(p => p.PipelineRunId == run.Id && p.Status == "UploadFailed", cancellationToken);
                    if (needsPublishRetry)
                    {
                        await QueueOpsJobAsync(run, PipelineJobType.PublishVideo, cancellationToken);
                    }

                    var hasArchive = await _db.MediaAssets.AnyAsync(a => a.PipelineRunId == run.Id && a.AssetType == "video" && a.PublicUrl != null, cancellationToken);
                    if (!hasArchive)
                    {
                        await QueueOpsJobAsync(run, PipelineJobType.ArchiveAssets, cancellationToken);
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            operation.ResultSummary = $"Marked {markedIds.Count} stale jobs, requeued {requeuedIds.Count} jobs, recovered {recoveredRunIds.Count} incomplete runs.";
            return new StaleJobRecoverySummary(operation.Id, markedIds.Count, requeuedIds.Count, recoveredRunIds.Count, markedIds, recoveredRunIds);
        }, cancellationToken);
    }

    private async Task QueueOpsJobAsync(PipelineRun run, PipelineJobType jobType, CancellationToken cancellationToken)
    {
        var exists = await _db.PipelineJobs.AnyAsync(x => x.ParentPipelineRunId == run.Id && x.JobType == jobType && x.Status != PipelineJobStatus.Failed, cancellationToken);
        if (exists)
            return;

        await _db.PipelineJobs.AddAsync(new PipelineJob
        {
            ParentPipelineRunId = run.Id,
            JobType = jobType,
            Status = PipelineJobStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow,
            RunDate = run.RunDate,
            ContentType = run.ContentType,
            LocationName = run.LocationName,
            TimeZone = run.TimeZone,
            PublishToYouTube = run.PublishToYouTube
        }, cancellationToken);
    }

    private Task<List<PipelineJob>> GetStaleJobCandidatesAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken)
        => _db.PipelineJobs
            .Where(x => !x.IsStale)
            .Where(x => (x.Status == PipelineJobStatus.Running && x.StartedAt.HasValue && x.StartedAt <= staleBefore)
                || (x.Status == PipelineJobStatus.Pending && x.ScheduledAt <= staleBefore)
                || (x.Status == PipelineJobStatus.Retrying && x.NextAttemptAt.HasValue && x.NextAttemptAt <= staleBefore))
            .ToListAsync(cancellationToken);

    private static void MarkJobAsStale(PipelineJob job, RecoverStaleJobsRequest request, int thresholdMinutes, DateTimeOffset detectedAt)
    {
        job.IsStale = true;
        job.StaleDetectedAt = detectedAt;
        job.RecoveryNotes = AppendNote(job.RecoveryNotes, $"Marked stale by {request.RequestedBy} after {thresholdMinutes} minutes. {request.Notes}");
        job.Status = PipelineJobStatus.Stale;
        job.Touch();
    }

    private static void RequeueRecoveredJob(PipelineJob job, DateTimeOffset scheduledAt)
    {
        job.Status = PipelineJobStatus.Pending;
        job.ScheduledAt = scheduledAt;
        job.StartedAt = null;
        job.FinishedAt = null;
        job.NextAttemptAt = null;
        job.ErrorMessage = null;
        job.IsStale = false;
        job.StaleDetectedAt = null;
        job.Touch();
    }



    private async Task EnsurePublishCooldownElapsedAsync(Guid runId, Guid currentOperationId, CancellationToken cancellationToken)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Clamp(_youTubeOptions.PublishRetryCooldownSeconds, 1, 600));
        var lastAttempt = await _db.RecoveryOperations
            .AsNoTracking()
            .Where(x => x.PipelineRunId == runId && x.Id != currentOperationId && x.OperationType == RecoveryOperationType.RetryPublish && x.Status != RecoveryOperationStatus.Rejected)
            .OrderByDescending(x => x.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastAttempt is null)
        {
            return;
        }

        var retryAt = lastAttempt.RequestedAt.Add(cooldown);
        if (retryAt > DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException($"Retry publish cooldown is active for run {runId}. Retry after {retryAt:O}.");
        }
    }

    private async Task<PipelineRun> RequireRunAsync(Guid runId, CancellationToken cancellationToken)
        => await _db.PipelineRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Pipeline run {runId} was not found.");

    private async Task<MediaAsset> GetRequiredAssetAsync(Guid runId, string assetType, CancellationToken cancellationToken)
        => await GetOptionalAssetAsync(runId, assetType, cancellationToken)
            ?? throw new InvalidOperationException($"Required asset '{assetType}' was not found for run {runId}.");

    private Task<MediaAsset?> GetOptionalAssetAsync(Guid runId, string assetType, CancellationToken cancellationToken)
        => _db.MediaAssets.OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.PipelineRunId == runId && x.AssetType == assetType, cancellationToken);

    private async Task<PublishedVideo?> GetPublishedVideoAsync(Guid runId, CancellationToken cancellationToken)
    {
        var published = await _db.PublishedVideos.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(x => x.PipelineRunId == runId, cancellationToken);
        if (published is not null)
            return published;

        var script = await _db.GeneratedScripts.AsNoTracking().OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.PipelineRunId == runId, cancellationToken);
        if (script is null)
            return null;

        return await _db.PublishedVideos
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.Title == (script.OptimizedTitle ?? script.Title), cancellationToken);
    }

    private async Task<T> TrackOperationAsync<T>(RecoveryOperationType operationType, Guid? runId, Guid? jobId, string requestedBy, string? notes, Func<RecoveryOperation, Task<T>> action, CancellationToken cancellationToken)
    {
        var operation = new RecoveryOperation
        {
            PipelineRunId = runId,
            PipelineJobId = jobId,
            OperationType = operationType,
            RequestedBy = requestedBy,
            Notes = notes,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = RecoveryOperationStatus.Requested
        };

        await _db.RecoveryOperations.AddAsync(operation, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["recoveryOperationId"] = operation.Id,
            ["pipelineRunId"] = runId,
            ["pipelineJobId"] = jobId,
            ["operationType"] = operationType.ToString(),
            ["requestedBy"] = requestedBy
        });
        _logger.LogInformation("Starting recovery operation {RecoveryOperationId} ({OperationType}) for run {PipelineRunId} / job {PipelineJobId}.", operation.Id, operationType, runId, jobId);

        try
        {
            var result = await action(operation);
            operation.Status = RecoveryOperationStatus.Completed;
            operation.Touch();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Completed recovery operation {RecoveryOperationId}: {ResultSummary}", operation.Id, operation.ResultSummary);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            operation.Status = RecoveryOperationStatus.Rejected;
            operation.ResultSummary = ex.Message;
            operation.Touch();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Recovery operation {RecoveryOperationId} rejected.", operation.Id);
            throw;
        }
        catch (Exception ex)
        {
            operation.Status = RecoveryOperationStatus.Failed;
            operation.ResultSummary = ex.Message;
            operation.Touch();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Recovery operation {RecoveryOperationId} failed.", operation.Id);
            if (_alertNotifier is not null)
            {
                await _alertNotifier.NotifyAsync(new OperationalAlert(
                    AlertCategory.PipelineFailed,
                    $"Recovery operation {operationType} failed.",
                    runId,
                    ErrorSummary: ex.Message,
                    JobId: jobId,
                    OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
            }
            throw;
        }
    }

    private static IReadOnlyCollection<string> SplitCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string AppendNote(string? existing, string note)
        => string.IsNullOrWhiteSpace(existing) ? note : $"{existing}\n{note}";
}
