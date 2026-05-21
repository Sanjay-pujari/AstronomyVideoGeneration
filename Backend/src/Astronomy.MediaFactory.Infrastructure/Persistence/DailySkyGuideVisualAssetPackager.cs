using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideVisualAssetPackager(IOptions<StellariumOptions> options) : IDailySkyGuideVisualAssetPackager
{
    private static readonly string[] RequiredRoles = ["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"];
    private readonly StellariumOptions _options = options.Value;

    public Task<DailySkyGuideVisualAssetPackageResponse> BuildPackageAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var captureDirectory = _options.CaptureDirectory;
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            warnings.Add("Stellarium:CaptureDirectory is not configured.");
            captureDirectory = string.Empty;
        }

        var assetRoot = Path.Combine(captureDirectory, "content-plans", contentGenerationPlanId.ToString(), "stellarium-scenes");
        var assets = RequiredRoles
            .Select(role =>
            {
                var path = FindRolePath(assetRoot, role);
                return new DailySkyGuideVisualAssetItem(role, path, !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            })
            .ToArray();

        var missing = assets.Where(x => !x.Exists).Select(x => x.Role).ToArray();
        if (missing.Length > 0)
        {
            warnings.Add($"Missing expected assets: {string.Join(", ", missing)}.");
        }

        return Task.FromResult(new DailySkyGuideVisualAssetPackageResponse(
            contentGenerationPlanId,
            missing.Length == 0,
            assetRoot,
            assets,
            warnings));
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
