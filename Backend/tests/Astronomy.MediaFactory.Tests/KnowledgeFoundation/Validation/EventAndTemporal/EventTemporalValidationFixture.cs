using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.EventAndTemporal;

internal static class EventTemporalValidationFixture
{
    public static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddAstronomyTypedKnowledgePayloadDescriptors();
        services.AddAstronomyEventAndTemporalValidation().AddAstronomyEventAndTemporalValidation();
        return services.BuildServiceProvider();
    }
}
