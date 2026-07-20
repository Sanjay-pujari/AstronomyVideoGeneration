using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public static class AstronomyPositionalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyPositionalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomySpatialReferenceContextValidationRule, AstronomySpatialPositionPayload>(AstronomySpatialReferenceContextValidationRule.Id, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyPositionValueValidationRule, AstronomySpatialPositionPayload>(AstronomyPositionValueValidationRule.Id, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyAngularPositionValidationRule, AstronomySpatialPositionPayload>(AstronomyAngularPositionValidationRule.Id, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition, 300);
        services.AddAstronomyKnowledgeValidationRule<AstronomySphericalPositionValidationRule, AstronomySpatialPositionPayload>(AstronomySphericalPositionValidationRule.Id, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition, 400);
        services.AddAstronomyKnowledgeValidationRule<AstronomyCartesianPositionValidationRule, AstronomySpatialPositionPayload>(AstronomyCartesianPositionValidationRule.Id, AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition, 500);
        return services;
    }

}
