using Astronomy.MediaFactory.ProductionAdapters;
using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompositionArchitectureTests
{
 [Fact] public void Adapter_is_disabled_by_default()=>Assert.False(new DocumentaryVariantCompositionAdapterOptions().Enabled);
 [Fact] public void Variant_adapter_has_no_upstream_generation_dependencies(){var p=Parameters();foreach(var type in new[]{typeof(IDocumentaryProductionVisualAdapter),typeof(IDocumentaryProductionNarrationAdapter),typeof(IDocumentaryProductionSubtitleAdapter),typeof(IDocumentaryProductionSceneCompositionAdapter)})Assert.DoesNotContain(type,p);}
 [Fact] public void Variant_adapter_has_no_storage_publishing_or_verification_dependency(){Assert.DoesNotContain(Parameters(),p=>p.Name.Contains("Storage",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("Publish",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("Verification",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("Verifier",StringComparison.OrdinalIgnoreCase));}
 [Fact] public void Variant_provider_identity_is_stable(){Assert.Equal("ExistingFFmpegVariantComposer",DocumentaryVariantCompositionProviderIds.ExistingFFmpegVariantComposer);Assert.DoesNotContain(Environment.MachineName,DocumentaryVariantCompositionProviderIds.ExistingFFmpegVariantComposer,StringComparison.OrdinalIgnoreCase);}
 [Fact] public void A3_8_does_not_depend_on_general_media_verification_adapter(){Assert.Contains(typeof(IDocumentaryVariantVideoInspector),Parameters());Assert.DoesNotContain(Parameters(),p=>p.Name.Contains("MediaVerification",StringComparison.OrdinalIgnoreCase));}
 [Fact] public void Variant_source_has_no_blocking_async_calls(){var source=Source();foreach(var pattern in new[]{".Result",".Wait(",".GetAwaiter().GetResult(","Task.Run("})Assert.DoesNotContain(pattern,source,StringComparison.Ordinal);}
 [Fact] public void Variant_source_has_no_direct_process_or_shell_invocation(){var source=Source();foreach(var pattern in new[]{"Process.Start(","cmd.exe","powershell.exe","/bin/sh","bash -c"})Assert.DoesNotContain(pattern,source,StringComparison.OrdinalIgnoreCase);}
 [Fact] public void Core_does_not_reference_production_adapters(){var root=Root();var project=File.ReadAllText(Path.Combine(root,"Backend/src/Astronomy.MediaFactory.Core/Astronomy.MediaFactory.Core.csproj"));Assert.DoesNotContain("Astronomy.MediaFactory.ProductionAdapters",project,StringComparison.Ordinal);var references=typeof(Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryMediaVariant).Assembly.GetReferencedAssemblies();Assert.DoesNotContain(references,a=>a.Name=="Astronomy.MediaFactory.ProductionAdapters");}
 static Type[] Parameters()=>typeof(ExistingDocumentaryVariantCompositionAdapter).GetConstructors().Single().GetParameters().Select(x=>x.ParameterType).ToArray();
 static string Source()=>File.ReadAllText(Path.Combine(Root(),"Backend/src/Astronomy.MediaFactory.ProductionAdapters/VariantCompositionAdapter.cs"));
 static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Backend","src")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
}
