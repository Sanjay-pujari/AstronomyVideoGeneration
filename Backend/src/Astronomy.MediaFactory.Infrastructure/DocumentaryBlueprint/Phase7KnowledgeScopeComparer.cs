using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Compares only governed authority scope. Fact values and confidence never participate.</summary>
public sealed class Phase7KnowledgeScopeComparer : IPhase7KnowledgeScopeComparer
{
    public Phase7KnowledgeScopeComparison Compare(Phase7KnowledgeAuthorityScope evergreen, Phase7KnowledgeAuthorityScope @event)
    {
        if (!evergreen.HasExplicitEvidence && !@event.HasExplicitEvidence)
            return Phase7KnowledgeScopeComparison.SameScope;
        if (!evergreen.HasExplicitEvidence && @event.HasExplicitEvidence)
            return Phase7KnowledgeScopeComparison.EventIsSpecialization;
        if (evergreen.HasExplicitEvidence && !@event.HasExplicitEvidence)
            return Phase7KnowledgeScopeComparison.InsufficientScopeEvidence;
        if (Equal(evergreen, @event))
            return Phase7KnowledgeScopeComparison.SameScope;

        // Two independently identified places, event instances, or observation windows are
        // positive evidence of distinct scopes, rather than merely missing evidence.
        if (Different(evergreen.Location, @event.Location)
            || Different(evergreen.EventInstanceId, @event.EventInstanceId)
            || Different(evergreen.ObservationWindowId, @event.ObservationWindowId)
            || CoordinatesDiffer(evergreen, @event))
            return Phase7KnowledgeScopeComparison.DistinctNonConflictingScopes;

        if (Contains(evergreen, @event))
            return Phase7KnowledgeScopeComparison.EventIsSpecialization;

        // Overlapping, incompatible time windows describe conflicting scope assertions.
        if (evergreen.StartUtc.HasValue && evergreen.EndUtc.HasValue && @event.StartUtc.HasValue && @event.EndUtc.HasValue
            && evergreen.StartUtc <= @event.EndUtc && @event.StartUtc <= evergreen.EndUtc)
            return Phase7KnowledgeScopeComparison.ConflictingScope;

        return Phase7KnowledgeScopeComparison.InsufficientScopeEvidence;
    }

    private static bool Different(string? left, string? right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right) && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool CoordinatesDiffer(Phase7KnowledgeAuthorityScope a, Phase7KnowledgeAuthorityScope b) =>
        a.Latitude.HasValue && b.Latitude.HasValue && a.Latitude != b.Latitude
        || a.Longitude.HasValue && b.Longitude.HasValue && a.Longitude != b.Longitude;
    private static bool Contains(Phase7KnowledgeAuthorityScope general, Phase7KnowledgeAuthorityScope specific) =>
        Match(general.ScopeType, specific.ScopeType) && Match(general.Location, specific.Location)
        && Match(general.Latitude, specific.Latitude) && Match(general.Longitude, specific.Longitude)
        && Match(general.ReferenceDate, specific.ReferenceDate) && Match(general.EventInstanceId, specific.EventInstanceId)
        && Match(general.ObservationWindowId, specific.ObservationWindowId)
        && (!general.StartUtc.HasValue || specific.StartUtc >= general.StartUtc)
        && (!general.EndUtc.HasValue || specific.EndUtc <= general.EndUtc);
    private static bool Equal(Phase7KnowledgeAuthorityScope a, Phase7KnowledgeAuthorityScope b) =>
        string.Equals(a.ScopeType, b.ScopeType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Location, b.Location, StringComparison.OrdinalIgnoreCase)
        && a.Latitude == b.Latitude && a.Longitude == b.Longitude && a.StartUtc == b.StartUtc && a.EndUtc == b.EndUtc
        && a.ReferenceDate == b.ReferenceDate && string.Equals(a.EventInstanceId, b.EventInstanceId, StringComparison.Ordinal)
        && string.Equals(a.ObservationWindowId, b.ObservationWindowId, StringComparison.Ordinal);
    private static bool Match(string? general, string? specific) => string.IsNullOrWhiteSpace(general)
        || string.Equals(general, specific, StringComparison.OrdinalIgnoreCase);
    private static bool Match<T>(T? general, T? specific) where T : struct => !general.HasValue || general.Equals(specific);
}
