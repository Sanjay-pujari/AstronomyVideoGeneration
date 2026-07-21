using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyCrossDomainValidationIntegrationTests
{
    [Fact]
    public void Registration_IsIdempotentAndRegistersEveryRuleOnceWithMatchingMetadata()
    {
        var services = new ServiceCollection().AddAstronomyCrossDomainValidation().AddAstronomyCrossDomainValidation();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>();
        var rules = provider.GetServices<IAstronomyCrossDomainValidationRule>().OrderBy(r => r.Order).ThenBy(r => r.RuleId, StringComparer.Ordinal).ToArray();
        Assert.Equal(9, rules.Length);
        Assert.Equal(9, registry.Descriptors.Count);
        Assert.Equal(rules.Length, rules.Select(r => r.RuleId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rules.Select(r => r.RuleId), registry.Descriptors.Select(d => d.RuleId));
        foreach (var rule in rules)
        {
            var descriptor = Assert.Single(registry.Descriptors.Where(d => d.RuleId == rule.RuleId));
            Assert.Equal(rule.GetType(), descriptor.RuleType);
            Assert.Equal(rule.Order, descriptor.Order);
        }
    }

    [Fact]
    public void Validator_ExecutesProductionRulesInRuleOrderAndKeepsSinglePayloadValidationIndependent()
    {
        var services = new ServiceCollection().AddAstronomyCrossDomainValidation();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IAstronomyCrossDomainValidator>();
        var set = CrossDomainValidationFixture.Set(
            CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars),
            CrossDomainValidationFixture.Position(CrossDomainValidationFixture.Venus),
            CrossDomainValidationFixture.Observation("other"),
            CrossDomainValidationFixture.Visibility(),
            CrossDomainValidationFixture.Event(CrossDomainValidationFixture.Mars, CrossDomainValidationFixture.T1.AddHours(2)),
            CrossDomainValidationFixture.Temporal(CrossDomainValidationFixture.T0, CrossDomainValidationFixture.T1));
        var result = validator.Validate(set, CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard,
            CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.EntityIdentity),
            CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.OrbitalPositional),
            CrossDomainValidationFixture.Relationship(2, 3, AstronomyCrossDomainRelationshipKind.ObservationVisibilityWindow),
            CrossDomainValidationFixture.Relationship(4, 5, AstronomyCrossDomainRelationshipKind.EventTemporalApplicability)));
        Assert.False(result.IsValid);
        Assert.Equal(result.Issues.Select(i => i.RuleId), result.Issues.Select(i => i.RuleId).OrderBy(id => provider.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>().Descriptors.Single(d => d.RuleId == id).Order));
        Assert.Contains(result.Issues, i => i.Path == "$.payloads[1]" && i.Code == AstronomyCrossDomainValidationCodes.EntityReferenceMismatch);

        var valid = validator.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Position(CrossDomainValidationFixture.Mars)), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.EntityIdentity), CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.OrbitalPositional)));
        Assert.True(valid.IsValid);

        var singlePayload = validator.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars)), CrossDomainValidationFixture.Context());
        Assert.True(singlePayload.IsValid);
    }
}
