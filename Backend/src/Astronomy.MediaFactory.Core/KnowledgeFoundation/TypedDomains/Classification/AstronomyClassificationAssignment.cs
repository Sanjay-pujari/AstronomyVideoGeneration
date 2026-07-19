using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

public sealed record AstronomyClassificationAssignment
{
    private const int MaxNoteLength = 512;

    public AstronomyClassificationAssignment(
        AstronomyClassificationSchemeId schemeId,
        AstronomyClassificationValue value,
        AstronomyClassificationQualifier qualifier,
        string? note = null)
    {
        if (!schemeId.IsValid)
        {
            throw new ArgumentException("Classification scheme ID is required.", nameof(schemeId));
        }

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
        return TypedKnowledgeTextGuards.NormalizeOptionalText(
            value,
            MaxNoteLength,
            nameof(value),
            "Classification note");
    }
}
