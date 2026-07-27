namespace Astronomy.MediaFactory.ProductionAdapters;

/// <summary>Centralizes the platform rules used for owned-workspace containment.</summary>
public static class DocumentaryPathComparison
{
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsBelow(string root, string candidate) =>
        IsBelow(root, candidate, Comparison);

    // Public to make Windows semantics testable on non-Windows build agents.
    public static bool IsBelow(string root, string candidate, StringComparison comparison)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidateFull = Path.GetFullPath(candidate);
        if (candidateFull.Equals(rootFull, comparison)) return true;
        var prefix = rootFull + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(prefix, comparison);
    }
}
