using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryExportMaterializationStatus { Complete, Rejected }
public enum DocumentaryExportMaterializationRejectionReason { ExportSpecificationNotComplete, ExportSpecificationIdentityMismatch, ExportManifestIdentityMismatch, CertificationIdentityMismatch, ProvenanceIdentityMismatch, PackageIdentityMismatch, CorrelationMismatch, MaterializationPolicyRejected, SerializerProfileRejected, RequiredPayloadMissing, PayloadInventoryMismatch, PayloadOrderMismatch, PayloadIdentityMismatch, PayloadContentMismatch, PayloadDependencyMismatch, UnsupportedPayloadPresent }
public enum DocumentaryExportSerializerProfile { CanonicalWebJson }
public enum DocumentaryExportPayloadType { AcceptedNarrative, FinalValidationEvidence, RevisionHistory, ConvergenceEvidence, AcceptanceEvidence, ProductionPackageManifest, ProvenanceRecord, CertificationDecision, CertificationRecord, ExportManifest }
public enum DocumentaryExportPayloadContentType { DocumentaryNarrativeJson, DocumentaryValidationEvidenceJson, DocumentaryRevisionHistoryJson, DocumentaryConvergenceEvidenceJson, DocumentaryAcceptanceEvidenceJson, DocumentaryPackageManifestJson, DocumentaryProvenanceJson, DocumentaryCertificationDecisionJson, DocumentaryCertificationRecordJson, DocumentaryExportManifestJson }
public enum DocumentaryExportCharacterEncoding { Utf8 }

internal static class DocumentaryExportMaterializationInventory
{
    internal const string Schema="1.0";
    internal static readonly DocumentaryExportPayloadType[] PayloadTypes=Enum.GetValues<DocumentaryExportPayloadType>();
    internal static readonly DocumentaryExportPayloadContentType[] ContentTypes=Enum.GetValues<DocumentaryExportPayloadContentType>();
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source,string name){ArgumentNullException.ThrowIfNull(source,name);return new ReadOnlyCollection<T>(source.ToArray());}
    internal static bool Eq(string? left,string? right)=>string.Equals(left,right,StringComparison.Ordinal);
    internal static DocumentaryExportPayloadContentType PayloadContentTypeFor(DocumentaryExportPayloadType type){Guard.Enum(type,nameof(type));return (DocumentaryExportPayloadContentType)(int)type;}
    internal static DocumentaryExportPayloadContentType ContentTypeFor(DocumentaryExportPayloadType type)=>PayloadContentTypeFor(type);
    internal static IReadOnlyList<DocumentaryExportPayloadType> DependencyTargetsFor(DocumentaryExportPayloadType type)
    {
        Guard.Enum(type,nameof(type));
        DocumentaryExportPayloadType[][] targets=[[],[DocumentaryExportPayloadType.AcceptedNarrative],[DocumentaryExportPayloadType.AcceptedNarrative],
            [DocumentaryExportPayloadType.FinalValidationEvidence,DocumentaryExportPayloadType.RevisionHistory],[DocumentaryExportPayloadType.ConvergenceEvidence],
            [DocumentaryExportPayloadType.AcceptedNarrative,DocumentaryExportPayloadType.FinalValidationEvidence,DocumentaryExportPayloadType.RevisionHistory,DocumentaryExportPayloadType.ConvergenceEvidence,DocumentaryExportPayloadType.AcceptanceEvidence],
            [DocumentaryExportPayloadType.ProductionPackageManifest],[DocumentaryExportPayloadType.ProvenanceRecord],
            [DocumentaryExportPayloadType.ProvenanceRecord,DocumentaryExportPayloadType.CertificationDecision],
            [DocumentaryExportPayloadType.AcceptedNarrative,DocumentaryExportPayloadType.FinalValidationEvidence,DocumentaryExportPayloadType.RevisionHistory,DocumentaryExportPayloadType.ConvergenceEvidence,DocumentaryExportPayloadType.AcceptanceEvidence,DocumentaryExportPayloadType.ProductionPackageManifest,DocumentaryExportPayloadType.ProvenanceRecord,DocumentaryExportPayloadType.CertificationDecision,DocumentaryExportPayloadType.CertificationRecord]];
        return Array.AsReadOnly(targets[(int)type]);
    }
}
