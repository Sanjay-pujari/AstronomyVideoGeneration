namespace Astronomy.MediaFactory.Core;

public static class RegionIdNormalizer
{
    public static string NormalizeRegionId(string regionId)
        => string.IsNullOrWhiteSpace(regionId)
            ? string.Empty
            : regionId.Trim().ToUpperInvariant();
}
