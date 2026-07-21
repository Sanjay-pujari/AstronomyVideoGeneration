using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyCrossDomainValidatorTests
{
    [Fact]
    public void Validate_RejectsNullArgumentsAndEmptySetPasses()
    {
        var validator = new AstronomyCrossDomainValidator(Array.Empty<IAstronomyCrossDomainValidationRule>());
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, CrossDomainValidationFixture.Context()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(CrossDomainValidationFixture.EmptySet(), null!));
        Assert.True(validator.Validate(CrossDomainValidationFixture.EmptySet(), CrossDomainValidationFixture.Context()).IsValid);
    }

    [Fact]
    public void Validate_IsDeterministicOrderedFiltersAndDoesNotMutateInputs()
    {
        var rules = new IAstronomyCrossDomainValidationRule[] { new OrderedRule("b", 20), new OrderedRule("a", 10), new OrderedRule("a", 10) };
        var validator = new AstronomyCrossDomainValidator(rules);
        var relationships = new[] { CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.EntityIdentity) };
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Position(CrossDomainValidationFixture.Venus));
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, relationships);
        var beforePayloads = set.Payloads.ToArray();
        var beforeRelationships = context.Relationships.ToArray();
        var first = validator.Validate(set, context).Issues.ToArray();
        var second = validator.Validate(set, context).Issues.ToArray();
        Assert.Equal(new[] { "a-1", "a-2", "b-1", "b-2" }, first.Select(i => i.Code));
        Assert.Equal(first.Select(i => i.Code + i.Path), second.Select(i => i.Code + i.Path));
        Assert.Equal(beforePayloads, set.Payloads);
        Assert.Equal(beforeRelationships, context.Relationships);
    }

    [Fact]
    public void Validate_ProductionRulesExecuteAndDuplicateRegistrationDoesNotDuplicateIssues()
    {
        var rule = new AstronomyEntityConsistencyValidationRule();
        var validator = new AstronomyCrossDomainValidator(new IAstronomyCrossDomainValidationRule[] { rule, rule });
        var result = validator.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Position(CrossDomainValidationFixture.Venus)), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.EntityIdentity)));
        var issue = Assert.Single(result.Issues);
        CrossDomainValidationFixture.AssertExactIssue(issue, AstronomyCrossDomainValidationCodes.EntityReferenceMismatch, "$.payloads[1]", AstronomyEntityConsistencyValidationRule.Id, AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition);
    }

    [Fact]
    public void Validate_RuleExceptionsAreNotSwallowed()
    {
        var validator = new AstronomyCrossDomainValidator(new[] { new ThrowingRule() });
        Assert.Throws<InvalidOperationException>(() => validator.Validate(CrossDomainValidationFixture.EmptySet(), CrossDomainValidationFixture.Context()).Issues.ToArray());
    }

    [Fact]
    public void Validate_AppliesMinimumSeverityFiltering()
    {
        var validator = new AstronomyCrossDomainValidator(new[] { new WarningRule() });
        Assert.Empty(validator.Validate(CrossDomainValidationFixture.EmptySet(), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Error)).Issues);
    }

    private sealed class OrderedRule(string id, int order) : IAstronomyCrossDomainValidationRule
    {
        public string RuleId => id;
        public int Order => order;
        public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationSet set, AstronomyCrossDomainValidationContext context)
        {
            yield return new AstronomyKnowledgeValidationIssue($"{id}-1", AstronomyKnowledgeValidationSeverity.Error, "one", "$.a", RuleId, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
            yield return new AstronomyKnowledgeValidationIssue($"{id}-2", AstronomyKnowledgeValidationSeverity.Error, "two", "$.b", RuleId, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
        }
    }

    private sealed class ThrowingRule : IAstronomyCrossDomainValidationRule
    {
        public string RuleId => "throwing";
        public int Order => 1;
        public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationSet set, AstronomyCrossDomainValidationContext context) => throw new InvalidOperationException("boom");
    }

    private sealed class WarningRule : IAstronomyCrossDomainValidationRule
    {
        public string RuleId => "cross-domain.test.warning";
        public int Order => 1;
        public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationSet set, AstronomyCrossDomainValidationContext context)
        { yield return new AstronomyKnowledgeValidationIssue("cross-domain.entity.reference-missing", AstronomyKnowledgeValidationSeverity.Warning, "missing", "$", RuleId, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification); }
    }
}
