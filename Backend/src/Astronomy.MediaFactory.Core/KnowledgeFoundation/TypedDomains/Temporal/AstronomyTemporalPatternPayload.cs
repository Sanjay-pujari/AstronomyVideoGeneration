using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyTemporalPatternPayload : ITypedAstronomyKnowledgePayload
{
    public AstronomyTemporalPatternPayload(AstronomyKnowledgeTypeId typeId, AstronomyTemporalPattern pattern) { if (!typeId.IsValid) throw new ArgumentException("Astronomy knowledge type ID is required.", nameof(typeId)); TypeId = typeId; Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern)); }
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyTemporalPattern Pattern { get; }
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Temporal;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.TemporalCycle;
}
