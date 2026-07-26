namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionDocumentationTests
{
 [Fact] public void Foundation_document_declares_certified_scope_and_boundaries()
 {var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../docs/documentary-media-projection-foundation.md"));var text=File.ReadAllText(path);var terms=new[]{"four canonical variants","shared semantic plan","topic applicability filtering","constellation","planet-conjunction","no invented facts","complete subtitle coverage","validated payload traceability","deterministic timing","No images are generated","no speech is synthesized","no subtitle files are created","no video is rendered","no FFmpeg","upload","publishing","external work","O2.18 has not started"};Assert.All(terms,x=>Assert.Contains(x,text,StringComparison.OrdinalIgnoreCase));}
}
