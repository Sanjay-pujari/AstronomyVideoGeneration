using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryVariantCompositionDiTests
{
 [Fact] public void Bridge_registers_all_variant_composition_services(){using var provider=Provider();using var scope=provider.CreateScope();var s=scope.ServiceProvider;Assert.NotNull(s.GetRequiredService<IDocumentaryVariantDependencyResolver>());Assert.NotNull(s.GetRequiredService<IDocumentaryVariantCompatibilityValidator>());Assert.NotNull(s.GetRequiredService<IDocumentaryVariantCompositionProviderBinding>());Assert.NotNull(s.GetRequiredService<IDocumentaryVariantVideoInspector>());Assert.NotNull(s.GetRequiredService<IDocumentaryProductionVariantCompositionAdapter>());Assert.NotNull(s.GetRequiredService<IDocumentaryVariantCompositionResultMapper>());Assert.NotNull(s.GetRequiredService<IDocumentaryProductionAdapterRegistry>());}
 [Fact] public void Adapter_registry_exposes_variant_composition(){using var provider=Provider();using var scope=provider.CreateScope();var registry=scope.ServiceProvider.GetRequiredService<IDocumentaryProductionAdapterRegistry>();Assert.NotNull(registry.VariantComposition);Assert.True(registry.IsAvailable(DocumentaryProductionOperationKind.VariantComposition));}
 [Fact] public void Variant_adapter_options_are_disabled_by_default(){using var provider=Provider();Assert.False(provider.GetRequiredService<IOptions<DocumentaryVariantCompositionAdapterOptions>>().Value.Enabled);}
 [Fact] public async Task Duplicate_variant_provider_bindings_are_rejected_deterministically(){await using var f=await DocumentaryVariantCompositionAdapterFixture.CreateAsync();var one=new FakeVariantCompositionProviderBinding();var two=new FakeVariantCompositionProviderBinding();f.Rebuild(bindings:[one,two]);var x=await f.ComposeAsync();Assert.Equal(DocumentaryProductionFailureCode.AdapterUnavailable,x.Failure?.Code);Assert.Equal(0,one.InvocationCount);Assert.Equal(0,two.InvocationCount);Assert.Equal(0,f.VariantInspector.InvocationCount);}
 static ServiceProvider Provider(){var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Rendering:FfmpegPath","never-executed"}}).Build();var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IAzureSpeechClient,FakeAzure>();services.AddSingleton<ISsmlBuilder,FakeSsml>();services.AddSingleton<IProcessRunner,A38RecordingProcessRunner>();services.AddSingleton<IDocumentaryMediaProbe,FakeDocumentaryMediaProbe>();services.AddDocumentaryProductionBridge(configuration);return services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=false,ValidateOnBuild=false});}
 sealed class FakeAzure:IAzureSpeechClient{public Task<byte[]> SynthesizeMp3Async(string text,AzureSpeechOptions options,CancellationToken token)=>Task.FromResult(Array.Empty<byte>());public Task<byte[]> SynthesizeWavSsmlAsync(string ssml,AzureSpeechOptions options,CancellationToken token)=>Task.FromResult(Array.Empty<byte>());}
 sealed class FakeSsml:ISsmlBuilder{public string BuildSsml(string text,string voiceName,SsmlNarrationProfile? profile=null,string? rateOverride=null,string? pitchOverride=null)=>text;}
}
