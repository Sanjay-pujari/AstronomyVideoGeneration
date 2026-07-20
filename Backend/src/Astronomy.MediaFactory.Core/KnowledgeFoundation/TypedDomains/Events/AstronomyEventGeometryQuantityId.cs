namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public readonly record struct AstronomyEventGeometryQuantityId
{
    public AstronomyEventGeometryQuantityId(string value) { Value = EventText.Token(value, nameof(value), "Astronomy event geometry quantity ID"); }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
    public static AstronomyEventGeometryQuantityId Create(string value) => new(value);
}
