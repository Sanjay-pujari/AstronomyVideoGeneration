using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomyPositionReferenceContext
{
    public AstronomyPositionReferenceContext(AstronomyReferenceFrame referenceFrame, AstronomyReferenceOrigin referenceOrigin, AstronomyCoordinateSystem coordinateSystem, AstronomyEpochReference epoch, AstronomyEntityReference? originBody = null)
    {
        ReferenceFrame = TypedKnowledgeEnumGuard.RequireDefined(referenceFrame, nameof(referenceFrame));
        ReferenceOrigin = TypedKnowledgeEnumGuard.RequireDefined(referenceOrigin, nameof(referenceOrigin));
        CoordinateSystem = TypedKnowledgeEnumGuard.RequireDefined(coordinateSystem, nameof(coordinateSystem));
        Epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        OriginBody = originBody;
    }

    public AstronomyReferenceFrame ReferenceFrame { get; }
    public AstronomyReferenceOrigin ReferenceOrigin { get; }
    public AstronomyCoordinateSystem CoordinateSystem { get; }
    public AstronomyEpochReference Epoch { get; }
    public AstronomyEntityReference? OriginBody { get; }
}
