using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
using Microsoft.Extensions.DependencyInjection;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationAndPhysicalValidationIntegrationTests
{
    [Fact] public void RegistrationIsIdempotentAndRegistersExpectedRulesOnce()
    {
        var services = new ServiceCollection().AddAstronomyClassificationAndPhysicalValidation().AddAstronomyClassificationAndPhysicalValidation();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>();
        Assert.Equal(3, registry.Descriptors.Count(d => d.Domain == Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Classification));
        Assert.Equal(3, registry.Descriptors.Count(d => d.Domain == Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Physical));
        Assert.Equal(registry.Descriptors.Select(d => d.RuleId).Distinct(StringComparer.Ordinal).Count(), registry.Descriptors.Count);
    }
    [Fact] public void RealPayloadsValidateThroughTypedValidatorAndSeverityFilteringWorks()
    {
        using var provider = new ServiceCollection().AddAstronomyClassificationAndPhysicalValidation().BuildServiceProvider();
        var validator = provider.GetRequiredService<IAstronomyTypedKnowledgeValidator>();
        Assert.Empty(validator.Validate(ValidationFixture.Classification(), ValidationFixture.Context()).Issues);
        Assert.Empty(validator.Validate(ValidationFixture.Physical(ValidationFixture.Scalar()), ValidationFixture.Context()).Issues);
        var warningPayload = ValidationFixture.Classification(ValidationFixture.Assignment(qualifier: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification.AstronomyClassificationQualifier.Secondary));
        Assert.Empty(validator.Validate(warningPayload, ValidationFixture.Context(minimum: AstronomyKnowledgeValidationSeverity.Error)).Issues);
    }
    [Fact] public void FinalIssueOrderingIsResultOwned()
    {
        var result = new AstronomyKnowledgeValidationResult(new[] {
            new AstronomyKnowledgeValidationIssue("z.issue", AstronomyKnowledgeValidationSeverity.Warning, "b", "$.b", "classification.primary.cardinality", Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Classification, Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgePayloadFamily.EntityClassification),
            new AstronomyKnowledgeValidationIssue("a.issue", AstronomyKnowledgeValidationSeverity.Error, "a", "$.a", "physical.property.identity", Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Physical, Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgePayloadFamily.PhysicalProperty)});
        Assert.Equal(new[] { "a.issue", "z.issue" }, result.Issues.Select(i => i.Code));
    }
}
