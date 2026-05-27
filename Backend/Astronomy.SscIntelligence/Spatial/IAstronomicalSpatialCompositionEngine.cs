using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Spatial;

public interface IAstronomicalSpatialCompositionEngine
{
    SpatialCompositionResult Analyze(IReadOnlyList<SkyObjectPosition> objects);
}
