using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Type-safe base class for typed knowledge validation rules.</summary>
public abstract class AstronomyKnowledgeValidationRule<TPayload> : IAstronomyKnowledgeValidationRule where TPayload : class, ITypedAstronomyKnowledgePayload
{
    public abstract string RuleId { get; }
    public abstract AstronomyKnowledgeDomain Domain { get; }
    public abstract AstronomyKnowledgePayloadFamily Family { get; }
    public virtual int Order => 0;
    public virtual bool Supports(TPayload payload, AstronomyKnowledgeValidationContext context) => true;
    public bool Supports(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(context);
        return payload is TPayload typed && payload.Domain.Equals(Domain) && payload.Family.Equals(Family) && Supports(typed, context);
    }
    public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(context);
        if (payload is not TPayload typed) throw new ArgumentException("Payload type is not supported by this validation rule.", nameof(payload));
        if (!payload.Domain.Equals(Domain) || !payload.Family.Equals(Family)) throw new ArgumentException("Payload domain or family is not supported by this validation rule.", nameof(payload));
        return (ValidateTyped(typed, context) ?? throw new InvalidOperationException($"Rule '{RuleId}' returned a null issue collection.")).ToArray();
    }
    protected abstract IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(TPayload payload, AstronomyKnowledgeValidationContext context);
}
