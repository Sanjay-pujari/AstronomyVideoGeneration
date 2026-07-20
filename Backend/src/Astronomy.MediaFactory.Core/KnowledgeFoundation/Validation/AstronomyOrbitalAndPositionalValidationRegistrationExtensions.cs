using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

public static class AstronomyOrbitalAndPositionalValidationRegistrationExtensions
{
    public static IServiceCollection AddAstronomyOrbitalAndPositionalValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyOrbitalValidation();
        services.AddAstronomyPositionalValidation();
        return services;
    }
}
