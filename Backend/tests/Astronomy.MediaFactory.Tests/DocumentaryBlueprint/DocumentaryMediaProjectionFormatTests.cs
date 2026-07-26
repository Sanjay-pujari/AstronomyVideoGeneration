using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionFormatTests
{
 [Fact] public void Short_projection_is_not_a_long_prefix_and_uses_independent_timing()
 {var p=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());var l=p.Variants.Single(x=>x.VariantType==DocumentaryMediaVariantType.LongEnglish);var s=p.Variants.Single(x=>x.VariantType==DocumentaryMediaVariantType.ShortEnglish);Assert.NotEqual(l.Scenes.Take(s.SceneCount).Select(x=>x.Title),s.Scenes.Select(x=>x.Title));Assert.Equal("16:9",l.AspectRatio);Assert.Equal("9:16",s.AspectRatio);Assert.NotEqual(l.PlannedDurationMilliseconds,s.PlannedDurationMilliseconds);Assert.All(s.Scenes.SelectMany(x=>x.KnowledgeReferences),x=>Assert.Contains(p.MaterializationRecord.Payloads,y=>y.PayloadId==x.PayloadId));}
}
