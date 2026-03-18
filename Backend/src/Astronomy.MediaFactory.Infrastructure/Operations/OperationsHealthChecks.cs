using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class DatabaseConnectivityHealthCheck : IHealthCheck
{
    private readonly MediaFactoryDbContext _db;
    public DatabaseConnectivityHealthCheck(MediaFactoryDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await _db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database reachable")
            : HealthCheckResult.Unhealthy("Database unreachable");
}

public sealed class QueueProcessorReadinessHealthCheck : IHealthCheck
{
    private readonly MediaFactoryDbContext _db;
    public QueueProcessorReadinessHealthCheck(MediaFactoryDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _ = await _db.PipelineJobs.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(1).ToListAsync(cancellationToken);
        return HealthCheckResult.Healthy("Queue processor can read jobs");
    }
}

public sealed class OperationsConfigHealthCheck : IHealthCheck
{
    private readonly SkyfieldSidecarOptions _sidecar;
    private readonly AzureBlobOptions _blob;
    private readonly YouTubeOptions _youTube;
    private readonly IHostEnvironment _environment;

    public OperationsConfigHealthCheck(IOptions<SkyfieldSidecarOptions> sidecar, IOptions<AzureBlobOptions> blob, IOptions<YouTubeOptions> youTube, IHostEnvironment environment)
    {
        _sidecar = sidecar.Value;
        _blob = blob.Value;
        _youTube = youTube.Value;
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        if (_sidecar.Enabled && !Uri.TryCreate(_sidecar.BaseUrl, UriKind.Absolute, out _))
            issues.Add("SkyfieldSidecar:BaseUrl invalid while enabled");

        issues.AddRange(AzureConfigurationValidation.ValidateBlob(_blob, requireConfiguration: true)
            .Where(issue => issue.Contains("AzureBlob", StringComparison.Ordinal)));

        if (_youTube.PublishingEnabled && (string.IsNullOrWhiteSpace(_youTube.ClientId) || string.IsNullOrWhiteSpace(_youTube.ClientSecret)))
            issues.Add("YouTube credentials missing while publishing is enabled");

        if (issues.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("Operational config valid"));

        return Task.FromResult(_environment.IsDevelopment()
            ? HealthCheckResult.Degraded(string.Join("; ", issues))
            : HealthCheckResult.Unhealthy(string.Join("; ", issues)));
    }
}
