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
        Assert.Equal(rule.RuleId, issues[0].RuleId);
        Assert.Equal(AstronomyCrossDomainValidationCodes.ReferenceFrameMismatch, issues[0].Code);
        Assert.Equal(AstronomyKnowledgeValidationSeverity.Error, issues[0].Severity);
        Assert.NotEqual(default, issues[0].Domain);
        Assert.NotEqual(default, issues[0].Family);
        Assert.Contains("$.payloads[", issues[0].Path);
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
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Observation(), CrossDomainValidationFixture.Visibility(f: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates.AstronomyReferenceFrame.FK5));
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ReferenceContext), CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ReferenceContext));
        var before = set.Payloads.ToArray();
        var first = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        var second = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(before, set.Payloads);
    }
}
