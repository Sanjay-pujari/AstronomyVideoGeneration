using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public sealed class AstronomyTypedKnowledgeValidator : IAstronomyTypedKnowledgeValidator
{
    private const string FoundationRuleId = "foundation.payload.descriptor-match";
    private readonly IAstronomyTypedPayloadRegistry payloadRegistry; private readonly IAstronomyKnowledgeValidationRuleRegistry ruleRegistry; private readonly IReadOnlyList<IAstronomyKnowledgeValidationRule> rules;
    public AstronomyTypedKnowledgeValidator(IAstronomyTypedPayloadRegistry payloadRegistry, IAstronomyKnowledgeValidationRuleRegistry ruleRegistry, IEnumerable<IAstronomyKnowledgeValidationRule> rules)
    { this.payloadRegistry=payloadRegistry??throw new ArgumentNullException(nameof(payloadRegistry)); this.ruleRegistry=ruleRegistry??throw new ArgumentNullException(nameof(ruleRegistry)); this.rules=(rules??throw new ArgumentNullException(nameof(rules))).Select(r=>r??throw new ArgumentException("Rules cannot contain null entries.",nameof(rules))).ToArray(); }
    public AstronomyKnowledgeValidationResult Validate(ITypedAstronomyKnowledgePayload payload, AstronomyKnowledgeValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(context);
        var issues=new List<AstronomyKnowledgeValidationIssue>(); var runtimeType=payload.GetType();
        if(!payloadRegistry.TryGetByPayloadType(runtimeType,out var payloadDescriptor)) return new AstronomyKnowledgeValidationResult(new[]{Issue(AstronomyKnowledgeValidationCodes.PayloadUnregistered,"Payload runtime type is not registered.",payload.Domain,payload.Family)});
        var prop=runtimeType.GetProperty("TypeId"); var typeId=prop?.GetValue(payload)?.ToString();
        if(!payloadDescriptor.PayloadType.IsAssignableFrom(runtimeType)) issues.Add(Issue(AstronomyKnowledgeValidationCodes.PayloadRuntimeTypeMismatch,"Payload runtime type does not match its descriptor.",payloadDescriptor.Domain,payloadDescriptor.Family));
        if(!StringComparer.Ordinal.Equals(typeId,payloadDescriptor.Discriminator)) issues.Add(Issue(AstronomyKnowledgeValidationCodes.PayloadTypeIdMismatch,"Payload type ID does not match its descriptor discriminator.",payloadDescriptor.Domain,payloadDescriptor.Family));
        if(payload.Domain!=payloadDescriptor.Domain) issues.Add(Issue(AstronomyKnowledgeValidationCodes.PayloadDomainMismatch,"Payload domain does not match its descriptor.",payloadDescriptor.Domain,payloadDescriptor.Family));
        if(payload.Family!=payloadDescriptor.Family) issues.Add(Issue(AstronomyKnowledgeValidationCodes.PayloadFamilyMismatch,"Payload family does not match its descriptor.",payloadDescriptor.Domain,payloadDescriptor.Family));
        if(issues.Count>0) return new AstronomyKnowledgeValidationResult(issues.Where(i => i.Severity >= context.MinimumSeverity));
        var ruleMap=rules.ToDictionary(r => (r.RuleId, r.GetType()), r => r);
        foreach(var descriptor in ruleRegistry.GetApplicable(runtimeType,payload.Domain,payload.Family).OrderBy(d=>d.Order).ThenBy(d=>d.RuleId,StringComparer.Ordinal).ThenBy(d=>d.RuleType.FullName,StringComparer.Ordinal))
        {
            if(!ruleMap.TryGetValue((descriptor.RuleId,descriptor.RuleType),out var rule)) continue;
            VerifyRule(rule, descriptor);
            if(!rule.Supports(payload,context)) continue;
            foreach(var issue in rule.Validate(payload,context) ?? throw new InvalidOperationException($"Rule '{rule.RuleId}' returned a null issue collection."))
            {
                if(issue is null) throw new InvalidOperationException($"Rule '{rule.RuleId}' returned a null issue.");
                if(!StringComparer.Ordinal.Equals(issue.RuleId,descriptor.RuleId)) throw new InvalidOperationException("Validation issue rule ID does not match executing rule.");
                if(issue.Domain!=descriptor.Domain) throw new InvalidOperationException("Validation issue domain does not match executing rule.");
                if(issue.Family!=descriptor.Family) throw new InvalidOperationException("Validation issue family does not match executing rule.");
                if(issue.Severity >= context.MinimumSeverity) issues.Add(issue);
            }
        }
        return new AstronomyKnowledgeValidationResult(issues);
    }
    private static void VerifyRule(IAstronomyKnowledgeValidationRule rule, AstronomyKnowledgeValidationRuleDescriptor d)
    { if(rule.GetType()!=d.RuleType || rule.RuleId!=d.RuleId || rule.Domain!=d.Domain || rule.Family!=d.Family || rule.Order!=d.Order) throw new InvalidOperationException($"Validation rule '{d.RuleId}' metadata does not match its descriptor."); }
    private static AstronomyKnowledgeValidationIssue Issue(string code,string message,AstronomyKnowledgeDomain domain,AstronomyKnowledgePayloadFamily family) => new(code,AstronomyKnowledgeValidationSeverity.Critical,message,"$",FoundationRuleId,domain,family);
}
