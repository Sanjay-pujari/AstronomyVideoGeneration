using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMediaFactory_ResolvesRelativeYouTubeTokenPathFromMaintenanceWorkingDirectory()
    {
        using var workspace = new TempTokenHealthWorkspace();
        var mediaOutput = Path.Combine(workspace.Root, "media-output");
        Directory.CreateDirectory(mediaOutput);
        var tokenPath = Path.Combine(mediaOutput, "youtube-oauth-token.json");
        File.WriteAllText(tokenPath, "{}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["YouTube:ClientId"] = "client-id",
                ["YouTube:ClientSecret"] = "client-secret",
                ["YouTube:TokenFilePath"] = "youtube-oauth-token.json",
                ["Maintenance:WorkingDirectory"] = mediaOutput,
                ["Rendering:WorkingDirectory"] = mediaOutput
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddMediaFactory(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<YouTubeOptions>>().Value;

        Assert.Equal(tokenPath, options.TokenFilePath);
    }
}
