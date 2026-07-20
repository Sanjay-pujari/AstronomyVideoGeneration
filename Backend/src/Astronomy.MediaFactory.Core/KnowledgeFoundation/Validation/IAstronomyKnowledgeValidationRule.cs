using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Contract for deterministic typed knowledge validation rules.</summary>
public interface IAstronomyKnowledgeValidationRule
{
    string RuleId { get; }
    AstronomyKnowledgeDomain Domain { get; }
    AstronomyKnowledgePayloadFamily Family { get; }
    int Order { get; }
    bool Supports(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context);
    IEnumerable<AstronomyKnowledgeValidationIssue> Validate(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context);
}
