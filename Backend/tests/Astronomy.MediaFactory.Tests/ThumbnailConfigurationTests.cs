using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailConfigurationTests
{
    [Fact]
    public void AddMediaFactory_UsesLocalAssetCollage_WhenDeprecatedThumbnailAiSectionsExist()
    {
        var values = new Dictionary<string, string?>
        {
            ["ThumbnailGeneration:Mode"] = "LocalAssetCollage",
            ["ThumbnailAIOptimization:MaxHookWords"] = "5",
            ["ThumbnailCinematicAI:MaximumObjectScaleBoost"] = "1.2",
            ["Observation:Latitude"] = "0",
            ["Observation:Longitude"] = "0",
            ["Observation:Timezone"] = "UTC",
            ["Observation:LocationName"] = "Test"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMediaFactory(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LocalAssetCollageThumbnailService>(provider.GetRequiredService<IThumbnailGenerationService>());
    }
}
