using Astronomy.MediaFactory.Core.KnowledgeFoundation;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public readonly record struct AstronomyTemporalPatternId
{
    public AstronomyTemporalPatternId(string value) { Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy temporal pattern ID", 128).ToLowerInvariant(); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyTemporalPatternId Create(string value) => new(value);
}
