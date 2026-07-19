namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public sealed record AstronomyPhysicalProperty
{
    private const int MaxNoteLength = 512;
    public AstronomyPhysicalProperty(AstronomyPhysicalPropertyId propertyId, AstronomyPhysicalPropertyCategory category, AstronomyPhysicalPropertyValue value, AstronomyPhysicalPropertyQualifier? qualifier = null, string? note = null)
    {
        if (!propertyId.IsValid) throw new ArgumentException("Physical property ID is required.", nameof(propertyId));
        PropertyId = propertyId;
        Category = EnumGuard.RequireDefined(category, nameof(category));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Qualifier = qualifier.HasValue ? EnumGuard.RequireDefined(qualifier.Value, nameof(qualifier)) : null;
        Note = NormalizeNote(note);
    }
    public AstronomyPhysicalPropertyId PropertyId { get; }
    public AstronomyPhysicalPropertyCategory Category { get; }
    public AstronomyPhysicalPropertyValue Value { get; }
    public AstronomyPhysicalPropertyQualifier? Qualifier { get; }
    public string? Note { get; }
    private static string? NormalizeNote(string? value) { if (value is null) return null; var normalized = value.Trim(); if (normalized.Length == 0) return null; if (normalized.Length > MaxNoteLength) throw new ArgumentException($"Physical property note must be {MaxNoteLength} characters or fewer.", nameof(value)); if (normalized.Any(char.IsControl)) throw new ArgumentException("Physical property note must not contain control characters.", nameof(value)); return normalized; }
}
