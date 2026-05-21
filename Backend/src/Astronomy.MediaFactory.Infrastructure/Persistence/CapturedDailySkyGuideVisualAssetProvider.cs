using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class CapturedDailySkyGuideVisualAssetProvider(IOptions<StellariumOptions> options) : IDailySkyGuideVisualAssetProvider
{
    private static readonly (string Role, int SortOrder)[] RoleOrder =
    [
        ("IntroBackground", 1),
        ("ThumbnailCandidate", 2),
        ("SupportingSkyMap", 3),
        ("OutroBackground", 4)
    ];

    private readonly StellariumOptions _options = options.Value;

    public Task<IReadOnlyList<DailySkyGuideVisualAsset>> GetAssetsAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assetRoot = Path.Combine(_options.CaptureDirectory ?? string.Empty, "content-plans", contentGenerationPlanId.ToString(), "stellarium-scenes");
        var assets = RoleOrder.Select(x => BuildAsset(assetRoot, x.Role, x.SortOrder)).ToArray();
        return Task.FromResult<IReadOnlyList<DailySkyGuideVisualAsset>>(assets);
    }

    private static DailySkyGuideVisualAsset BuildAsset(string assetRoot, string role, int sortOrder)
    {
        var path = FindRolePath(assetRoot, role);
        return new DailySkyGuideVisualAsset(role, path, !string.IsNullOrWhiteSpace(path) && File.Exists(path), sortOrder, null, null, null);
    }

    private static string FindRolePath(string assetRoot, string role)
    {
        if (!Directory.Exists(assetRoot))
        {
            return Path.Combine(assetRoot, $"*_{role}.png");
        }

        var file = Directory
            .EnumerateFiles(assetRoot, $"*_{role}.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return file ?? Path.Combine(assetRoot, $"*_{role}.png");
    }
}
