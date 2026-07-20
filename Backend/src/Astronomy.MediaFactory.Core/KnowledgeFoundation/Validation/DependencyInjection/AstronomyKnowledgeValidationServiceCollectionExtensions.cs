using Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;

/// <summary>Service registrations for typed knowledge validation.</summary>
public static class AstronomyKnowledgeValidationServiceCollectionExtensions
{
    public static IServiceCollection AddAstronomyKnowledgeValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCgA2AstronomyKnowledgeFoundation();
        services.TryAddSingleton<IAstronomyKnowledgeValidationRuleRegistry>(provider =>
            new AstronomyKnowledgeValidationRuleRegistry(provider.GetServices<AstronomyKnowledgeValidationRuleDescriptor>()));
        services.TryAddSingleton<IAstronomyTypedKnowledgeValidator, AstronomyTypedKnowledgeValidator>();
        return services;
    }

    public static IServiceCollection AddAstronomyKnowledgeValidationRule<TRule, TPayload>(
        this IServiceCollection services,
        string ruleId,
        AstronomyKnowledgeDomain domain,
        AstronomyKnowledgePayloadFamily family,
        int order = 0)
        where TRule : class, IAstronomyKnowledgeValidationRule
        where TPayload : class, ITypedAstronomyKnowledgePayload
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptor = new AstronomyKnowledgeValidationRuleDescriptor(ruleId, typeof(TRule), typeof(TPayload), domain, family, order);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAstronomyKnowledgeValidationRule, TRule>());
        if (!services.Any(service =>
                service.ServiceType == typeof(AstronomyKnowledgeValidationRuleDescriptor)
                && service.ImplementationInstance is AstronomyKnowledgeValidationRuleDescriptor existing
                && existing == descriptor))
        {
            services.AddSingleton(descriptor);
        }

        return services;
    }
}
