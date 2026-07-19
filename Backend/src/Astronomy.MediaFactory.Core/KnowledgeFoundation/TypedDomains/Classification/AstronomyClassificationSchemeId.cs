using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public readonly record struct AstronomyClassificationSchemeId
{
    private const int MaxLength = 128;

    public AstronomyClassificationSchemeId(string value)
    {
        Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy classification scheme ID", MaxLength).ToLowerInvariant();
    }

    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyClassificationSchemeId Create(string value) => new(value);
}
