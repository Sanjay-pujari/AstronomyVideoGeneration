using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Analytics;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class SafeAnalyticsExecutorTests
{
    [Fact]
    public async Task ExecuteInitializationAsync_WhenIngestionThrows_ReturnsFailureWithoutThrowing()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"analytics-safe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var services = new ServiceCollection();
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<IAnalyticsIngestionService, ThrowingAnalyticsIngestionService>();
        services.AddScoped<ISafeAnalyticsExecutor, SafeAnalyticsExecutor>();
        var provider = services.BuildServiceProvider();

        var executor = new SafeAnalyticsExecutor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SafeAnalyticsExecutor>.Instance);

        var result = await executor.ExecuteInitializationAsync(BuildRequest(), outputDir, CancellationToken.None);

        Assert.True(result.AnalyticsStarted);
        Assert.True(result.ScopeCreated);
        Assert.True(result.AnalyticsFailed);
        Assert.False(result.AnalyticsCompleted);
        Assert.False(string.IsNullOrWhiteSpace(result.Exception));
        Assert.True(File.Exists(Path.Combine(outputDir, "analytics-post-processing-report.json")));
    }

    [Fact]
    public async Task ExecuteInitializationAsync_WhenTimedOut_CancelsOnlyAnalytics()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"analytics-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var services = new ServiceCollection();
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<IAnalyticsIngestionService, SlowAnalyticsIngestionService>();
        services.AddScoped<ISafeAnalyticsExecutor, SafeAnalyticsExecutor>();
        var provider = services.BuildServiceProvider();

        var executor = new SafeAnalyticsExecutor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SafeAnalyticsExecutor>.Instance);

        var result = await executor.ExecuteInitializationAsync(BuildRequest(), outputDir, CancellationToken.None);

        Assert.True(result.AnalyticsFailed);
        Assert.True(result.TimedOut);
    }

    private static AnalyticsPipelineInitializationRequest BuildRequest()
        => new(
            Guid.NewGuid(), "en", "us", DateTimeOffset.UtcNow,
            ["YouTube"], ["Hook"], [new AnalyticsThumbnailSeed("thumb.jpg", "Long")],
            "LongVideo", null, null);

    private sealed class ThrowingAnalyticsIngestionService : IAnalyticsIngestionService
    {
        public Task IngestManualAsync(IReadOnlyCollection<Astronomy.MediaFactory.Analytics.AnalyticsIngestionDto> records, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InitializeForPipelineRunAsync(AnalyticsPipelineInitializationRequest request, CancellationToken cancellationToken)
            => throw new ObjectDisposedException("Npgsql.NpgsqlConnection");
    }

    private sealed class SlowAnalyticsIngestionService : IAnalyticsIngestionService
    {
        public Task IngestManualAsync(IReadOnlyCollection<Astronomy.MediaFactory.Analytics.AnalyticsIngestionDto> records, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task InitializeForPipelineRunAsync(AnalyticsPipelineInitializationRequest request, CancellationToken cancellationToken)
            => await Task.Delay(TimeSpan.FromSeconds(65), cancellationToken);
    }
}
