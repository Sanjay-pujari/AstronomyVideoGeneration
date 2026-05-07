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
    private readonly MetaPublishingOptions _options;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<MetaPublishService> _logger;

    public MetaPublishService(
        IPipelineRepository repository,
        IFacebookReelPublishService facebookReelPublishService,
        IOptions<MetaPublishingOptions> options,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<MetaPublishService> logger)
    {
        _repository = repository;
        _facebookReelPublishService = facebookReelPublishService;
        _options = options.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MetaPublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset = "all", CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(_options.Mode);
        if (mode == "Disabled")
        {
            return [new MetaPublishResult { Success = false, Platform = "Facebook", Mode = mode, Error = "Meta publishing is disabled.", PublishedUtc = DateTime.UtcNow }];
        }

        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
        {
            return [new MetaPublishResult { Success = false, Platform = "Facebook", Mode = mode, Error = $"Pipeline run {pipelineRunId} was not found.", PublishedUtc = DateTime.UtcNow }];
        }

        var selector = NormalizeAsset(asset);
        if (selector != "all" && selector != "facebook-reel")
        {
            return [new MetaPublishResult { Success = false, Platform = "Facebook", Mode = mode, Error = $"Unsupported Meta publish asset '{asset}'. Only facebook-reel is implemented.", PublishedUtc = DateTime.UtcNow }];
        }

        if (!_options.PublishFacebookReel)
        {
            return [new MetaPublishResult { Success = false, Platform = "Facebook", Mode = mode, Error = "Facebook Reel publishing is disabled by configuration.", PublishedUtc = DateTime.UtcNow }];
        }

        var outputDirectory = ResolveOutputDirectory(run);
        Directory.CreateDirectory(outputDirectory);
        var videoPath = Path.Combine(outputDirectory, "shorts", "short-video.mp4");
        var metadata = await ReadFacebookMetadataAsync(outputDirectory, cancellationToken);
        var caption = await BuildFacebookCaptionAsync(outputDirectory, run, metadata, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-caption.txt"), caption, cancellationToken);

        var request = new MetaPublishRequest
        {
            PipelineRunId = pipelineRunId,
            Platform = "Facebook",
            VideoPath = videoPath,
            Caption = caption,
            ShortTitle = BuildFacebookShortTitle(metadata?.Title, caption),
            IsReel = true
        };

        var result = await _facebookReelPublishService.PublishReelAsync(request, cancellationToken);
        _logger.LogInformation("Facebook Reel publish for run {PipelineRunId} completed with success={Success} mode={Mode}.", pipelineRunId, result.Success, result.Mode);
        return [result];
    }

    private static async Task<SeoMetadataResult?> ReadFacebookMetadataAsync(string outputDirectory, CancellationToken cancellationToken)
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
        => Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private static string NormalizeAsset(string? asset)
        => string.IsNullOrWhiteSpace(asset) || string.Equals(asset, "all", StringComparison.OrdinalIgnoreCase) ? "all"
            : string.Equals(asset, "facebook-reel", StringComparison.OrdinalIgnoreCase) ? "facebook-reel"
            : asset.Trim();
}
