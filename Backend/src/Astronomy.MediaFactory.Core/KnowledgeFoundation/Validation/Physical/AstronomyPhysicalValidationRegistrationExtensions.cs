using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;

public static class AstronomyPhysicalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyPhysicalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomyPhysicalPropertyIdentityValidationRule, AstronomyPhysicalPropertiesPayload>(AstronomyPhysicalPropertyIdentityValidationRule.Id, AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyPhysicalPropertyValueValidationRule, AstronomyPhysicalPropertiesPayload>(AstronomyPhysicalPropertyValueValidationRule.Id, AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyPhysicalRangeValidationRule, AstronomyPhysicalPropertiesPayload>(AstronomyPhysicalRangeValidationRule.Id, AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty, 300);
        return services;
    }
}
