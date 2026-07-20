namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public readonly record struct AstronomyEventId
{
    public AstronomyEventId(string value) { Value = EventText.Token(value, nameof(value), "Astronomy event ID"); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyEventId Create(string value) => new(value);
}
