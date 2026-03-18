using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class MaintenanceService : IMaintenanceService
{
    private readonly MediaFactoryDbContext _db;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(MediaFactoryDbContext db, IOptions<MaintenanceOptions> options, ILogger<MaintenanceService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MaintenanceCleanupSummary> CleanupAsync(CleanupMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var validationError = RecoveryRequestValidator.Validate(request);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var operation = new RecoveryOperation
        {
            OperationType = RecoveryOperationType.CleanupRetention,
            RequestedBy = request.RequestedBy,
            Notes = request.Notes,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = RecoveryOperationStatus.Requested
        };

        await _db.RecoveryOperations.AddAsync(operation, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var deletedPaths = new List<string>();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var retentionPolicy = RetentionCleanupPolicy.From(_options, request, now);
            var stageDeleted = 0;
            var jobDeleted = 0;
            var analyticsDeleted = 0;

            if (retentionPolicy.StageCutoff is { } stageCutoff)
            {
                var oldStages = await _db.PipelineStageExecutions.Where(x => x.StartedAt < stageCutoff).ToListAsync(cancellationToken);
                stageDeleted = oldStages.Count;
                _db.PipelineStageExecutions.RemoveRange(oldStages);
            }

            if (retentionPolicy.JobCutoff is { } jobCutoff)
            {
                var oldJobs = await _db.PipelineJobs
                    .Where(x => x.Status != PipelineJobStatus.Running && x.Status != PipelineJobStatus.Pending && x.Status != PipelineJobStatus.Retrying)
                    .Where(x => x.ScheduledAt < jobCutoff)
                    .ToListAsync(cancellationToken);
                jobDeleted = oldJobs.Count;
                _db.PipelineJobs.RemoveRange(oldJobs);
            }

            if (retentionPolicy.AnalyticsCutoff is { } analyticsCutoff)
            {
                var oldAnalytics = await _db.VideoAnalytics.Where(x => x.RetrievedAt < analyticsCutoff).ToListAsync(cancellationToken);
                analyticsDeleted = oldAnalytics.Count;
                _db.VideoAnalytics.RemoveRange(oldAnalytics);
            }

            var fileDeleted = 0;
            if (retentionPolicy.WorkingFilesCutoff is { } workingFilesCutoff)
            {
                fileDeleted = CleanupWorkingFiles(_options.WorkingDirectory, workingFilesCutoff, deletedPaths);
            }

            operation.Status = RecoveryOperationStatus.Completed;
            operation.ResultSummary = $"Cleanup deleted {stageDeleted} stages, {jobDeleted} jobs, {analyticsDeleted} analytics records, and {fileDeleted} working files.";
            operation.Touch();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Maintenance cleanup completed. {ResultSummary}", operation.ResultSummary);

            return new MaintenanceCleanupSummary(operation.Id, stageDeleted, jobDeleted, analyticsDeleted, fileDeleted, deletedPaths);
        }
        catch (Exception ex)
        {
            operation.Status = RecoveryOperationStatus.Failed;
            operation.ResultSummary = ex.Message;
            operation.Touch();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Maintenance cleanup operation {RecoveryOperationId} failed.", operation.Id);
            throw;
        }
    }

    private sealed record RetentionCleanupPolicy(
        DateTimeOffset? StageCutoff,
        DateTimeOffset? JobCutoff,
        DateTimeOffset? AnalyticsCutoff,
        DateTimeOffset? WorkingFilesCutoff)
    {
        public static RetentionCleanupPolicy From(MaintenanceOptions options, CleanupMaintenanceRequest request, DateTimeOffset now)
            => new(
                request.DeleteDbRecords ? now.AddDays(-options.StageRetentionDays) : null,
                request.DeleteDbRecords ? now.AddDays(-options.JobRetentionDays) : null,
                request.DeleteAnalytics ? now.AddDays(-options.AnalyticsRetentionDays) : null,
                request.DeleteWorkingFiles ? now.AddDays(-options.WorkingFileRetentionDays) : null);
    }

    private static int CleanupWorkingFiles(string workingDirectory, DateTimeOffset cutoff, ICollection<string> deletedPaths)
    {
        if (!Directory.Exists(workingDirectory))
            return 0;

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.LastWriteTimeUtc > cutoff.UtcDateTime)
                continue;

            var relativeName = Path.GetFileName(file);
            var looksSafeToDelete = relativeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || relativeName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || relativeName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || relativeName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || relativeName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || relativeName.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase);
            if (!looksSafeToDelete)
                continue;

            File.Delete(file);
            deletedPaths.Add(file);
            deleted += 1;
        }

        foreach (var directory in Directory.EnumerateDirectories(workingDirectory, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory, recursive: false);
        }

        return deleted;
    }
}
