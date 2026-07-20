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
        var result = validator.Validate(CrossDomainValidationFixture.EmptySet(), CrossDomainValidationFixture.Context());
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_AppliesMinimumSeverityFiltering()
    {
        var validator = new AstronomyCrossDomainValidator(new[] { new WarningRule() });
        Assert.Empty(validator.Validate(CrossDomainValidationFixture.EmptySet(), CrossDomainValidationFixture.Context(AstronomyKnowledgeValidationSeverity.Error)).Issues);
    }

    private sealed class WarningRule : IAstronomyCrossDomainValidationRule
    {
        public string RuleId => "cross-domain.test.warning";
        public int Order => 1;
        public IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyCrossDomainValidationSet set, AstronomyCrossDomainValidationContext context)
        { yield return new AstronomyKnowledgeValidationIssue("cross-domain.entity.reference-missing", AstronomyKnowledgeValidationSeverity.Warning, "missing", "$", RuleId, Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Classification, Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgePayloadFamily.EntityClassification); }
    }
}
