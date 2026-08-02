using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelinePhase6RoutingTests
{
    [Fact]
    public void ServiceCollection_RegistersExactlyOnePhase6InputAuthorityEvaluator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=phase6-routing-test.invalid;Port=5432;Database=astronomy_mediafactory_test;Username=test_user;Password=test_password;Pooling=false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMediaFactory(configuration);
        Assert.Single(services.Where(x => x.ServiceType == typeof(IPhase6InputAuthorityEvaluator)));
    }

    [Fact]
    public void ProductionPipelineConstruction_RequiresPhase6InputAuthorityEvaluator()
    {
        var parameter = typeof(ProductionPipelineExecutionService).GetConstructors().Single().GetParameters()
            .Single(value => value.ParameterType == typeof(IPhase6InputAuthorityEvaluator));

        Assert.False(parameter.HasDefaultValue);
        Assert.False(parameter.IsOptional);
    }
}
