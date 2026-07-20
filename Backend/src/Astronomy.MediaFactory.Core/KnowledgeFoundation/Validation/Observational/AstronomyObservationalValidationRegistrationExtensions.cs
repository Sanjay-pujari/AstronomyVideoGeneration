using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

public static class AstronomyObservationalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyObservationalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomyObservationContextValidationRule, AstronomyObservationConditionsPayload>(AstronomyObservationContextValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyObservationConditionsValidationRule, AstronomyObservationConditionsPayload>(AstronomyObservationConditionsValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyObservationalQuantityValidationRule, AstronomyObservationConditionsPayload>(AstronomyObservationalQuantityValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition, 300);
        services.AddAstronomyKnowledgeValidationRule<AstronomyHorizontalCoordinatesValidationRule, AstronomyObservationConditionsPayload>(AstronomyHorizontalCoordinatesValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition, 400);
        return services;
    }
}
