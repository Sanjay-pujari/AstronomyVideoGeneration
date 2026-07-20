using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public static class AstronomyTypedKnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddAstronomyTypedKnowledge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAstronomyTypedPayloadRegistry>(_ => new AstronomyTypedPayloadRegistry(AstronomyBuiltInTypedPayloadDescriptors.BuiltIn));
        services.TryAddSingleton(provider => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyTypedKnowledgeJson(provider.GetRequiredService<IAstronomyTypedPayloadRegistry>()));
        return services;
    }
}
