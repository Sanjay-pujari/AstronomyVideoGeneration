using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure;

public sealed class TokenHealthStartupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TokenHealthOptions _options;
    private readonly ILogger<TokenHealthStartupHostedService> _logger;

    public TokenHealthStartupHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<TokenHealthOptions> options,
        ILogger<TokenHealthStartupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.CheckOnStartup)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tokenHealth = scope.ServiceProvider.GetRequiredService<ITokenHealthService>();
            var results = await tokenHealth.CheckAllAsync(cancellationToken);

            foreach (var result in results)
            {
                if (!result.IsValid)
                {
                    _logger.LogWarning("Token health warning: {Platform} token is invalid or unavailable. Account={AccountName} ({AccountId}); Warning={Warning}; Error={Error}",
                        result.Platform,
                        result.AccountName,
                        result.AccountId,
                        result.Warning,
                        result.Error);
                }
                else if (!string.IsNullOrWhiteSpace(result.Warning))
                {
                    _logger.LogWarning("Token health warning: {Platform} token requires attention. Account={AccountName} ({AccountId}); ExpiresAtUtc={ExpiresAtUtc}; Warning={Warning}",
                        result.Platform,
                        result.AccountName,
                        result.AccountId,
                        result.ExpiresAtUtc,
                        result.Warning);
                }
            }

            if (_options.WriteHealthReport)
            {
                var writer = scope.ServiceProvider.GetRequiredService<ITokenHealthReportWriter>();
                var path = await writer.WriteAsync(results, cancellationToken);
                _logger.LogInformation("Token health report written to {Path}.", path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Token health startup check failed. The application will continue running.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
