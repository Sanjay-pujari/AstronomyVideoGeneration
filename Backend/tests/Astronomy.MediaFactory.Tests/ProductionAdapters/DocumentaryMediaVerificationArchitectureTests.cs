using Astronomy.MediaFactory.ProductionAdapters;using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryMediaVerificationArchitectureTests {
 [Fact] public void Adapter_is_disabled_by_default_and_provider_id_is_stable(){Assert.False(new DocumentaryMediaVerificationAdapterOptions().Enabled);Assert.Equal("ExistingFFprobeMediaVerifier",DocumentaryMediaVerificationProviderIds.ExistingFFprobeMediaVerifier);}
 [Fact] public void Media_verification_does_not_invoke_generation_or_composition_adapters(){var parameters=typeof(DocumentaryProductionMediaVerificationAdapter).GetConstructors().Single().GetParameters().Select(x=>x.ParameterType.Name);Assert.DoesNotContain(parameters,x=>x.Contains("Visual")||x.Contains("NarrationAdapter")||x.Contains("SubtitleAdapter")||x.Contains("SceneComposition")||x.Contains("VariantComposition"));}
}
