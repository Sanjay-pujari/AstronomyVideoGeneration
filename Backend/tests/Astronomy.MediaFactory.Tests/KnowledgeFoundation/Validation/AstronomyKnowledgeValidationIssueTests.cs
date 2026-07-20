using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationIssueTests
{
    [Fact]
    public void Constructor_NormalizesTrimmedValuesAndUsesValueEquality()
    {
        var issue = new AstronomyKnowledgeValidationIssue(" validation.payload.null ", AstronomyKnowledgeValidationSeverity.Warning, " message ", " $.typeId ", " foundation.payload.not-null ", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
        var same = new AstronomyKnowledgeValidationIssue("validation.payload.null", AstronomyKnowledgeValidationSeverity.Warning, "message", "$.typeId", "foundation.payload.not-null", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification);
        Assert.Equal(same, issue);
        Assert.Equal("validation.payload.null", issue.Code);
    }
    [Theory]
    [InlineData("Validation.Payload.Null")]
    [InlineData("validation payload null")]
    [InlineData("validation..payload")]
    [InlineData("validation.payload.")]
    public void Constructor_RejectsMalformedCodes(string code) => Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationIssue(code, AstronomyKnowledgeValidationSeverity.Warning, "message", "$", "foundation.rule", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
    [Fact]
    public void Constructor_RejectsInvalidFields()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationIssue("valid", AstronomyKnowledgeValidationSeverity.Warning, " ", "$", "rule", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeValidationIssue("valid", AstronomyKnowledgeValidationSeverity.Warning, "message", "x", "rule", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeValidationIssue("valid", (AstronomyKnowledgeValidationSeverity)99, "message", "$", "rule", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeValidationIssue("valid", AstronomyKnowledgeValidationSeverity.Warning, "message", "$", "rule", (AstronomyKnowledgeDomain)99, AstronomyKnowledgePayloadFamily.EntityClassification));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeValidationIssue("valid", AstronomyKnowledgeValidationSeverity.Warning, "message", "$", "rule", AstronomyKnowledgeDomain.Classification, (AstronomyKnowledgePayloadFamily)99));
    }
}
