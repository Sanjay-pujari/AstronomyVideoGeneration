namespace Astronomy.MediaFactory.Tests;

internal static class RepositoryTestPaths
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AstronomyVideoGeneration.sln")) || Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository root from {AppContext.BaseDirectory}; expected parent containing AstronomyVideoGeneration.sln or .git.");
    }

    public static string InfrastructureSource(params string[] parts)
    {
        var path = Path.Combine(new[] { Root(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure" }.Concat(parts).ToArray());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Requested infrastructure source file was not found: {Path.Combine(parts)}", path);
        }

        return path;
    }

    public static string CoreSource(params string[] parts)
    {
        var path = Path.Combine(new[] { Root(), "Backend", "src", "Astronomy.MediaFactory.Core" }.Concat(parts).ToArray());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Requested core source file was not found: {Path.Combine(parts)}", path);
        }

        return path;
    }
}
