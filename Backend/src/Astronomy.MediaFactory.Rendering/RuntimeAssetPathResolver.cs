using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class RuntimeAssetPathResolver : IRuntimeAssetPathResolver
{
    public string BaseDirectory { get; } = NormalizeDirectory(AppContext.BaseDirectory);

    public string ResolveAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Asset path is required.", nameof(relativePath));

        if (Path.IsPathRooted(relativePath))
            return NormalizePath(Path.GetFullPath(relativePath));

        var normalized = NormalizeRelativePath(relativePath);
        var resolved = Path.GetFullPath(Path.Combine(BaseDirectory, normalized));
        return NormalizePath(resolved);
    }

    public string ResolveFontPath(string relativeFontPath) => ResolveAssetPath(relativeFontPath);

    public string ResolveCelestialAssetPath(string objectKey, string fileName)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Celestial object key is required.", nameof(objectKey));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Celestial asset file name is required.", nameof(fileName));

        return ResolveAssetPath(Path.Combine("assets", "celestial", NormalizeRelativePath(objectKey), NormalizeRelativePath(fileName)));
    }

    public string GetAssetsRoot() => ResolveAssetPath("assets");

    public string GetFontsRoot() => ResolveAssetPath(Path.Combine("assets", "fonts"));

    public string GetCelestialRoot() => ResolveAssetPath(Path.Combine("assets", "celestial"));

    public bool AssetExists(string relativePath) => File.Exists(ResolveAssetPath(relativePath)) || Directory.Exists(ResolveAssetPath(relativePath));

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeDirectory(string path)
        => NormalizePath(Path.GetFullPath(path));

    private static string NormalizePath(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
