using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentarySceneCompositionArchitectureTests
{
 [Fact] public void Adapter_constructor_has_only_scene_composition_dependencies(){var types=typeof(ExistingDocumentarySceneCompositionAdapter).GetConstructors().Single().GetParameters().Select(x=>x.ParameterType).ToArray();types.Should().NotContain(typeof(IDocumentaryProductionVisualAdapter));types.Should().NotContain(typeof(IDocumentaryProductionNarrationAdapter));types.Should().NotContain(typeof(IDocumentaryProductionSubtitleAdapter));types.Select(x=>x.FullName).Should().NotContain(x=>x!.Contains("Variant")||x.Contains("Publishing")||x.Contains("Storage"));}
 [Fact] public void Core_does_not_reference_production_adapters(){typeof(Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentarySceneCompositionRequest).Assembly.GetReferencedAssemblies().Select(x=>x.Name).Should().NotContain("Astronomy.MediaFactory.ProductionAdapters");}
 [Fact] public void Scene_options_are_disabled_by_default_and_provider_id_is_stable(){new DocumentarySceneCompositionAdapterOptions().Enabled.Should().BeFalse();DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer.Should().Be("ExistingFFmpegSceneComposer");typeof(IDocumentaryProductionAdapterRegistry).GetProperty("SceneComposition").Should().NotBeNull();}
 [Fact] public void A37_adapter_source_uses_no_blocking_or_shell_process_api(){var root=FindRoot();var source=File.ReadAllText(Path.Combine(root,"Backend/src/Astronomy.MediaFactory.ProductionAdapters/SceneCompositionAdapter.cs"));foreach(var forbidden in new[]{".Result",".Wait(",".GetAwaiter().GetResult(","Task.Run(","Process.Start(","cmd.exe","powershell.exe","/bin/sh","bash -c"})source.Should().NotContain(forbidden);}
 static string FindRoot(){var p=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(p,"Backend/Astronomy.MediaFactory.slnx")))p=Directory.GetParent(p)!.FullName;return p;}
}
