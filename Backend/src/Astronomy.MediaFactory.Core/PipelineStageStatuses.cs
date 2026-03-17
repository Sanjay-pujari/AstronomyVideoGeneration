namespace Astronomy.MediaFactory.Core;

public static class PipelineStageStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string FailedWithFallback = "FailedWithFallback";

    public static bool IsFailed(string status)
        => status.StartsWith(Failed, StringComparison.Ordinal);
}
