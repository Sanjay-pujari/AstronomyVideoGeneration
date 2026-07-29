using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelinePhase6ConcurrencyTests
{
    [Fact]
    public async Task Same_plan_concurrent_generation_is_serialized_and_unused_entry_is_removed()
    {
        var gate = new InProcessStoryFrameExecutionLock();
        await using var first = await gate.AcquireAsync("plan", CancellationToken.None);
        var entered = false;
        var second = Task.Run(async () => { await using var lease = await gate.AcquireAsync("plan", CancellationToken.None); entered = true; });
        await Task.Delay(30); Assert.False(entered);
        await first.DisposeAsync(); await second;
        Assert.True(entered); Assert.Equal(0, gate.EntryCount);
    }

    [Fact]
    public async Task Different_plans_execute_independently()
    {
        var gate = new InProcessStoryFrameExecutionLock();
        await using var first = await gate.AcquireAsync("one", CancellationToken.None);
        await using var second = await gate.AcquireAsync("two", CancellationToken.None);
        Assert.Equal(2, gate.EntryCount);
    }

    [Fact]
    public async Task Waiting_lock_honors_cancellation_and_releases_reference()
    {
        var gate = new InProcessStoryFrameExecutionLock();
        await using var first = await gate.AcquireAsync("plan", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.AcquireAsync("plan", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.Equal(1, gate.EntryCount);
        await first.DisposeAsync(); Assert.Equal(0, gate.EntryCount);
    }
}
