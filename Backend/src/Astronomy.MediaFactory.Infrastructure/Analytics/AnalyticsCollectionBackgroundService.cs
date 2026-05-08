using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class AnalyticsCollectionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AnalyticsOptions> _options;
    private readonly ILogger<AnalyticsCollectionBackgroundService> _logger;

    public AnalyticsCollectionBackgroundService(IServiceScopeFactory scopeFactory, IOptionsMonitor<AnalyticsOptions> options, ILogger<AnalyticsCollectionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IAnalyticsCollectionService>();
                    await service.CollectRecentAnalyticsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Analytics background collection failed without affecting pipeline execution.");
                }
            }

            var minutes = Math.Max(1, _options.CurrentValue.CollectEveryMinutes);
            await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
        }
    }
}
