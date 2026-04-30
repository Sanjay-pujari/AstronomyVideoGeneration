using System.Diagnostics;

namespace Astronomy.MediaFactory.Rendering;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(180);

    public async Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var start = DateTimeOffset.UtcNow;
        var effectiveTimeout = timeout.GetValueOrDefault(DefaultTimeout);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            return new ProcessExecutionResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: "Process failed to start.",
                StartTimeUtc: start,
                EndTimeUtc: DateTimeOffset.UtcNow,
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty,
                TimedOut: false);
        }

        var command = $"{fileName} {arguments}".TrimEnd();
        var timedOut = false;
        var stdOut = string.Empty;
        var stdErr = string.Empty;

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var processCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var waitForExitTask = process.WaitForExitAsync(processCancellationTokenSource.Token);
            var timeoutTask = Task.Delay(effectiveTimeout, CancellationToken.None);
            var completed = await Task.WhenAny(waitForExitTask, timeoutTask);

            if (completed == timeoutTask)
            {
                timedOut = true;
                processCancellationTokenSource.Cancel();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
            else
            {
                await waitForExitTask;
            }

            stdOut = await AwaitWithFallbackAsync(stdoutTask, TimeSpan.FromSeconds(5));
            stdErr = await AwaitWithFallbackAsync(stderrTask, TimeSpan.FromSeconds(5));

            var end = DateTimeOffset.UtcNow;
            Console.WriteLine($"[ProcessRunner] Command={command}; Start={start:O}; End={end:O}; ElapsedMs={(end - start).TotalMilliseconds:F0}; ExitCode={(process.HasExited ? process.ExitCode : -1)}; TimedOut={timedOut}");
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult(
                ExitCode: process.HasExited ? process.ExitCode : -1,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                StartTimeUtc: start,
                EndTimeUtc: DateTimeOffset.UtcNow,
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: ex.ToString(),
                TimedOut: timedOut);
        }

        return new ProcessExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdOut,
            StandardError: stdErr,
            StartTimeUtc: start,
            EndTimeUtc: DateTimeOffset.UtcNow,
            FileName: fileName,
            Arguments: arguments,
            ExceptionText: string.Empty,
            TimedOut: timedOut);
    }

    private static async Task<string> AwaitWithFallbackAsync(Task<string> readTask, TimeSpan fallbackTimeout)
    {
        var completed = await Task.WhenAny(readTask, Task.Delay(fallbackTimeout, CancellationToken.None));
        if (completed == readTask)
        {
            return await readTask;
        }

        return string.Empty;
    }
}

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string FileName,
    string Arguments,
    string ExceptionText,
    bool TimedOut)
{
    public TimeSpan Duration => EndTimeUtc - StartTimeUtc;
}
