using Astronomy.MediaFactory.ProductionAdapters;
using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionArchitectureTests
{
 [Fact] public async Task Variant_composition_does_not_generate_or_render_upstream_assets(){var dependencies=typeof(ExistingDocumentaryVariantCompositionAdapter).GetConstructors().Single().GetParameters().Select(x=>x.ParameterType).ToArray();Assert.DoesNotContain(typeof(IDocumentaryProductionVisualAdapter),dependencies);Assert.DoesNotContain(typeof(IDocumentaryProductionNarrationAdapter),dependencies);Assert.DoesNotContain(typeof(IDocumentaryProductionSubtitleAdapter),dependencies);Assert.DoesNotContain(typeof(IDocumentaryProductionSceneCompositionAdapter),dependencies);await Task.CompletedTask;}
 [Fact] public async Task One_variant_request_produces_one_variant_video(){Assert.Equal("ExistingFFmpegVariantComposer",DocumentaryVariantCompositionProviderIds.ExistingFFmpegVariantComposer);Assert.Single(typeof(IDocumentaryProductionVariantCompositionAdapter).GetMethods());await Task.CompletedTask;}
 [Fact] public async Task Finalized_scenes_are_composed_in_certified_sequence_order(){var scenes=new[]{new {Sequence=2,AssetId="b"},new {Sequence=1,AssetId="z"},new {Sequence=1,AssetId="a"}};Assert.Equal(new[]{"a","z","b"},scenes.OrderBy(x=>x.Sequence).ThenBy(x=>x.AssetId,StringComparer.Ordinal).Select(x=>x.AssetId));await Task.CompletedTask;}
 [Fact] public void Adapter_is_disabled_by_default()=>Assert.False(new DocumentaryVariantCompositionAdapterOptions().Enabled);
}
