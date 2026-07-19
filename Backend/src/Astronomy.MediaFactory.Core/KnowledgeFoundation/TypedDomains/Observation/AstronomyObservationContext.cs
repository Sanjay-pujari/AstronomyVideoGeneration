using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;

public sealed record AstronomyObservationContext
{
    private const int MaxLocationReferenceLength = 256;

    public AstronomyObservationContext(
        string observerLocationReference,
        DateTimeOffset observationTimeUtc,
        AstronomyReferenceFrame referenceFrame = AstronomyReferenceFrame.Unspecified,
        AstronomyReferenceOrigin referenceOrigin = AstronomyReferenceOrigin.Unspecified,
        AstronomyCoordinateSystem? coordinateSystem = null,
        decimal? altitudeMetres = null)
    {
        ObserverLocationReference = KnowledgeId.NormalizeToken(observerLocationReference, nameof(observerLocationReference), "Observer location reference", MaxLocationReferenceLength);
        if (observationTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Observation time must use UTC (zero offset).", nameof(observationTimeUtc));
        ObservationTimeUtc = observationTimeUtc;
        ReferenceFrame = TypedKnowledgeEnumGuard.RequireDefined(referenceFrame, nameof(referenceFrame));
        ReferenceOrigin = TypedKnowledgeEnumGuard.RequireDefined(referenceOrigin, nameof(referenceOrigin));
        CoordinateSystem = coordinateSystem.HasValue ? TypedKnowledgeEnumGuard.RequireDefined(coordinateSystem.Value, nameof(coordinateSystem)) : null;
        AltitudeMetres = altitudeMetres;
    }

    public string ObserverLocationReference { get; }
    public DateTimeOffset ObservationTimeUtc { get; }
    public AstronomyReferenceFrame ReferenceFrame { get; }
    public AstronomyReferenceOrigin ReferenceOrigin { get; }
    public AstronomyCoordinateSystem? CoordinateSystem { get; }
    public decimal? AltitudeMetres { get; }
}
