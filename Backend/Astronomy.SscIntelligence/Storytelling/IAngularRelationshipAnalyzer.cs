using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public interface IAngularRelationshipAnalyzer
{
    AngularRelationshipResult Analyze(IReadOnlyList<SkyObjectPosition> visibleObjects);
}
