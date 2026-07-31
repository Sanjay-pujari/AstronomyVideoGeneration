using System.Reflection;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase4DownstreamAuthorityArchitectureTests
{
    [Fact]
    public void execution_context_carries_the_published_aggregate()
    {
        var property = typeof(ProductionPipelineExecutionContext)
            .GetProperty(nameof(ProductionPipelineExecutionContext.PublishedDocumentaryBlueprintAggregate));

        Assert.NotNull(property);
        Assert.Equal(typeof(DocumentaryBlueprintAggregate), property!.PropertyType);
    }

    [Fact]
    public void pipeline_has_no_legacy_phase4_builder_dependencies()
    {
        var constructors = typeof(ProductionPipelineExecutionService).GetConstructors();
        var dependencies = constructors.SelectMany(x => x.GetParameters()).Select(x => x.ParameterType).ToArray();

        Assert.DoesNotContain(dependencies, x => x.Name is "DocumentaryBlueprintBuilder" or "StoryGraphBuilder");
        Assert.Contains(typeof(IDocumentaryBlueprintPhase4IntegrationService), dependencies);
    }

    [Fact]
    public void publication_service_is_the_phase4_publication_boundary()
    {
        Assert.True(typeof(IPhase4DocumentaryBlueprintPublicationService)
            .IsAssignableFrom(typeof(Phase4DocumentaryBlueprintPublicationService)));
        Assert.DoesNotContain(typeof(ProductionPipelineExecutionService).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            method => method.Name.Contains("DocumentaryBlueprintBuilder", StringComparison.Ordinal));
    }
}
