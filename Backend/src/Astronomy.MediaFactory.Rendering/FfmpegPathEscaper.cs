using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Rendering;

public static partial class FfmpegPathEscaper
{
    public static string ToDrawTextPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/');
        normalized = AlreadyEscapedDriveColonPattern().Replace(normalized, "$1:/$2");

        if (normalized.Length >= 3
            && char.IsAsciiLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/')
        {
            normalized = string.Concat(normalized[0], "\\:", normalized.AsSpan(2));
        }

        return normalized.Replace("'", "\\'", StringComparison.Ordinal);
    }

    [GeneratedRegex("^([A-Za-z])/{1,}:(?:/)(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AlreadyEscapedDriveColonPattern();
}
