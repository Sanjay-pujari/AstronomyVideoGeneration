using System.Text;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportMaterializationHardeningTests
{
    private static DocumentaryExportMaterializationRecord Record()
    {
        var specification=DocumentaryExportSpecificationFixture.Specification(2);
        var policy=new DocumentaryExportMaterializationPolicy(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryExportPayloadType>(),Enum.GetValues<DocumentaryExportPayloadContentType>(),DocumentaryExportSerializerProfile.CanonicalWebJson,DocumentaryExportCharacterEncoding.Utf8,"1.0");
        var metadata=new DocumentaryExportMaterializationMetadata(DocumentaryExportSpecificationFixture.Timestamp," certifier ","1.0",specification.Metadata.CorrelationId);
        return Assert.IsType<DocumentaryExportMaterializationRecord>(new DocumentaryExportMaterializer().Materialize(new(specification,policy,metadata,DocumentaryExportSerializerProfile.CanonicalWebJson)).MaterializationRecord);
    }

    [Fact]
    public void Canonical_inventory_owns_all_ten_mappings_and_twenty_three_ordered_targets()
    {
        var types=Enum.GetValues<DocumentaryExportPayloadType>();
        Assert.Equal(10,types.Length);
        Assert.Equal(Enum.GetValues<DocumentaryExportPayloadContentType>(),types.Select(DocumentaryExportMaterializationInventory.PayloadContentTypeFor));
        Assert.Equal(23,types.Sum(x=>DocumentaryExportMaterializationInventory.DependencyTargetsFor(x).Count));
        Assert.Equal([DocumentaryExportPayloadType.ProvenanceRecord,DocumentaryExportPayloadType.CertificationDecision],DocumentaryExportMaterializationInventory.DependencyTargetsFor(DocumentaryExportPayloadType.CertificationRecord));
    }

    [Fact]
    public void Payload_contract_rejects_noncanonical_dependency_graphs()
    {
        var source=Record().Payloads[(int)DocumentaryExportPayloadType.ConvergenceEvidence];
        var missing=source.Dependencies.Take(1).ToArray();
        Assert.Throws<ArgumentException>(()=>Copy(source,missing));
        var reversed=source.Dependencies.Reverse().ToArray();
        Assert.Throws<ArgumentException>(()=>Copy(source,reversed));
        var duplicate=new[]{source.Dependencies[0],source.Dependencies[0]};
        Assert.Throws<ArgumentException>(()=>Copy(source,duplicate));
    }

    [Fact]
    public void Categorized_validator_accepts_the_complete_canonical_graph_and_categorizes_missing_payloads()
    {
        var record=Record();
        Assert.Empty(DocumentaryExportMaterializationValidator.ValidatePayloads(record.ExportSpecification,record.Metadata,record.SerializerProfile,record.Payloads));
        var reasons=DocumentaryExportMaterializationValidator.ValidatePayloads(record.ExportSpecification,record.Metadata,record.SerializerProfile,record.Payloads.Skip(1).ToArray());
        Assert.Contains(DocumentaryExportMaterializationRejectionReason.RequiredPayloadMissing,reasons);
        Assert.Contains(DocumentaryExportMaterializationRejectionReason.PayloadInventoryMismatch,reasons);
        Assert.Contains(DocumentaryExportMaterializationRejectionReason.PayloadOrderMismatch,reasons);
    }

    [Fact]
    public void Shared_finalizer_returns_categorized_rejection_result_for_a_corrupt_candidate_graph()
    {
        var record=Record();
        var request=new DocumentaryExportMaterializationRequest(record.ExportSpecification,record.Policy,record.Metadata,record.SerializerProfile);

        var result=DocumentaryExportMaterializer.FinalizeMaterialization(request,record.Payloads.Skip(1).ToArray());

        Assert.Equal(DocumentaryExportMaterializationStatus.Rejected,result.Status);
        Assert.Equal([
            DocumentaryExportMaterializationRejectionReason.RequiredPayloadMissing,
            DocumentaryExportMaterializationRejectionReason.PayloadInventoryMismatch,
            DocumentaryExportMaterializationRejectionReason.PayloadOrderMismatch
        ],result.RejectionReasons);
        Assert.Null(result.MaterializationRecord);
    }

    private static DocumentaryExportPayload Copy(DocumentaryExportPayload p,IReadOnlyList<DocumentaryExportPayloadDependency> dependencies)=>new(p.PayloadId,p.PayloadType,p.ContentType,p.SerializerProfile,p.CharacterEncoding,p.SourceItemId,p.ArtifactIdentity,p.ArtifactVersion,p.Sequence,dependencies,p.Content,Encoding.UTF8.GetBytes(p.Content),p.CharacterCount,p.ByteCount,p.CorrelationId);
}
