namespace Astronomy.MediaFactory.Rendering;

public static class FfmpegPathEscaper
{
    public static string ToDrawTextPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
            normalized = normalized[0] + "\\\\:" + normalized[2..];

        return normalized.Replace("'", "\\'", StringComparison.Ordinal);
    }
}
