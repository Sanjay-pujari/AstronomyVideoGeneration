using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionTraceabilityTests
{
 [Fact] public void Every_projected_component_resolves_to_the_exact_retained_payload_object()
 {var p=DocumentaryMediaProjectionFixture.Complete(DocumentaryMediaProjectionFixture.Orion());foreach(var reference in p.Variants.SelectMany(x=>x.Scenes).SelectMany(s=>s.KnowledgeReferences.Concat(s.Narration.SelectMany(x=>x.KnowledgeReferences)).Concat(s.SubtitleCues.SelectMany(x=>x.KnowledgeReferences)).Concat(s.VisualPrompts.SelectMany(x=>x.KnowledgeReferences)))){var payload=Assert.Single(p.MaterializationRecord.Payloads,x=>x.PayloadId==reference.PayloadId);Assert.Equal(payload.PayloadType,reference.PayloadType);Assert.Equal(payload.SourceItemId,reference.SourceItemId);Assert.Equal(payload.ArtifactIdentity,reference.ArtifactIdentity);Assert.Equal(payload.ArtifactVersion,reference.ArtifactVersion);Assert.Equal(payload.CorrelationId,reference.CorrelationId);using var json=JsonDocument.Parse(payload.Content);Assert.StartsWith("/",reference.JsonPointer);}}
}
