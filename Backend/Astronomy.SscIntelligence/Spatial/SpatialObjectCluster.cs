using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Spatial;

public sealed record SpatialObjectCluster(IReadOnlyList<SkyObjectPosition> Objects)
{
    public IReadOnlyList<string> ObjectNames => Objects.Select(x => x.Name).ToArray();
}
