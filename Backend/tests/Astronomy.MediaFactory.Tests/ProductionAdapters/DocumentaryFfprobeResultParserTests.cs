using Astronomy.MediaFactory.ProductionAdapters;using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryFfprobeResultParserTests {
 [Fact] public void Parses_video_audio_subtitle_and_rational_rate(){var x=new DocumentaryFfprobeResultParser().Parse("""{"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"2.500"},"streams":[{"codec_type":"video","width":1920,"height":1080,"avg_frame_rate":"30000/1001"},{"codec_type":"audio","sample_rate":"48000","channels":2},{"codec_type":"subtitle"}]}""");Assert.True(x.Succeeded);Assert.Equal(2500,x.DurationMilliseconds);Assert.True(x.HasVideoStream);Assert.True(x.HasAudioStream);Assert.True(x.HasSubtitleStream);Assert.Equal(30000m/1001m,x.FrameRate);}
 [Theory][InlineData("")][InlineData("{")][InlineData("{\"format\":{\"duration\":\"Infinity\"},\"streams\":[]}")] public void Invalid_input_is_safe(string json){var x=new DocumentaryFfprobeResultParser().Parse(json);if(json is "" or "{")Assert.False(x.Succeeded);else Assert.True(x.Succeeded);}
}
