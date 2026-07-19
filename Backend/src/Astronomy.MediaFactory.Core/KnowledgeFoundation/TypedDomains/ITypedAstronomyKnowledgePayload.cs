using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

public interface ITypedAstronomyKnowledgePayload : IAstronomyKnowledgePayload
{
    AstronomyKnowledgeDomain Domain { get; }
    AstronomyKnowledgePayloadFamily Family { get; }
}
