using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class ContentPublishService : IContentPublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IPipelineRepository _repository;
    private readonly IYouTubePublishService _youTubePublishService;
    private readonly PublishingOptions _publishingOptions;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly TokenHealthOptions _tokenHealthOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ITokenHealthService _tokenHealthService;
    private readonly ILogger<ContentPublishService> _logger;

    public ContentPublishService(
        IPipelineRepository repository,
        IYouTubePublishService youTubePublishService,
        ITokenHealthService tokenHealthService,
        IOptions<PublishingOptions> publishingOptions,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<TokenHealthOptions> tokenHealthOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<ContentPublishService> logger)
    {
        _repository = repository;
        _youTubePublishService = youTubePublishService;
        _tokenHealthService = tokenHealthService;
        _publishingOptions = publishingOptions.Value;
        _youTubeOptions = youTubeOptions.Value;
        _tokenHealthOptions = tokenHealthOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => PublishForPipelineRunAsync(pipelineRunId, "all", cancellationToken);

    public async Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        if (mode == "Disabled")
        {
            return [new PublishResult { Success = false, Platform = "YouTube", Mode = mode, Error = "Publishing is disabled.", PublishedUtc = DateTime.UtcNow }];
        }

        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
        {
            return [new PublishResult { Success = false, Platform = "YouTube", Mode = mode, Error = $"Pipeline run {pipelineRunId} was not found.", PublishedUtc = DateTime.UtcNow }];
        }

        var outputDirectory = ResolveOutputDirectory(run);
        Directory.CreateDirectory(outputDirectory);
        var selector = NormalizeAssetSelector(asset);
        var assets = await BuildAssetsAsync(run, outputDirectory, mode, selector, cancellationToken);
        var diagnostics = BuildAssetDiagnostics(assets, selector).ToList();
        var results = new List<PublishResult>();

        if (mode == "Disabled")
        {
            // Defensive; the early return above should already handle this.
            await WriteAssetsAsync(outputDirectory, diagnostics, cancellationToken);
            return [new PublishResult { Success = false, Platform = "YouTube", Mode = mode, Error = "Publishing is disabled.", PublishedUtc = DateTime.UtcNow }];
        }

        foreach (var publishAsset in assets)
        {
            var enabledSkipReason = GetAssetEnabledSkipReason(publishAsset, selector);
            if (enabledSkipReason is not null)
            {
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).WillUpload = false;
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).SkipReason = enabledSkipReason;
                results.Add(SkippedResult(publishAsset, mode, enabledSkipReason));
                continue;
            }

            var tokenHealthSkipReason = await GetTokenHealthSkipReasonAsync(cancellationToken);
            if (tokenHealthSkipReason is not null)
            {
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).WillUpload = false;
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).SkipReason = tokenHealthSkipReason;
                results.Add(SkippedResult(publishAsset, mode, tokenHealthSkipReason));
                continue;
            }

            var validationSkipReason = await GetValidationSkipReasonAsync(outputDirectory, run, publishAsset, mode, cancellationToken);
            if (validationSkipReason is not null)
            {
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).WillUpload = false;
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).SkipReason = validationSkipReason;
                results.Add(SkippedResult(publishAsset, mode, validationSkipReason));
                continue;
            }

            diagnostics.First(x => x.AssetType == publishAsset.AssetType).WillUpload = true;
            var youtubeShortEligible = publishAsset.YouTubeShortEligible;
            if (publishAsset.IsShort)
            {
                var shortValidation = await YouTubeShortsValidation.ValidateBeforeUploadAsync(publishAsset.VideoPath, null, cancellationToken);
                youtubeShortEligible = shortValidation.YouTubeShortEligible;
                diagnostics.First(x => x.AssetType == publishAsset.AssetType).YouTubeShortEligible = youtubeShortEligible;
                if (!shortValidation.YouTubeShortEligible)
                {
                    _logger.LogWarning("Short video at {VideoPath} is not eligible for YouTube Shorts: {Warnings}", publishAsset.VideoPath, string.Join("; ", shortValidation.Warnings));
                }
            }

            var request = new PublishRequest
            {
                PipelineRunId = pipelineRunId,
                Platform = "YouTube",
                AssetType = publishAsset.AssetType,
                IsShort = publishAsset.IsShort,
                VideoPath = publishAsset.VideoPath,
                ThumbnailPath = publishAsset.ThumbnailPath,
                Title = publishAsset.Title,
                Description = publishAsset.Description,
                Tags = publishAsset.Tags,
                PrivacyStatus = publishAsset.PrivacyStatus,
                UploadThumbnail = publishAsset.UploadThumbnail,
                YouTubeShortEligible = youtubeShortEligible
            };

            var result = await _youTubePublishService.PublishAsync(request, cancellationToken);
            results.Add(result);
        }

        await WriteAssetsAsync(outputDirectory, diagnostics, cancellationToken);
        await WriteCombinedResultsAsync(outputDirectory, results, cancellationToken);
        return results;
    }

    private async Task<IReadOnlyList<PublishAsset>> BuildAssetsAsync(PipelineRun run, string outputDirectory, string mode, string selector, CancellationToken cancellationToken)
    {
        var privacyStatus = ResolvePrivacyStatus(mode);
        if (selector == "short")
        {
            var shortVideoOnlyPath = ResolveShortVideoPath(outputDirectory);
            if (shortVideoOnlyPath is not null)
            {
                return [await BuildShortAssetAsync(run, outputDirectory, shortVideoOnlyPath, privacyStatus, cancellationToken)];
            }
        }

        var metadata = await ReadRequiredJsonAsync<SeoMetadataResult>(Path.Combine(outputDirectory, "seo-metadata.json"), cancellationToken);
        var longAsset = new PublishAsset
        {
            AssetType = "LongVideo",
            VideoPath = Path.Combine(outputDirectory, "final-video.mp4"),
            ThumbnailPath = await ResolveThumbnailPathAsync(outputDirectory, cancellationToken),
            Title = metadata.Title,
            Description = metadata.Description,
            Tags = SplitCsv(metadata.TagsCsv),
            PrivacyStatus = privacyStatus,
            UploadThumbnail = _publishingOptions.UploadThumbnail && _youTubeOptions.UploadThumbnailForLongVideos,
            IsShort = false
        };

        var assets = new List<PublishAsset> { longAsset };
        var shortVideoPath = ResolveShortVideoPath(outputDirectory);

        if (shortVideoPath is not null)
        {
            assets.Add(await BuildShortAssetAsync(run, outputDirectory, shortVideoPath, privacyStatus, cancellationToken, metadata));
        }

        return assets;
    }

    private async Task<PublishAsset> BuildShortAssetAsync(
        PipelineRun run,
        string outputDirectory,
        string shortVideoPath,
        string privacyStatus,
        CancellationToken cancellationToken,
        SeoMetadataResult? rootMetadata = null)
    {
        var shortMetadataPath = Path.Combine(outputDirectory, "shorts", "seo-metadata.json");
        var shortMetadata = File.Exists(shortMetadataPath)
            ? await ReadRequiredJsonAsync<SeoMetadataResult>(shortMetadataPath, cancellationToken)
            : rootMetadata is not null
                ? DeriveShortMetadata(rootMetadata, run)
                : await DeriveShortMetadataFromRootIfPresentAsync(outputDirectory, run, cancellationToken);
        var marker = EnsureShortsMarker(ShortenTitle(shortMetadata.Title), shortMetadata.Description);

        return new PublishAsset
        {
            AssetType = "ShortVideo",
            VideoPath = shortVideoPath,
            ThumbnailPath = await ResolveShortThumbnailPathAsync(outputDirectory, cancellationToken),
            Title = marker.Title,
            Description = marker.Description,
            Tags = SplitCsv(shortMetadata.TagsCsv),
            PrivacyStatus = privacyStatus,
            UploadThumbnail = _youTubeOptions.UploadThumbnailForShorts,
            IsShort = true
        };
    }

    private string? ResolveShortVideoPath(string outputDirectory)
    {
        var shortVideoPath = Path.Combine(outputDirectory, "shorts", "short-video.mp4");
        if (File.Exists(shortVideoPath))
        {
            return shortVideoPath;
        }

        var fallbackPath = Path.Combine(outputDirectory, "shorts", "final-video.mp4");
        if (File.Exists(fallbackPath))
        {
            _logger.LogWarning(
                "Short video publish asset was not found at {ShortVideoPath}; falling back to legacy path {FallbackPath}.",
                shortVideoPath,
                fallbackPath);
            return fallbackPath;
        }

        return null;
    }

    private IEnumerable<PublishAssetDiagnostic> BuildAssetDiagnostics(IReadOnlyList<PublishAsset> assets, string selector)
    {
        foreach (var asset in assets)
        {
            var skipReason = GetAssetEnabledSkipReason(asset, selector);
            yield return new PublishAssetDiagnostic
            {
                AssetType = asset.AssetType,
                VideoPath = asset.VideoPath,
                ThumbnailPath = string.IsNullOrWhiteSpace(asset.ThumbnailPath) ? null : asset.ThumbnailPath,
                WillUpload = skipReason is null,
                SkipReason = skipReason
            };
        }
    }

    private string? GetAssetEnabledSkipReason(PublishAsset asset, string selector)
    {
        if (selector == "long" && asset.IsShort)
        {
            return "Skipped because asset=long was requested.";
        }

        if (selector == "short" && !asset.IsShort)
        {
            return "Skipped because asset=short was requested.";
        }

        if (!asset.IsShort && !_publishingOptions.PublishLongVideo)
        {
            return "Skipped because Publishing.PublishLongVideo is false.";
        }

        if (asset.IsShort && !_publishingOptions.PublishShortVideo)
        {
            return "Skipped because Publishing.PublishShortVideo is false.";
        }

        return null;
    }

    private async Task<string?> GetTokenHealthSkipReasonAsync(CancellationToken cancellationToken)
    {
        if (!_tokenHealthOptions.Enabled || !_tokenHealthOptions.CheckBeforePublish)
        {
            return null;
        }

        var health = await _tokenHealthService.CheckYouTubeAsync(cancellationToken);
        if (health.IsValid)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(health.Error) ? health.Warning : health.Error;
        return string.IsNullOrWhiteSpace(reason)
            ? "YouTube token health check failed."
            : $"YouTube token health check failed: {reason}";
    }

    private async Task<string?> GetValidationSkipReasonAsync(string outputDirectory, PipelineRun run, PublishAsset asset, string mode, CancellationToken cancellationToken)
    {
        if (!File.Exists(asset.VideoPath))
        {
            return $"Video file is missing: {asset.VideoPath}";
        }

        if (!_publishingOptions.RequirePrePublishValidation)
        {
            return null;
        }

        var reportPath = asset.IsShort
            ? Path.Combine(outputDirectory, "shorts", "pre-publish-validation-report.json")
            : Path.Combine(outputDirectory, "pre-publish-validation-report.json");

        if (!File.Exists(reportPath) && asset.IsShort)
        {
            reportPath = Path.Combine(outputDirectory, "pre-publish-validation-report.json");
        }

        if (!File.Exists(reportPath))
        {
            return "Pre-publish validation report is missing.";
        }

        var report = await ReadRequiredJsonAsync<PrePublishValidationReport>(reportPath, cancellationToken);
        if (report.Passed)
        {
            return null;
        }

        var reason = report.Errors.Count > 0
            ? $"Pre-publish validation failed: {string.Join("; ", report.Errors)}"
            : "Pre-publish validation failed.";
        run.FailureReason = reason;
        await _repository.SaveChangesAsync(cancellationToken);
        return reason;
    }

    private static PublishResult SkippedResult(PublishAsset asset, string mode, string reason)
        => new()
        {
            Success = false,
            Platform = "YouTube",
            AssetType = asset.AssetType,
            IsShort = asset.IsShort,
            Mode = mode,
            Error = reason,
            PublishedUtc = DateTime.UtcNow
        };

    private static async Task<T> ReadRequiredJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required publish artifact is missing: {Path.GetFileName(path)}", path);
        }

        var value = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        return value ?? throw new InvalidOperationException($"Required publish artifact is invalid: {Path.GetFileName(path)}");
    }

    private async Task<string> ResolveThumbnailPathAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var selectionPath = Path.Combine(outputDirectory, "thumbnail-selection.json");
        if (!File.Exists(selectionPath))
        {
            selectionPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-selection.json");
        }

        if (File.Exists(selectionPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(selectionPath, cancellationToken));
            foreach (var propertyName in new[] { "preferredThumbnailPath", "selectedThumbnailPath", "thumbnailPath" })
            {
                if (doc.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var candidate = property.GetString();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return Path.IsPathRooted(candidate) ? candidate : Path.Combine(outputDirectory, candidate);
                    }
                }
            }
        }

        var fallback = Path.Combine(outputDirectory, "thumbnail-1.png");
        return File.Exists(fallback) ? fallback : Path.Combine(outputDirectory, "thumbnails", "thumbnail-1.png");
    }

    private async Task<string> ResolveShortThumbnailPathAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var shortsDirectory = Path.Combine(outputDirectory, "shorts");
        var selectionPath = Path.Combine(shortsDirectory, "thumbnail-selection.json");
        if (File.Exists(selectionPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(selectionPath, cancellationToken));
            foreach (var propertyName in new[] { "preferredThumbnailPath", "selectedThumbnailPath", "thumbnailPath" })
            {
                if (doc.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var candidate = property.GetString();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return Path.IsPathRooted(candidate) ? candidate : Path.Combine(shortsDirectory, candidate);
                    }
                }
            }
        }

        foreach (var candidate in new[]
        {
            Path.Combine(shortsDirectory, "thumbnail-1.png"),
            Path.Combine(shortsDirectory, "short-cover-1.png"),
            Path.Combine(shortsDirectory, "thumbnails", "thumbnail-1.png")
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private string ResolveOutputDirectory(PipelineRun run)
        => Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));

    private string ResolvePrivacyStatus(string mode)
        => mode == "Public"
            ? "public"
            : mode == "Private"
                ? "private"
                : string.Equals(_publishingOptions.DefaultPrivacyStatus, "unlisted", StringComparison.OrdinalIgnoreCase)
                    ? "unlisted"
                    : string.Equals(_youTubeOptions.DefaultPrivacyStatus, "unlisted", StringComparison.OrdinalIgnoreCase)
                        ? "unlisted"
                        : "private";

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.Ordinal) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private static string NormalizeAssetSelector(string? asset)
        => string.Equals(asset, "long", StringComparison.OrdinalIgnoreCase) ? "long"
            : string.Equals(asset, "short", StringComparison.OrdinalIgnoreCase) ? "short"
            : "all";

    private static List<string> SplitCsv(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SeoMetadataResult DeriveShortMetadata(SeoMetadataResult root, PipelineRun run)
    {
        var title = ShortenTitle(root.Title);
        var marker = EnsureShortsMarker(title, root.Description);
        return new SeoMetadataResult
        {
            Title = marker.Title,
            Description = marker.Description,
            TagsCsv = string.Join(',', SplitCsv(root.TagsCsv).Concat(["shorts", "youtube shorts"]).Distinct(StringComparer.OrdinalIgnoreCase)),
            HashtagsCsv = "#Shorts",
            PinnedComment = root.PinnedComment
        };
    }


    private static async Task<SeoMetadataResult> DeriveShortMetadataFromRootIfPresentAsync(string outputDirectory, PipelineRun run, CancellationToken cancellationToken)
    {
        var rootMetadataPath = Path.Combine(outputDirectory, "seo-metadata.json");
        if (File.Exists(rootMetadataPath))
        {
            var rootMetadata = await ReadRequiredJsonAsync<SeoMetadataResult>(rootMetadataPath, cancellationToken);
            return DeriveShortMetadata(rootMetadata, run);
        }

        var fallback = new SeoMetadataResult
        {
            Title = BuildFallbackShortTitle(run),
            Description = BuildFallbackShortDescription(run),
            TagsCsv = "astronomy,night sky,shorts,youtube shorts",
            HashtagsCsv = "#Shorts",
            PinnedComment = string.Empty
        };

        return DeriveShortMetadata(fallback, run);
    }

    private static string BuildFallbackShortTitle(PipelineRun run)
        => string.IsNullOrWhiteSpace(run.LocationName)
            ? $"Tonight's Sky - {run.RunDate:MMM d} #Shorts"
            : $"Tonight's Sky Over {run.LocationName} - {run.RunDate:MMM d} #Shorts";

    private static string BuildFallbackShortDescription(PipelineRun run)
        => string.IsNullOrWhiteSpace(run.LocationName)
            ? $"Quick night-sky highlights for {run.RunDate:MMMM d, yyyy}. #Shorts"
            : $"Quick night-sky highlights for {run.LocationName} on {run.RunDate:MMMM d, yyyy}. #Shorts";

    private static string ShortenTitle(string title)
    {
        var fallback = "Tonight's Sky #Shorts";
        if (string.IsNullOrWhiteSpace(title))
        {
            return fallback;
        }

        var trimmed = title.Trim();
        if (trimmed.Length <= 80)
        {
            return trimmed;
        }

        var shortened = trimmed[..Math.Min(72, trimmed.Length)].TrimEnd(' ', '-', ':', '|');
        return string.IsNullOrWhiteSpace(shortened) ? fallback : shortened;
    }

    private static (string Title, string Description) EnsureShortsMarker(string title, string description)
        => (title, YouTubeShortsValidation.EnsureShortsMarkerInDescription(title, description));

    private static Task WriteAssetsAsync(string outputDirectory, IReadOnlyCollection<PublishAssetDiagnostic> diagnostics, CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-assets.json"), diagnostics, cancellationToken);

    private static async Task WriteCombinedResultsAsync(string outputDirectory, IReadOnlyCollection<PublishResult> results, CancellationToken cancellationToken)
    {
        var combined = results.ToList();
        foreach (var fileName in new[] { "youtube-publish-result-long.json", "youtube-publish-result-short.json" })
        {
            var path = Path.Combine(outputDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var existing = JsonSerializer.Deserialize<PublishResult>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
            if (existing is null)
            {
                continue;
            }

            var index = combined.FindIndex(x => x.AssetType.Equals(existing.AssetType, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                if (existing.Success || !combined[index].Success)
                {
                    combined[index] = existing;
                }
            }
            else
            {
                combined.Add(existing);
            }
        }

        await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-results.json"), combined, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
    }

    private sealed class PublishAssetDiagnostic
    {
        public string AssetType { get; init; } = string.Empty;
        public string VideoPath { get; init; } = string.Empty;
        public string? ThumbnailPath { get; init; }
        public bool WillUpload { get; set; }
        public string? SkipReason { get; set; }
        public bool? YouTubeShortEligible { get; set; }
    }
}
