using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public readonly record struct AstronomyObservationalQuantityId
{
    private const int MaxLength = 128;
    public AstronomyObservationalQuantityId(string value) => Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy observational quantity ID", MaxLength).ToLowerInvariant();
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
}
