using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public sealed record AstronomyOrbitalReferenceContext
{
    public AstronomyOrbitalReferenceContext(AstronomyEntityReference centralBody, AstronomyReferenceFrame referenceFrame, AstronomyReferenceOrigin referenceOrigin, AstronomyEpochReference epoch)
    {
        CentralBody = centralBody ?? throw new ArgumentNullException(nameof(centralBody));
        ReferenceFrame = TypedKnowledgeEnumGuard.RequireDefined(referenceFrame, nameof(referenceFrame));
        ReferenceOrigin = TypedKnowledgeEnumGuard.RequireDefined(referenceOrigin, nameof(referenceOrigin));
        Epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
    }

    public AstronomyEntityReference CentralBody { get; }
    public AstronomyReferenceFrame ReferenceFrame { get; }
    public AstronomyReferenceOrigin ReferenceOrigin { get; }
    public AstronomyEpochReference Epoch { get; }
}
