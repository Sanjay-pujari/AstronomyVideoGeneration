using System.Text.Json;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;

public static class CgA2AstronomyKnowledgeFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddCgA2AstronomyKnowledgeFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<AstronomyKnowledgeStatementValidator>();
        services.TryAddSingleton<IAstronomyKnowledgeStatementValidator>(provider => provider.GetRequiredService<AstronomyKnowledgeStatementValidator>());
        services.TryAddSingleton<AstronomyEvidenceRecordValidator>();
        services.TryAddSingleton<IAstronomyEvidenceRecordValidator>(provider => provider.GetRequiredService<AstronomyEvidenceRecordValidator>());
        services.TryAddSingleton<AstronomyKnowledgeStatementEvidenceSetValidator>();
        services.TryAddSingleton<IAstronomyKnowledgeStatementEvidenceSetValidator>(provider => provider.GetRequiredService<AstronomyKnowledgeStatementEvidenceSetValidator>());
        services.TryAddSingleton<AstronomyKnowledgeConfidenceAssessmentValidator>();
        services.TryAddSingleton<IAstronomyKnowledgeConfidenceAssessmentValidator>(provider => provider.GetRequiredService<AstronomyKnowledgeConfidenceAssessmentValidator>());
        services.TryAddSingleton<AstronomyEvidenceConfidenceConsistencyValidator>();
        services.TryAddSingleton<IAstronomyEvidenceConfidenceConsistencyValidator>(provider => provider.GetRequiredService<AstronomyEvidenceConfidenceConsistencyValidator>());
        services.TryAddSingleton(_ => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyEvidenceAndConfidenceJson());
        return services;
    }
}
