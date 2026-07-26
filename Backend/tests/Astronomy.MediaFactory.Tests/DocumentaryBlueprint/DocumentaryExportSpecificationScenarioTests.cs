using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportSpecificationScenarioTests
{
    private static readonly DocumentaryExportItemType[][] Targets=[[],[DocumentaryExportItemType.AcceptedNarrative],[DocumentaryExportItemType.AcceptedNarrative],[DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory],[DocumentaryExportItemType.ConvergenceEvidence],[DocumentaryExportItemType.AcceptedNarrative,DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory,DocumentaryExportItemType.ConvergenceEvidence,DocumentaryExportItemType.AcceptanceEvidence],[DocumentaryExportItemType.ProductionPackageManifest],[DocumentaryExportItemType.ProvenanceRecord],[DocumentaryExportItemType.ProvenanceRecord,DocumentaryExportItemType.CertificationDecision],[DocumentaryExportItemType.AcceptedNarrative,DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory,DocumentaryExportItemType.ConvergenceEvidence,DocumentaryExportItemType.AcceptanceEvidence,DocumentaryExportItemType.ProductionPackageManifest,DocumentaryExportItemType.ProvenanceRecord,DocumentaryExportItemType.CertificationDecision,DocumentaryExportItemType.CertificationRecord]];

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public void Zero_one_and_multi_cycle_graphs_are_complete_exact_and_canonical(int cycles)
    {
        var result=DocumentaryExportSpecificationFixture.Build(cycles);var s=Assert.IsType<DocumentaryExportSpecification>(result.ExportSpecification);
        Assert.Equal(DocumentaryExportSpecificationStatus.Complete,result.Status);Assert.Empty(result.RejectionReasons);Assert.True(s.IsComplete);Assert.Equal(10,s.ItemCount);Assert.Equal(10,s.RequiredItemCount);Assert.Equal(23,s.Items.Sum(x=>x.Dependencies.Count));Assert.Equal(10,s.Manifest.ItemCount);Assert.Equal(10,s.Manifest.RequiredItemCount);Assert.Equal(DocumentaryExportProfile.CertifiedKnowledgePackage,s.Profile);Assert.Equal(DocumentaryExportEncoding.StructuredJson,s.Manifest.Encoding);
        Assert.Equal(Enum.GetValues<DocumentaryExportItemType>(),s.Items.Select(x=>x.ItemType));Assert.Equal(Enumerable.Range(0,10),s.Items.Select(x=>x.Sequence));
        foreach(var item in s.Items){Assert.Equal(DocumentaryExportItemRequirement.Required,item.Requirement);Assert.Equal((DocumentaryExportContentType)(int)item.ItemType,item.ContentType);Assert.Equal(DocumentaryExportEncoding.StructuredJson,item.Encoding);Assert.Equal($"{item.ItemType}.{item.ArtifactIdentity}.{item.ArtifactVersion}",item.ItemId);Assert.Equal(s.Metadata.CorrelationId,item.CorrelationId);Assert.Equal(Targets[(int)item.ItemType],item.Dependencies.Select(x=>x.TargetItemType));Assert.Equal(Enumerable.Range(0,item.Dependencies.Count),item.Dependencies.Select(x=>x.Sequence));Assert.All(item.Dependencies,d=>{Assert.Equal(item.ItemType,d.SourceItemType);Assert.True((int)d.TargetItemType<(int)d.SourceItemType);Assert.NotEqual(d.SourceItemType,d.TargetItemType);Assert.Equal($"{d.SourceItemType}.depends-on.{d.TargetItemType}",d.DependencyId);Assert.Equal(item.CorrelationId,d.CorrelationId);});}
        Assert.Equal(cycles.ToString(),s.Items[(int)DocumentaryExportItemType.RevisionHistory].ArtifactVersion);Assert.Equal(23,s.Items.SelectMany(x=>x.Dependencies).Select(x=>x.DependencyId).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public void Builder_summarizer_and_reconstruction_are_deterministic_and_non_mutating(int cycles)
    {
        var request=DocumentaryExportSpecificationFixture.Request(cycles);var before=DocumentaryExportSpecificationFixture.Serialize(request);var first=new DocumentaryExportSpecificationBuilder().Build(request);Assert.Equal(before,DocumentaryExportSpecificationFixture.Serialize(request));
        var second=DocumentaryExportSpecificationFixture.Build(cycles);Assert.Equal(DocumentaryExportSpecificationFixture.Serialize(first),DocumentaryExportSpecificationFixture.Serialize(second));
        var specification=Assert.IsType<DocumentaryExportSpecification>(first.ExportSpecification);var specificationBefore=DocumentaryExportSpecificationFixture.Serialize(specification);var summary=new DocumentaryExportSpecificationSummarizer().Summarize(specification);Assert.Equal(specificationBefore,DocumentaryExportSpecificationFixture.Serialize(specification));Assert.Equal(DocumentaryExportSpecificationFixture.Serialize(summary),DocumentaryExportSpecificationFixture.Serialize(DocumentaryExportSpecificationFixture.Summary(cycles)));
    }

    [Fact] public void Correlation_case_and_whitespace_mismatches_are_rejected_without_mutation()
    {
        var record=DocumentaryExportSpecificationFixture.CertifiedRecord(0);foreach(var correlation in new[]{record.Metadata.CorrelationId.ToUpperInvariant()," "+record.Metadata.CorrelationId,record.Metadata.CorrelationId+" "}){var request=new DocumentaryExportSpecificationRequest(record,DocumentaryExportSpecificationFixture.Policy(),DocumentaryExportSpecificationFixture.Metadata(correlation),DocumentaryExportProfile.CertifiedKnowledgePackage);var before=DocumentaryExportSpecificationFixture.Serialize(request);var result=new DocumentaryExportSpecificationBuilder().Build(request);Assert.Equal([DocumentaryExportSpecificationRejectionReason.CorrelationMismatch],result.RejectionReasons);Assert.Null(result.ExportSpecification);Assert.Equal(before,DocumentaryExportSpecificationFixture.Serialize(request));}
    }

    [Fact] public void Summary_preserves_identity_inventory_timestamp_precision_and_creator()
    {
        var s=DocumentaryExportSpecificationFixture.Specification(1);var summary=new DocumentaryExportSpecificationSummarizer().Summarize(s);Assert.Equal(s.ExportSpecificationId,summary.ExportSpecificationId);Assert.Equal(s.Manifest.ManifestId,summary.ManifestId);Assert.Equal(23,summary.DependencyCount);Assert.Equal(Enum.GetValues<DocumentaryExportItemType>(),summary.ItemTypes);Assert.Equal(Enum.GetValues<DocumentaryExportContentType>(),summary.ContentTypes);Assert.Equal(TimeSpan.FromHours(-4),summary.CreatedUtc.Offset);Assert.Equal(1234567,summary.CreatedUtc.Ticks%TimeSpan.TicksPerSecond);Assert.Equal(" export certifier ",summary.CreatedBy);Assert.True(summary.IsComplete);
    }
}
