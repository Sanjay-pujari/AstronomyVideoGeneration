using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Core.ExecutionValidation;

public interface IExecutionRequirementValidator
{
    string ValidatorId { get; }
    FamilyValidationBoundary Boundary { get; }
    bool CanValidate(FamilyExecutionContract contract, FamilyExecutionContext context);
    ImmutableArray<ExecutionRequirementEvaluation> Validate(ExecutionValidationRequest request);
}
public interface IExecutionValidationPipeline { ExecutionValidationResult Validate(ExecutionValidationRequest request); }
public interface IExecutionClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemExecutionClock : IExecutionClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class ExecutionValidationPipeline : IExecutionValidationPipeline
{
    private readonly ImmutableArray<IExecutionRequirementValidator> validators; private readonly IExecutionClock clock;
    public ExecutionValidationPipeline(IEnumerable<IExecutionRequirementValidator> validators, IExecutionClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(validators); this.clock = clock ?? new SystemExecutionClock();
        var list = validators.OrderBy(v => v.ValidatorId, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in list) { if (string.IsNullOrWhiteSpace(v.ValidatorId)) throw new ArgumentException("ValidatorId must be non-empty.", nameof(validators)); if (!seen.Add(v.ValidatorId)) throw new ArgumentException($"Duplicate validator id '{v.ValidatorId}'.", nameof(validators)); }
        this.validators = list;
    }
    public ExecutionValidationResult Validate(ExecutionValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); var started = request.StartedUtc ?? clock.UtcNow; var evaluations = ImmutableArray.CreateBuilder<ExecutionRequirementEvaluation>();
        evaluations.AddRange(ContractChecks(request));
        var selected = validators.Where(v => v.Boundary == request.Boundary && v.CanValidate(request.FamilyContract, request.Context)).OrderBy(v => v.ValidatorId, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        if (selected.Length == 0) evaluations.Add(Fail("pipeline.unsupportedBoundary", request.Boundary, FamilyValidationSeverity.Blocking, ExecutionRequirementOutcome.NotEvaluated, ExecutionValidationIssueCode.UnsupportedBoundary, $"No validator supports boundary '{request.Boundary}'.", null, null, null));
        foreach (var v in selected) evaluations.AddRange(v.Validate(request));
        var completed = clock.UtcNow;
        return new ExecutionValidationResult(request.Context.ExecutionId, request.Context.DomainId, request.Context.FamilyId, request.Context.ContractVersion, request.Boundary, evaluations.ToImmutable(), started, completed, selected.Select(v => v.ValidatorId).ToImmutableArray(), request.Metadata);
    }
    private static IEnumerable<ExecutionRequirementEvaluation> ContractChecks(ExecutionValidationRequest r)
    {
        if (!string.Equals(r.Context.DomainId, r.DomainContract.DomainId, StringComparison.OrdinalIgnoreCase)) yield return Mismatch(r, "contract.domain", "DomainId", r.DomainContract.DomainId, r.Context.DomainId);
        if (!r.DomainContract.Families.Any(f => string.Equals(f.FamilyId, r.FamilyContract.FamilyId, StringComparison.OrdinalIgnoreCase) && string.Equals(f.ContractVersion, r.FamilyContract.ContractVersion, StringComparison.Ordinal))) yield return Mismatch(r, "contract.familyMembership", "FamilyContract", "Family listed in DomainContract.Families", r.FamilyContract.FamilyId);
        if (!string.Equals(r.Context.FamilyId, r.FamilyContract.FamilyId, StringComparison.OrdinalIgnoreCase)) yield return Mismatch(r, "contract.family", "FamilyId", r.FamilyContract.FamilyId, r.Context.FamilyId);
        if (!string.Equals(r.Context.ContractVersion, r.FamilyContract.ContractVersion, StringComparison.Ordinal)) yield return Mismatch(r, "contract.version", "ContractVersion", r.FamilyContract.ContractVersion, r.Context.ContractVersion);
    }
    private static ExecutionRequirementEvaluation Mismatch(ExecutionValidationRequest r, string id, string key, string expected, string actual) => Fail(id, r.Boundary, FamilyValidationSeverity.Blocking, ExecutionRequirementOutcome.Invalid, ExecutionValidationIssueCode.ContractMismatch, $"{key} does not match the selected contract.", expected, actual, key);
    internal static ExecutionRequirementEvaluation Fail(string id, FamilyValidationBoundary boundary, FamilyValidationSeverity severity, ExecutionRequirementOutcome outcome, ExecutionValidationIssueCode code, string message, string? expected, string? actual, string? sourceKey, ImmutableArray<string> evidence = default)
    { var issue = new ExecutionValidationIssue(code, id, boundary, severity, outcome, message, expected, actual, sourceKey, evidence); return new ExecutionRequirementEvaluation(id, boundary, outcome, severity, severity == FamilyValidationSeverity.Blocking, sourceKey, ImmutableArray.Create(issue)); }
}
public static class ExecutionValidationPipelineFactory
{
    public static IExecutionValidationPipeline CreateDefault(IExecutionClock? clock = null) => new ExecutionValidationPipeline(new IExecutionRequirementValidator[] { new ArtifactRequirementValidator(), new ContractValidationRuleValidator(), new InputRequirementValidator(), new ProjectionRequirementValidator(), new SemanticRequirementValidator() }, clock);
}
