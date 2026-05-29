using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public interface INasaImagesClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<NasaImageCandidate>> SearchAsync(NasaAssetRequirement requirement, string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<NasaImageDownloadChoice>> GetAssetDownloadChoicesAsync(NasaImageCandidate candidate, CancellationToken cancellationToken);
}

public sealed class NasaImagesClient(
    HttpClient httpClient,
    IOptions<NasaImagesOptions> nasaImagesOptions,
    IOptions<AstronomyApiOptions> astronomyApiOptions,
    ILogger<NasaImagesClient> logger) : INasaImagesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private NasaAssetOptions Options => new()
    {
        SearchBaseUrl = nasaImagesOptions.Value.SearchBaseUrl,
        SearchEndpoint = nasaImagesOptions.Value.SearchEndpoint,
        AssetEndpoint = nasaImagesOptions.Value.AssetEndpoint,
        NasaApiKey = astronomyApiOptions.Value.NasaApiKey,
        NasaBaseUrl = astronomyApiOptions.Value.NasaBaseUrl
    };

    public bool IsConfigured => Options.ProviderConfigured && Uri.TryCreate(Options.SearchBaseUrl, UriKind.Absolute, out _);

    public async Task<IReadOnlyList<NasaImageCandidate>> SearchAsync(NasaAssetRequirement requirement, string query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new NasaProviderUnavailableException("NASA Images provider is not configured.");
        var options = Options;
        var endpoint = options.SearchEndpoint.StartsWith('/') ? options.SearchEndpoint : $"/{options.SearchEndpoint}";
        var url = $"{options.SearchBaseUrl.TrimEnd('/')}{endpoint}?q={Uri.EscapeDataString(query)}&media_type=image";
        logger.LogInformation("NASA_IMAGE_SEARCH_START assetCode={AssetCode} segmentType={SegmentType} query={SearchQuery}", requirement.AssetCode, requirement.SegmentType, query);
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new NasaProviderUnavailableException($"NASA Images search returned HTTP {(int)response.StatusCode}.");

        var payload = await response.Content.ReadFromJsonAsync<NasaSearchResponse>(JsonOptions, cancellationToken);
        var candidates = new List<NasaImageCandidate>();
        foreach (var item in payload?.Collection?.Items ?? [])
        {
            var data = item.Data?.FirstOrDefault();
            if (data is null || string.IsNullOrWhiteSpace(data.NasaId)) continue;
            var mediaType = data.MediaType ?? "image";
            var links = item.Links?.Select(x => x.Href).Where(IsImageUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            candidates.Add(new NasaImageCandidate(
                data.NasaId,
                data.Title ?? requirement.TargetNasaAssetCategory,
                data.Description ?? string.Empty,
                data.DateCreated,
                data.Keywords ?? [],
                data.Center,
                links,
                mediaType,
                PixelHint: links.Select(PixelHint).DefaultIfEmpty(0).Max()));
        }

        logger.LogInformation("NASA_IMAGE_SEARCH_COMPLETE assetCode={AssetCode} query={SearchQuery} candidates={CandidateCount}", requirement.AssetCode, query, candidates.Count);
        return candidates;
    }

    public async Task<IReadOnlyList<NasaImageDownloadChoice>> GetAssetDownloadChoicesAsync(NasaImageCandidate candidate, CancellationToken cancellationToken)
    {
        var options = Options;
        var choices = new List<NasaImageDownloadChoice>();
        if (!string.IsNullOrWhiteSpace(options.AssetEndpoint))
        {
            try
            {
                var endpoint = options.AssetEndpoint.Replace("{nasaId}", Uri.EscapeDataString(candidate.NasaId), StringComparison.OrdinalIgnoreCase);
                endpoint = endpoint.StartsWith('/') ? endpoint : $"/{endpoint}";
                logger.LogInformation("NASA_IMAGE_ASSET_ENDPOINT_START nasaId={NasaId}", candidate.NasaId);
                using var response = await httpClient.GetAsync($"{options.SearchBaseUrl.TrimEnd('/')}{endpoint}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<NasaAssetResponse>(JsonOptions, cancellationToken);
                    choices.AddRange((payload?.Collection?.Items ?? [])
                        .Select(x => x.Href)
                        .Where(IsImageUrl)
                        .Where(url => !IsMetadataOrThumbnail(url))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(url => new NasaImageDownloadChoice(url, true, PixelHint(url))));
                }
                logger.LogInformation("NASA_IMAGE_ASSET_ENDPOINT_COMPLETE nasaId={NasaId} choices={ChoiceCount}", candidate.NasaId, choices.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NASA_IMAGE_ASSET_ENDPOINT_COMPLETE nasaId={NasaId} status=Failed", candidate.NasaId);
            }
        }

        if (choices.Count == 0)
        {
            choices.AddRange(candidate.PreviewLinks
                .Where(IsImageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(url => new NasaImageDownloadChoice(url, false, PixelHint(url))));
        }

        return choices
            .OrderByDescending(x => x.PixelHint)
            .ThenByDescending(x => IsPreferredOriginal(x.Url))
            .ToList();
    }

    private static bool IsImageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetadataOrThumbnail(string url)
    {
        var value = url.ToLowerInvariant();
        return value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || (value.Contains("~thumb") && (value.Contains("~orig") is false && value.Contains("~large") is false));
    }

    private static bool IsPreferredOriginal(string url) => url.Contains("~orig", StringComparison.OrdinalIgnoreCase) || url.Contains("orig.", StringComparison.OrdinalIgnoreCase);

    private static long PixelHint(string link)
    {
        if (link.Contains("~orig", StringComparison.OrdinalIgnoreCase) || link.Contains("orig", StringComparison.OrdinalIgnoreCase)) return 10_000_000;
        if (link.Contains("~large", StringComparison.OrdinalIgnoreCase) || link.Contains("large", StringComparison.OrdinalIgnoreCase)) return 5_000_000;
        if (link.Contains("~medium", StringComparison.OrdinalIgnoreCase) || link.Contains("medium", StringComparison.OrdinalIgnoreCase)) return 1_000_000;
        return 0;
    }
}
