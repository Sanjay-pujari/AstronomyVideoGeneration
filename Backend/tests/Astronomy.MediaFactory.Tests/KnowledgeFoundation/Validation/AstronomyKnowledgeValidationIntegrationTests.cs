using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation;

public sealed class AstronomyKnowledgeValidationIntegrationTests
{
    [Fact]
    public void ServiceRegistration_IsIdempotentAndExecutesRegisteredRule()
    {
        var services = new ServiceCollection();
        services.AddAstronomyKnowledgeValidation().AddAstronomyKnowledgeValidation().AddAstronomyKnowledgeValidationRule<AlwaysWarningRule, TestPayload>("test.always-warning", AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 10);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAstronomyTypedKnowledgeValidator>());
        var registry = provider.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>();
        Assert.Single(registry.Descriptors);
    }

    [Fact]
    public void PayloadExtension_DelegatesToValidator()
    {
        var registry = new AstronomyKnowledgeValidationRuleRegistry(Array.Empty<AstronomyKnowledgeValidationRuleDescriptor>());
        var result = Fixtures.Payload().Validate(new AstronomyTypedKnowledgeValidator(Fixtures.PayloadRegistry(), registry, Array.Empty<IAstronomyKnowledgeValidationRule>()), Fixtures.Context());
        Assert.True(result.IsValid);
    }
}
