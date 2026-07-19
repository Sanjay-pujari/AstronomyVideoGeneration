using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;

public sealed record AstronomyEpochReference
{
    public AstronomyEpochReference(AstronomyEpochKind kind, DateTimeOffset? instantUtc = null)
    {
        Kind = TypedKnowledgeEnumGuard.RequireDefined(kind, nameof(kind));
        if (instantUtc.HasValue && instantUtc.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Epoch instants must use UTC (zero offset).", nameof(instantUtc));
        if (Kind == AstronomyEpochKind.Custom && !instantUtc.HasValue)
            throw new ArgumentException("Custom epoch requires an explicit UTC instant.", nameof(instantUtc));
        if (Kind != AstronomyEpochKind.Custom && instantUtc.HasValue)
            throw new ArgumentException("Only custom epochs may carry an explicit instant.", nameof(instantUtc));
        InstantUtc = instantUtc;
    }

    public AstronomyEpochKind Kind { get; }
    public DateTimeOffset? InstantUtc { get; }

    public static AstronomyEpochReference Unspecified { get; } = new(AstronomyEpochKind.Unspecified);
    public static AstronomyEpochReference J2000 { get; } = new(AstronomyEpochKind.J2000);
    public static AstronomyEpochReference B1950 { get; } = new(AstronomyEpochKind.B1950);
    public static AstronomyEpochReference ObservationTime { get; } = new(AstronomyEpochKind.ObservationTime);
    public static AstronomyEpochReference Custom(DateTimeOffset instantUtc) => new(AstronomyEpochKind.Custom, instantUtc);
}
