using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyClassificationConsistencyValidationRuleTests
{
    [Fact]
    public void Validate_CompatibleRelatedSet_Passes()
    {
        var rule = new AstronomyClassificationConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars)), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ClassificationSubject))).ToArray();
        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_GenuineMismatch_ReportsMetadata()
    {
        var rule = new AstronomyClassificationConsistencyValidationRule();
        var issues = rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Venus)), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ClassificationSubject))).ToArray();
        Assert.NotEmpty(issues);
        Assert.Equal(rule.RuleId, issues[0].RuleId);
        Assert.Equal(AstronomyCrossDomainValidationCodes.ClassificationSubjectMismatch, issues[0].Code);
        Assert.Equal(AstronomyKnowledgeValidationSeverity.Error, issues[0].Severity);
        Assert.NotEqual(default, issues[0].Domain);
        Assert.NotEqual(default, issues[0].Family);
        Assert.Contains("$.payloads[", issues[0].Path);
    }

    [Fact]
    public void Validate_UnrelatedPayloads_AreIgnored()
    {
        var rule = new AstronomyClassificationConsistencyValidationRule();
        Assert.Empty(rule.Validate(CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Venus)), CrossDomainValidationFixture.Context()).ToArray());
    }

    [Fact]
    public void Validate_MultiplePairs_IsDeterministicAndDoesNotMutateInput()
    {
        var rule = new AstronomyClassificationConsistencyValidationRule();
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Venus));
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ClassificationSubject), CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ClassificationSubject));
        var before = set.Payloads.ToArray();
        var first = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        var second = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(before, set.Payloads);
    }
}
