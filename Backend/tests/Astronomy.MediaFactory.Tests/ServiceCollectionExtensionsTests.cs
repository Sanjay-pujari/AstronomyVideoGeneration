using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMediaFactory_ResolvesRelativeYouTubeTokenPathFromServiceExecutableDirectory()
    {
        using var workspace = new TempTokenHealthWorkspace();
        var mediaOutput = Path.Combine(workspace.Root, "media-output");
        Directory.CreateDirectory(mediaOutput);
        var executableTokenFileName = $"youtube-oauth-token-{Guid.NewGuid():N}.json";
        var mediaOutputTokenPath = Path.Combine(mediaOutput, executableTokenFileName);
        File.WriteAllText(mediaOutputTokenPath, "{}");
        var executableTokenPath = Path.Combine(AppContext.BaseDirectory, executableTokenFileName);
        File.WriteAllText(executableTokenPath, "{}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["YouTube:ClientId"] = "client-id",
                    ["YouTube:ClientSecret"] = "client-secret",
                    ["YouTube:TokenFilePath"] = executableTokenFileName,
                    ["Maintenance:WorkingDirectory"] = mediaOutput,
                    ["Rendering:WorkingDirectory"] = mediaOutput
                })
                .Build();

            using var provider = new ServiceCollection()
                .AddMediaFactory(configuration)
                .BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<YouTubeOptions>>().Value;

            Assert.Equal(executableTokenPath, options.TokenFilePath);
        }
        finally
        {
            File.Delete(executableTokenPath);
        }
    }

    [Fact]
    public void AddMediaFactory_FallsBackToServiceExecutableDirectoryForMissingRelativeYouTubeTokenPath()
    {
        using var workspace = new TempTokenHealthWorkspace();
        var mediaOutput = Path.Combine(workspace.Root, "media-output");
        Directory.CreateDirectory(mediaOutput);
        var executableTokenFileName = $"missing-youtube-oauth-token-{Guid.NewGuid():N}.json";
        var executableTokenPath = Path.Combine(AppContext.BaseDirectory, executableTokenFileName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["YouTube:ClientId"] = "client-id",
                ["YouTube:ClientSecret"] = "client-secret",
                ["YouTube:TokenFilePath"] = executableTokenFileName,
                ["Maintenance:WorkingDirectory"] = mediaOutput,
                ["Rendering:WorkingDirectory"] = mediaOutput
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddMediaFactory(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<YouTubeOptions>>().Value;

        Assert.Equal(executableTokenPath, options.TokenFilePath);
    }

    [Fact]
    public void YouTubeTokenResolver_ResolvesRelativeTokenPathFromServiceExecutableDirectory()
    {
        var tokenFileName = $"youtube-oauth-token-{Guid.NewGuid():N}.json";
        var options = new YouTubeOptions { TokenFilePath = tokenFileName };

        var tokenPath = YouTubeTokenResolver.ResolveTokenFilePath(options);

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, tokenFileName), tokenPath);
    }
}
