using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionSummaryTests
{
 [Fact] public void Summary_exactly_aggregates_the_complete_project()
 {var p=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());var s=new DocumentaryMediaProjectionSummarizer().Summarize(p);Assert.Equal(p.MediaProjectId,s.MediaProjectId);Assert.Equal(p.TopicId,s.TopicId);Assert.Equal(4,s.VariantCount);Assert.Equal(2,s.LanguageCount);Assert.Equal(2,s.FormatCount);Assert.Equal(p.TotalSceneCount,s.TotalSceneCount);Assert.Equal(p.TotalPlannedDurationMilliseconds,s.TotalPlannedDurationMilliseconds);Assert.Equal(p.Metadata.CreatedUtc,s.CreatedUtc);Assert.Equal(" projection fixture ",s.CreatedBy);Assert.True(s.IsComplete);}
}
