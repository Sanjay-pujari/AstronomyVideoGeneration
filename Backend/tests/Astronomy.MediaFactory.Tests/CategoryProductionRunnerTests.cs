using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CategoryProductionRunnerTests
{
    [Fact]
    public async Task Runner_Resolves_DailySkyGuide_Strategy()
    {
        var strategy = new FakeStrategy();
        var runner = new CategoryProductionRunner([strategy]);
        var response = await runner.RunAsync(Request(), CancellationToken.None);
        Assert.True(response.Success);
        Assert.True(strategy.Called);
        Assert.False(response.PublishToYouTube);
        Assert.False(response.PublishToFacebook);
        Assert.False(response.PublishToInstagram);
    }

    [Fact]
    public async Task Runner_Returns_Failed_For_Unsupported_Category()
    {
        var runner = new CategoryProductionRunner([]);
        var response = await runner.RunAsync(Request() with { ContentCategoryCode = "Unknown" }, CancellationToken.None);
        Assert.False(response.Success);
        Assert.Contains("Unsupported", response.ErrorMessage);
    }

    private static CategoryProductionPreviewRequest Request() => new("DailySkyGuide", "hi", "IN-RJ-UDAIPUR", "Udaipur", DateTime.Parse("2026-05-21T18:00:00Z"), "Moon", true, true, true, false, true);

    private sealed class FakeStrategy : ICategoryProductionPipelineStrategy
    {
        public string ContentCategoryCode => "DailySkyGuide";
        public bool Called { get; private set; }

        public Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new CategoryProductionPreviewResponse(
                Guid.NewGuid(), request.ContentCategoryCode, true, false,
                request.PublishToYouTube, request.PublishToFacebook, request.PublishToInstagram, false,
                "long-audio.mp3", "short-audio.mp3", "long.mp4", "short.mp4", "long.jpg", "short.jpg",
                null, null, null, new { title = "t" },
                [new CategoryProductionStepResult("BuildRunPipelineRequest", "Completed", DateTime.UtcNow, DateTime.UtcNow, 1, "ok", null, [])],
                [], null, null));
        }
    }
}
