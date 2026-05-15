using Astronomy.MediaFactory.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineRecoveryService : IPipelineRecoveryService
{
    private static readonly Dictionary<string, string[]> PublishStagesByPlatform = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = [PipelineStageNames.YouTubeLongPublished, PipelineStageNames.YouTubeShortPublished],
        ["facebook"] = [PipelineStageNames.FacebookReelPublished],
        ["instagram"] = [PipelineStageNames.InstagramReelPublished],
        ["all"] = [PipelineStageNames.YouTubeLongPublished, PipelineStageNames.YouTubeShortPublished, PipelineStageNames.FacebookReelPublished, PipelineStageNames.InstagramReelPublished]
    };

    private readonly IPipelineRepository _repository;
    private readonly ITokenHealthService? _tokenHealthService;
    private readonly IContentPublishService? _contentPublishService;

    public PipelineRecoveryService(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public PipelineRecoveryService(
        IPipelineRepository repository,
        ITokenHealthService tokenHealthService,
        IContentPublishService contentPublishService)
    {
        _repository = repository;
        _tokenHealthService = tokenHealthService;
        _contentPublishService = contentPublishService;
    }

    public async Task<PipelineStatusResponse?> GetStatusAsync(Guid pipelineRunId, CancellationToken cancellationToken, bool includeInternal = false)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        var allStages = await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken);
        var stages = FilterStages(allStages, includeInternal).ToArray();
        var published = await _repository.GetPublishedVideosByRunAsync(pipelineRunId, cancellationToken);
        var platformPublications = await _repository.GetPlatformPublicationRecordsByRunAsync(pipelineRunId, cancellationToken);
        var failed = stages.FirstOrDefault(s => s.Status == PersistentStageStatuses.Failed);
        var publishedUrlStatus = await BuildPublishedUrlStatusAsync(run, stages, published, platformPublications, cancellationToken);

        return new PipelineStatusResponse(
            run.Id,
            run.Status,
            stages.Select(ToDto).ToArray(),
            publishedUrlStatus.Urls,
            failed?.StageName,
            failed?.LastError ?? run.FailureReason,
            run.OutputFolder,
            publishedUrlStatus.Warnings);
    }

    public async Task<PipelineStatusResponse?> ResumeAsync(Guid pipelineRunId, string? forceStage, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        var stages = (await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken)).ToList();
        if (!string.IsNullOrWhiteSpace(forceStage))
        {
            var forced = stages.FirstOrDefault(s => s.StageName.Equals(forceStage, StringComparison.OrdinalIgnoreCase));
            if (forced is not null)
            {
                forced.Status = PersistentStageStatuses.Pending;
                forced.LastError = null;
                forced.CompletedUtc = null;
            }
        }

        var firstResumeStage = stages.FirstOrDefault(s => s.Status is PersistentStageStatuses.Failed or PersistentStageStatuses.Pending);
        if (firstResumeStage is not null)
            run.Status = PipelineRunStatus.Running;
        run.ResumeSupported = true;
        await _repository.SaveChangesAsync(cancellationToken);
        await WriteStateAsync(run, stages, firstResumeStage?.StageName, cancellationToken);
        return await GetStatusAsync(pipelineRunId, cancellationToken);
    }

    public async Task<PipelineStatusResponse?> RetryPublishAsync(Guid pipelineRunId, string platform, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
            return null;

        platform = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
        if (!PublishStagesByPlatform.TryGetValue(platform, out var targetStages))
            targetStages = PublishStagesByPlatform["all"];

        var stages = (await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken)).ToList();
        var targetStageSet = targetStages.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetStageSet.Contains(PipelineStageNames.YouTubeLongPublished) || targetStageSet.Contains(PipelineStageNames.YouTubeShortPublished))
        {
            var healthFailure = await GetYouTubeTokenHealthFailureAsync(cancellationToken);
            if (healthFailure is not null)
            {
                var failedStages = PublishStagesByPlatform["youtube"];
                foreach (var stageName in failedStages)
                {
                    var stage = await EnsureStageAsync(stages, pipelineRunId, stageName, cancellationToken);
                    MarkStageFailed(stage, healthFailure);
                }

                run.Status = PipelineRunStatus.CompletedWithPublishErrors;
                run.FailureReason = $"Publishing failed: {healthFailure}";
                run.ResumeSupported = true;
                await WritePublishIdempotencyAsync(run, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                await WriteStateAsync(run, stages, failedStages.FirstOrDefault(), cancellationToken);
                return await GetStatusAsync(pipelineRunId, cancellationToken);
            }
        }

        foreach (var stageName in targetStages)
        {
            var stage = await EnsureStageAsync(stages, pipelineRunId, stageName, cancellationToken);
            if (stage.Status != PersistentStageStatuses.Succeeded)
            {
                stage.Status = PersistentStageStatuses.Pending;
                stage.LastError = null;
                stage.CompletedUtc = null;
            }
        }

        await WritePublishIdempotencyAsync(run, cancellationToken);

        if (_contentPublishService is not null && targetStageSet.Contains(PipelineStageNames.YouTubeLongPublished))
            await RetryYouTubePublishStageAsync(stages, pipelineRunId, PipelineStageNames.YouTubeLongPublished, "long", false, cancellationToken);

        if (_contentPublishService is not null && targetStageSet.Contains(PipelineStageNames.YouTubeShortPublished))
            await RetryYouTubePublishStageAsync(stages, pipelineRunId, PipelineStageNames.YouTubeShortPublished, "short", true, cancellationToken);

        var failedTargetStages = stages.Where(s => targetStageSet.Contains(s.StageName) && s.Status == PersistentStageStatuses.Failed).ToArray();
        if (failedTargetStages.Length > 0)
        {
            run.Status = PipelineRunStatus.CompletedWithPublishErrors;
            run.FailureReason = $"Publishing failed: {string.Join(", ", failedTargetStages.Select(s => s.StageName))}";
        }
        else if (targetStageSet.Any(stageName => stages.Any(s => s.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase) && s.Status == PersistentStageStatuses.Pending)))
        {
            run.Status = PipelineRunStatus.Running;
        }
        else if (run.Status == PipelineRunStatus.CompletedWithPublishErrors || run.Status == PipelineRunStatus.PublishFailed)
        {
            run.Status = PipelineRunStatus.Succeeded;
            run.FailureReason = null;
        }

        run.ResumeSupported = true;
        await _repository.SaveChangesAsync(cancellationToken);
        await WriteStateAsync(run, stages, targetStages.FirstOrDefault(), cancellationToken);
        return await GetStatusAsync(pipelineRunId, cancellationToken);
    }


    private async Task<string?> GetYouTubeTokenHealthFailureAsync(CancellationToken cancellationToken)
    {
        if (_tokenHealthService is null)
            return null;

        var health = await _tokenHealthService.CheckYouTubeAsync(cancellationToken);
        if (health.IsValid)
            return null;

        var reason = string.IsNullOrWhiteSpace(health.Error) ? health.Warning : health.Error;
        return string.IsNullOrWhiteSpace(reason)
            ? "YouTube token health check failed."
            : $"YouTube token health check failed: {reason}";
    }

    private async Task<PipelineStageExecution> EnsureStageAsync(List<PipelineStageExecution> stages, Guid pipelineRunId, string stageName, CancellationToken cancellationToken)
    {
        var stage = stages.FirstOrDefault(s => s.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase));
        if (stage is not null)
            return stage;

        stage = new PipelineStageExecution { PipelineRunId = pipelineRunId, StageName = stageName, Status = PersistentStageStatuses.Pending, MaxAttempts = 3 };
        await _repository.AddStageExecutionAsync(stage, cancellationToken);
        stages.Add(stage);
        return stage;
    }

    private static void MarkStageFailed(PipelineStageExecution stage, string error)
    {
        var now = DateTimeOffset.UtcNow;
        stage.Status = PersistentStageStatuses.Failed;
        stage.LastError = error;
        stage.CompletedUtc = now;
        stage.StartedUtc = now;
        stage.AttemptCount += 1;
        if (stage.MaxAttempts <= 0)
            stage.MaxAttempts = 3;
    }

    private async Task RetryYouTubePublishStageAsync(
        List<PipelineStageExecution> stages,
        Guid pipelineRunId,
        string stageName,
        string asset,
        bool isShort,
        CancellationToken cancellationToken)
    {
        var stage = await EnsureStageAsync(stages, pipelineRunId, stageName, cancellationToken);
        if (stage.Status == PersistentStageStatuses.Succeeded)
            return;

        var now = DateTimeOffset.UtcNow;
        stage.StartedUtc = now;
        stage.AttemptCount += 1;

        try
        {
            var results = await _contentPublishService!.PublishForPipelineRunAsync(pipelineRunId, asset, cancellationToken);
            var result = results.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.IsShort == isShort);
            if (result is { Success: true })
            {
                stage.Status = string.Equals(result.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) ? PersistentStageStatuses.Skipped : PersistentStageStatuses.Succeeded;
                stage.LastError = null;
            }
            else
            {
                stage.Status = PersistentStageStatuses.Failed;
                stage.LastError = result?.Error ?? $"{stageName} retry did not produce a successful YouTube publish result.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            stage.Status = PersistentStageStatuses.Failed;
            stage.LastError = ex.Message;
        }

        stage.CompletedUtc = DateTimeOffset.UtcNow;
        if (stage.MaxAttempts <= 0)
            stage.MaxAttempts = 3;
    }

    private static IReadOnlyCollection<PipelineStageExecution> FilterStages(IReadOnlyCollection<PipelineStageExecution> stages, bool includeInternal)
    {
        var filtered = includeInternal
            ? stages
            : stages.Where(s => PipelineStageNames.All.Contains(s.StageName, StringComparer.OrdinalIgnoreCase));

        return filtered
            .GroupBy(s => s.StageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.CreatedUtc).First())
            .OrderBy(s => Array.IndexOf(PipelineStageNames.All, s.StageName) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(s => s.StartedUtc)
            .ToArray();
    }

    private static async Task<PublishedUrlStatus> BuildPublishedUrlStatusAsync(
        PipelineRun run,
        IReadOnlyCollection<PipelineStageExecution> stages,
        IReadOnlyCollection<PublishedVideo> published,
        IReadOnlyCollection<PlatformPublicationRecord> platformPublications,
        CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        var warnings = new List<string>();

        foreach (var result in await ReadPublishResultFilesAsync<PublishResult>(run.OutputFolder, stages, cancellationToken,
            (PipelineStageNames.YouTubeLongPublished, "youtube-publish-result-long.json"),
            (PipelineStageNames.YouTubeShortPublished, "youtube-publish-result-short.json")))
        {
            var url = BuildYouTubeUrl(result);
            if (IsAllowedPublishedUrl(url))
                urls.Add(url!);
        }

        urls.AddRange(published
            .Where(p => !string.IsNullOrWhiteSpace(p.YouTubeVideoId) && p.Status.Equals("Published", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"https://www.youtube.com/watch?v={p.YouTubeVideoId}"));

        foreach (var result in await ReadMetaPublishResultFilesAsync(run.OutputFolder, stages, cancellationToken,
            (PipelineStageNames.FacebookReelPublished, "facebook-reel-publish-result.json", "Facebook"),
            (PipelineStageNames.InstagramReelPublished, "instagram-reel-publish-result.json", "Instagram")))
        {
            if (!result.Success)
                continue;

            if (result.Platform.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
            {
                var url = BuildFacebookReelUrl(result);
                if (IsAllowedPublishedUrl(url))
                    urls.Add(url!);
            }
            else if (result.Platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
            {
                var url = BuildInstagramPermalinkUrl(result);
                if (IsAllowedPublishedUrl(url))
                {
                    urls.Add(url!);
                }
                else if (!string.IsNullOrWhiteSpace(result.VideoId) || !string.IsNullOrWhiteSpace(result.PostId))
                {
                    warnings.Add("Instagram Reel publish result contained an id but no permalink URL; omitting it from publishedUrls.");
                }
            }
        }

        urls.AddRange(platformPublications
            .Where(p => p.Status == PlatformPublicationStatus.Published)
            .Select(p => p.ExternalUrl)
            .Where(IsAllowedPublishedUrl)
            .Select(url => url!));

        return new PublishedUrlStatus(
            urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? BuildYouTubeUrl(PublishResult result)
    {
        if (!result.Success || !result.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrWhiteSpace(result.VideoId))
            return result.IsShort
                ? $"https://www.youtube.com/shorts/{result.VideoId}"
                : $"https://www.youtube.com/watch?v={result.VideoId}";

        return IsYouTubePublishedUrl(result.VideoUrl, result.IsShort) ? result.VideoUrl : null;
    }

    private static string? BuildFacebookReelUrl(StatusMetaPublishResult result)
    {
        if (IsFacebookReelUrl(result.Url))
            return result.Url;

        return string.IsNullOrWhiteSpace(result.VideoId) ? null : $"https://www.facebook.com/reel/{Uri.EscapeDataString(result.VideoId)}/";
    }

    private static string? BuildInstagramPermalinkUrl(StatusMetaPublishResult result)
        => IsInstagramPermalinkUrl(result.Url) ? result.Url : null;

    private static async Task<IReadOnlyCollection<T>> ReadPublishResultFilesAsync<T>(
        string? outputFolder,
        IReadOnlyCollection<PipelineStageExecution> stages,
        CancellationToken cancellationToken,
        params (string StageName, string FileName)[] stageFiles)
    {
        var results = new List<T>();
        foreach (var (stageName, fileName) in stageFiles)
        {
            foreach (var path in GetPublishResultPaths(outputFolder, stages, stageName, fileName))
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    var result = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
                    if (result is not null)
                        results.Add(result);
                    break;
                }
                catch (JsonException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static async Task<IReadOnlyCollection<StatusMetaPublishResult>> ReadMetaPublishResultFilesAsync(
        string? outputFolder,
        IReadOnlyCollection<PipelineStageExecution> stages,
        CancellationToken cancellationToken,
        params (string StageName, string FileName, string Platform)[] stageFiles)
    {
        var results = new List<StatusMetaPublishResult>();
        foreach (var (stageName, fileName, platform) in stageFiles)
        {
            var stageSucceeded = stages.Any(s => s.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase) && s.Status == PersistentStageStatuses.Succeeded);
            foreach (var path in GetPublishResultPaths(outputFolder, stages, stageName, fileName))
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                    var root = document.RootElement;
                    var url = FirstNonBlank(
                        GetStringProperty(root, "url"),
                        GetStringProperty(root, "permalinkUrl"),
                        GetStringProperty(root, "permalink"));
                    var videoId = FirstNonBlank(
                        GetStringProperty(root, "videoId"),
                        GetStringProperty(root, "mediaId"));
                    var postId = FirstNonBlank(
                        GetStringProperty(root, "postId"),
                        GetStringProperty(root, "mediaId"));
                    var filePlatform = FirstNonBlank(GetStringProperty(root, "platform"), platform) ?? platform;
                    var success = GetBooleanProperty(root, "success") ?? (stageSucceeded && (!string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(videoId) || !string.IsNullOrWhiteSpace(postId)));

                    results.Add(new StatusMetaPublishResult(success, filePlatform, videoId, postId, url));
                    break;
                }
                catch (JsonException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        }

        return null;
    }

    private static bool? GetBooleanProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(name) && !property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var value) => value,
                _ => null
            };
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> GetPublishResultPaths(
        string? outputFolder,
        IReadOnlyCollection<PipelineStageExecution> stages,
        string stageName,
        string fileName)
    {
        var stageOutputPath = stages
            .Where(s => s.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedUtc)
            .Select(s => s.OutputPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (!string.IsNullOrWhiteSpace(stageOutputPath))
            yield return stageOutputPath!;

        if (!string.IsNullOrWhiteSpace(outputFolder))
            yield return Path.Combine(outputFolder, fileName);
    }

    private static bool IsAllowedPublishedUrl(string? url)
        => IsYouTubePublishedUrl(url, isShort: null) || IsFacebookReelUrl(url) || IsInstagramPermalinkUrl(url);

    private static bool IsYouTubePublishedUrl(string? url, bool? isShort)
    {
        if (!TryGetHttpsHostAndPath(url, out var host, out var path))
            return false;

        var isYouTubeHost = host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        if (!isYouTubeHost)
            return false;

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return isShort is not true;

        return isShort switch
        {
            true => path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase),
            false => path.Equals("/watch", StringComparison.OrdinalIgnoreCase),
            _ => path.Equals("/watch", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool IsFacebookReelUrl(string? url)
    {
        if (!TryGetHttpsHostAndPath(url, out var host, out var path))
            return false;

        return (host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase))
            && path.StartsWith("/reel/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInstagramPermalinkUrl(string? url)
    {
        if (!TryGetHttpsHostAndPath(url, out var host, out var path))
            return false;

        return (host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase))
            && (path.StartsWith("/reel/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/p/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetHttpsHostAndPath(string? url, out string host, out string path)
    {
        host = string.Empty;
        path = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        host = uri.Host;
        path = uri.AbsolutePath;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record StatusMetaPublishResult(bool Success, string Platform, string? VideoId, string? PostId, string? Url);

    private sealed record PublishedUrlStatus(IReadOnlyCollection<string> Urls, IReadOnlyCollection<string> Warnings);

    private static PipelineStageStatusDto ToDto(PipelineStageExecution s)
        => new(s.StageName, s.Status, s.AttemptCount, s.MaxAttempts, s.StartedUtc, s.CompletedUtc, s.LastError, s.OutputPath, s.DiagnosticPath);

    private async Task WriteStateAsync(PipelineRun run, IReadOnlyCollection<PipelineStageExecution> stages, string? currentStage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.OutputFolder))
            return;
        Directory.CreateDirectory(run.OutputFolder);
        var failed = stages.FirstOrDefault(s => s.Status == PersistentStageStatuses.Failed);
        var payload = new
        {
            runId = run.Id,
            overallStatus = run.Status.ToString(),
            stages = stages.Select(ToDto),
            currentStage,
            failedStage = failed?.StageName,
            ThumbnailMode = ResolveThumbnailMode(run.OutputFolder),
            retryable = failed is not null && failed.AttemptCount < failed.MaxAttempts,
            resumeCommandSuggestion = $"POST /api/pipeline/resume/{run.Id}"
        };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "pipeline-state.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string? ResolveThumbnailMode(string outputFolder)
    {
        var selectionPath = Path.Combine(outputFolder, "thumbnails", "thumbnail-selection.json");
        if (!File.Exists(selectionPath)) selectionPath = Path.Combine(outputFolder, "thumbnail-selection.json");
        if (!File.Exists(selectionPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(selectionPath));
            if (doc.RootElement.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String) return mode.GetString();
            if (doc.RootElement.TryGetProperty("Mode", out var pascalMode) && pascalMode.ValueKind == JsonValueKind.String) return pascalMode.GetString();
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task WritePublishIdempotencyAsync(PipelineRun run, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.OutputFolder))
            return;
        var published = await _repository.GetPublishedVideosByRunAsync(run.Id, cancellationToken);
        var payload = new
        {
            runId = run.Id,
            checkedAtUtc = DateTimeOffset.UtcNow,
            youtubeAlreadyPublished = published.Any(x => !string.IsNullOrWhiteSpace(x.YouTubeVideoId) && x.Status.Equals("Published", StringComparison.OrdinalIgnoreCase)),
            published = published.Select(x => new { x.Id, x.YouTubeVideoId, x.BlobUrl, x.Status })
        };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "publish-idempotency-check.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
