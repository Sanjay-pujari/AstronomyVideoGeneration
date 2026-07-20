using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.CrossDomain;

public sealed class AstronomyCrossDomainValidationIntegrationTests
{
    [Fact]
    public void Registration_IsIdempotentAndRegistersEveryRuleOnce()
    {
        var services = new ServiceCollection().AddAstronomyCrossDomainValidation().AddAstronomyCrossDomainValidation();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>();
        Assert.Equal(9, registry.Descriptors.Count);
        Assert.Equal(registry.Descriptors.Count, registry.Descriptors.Select(d => d.RuleId).Distinct(StringComparer.Ordinal).Count());
        Assert.NotNull(provider.GetRequiredService<IAstronomyCrossDomainValidator>());
    }
}
