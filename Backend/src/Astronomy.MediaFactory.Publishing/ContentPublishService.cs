using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class ContentPublishService : IContentPublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly IPipelineRepository _repository;
    private readonly IYouTubePublishService _youTubePublishService;
    private readonly PublishingOptions _publishingOptions;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly MaintenanceOptions _maintenanceOptions;

    public ContentPublishService(
        IPipelineRepository repository,
        IYouTubePublishService youTubePublishService,
        IOptions<PublishingOptions> publishingOptions,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<MaintenanceOptions> maintenanceOptions)
    {
        _repository = repository;
        _youTubePublishService = youTubePublishService;
        _publishingOptions = publishingOptions.Value;
        _youTubeOptions = youTubeOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
    }

    public async Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
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
        if (_publishingOptions.RequirePrePublishValidation)
        {
            var validationResult = await ValidatePrePublishReportAsync(outputDirectory, run, mode, cancellationToken);
            if (validationResult is not null)
            {
                return [validationResult];
            }
        }

        var metadata = await ReadRequiredJsonAsync<SeoMetadataResult>(Path.Combine(outputDirectory, "seo-metadata.json"), cancellationToken);
        var request = new PublishRequest
        {
            PipelineRunId = pipelineRunId,
            Platform = "YouTube",
            VideoPath = Path.Combine(outputDirectory, "final-video.mp4"),
            ThumbnailPath = await ResolveThumbnailPathAsync(outputDirectory, cancellationToken),
            Title = metadata.Title,
            Description = metadata.Description,
            Tags = SplitCsv(metadata.TagsCsv),
            PrivacyStatus = ResolvePrivacyStatus(mode),
            UploadThumbnail = _publishingOptions.UploadThumbnail
        };

        return [await _youTubePublishService.PublishAsync(request, cancellationToken)];
    }

    private async Task<PublishResult?> ValidatePrePublishReportAsync(string outputDirectory, PipelineRun run, string mode, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(outputDirectory, "pre-publish-validation-report.json");
        if (!File.Exists(reportPath))
        {
            return await WriteBlockedResultAsync(outputDirectory, mode, "Pre-publish validation report is missing.", cancellationToken);
        }

        var report = await ReadRequiredJsonAsync<PrePublishValidationReport>(reportPath, cancellationToken);
        if (!report.Passed)
        {
            var reason = report.Errors.Count > 0
                ? $"Pre-publish validation failed: {string.Join("; ", report.Errors)}"
                : "Pre-publish validation failed.";
            run.FailureReason = reason;
            await _repository.SaveChangesAsync(cancellationToken);
            return await WriteBlockedResultAsync(outputDirectory, mode, reason, cancellationToken);
        }

        return null;
    }

    private static async Task<PublishResult> WriteBlockedResultAsync(string outputDirectory, string mode, string error, CancellationToken cancellationToken)
    {
        var result = new PublishResult
        {
            Success = false,
            Platform = "YouTube",
            Mode = mode,
            Error = error,
            PublishedUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "youtube-publish-result.json"), JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        return result;
    }

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

    private static List<string> SplitCsv(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
