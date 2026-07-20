using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

public static class AstronomyClassificationAndPhysicalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyClassificationAndPhysicalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyClassificationValidation();
        services.AddAstronomyPhysicalValidation();
        return services;
    }
}
