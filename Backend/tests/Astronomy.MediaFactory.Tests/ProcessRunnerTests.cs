using Astronomy.MediaFactory.Rendering;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesStdoutAndStderr_WhenProcessExitsNormally()
    {
        var runner = new ProcessRunner();

        var result = await runner.ExecuteAsync(
            "bash",
            "-c \"echo out-message; echo err-message 1>&2\"",
            CancellationToken.None,
            TimeSpan.FromSeconds(10));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-message", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err-message", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_TimesOutAndKillsProcessTree()
    {
        var runner = new ProcessRunner();

        var result = await runner.ExecuteAsync(
            "bash",
            "-c \"sleep 30\"",
            CancellationToken.None,
            TimeSpan.FromMilliseconds(250));

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsStreamsWithoutDeadlock_WhenOutputIsLarge()
    {
        var runner = new ProcessRunner();

        var result = await runner.ExecuteAsync(
            "bash",
            "-c \"for i in $(seq 1 5000); do echo out-$i; echo err-$i 1>&2; done\"",
            CancellationToken.None,
            TimeSpan.FromSeconds(10));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-5000", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err-5000", result.StandardError, StringComparison.Ordinal);
    }
}
