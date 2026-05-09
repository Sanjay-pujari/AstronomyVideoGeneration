using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
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
    public async Task SpecialEventGuide_prioritizes_event_seo()
    {
        var request = CreateRequest();
        request = new SeoMetadataRequest
        {
            SceneObservationContext = request.SceneObservationContext,
            SelectedVisibleObjects = ["Moon"],
            LocationName = request.LocationName,
            TargetDate = request.TargetDate,
            IsShortForm = false,
            ContentType = Astronomy.MediaFactory.Contracts.ContentType.SpecialEventGuide,
            EventId = "moon-full-moon-20260504",
            EventType = "full_moon",
            EventTitle = "Full Moon",
            EventDescription = "A dedicated full moon viewing guide."
        };

        var result = await CreateService().GenerateAsync(request, default);

        Assert.Contains("Full Moon", result.Title);
        Assert.Contains("How to Watch", result.Title);
        Assert.Contains("#FullMoon", result.HashtagsCsv);
        Assert.Contains("astronomy event", result.TagsCsv);
    }


    [Fact]
    public async Task Description_contains_growth_cta()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);

        Assert.Contains("Subscribe and watch the next video.", result.Description);
        Assert.Contains("Follow AstroPulse for your daily sky guide.", result.Description);
    }

    [Fact]
    public async Task Affiliate_disclosure_appears_only_when_enabled()
    {
        var disabled = await CreateService(new GrowthOptions { EnableAffiliateBlocks = false }).GenerateAsync(CreateRequest(), default);
        Assert.DoesNotContain("affiliate links", disabled.Description, StringComparison.OrdinalIgnoreCase);

        var enabled = await CreateService(new GrowthOptions { EnableAffiliateBlocks = true }).GenerateAsync(CreateRequest(), default);
        Assert.Contains("affiliate links", enabled.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("telescope and binocular links coming soon", enabled.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hindi_localization_keeps_growth_cta_localized()
    {
        var request = Clone(CreateRequest(), language: "hi");
        var result = await CreateService().GenerateAsync(request, default);

        Assert.Contains("स्थान:", result.Description);
        Assert.Contains("सब्सक्राइब करें", result.Description);
    }


    [Fact]
    public async Task Description_does_not_duplicate_growth_cta()
    {
        var request = CreateRequest();
        var service = CreateService(new GrowthOptions { DefaultCallToAction = "Follow AstroPulse for your daily sky guide." });

        var result = await service.GenerateAsync(request, default);
        var secondPass = GrowthMetadataComposer.ApplyGrowthBlock(result.Description, new GrowthOptions(), new GrowthMetadataInput
        {
            Platform = "YouTube",
            Language = request.Language,
            Region = request.LocationName,
            IsShortForm = false
        });

        Assert.Equal(result.Description, secondPass);
    }

    [Fact]
    public async Task Metadata_file_is_written()
    {
        var result = await CreateService().GenerateAsync(CreateRequest(), default);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await SeoMetadataGeneratorService.WriteToFileAsync(result, dir, default);
        Assert.True(File.Exists(Path.Combine(dir, "seo-metadata.json")));
        Assert.True(File.Exists(Path.Combine(dir, "growth-metadata.json")));
    }

    private static SeoMetadataGeneratorService CreateService(GrowthOptions? options = null)
        => new(Options.Create(options ?? new GrowthOptions()));

    private static SeoMetadataRequest Clone(SeoMetadataRequest source, string? language = null)
        => new()
        {
            SceneObservationContext = source.SceneObservationContext,
            SelectedVisibleObjects = source.SelectedVisibleObjects,
            LocationName = source.LocationName,
            TargetDate = source.TargetDate,
            IsShortForm = source.IsShortForm,
            ThumbnailVariants = source.ThumbnailVariants,
            ContentType = source.ContentType,
            EventId = source.EventId,
            EventType = source.EventType,
            EventTitle = source.EventTitle,
            EventDescription = source.EventDescription,
            Language = language ?? source.Language,
            RegionId = source.RegionId
        };

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
