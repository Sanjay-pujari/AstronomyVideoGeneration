using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public interface ICelestialEventClassifier
{
    CelestialEventClassification Classify(IReadOnlyList<SkyObjectPosition> visibleObjects, AngularRelationshipResult angular);
}
