using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

public static class AstronomyKnowledgeQueryExecutionRegistrationExtensions
{
    public static IServiceCollection AddAstronomyKnowledgeQueryExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAstronomyKnowledgeQueryModel();
        services.TryAddSingleton<IAstronomyKnowledgeCatalogQueryEngine, AstronomyKnowledgeCatalogQueryEngine>();
        services.TryAddSingleton<IAstronomyKnowledgeStatementQueryEngine, AstronomyKnowledgeStatementQueryEngine>();
        return services;
    }
}
