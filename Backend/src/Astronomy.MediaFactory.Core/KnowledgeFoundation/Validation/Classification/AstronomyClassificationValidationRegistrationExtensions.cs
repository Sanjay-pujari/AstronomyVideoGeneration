using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;

public static class AstronomyClassificationValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyClassificationValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeValidation();
        services.AddAstronomyKnowledgeValidationRule<AstronomyClassificationAssignmentValidationRule, AstronomyEntityClassificationPayload>(AstronomyClassificationAssignmentValidationRule.Id, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 100);
        services.AddAstronomyKnowledgeValidationRule<AstronomyClassificationDuplicateAssignmentValidationRule, AstronomyEntityClassificationPayload>(AstronomyClassificationDuplicateAssignmentValidationRule.Id, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 200);
        services.AddAstronomyKnowledgeValidationRule<AstronomyClassificationPrimaryAssignmentValidationRule, AstronomyEntityClassificationPayload>(AstronomyClassificationPrimaryAssignmentValidationRule.Id, AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification, 300);
        return services;
    }
}
