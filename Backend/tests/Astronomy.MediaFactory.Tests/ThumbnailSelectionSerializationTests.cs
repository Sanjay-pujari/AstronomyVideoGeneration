using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailSelectionSerializationTests
{
    [Fact]
    public async Task PipelineOrchestrator_WritesThumbnailSelectionWithoutCaseConflictingThumbnailKeys()
    {
        var outputDirectory = CreateTempDirectory();
        var thumbnailPath = Path.Combine(outputDirectory, "thumbnail.jpg");
        await File.WriteAllTextAsync(thumbnailPath, "thumbnail");
        var plan = new ThumbnailPlan
        {
            ThumbnailPath = thumbnailPath,
            LongThumbnailPath = thumbnailPath,
            ShortThumbnailPath = thumbnailPath,
            ThumbnailVariantPaths = [thumbnailPath],
            PrimaryThumbnailText = "Tonight's sky"
        };

        var method = typeof(PipelineOrchestrator).GetMethod("WriteThumbnailSelectionAsync", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task)method!.Invoke(null, [plan, outputDirectory, CancellationToken.None])!;
        await task;

        var selectionPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-selection.json");
        using var selection = JsonDocument.Parse(await File.ReadAllTextAsync(selectionPath));
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("thumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("originalThumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("longThumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("shortThumbnailPath").GetString());
        Assert.False(selection.RootElement.TryGetProperty("ThumbnailPath", out _));
    }

    [Fact]
    public async Task CinematicThumbnailService_WritesThumbnailSelectionWithoutCaseConflictingThumbnailKeys()
    {
        var outputDirectory = CreateTempDirectory();
        var thumbnailsDirectory = Path.Combine(outputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        var thumbnailPath = Path.Combine(thumbnailsDirectory, "thumbnail-long.jpg");
        await File.WriteAllTextAsync(thumbnailPath, "thumbnail");
        var plan = new ThumbnailPlan
        {
            ThumbnailPath = thumbnailPath,
            LongThumbnailPath = thumbnailPath,
            ShortThumbnailPath = thumbnailPath,
            ThumbnailVariantPaths = [thumbnailPath],
            PrimaryThumbnailText = "Tonight's sky"
        };

        var method = typeof(CinematicThumbnailService).GetMethod("WriteSelectionAsync", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task)method!.Invoke(null, [plan, thumbnailsDirectory, CancellationToken.None])!;
        await task;

        var selectionPath = Path.Combine(thumbnailsDirectory, "thumbnail-selection.json");
        using var selection = JsonDocument.Parse(await File.ReadAllTextAsync(selectionPath));
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("thumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("originalThumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("longThumbnailPath").GetString());
        Assert.Equal(thumbnailPath, selection.RootElement.GetProperty("shortThumbnailPath").GetString());
        Assert.False(selection.RootElement.TryGetProperty("ThumbnailPath", out _));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "astro-thumbnail-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
