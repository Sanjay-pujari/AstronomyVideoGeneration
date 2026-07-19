using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

public sealed record AstronomyMeasurementUnit
{
    private const int MaxCodeLength = 96;
    private const int MaxSymbolLength = 32;
    private const int MaxDisplayNameLength = 128;

    public AstronomyMeasurementUnit(string code, string symbol, AstronomyMeasurementDimension dimension, string? displayName = null)
    {
        Code = KnowledgeId.NormalizeToken(code, nameof(code), "Measurement unit code", MaxCodeLength).ToLowerInvariant();
        Symbol = NormalizeText(symbol, nameof(symbol), "Measurement unit symbol", MaxSymbolLength, required: true)!;
        Dimension = TypedKnowledgeEnumGuard.RequireDefined(dimension, nameof(dimension));
        DisplayName = NormalizeText(displayName, nameof(displayName), "Measurement unit display name", MaxDisplayNameLength, required: false);
    }

    public string Code { get; }
    public string Symbol { get; }
    public AstronomyMeasurementDimension Dimension { get; }
    public string? DisplayName { get; }

    private static string? NormalizeText(string? value, string parameterName, string displayName, int maxLength, bool required)
    {
        if (value is null)
        {
            if (required) throw new ArgumentException($"{displayName} is required.", parameterName);
            return null;
        }
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            if (required) throw new ArgumentException($"{displayName} is required.", parameterName);
            return null;
        }
        if (normalized.Length > maxLength) throw new ArgumentException($"{displayName} must be {maxLength} characters or fewer.", parameterName);
        if (normalized.Any(char.IsControl)) throw new ArgumentException($"{displayName} must not contain control characters.", parameterName);
        return normalized;
    }
}
