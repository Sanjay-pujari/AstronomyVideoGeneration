using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public sealed class AstronomyKnowledgeValidationRuleRegistry : IAstronomyKnowledgeValidationRuleRegistry
{
    private readonly IReadOnlyDictionary<string,AstronomyKnowledgeValidationRuleDescriptor> byRuleId;
    public AstronomyKnowledgeValidationRuleRegistry(IEnumerable<AstronomyKnowledgeValidationRuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var ordered = descriptors.Select(d => d ?? throw new ArgumentException("Rule descriptors cannot contain null entries.", nameof(descriptors)))
            .OrderBy(d => d.Order).ThenBy(d => d.RuleId, StringComparer.Ordinal).ThenBy(d => d.Domain).ThenBy(d => d.Family).ThenBy(d => d.PayloadType.FullName, StringComparer.Ordinal).ThenBy(d => d.RuleType.FullName, StringComparer.Ordinal).ToArray();
        var dupId=ordered.GroupBy(d=>d.RuleId,StringComparer.Ordinal).FirstOrDefault(g=>g.Count()>1); if(dupId is not null) throw new ArgumentException($"Duplicate validation rule ID '{dupId.Key}'.", nameof(descriptors));
        var dup=ordered.GroupBy(d => (d.RuleId,d.RuleType,d.PayloadType,d.Domain,d.Family,d.Order)).FirstOrDefault(g=>g.Count()>1); if(dup is not null) throw new ArgumentException("Duplicate validation rule descriptor.", nameof(descriptors));
        Descriptors = Array.AsReadOnly(ordered); byRuleId=ordered.ToDictionary(d=>d.RuleId,d=>d,StringComparer.Ordinal);
    }
    public IReadOnlyList<AstronomyKnowledgeValidationRuleDescriptor> Descriptors { get; }
    public bool TryGetByRuleId(string ruleId, out AstronomyKnowledgeValidationRuleDescriptor descriptor) => byRuleId.TryGetValue(ruleId ?? string.Empty, out descriptor!);
    public IReadOnlyList<AstronomyKnowledgeValidationRuleDescriptor> GetApplicable(Type payloadType, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        if (!typeof(ITypedAstronomyKnowledgePayload).IsAssignableFrom(payloadType)) throw new ArgumentException("Payload type must implement ITypedAstronomyKnowledgePayload.", nameof(payloadType));
        if (!Enum.IsDefined(domain)) throw new ArgumentOutOfRangeException(nameof(domain)); if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        return Array.AsReadOnly(Descriptors.Where(d => d.Domain==domain && d.Family==family && d.PayloadType.IsAssignableFrom(payloadType)).OrderBy(d=>d.Order).ThenBy(d=>d.RuleId,StringComparer.Ordinal).ThenBy(d=>d.RuleType.FullName,StringComparer.Ordinal).ToArray());
    }
}
