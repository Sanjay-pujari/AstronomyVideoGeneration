namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public sealed record AstronomyClassificationAssignment
{
    private const int MaxNoteLength = 512;

    public AstronomyClassificationAssignment(AstronomyClassificationSchemeId schemeId, AstronomyClassificationValue value, AstronomyClassificationQualifier qualifier, string? note = null)
    {
        if (!schemeId.IsValid) throw new ArgumentException("Classification scheme ID is required.", nameof(schemeId));
        SchemeId = schemeId;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Qualifier = EnumGuard.RequireDefined(qualifier, nameof(qualifier));
        Note = NormalizeNote(note);
    }

    public AstronomyClassificationSchemeId SchemeId { get; }
    public AstronomyClassificationValue Value { get; }
    public AstronomyClassificationQualifier Qualifier { get; }
    public string? Note { get; }

    private static string? NormalizeNote(string? value)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > MaxNoteLength) throw new ArgumentException($"Classification note must be {MaxNoteLength} characters or fewer.", nameof(value));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Classification note must not contain control characters.", nameof(value));
        return normalized;
    }
}
