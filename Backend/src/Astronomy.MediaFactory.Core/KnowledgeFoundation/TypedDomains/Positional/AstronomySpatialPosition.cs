using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomySpatialPosition
{
    private const int MaxNoteLength = 512;

    public AstronomySpatialPosition(AstronomyPositionReferenceContext referenceContext, AstronomyPositionValue value, string? note = null)
    {
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Spatial position note");
    }

    public AstronomyPositionReferenceContext ReferenceContext { get; }
    public AstronomyPositionValue Value { get; }
    public string? Note { get; }
}
