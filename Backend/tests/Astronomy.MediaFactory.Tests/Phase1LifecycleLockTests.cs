using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase1LifecycleLockTests
{
    [Fact]
    public async Task Same_workspace_is_serialized_and_entries_are_removed()
    {
        var gate = new InProcessPhase1ExecutionLock();
        await using var first = await gate.AcquireAsync("workspace", CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () => { await using var lease = await gate.AcquireAsync("workspace", CancellationToken.None); entered.SetResult(); });
        await Task.Delay(50);
        Assert.False(entered.Task.IsCompleted);
        await first.DisposeAsync();
        await second;
        Assert.Equal(0, gate.EntryCount);
    }

    [Fact]
    public async Task Different_workspaces_are_independent()
    {
        var gate = new InProcessPhase1ExecutionLock();
        await using var first = await gate.AcquireAsync("one", CancellationToken.None);
        await using var second = await gate.AcquireAsync("two", CancellationToken.None);
        Assert.Equal(2, gate.EntryCount);
    }

    [Fact]
    public async Task Waiting_cancellation_propagates_and_does_not_leak_entry()
    {
        var gate = new InProcessPhase1ExecutionLock();
        await using var first = await gate.AcquireAsync("workspace", CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gate.AcquireAsync("workspace", cancellation.Token));
        Assert.Equal(1, gate.EntryCount);
    }
}
