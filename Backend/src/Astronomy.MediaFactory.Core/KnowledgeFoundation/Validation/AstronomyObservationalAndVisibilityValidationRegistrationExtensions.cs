using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

public static class AstronomyObservationalAndVisibilityValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyObservationalAndVisibilityValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyObservationalValidation();
        services.AddAstronomyVisibilityValidation();
        return services;
    }
}
