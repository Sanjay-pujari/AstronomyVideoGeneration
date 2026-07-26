namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryCertificationStatus { Certified, NonCompliant }
public enum DocumentaryCertificationSeverity { Error }
public enum DocumentaryCertificationDomain
{
    ProductionPackage, Provenance, Identity, Manifest, ArtifactInventory, RelationshipInventory,
    DraftLineage, ValidationLineage, RevisionLineage, ConvergenceLineage, AcceptanceLineage,
    ReleaseCandidateLineage, Correlation, Determinism, Serialization, Immutability,
    OperationBoundary, ForbiddenCapability, Documentation, UpstreamCertification
}
public enum DocumentaryCertificationRule
{
    ProductionPackageMustBeComplete, ProvenanceRecordMustBeComplete,
    PackageIdentityMustBeDeterministic, ManifestIdentityMustBeDeterministic,
    ProvenanceIdentityMustBeDeterministic, ManifestEntriesMustBeCanonical,
    ArtifactNodesMustBeCanonical, RelationshipEdgesMustBeCanonical,
    DraftLineageMustBeConsistent, ValidationLineageMustBeConsistent,
    RevisionLineageMustBeConsistent, ConvergenceLineageMustBeConsistent,
    AcceptanceLineageMustBeConsistent, ReleaseCandidateLineageMustBeConsistent,
    CorrelationChainMustBeExact, PackageMustBeDeterministicallyReconstructable,
    ProvenanceMustBeDeterministicallyReconstructable, CertifiedArtifactsMustBeImmutable,
    OperationsMustRespectCertifiedBoundaries, ForbiddenCapabilitiesMustBeAbsent,
    RequiredDocumentationMustBePresent, UpstreamCertificationMustBePreserved
}

internal static class DocumentaryCertificationInventory
{
    internal const string Schema = "1.0";
    internal static readonly DocumentaryCertificationDomain[] Domains = Enum.GetValues<DocumentaryCertificationDomain>();
    internal static readonly DocumentaryCertificationRule[] Rules = Enum.GetValues<DocumentaryCertificationRule>();
    private static readonly DocumentaryCertificationDomain[] RuleDomains = [
        DocumentaryCertificationDomain.ProductionPackage, DocumentaryCertificationDomain.Provenance,
        DocumentaryCertificationDomain.Identity, DocumentaryCertificationDomain.Manifest, DocumentaryCertificationDomain.Identity,
        DocumentaryCertificationDomain.Manifest, DocumentaryCertificationDomain.ArtifactInventory, DocumentaryCertificationDomain.RelationshipInventory,
        DocumentaryCertificationDomain.DraftLineage, DocumentaryCertificationDomain.ValidationLineage, DocumentaryCertificationDomain.RevisionLineage,
        DocumentaryCertificationDomain.ConvergenceLineage, DocumentaryCertificationDomain.AcceptanceLineage, DocumentaryCertificationDomain.ReleaseCandidateLineage,
        DocumentaryCertificationDomain.Correlation, DocumentaryCertificationDomain.Serialization, DocumentaryCertificationDomain.Serialization,
        DocumentaryCertificationDomain.Immutability, DocumentaryCertificationDomain.OperationBoundary, DocumentaryCertificationDomain.ForbiddenCapability,
        DocumentaryCertificationDomain.Documentation, DocumentaryCertificationDomain.UpstreamCertification];
    private static readonly string[] MessageCodes = [
        "CERT-PACKAGE-NOT-COMPLETE","CERT-PROVENANCE-NOT-COMPLETE","CERT-PACKAGE-IDENTITY","CERT-MANIFEST-IDENTITY","CERT-PROVENANCE-IDENTITY","CERT-MANIFEST-INVENTORY","CERT-ARTIFACT-INVENTORY","CERT-RELATIONSHIP-INVENTORY","CERT-DRAFT-LINEAGE","CERT-VALIDATION-LINEAGE","CERT-REVISION-LINEAGE","CERT-CONVERGENCE-LINEAGE","CERT-ACCEPTANCE-LINEAGE","CERT-RELEASE-LINEAGE","CERT-CORRELATION","CERT-PACKAGE-SERIALIZATION","CERT-PROVENANCE-SERIALIZATION","CERT-IMMUTABILITY","CERT-OPERATION-BOUNDARY","CERT-FORBIDDEN-CAPABILITY","CERT-DOCUMENTATION","CERT-UPSTREAM-CERTIFICATION"];
    internal static readonly DocumentaryCertificationDomain[] EvaluatedDomains =
        Rules.Select(DomainFor).Distinct().ToArray();
    internal static DocumentaryCertificationDomain DomainFor(DocumentaryCertificationRule rule)
    { Guard.Enum(rule,nameof(rule)); return RuleDomains[(int)rule]; }
    internal static string MessageCodeFor(DocumentaryCertificationRule rule)
    { Guard.Enum(rule,nameof(rule)); return MessageCodes[(int)rule]; }
    internal static readonly string[] Objectives = Enumerable.Range(1,13).Select(i=>$"O2.{i}").ToArray();
    internal static readonly string[] DocumentIds = [
        "documentary-narrative-acceptance-and-release-candidate", "documentary-production-package-foundation",
        "documentary-traceability-and-provenance-foundation", "documentary-certification-and-compliance-foundation"];
    internal static readonly string[][] Statements = [
        ["O2.11 does not call an AI model.","O2.11 does not construct prompts.","O2.11 does not publish narrative content.","O2.11 does not persist acceptance results."],
        ["O2.12 does not call an AI model.","O2.12 does not construct prompts.","O2.12 does not create files, archives, hashes, or signatures.","O2.12 does not persist production packages."],
        ["O2.13 does not call an AI model.","O2.13 does not construct prompts.","O2.13 does not use a graph database.","O2.13 does not persist provenance records."],
        ["O2.14 does not call an AI model.","O2.14 does not construct prompts.","O2.14 does not create hashes or digital signatures.","O2.14 does not persist certification records."]];
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values,string name)
    { ArgumentNullException.ThrowIfNull(values,name); return Array.AsReadOnly(values.ToArray()); }
    internal static bool Eq(string? a,string? b)=>string.Equals(a,b,StringComparison.Ordinal);
}
