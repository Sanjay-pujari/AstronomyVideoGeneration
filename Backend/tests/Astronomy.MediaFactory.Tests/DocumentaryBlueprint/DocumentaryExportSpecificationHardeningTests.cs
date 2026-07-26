using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportSpecificationHardeningTests
{
    [Fact]
    public void Canonical_item_mappings_are_exact()
    {
        var types=Enum.GetValues<DocumentaryExportItemType>();
        Assert.Equal(10,types.Length);
        Assert.Equal(Enum.GetValues<DocumentaryExportContentType>(),types.Select(DocumentaryExportSpecificationInventory.ContentTypeFor));
        Assert.All(types,type=>Assert.Equal(DocumentaryExportItemRequirement.Required,DocumentaryExportSpecificationInventory.RequirementFor(type)));
        Assert.All(types,type=>Assert.Equal(DocumentaryExportEncoding.StructuredJson,DocumentaryExportSpecificationInventory.EncodingFor(type)));
    }

    [Fact]
    public void Canonical_dependency_mapping_contains_twenty_three_ordered_edges()
    {
        DocumentaryExportItemType[][] expected=[[],[DocumentaryExportItemType.AcceptedNarrative],[DocumentaryExportItemType.AcceptedNarrative],
            [DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory],[DocumentaryExportItemType.ConvergenceEvidence],
            [DocumentaryExportItemType.AcceptedNarrative,DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory,DocumentaryExportItemType.ConvergenceEvidence,DocumentaryExportItemType.AcceptanceEvidence],
            [DocumentaryExportItemType.ProductionPackageManifest],[DocumentaryExportItemType.ProvenanceRecord],
            [DocumentaryExportItemType.ProvenanceRecord,DocumentaryExportItemType.CertificationDecision],
            [DocumentaryExportItemType.AcceptedNarrative,DocumentaryExportItemType.FinalValidationEvidence,DocumentaryExportItemType.RevisionHistory,DocumentaryExportItemType.ConvergenceEvidence,DocumentaryExportItemType.AcceptanceEvidence,DocumentaryExportItemType.ProductionPackageManifest,DocumentaryExportItemType.ProvenanceRecord,DocumentaryExportItemType.CertificationDecision,DocumentaryExportItemType.CertificationRecord]];
        Assert.Equal(23,expected.Sum(x=>x.Length));
        Assert.All(Enum.GetValues<DocumentaryExportItemType>().Select((type,index)=>(type,index)),pair=>Assert.Equal(expected[pair.index],DocumentaryExportSpecificationInventory.DependencyTargetsFor(pair.type)));
    }

    [Fact]
    public void Item_contract_rejects_noncanonical_mapping_and_dependencies()
    {
        const string correlation="correlation";var type=DocumentaryExportItemType.FinalValidationEvidence;
        var dependency=new DocumentaryExportItemDependency($"{type}.depends-on.AcceptedNarrative",type,DocumentaryExportItemType.AcceptedNarrative,0,correlation);
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationItem($"{type}.artifact.1",type,DocumentaryExportItemRequirement.Required,DocumentaryExportContentType.DocumentaryNarrative,DocumentaryExportEncoding.StructuredJson,"artifact","1",1,[dependency],correlation));
        var wrongCorrelation=new DocumentaryExportItemDependency($"{type}.depends-on.AcceptedNarrative",type,DocumentaryExportItemType.AcceptedNarrative,0,"CORRELATION");
        Assert.Throws<ArgumentException>(()=>new DocumentaryExportSpecificationItem($"{type}.artifact.1",type,DocumentaryExportItemRequirement.Required,DocumentaryExportContentType.DocumentaryValidationEvidence,DocumentaryExportEncoding.StructuredJson,"artifact","1",1,[wrongCorrelation],correlation));
    }

    [Theory]
    [InlineData(typeof(DocumentaryExportSpecificationBuilder),"Build",typeof(DocumentaryExportSpecificationRequest),typeof(DocumentaryExportSpecificationBuildResult))]
    [InlineData(typeof(DocumentaryExportSpecificationSummarizer),"Summarize",typeof(DocumentaryExportSpecification),typeof(DocumentaryExportSpecificationSummary))]
    public void Public_operations_retain_the_synchronous_stateless_architecture(Type type,string name,Type parameter,Type result)
    {
        Assert.True(type.IsSealed);Assert.Empty(type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));
        Assert.Single(type.GetConstructors());var method=Assert.Single(type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly));
        Assert.Equal(name,method.Name);Assert.Equal(parameter,Assert.Single(method.GetParameters()).ParameterType);Assert.Equal(result,method.ReturnType);
        Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }
}
