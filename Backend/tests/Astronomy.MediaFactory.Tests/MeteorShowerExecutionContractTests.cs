using Astronomy.MediaFactory.Core.ExecutionContracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class MeteorShowerExecutionContractTests
{
    [Fact]
    public void Catalog_registers_meteor_shower_in_astronomy()
    {
        var domain = AstronomyExecutionContractCatalog.Create();
        var family = Assert.Single(domain.Families);

        Assert.Equal("Astronomy", domain.DomainId);
        Assert.Equal("AstronomyExecutionContracts-v1", domain.ContractVersion);
        Assert.Equal("2C", domain.Metadata["frameworkMilestone"]);
        Assert.Equal("shadow", domain.Metadata["validationMode"]);
        Assert.Equal("production", domain.Metadata["runtimeAuthority"]);
        Assert.Equal(MeteorShowerExecutionKeys.FamilyId, family.FamilyId);
    }

    [Fact]
    public void Registry_resolves_canonical_id_and_aliases()
    {
        var registry = new ExecutionContractRegistry([AstronomyExecutionContractCatalog.Create()]);

        var canonical = registry.ResolveFamily(MeteorShowerExecutionKeys.FamilyId, "Astronomy");
        var meteor = registry.ResolveFamily("Meteor", "Astronomy");
        var snake = registry.ResolveFamily("METEOR_SHOWER", "Astronomy");
        var spaced = registry.ResolveFamily("meteor shower", "Astronomy");

        Assert.Equal(FamilyContractResolutionStatus.Resolved, canonical.Status);
        Assert.Equal(FamilyContractMatchKind.CanonicalFamilyId, canonical.MatchedBy);
        Assert.Equal(MeteorShowerExecutionKeys.FamilyId, meteor.ResolvedFamilyId);
        Assert.Equal(MeteorShowerExecutionKeys.FamilyId, snake.ResolvedFamilyId);
        Assert.Equal(MeteorShowerExecutionKeys.FamilyId, spaced.ResolvedFamilyId);
        Assert.All(new[] { meteor, snake, spaced }, r => Assert.Equal(FamilyContractMatchKind.Alias, r.MatchedBy));
    }

    [Fact]
    public void Contract_is_immutable_and_deterministic()
    {
        var first = MeteorShowerExecutionContractFactory.Create();
        var second = MeteorShowerExecutionContractFactory.Create();

        Assert.Equal(first, second);
        Assert.True(first.InputRequirements.GetType().IsValueType);
        Assert.True(first.Metadata.GetType().Name.Contains("Immutable", StringComparison.Ordinal));
        Assert.Equal(first.InputRequirements.Select(r => r.RequirementId), second.InputRequirements.Select(r => r.RequirementId));
        Assert.Equal(first.ValidationRequirements.Select(r => r.RuleId), second.ValidationRequirements.Select(r => r.RuleId));
    }

    [Fact]
    public void Requirement_validation_rule_and_projection_ids_are_unique()
    {
        var contract = MeteorShowerExecutionContractFactory.Create();
        var requirementIds = contract.InputRequirements.Select(r => r.RequirementId)
            .Concat(contract.SemanticRequirements.Select(r => r.RequirementId))
            .Concat(contract.ProjectionRequirements.Select(r => r.RequirementId))
            .Concat(contract.ArtifactRequirements.Select(r => r.RequirementId))
            .Concat(contract.ValidationRequirements.Select(r => r.RequirementId))
            .ToArray();

        Assert.Equal(requirementIds.Length, requirementIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(contract.ValidationRequirements.Length, contract.ValidationRequirements.Select(r => r.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(contract.ProjectionRequirements.Length, contract.ProjectionRequirements.Select(r => r.TargetFactType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Stable_keys_exist_and_are_used_by_contract()
    {
        var contract = MeteorShowerExecutionContractFactory.Create();

        Assert.Contains(contract.InputRequirements, r => r.InputKey == MeteorShowerExecutionKeys.Inputs.EventIdentity);
        Assert.Contains(contract.InputRequirements, r => r.InputKey == MeteorShowerExecutionKeys.Inputs.ContentStrategy && r.Level == FamilyRequirementLevel.Optional);
        Assert.Contains(contract.SemanticRequirements, r => r.CapabilityId == MeteorShowerExecutionKeys.Semantic.MeteorActivity);
        Assert.Contains(contract.ProjectionRequirements, r => r.ProjectionRuleId == MeteorShowerExecutionKeys.Projection.RadiantRule);
        Assert.Contains(contract.ValidationRequirements, r => r.RuleId == MeteorShowerExecutionKeys.Rules.RequiredFactsRetained);
    }

    [Fact]
    public void Contract_contains_no_executable_delegates_or_runtime_dependencies()
    {
        var contract = MeteorShowerExecutionContractFactory.Create();
        var values = contract.GetType().GetProperties().Select(p => p.GetValue(contract)).Where(v => v is not null).ToArray();

        Assert.DoesNotContain(values, v => v is Delegate);
        Assert.Empty(contract.ArtifactRequirements);
        Assert.DoesNotContain(contract.Metadata.Keys, k => k.Contains("service", StringComparison.OrdinalIgnoreCase) || k.Contains("adapter", StringComparison.OrdinalIgnoreCase));
    }
}
