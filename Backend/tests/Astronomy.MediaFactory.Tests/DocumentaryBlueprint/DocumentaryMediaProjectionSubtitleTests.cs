using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionSubtitleTests
{
 [Theory] [InlineData(DocumentaryMediaLanguage.English)] [InlineData(DocumentaryMediaLanguage.Hindi)] public void Subtitles_completely_cover_narration_without_splitting_words(DocumentaryMediaLanguage language)
 {var p=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());foreach(var s in p.Variants.Single(x=>x.Format==DocumentaryVideoFormat.Long&&x.Language==language).Scenes){var n=Assert.Single(s.Narration);Assert.Equal(N(n.Text),N(string.Join(' ',s.SubtitleCues.Select(x=>x.Text))));Assert.Equal(0,s.SubtitleCues[0].StartOffsetMilliseconds);Assert.Equal(n.EstimatedDurationMilliseconds,s.SubtitleCues[^1].EndOffsetMilliseconds);Assert.Equal(Enumerable.Range(0,s.SubtitleCues.Count),s.SubtitleCues.Select(x=>x.Sequence));Assert.All(s.SubtitleCues,x=>{Assert.InRange(x.Line1.Length,1,34);Assert.True(x.Line2 is null||x.Line2.Length<=34);});}}
 private static string N(string x)=>string.Join(' ',x.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
}
