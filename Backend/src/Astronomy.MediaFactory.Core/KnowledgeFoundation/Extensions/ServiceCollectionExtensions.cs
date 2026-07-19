using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;

public static class CgA2AstronomyKnowledgeFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddCgA2AstronomyKnowledgeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAstronomyKnowledgeStatementValidator, AstronomyKnowledgeStatementValidator>();
        services.TryAddSingleton<AstronomyKnowledgeStatementValidator>();
        return services;
    }
}
