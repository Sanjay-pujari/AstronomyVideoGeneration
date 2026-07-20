using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public readonly record struct AstronomyOrbitalParameterId
{
    private const int MaxLength = 128;

    public AstronomyOrbitalParameterId(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy orbital parameter ID", MaxLength).ToLowerInvariant();
    }

    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
}
