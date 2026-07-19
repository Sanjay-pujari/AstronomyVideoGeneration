namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public sealed record AstronomyTextPhysicalPropertyValue : AstronomyPhysicalPropertyValue
{
    private const int MaxLength = 512;
    public AstronomyTextPhysicalPropertyValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Physical property text value is required.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > MaxLength) throw new ArgumentException($"Physical property text value must be {MaxLength} characters or fewer.", nameof(value));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Physical property text value must not contain control characters.", nameof(value));
        Value = normalized;
    }
    public override AstronomyPhysicalPropertyValueKind Kind => AstronomyPhysicalPropertyValueKind.Text;
    public string Value { get; }
}
