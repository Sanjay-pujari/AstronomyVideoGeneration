namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public readonly record struct AstronomyEventCircumstanceId
{
    public AstronomyEventCircumstanceId(string value) { Value = EventText.Token(value, nameof(value), "Astronomy event circumstance ID"); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyEventCircumstanceId Create(string value) => new(value);
}
