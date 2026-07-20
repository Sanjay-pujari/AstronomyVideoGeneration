using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AstronomyTypedKnowledgePayloadDescriptorServiceCollectionExtensions
{
    public static IServiceCollection AddAstronomyTypedKnowledgePayloadDescriptors(this IServiceCollection services) =>
        services.AddAstronomyTypedKnowledge();
}
