using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;

public interface IAstronomyContextProvider { Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken); }
public interface ITopicRankingService { Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken); }
public interface IVisualAssetProvider { Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken); }
public interface IScriptGenerationService { Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken); }
public interface IShortsScriptGenerationService { Task<ShortScriptResult> GenerateShortAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken); }
public interface ISpeechSynthesisService { Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken); }
public interface IVideoRenderService { Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken); }
public interface IShortsVideoRenderService { Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken); }
public interface IAzureBlobStorageService { Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken); }
public interface IYouTubePublishingService { Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken); }
public interface IPipelineRepository {
 Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken);
 Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken);
 Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken);
 Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken);
 Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken);
 Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken);
 Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken);
 Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken);
 Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken);
 Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken);
 Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPipelineJobQueue
{
    Task<PipelineJob> EnqueueAsync(EnqueuePipelineJobRequest request, CancellationToken cancellationToken);
}

public interface IPipelineJobExecutor
{
    Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken);
}
