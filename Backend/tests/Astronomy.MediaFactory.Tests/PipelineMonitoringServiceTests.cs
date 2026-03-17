using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Operations;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineMonitoringServiceTests
{
    [Fact]
    public async Task Summary_ComputesCounts_Failures_AndSlowStages()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new MediaFactoryDbContext(options);

        var failedRun = new PipelineRun { Status = PipelineRunStatus.Failed, StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-5), FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-4) };
        var succeededRun = new PipelineRun { Status = PipelineRunStatus.Succeeded, StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-3), FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-2) };
        db.PipelineRuns.AddRange(failedRun, succeededRun);
        db.PipelineJobs.Add(new PipelineJob { Status = PipelineJobStatus.Pending });
        db.PublishedVideos.Add(new PublishedVideo { Title = "Latest", Status = "Published", CreatedAt = DateTimeOffset.UtcNow });
        db.PipelineStageExecutions.AddRange(
            new PipelineStageExecution { PipelineRunId = failedRun.Id, StageName = "Rendering", Status = "Failed", StartedAt = DateTimeOffset.UtcNow.AddSeconds(-20), FinishedAt = DateTimeOffset.UtcNow.AddSeconds(-5), DurationMs = 15000, ErrorMessage = "render fail" },
            new PipelineStageExecution { PipelineRunId = failedRun.Id, StageName = "BlobUpload", Status = "FailedWithFallback", StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10), FinishedAt = DateTimeOffset.UtcNow.AddSeconds(-9), DurationMs = 1000, ErrorMessage = "blob fail" });
        await db.SaveChangesAsync();

        var svc = new PipelineMonitoringService(db, Options.Create(new OperationsOptions { SlowStageThresholdMs = 10000, RetainDays = 30 }));
        var summary = await svc.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(2, summary.TotalRuns);
        Assert.Equal(1, summary.SuccessfulRuns);
        Assert.Equal(1, summary.FailedRuns);
        Assert.Equal("Rendering", summary.MostCommonFailingStage);
        Assert.Single(summary.SlowStages);
    }
}
