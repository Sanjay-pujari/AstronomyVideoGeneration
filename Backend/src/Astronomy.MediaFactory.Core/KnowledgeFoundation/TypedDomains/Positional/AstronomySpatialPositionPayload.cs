using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomySpatialPositionPayload : ITypedAstronomyKnowledgePayload
{
    public AstronomySpatialPositionPayload(AstronomyKnowledgeTypeId typeId, AstronomySpatialPosition position)
    {
        if (!typeId.IsValid) throw new ArgumentException("Spatial position payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        Position = position ?? throw new ArgumentNullException(nameof(position));
    }

    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomySpatialPosition Position { get; }
}
