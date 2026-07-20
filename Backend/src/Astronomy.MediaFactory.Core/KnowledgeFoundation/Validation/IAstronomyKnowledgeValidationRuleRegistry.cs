using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public interface IAstronomyKnowledgeValidationRuleRegistry
{
    IReadOnlyList<AstronomyKnowledgeValidationRuleDescriptor> Descriptors { get; }
    bool TryGetByRuleId(string ruleId, out AstronomyKnowledgeValidationRuleDescriptor descriptor);
    IReadOnlyList<AstronomyKnowledgeValidationRuleDescriptor> GetApplicable(Type payloadType, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family);
}
