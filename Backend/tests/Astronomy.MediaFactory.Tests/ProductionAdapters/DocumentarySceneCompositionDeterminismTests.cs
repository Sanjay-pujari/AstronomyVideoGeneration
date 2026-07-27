using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentarySceneCompositionDeterminismTests
{
 [Fact] public void Same_logical_scene_has_same_provider_filename(){using var a=new Temp();using var b=new Temp();Path.GetFileName(DocumentarySceneCompositionTestFixtures.ProviderRequest(a.Path).OutputPath).Should().Be(Path.GetFileName(DocumentarySceneCompositionTestFixtures.ProviderRequest(b.Path).OutputPath));}
 [Fact] public async Task Same_bytes_have_same_checksum_and_content_identity(){using var t=new Temp();var a=Path.Combine(t.Path,"a");var b=Path.Combine(t.Path,"b");await File.WriteAllBytesAsync(a,[1,2,3]);await File.WriteAllBytesAsync(b,[1,2,3]);var checksum=new DocumentaryChecksumService();var x=await checksum.ComputeSha256Async(a,default);var y=await checksum.ComputeSha256Async(b,default);x.Should().Be(y);new DocumentaryContentIdentityFactory().Create(x).Should().Be("sha256:"+y);}
 sealed class Temp:IDisposable{public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));public Temp()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
}
