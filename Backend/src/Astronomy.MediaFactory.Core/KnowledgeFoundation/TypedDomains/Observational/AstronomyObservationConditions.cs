using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyObservationConditions
{
    private const int MaxNoteLength = 512;
    public AstronomyObservationConditions(AstronomySkyConditionKind skyCondition, AstronomySeeingQuality seeing, AstronomyTransparencyQuality transparency, AstronomyMeasurement? limitingMagnitude = null, AstronomyMeasurement? skyBrightness = null, string? note = null)
    {
        SkyCondition = EnumGuard.RequireDefined(skyCondition, nameof(skyCondition));
        Seeing = EnumGuard.RequireDefined(seeing, nameof(seeing));
        Transparency = EnumGuard.RequireDefined(transparency, nameof(transparency));
        LimitingMagnitude = limitingMagnitude;
        SkyBrightness = skyBrightness;
        if (note is not null && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Observation conditions note must not be blank when supplied.", nameof(note));
        }
        Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Observation conditions note");
    }
    public AstronomySkyConditionKind SkyCondition { get; }
    public AstronomySeeingQuality Seeing { get; }
    public AstronomyTransparencyQuality Transparency { get; }
    public AstronomyMeasurement? LimitingMagnitude { get; }
    public AstronomyMeasurement? SkyBrightness { get; }
    public string? Note { get; }
}
