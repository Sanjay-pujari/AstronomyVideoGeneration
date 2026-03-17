using System.Diagnostics;

namespace Astronomy.MediaFactory.Rendering;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
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
                ExceptionText: string.Empty);
        }

        string stdOut;
        string stdErr;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
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
                ExceptionText: ex.ToString());
        }

        return new ProcessExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdOut,
            StandardError: stdErr,
            StartTimeUtc: start,
            EndTimeUtc: DateTimeOffset.UtcNow,
            FileName: fileName,
            Arguments: arguments,
            ExceptionText: string.Empty);
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
    string ExceptionText)
{
    public TimeSpan Duration => EndTimeUtc - StartTimeUtc;
}
