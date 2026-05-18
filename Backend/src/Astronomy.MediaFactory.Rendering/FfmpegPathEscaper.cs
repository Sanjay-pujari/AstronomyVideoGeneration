namespace Astronomy.MediaFactory.Rendering;

public static class FfmpegPathEscaper
{
    public static string ToDrawTextPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/');
        return normalized.Replace("'", "\\'", StringComparison.Ordinal);
    }
}
