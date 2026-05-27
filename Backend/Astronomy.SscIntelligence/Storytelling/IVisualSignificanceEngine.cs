using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public interface IVisualSignificanceEngine
{
    VisualSignificanceResult Score(CelestialEventType eventType, AngularRelationshipResult angular, IReadOnlyList<SkyObjectPosition> visibleObjects, NightWindowResult nightWindow);
}
