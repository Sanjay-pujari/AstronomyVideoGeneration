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
            return new ProcessExecutionResult(-1, string.Empty, "Process failed to start.");
        }

        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
        return new ProcessExecutionResult(process.ExitCode, stdOut, stdErr);
    }
}

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);
