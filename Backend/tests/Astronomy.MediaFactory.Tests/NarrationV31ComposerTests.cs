using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationV31ComposerTests
{
    [Fact]
    public async Task PreviewAsync_AllowsDuplicatePurposesAcrossShortAndLongWhenKeyedByFormatAndSceneId()
    {
        var composer = new NarrationV31Composer();

        var response = await composer.PreviewAsync(new NarrationV31PreviewRequest(
            EventId: "event-1",
            RegionId: "us",
            Language: "en",
            DryRun: true,
            OverwriteExisting: true,
            ProductionContext: null,
            OutputRoot: null,
            EventType: "meteor shower",
            Title: "Geminids",
            LocalPeakTime: "10 PM",
            SkyDirectionHint: "eastern sky",
            BestViewingWindowLocal: "after 10 PM"), CancellationToken.None);

        Assert.True(response.IsValid, string.Join(" | ", response.Quality.Errors));
        Assert.True(response.Quality.IsValid, string.Join(" | ", response.Quality.Errors));
        Assert.Contains(response.ShortNarration.Scenes, s => s.ScenePurpose == "cause");
        Assert.Contains(response.LongNarration.Scenes, s => s.ScenePurpose == "cause");
        Assert.Equal(new[]
        {
            "short:001-hook",
            "short:002-cause",
            "short:003-accurate-sky-guide",
            "short:004-viewing-tip",
            "short:005-final-reminder"
        }, response.ShortNarration.Diagnostics!.V31NarrationKeysUsed);
        Assert.Equal(new[]
        {
            "long:001-hook",
            "long:002-what-is-it",
            "long:003-cause",
            "long:004-interesting-fact",
            "long:005-best-time",
            "long:006-accurate-sky-guide",
            "long:007-what-you-will-see",
            "long:008-viewing-tips",
            "long:009-final-reminder"
        }, response.LongNarration.Diagnostics!.V31NarrationKeysUsed);
        Assert.Contains("short/cause", response.ShortNarration.Diagnostics!.V31ScenePurposeLookupKeysUsed!);
        Assert.Contains("long/cause", response.LongNarration.Diagnostics!.V31ScenePurposeLookupKeysUsed!);
    }

    [Fact]
    public async Task WriteFinalSceneNarrationAsync_DeletesStaleFormatFoldersAndWritesExpectedFilesSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), "narration-v31-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "narration", "en", "short"));
        Directory.CreateDirectory(Path.Combine(root, "narration", "en", "long"));
        await File.WriteAllTextAsync(Path.Combine(root, "narration", "en", "short", "stale.txt"), "stale");
        await File.WriteAllTextAsync(Path.Combine(root, "narration", "en", "long", "stale.txt"), "stale");
        var composer = new NarrationV31Composer();

        try
        {
            var response = await composer.WriteFinalSceneNarrationAsync(new NarrationV31PreviewRequest(
                EventId: "event-1",
                RegionId: "us",
                Language: "en",
                DryRun: false,
                OverwriteExisting: true,
                ProductionContext: null,
                OutputRoot: root,
                EventType: "meteor shower",
                Title: "Geminids",
                LocalPeakTime: "10 PM",
                SkyDirectionHint: "eastern sky",
                BestViewingWindowLocal: "after 10 PM"), CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(root, "narration", "en", "short", "stale.txt")));
            Assert.False(File.Exists(Path.Combine(root, "narration", "en", "long", "stale.txt")));
            Assert.Equal(response.ShortNarration.Scenes.Select(s => s.Section + ".txt").OrderBy(name => name), Directory.EnumerateFiles(Path.Combine(root, "narration", "en", "short"), "*.txt").Select(Path.GetFileName).OrderBy(name => name));
            Assert.Equal(response.LongNarration.Scenes.Select(s => s.Section + ".txt").OrderBy(name => name), Directory.EnumerateFiles(Path.Combine(root, "narration", "en", "long"), "*.txt").Select(Path.GetFileName).OrderBy(name => name));
            Assert.All(response.GeneratedFiles, path => Assert.Contains("/narration/en/", path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
