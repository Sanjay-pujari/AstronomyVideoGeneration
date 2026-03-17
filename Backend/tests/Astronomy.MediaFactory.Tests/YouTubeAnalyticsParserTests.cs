using Astronomy.MediaFactory.Publishing;
using Google.Apis.YouTube.v3.Data;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubeAnalyticsParserTests
{
    [Fact]
    public void Parse_ReturnsStatisticsAndDuration()
    {
        var video = new Video
        {
            Id = "abc123",
            Statistics = new VideoStatistics { ViewCount = 101, LikeCount = 7, CommentCount = 3 },
            ContentDetails = new VideoContentDetails { Duration = "PT1M5S" }
        };

        var parsed = YouTubeAnalyticsParser.Parse(video);

        Assert.Equal("abc123", parsed.VideoId);
        Assert.Equal(101, parsed.Views);
        Assert.Equal(7, parsed.Likes);
        Assert.Equal(3, parsed.Comments);
        Assert.Equal(65, parsed.DurationSeconds);
    }
}
