using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>The single canonical representation used for approved knowledge field paths.</summary>
public static partial class Phase7CanonicalFieldPathPolicy
{
    [GeneratedRegex(@"\[(?:\d+|\*?)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ArrayOrdinal();

    public static bool TryCanonicalize(string? value, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(value) || value.Contains("..", StringComparison.Ordinal)) return false;
        var normalized = ArrayOrdinal().Replace(value.Trim().Replace('\\', '.').Replace('/', '.'), "");
        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || !IsIdentifier(s))) return false;
        canonical = string.Join('.', segments.Select(LowerCamel));
        return true;
    }

    public static string Canonicalize(string value) => TryCanonicalize(value, out var canonical)
        ? canonical
        : throw new ArgumentException("P7KNOWLEDGE_FIELD_PATH_INVALID", nameof(value));

    private static bool IsIdentifier(string value) => value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    private static string LowerCamel(string value)
    {
        var words = value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        var first = words[0];
        var uppercasePrefix = 1;
        while (uppercasePrefix < first.Length && char.IsUpper(first[uppercasePrefix])
            && (uppercasePrefix + 1 == first.Length || char.IsUpper(first[uppercasePrefix + 1]))) uppercasePrefix++;
        var result = first[..uppercasePrefix].ToLowerInvariant() + first[uppercasePrefix..];
        return result + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
