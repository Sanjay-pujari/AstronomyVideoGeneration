using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Immutable metadata describing a validation rule registration.</summary>
public sealed record AstronomyKnowledgeValidationRuleDescriptor
{
    public const int MinimumOrder = -100000; public const int MaximumOrder = 100000;
    public AstronomyKnowledgeValidationRuleDescriptor(string ruleId, Type ruleType, Type payloadType, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family, int order = 0)
    {
        RuleId = AstronomyKnowledgeValidationIssue.ValidateIdentifier(ruleId, nameof(ruleId));
        RuleType = ruleType ?? throw new ArgumentNullException(nameof(ruleType));
        if (!typeof(IAstronomyKnowledgeValidationRule).IsAssignableFrom(RuleType) || RuleType.IsAbstract || RuleType.IsInterface) throw new ArgumentException("Rule type must be a concrete IAstronomyKnowledgeValidationRule type.", nameof(ruleType));
        PayloadType = payloadType ?? throw new ArgumentNullException(nameof(payloadType));
        if (!typeof(ITypedAstronomyKnowledgePayload).IsAssignableFrom(PayloadType) || PayloadType.IsAbstract || PayloadType.IsInterface) throw new ArgumentException("Payload type must be a concrete ITypedAstronomyKnowledgePayload type.", nameof(payloadType));
        Domain = Enum.IsDefined(domain) ? domain : throw new ArgumentOutOfRangeException(nameof(domain));
        Family = Enum.IsDefined(family) ? family : throw new ArgumentOutOfRangeException(nameof(family));
        if (order < MinimumOrder || order > MaximumOrder) throw new ArgumentOutOfRangeException(nameof(order));
        Order = order;
    }
    public string RuleId { get; } public Type RuleType { get; } public Type PayloadType { get; } public AstronomyKnowledgeDomain Domain { get; } public AstronomyKnowledgePayloadFamily Family { get; } public int Order { get; }
}
