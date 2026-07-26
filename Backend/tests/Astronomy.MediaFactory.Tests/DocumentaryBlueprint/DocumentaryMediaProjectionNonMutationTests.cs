using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionNonMutationTests
{
 [Fact] public void Projection_finalization_and_summarization_do_not_mutate_inputs()
 {var r=DocumentaryMediaProjectionFixture.Orion();var before=DocumentaryMediaProjectionFixture.Json(r);var result=new DocumentaryMediaProjector().Project(r);Assert.Equal(before,DocumentaryMediaProjectionFixture.Json(r));var p=Assert.IsType<DocumentaryMediaProject>(result.MediaProject);var variants=DocumentaryMediaProjectionFixture.Json(p.Variants);_ = DocumentaryMediaProjector.FinalizeProjection(r,p.Variants);Assert.Equal(variants,DocumentaryMediaProjectionFixture.Json(p.Variants));var project=DocumentaryMediaProjectionFixture.Json(p);_ = new DocumentaryMediaProjectionSummarizer().Summarize(p);Assert.Equal(project,DocumentaryMediaProjectionFixture.Json(p));}
}
