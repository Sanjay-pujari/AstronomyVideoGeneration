using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public sealed record AstronomyClassificationValue
{
    private const int MaxCodeLength = 128;
    private const int MaxDisplayNameLength = 160;
    private const int MaxDescriptionLength = 512;

    public AstronomyClassificationValue(string code, string displayName, string? description = null)
    {
        Code = KnowledgeId.NormalizeToken(code, nameof(code), "Astronomy classification code", MaxCodeLength).ToLowerInvariant();
        DisplayName = NormalizeText(displayName, nameof(displayName), "Astronomy classification display name", MaxDisplayNameLength, required: true)!;
        Description = NormalizeText(description, nameof(description), "Astronomy classification description", MaxDescriptionLength, required: false);
    }

    public string Code { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    private static string? NormalizeText(
        string? value,
        string parameterName,
        string displayName,
        int maxLength,
        bool required)
    {
        if (value is null)
        {
            if (required)
            {
                throw new ArgumentException($"{displayName} is required.", parameterName);
            }

            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            if (required)
            {
                throw new ArgumentException($"{displayName} is required.", parameterName);
            }

            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        }

        return normalized;
    }
}
