using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyVisibilityWindow
{
    private const int MaxNoteLength = 512;
    public AstronomyVisibilityWindow(AstronomyObservationTimeWindow window, AstronomyVisibilityAssessment assessment, DateTimeOffset? peakTimeUtc = null, AstronomyMeasurement? peakAltitude = null, string? note = null)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));
        if (peakTimeUtc.HasValue)
        {
            if (peakTimeUtc.Value.Offset != TimeSpan.Zero) throw new ArgumentException("Peak time must use UTC (zero offset).", nameof(peakTimeUtc));
            if (peakTimeUtc.Value < Window.StartUtc || peakTimeUtc.Value > Window.EndUtc) throw new ArgumentException("Peak time must fall within the observation window.", nameof(peakTimeUtc));
        }
        if (peakAltitude is not null && peakAltitude.Unit.Dimension != AstronomyMeasurementDimension.Angle) throw new ArgumentException("Peak altitude must use the Angle dimension.", nameof(peakAltitude));
        PeakTimeUtc = peakTimeUtc; PeakAltitude = peakAltitude; Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Visibility window note");
    }
    public AstronomyObservationTimeWindow Window { get; }
    public AstronomyVisibilityAssessment Assessment { get; }
    public DateTimeOffset? PeakTimeUtc { get; }
    public AstronomyMeasurement? PeakAltitude { get; }
    public string? Note { get; }
}
