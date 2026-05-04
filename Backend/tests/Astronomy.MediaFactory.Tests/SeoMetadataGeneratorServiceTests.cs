using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SeoMetadataGeneratorServiceTests
{
    [Fact]
    public async Task Title_contains_selected_objects()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);
        Assert.Contains("Moon", result.Title);
        Assert.Contains("Jupiter", result.Title);
    }

    [Fact]
    public async Task Description_contains_local_times()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);
        Assert.Contains("08:15 PM", result.Description);
    }

    [Fact]
    public async Task No_unselected_object_appears()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);
        Assert.DoesNotContain("Venus", result.Title);
        Assert.DoesNotContain("Venus", result.Description);
    }

    [Fact]
    public async Task Shorts_include_shorts_hashtag()
    {
        var request = CreateRequest();
        request = new SeoMetadataRequest
        {
            SceneObservationContext = request.SceneObservationContext,
            SelectedVisibleObjects = request.SelectedVisibleObjects,
            LocationName = request.LocationName,
            TargetDate = request.TargetDate,
            IsShortForm = true,
            ThumbnailVariants = request.ThumbnailVariants
        };
        var result = await CreateService().GenerateAsync(request, default);
        Assert.Contains("#Shorts", result.Title + " " + result.Description + " " + result.HashtagsCsv);
    }

    [Fact]
    public async Task Metadata_file_is_written()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await SeoMetadataGeneratorService.WriteToFileAsync(result, dir, default);
        Assert.True(File.Exists(Path.Combine(dir, "seo-metadata.json")));
    }

    private static SeoMetadataGeneratorService CreateService() => new();

    private static SeoMetadataRequest CreateRequest()
        => new()
        {
            SceneObservationContext =
            [
                new SceneObservationContext { ObjectName = "Moon", LocalObservationTime = new DateTime(2026,5,4,20,15,0), Timezone = "Asia/Kolkata", DirectionLabel = "West", AltitudeDegrees = 31.2 },
                new SceneObservationContext { ObjectName = "Jupiter", LocalObservationTime = new DateTime(2026,5,4,21,0,0), Timezone = "Asia/Kolkata", DirectionLabel = "South", AltitudeDegrees = 45.2 },
                new SceneObservationContext { ObjectName = "Venus", LocalObservationTime = new DateTime(2026,5,4,19,45,0), Timezone = "Asia/Kolkata", DirectionLabel = "West", AltitudeDegrees = 20.2 }
            ],
            SelectedVisibleObjects = ["Moon", "Jupiter"],
            LocationName = "Udaipur",
            TargetDate = new DateOnly(2026, 5, 4),
            IsShortForm = false
        };
}
