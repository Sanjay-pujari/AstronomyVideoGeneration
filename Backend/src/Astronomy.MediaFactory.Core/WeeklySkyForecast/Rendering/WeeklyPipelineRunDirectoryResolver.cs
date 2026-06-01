namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyPipelineRunDirectoryResolver
{
    Task<string> ResolveRunDirectoryAsync(Guid pipelineRunId);
}

public static class WeeklyPipelineRunDirectoryValidator
{
    private static readonly string[] RequiredDirectories = ["audio", "render", "episode"];

    public static bool IsValidRunDirectory(string path)
        => RequiredDirectories.All(directory => Directory.Exists(Path.Combine(path, directory)));

    public static string ToCanonicalPath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static IReadOnlyList<string> FindMissingRequiredDirectories(string path)
        => RequiredDirectories
            .Select(directory => Path.Combine(path, directory))
            .Where(directory => !Directory.Exists(directory))
            .ToList();
}
