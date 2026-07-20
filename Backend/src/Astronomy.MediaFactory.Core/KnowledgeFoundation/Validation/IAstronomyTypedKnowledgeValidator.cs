using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public interface IAstronomyTypedKnowledgeValidator
{
    AstronomyKnowledgeValidationResult Validate(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context);
}
