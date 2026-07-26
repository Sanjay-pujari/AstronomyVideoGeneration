using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryExportSpecificationInventoryTests
{
    [Fact] public void Exact_enum_inventories_are_certified()
    {
        Assert.Equal(["Complete","Rejected"],Enum.GetNames<DocumentaryExportSpecificationStatus>());
        Assert.Equal(["CertificationRecordNotCertified","CertificationIdentityMismatch","PackageIdentityMismatch","ProvenanceIdentityMismatch","CorrelationMismatch","ExportPolicyRejected","ExportProfileRejected","RequiredItemMissing","ItemInventoryMismatch","ItemOrderMismatch","ItemIdentityMismatch","ItemDependencyMismatch","UnsupportedItemPresent"],Enum.GetNames<DocumentaryExportSpecificationRejectionReason>());
        Assert.Equal(["CertifiedKnowledgePackage"],Enum.GetNames<DocumentaryExportProfile>());
        Assert.Equal(["AcceptedNarrative","FinalValidationEvidence","RevisionHistory","ConvergenceEvidence","AcceptanceEvidence","ProductionPackageManifest","ProvenanceRecord","CertificationDecision","CertificationRecord","ExportManifest"],Enum.GetNames<DocumentaryExportItemType>());
        Assert.Equal(["Required"],Enum.GetNames<DocumentaryExportItemRequirement>());
        Assert.Equal(["DocumentaryNarrative","DocumentaryValidationEvidence","DocumentaryRevisionHistory","DocumentaryConvergenceEvidence","DocumentaryAcceptanceEvidence","DocumentaryPackageManifest","DocumentaryProvenance","DocumentaryCertificationDecision","DocumentaryCertificationRecord","DocumentaryExportManifest"],Enum.GetNames<DocumentaryExportContentType>());
        Assert.Equal(["StructuredJson"],Enum.GetNames<DocumentaryExportEncoding>());
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void Exact_read_only_property_inventory_is_certified(Type type,string[] expected)
    {
        var properties=type.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
        Assert.Equal(expected,properties.Select(x=>x.Name));Assert.All(properties,x=>Assert.Null(x.SetMethod));
    }
    public static TheoryData<Type,string[]> Properties=>new()
    {
        {typeof(DocumentaryExportSpecificationPolicy),["RequireCertifiedRecord","RequireCompleteProductionPackage","RequireCompleteProvenance","RequireCanonicalItems","RequireCanonicalOrdering","RequireExactCorrelation","RequireDeterministicIdentity","RequireDeterministicDependencies","RequiredItemTypes","RequiredContentTypes","ExportEncoding","PolicySchemaVersion"]},
        {typeof(DocumentaryExportSpecificationMetadata),["CreatedUtc","CreatedBy","ExportSchemaVersion","CorrelationId"]},
        {typeof(DocumentaryExportSpecificationRequest),["CertificationRecord","Policy","Metadata","Profile"]},
        {typeof(DocumentaryExportItemDependency),["DependencyId","SourceItemType","TargetItemType","Sequence","CorrelationId"]},
        {typeof(DocumentaryExportSpecificationItem),["ItemId","ItemType","Requirement","ContentType","Encoding","ArtifactIdentity","ArtifactVersion","Sequence","Dependencies","CorrelationId"]},
        {typeof(DocumentaryExportSpecificationManifest),["ManifestId","ExportSpecificationId","Profile","Items","ItemCount","RequiredItemCount","Encoding","ManifestSchemaVersion","CorrelationId"]},
        {typeof(DocumentaryExportSpecification),["ExportSpecificationId","CertificationRecord","ProvenanceRecord","ProductionPackage","Profile","Policy","Metadata","Items","Manifest","CertificationId","ProvenanceId","PackageId","ReleaseCandidateId","ConvergenceId","ItemCount","RequiredItemCount","IsComplete"]},
        {typeof(DocumentaryExportSpecificationBuildResult),["Status","RejectionReasons","ExportSpecification","HasExportSpecification","IsComplete","IsRejected"]},
        {typeof(DocumentaryExportSpecificationSummary),["ExportSpecificationId","ManifestId","CertificationId","ProvenanceId","PackageId","ReleaseCandidateId","ConvergenceId","Profile","Encoding","ItemCount","RequiredItemCount","DependencyCount","ItemTypes","ContentTypes","CreatedUtc","CreatedBy","IsComplete"]}
    };
}
