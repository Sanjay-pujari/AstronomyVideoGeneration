using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
public static class AstronomyKnowledgeFoundationRegistrationExtensions
{ public static IServiceCollection AddAstronomyKnowledgeFoundation(this IServiceCollection services)=>services.AddAstronomyKnowledgeFoundation(null);
 public static IServiceCollection AddAstronomyKnowledgeFoundation(this IServiceCollection services,Action<AstronomyKnowledgeFoundationRegistrationOptions>? configure){ArgumentNullException.ThrowIfNull(services); var options=new AstronomyKnowledgeFoundationRegistrationOptions(); configure?.Invoke(options); services.AddAstronomyTypedKnowledge(); services.AddAstronomyKnowledgeValidation(); services.AddAstronomyCrossDomainValidation(); services.AddAstronomyKnowledgeGraphValidation(); services.AddAstronomyKnowledgeCatalog(); services.AddAstronomyKnowledgeQueryModel(); if(options.IncludeQueryExecution) services.AddAstronomyKnowledgeQueryExecution(); services.TryAddSingleton(_=>AstronomyKnowledgeFoundationCapabilityCatalog.CreateSnapshot()); services.TryAddSingleton<IAstronomyKnowledgeFoundationCapabilities,AstronomyKnowledgeFoundationCapabilities>(); if(options.IncludeCompatibilityVerifier) services.TryAddSingleton<IAstronomyKnowledgeFoundationCompatibilityVerifier,AstronomyKnowledgeFoundationCompatibilityVerifier>(); return services;}}
