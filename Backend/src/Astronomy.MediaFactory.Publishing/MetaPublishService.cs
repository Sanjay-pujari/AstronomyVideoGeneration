using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class MetaPublishService : IMetaPublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IPipelineRepository _repository;
    private readonly IFacebookReelPublishService _facebookReelPublishService;
    private readonly IInstagramReelPublishService _instagramReelPublishService;
    private readonly MetaPublishingOptions _options;
    private readonly TokenHealthOptions _tokenHealthOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ITokenHealthService _tokenHealthService;
    private readonly IPlatformThumbnailResolver _thumbnailResolver;
    private readonly IMetaPosterFrameFallbackService? _posterFrameFallbackService;
    private readonly ILogger<MetaPublishService> _logger;

    public MetaPublishService(
        IPipelineRepository repository,
        IFacebookReelPublishService facebookReelPublishService,
        IInstagramReelPublishService instagramReelPublishService,
        ITokenHealthService tokenHealthService,
        IOptions<MetaPublishingOptions> options,
        IOptions<TokenHealthOptions> tokenHealthOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<MetaPublishService> logger,
        IPlatformThumbnailResolver? thumbnailResolver = null,
        IMetaPosterFrameFallbackService? posterFrameFallbackService = null)
    {
        _repository = repository;
        _facebookReelPublishService = facebookReelPublishService;
        _instagramReelPublishService = instagramReelPublishService;
        _tokenHealthService = tokenHealthService;
        _options = options.Value;
        _tokenHealthOptions = tokenHealthOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
        _thumbnailResolver = thumbnailResolver ?? new PlatformThumbnailResolver(Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformThumbnailResolver>.Instance);
        _posterFrameFallbackService = posterFrameFallbackService;
    }

    public async Task<IReadOnlyList<MetaPublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset = "all", CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(_options.Mode);
        if (mode == "Disabled")
        {
            return [new MetaPublishResult { Success = false, Platform = "Meta", Mode = mode, Error = "Meta publishing is disabled.", PublishedUtc = DateTime.UtcNow }];
        }

        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
        {
            return [new MetaPublishResult { Success = false, Platform = "Meta", Mode = mode, Error = $"Pipeline run {pipelineRunId} was not found.", PublishedUtc = DateTime.UtcNow }];
        }

        var selector = NormalizeAsset(asset);
        if (selector != "all" && selector != "facebook-reel" && selector != "instagram-reel")
        {
            return [new MetaPublishResult { Success = false, Platform = "Meta", Mode = mode, Error = $"Unsupported Meta publish asset '{asset}'. Supported assets are facebook-reel, instagram-reel, and all.", PublishedUtc = DateTime.UtcNow }];
        }

        var publishFacebook = (selector == "all" || selector == "facebook-reel") && _options.PublishFacebookReel;
        var publishInstagram = (selector == "all" || selector == "instagram-reel") && _options.PublishInstagramReel;
        if (!publishFacebook && !publishInstagram)
        {
            var platform = selector == "instagram-reel" ? "Instagram" : selector == "facebook-reel" ? "Facebook" : "Meta";
            return [new MetaPublishResult { Success = false, Platform = platform, Mode = mode, Error = "Requested Meta Reel publishing is disabled by configuration.", PublishedUtc = DateTime.UtcNow }];
        }

        if (_options.PublishFacebookFullVideo || _options.PublishInstagramFullVideo)
        {
            _logger.LogWarning("Meta full-video publishing flags are ignored. Only shorts/short-video.mp4 is eligible for Facebook and Instagram Reels.");
        }

        var tokenHealthBlock = await GetTokenHealthBlockReasonAsync(cancellationToken);
        if (tokenHealthBlock is not null)
        {
            var blockedResults = new List<MetaPublishResult>();
            if (publishFacebook)
            {
                blockedResults.Add(new MetaPublishResult { Success = false, Platform = "Facebook", Mode = mode, Error = tokenHealthBlock, PublishedUtc = DateTime.UtcNow });
            }

            if (publishInstagram)
            {
                blockedResults.Add(new MetaPublishResult { Success = false, Platform = "Instagram", Mode = mode, Error = tokenHealthBlock, PublishedUtc = DateTime.UtcNow });
            }

            return blockedResults;
        }

        var outputDirectory = ResolveOutputDirectory(run);
        Directory.CreateDirectory(outputDirectory);
        var videoPath = Path.Combine(outputDirectory, "shorts", "short-video.mp4");
        MetaPosterFrameFallbackResult? posterFrameFallback = null;
        var facebookThumbnail = await _thumbnailResolver.ResolveAsync(outputDirectory, "Facebook", PlatformThumbnailContentTypes.Reel, cancellationToken);
        var instagramThumbnail = await _thumbnailResolver.ResolveAsync(outputDirectory, "Instagram", PlatformThumbnailContentTypes.Reel, cancellationToken);
        _logger.LogInformation("Platform thumbnail resolved: {@ThumbnailResolution}", new { Platform = "Facebook", ContentKind = PlatformThumbnailContentTypes.Reel, ResolvedThumbnailPath = facebookThumbnail.PlatformThumbnailPath, ThumbnailSource = facebookThumbnail.ThumbnailSource, Exists = !string.IsNullOrWhiteSpace(facebookThumbnail.PlatformThumbnailPath) && File.Exists(facebookThumbnail.PlatformThumbnailPath), Size = !string.IsNullOrWhiteSpace(facebookThumbnail.PlatformThumbnailPath) && File.Exists(facebookThumbnail.PlatformThumbnailPath) ? new FileInfo(facebookThumbnail.PlatformThumbnailPath).Length : 0 });
        _logger.LogInformation("Platform thumbnail resolved: {@ThumbnailResolution}", new { Platform = "Instagram", ContentKind = PlatformThumbnailContentTypes.Reel, ResolvedThumbnailPath = instagramThumbnail.PlatformThumbnailPath, ThumbnailSource = instagramThumbnail.ThumbnailSource, Exists = !string.IsNullOrWhiteSpace(instagramThumbnail.PlatformThumbnailPath) && File.Exists(instagramThumbnail.PlatformThumbnailPath), Size = !string.IsNullOrWhiteSpace(instagramThumbnail.PlatformThumbnailPath) && File.Exists(instagramThumbnail.PlatformThumbnailPath) ? new FileInfo(instagramThumbnail.PlatformThumbnailPath).Length : 0 });
        await WritePlatformThumbnailResolutionReportAsync(outputDirectory, new[]
        {
            new PlatformThumbnailResolutionReportEntry { Platform = "Facebook", ContentKind = PlatformThumbnailContentTypes.Reel, ResolvedThumbnailPath = facebookThumbnail.PlatformThumbnailPath, ThumbnailSource = facebookThumbnail.ThumbnailSource, Exists = !string.IsNullOrWhiteSpace(facebookThumbnail.PlatformThumbnailPath) && File.Exists(facebookThumbnail.PlatformThumbnailPath), Size = !string.IsNullOrWhiteSpace(facebookThumbnail.PlatformThumbnailPath) && File.Exists(facebookThumbnail.PlatformThumbnailPath) ? new FileInfo(facebookThumbnail.PlatformThumbnailPath).Length : 0 },
            new PlatformThumbnailResolutionReportEntry { Platform = "Instagram", ContentKind = PlatformThumbnailContentTypes.Reel, ResolvedThumbnailPath = instagramThumbnail.PlatformThumbnailPath, ThumbnailSource = instagramThumbnail.ThumbnailSource, Exists = !string.IsNullOrWhiteSpace(instagramThumbnail.PlatformThumbnailPath) && File.Exists(instagramThumbnail.PlatformThumbnailPath), Size = !string.IsNullOrWhiteSpace(instagramThumbnail.PlatformThumbnailPath) && File.Exists(instagramThumbnail.PlatformThumbnailPath) ? new FileInfo(instagramThumbnail.PlatformThumbnailPath).Length : 0 }
        }, cancellationToken);

        if (_options.UsePosterFrameFallbackForReels && (publishFacebook || publishInstagram))
        {
            var posterFrameImagePath = ResolveExistingPosterFrameImagePath(facebookThumbnail.ShortThumbnailPath, instagramThumbnail.ShortThumbnailPath, facebookThumbnail.PlatformThumbnailPath, instagramThumbnail.PlatformThumbnailPath, Path.Combine(outputDirectory, "thumbnails", "thumbnail-short.jpg"));
            if (!string.IsNullOrWhiteSpace(posterFrameImagePath) && File.Exists(videoPath) && _posterFrameFallbackService is not null)
            {
                try
                {
                    posterFrameFallback = await _posterFrameFallbackService.ApplyAsync(outputDirectory, videoPath, posterFrameImagePath, _options.PosterFrameDurationSeconds, cancellationToken);
                    videoPath = posterFrameFallback.OutputMetaVideoPath;
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    _logger.LogWarning(ex, "Meta poster-frame fallback generation failed; publishing will continue with the original short video.");
                    posterFrameFallback = await WriteUnappliedPosterFrameReportAsync(outputDirectory, videoPath, posterFrameImagePath, _options.PosterFrameDurationSeconds, cancellationToken);
                }
            }
            else
            {
                posterFrameFallback = await WriteUnappliedPosterFrameReportAsync(outputDirectory, videoPath, posterFrameImagePath ?? string.Empty, _options.PosterFrameDurationSeconds, cancellationToken);
            }
        }

        var results = new List<MetaPublishResult>();

        if (publishFacebook)
        {
            var metadata = await ReadFacebookMetadataAsync(outputDirectory, cancellationToken);
            var caption = await BuildFacebookCaptionAsync(outputDirectory, run, metadata, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-caption.txt"), caption, cancellationToken);

            var request = new MetaPublishRequest
            {
                PipelineRunId = pipelineRunId,
                Platform = "Facebook",
                VideoPath = videoPath,
                LongThumbnailPath = facebookThumbnail.LongThumbnailPath,
                ShortThumbnailPath = facebookThumbnail.ShortThumbnailPath,
                PlatformThumbnailPath = posterFrameFallback?.PosterFrameApplied == true ? string.Empty : facebookThumbnail.PlatformThumbnailPath,
                ThumbnailSource = facebookThumbnail.ThumbnailSource,
                PosterFrameApplied = posterFrameFallback?.PosterFrameApplied == true,
                PosterFrameImagePath = posterFrameFallback?.PosterFrameImagePath ?? string.Empty,
                PosterFrameDurationSeconds = posterFrameFallback?.PosterFrameDurationSeconds ?? 0d,
                Caption = caption,
                ShortTitle = BuildFacebookShortTitle(metadata?.Title, caption),
                IsReel = true
            };

            var result = await _facebookReelPublishService.PublishReelAsync(request, cancellationToken);
            _logger.LogInformation("Facebook Reel publish for run {PipelineRunId} completed with success={Success} mode={Mode}.", pipelineRunId, result.Success, result.Mode);
            results.Add(result);
        }

        if (publishInstagram)
        {
            var metadata = await ReadInstagramMetadataAsync(outputDirectory, cancellationToken);
            var caption = await BuildInstagramCaptionAsync(outputDirectory, run, metadata, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-reel-caption.txt"), caption, cancellationToken);

            var request = new MetaPublishRequest
            {
                PipelineRunId = pipelineRunId,
                Platform = "Instagram",
                VideoPath = videoPath,
                LongThumbnailPath = instagramThumbnail.LongThumbnailPath,
                ShortThumbnailPath = instagramThumbnail.ShortThumbnailPath,
                PlatformThumbnailPath = posterFrameFallback?.PosterFrameApplied == true ? string.Empty : instagramThumbnail.PlatformThumbnailPath,
                ThumbnailSource = instagramThumbnail.ThumbnailSource,
                PosterFrameApplied = posterFrameFallback?.PosterFrameApplied == true,
                PosterFrameImagePath = posterFrameFallback?.PosterFrameImagePath ?? string.Empty,
                PosterFrameDurationSeconds = posterFrameFallback?.PosterFrameDurationSeconds ?? 0d,
                Caption = caption,
                ShortTitle = BuildFacebookShortTitle(metadata?.Title, caption),
                IsReel = true
            };

            var result = await _instagramReelPublishService.PublishReelAsync(request, cancellationToken);
            _logger.LogInformation("Instagram Reel publish for run {PipelineRunId} completed with success={Success} mode={Mode}.", pipelineRunId, result.Success, result.Mode);
            results.Add(result);
        }

        return results;
    }


    private static string? ResolveExistingPosterFrameImagePath(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));

    private static async Task<MetaPosterFrameFallbackResult> WriteUnappliedPosterFrameReportAsync(string outputDirectory, string inputShortVideoPath, string posterFrameImagePath, double durationSeconds, CancellationToken cancellationToken)
    {
        var result = new MetaPosterFrameFallbackResult(
            PosterFrameApplied: false,
            PosterFrameImagePath: posterFrameImagePath,
            PosterFrameDurationSeconds: Math.Clamp(durationSeconds, 0.5d, 1.0d),
            InputShortVideoPath: inputShortVideoPath,
            OutputMetaVideoPath: Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputShortVideoPath)) ?? outputDirectory, "short-video-meta.mp4"),
            Reason: MetaPosterFrameFallbackService.Reason);
        await MetaPosterFrameFallbackService.WriteReportAsync(outputDirectory, result, cancellationToken);
        return result;
    }

    private static async Task WritePlatformThumbnailResolutionReportAsync(string outputDirectory, IEnumerable<PlatformThumbnailResolutionReportEntry> entries, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputDirectory, "platform-thumbnail-resolution-report.json");
        var combined = new List<PlatformThumbnailResolutionReportEntry>();
        if (File.Exists(path))
        {
            try
            {
                combined.AddRange(JsonSerializer.Deserialize<List<PlatformThumbnailResolutionReportEntry>>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? []);
            }
            catch (JsonException)
            {
            }
        }

        foreach (var entry in entries)
        {
            combined.RemoveAll(x => x.Platform.Equals(entry.Platform, StringComparison.OrdinalIgnoreCase) && x.ContentKind.Equals(entry.ContentKind, StringComparison.OrdinalIgnoreCase));
            combined.Add(entry);
            // Structured pre-publish diagnostic is intentionally mirrored to the report file.
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(combined.OrderBy(x => x.Platform).ThenBy(x => x.ContentKind).ToList(), JsonOptions), cancellationToken);
    }

    private async Task<string?> GetTokenHealthBlockReasonAsync(CancellationToken cancellationToken)
    {
        if (!_tokenHealthOptions.Enabled || !_tokenHealthOptions.CheckBeforePublish)
        {
            return null;
        }

        var health = await _tokenHealthService.CheckMetaAsync(cancellationToken);
        if (health.IsValid)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(health.Error) ? health.Warning : health.Error;
        return string.IsNullOrWhiteSpace(reason)
            ? "Meta token health check failed."
            : $"Meta token health check failed: {reason}";
    }

    private static async Task<SeoMetadataResult?> ReadFacebookMetadataAsync(string outputDirectory, CancellationToken cancellationToken)
        => await TryReadMetadataAsync(Path.Combine(outputDirectory, "shorts", "seo-metadata.json"), cancellationToken)
            ?? await TryReadMetadataAsync(Path.Combine(outputDirectory, "seo-metadata.json"), cancellationToken);

    private static async Task<SeoMetadataResult?> ReadInstagramMetadataAsync(string outputDirectory, CancellationToken cancellationToken)
        => await TryReadMetadataAsync(Path.Combine(outputDirectory, "shorts", "seo-metadata.json"), cancellationToken)
            ?? await TryReadMetadataAsync(Path.Combine(outputDirectory, "seo-metadata.json"), cancellationToken);

    private async Task<string> BuildFacebookCaptionAsync(string outputDirectory, PipelineRun run, SeoMetadataResult? metadata, CancellationToken cancellationToken)
    {
        var selectedObjects = await ReadSelectedObjectsAsync(outputDirectory, cancellationToken);
        var location = string.IsNullOrWhiteSpace(run.LocationName) ? "your night sky" : run.LocationName.Trim();
        var hook = !string.IsNullOrWhiteSpace(metadata?.Title)
            ? metadata!.Title.Trim()
            : $"Tonight's sky from {location}";

        var lines = new List<string> { hook };
        if (!string.IsNullOrWhiteSpace(location))
        {
            lines.Add($"Location: {location}");
        }

        if (selectedObjects.Count > 0)
        {
            lines.Add($"Featured objects: {string.Join(", ", selectedObjects.Take(6))}");
        }

        var descriptionLine = BuildDescriptionLine(metadata?.Description);
        if (!string.IsNullOrWhiteSpace(descriptionLine))
        {
            lines.Add(descriptionLine);
        }

        var suffix = string.IsNullOrWhiteSpace(_options.CaptionHashtagSuffix)
            ? "#Astronomy #NightSky #Stargazing"
            : _options.CaptionHashtagSuffix.Trim();
        lines.Add(suffix);

        var caption = string.Join(Environment.NewLine, lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        return caption.Length <= 1800 ? caption : caption[..1800].TrimEnd();
    }


    private async Task<string> BuildInstagramCaptionAsync(string outputDirectory, PipelineRun run, SeoMetadataResult? metadata, CancellationToken cancellationToken)
    {
        var selectedObjects = await ReadSelectedObjectsAsync(outputDirectory, cancellationToken);
        var location = string.IsNullOrWhiteSpace(run.LocationName) ? "your night sky" : run.LocationName.Trim();
        var hook = !string.IsNullOrWhiteSpace(metadata?.Title)
            ? metadata!.Title.Trim()
            : $"Tonight's sky from {location}";

        var lines = new List<string> { hook };
        if (!string.IsNullOrWhiteSpace(location))
        {
            lines.Add($"Location: {location}");
        }

        if (selectedObjects.Count > 0)
        {
            lines.Add($"Featured: {string.Join(", ", selectedObjects.Take(5))}");
        }

        var hashtags = EnsureInstagramHashtags(_options.CaptionHashtagSuffix);
        lines.Add(hashtags);

        var caption = string.Join(Environment.NewLine, lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        return caption.Length <= 1800 ? caption : caption[..1800].TrimEnd();
    }

    private static string EnsureInstagramHashtags(string? configuredSuffix)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredSuffix))
        {
            tags.AddRange(configuredSuffix.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var required in new[] { "#Astronomy", "#NightSky", "#Stargazing", "#Reels" })
        {
            if (!tags.Any(x => x.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                tags.Add(required);
            }
        }

        return string.Join(' ', tags);
    }


    private static string BuildFacebookShortTitle(string? metadataTitle, string caption)
    {
        var title = !string.IsNullOrWhiteSpace(metadataTitle)
            ? metadataTitle.Trim()
            : caption.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Astronomy Reel";

        return title.Length <= 120 ? title : title[..120].TrimEnd();
    }

    private static string BuildDescriptionLine(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var line = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => !x.StartsWith("Location:", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("Featured objects:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        return line.Length <= 260 ? line : line[..260].TrimEnd();
    }

    private static async Task<SeoMetadataResult?> TryReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SeoMetadataResult>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<List<string>> ReadSelectedObjectsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var values = new List<string>();
        foreach (var fileName in new[] { "selected-visible-objects.json", "scene-observation-context.json" })
        {
            var path = Path.Combine(outputDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                CollectObjectNames(doc.RootElement, values);
            }
            catch (JsonException)
            {
            }
        }

        return values
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static void CollectObjectNames(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.NameEquals("objectName") || property.NameEquals("ObjectName")) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                    else
                    {
                        CollectObjectNames(property.Value, values);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                    else
                    {
                        CollectObjectNames(item, values);
                    }
                }
                break;
        }
    }

    private string ResolveOutputDirectory(PipelineRun run)
    {
        if (!string.IsNullOrWhiteSpace(run.OutputFolder))
        {
            return run.OutputFolder;
        }

        var regionAwarePath = PipelineOrchestrator.BuildOutputDirectory(
            _maintenanceOptions.WorkingDirectory,
            run.ContentType,
            run.RunDate,
            run.RegionId,
            run.LocationName,
            run.Id);
        if (Directory.Exists(regionAwarePath))
        {
            return regionAwarePath;
        }

        return Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
    }

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private static string NormalizeAsset(string? asset)
        => string.IsNullOrWhiteSpace(asset) || string.Equals(asset, "all", StringComparison.OrdinalIgnoreCase) ? "all"
            : string.Equals(asset, "facebook-reel", StringComparison.OrdinalIgnoreCase) ? "facebook-reel"
            : string.Equals(asset, "instagram-reel", StringComparison.OrdinalIgnoreCase) ? "instagram-reel"
            : asset.Trim();
}
