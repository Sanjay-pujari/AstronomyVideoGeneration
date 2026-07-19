using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

public readonly record struct AstronomyKnowledgeTypeId
{
    private const int MaxLength = 128;

    public AstronomyKnowledgeTypeId(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy knowledge type ID", MaxLength).ToLowerInvariant();
    }

    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyKnowledgeTypeId Create(string value) => new(value);
}
