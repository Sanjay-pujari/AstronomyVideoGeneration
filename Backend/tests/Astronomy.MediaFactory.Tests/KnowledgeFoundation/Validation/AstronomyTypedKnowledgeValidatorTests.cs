using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyTypedKnowledgeValidatorTests
{
    [Fact]
    public void Validate_ExecutesMatchingRulesDeterministically()
    {
        var registry = new AstronomyKnowledgeValidationRuleRegistry(new[] { new AstronomyKnowledgeValidationRuleDescriptor("test.always-error", typeof(AlwaysErrorRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 20), new AstronomyKnowledgeValidationRuleDescriptor("test.always-warning", typeof(AlwaysWarningRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 10) });
        var validator = new AstronomyTypedKnowledgeValidator(Fixtures.PayloadRegistry(), registry, new IAstronomyKnowledgeValidationRule[] { new AlwaysErrorRule(), new AlwaysWarningRule() });
        var result = validator.Validate(Fixtures.Payload(), Fixtures.Context());
        Assert.Equal(new[] { AstronomyKnowledgeValidationSeverity.Error, AstronomyKnowledgeValidationSeverity.Warning }, result.Issues.Select(i => i.Severity));
    }
    [Fact]
    public void Validate_ReturnsCriticalIssueForUnregisteredPayload()
    {
        var emptyRules = new AstronomyKnowledgeValidationRuleRegistry(Array.Empty<AstronomyKnowledgeValidationRuleDescriptor>());
        var registry = new AstronomyTypedPayloadRegistry(new[] { new AstronomyTypedPayloadDescriptor("typed.test.payload.v1", typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification) });
        var result = new AstronomyTypedKnowledgeValidator(registry, emptyRules, Array.Empty<IAstronomyKnowledgeValidationRule>()).Validate(new DerivedTestPayload(new AstronomyKnowledgeTypeId("typed.derived.payload.v1")), Fixtures.Context());
        Assert.Single(result.Issues); Assert.Equal(AstronomyKnowledgeValidationCodes.PayloadUnregistered, result.Issues[0].Code); Assert.Equal(AstronomyKnowledgeValidationSeverity.Critical, result.Issues[0].Severity);
    }
    [Fact]
    public void Validate_AppliesMinimumSeverityFiltering()
    {
        var registry = new AstronomyKnowledgeValidationRuleRegistry(new[] { new AstronomyKnowledgeValidationRuleDescriptor("test.always-warning", typeof(AlwaysWarningRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 10) });
        var validator = new AstronomyTypedKnowledgeValidator(Fixtures.PayloadRegistry(), registry, new[] { new AlwaysWarningRule() });
        Assert.Empty(validator.Validate(Fixtures.Payload(), Fixtures.Context(AstronomyKnowledgeValidationSeverity.Error)).Issues);
    }
    [Fact]
    public void Validate_StopsOnDescriptorMismatch()
    {
        var registry = new AstronomyKnowledgeValidationRuleRegistry(new[] { new AstronomyKnowledgeValidationRuleDescriptor("test.always-warning", typeof(AlwaysWarningRule), typeof(TestPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification) });
        var result = new AstronomyTypedKnowledgeValidator(Fixtures.PayloadRegistry(), registry, new[] { new AlwaysWarningRule() }).Validate(Fixtures.Payload("wrong.type.v1"), Fixtures.Context());
        Assert.Single(result.Issues); Assert.Equal(AstronomyKnowledgeValidationCodes.PayloadTypeIdMismatch, result.Issues[0].Code);
    }
}
