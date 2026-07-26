using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionVariantTests
{
 [Fact] public void Four_variants_are_complete_canonical_and_bounded()
 {var r=DocumentaryMediaProjectionFixture.Orion();var p=DocumentaryMediaProjectionFixture.Complete(r);Assert.Equal(Enum.GetValues<DocumentaryMediaVariantType>(),p.Variants.Select(x=>x.VariantType));foreach(var v in p.Variants){Assert.Equal(r.Metadata.CorrelationId,v.CorrelationId);Assert.False(string.IsNullOrWhiteSpace(v.Title));Assert.False(string.IsNullOrWhiteSpace(v.Description));Assert.False(string.IsNullOrWhiteSpace(v.Hook));Assert.Equal(v.Format==DocumentaryVideoFormat.Long?"16:9":"9:16",v.AspectRatio);Assert.InRange(v.SceneCount,v.Format==DocumentaryVideoFormat.Long?4:3,v.Format==DocumentaryVideoFormat.Long?12:4);}}
}
