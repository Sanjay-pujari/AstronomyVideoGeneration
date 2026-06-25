using System.Text.Json;
using Astronomy.MediaFactory.Core;

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
