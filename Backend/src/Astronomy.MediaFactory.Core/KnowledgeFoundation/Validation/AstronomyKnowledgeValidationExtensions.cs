using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public static class AstronomyKnowledgeValidationExtensions
{
    public static AstronomyKnowledgeValidationResult Validate(this ITypedAstronomyKnowledgePayload payload, IAstronomyTypedKnowledgeValidator validator, AstronomyKnowledgeValidationContext context)
    { ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(validator); return validator.Validate(payload, context); }
}
