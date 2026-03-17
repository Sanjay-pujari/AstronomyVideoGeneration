using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class MetadataOptimizationServiceTests
{
    [Fact]
    public async Task OptimizeForVideoAsync_Generates_ContentTypeSpecific_Title()
    {
        var service = new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance);
        var context = BuildContext();

        var result = await service.OptimizeForVideoAsync(new MetadataOptimizationInput
        {
            ContentType = ContentType.DailySkyGuide,
            Context = context,
            SourceTitle = "Sky Update",
            SourceDescription = "Tonight we cover Jupiter and Orion.",
            SourceTags = ["astronomy", "Jupiter"]
        }, CancellationToken.None);

        Assert.Contains("Tonight", result.PrimaryTitle);
        Assert.Contains("jupiter", string.Join(',', result.Tags), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OptimizeForVideoAsync_Deduplicates_Tags()
    {
        var service = new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance);
        var result = await service.OptimizeForVideoAsync(new MetadataOptimizationInput
        {
            ContentType = ContentType.SpaceNews,
            Context = BuildContext(),
            SourceTitle = "News",
            SourceDescription = "Desc",
            SourceTags = ["Astronomy", "astronomy", "  astronomy  "]
        }, CancellationToken.None);

        Assert.Equal(result.Tags.Length, result.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task OptimizeForShortAsync_Adds_Shorts_Hashtag()
    {
        var service = new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance);
        var result = await service.OptimizeForShortAsync(new MetadataOptimizationInput
        {
            ContentType = ContentType.TelescopeTargets,
            Context = BuildContext(),
            SourceTitle = "Find M42",
            SourceDescription = "Quick guide",
            SourceTags = ["astronomy"],
            SourceHookLine = "Stop scrolling"
        }, CancellationToken.None);

        Assert.Contains("#shorts", result.Hashtags, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.HookLine));
    }

    [Fact]
    public async Task OptimizeForVideoAsync_Uses_Fallback_WhenAiFails()
    {
        var service = new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance, new ThrowingModelClient());
        var result = await service.OptimizeForVideoAsync(new MetadataOptimizationInput
        {
            ContentType = ContentType.AstrophotographyTips,
            Context = BuildContext(),
            SourceTitle = "AP tips",
            SourceDescription = "Use ISO 800",
            SourceTags = ["ap"]
        }, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.PrimaryTitle));
        Assert.NotEmpty(result.Tags);
    }

    private static AstronomyContext BuildContext() => new()
    {
        Date = new DateOnly(2026, 3, 17),
        LocationName = "Pune",
        Events =
        [
            new AstronomyEventModel { ObjectName = "Jupiter", Direction = "south", VisibilityWindow = "after sunset", ObservationTool = "binoculars", Score = 0.9 },
            new AstronomyEventModel { ObjectName = "Orion Nebula", Direction = "west", VisibilityWindow = "early evening", ObservationTool = "small telescope", Score = 0.8 }
        ]
    };

    private sealed class ThrowingModelClient : IMetadataOptimizationModelClient
    {
        public Task<OptimizedVideoMetadata?> TryOptimizeAsync(MetadataOptimizationInput input, bool isShort, CancellationToken cancellationToken)
            => throw new InvalidOperationException("fail");
    }
}
