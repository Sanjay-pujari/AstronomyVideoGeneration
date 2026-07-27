using System.Collections.Concurrent;
using Astronomy.MediaFactory.ProductionAdapters;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

/// <summary>Shared deterministic, provider-free infrastructure for the A3.10 host matrix.</summary>
internal static class DocumentaryProductionExecutionHostTestFixtures
{
 public const bool CertificationContract = true;
 public static string CreateWorkspaceRoot()
 {
  var path=Path.Combine(Path.GetTempPath(),"astronomy-a3-10",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(path);
  return path;
 }
 public static DocumentaryPhysicalArtifactDescriptor Descriptor(string root,string assetId,DocumentaryPhysicalArtifactKind kind,int sequence=1)
 {
  var extension=kind is DocumentaryPhysicalArtifactKind.VisualImage?"png":kind is DocumentaryPhysicalArtifactKind.NarrationAudio?"wav":kind is DocumentaryPhysicalArtifactKind.SubtitleDocument?"srt":"mp4";
  var path=Path.Combine(root,$"{sequence:D3}-{assetId}.{extension}");
  File.WriteAllText(path,$"A3.10|{kind}|{assetId}|{sequence}\n");
  var checksum=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
  return new(assetId,$"sha256:{checksum}",path,kind is DocumentaryPhysicalArtifactKind.VisualImage?"image/png":kind is DocumentaryPhysicalArtifactKind.NarrationAudio?"audio/wav":kind is DocumentaryPhysicalArtifactKind.SubtitleDocument?"application/x-subrip":"video/mp4",new FileInfo(path).Length,checksum,kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SubtitleDocument?null:1000,kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo?1920:null,kind is DocumentaryPhysicalArtifactKind.VisualImage or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo?1080:null,kind is DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo?30m:null,kind is DocumentaryPhysicalArtifactKind.NarrationAudio or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo?48000:null,kind is DocumentaryPhysicalArtifactKind.NarrationAudio or DocumentaryPhysicalArtifactKind.SceneVideo or DocumentaryPhysicalArtifactKind.VariantVideo?2:null,"deterministic-fake",1,"correlation-a3-10");
 }
}

internal sealed class FakeDocumentaryProductionAdapterRegistry : IDocumentaryProductionAdapterRegistry
{
 public IDocumentaryProductionVisualAdapter? VisualGeneration { get; init; }
 public IDocumentaryProductionNarrationAdapter? NarrationSynthesis { get; init; }
 public IDocumentaryProductionSubtitleAdapter? SubtitleGeneration { get; init; }
 public IDocumentaryProductionSceneCompositionAdapter? SceneComposition { get; init; }
 public IDocumentaryProductionVariantCompositionAdapter? VariantComposition { get; init; }
 public IDocumentaryProductionMediaVerificationAdapter? MediaVerification { get; init; }
 public ConcurrentQueue<string> InvocationOrder { get; } = new();
 public ConcurrentQueue<DocumentaryProductionAttemptContext> Attempts { get; } = new();
 public ConcurrentQueue<CancellationToken> CancellationTokens { get; } = new();
 public void Capture(string operation,DocumentaryProductionAttemptContext attempt,CancellationToken token){InvocationOrder.Enqueue(operation);Attempts.Enqueue(attempt);CancellationTokens.Enqueue(token);}
 public bool IsAvailable(DocumentaryProductionOperationKind operation)=>operation switch { DocumentaryProductionOperationKind.VisualGeneration=>VisualGeneration is not null,DocumentaryProductionOperationKind.NarrationSynthesis=>NarrationSynthesis is not null,DocumentaryProductionOperationKind.SubtitleGeneration=>SubtitleGeneration is not null,DocumentaryProductionOperationKind.SceneComposition=>SceneComposition is not null,DocumentaryProductionOperationKind.VariantComposition=>VariantComposition is not null,DocumentaryProductionOperationKind.MediaVerification=>MediaVerification is not null,_=>false };
}
