using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionProjectTests
{
 [Fact] public void Complete_project_retains_all_upstream_linkage_and_totals()
 {var p=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());Assert.Equal($"{p.MaterializationId}.media-project",p.MediaProjectId);Assert.Same(p.ExportSpecification,p.MaterializationRecord.ExportSpecification);Assert.Same(p.CertificationRecord,p.MaterializationRecord.CertificationRecord);Assert.Same(p.ProvenanceRecord,p.MaterializationRecord.ProvenanceRecord);Assert.Same(p.ProductionPackage,p.MaterializationRecord.ProductionPackage);Assert.Equal(4,p.VariantCount);Assert.Equal(p.Variants.Sum(x=>x.SceneCount),p.TotalSceneCount);Assert.Equal(p.Variants.Sum(x=>x.PlannedDurationMilliseconds),p.TotalPlannedDurationMilliseconds);Assert.True(p.IsComplete);Assert.True(DocumentaryMediaProjectionValidator.ProjectValid(p));}
}
