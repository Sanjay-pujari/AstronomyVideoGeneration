using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

    public OperationsConfigHealthCheck(IOptions<SkyfieldSidecarOptions> sidecar, IOptions<AzureBlobOptions> blob, IOptions<YouTubeOptions> youTube)
    {
        _sidecar = sidecar.Value;
        _blob = blob.Value;
        _youTube = youTube.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        if (!Uri.TryCreate(_sidecar.BaseUrl, UriKind.Absolute, out _))
            issues.Add("SkyfieldSidecar:BaseUrl invalid");
        if (string.IsNullOrWhiteSpace(_blob.ContainerName))
            issues.Add("AzureBlob:ContainerName missing");
        if (string.IsNullOrWhiteSpace(_youTube.PrivacyStatus))
            issues.Add("YouTube:PrivacyStatus missing");

        return Task.FromResult(issues.Count == 0
            ? HealthCheckResult.Healthy("Operational config valid")
            : HealthCheckResult.Degraded(string.Join("; ", issues)));
    }
}
