using Astronomy.MediaFactory.Core.KnowledgeFoundation;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public readonly record struct AstronomyPhysicalPropertyId
{
    private const int MaxLength = 128;
    public AstronomyPhysicalPropertyId(string value) { Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy physical property ID", MaxLength).ToLowerInvariant(); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyPhysicalPropertyId Create(string value) => new(value);
}
