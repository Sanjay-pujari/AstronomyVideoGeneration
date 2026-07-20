using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyVisibilityWindowsPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyVisibilityWindowsPayload>
{
    public AstronomyVisibilityWindowsPayload(AstronomyKnowledgeTypeId typeId, AstronomyObservationContext observationContext, IEnumerable<AstronomyVisibilityWindow> windows)
    {
        if (!typeId.IsValid) throw new ArgumentException("Visibility windows payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        ObservationContext = observationContext ?? throw new ArgumentNullException(nameof(observationContext));
        Windows = CopyWindows(windows);
    }
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.VisibilityWindow;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyObservationContext ObservationContext { get; }
    public IReadOnlyList<AstronomyVisibilityWindow> Windows { get; }
    public bool Equals(AstronomyVisibilityWindowsPayload? other)
        => other is not null
            && TypeId == other.TypeId
            && ObservationContext == other.ObservationContext
            && Windows.SequenceEqual(other.Windows);
    public override int GetHashCode() => Windows.Aggregate(HashCode.Combine(TypeId, ObservationContext), HashCode.Combine);
    private static IReadOnlyList<AstronomyVisibilityWindow> CopyWindows(IEnumerable<AstronomyVisibilityWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var ordered = windows
            .Select(w => w ?? throw new ArgumentException("Visibility windows cannot contain null entries.", nameof(windows)))
            .OrderBy(w => w.Window.StartUtc)
            .ThenBy(w => w.Window.EndUtc)
            .ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one visibility window is required.", nameof(windows));
        }

        if (ordered.GroupBy(w => w).Any(g => g.Count() > 1))
        {
            throw new ArgumentException("Visibility windows must not contain exact duplicates.", nameof(windows));
        }
        return Array.AsReadOnly(ordered);
    }
}
