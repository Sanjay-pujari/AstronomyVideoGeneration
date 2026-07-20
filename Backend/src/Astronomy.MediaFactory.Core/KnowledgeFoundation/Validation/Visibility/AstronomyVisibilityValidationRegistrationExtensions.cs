using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;

public static class AstronomyVisibilityValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyVisibilityValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomyVisibilityContextValidationRule, AstronomyVisibilityWindowsPayload>(AstronomyVisibilityContextValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyVisibilityWindowValidationRule, AstronomyVisibilityWindowsPayload>(AstronomyVisibilityWindowValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyVisibilityAssessmentValidationRule, AstronomyVisibilityWindowsPayload>(AstronomyVisibilityAssessmentValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow, 300);
        services.AddAstronomyKnowledgeValidationRule<AstronomyVisibilityPeakValidationRule, AstronomyVisibilityWindowsPayload>(AstronomyVisibilityPeakValidationRule.Id, AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow, 400);
        return services;
    }
}
