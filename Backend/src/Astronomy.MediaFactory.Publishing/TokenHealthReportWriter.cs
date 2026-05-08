using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public interface ITokenHealthReportWriter
{
    Task<string> WriteAsync(IReadOnlyList<TokenHealthResult> results, CancellationToken cancellationToken);
}

public sealed class TokenHealthReportWriter : ITokenHealthReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly MaintenanceOptions _maintenanceOptions;

    public TokenHealthReportWriter(IOptions<MaintenanceOptions> maintenanceOptions)
    {
        _maintenanceOptions = maintenanceOptions.Value;
    }

    public async Task<string> WriteAsync(IReadOnlyList<TokenHealthResult> results, CancellationToken cancellationToken)
    {
        var directory = string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory)
            ? AppContext.BaseDirectory
            : _maintenanceOptions.WorkingDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "token-health-report.json");
        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            platforms = results.Select(result => new
            {
                platform = result.Platform,
                valid = result.IsValid,
                configured = result.IsConfigured,
                canRefresh = result.CanRefresh,
                accountName = result.AccountName,
                accountId = result.AccountId,
                expiry = result.ExpiresAtUtc,
                daysUntilExpiry = result.DaysUntilExpiry,
                warning = result.Warning,
                error = result.Error
            })
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        return path;
    }
}
