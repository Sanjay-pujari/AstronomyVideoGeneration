using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Scheduling;

public sealed class JsonSchedulerAuditStore : ISchedulerAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public JsonSchedulerAuditStore(IOptions<MaintenanceOptions> maintenanceOptions)
    {
        _path = Path.Combine(maintenanceOptions.Value.WorkingDirectory, "scheduler-runs.json");
    }

    public async Task<IReadOnlyCollection<SchedulerRunRecord>> GetRunsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<SchedulerRunRecord>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
    {
        var runs = await GetRunsAsync(cancellationToken);
        return runs.OrderByDescending(x => x.UpdatedUtc).Take(take).ToList();
    }

    public async Task UpsertAsync(SchedulerRunRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var runs = (await ReadUnsafeAsync(cancellationToken)).ToList();
            var index = runs.FindIndex(x => IsSameRun(x, record));
            if (index >= 0)
                runs[index] = record;
            else
                runs.Add(record);

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, runs.OrderByDescending(x => x.PlannedRunUtc).ToList(), JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyCollection<SchedulerRunRecord>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return [];

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<SchedulerRunRecord>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private static bool IsSameRun(SchedulerRunRecord left, SchedulerRunRecord right)
    {
        if (left.Status == "Skipped" || right.Status == "Skipped")
            return left.Status == right.Status
                && left.ScheduleName.Equals(right.ScheduleName, StringComparison.OrdinalIgnoreCase)
                && left.TargetDate == right.TargetDate
                && left.LocationName.Equals(right.LocationName, StringComparison.OrdinalIgnoreCase)
                && left.PlannedRunUtc == right.PlannedRunUtc;

        return left.ScheduleName.Equals(right.ScheduleName, StringComparison.OrdinalIgnoreCase)
            && left.TargetDate == right.TargetDate
            && left.LocationName.Equals(right.LocationName, StringComparison.OrdinalIgnoreCase);
    }
}
