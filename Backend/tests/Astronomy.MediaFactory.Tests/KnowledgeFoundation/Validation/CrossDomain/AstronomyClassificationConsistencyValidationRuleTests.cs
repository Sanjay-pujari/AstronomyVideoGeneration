using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
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
        CrossDomainValidationFixture.AssertExactIssue(issues[0], AstronomyCrossDomainValidationCodes.ClassificationSubjectMismatch, "$.payloads[0]", AstronomyClassificationConsistencyValidationRule.Id, AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
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
        var set = CrossDomainValidationFixture.Set(CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Classification(CrossDomainValidationFixture.Mars), CrossDomainValidationFixture.Orbital(CrossDomainValidationFixture.Venus));
        var context = CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Information, AstronomyKnowledgeValidationMode.Standard, CrossDomainValidationFixture.Relationship(0, 1, AstronomyCrossDomainRelationshipKind.ClassificationSubject), CrossDomainValidationFixture.Relationship(2, 3, AstronomyCrossDomainRelationshipKind.ClassificationSubject));
        var beforePayloads = set.Payloads.ToArray();
        var beforeRelationships = context.Relationships.ToArray();
        var first = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        var second = rule.Validate(set, context).Select(i => i.Code + i.Path).ToArray();
        Assert.Equal(first, second);
        Assert.Single(first);
        Assert.EndsWith("$.payloads[2]", first[0]);
        Assert.DoesNotContain("payloads[1]", first[0]);
        Assert.Equal(beforePayloads, set.Payloads);
        Assert.Equal(beforeRelationships, context.Relationships);
    }
}
