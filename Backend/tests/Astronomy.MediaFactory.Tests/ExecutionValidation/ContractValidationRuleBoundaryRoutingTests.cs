using System.Collections.Immutable;
using Astronomy.MediaFactory.Core.ExecutionContracts;
using Astronomy.MediaFactory.Core.ExecutionValidation;
using Xunit;

namespace Astronomy.MediaFactory.Tests.ExecutionValidation;

public sealed class ContractValidationRuleBoundaryRoutingTests
{
    public static IEnumerable<object[]> Boundaries() => Enum.GetValues<FamilyValidationBoundary>().Select(b => new object[] { b });

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Validation_requirement_is_evaluated_only_at_its_declared_boundary(FamilyValidationBoundary boundary)
    {
        var rule = Rule(boundary, "rule.boundary");
        var contract = Contract(rule);
        var context = Ctx(R(new ExecutionRuleValue("rule.boundary", true)));
        var pipeline = Pipeline();

        foreach (var current in Enum.GetValues<FamilyValidationBoundary>())
        {
            var result = pipeline.Validate(Req(contract, context, current));
            var evaluations = result.Evaluations.Where(e => e.RequirementId == rule.RequirementId).ToArray();

            if (current == boundary)
            {
                var evaluation = Assert.Single(evaluations);
                Assert.Equal(boundary, evaluation.Boundary);
                Assert.Equal("rule.boundary", evaluation.SourceKey);
                Assert.Equal(ExecutionRequirementOutcome.Satisfied, evaluation.Outcome);
            }
            else
            {
                Assert.Empty(evaluations);
                Assert.DoesNotContain(result.Issues, i => i.SourceKey == "rule.boundary");
            }
        }
    }

    [Fact]
    public void Same_rule_is_not_evaluated_twice_when_multiple_boundary_validators_exist()
    {
        var contract = Contract(Rule(FamilyValidationBoundary.SemanticResolution, "rule.once"));
        var result = Pipeline().Validate(Req(contract, Ctx(R(new ExecutionRuleValue("rule.once", true))), FamilyValidationBoundary.SemanticResolution));

        Assert.Equal(1, result.Evaluations.Count(e => e.SourceKey == "rule.once"));
    }

    [Fact]
    public void Validators_have_unique_deterministic_ids()
    {
        var validators = Enum.GetValues<FamilyValidationBoundary>().Select(b => new ContractValidationRuleValidator(b)).ToArray();

        Assert.Equal(new[]
        {
            "core.validation-rules:PreExecution",
            "core.validation-rules:SemanticResolution",
            "core.validation-rules:Projection",
            "core.validation-rules:ArtifactGeneration",
            "core.validation-rules:PostExecution"
        }, validators.Select(v => v.ValidatorId).ToArray());
        Assert.Equal(validators.Length, validators.Select(v => v.ValidatorId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Rules_from_one_boundary_do_not_leak_into_another_boundary()
    {
        var contract = Contract(
            Rule(FamilyValidationBoundary.PreExecution, "rule.pre"),
            Rule(FamilyValidationBoundary.SemanticResolution, "rule.semantic"));
        var context = Ctx(R(new ExecutionRuleValue("rule.pre", false), new ExecutionRuleValue("rule.semantic", false)));

        var pre = Pipeline().Validate(Req(contract, context, FamilyValidationBoundary.PreExecution));
        var semantic = Pipeline().Validate(Req(contract, context, FamilyValidationBoundary.SemanticResolution));

        Assert.Contains(pre.Issues, i => i.SourceKey == "rule.pre" && i.IssueCode == ExecutionValidationIssueCode.ValidationRuleFailed);
        Assert.DoesNotContain(pre.Issues, i => i.SourceKey == "rule.semantic");
        Assert.Contains(semantic.Issues, i => i.SourceKey == "rule.semantic" && i.IssueCode == ExecutionValidationIssueCode.ValidationRuleFailed);
        Assert.DoesNotContain(semantic.Issues, i => i.SourceKey == "rule.pre");
    }

    [Fact]
    public void Missing_rule_observation_produces_not_evaluated_at_correct_boundary()
    {
        var contract = Contract(Rule(FamilyValidationBoundary.Projection, "rule.missing"));
        var result = Pipeline().Validate(Req(contract, Ctx(), FamilyValidationBoundary.Projection));
        var evaluation = Assert.Single(result.Evaluations.Where(e => e.SourceKey == "rule.missing"));

        Assert.Equal(FamilyValidationBoundary.Projection, evaluation.Boundary);
        Assert.Equal(ExecutionRequirementOutcome.NotEvaluated, evaluation.Outcome);
        Assert.Contains(evaluation.Issues, i => i.IssueCode == ExecutionValidationIssueCode.ConditionalRequirementNotEvaluated);
    }

    [Fact]
    public void Failed_supplied_rule_produces_validation_rule_failed_at_correct_boundary()
    {
        var contract = Contract(Rule(FamilyValidationBoundary.ArtifactGeneration, "rule.failed"));
        var result = Pipeline().Validate(Req(contract, Ctx(R(new ExecutionRuleValue("rule.failed", false, "bad", "good", "failed"))), FamilyValidationBoundary.ArtifactGeneration));

        Assert.Contains(result.Issues, i =>
            i.SourceKey == "rule.failed" &&
            i.Boundary == FamilyValidationBoundary.ArtifactGeneration &&
            i.IssueCode == ExecutionValidationIssueCode.ValidationRuleFailed);
    }

    [Fact]
    public void Default_pipeline_factory_registers_boundary_specific_rule_validators()
    {
        var contract = Contract(Enum.GetValues<FamilyValidationBoundary>().Select(b => Rule(b, $"rule.{b}")).ToArray());
        var context = Ctx(R(Enum.GetValues<FamilyValidationBoundary>().Select(b => new ExecutionRuleValue($"rule.{b}", true)).ToArray()));
        var pipeline = ExecutionValidationPipelineFactory.CreateDefault(new FixedClock());

        foreach (var boundary in Enum.GetValues<FamilyValidationBoundary>())
        {
            var result = pipeline.Validate(Req(contract, context, boundary));
            Assert.Contains($"core.validation-rules:{boundary}", result.ValidatorIds);
            Assert.Contains(result.Evaluations, e => e.SourceKey == $"rule.{boundary}" && e.Boundary == boundary && e.Outcome == ExecutionRequirementOutcome.Satisfied);
        }
    }

    [Fact]
    public void Parameterless_rule_validator_defaults_to_post_execution_for_compatibility()
    {
        var validator = new ContractValidationRuleValidator();
        var contract = Contract(Rule(FamilyValidationBoundary.PostExecution, "rule.compat"));
        var evaluation = Assert.Single(validator.Validate(Req(contract, Ctx(R(new ExecutionRuleValue("rule.compat", true))), FamilyValidationBoundary.PostExecution)));

        Assert.Equal(FamilyValidationBoundary.PostExecution, validator.Boundary);
        Assert.Equal("core.validation-rules:PostExecution", validator.ValidatorId);
        Assert.Equal("rule.compat", evaluation.SourceKey);
        Assert.Equal(ExecutionRequirementOutcome.Satisfied, evaluation.Outcome);
    }

    private static ContractValidationRuleValidator[] RuleValidators() => Enum.GetValues<FamilyValidationBoundary>().Select(b => new ContractValidationRuleValidator(b)).ToArray();
    private static ExecutionValidationPipeline Pipeline() => new(RuleValidators(), new FixedClock());
    private static FamilyValidationRequirement Rule(FamilyValidationBoundary boundary, string ruleId) => new($"requirement.{ruleId}", ruleId, boundary, FamilyValidationSeverity.Blocking);
    private static FamilyExecutionContract Contract(params FamilyValidationRequirement[] rules) => new("f", "v", "Family", ValidationRequirements: rules.ToImmutableArray());
    private static FamilyExecutionContext Ctx(ImmutableDictionary<string, ExecutionRuleValue>? rules = null) => new("e", "d", "f", "v", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), ValidationRuleValues: rules);
    private static ImmutableDictionary<string, ExecutionRuleValue> R(params ExecutionRuleValue[] values) => values.ToImmutableDictionary(x => x.RuleId, StringComparer.OrdinalIgnoreCase);
    private static DomainExecutionContract Domain(FamilyExecutionContract contract) => new("d", "dv", "Domain", Families: ImmutableArray.Create(contract));
    private static ExecutionValidationRequest Req(FamilyExecutionContract contract, FamilyExecutionContext context, FamilyValidationBoundary boundary) => new(Domain(contract), contract, context, boundary, StartedUtc: DateTimeOffset.UnixEpoch);
    private sealed class FixedClock : IExecutionClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
