using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportSpecificationContractTests
{
    [Fact] public void Policy_enforces_every_control_and_canonical_inventory()
    {
        var items=Enum.GetValues<DocumentaryExportItemType>();var contents=Enum.GetValues<DocumentaryExportContentType>();
        for(var falseIndex=0;falseIndex<8;falseIndex++){var b=Enumerable.Repeat(true,8).ToArray();b[falseIndex]=false;Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(b[0],b[1],b[2],b[3],b[4],b[5],b[6],b[7],items,contents,DocumentaryExportEncoding.StructuredJson,"1.0"));}
        Assert.ThrowsAny<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(true,true,true,true,true,true,true,true,null!,contents,DocumentaryExportEncoding.StructuredJson,"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(true,true,true,true,true,true,true,true,items.Reverse().ToArray(),contents,DocumentaryExportEncoding.StructuredJson,"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(true,true,true,true,true,true,true,true,items,contents.Reverse().ToArray(),DocumentaryExportEncoding.StructuredJson,"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(true,true,true,true,true,true,true,true,items,contents,(DocumentaryExportEncoding)99,"1.0"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationPolicy(true,true,true,true,true,true,true,true,items,contents,DocumentaryExportEncoding.StructuredJson,"2.0"));
        var policy=DocumentaryExportSpecificationFixture.Policy();items[0]=items[1];contents[0]=contents[1];Assert.Equal(DocumentaryExportItemType.AcceptedNarrative,policy.RequiredItemTypes[0]);Assert.Equal(DocumentaryExportContentType.DocumentaryNarrative,policy.RequiredContentTypes[0]);
    }

    [Fact] public void Metadata_and_request_retain_exact_values_and_references()
    {
        var record=DocumentaryExportSpecificationFixture.CertifiedRecord(0);var policy=DocumentaryExportSpecificationFixture.Policy();var metadata=DocumentaryExportSpecificationFixture.Metadata(record.Metadata.CorrelationId);
        Assert.Equal(TimeSpan.FromHours(-4),metadata.CreatedUtc.Offset);Assert.Equal(1234567,metadata.CreatedUtc.Ticks%TimeSpan.TicksPerSecond);Assert.Equal(" export certifier ",metadata.CreatedBy);
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationMetadata(default,"x","1.0","c"));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationMetadata(metadata.CreatedUtc," ","1.0","c"));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationMetadata(metadata.CreatedUtc,"x","2.0","c"));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationMetadata(metadata.CreatedUtc,"x","1.0"," "));
        var request=new DocumentaryExportSpecificationRequest(record,policy,metadata,DocumentaryExportProfile.CertifiedKnowledgePackage);Assert.Same(record,request.CertificationRecord);Assert.Same(policy,request.Policy);Assert.Same(metadata,request.Metadata);
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryExportSpecificationRequest(null!,policy,metadata,DocumentaryExportProfile.CertifiedKnowledgePackage));Assert.Throws<ArgumentNullException>(()=>new DocumentaryExportSpecificationRequest(record,null!,metadata,DocumentaryExportProfile.CertifiedKnowledgePackage));Assert.Throws<ArgumentNullException>(()=>new DocumentaryExportSpecificationRequest(record,policy,null!,DocumentaryExportProfile.CertifiedKnowledgePackage));Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentaryExportSpecificationRequest(record,policy,metadata,(DocumentaryExportProfile)99));
    }

    [Fact] public void Dependency_and_item_contracts_are_strict_and_defensive()
    {
        const string c="correlation";var source=DocumentaryExportItemType.FinalValidationEvidence;var dependency=new DocumentaryExportItemDependency($"{source}.depends-on.AcceptedNarrative",source,DocumentaryExportItemType.AcceptedNarrative,0,c);
        Assert.Equal(source,dependency.SourceItemType);Assert.Throws<ArgumentException>(()=>new DocumentaryExportItemDependency("wrong",source,DocumentaryExportItemType.AcceptedNarrative,0,c));Assert.Throws<ArgumentException>(()=>new DocumentaryExportItemDependency(dependency.DependencyId,source,source,0,c));Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentaryExportItemDependency(dependency.DependencyId,source,DocumentaryExportItemType.AcceptedNarrative,-1,c));
        var deps=new[]{dependency};var item=new DocumentaryExportSpecificationItem($"{source}.artifact.1",source,DocumentaryExportItemRequirement.Required,DocumentaryExportContentType.DocumentaryValidationEvidence,DocumentaryExportEncoding.StructuredJson,"artifact","1",1,deps,c);deps[0]=null!;Assert.Same(dependency,item.Dependencies[0]);Assert.ThrowsAny<Exception>(()=>((IList<DocumentaryExportItemDependency>)item.Dependencies).Add(dependency));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationItem("wrong",source,DocumentaryExportItemRequirement.Required,DocumentaryExportContentType.DocumentaryValidationEvidence,DocumentaryExportEncoding.StructuredJson,"artifact","1",1,[dependency],c));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationItem($"{source}.artifact.1",source,DocumentaryExportItemRequirement.Required,DocumentaryExportContentType.DocumentaryNarrative,DocumentaryExportEncoding.StructuredJson,"artifact","1",1,[dependency],c));
    }

    [Fact] public void Build_result_enforces_status_reason_specification_matrix()
    {
        var specification=DocumentaryExportSpecificationFixture.Specification(0);var complete=new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Complete,[],specification);Assert.True(complete.IsComplete);Assert.True(complete.HasExportSpecification);Assert.False(complete.IsRejected);
        var rejected=new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Rejected,[DocumentaryExportSpecificationRejectionReason.CorrelationMismatch],null);Assert.True(rejected.IsRejected);Assert.False(rejected.HasExportSpecification);
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Complete,[DocumentaryExportSpecificationRejectionReason.CorrelationMismatch],specification));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Complete,[],null));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Rejected,[],null));Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Rejected,[DocumentaryExportSpecificationRejectionReason.CorrelationMismatch],specification));
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Rejected,[DocumentaryExportSpecificationRejectionReason.CorrelationMismatch,DocumentaryExportSpecificationRejectionReason.CertificationRecordNotCertified],null));
    }

    [Fact] public void All_nine_contracts_have_byte_identical_web_json_round_trips()
    {
        var specification=DocumentaryExportSpecificationFixture.Specification(2);object[] contracts=[specification.Policy,specification.Metadata,new DocumentaryExportSpecificationRequest(specification.CertificationRecord,specification.Policy,specification.Metadata,specification.Profile),specification.Items[1].Dependencies[0],specification.Items[1],specification.Manifest,specification,new DocumentaryExportSpecificationBuildResult(DocumentaryExportSpecificationStatus.Complete,[],specification),new DocumentaryExportSpecificationSummarizer().Summarize(specification)];
        foreach(var value in contracts){var json=JsonSerializer.Serialize(value,value.GetType(),DocumentaryExportSpecificationFixture.Json);var copy=JsonSerializer.Deserialize(json,value.GetType(),DocumentaryExportSpecificationFixture.Json);Assert.Equal(json,JsonSerializer.Serialize(copy,value.GetType(),DocumentaryExportSpecificationFixture.Json));}
    }
}
