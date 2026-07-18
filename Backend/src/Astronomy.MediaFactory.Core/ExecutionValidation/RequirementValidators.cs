using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Core.ExecutionValidation;

internal static class ValidatorSupport
{
    internal static bool TryCondition(ExecutionValidationRequest r, string? conditionKey, out bool applies)
    {
        applies = true; if (conditionKey is null) return true; if (!r.EvaluateConditionalRequirements) { applies = false; return true; }
        if (!r.Context.Metadata.TryGetValue($"condition:{conditionKey}", out var raw)) return false;
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) { applies = true; return true; }
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) { applies = false; return true; }
        return false;
    }
    internal static bool Include(FamilyRequirementLevel level, bool includeOptional) => level != FamilyRequirementLevel.Optional || includeOptional;
    internal static FamilyValidationSeverity Severity(FamilyRequirementLevel level) => level == FamilyRequirementLevel.Required ? FamilyValidationSeverity.Blocking : FamilyValidationSeverity.Information;
    internal static ExecutionRequirementEvaluation Eval(string id, FamilyValidationBoundary b, ExecutionRequirementOutcome o, FamilyValidationSeverity s, string? key, ExecutionValidationIssue? issue = null, ImmutableDictionary<string,string>? metadata = null) => new(id,b,o,s,issue is not null && s == FamilyValidationSeverity.Blocking,key, issue is null ? ImmutableArray<ExecutionValidationIssue>.Empty : ImmutableArray.Create(issue), metadata);
    internal static ExecutionRequirementEvaluation NotEvaluated(string id, FamilyValidationBoundary b, string? key) { var i = new ExecutionValidationIssue(ExecutionValidationIssueCode.ConditionalRequirementNotEvaluated, id, b, FamilyValidationSeverity.Information, ExecutionRequirementOutcome.NotEvaluated, "Conditional requirement was not evaluated because condition state was absent or disabled.", SourceKey:key); return Eval(id,b,ExecutionRequirementOutcome.NotEvaluated,FamilyValidationSeverity.Information,key,i); }
    internal static ExecutionRequirementEvaluation Missing(string id, FamilyValidationBoundary b, FamilyValidationSeverity s, ExecutionValidationIssueCode c, string key, string kind, ImmutableArray<string> evidence = default) { var i = new ExecutionValidationIssue(c,id,b,s,ExecutionRequirementOutcome.Missing,$"Required {kind} '{key}' is missing.",Expected:"present",Actual:"missing",SourceKey:key,Evidence:evidence); return Eval(id,b,ExecutionRequirementOutcome.Missing,s,key,i); }
}

public sealed class InputRequirementValidator : IExecutionRequirementValidator
{
    public string ValidatorId => "core.input-requirements"; public FamilyValidationBoundary Boundary => FamilyValidationBoundary.PreExecution; public bool CanValidate(FamilyExecutionContract c, FamilyExecutionContext _) => !c.InputRequirements.IsDefaultOrEmpty;
    public ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest r) => r.FamilyContract.InputRequirements.OrderBy(x=>x.RequirementId,StringComparer.OrdinalIgnoreCase).SelectMany(req => ValidateOne(r, req)).ToImmutableArray();
    private static IEnumerable<ExecutionRequirementEvaluation> ValidateOne(ExecutionValidationRequest r, FamilyInputRequirement req)
    {
        if (!ValidatorSupport.Include(req.Level, r.IncludeOptionalRequirements)) yield break;

        var severity = ValidatorSupport.Severity(req.Level);

        if (req.Level == FamilyRequirementLevel.Conditional)
        {
            if (!ValidatorSupport.TryCondition(r, req.ConditionKey, out var applies))
            {
                yield return ValidatorSupport.NotEvaluated(req.RequirementId, FamilyValidationBoundary.PreExecution, req.InputKey);
                yield break;
            }

            if (!applies)
            {
                yield return ValidatorSupport.Eval(req.RequirementId, FamilyValidationBoundary.PreExecution, ExecutionRequirementOutcome.ConditionalNotApplicable, FamilyValidationSeverity.Information, req.InputKey);
                yield break;
            }

            severity = FamilyValidationSeverity.Blocking;
        }

        var present = r.Context.InputValues.TryGetValue(req.InputKey, out var v) && v.IsPresent;
        yield return present
            ? ValidatorSupport.Eval(req.RequirementId, FamilyValidationBoundary.PreExecution, ExecutionRequirementOutcome.Satisfied, severity, req.InputKey)
            : ValidatorSupport.Missing(req.RequirementId, FamilyValidationBoundary.PreExecution, severity, ExecutionValidationIssueCode.RequiredInputMissing, req.InputKey, "input");
    }
}
public sealed class SemanticRequirementValidator : IExecutionRequirementValidator
{
    public string ValidatorId => "core.semantic-requirements"; public FamilyValidationBoundary Boundary => FamilyValidationBoundary.SemanticResolution; public bool CanValidate(FamilyExecutionContract c, FamilyExecutionContext _) => !c.SemanticRequirements.IsDefaultOrEmpty;
    public ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest r) => r.FamilyContract.SemanticRequirements.OrderBy(x=>x.RequirementId,StringComparer.OrdinalIgnoreCase).SelectMany(req => V(r, req)).ToImmutableArray();
    static IEnumerable<ExecutionRequirementEvaluation> V(ExecutionValidationRequest r, FamilySemanticRequirement req){ if(!ValidatorSupport.Include(req.Level,r.IncludeOptionalRequirements)) yield break; var applies = true; if(req.Level==FamilyRequirementLevel.Conditional&&!ValidatorSupport.TryCondition(r,req.ConditionKey,out applies)){yield return ValidatorSupport.NotEvaluated(req.RequirementId,FamilyValidationBoundary.SemanticResolution,req.CapabilityId);yield break;} if(req.Level==FamilyRequirementLevel.Conditional&&!applies){yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.SemanticResolution,ExecutionRequirementOutcome.ConditionalNotApplicable,FamilyValidationSeverity.Information,req.CapabilityId);yield break;} var ok=r.Context.SemanticValues.TryGetValue(req.CapabilityId,out var v)&&v.IsPresent; yield return ok ? ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.SemanticResolution,ExecutionRequirementOutcome.Satisfied,ValidatorSupport.Severity(req.Level),req.CapabilityId, metadata:v!.Metadata) : ValidatorSupport.Missing(req.RequirementId,FamilyValidationBoundary.SemanticResolution,ValidatorSupport.Severity(req.Level),ExecutionValidationIssueCode.RequiredSemanticValueMissing,req.CapabilityId,"semantic value", v?.Evidence ?? default); }
}
public sealed class ProjectionRequirementValidator : IExecutionRequirementValidator
{
    public string ValidatorId => "core.projection-requirements"; public FamilyValidationBoundary Boundary => FamilyValidationBoundary.Projection; public bool CanValidate(FamilyExecutionContract c, FamilyExecutionContext _) => !c.ProjectionRequirements.IsDefaultOrEmpty;
    public ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest r) => r.FamilyContract.ProjectionRequirements.OrderBy(x=>x.RequirementId,StringComparer.OrdinalIgnoreCase).SelectMany(req => V(r, req)).ToImmutableArray();
    static IEnumerable<ExecutionRequirementEvaluation> V(ExecutionValidationRequest r, FamilyProjectionRequirement req){ if(!ValidatorSupport.Include(req.Level,r.IncludeOptionalRequirements)) yield break; var applies = true; if(req.Level==FamilyRequirementLevel.Conditional&&!ValidatorSupport.TryCondition(r,req.ConditionKey,out applies)){yield return ValidatorSupport.NotEvaluated(req.RequirementId,FamilyValidationBoundary.Projection,req.TargetFactType);yield break;} if(req.Level==FamilyRequirementLevel.Conditional&&!applies){yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.Projection,ExecutionRequirementOutcome.ConditionalNotApplicable,FamilyValidationSeverity.Information,req.TargetFactType);yield break;} var ok=r.Context.ProjectionValues.TryGetValue(req.TargetFactType,out var v)&&v.IsPresent; yield return ok ? ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.Projection,ExecutionRequirementOutcome.Satisfied,ValidatorSupport.Severity(req.Level),req.TargetFactType) : ValidatorSupport.Missing(req.RequirementId,FamilyValidationBoundary.Projection,ValidatorSupport.Severity(req.Level),ExecutionValidationIssueCode.RequiredProjectionMissing,req.TargetFactType,"projection"); }
}
public sealed class ArtifactRequirementValidator : IExecutionRequirementValidator
{
    public string ValidatorId => "core.artifact-requirements"; public FamilyValidationBoundary Boundary => FamilyValidationBoundary.ArtifactGeneration; public bool CanValidate(FamilyExecutionContract c, FamilyExecutionContext _) => !c.ArtifactRequirements.IsDefaultOrEmpty;
    public ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest r) => r.FamilyContract.ArtifactRequirements.OrderBy(x=>x.RequirementId,StringComparer.OrdinalIgnoreCase).SelectMany(req => V(r, req)).ToImmutableArray();
    static IEnumerable<ExecutionRequirementEvaluation> V(ExecutionValidationRequest r, FamilyPhaseArtifactRequirement req){ var applies = true; if(req.ConditionKey is not null&&!ValidatorSupport.TryCondition(r,req.ConditionKey,out applies)){yield return ValidatorSupport.NotEvaluated(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,req.ArtifactId);yield break;} if(req.ConditionKey is not null&&!applies){yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,ExecutionRequirementOutcome.ConditionalNotApplicable,FamilyValidationSeverity.Information,req.ArtifactId);yield break;} var sev=req.Classification==FamilyArtifactClassification.Required?FamilyValidationSeverity.Blocking:FamilyValidationSeverity.Information; r.Context.ArtifactValues.TryGetValue(req.ArtifactId,out var v); var count=v?.ObservedCount??0; var exists=v is not null && v.Exists && count>0; if(!exists && req.Classification!=FamilyArtifactClassification.Required){yield return ValidatorSupport.Missing(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,sev,ExecutionValidationIssueCode.RequiredArtifactMissing,req.ArtifactId,"artifact");yield break;} if(!exists){yield return ValidatorSupport.Missing(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,sev,ExecutionValidationIssueCode.RequiredArtifactMissing,req.ArtifactId,"artifact");yield break;} if(!CardinalityOk(req.Cardinality,count)){ var i=new ExecutionValidationIssue(ExecutionValidationIssueCode.ArtifactCardinalityInvalid,req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,sev,ExecutionRequirementOutcome.Invalid,"Artifact cardinality is invalid.",req.Cardinality.ToString(),count.ToString(),req.ArtifactId); yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,ExecutionRequirementOutcome.Invalid,sev,req.ArtifactId,i); yield break;} if(req.MustBeNonEmpty&&!v!.IsNonEmpty){ var i=new ExecutionValidationIssue(ExecutionValidationIssueCode.ArtifactEmpty,req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,sev,ExecutionRequirementOutcome.Invalid,"Artifact exists but is empty.","non-empty","empty",req.ArtifactId); yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,ExecutionRequirementOutcome.Invalid,sev,req.ArtifactId,i); yield break;} yield return ValidatorSupport.Eval(req.RequirementId,FamilyValidationBoundary.ArtifactGeneration,ExecutionRequirementOutcome.Satisfied,sev,req.ArtifactId); }
    static bool CardinalityOk(FamilyArtifactCardinality c,int n)=>c switch{FamilyArtifactCardinality.ExactlyOne=>n==1,FamilyArtifactCardinality.OneOrMore=>n>=1,FamilyArtifactCardinality.ZeroOrOne=>n is 0 or 1,FamilyArtifactCardinality.ZeroOrMore=>true,_=>false};
}
public sealed class ContractValidationRuleValidator : IExecutionRequirementValidator
{
    public ContractValidationRuleValidator() : this(FamilyValidationBoundary.PostExecution) { }
    public ContractValidationRuleValidator(FamilyValidationBoundary boundary) => Boundary = boundary;
    public string ValidatorId => $"core.validation-rules:{Boundary}";
    public FamilyValidationBoundary Boundary { get; }
    public bool CanValidate(FamilyExecutionContract c, FamilyExecutionContext _) => !c.ValidationRequirements.IsDefaultOrEmpty && c.ValidationRequirements.Any(r => r.Boundary == Boundary);
    public ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest r) => r.FamilyContract.ValidationRequirements.Where(x=>x.Boundary==Boundary).OrderBy(x=>x.RequirementId,StringComparer.OrdinalIgnoreCase).Select(req => V(r, req)).ToImmutableArray();
    static ExecutionRequirementEvaluation V(ExecutionValidationRequest r, FamilyValidationRequirement req){ var applies = true; if(req.ConditionKey is not null&&!ValidatorSupport.TryCondition(r,req.ConditionKey,out applies)) return ValidatorSupport.NotEvaluated(req.RequirementId,req.Boundary,req.RuleId); if(req.ConditionKey is not null&&!applies) return ValidatorSupport.Eval(req.RequirementId,req.Boundary,ExecutionRequirementOutcome.ConditionalNotApplicable,FamilyValidationSeverity.Information,req.RuleId); if(!r.Context.ValidationRuleValues.TryGetValue(req.RuleId,out var v)) return ValidatorSupport.NotEvaluated(req.RequirementId,req.Boundary,req.RuleId); if(v.Passed) return ValidatorSupport.Eval(req.RequirementId,req.Boundary,ExecutionRequirementOutcome.Satisfied,req.Severity,req.RuleId, metadata:v.Metadata); var i=new ExecutionValidationIssue(ExecutionValidationIssueCode.ValidationRuleFailed,req.RequirementId,req.Boundary,req.Severity,ExecutionRequirementOutcome.Invalid,v.Message ?? "Validation rule observation failed.",v.Expected,v.Actual,req.RuleId,v.Evidence); return ValidatorSupport.Eval(req.RequirementId,req.Boundary,ExecutionRequirementOutcome.Invalid,req.Severity,req.RuleId,i); }
}
