using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class DocumentaryExportSpecificationFixture
{
    internal const string Correlation = "orion-documentary-correlation";
    internal static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-07-26T18:19:20.1234567-04:00");
    internal const string Creator = " export certifier ";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static DocumentaryCertificationRecord CertifiedRecord(int completedCycleCount)
    {
        var candidate=completedCycleCount switch { 0=>OrionDocumentaryNarrativeAcceptanceFixture.InitiallyCleanReleaseCandidate(), 1=>OrionDocumentaryNarrativeAcceptanceFixture.OneCycleReleaseCandidate(), _=>OrionDocumentaryNarrativeAcceptanceFixture.MultiCycleReleaseCandidate() };
        var packagePolicy=new DocumentaryProductionPackagePolicy(true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProductionPackageSection>(),"1.0");
        var packageMetadata=new DocumentaryProductionPackageMetadata(Timestamp," package creator ","1.0",candidate.Metadata.CorrelationId);
        var package=Assert.IsType<DocumentaryProductionPackage>(new DocumentaryProductionPackageAssembler().Assemble(new(candidate,packagePolicy,packageMetadata)).Package);
        var provenancePolicy=new DocumentaryProvenancePolicy(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryProvenanceArtifactType>(),Enum.GetValues<DocumentaryProvenanceRelationshipType>(),"1.0");
        var provenanceMetadata=new DocumentaryProvenanceMetadata(Timestamp," provenance creator ","1.0",package.Metadata.CorrelationId);
        var provenance=Assert.IsType<DocumentaryProvenanceRecord>(new DocumentaryProvenanceBuilder().Build(new(package,provenancePolicy,provenanceMetadata)).ProvenanceRecord);
        var certificationPolicy=new DocumentaryCertificationPolicy(true,true,true,true,true,true,true,true,true,true,true,true,true,true,DocumentaryCertificationInventory.Domains,DocumentaryCertificationInventory.Rules,"1.0");
        var metadata=new DocumentaryCertificationMetadata(Timestamp," certification creator ","1.0",provenance.Metadata.CorrelationId);
        var upstream=DocumentaryCertificationInventory.Objectives.Select((x,i)=>new DocumentaryUpstreamCertificationEvidence(x,"1.0",true,i,metadata.CorrelationId)).ToArray();
        var docs=DocumentaryCertificationInventory.DocumentIds.Select((x,i)=>new DocumentaryCertificationDocumentationEvidence(x,"1.0",DocumentaryCertificationInventory.Statements[i],i,metadata.CorrelationId)).ToArray();
        return Assert.IsType<DocumentaryCertificationRecord>(new DocumentaryCertificationEvaluator().Evaluate(new(provenance,certificationPolicy,metadata,upstream,docs)).CertificationRecord);
    }

    internal static DocumentaryExportSpecificationPolicy Policy()=>new(true,true,true,true,true,true,true,true,Enum.GetValues<DocumentaryExportItemType>(),Enum.GetValues<DocumentaryExportContentType>(),DocumentaryExportEncoding.StructuredJson,"1.0");
    internal static DocumentaryExportSpecificationMetadata Metadata(string correlationId)=>new(Timestamp,Creator,"1.0",correlationId);
    internal static DocumentaryExportSpecificationRequest Request(int cycles){var record=CertifiedRecord(cycles);return new(record,Policy(),Metadata(record.Metadata.CorrelationId),DocumentaryExportProfile.CertifiedKnowledgePackage);}
    internal static DocumentaryExportSpecificationBuildResult Build(int cycles)=>new DocumentaryExportSpecificationBuilder().Build(Request(cycles));
    internal static DocumentaryExportSpecification Specification(int cycles)=>Assert.IsType<DocumentaryExportSpecification>(Build(cycles).ExportSpecification);
    internal static DocumentaryExportSpecificationSummary Summary(int cycles)=>new DocumentaryExportSpecificationSummarizer().Summarize(Specification(cycles));
    internal static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Json);
    internal static T RoundTrip<T>(T value)=>Assert.IsType<T>(JsonSerializer.Deserialize<T>(Serialize(value),Json));
}
