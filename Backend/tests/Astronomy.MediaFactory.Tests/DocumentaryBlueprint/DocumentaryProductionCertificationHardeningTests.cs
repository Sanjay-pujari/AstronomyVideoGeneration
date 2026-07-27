using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryProductionCertificationHardeningTests
{
    public static IEnumerable<object[]> Scenarios()
    {
        yield return new object[]{DocumentaryMediaPipelineFixture.Orion()};
        yield return new object[]{DocumentaryMediaPipelineFixture.Leo()};
        yield return new object[]{DocumentaryMediaPipelineFixture.Conjunction()};
    }

    [Theory,MemberData(nameof(Scenarios))]
    public void Complete_scenarios_certify_four_traceable_verified_outputs(DocumentaryMediaProject project)
    {
        var result=DocumentaryProductionCertificationFixture.Certify(project);
        Assert.True(result.IsCertified);Assert.Empty(result.RejectionReasons);
        var record=Assert.IsType<DocumentaryProductionCertificationRecord>(result.CertificationRecord);
        Assert.Equal(DocumentaryProductionCertificationInventory.VariantTypes,record.VariantCertificationRecords.Select(x=>x.VariantType));
        Assert.Equal(4,record.VerifiedOutputCount);Assert.All(record.VariantCertificationRecords,x=>{Assert.True(x.IsOutputVerified);Assert.True(x.IsTraceabilityComplete);Assert.NotEmpty(x.TraceabilityLinks);});
        Assert.Equal(record.MediaProject.Variants.Sum(x=>x.Scenes.Sum(s=>s.KnowledgeReferences.Count+s.Narration.Sum(n=>n.KnowledgeReferences.Count)+s.SubtitleCues.Sum(c=>c.KnowledgeReferences.Count)+s.VisualPrompts.Sum(v=>v.KnowledgeReferences.Count))),record.TraceabilityLinkCount);
        Assert.Equal(Enum.GetValues<DocumentaryProductionTraceabilityType>(),record.VariantCertificationRecords.SelectMany(x=>x.TraceabilityLinks).Select(x=>x.TraceabilityType).Distinct());
        Assert.Equal(Enum.GetValues<CertificationEvidenceType>(),record.Evidence.EvidenceReferences.Select(x=>x.EvidenceType));
        Assert.All(record.Evidence.EvidenceReferences,x=>Assert.True(x.Verified));
        Assert.True(new DocumentaryProductionCertificationSummarizer().Summarize(record).IsCertified);
    }

    [Fact]
    public void Certification_is_deterministic_non_mutating_and_web_json_stable()
    {
        var request=DocumentaryProductionCertificationFixture.Request(DocumentaryMediaPipelineFixture.Orion());
        var before=JsonSerializer.Serialize(request,JsonSerializerOptions.Web);
        var first=new DocumentaryProductionCertifier().Certify(request);var second=new DocumentaryProductionCertifier().Certify(request);
        Assert.Equal(before,JsonSerializer.Serialize(request,JsonSerializerOptions.Web));
        Assert.Equal(JsonSerializer.Serialize(first,JsonSerializerOptions.Web),JsonSerializer.Serialize(second,JsonSerializerOptions.Web));
        var json=JsonSerializer.Serialize(first,JsonSerializerOptions.Web);
        var roundTrip=JsonSerializer.Deserialize<DocumentaryProductionCertificationResult>(json,JsonSerializerOptions.Web);
        Assert.Equal(json,JsonSerializer.Serialize(roundTrip,JsonSerializerOptions.Web));
        Assert.Contains(" O2.19 certifier ",json);Assert.Contains("+05:30",json);
    }

    [Fact]
    public void Correlation_mismatch_is_categorized_and_never_returns_a_record()
    {
        var valid=DocumentaryProductionCertificationFixture.Request(DocumentaryMediaPipelineFixture.Orion());
        var invalid=valid with { Metadata=new(valid.Metadata.CertifiedUtc,valid.Metadata.CertifiedBy,"different",valid.PipelineExecutionRecord.ExecutionId) };
        var result=new DocumentaryProductionCertifier().Certify(invalid);
        Assert.True(result.IsRejected);Assert.Null(result.CertificationRecord);Assert.Contains(DocumentaryProductionCertificationRejectionReason.CorrelationChainMismatch,result.RejectionReasons);
    }
}
