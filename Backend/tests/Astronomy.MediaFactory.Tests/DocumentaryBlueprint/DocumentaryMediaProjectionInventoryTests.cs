using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionInventoryTests
{
 [Fact] public void Inventories_have_exact_certified_order_and_values()
 {
  Assert.Equal(new[]{"Complete","Rejected"},Enum.GetNames<DocumentaryMediaProjectionStatus>());
  Assert.Equal(new[]{"MaterializationRecordNotComplete","MaterializationIdentityMismatch","ExportSpecificationIdentityMismatch","CertificationIdentityMismatch","ProvenanceIdentityMismatch","PackageIdentityMismatch","CorrelationMismatch","ProjectionPolicyRejected","TopicProfileRejected","RequiredVariantMissing","VariantInventoryMismatch","VariantOrderMismatch","VariantIdentityMismatch","SceneInventoryMismatch","SceneOrderMismatch","SceneIdentityMismatch","NarrativeMappingMismatch","SubtitleMappingMismatch","VisualPromptMappingMismatch","TimingPlanMismatch","UnsupportedVariantPresent"},Enum.GetNames<DocumentaryMediaProjectionRejectionReason>());
  Assert.Equal(new[]{"Long","Short"},Enum.GetNames<DocumentaryVideoFormat>()); Assert.Equal(new[]{"English","Hindi"},Enum.GetNames<DocumentaryMediaLanguage>());
  Assert.Equal(new[]{"LongEnglish","LongHindi","ShortEnglish","ShortHindi"},Enum.GetNames<DocumentaryMediaVariantType>());
  Assert.Equal(27,Enum.GetValues<DocumentaryAstronomyTopicFamily>().Length); Assert.Equal(17,Enum.GetValues<DocumentaryMediaSceneRole>().Length); Assert.Equal(11,Enum.GetValues<DocumentaryMediaVisualType>().Length); Assert.Equal(11,Enum.GetValues<DocumentaryCameraMotion>().Length); Assert.Equal(5,Enum.GetValues<DocumentarySceneTransition>().Length); Assert.Equal(5,Enum.GetValues<DocumentarySubtitlePresentation>().Length);
 }
 [Fact] public void Canonical_variants_map_to_format_and_language()
 { Assert.Equal(new[]{(DocumentaryVideoFormat.Long,DocumentaryMediaLanguage.English),(DocumentaryVideoFormat.Long,DocumentaryMediaLanguage.Hindi),(DocumentaryVideoFormat.Short,DocumentaryMediaLanguage.English),(DocumentaryVideoFormat.Short,DocumentaryMediaLanguage.Hindi)},Enum.GetValues<DocumentaryMediaVariantType>().Select(DocumentaryMediaProjectionInventory.Mapping)); }
}
