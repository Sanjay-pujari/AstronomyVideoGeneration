using Astronomy.MediaFactory.Core.KnowledgeFoundation;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public readonly record struct AstronomyCyclePhaseId
{
    public AstronomyCyclePhaseId(string value) { Value = KnowledgeId.NormalizeToken(value, nameof(value), "Astronomy cycle phase ID", 128).ToLowerInvariant(); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyCyclePhaseId Create(string value) => new(value);
}
