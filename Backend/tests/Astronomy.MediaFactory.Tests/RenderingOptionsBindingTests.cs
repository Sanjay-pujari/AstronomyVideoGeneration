using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class RenderingOptionsBindingTests
{
    [Fact]
    public void RenderingOptions_BindsTransitionSettingsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RenderingOptions.SectionName}:EnableTransitions"] = "true",
                [$"{RenderingOptions.SectionName}:TransitionDurationSeconds"] = "0.5",
                [$"{RenderingOptions.SectionName}:TransitionType"] = "fade"
            })
            .Build();

        var options = new RenderingOptions();
        config.GetSection(RenderingOptions.SectionName).Bind(options);

        Assert.True(options.EnableTransitions);
        Assert.Equal(0.5d, options.TransitionDurationSeconds);
        Assert.Equal("fade", options.TransitionType);
    }
}
