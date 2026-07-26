using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportMaterializationTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static DocumentaryExportMaterializationPolicy Policy()=>new(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryExportPayloadType>(),Enum.GetValues<DocumentaryExportPayloadContentType>(),DocumentaryExportSerializerProfile.CanonicalWebJson,DocumentaryExportCharacterEncoding.Utf8,"1.0");
    private static DocumentaryExportMaterializationRecord Record(int cycles)
    {var specification=DocumentaryExportSpecificationFixture.Specification(cycles);var metadata=new DocumentaryExportMaterializationMetadata(DocumentaryExportSpecificationFixture.Timestamp," materializer ","1.0",specification.Metadata.CorrelationId);var result=new DocumentaryExportMaterializer().Materialize(new(specification,Policy(),metadata,DocumentaryExportSerializerProfile.CanonicalWebJson));Assert.Equal(DocumentaryExportMaterializationStatus.Complete,result.Status);return Assert.IsType<DocumentaryExportMaterializationRecord>(result.MaterializationRecord);}

    [Fact]
    public void Inventories_are_canonical()
    {Assert.Equal(["Complete","Rejected"],Enum.GetNames<DocumentaryExportMaterializationStatus>());Assert.Equal(16,Enum.GetValues<DocumentaryExportMaterializationRejectionReason>().Length);Assert.Equal(Enum.GetNames<DocumentaryExportItemType>(),Enum.GetNames<DocumentaryExportPayloadType>());Assert.Equal(10,Enum.GetValues<DocumentaryExportPayloadContentType>().Length);Assert.Equal(["CanonicalWebJson"],Enum.GetNames<DocumentaryExportSerializerProfile>());Assert.Equal(["Utf8"],Enum.GetNames<DocumentaryExportCharacterEncoding>());}

    [Theory][InlineData(0)][InlineData(1)][InlineData(3)]
    public void Complete_scenarios_materialize_canonical_content(int cycles)
    {var record=Record(cycles);Assert.Equal(10,record.PayloadCount);Assert.Equal(23,record.DependencyCount);Assert.Equal(record.Payloads.Sum(x=>x.CharacterCount),record.TotalCharacterCount);Assert.Equal(record.Payloads.Sum(x=>x.ByteCount),record.TotalByteCount);Assert.All(record.Payloads,p=>{Assert.Equal(Encoding.UTF8.GetBytes(p.Content),p.Utf8Bytes);Assert.Equal(p.Content.Length,p.CharacterCount);Assert.Equal(p.Utf8Bytes.Count,p.ByteCount);});var summary=new DocumentaryExportMaterializationSummarizer().Summarize(record);Assert.True(summary.IsComplete);Assert.Equal(record.TotalByteCount,summary.TotalByteCount);}

    [Fact]
    public void Contracts_round_trip_and_inputs_are_not_mutated()
    {var specification=DocumentaryExportSpecificationFixture.Specification(1);var policy=Policy();var metadata=new DocumentaryExportMaterializationMetadata(DocumentaryExportSpecificationFixture.Timestamp," materializer ","1.0",specification.Metadata.CorrelationId);var request=new DocumentaryExportMaterializationRequest(specification,policy,metadata,DocumentaryExportSerializerProfile.CanonicalWebJson);var before=JsonSerializer.Serialize(request,Json);var result=new DocumentaryExportMaterializer().Materialize(request);Assert.Equal(before,JsonSerializer.Serialize(request,Json));var record=Assert.IsType<DocumentaryExportMaterializationRecord>(result.MaterializationRecord);object[] contracts=[policy,metadata,request,record.Payloads[1].Dependencies[0],record.Payloads[0],record.Manifest,record,result,new DocumentaryExportMaterializationSummarizer().Summarize(record)];foreach(var value in contracts){var json=JsonSerializer.Serialize(value,value.GetType(),Json);var copy=JsonSerializer.Deserialize(json,value.GetType(),Json);Assert.Equal(json,JsonSerializer.Serialize(copy,value.GetType(),Json));}}

    [Fact]
    public void Correlation_mismatch_is_rejected()
    {var specification=DocumentaryExportSpecificationFixture.Specification(0);var request=new DocumentaryExportMaterializationRequest(specification,Policy(),new(DocumentaryExportSpecificationFixture.Timestamp,"x","1.0",specification.Metadata.CorrelationId.ToUpperInvariant()),DocumentaryExportSerializerProfile.CanonicalWebJson);var result=new DocumentaryExportMaterializer().Materialize(request);Assert.Equal([DocumentaryExportMaterializationRejectionReason.CorrelationMismatch],result.RejectionReasons);Assert.False(result.HasMaterializationRecord);}
}
