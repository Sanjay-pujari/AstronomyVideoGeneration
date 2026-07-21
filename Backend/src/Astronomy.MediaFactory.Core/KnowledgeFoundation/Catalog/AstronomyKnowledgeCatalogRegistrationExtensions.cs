using Astronomy.MediaFactory.Core.KnowledgeFoundation.Extensions;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
public static class AstronomyKnowledgeCatalogRegistrationExtensions
{ public static IServiceCollection AddAstronomyKnowledgeCatalog(this IServiceCollection services){ ArgumentNullException.ThrowIfNull(services); services.AddCgA2AstronomyKnowledgeFoundation(); services.AddAstronomyCrossDomainValidation(); services.AddAstronomyKnowledgeGraphValidation(); services.TryAddSingleton<IAstronomyKnowledgeCatalogBuilder,AstronomyKnowledgeCatalogBuilder>(); services.TryAddSingleton<IAstronomyKnowledgeCatalog>(p=>new AstronomyKnowledgeCatalog(p.GetRequiredService<IAstronomyKnowledgeCatalogBuilder>().Build())); return services; } }
