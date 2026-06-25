using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NarrationPreviewRequestTests
{
    [Fact]
    public void DeserializesReturnScenesBoolean()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("false"));

        Assert.NotNull(request);
        Assert.False(request.ReturnScenes);
    }

    [Fact]
    public void DeserializesReturnScenesArrayAsEnabledWhenScenesAreRequested()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("[\"hook\", \"best-time\"]"));

        Assert.NotNull(request);
        Assert.True(request.ReturnScenes);
    }

    [Fact]
    public void DeserializesEmptyReturnScenesArrayAsDisabled()
    {
        var request = JsonSerializer.Deserialize<NarrationPreviewRequest>(RequestJson("[]"));

        Assert.NotNull(request);
        Assert.False(request.ReturnScenes);
    }

    [Fact]
    public async Task NarrationGenerationFallsBackWhenEventNameIsNull()
    {
        var request = new NarrationPreviewRequest(
            PlanId: "plan-1",
            EventType: "meteor_shower",
            EventName: null!,
            ShortTitle: "Fallback Meteor Shower",
            Language: "en",
            RegionId: "us",
            Format: null,
            EventMetadata: null);
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("Fallback Meteor Shower", response.EventName);
        Assert.Contains(response.Scenes, scene => scene.Narration.Contains("Fallback Meteor Shower", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrationGenerationFallsBackWhenEventTypeAndNameAreNull()
    {
        var request = new NarrationPreviewRequest(
            PlanId: "plan-1",
            EventType: null!,
            EventName: null!,
            ShortTitle: null,
            Language: "en",
            RegionId: null!,
            Format: null,
            EventMetadata: null);
        var service = new NarrationGenerationService();

        var response = await service.GeneratePreviewAsync(request, CancellationToken.None);

        Assert.Equal("astronomy event", response.EventType);
        Assert.Equal("this sky event", response.EventName);
        Assert.Equal(string.Empty, response.RegionId);
        Assert.NotEmpty(response.Scenes);
    }

    private static string RequestJson(string returnScenes) => $$"""
        {
          "eventType": "meteor_shower",
          "eventName": "Geminid Meteor Shower",
          "language": "en",
          "regionId": "us",
          "returnScenes": {{returnScenes}}
        }
        """;
}
