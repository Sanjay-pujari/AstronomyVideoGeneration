using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Temporal;
using Microsoft.Extensions.DependencyInjection;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
public static class AstronomyEventAndTemporalValidationRegistrationExtensions
{
 public static IServiceCollection AddAstronomyEventAndTemporalValidation(this IServiceCollection services){ArgumentNullException.ThrowIfNull(services); services.AddAstronomyEventValidation(); services.AddAstronomyTemporalValidation(); return services;}
}
