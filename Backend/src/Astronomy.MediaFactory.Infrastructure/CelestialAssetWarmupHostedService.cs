using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure;

public sealed class CelestialAssetWarmupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CelestialAssetsOptions _options;
    private readonly ILogger<CelestialAssetWarmupHostedService> _logger;

    public CelestialAssetWarmupHostedService(
        IServiceProvider serviceProvider,
        IOptions<CelestialAssetsOptions> options,
        ILogger<CelestialAssetWarmupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.DownloadIfMissing)
        {
            _logger.LogInformation("Celestial asset warmup skipped because ingestion is disabled or downloads are disabled.");
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var scope = _serviceProvider.CreateScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<ICelestialAssetIngestionService>();
            var report = await ingestion.RefreshAsync(timeout.Token);
            _logger.LogInformation("Celestial asset warmup completed for {Count} objects.", report.Objects.Count);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Celestial asset warmup timed out; video generation will continue with cache/fallback assets.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Celestial asset warmup failed; video generation will continue with cache/fallback assets.");
        }
    }
}
