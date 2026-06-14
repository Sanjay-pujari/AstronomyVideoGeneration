using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class SceneAssetsV3ServiceTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "scene-assets-v3-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_WritesNarrationAlignedShortAndLongSceneV3Packages()
    {
        var service = new SceneAssetsV3Service(
            Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }),
            new DisabledAICinematicImageGenerator(),
            NullLogger<SceneAssetsV3Service>.Instance);

        var result = await service.GenerateAsync(new SceneAssetsV3Request(OverwriteExisting: true), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "visual-timeline-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "visual-timeline-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "scene-manifest-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "scene-manifest-v3.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "short", "003-accurate-sky-guide.png")));
        Assert.True(File.Exists(Path.Combine(result.OutputRoot, "long", "006-accurate-sky-guide.png")));

        var shortManifest = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "short", "scene-manifest-v3.json"));
        Assert.Contains("\"sceneCount\": 5", shortManifest);
        Assert.Contains("AccurateSkyGuideScene", shortManifest);
        Assert.Contains("From Udaipur, look east after 10 PM", shortManifest);

        var longReview = await File.ReadAllTextAsync(Path.Combine(result.OutputRoot, "long", "scene-review-v3.json"));
        Assert.Contains("\"sceneCount\": 9", longReview);
        Assert.Contains("\"accurateSkyGuidePresent\": true", longReview);
        Assert.Contains("\"duplicateHashDetected\": false", longReview);
        Assert.Contains("\"status\": \"Passed\"", longReview);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot)) Directory.Delete(_outputRoot, recursive: true);
    }
}
