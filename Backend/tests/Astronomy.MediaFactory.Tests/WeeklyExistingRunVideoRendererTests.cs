using System.Collections;
using System.Reflection;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyExistingRunVideoRendererTests
{
    [Fact]
    public void SelectRenderAssets_ToleratesMissingManifestCollections()
    {
        var segment = new FinalRenderSegment(
            "segment-1",
            "WeeklySkyOverview",
            "longform",
            0,
            10,
            10,
            "Narration",
            0,
            10,
            null!);
        var inputManifest = new WeeklyRenderInputManifest(
            Guid.NewGuid(),
            DateTime.UtcNow,
            null!,
            AllTimelineAssetsFound: true,
            AllTimelineAssetsReadable: true,
            Warnings: [],
            Errors: []);
        var productionManifest = new WeeklyProductionAssetManifest(
            Guid.NewGuid(),
            "global",
            "en",
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(6),
            600,
            60,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null!);

        var assets = InvokeSelectRenderAssets(segment, inputManifest, productionManifest);

        Assert.Empty(assets);
    }

    [Fact]
    public void SelectRenderAssets_FallsBackToInputManifest_WhenProductionBundleAssetsAreMissing()
    {
        var segment = new FinalRenderSegment(
            "segment-1",
            "WeeklySkyOverview",
            "longform",
            0,
            10,
            10,
            "Narration",
            0,
            10,
            []);
        var inputManifest = new WeeklyRenderInputManifest(
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new WeeklyRenderInputAsset(
                "overview-motion",
                "MotionGraphic",
                "/tmp/overview.png",
                Exists: true,
                Width: 1920,
                Height: 1080,
                DurationSecondsUsed: 10,
                UsedInLongform: true,
                UsedInShortform: false,
                Readable: true,
                FileSizeBytes: 1024,
                ValidationErrors: [])],
            AllTimelineAssetsFound: true,
            AllTimelineAssetsReadable: true,
            Warnings: [],
            Errors: []);
        var productionManifest = new WeeklyProductionAssetManifest(
            Guid.NewGuid(),
            "global",
            "en",
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(6),
            600,
            60,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [new SegmentProductionAssetBundle(
                "segment-1",
                "longform",
                "WeeklySkyOverview",
                10,
                "Ready",
                "narration.txt",
                12,
                null!,
                [],
                ProductionReady: false,
                ReadinessReason: "missing assigned assets",
                Warnings: [],
                ProductionReadyForTest: false,
                ProductionReadyForFinalVideo: false)]);

        var assets = InvokeSelectRenderAssets(segment, inputManifest, productionManifest);

        var asset = Assert.Single(assets);
        Assert.Equal("overview-motion", GetProperty<string>(asset, "AssetId"));
        Assert.Equal("/tmp/overview.png", GetProperty<string>(asset, "AssetPath"));
    }


    [Fact]
    public async Task HydrateRenderInputManifest_ToleratesMissingProductionManifestCollections()
    {
        var renderer = new WeeklyExistingRunVideoRenderer(
            Options.Create(new RenderingOptions()),
            NullLogger<WeeklyExistingRunVideoRenderer>.Instance);
        var pipelineRunId = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "weekly-render-hydration", pipelineRunId.ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var inputManifest = new WeeklyRenderInputManifest(
                pipelineRunId,
                DateTime.UtcNow,
                null!,
                AllTimelineAssetsFound: true,
                AllTimelineAssetsReadable: true,
                Warnings: null!,
                Errors: []);
            var productionManifest = new WeeklyProductionAssetManifest(
                pipelineRunId,
                "global",
                "en",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(6),
                600,
                60,
                10,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null!);
            var timeline = new FinalRenderTimeline(
                pipelineRunId,
                DateTime.UtcNow,
                new FinalRenderEpisodeTimeline(600, 0, null!),
                new FinalRenderEpisodeTimeline(60, 0, null!));

            var result = await InvokeHydrateRenderInputManifestAsync(renderer, pipelineRunId, root, inputManifest, productionManifest, timeline);

            Assert.Equal(10, GetField<int>(result, "Item2"));
            Assert.Equal(0, GetField<int>(result, "Item3"));
            Assert.False(GetField<bool>(result, "Item4"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static List<object> InvokeSelectRenderAssets(FinalRenderSegment segment, WeeklyRenderInputManifest inputManifest, WeeklyProductionAssetManifest productionManifest)
    {
        var method = typeof(WeeklyExistingRunVideoRenderer).GetMethod("SelectRenderAssets", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method.Invoke(null, [segment, false, inputManifest, productionManifest]);
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(result);
        return enumerable.Cast<object>().ToList();
    }


    private static async Task<object> InvokeHydrateRenderInputManifestAsync(WeeklyExistingRunVideoRenderer renderer, Guid pipelineRunId, string root, WeeklyRenderInputManifest inputManifest, WeeklyProductionAssetManifest productionManifest, FinalRenderTimeline timeline)
    {
        var method = typeof(WeeklyExistingRunVideoRenderer).GetMethod("HydrateRenderInputManifestAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var result = method.Invoke(renderer, [pipelineRunId, root, inputManifest, productionManifest, timeline, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(result);
        await task;
        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return resultProperty.GetValue(task)!;
    }


    private static T GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(instance));
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return Assert.IsType<T>(property.GetValue(instance));
    }
}
