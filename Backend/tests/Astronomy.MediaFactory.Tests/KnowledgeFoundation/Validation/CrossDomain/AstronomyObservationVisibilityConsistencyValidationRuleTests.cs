using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyObservationVisibilityConsistencyValidationRuleTests
{
    [Fact]
    public void Validate_CompatibleRelatedSet_Passes()
    {
        var rule = new AstronomyObservationVisibilityConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility()), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ObservationVisibilityWindow))).ToArray();
        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_GenuineMismatch_ReportsMetadata()
    {
        var rule = new AstronomyObservationVisibilityConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation("other"), CrossDomainValidationFixture.Visibility()), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ObservationVisibilityWindow))).ToArray();
        Assert.NotEmpty(issues);
        CrossDomainValidationFixture.AssertExactIssue(issues[0], AstronomyCrossDomainValidationCodes.ObservationVisibilityContextMismatch, "$.payloads[0].observationContext", AstronomyObservationVisibilityConsistencyValidationRule.Id, AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition);
    }

    [Fact]
    public void Validate_UnrelatedPayloads_AreIgnored()
    {
        var rule = new AstronomyObservationVisibilityConsistencyValidationRule();
        Assert.Empty(rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation("other"), CrossDomainValidationFixture.Visibility()), CrossDomainValidationFixture.Context()).ToArray());
    }

    [Fact]
    public void Validate_MultiplePairs_IsDeterministicAndDoesNotMutateInput()
    {
        var rule = new AstronomyObservationVisibilityConsistencyValidationRule();
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(), CrossDomainValidationFixture.Observation("other"), CrossDomainValidationFixture.Visibility());
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ObservationVisibilityWindow), CrossDomainValidationFixture.Relationship(2, 3, AstronomyCrossDomainRelationshipKind.ObservationVisibilityWindow));
        var beforePayloads = set.Payloads.ToArray();
        var beforeRelationships = context.Relationships.ToArray();
        var first = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        var second = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        Assert.Equal(first, second);
        Assert.Single(first);
        Assert.EndsWith("$.payloads[2].observationContext", first[0]);
        Assert.DoesNotContain("payloads[1]", first[0]);
        Assert.Equal(beforePayloads, set.Payloads);
        Assert.Equal(beforeRelationships, context.Relationships);
    }
}
