using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyReferenceContextConsistencyValidationRuleTests
{
    [Fact]
    public void Validate_CompatibleRelatedSet_Passes()
    {
        var rule = new AstronomyReferenceContextConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility()), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ReferenceContext))).ToArray();
        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_GenuineMismatch_ReportsMetadata()
    {
        var rule = new AstronomyReferenceContextConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(f: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates.AstronomyReferenceFrame.FK5)), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ReferenceContext))).ToArray();
        Assert.NotEmpty(issues);
        CrossDomainValidationFixture.AssertExactIssue(issues[0], AstronomyCrossDomainValidationCodes.ReferenceFrameMismatch, "$.payloads[1]", AstronomyReferenceContextConsistencyValidationRule.Id, AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow);
    }

    [Fact]
    public void Validate_UnrelatedPayloads_AreIgnored()
    {
        var rule = new AstronomyReferenceContextConsistencyValidationRule();
        Assert.Empty(rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(f: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates.AstronomyReferenceFrame.FK5)), CrossDomainValidationFixture.Context()).ToArray());
    }

    [Fact]
    public void Validate_MultiplePairs_IsDeterministicAndDoesNotMutateInput()
    {
        var rule = new AstronomyReferenceContextConsistencyValidationRule();
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(), CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(f: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates.AstronomyReferenceFrame.FK5));
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ReferenceContext), CrossDomainValidationFixture.Relationship(2, 3, AstronomyCrossDomainRelationshipKind.ReferenceContext));
        var beforePayloads = set.Payloads.ToArray();
        var beforeRelationships = context.Relationships.ToArray();
        var first = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        var second = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        Assert.Equal(first, second);
        Assert.Single(first);
        Assert.EndsWith("$.payloads[3]", first[0]);
        Assert.DoesNotContain("payloads[1]", first[0]);
        Assert.Equal(beforePayloads, set.Payloads);
        Assert.Equal(beforeRelationships, context.Relationships);
    }
}
