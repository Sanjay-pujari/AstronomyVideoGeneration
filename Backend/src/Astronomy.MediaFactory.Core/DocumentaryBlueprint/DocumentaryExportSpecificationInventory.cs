using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryExportSpecificationStatus { Complete, Rejected }
public enum DocumentaryExportSpecificationRejectionReason { CertificationRecordNotCertified, CertificationIdentityMismatch, PackageIdentityMismatch, ProvenanceIdentityMismatch, CorrelationMismatch, ExportPolicyRejected, ExportProfileRejected, RequiredItemMissing, ItemInventoryMismatch, ItemOrderMismatch, ItemIdentityMismatch, ItemDependencyMismatch, UnsupportedItemPresent }
public enum DocumentaryExportProfile { CertifiedKnowledgePackage }
public enum DocumentaryExportItemType { AcceptedNarrative, FinalValidationEvidence, RevisionHistory, ConvergenceEvidence, AcceptanceEvidence, ProductionPackageManifest, ProvenanceRecord, CertificationDecision, CertificationRecord, ExportManifest }
public enum DocumentaryExportItemRequirement { Required }
public enum DocumentaryExportContentType { DocumentaryNarrative, DocumentaryValidationEvidence, DocumentaryRevisionHistory, DocumentaryConvergenceEvidence, DocumentaryAcceptanceEvidence, DocumentaryPackageManifest, DocumentaryProvenance, DocumentaryCertificationDecision, DocumentaryCertificationRecord, DocumentaryExportManifest }
public enum DocumentaryExportEncoding { StructuredJson }

internal static class DocumentaryExportSpecificationInventory
{
    internal const string Schema = "1.0";
    internal static readonly DocumentaryExportItemType[] Items = Enum.GetValues<DocumentaryExportItemType>();
    internal static readonly DocumentaryExportContentType[] Contents = Enum.GetValues<DocumentaryExportContentType>();
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, string name) { ArgumentNullException.ThrowIfNull(source, name); return new ReadOnlyCollection<T>(source.ToArray()); }
    internal static bool Eq(string left,string right)=>string.Equals(left,right,StringComparison.Ordinal);
}
