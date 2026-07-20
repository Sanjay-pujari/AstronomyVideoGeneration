using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

public static class AstronomyOrbitalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyOrbitalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomyOrbitalReferenceContextValidationRule, AstronomyKeplerianElementsPayload>(AstronomyOrbitalReferenceContextValidationRule.Id, AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyKeplerianElementsValidationRule, AstronomyKeplerianElementsPayload>(AstronomyKeplerianElementsValidationRule.Id, AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyOrbitalParametersReferenceContextValidationRule, AstronomyOrbitalParametersPayload>(AstronomyOrbitalParametersReferenceContextValidationRule.Id, AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyOrbitalParametersValidationRule, AstronomyOrbitalParametersPayload>(AstronomyOrbitalParametersValidationRule.Id, AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter, 200);
        return services;
    }
}
