using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionLanguageTests
{
 [Theory] [InlineData(DocumentaryVideoFormat.Long)] [InlineData(DocumentaryVideoFormat.Short)] public void English_and_Hindi_are_semantically_aligned_but_naturally_independent(DocumentaryVideoFormat format)
 {var variants=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion()).Variants.Where(x=>x.Format==format).ToArray();var en=variants.Single(x=>x.Language==DocumentaryMediaLanguage.English);var hi=variants.Single(x=>x.Language==DocumentaryMediaLanguage.Hindi);Assert.Equal(en.Scenes.Select(x=>x.SceneRole),hi.Scenes.Select(x=>x.SceneRole));Assert.Equal(en.Scenes.SelectMany(x=>x.KnowledgeReferences).Select(x=>x.JsonPointer),hi.Scenes.SelectMany(x=>x.KnowledgeReferences).Select(x=>x.JsonPointer));Assert.NotEqual(en.Scenes.SelectMany(x=>x.Narration).Select(x=>x.Text),hi.Scenes.SelectMany(x=>x.Narration).Select(x=>x.Text));Assert.NotEqual(en.Scenes.SelectMany(x=>x.Narration).Sum(x=>x.EstimatedDurationMilliseconds),hi.Scenes.SelectMany(x=>x.Narration).Sum(x=>x.EstimatedDurationMilliseconds));}
}
