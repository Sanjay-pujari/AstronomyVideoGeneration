using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class RegionResolutionService(IOptions<SchedulerOptions> schedulerOptions) : IRegionResolutionService
{
    private static readonly RegionScheduleOptions UdaipurFallback = new()
    {
        RegionId = "INDIA-UDAIPUR",
        DisplayName = "Udaipur",
        Latitude = 24.5854,
        Longitude = 73.7125,
        Timezone = "Asia/Kolkata"
    };

    private static readonly IReadOnlyDictionary<string, string> RegionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["IN-RJ-UDAIPUR"] = "INDIA-UDAIPUR"
    };

    public Task<RegionResolutionResult?> TryResolveAsync(string regionId, string? regionName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestedRegionId = RegionIdNormalizer.NormalizeRegionId(regionId);
        var regions = schedulerOptions.Value.Regions.Items;
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (RegionAliases.TryGetValue(requestedRegionId, out var aliasCanonical))
        {
            aliases.Add(aliasCanonical);
        }

        foreach (var configured in regions.Where(r => !string.IsNullOrWhiteSpace(r.RegionId)))
        {
            var configuredId = RegionIdNormalizer.NormalizeRegionId(configured.RegionId);
            if (string.Equals(configuredId, requestedRegionId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<RegionResolutionResult?>(Build(configured, requestedRegionId, aliases));
            }

            if (aliases.Contains(configuredId))
            {
                return Task.FromResult<RegionResolutionResult?>(Build(configured, requestedRegionId, aliases));
            }
        }

        var fallbackByName = regions.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(r.DisplayName)
            && (string.Equals(r.DisplayName, regionName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.DisplayName, "Udaipur", StringComparison.OrdinalIgnoreCase)
                || r.DisplayName.Contains("Udaipur", StringComparison.OrdinalIgnoreCase)));
        if (fallbackByName is not null)
        {
            return Task.FromResult<RegionResolutionResult?>(Build(fallbackByName, requestedRegionId, aliases));
        }

        if (string.Equals(requestedRegionId, "IN-RJ-UDAIPUR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(regionName, "Udaipur", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<RegionResolutionResult?>(Build(UdaipurFallback, requestedRegionId, aliases));
        }

        return Task.FromResult<RegionResolutionResult?>(null);
    }

    private static RegionResolutionResult Build(RegionScheduleOptions region, string requestedRegionId, HashSet<string> aliases)
    {
        var canonicalRegionId = RegionIdNormalizer.NormalizeRegionId(region.RegionId);
        aliases.Add(canonicalRegionId);
        if (RegionAliases.TryGetValue(requestedRegionId, out var aliasCanonical)) aliases.Add(aliasCanonical);

        var locationName = string.IsNullOrWhiteSpace(region.DisplayName) ? canonicalRegionId : region.DisplayName;
        return new RegionResolutionResult(
            canonicalRegionId,
            requestedRegionId,
            locationName,
            region.Latitude,
            region.Longitude,
            region.Timezone,
            aliases.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            canonicalRegionId.ToLowerInvariant());
    }
}
