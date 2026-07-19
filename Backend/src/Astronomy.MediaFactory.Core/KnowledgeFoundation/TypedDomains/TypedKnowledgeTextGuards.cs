namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

internal static class TypedKnowledgeTextGuards
{
    public static string RequireText(
        string? value,
        int maxLength,
        string parameterName,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{displayName} is required.", parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        }

        return normalized;
    }

    public static string? NormalizeOptionalText(
        string? value,
        int maxLength,
        string parameterName,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        }

        return normalized;
    }
}
