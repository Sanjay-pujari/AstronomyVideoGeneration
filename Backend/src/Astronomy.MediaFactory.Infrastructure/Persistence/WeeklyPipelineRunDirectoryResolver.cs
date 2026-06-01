using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyPipelineRunDirectoryResolver(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<WeeklyPipelineRunDirectoryResolver> logger) : IWeeklyPipelineRunDirectoryResolver
{
    private readonly RenderingOptions _renderingOptions = renderingOptions.Value;

    public async Task<string> ResolveRunDirectoryAsync(Guid pipelineRunId)
    {
        logger.LogInformation("WEEKLY_PIPELINE_RUN_DIRECTORY_RESOLVE_START pipelineRunId={PipelineRunId} resolvedRoot={ResolvedRoot}", pipelineRunId, null);

        try
        {
            var metadataPath = await ResolveFromExecutionMetadataAsync(pipelineRunId);
            if (!string.IsNullOrWhiteSpace(metadataPath) && TryValidateRunDirectory(metadataPath, out var metadataRoot))
            {
                logger.LogInformation("WEEKLY_PIPELINE_RUN_DIRECTORY_RESOLVE_SUCCESS pipelineRunId={PipelineRunId} resolvedRoot={ResolvedRoot}", pipelineRunId, metadataRoot);
                return metadataRoot;
            }

            var workingRoot = string.IsNullOrWhiteSpace(_renderingOptions.WorkingDirectory) ? "./media-output" : _renderingOptions.WorkingDirectory;
            if (!Directory.Exists(workingRoot))
            {
                throw new DirectoryNotFoundException($"Pipeline working directory root does not exist: {workingRoot}");
            }

            var matches = Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("N"), SearchOption.AllDirectories)
                .Concat(Directory.EnumerateDirectories(workingRoot, pipelineRunId.ToString("D"), SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => WeeklyPipelineRunDirectoryValidator.IsValidRunDirectory(path))
                .Select(WeeklyPipelineRunDirectoryValidator.ToCanonicalPath)
                .ToList();

            var resolvedRoot = matches.Count switch
            {
                0 => throw new DirectoryNotFoundException($"No WeeklySkyForecast run directory was found for pipelineRunId {pipelineRunId} under {workingRoot}."),
                1 => matches[0],
                _ => matches.OrderByDescending(Directory.GetLastWriteTimeUtc).First()
            };

            logger.LogInformation("WEEKLY_PIPELINE_RUN_DIRECTORY_RESOLVE_SUCCESS pipelineRunId={PipelineRunId} resolvedRoot={ResolvedRoot}", pipelineRunId, resolvedRoot);
            return resolvedRoot;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEEKLY_PIPELINE_RUN_DIRECTORY_RESOLVE_FAILED pipelineRunId={PipelineRunId} resolvedRoot={ResolvedRoot}", pipelineRunId, null);
            throw;
        }
    }

    private async Task<string?> ResolveFromExecutionMetadataAsync(Guid pipelineRunId)
    {
        var execution = await db.ContentPipelineExecutions
            .AsNoTracking()
            .Where(x => x.PipelineRunId == pipelineRunId || x.ContentGenerationPlanId == pipelineRunId)
            .OrderByDescending(x => x.UpdatedUtc ?? x.CreatedUtc)
            .Select(x => x.OutputFolder)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(execution)) return execution;

        var planExecution = await db.ContentPipelineExecutions
            .AsNoTracking()
            .Join(
                db.ContentGenerationPlans.AsNoTracking().Where(plan => plan.PipelineRunId == pipelineRunId || plan.Id == pipelineRunId),
                executionRow => executionRow.ContentGenerationPlanId,
                plan => (Guid?)plan.Id,
                (executionRow, _) => executionRow)
            .OrderByDescending(x => x.UpdatedUtc ?? x.CreatedUtc)
            .Select(x => x.OutputFolder)
            .FirstOrDefaultAsync();
        return planExecution;
    }

    private static bool TryValidateRunDirectory(string path, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (!Directory.Exists(path) || !WeeklyPipelineRunDirectoryValidator.IsValidRunDirectory(path)) return false;
        canonicalPath = WeeklyPipelineRunDirectoryValidator.ToCanonicalPath(path);
        return true;
    }
}
