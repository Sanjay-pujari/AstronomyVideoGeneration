using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelineSerializationTests
{
 static readonly JsonSerializerOptions Web=new(JsonSerializerDefaults.Web);
 [Theory][MemberData(nameof(Values))] public void Public_graphs_round_trip_byte_identically(object value,Type type){var json=JsonSerializer.Serialize(value,type,Web);var copy=JsonSerializer.Deserialize(json,type,Web);Assert.Equal(json,JsonSerializer.Serialize(copy,type,Web));}
 public static IEnumerable<object[]> Values(){var q=DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion());var result=DocumentaryMediaPipelineFixture.Run(q.MediaProject);var record=result.ExecutionRecord!;yield return [q,typeof(DocumentaryMediaPipelineRequest)];yield return [q.Policy,typeof(DocumentaryMediaPipelinePolicy)];yield return [q.Metadata,typeof(DocumentaryMediaPipelineMetadata)];yield return [record.ExecutionPlan,typeof(DocumentaryMediaPipelineExecutionPlan)];yield return [record.OutputManifest,typeof(DocumentaryMediaOutputManifest)];yield return [record,typeof(DocumentaryMediaPipelineExecutionRecord)];yield return [result,typeof(DocumentaryMediaPipelineResult)];yield return [DocumentaryMediaPipelineFixture.Summary(record),typeof(DocumentaryMediaPipelineSummary)];}
 [Fact] public void Independently_reconstructed_request_is_byte_identical(){var p=DocumentaryMediaPipelineFixture.Orion();Assert.Equal(JsonSerializer.Serialize(DocumentaryMediaPipelineFixture.Request(p),Web),JsonSerializer.Serialize(DocumentaryMediaPipelineFixture.EquivalentRequest(p),Web));}
}
