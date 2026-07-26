using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryProvenanceStatus { Complete, Rejected }
public enum DocumentaryProvenanceRejectionReason
{
    ProductionPackageNotComplete, PackageIdentityMismatch, ManifestIdentityMismatch,
    ArtifactInventoryMismatch, RelationshipInventoryMismatch, DraftLineageMismatch,
    ValidationLineageMismatch, RevisionLineageMismatch, ConvergenceLineageMismatch,
    AcceptanceLineageMismatch, ReleaseCandidateLineageMismatch, CorrelationMismatch,
    RequiredNodeMissing, RequiredEdgeMissing, PolicyRejected
}
public enum DocumentaryProvenanceArtifactType
{
    OriginalNarrativeDraft, OriginalValidationResult, RevisionCycle, RevisedNarrativeDraft,
    RevisedValidationResult, ConvergenceState, AcceptanceDecision, NarrativeReleaseCandidate,
    ProductionPackageManifest, ProductionPackage
}
public enum DocumentaryProvenanceRelationshipType
{
    Validates, Revises, ProducesDraft, ProducesValidation, AdvancesConvergence,
    ConvergesTo, AcceptedBy, ProducesReleaseCandidate, ManifestDescribes, PackagedInto
}

internal static class DocumentaryProvenanceInventory
{
    internal const string Schema = "1.0";
    internal static readonly DocumentaryProvenanceArtifactType[] Artifacts = Enum.GetValues<DocumentaryProvenanceArtifactType>();
    internal static readonly DocumentaryProvenanceRelationshipType[] Relationships = Enum.GetValues<DocumentaryProvenanceRelationshipType>();
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, string name)
    { ArgumentNullException.ThrowIfNull(source, name); return new ReadOnlyCollection<T>(source.ToArray()); }
}

public sealed class DocumentaryProvenancePolicy
{
    public DocumentaryProvenancePolicy(bool requireCompleteProductionPackage, bool requireOriginalDraftLineage,
        bool requireValidationLineage, bool requireRevisionCycleLineage, bool requireConvergenceLineage,
        bool requireAcceptanceLineage, bool requireReleaseCandidateLineage, bool requireManifestLineage,
        IReadOnlyList<DocumentaryProvenanceArtifactType> requiredArtifactTypes,
        IReadOnlyList<DocumentaryProvenanceRelationshipType> requiredRelationshipTypes, string policySchemaVersion)
    {
        if (!requireCompleteProductionPackage || !requireOriginalDraftLineage || !requireValidationLineage ||
            !requireRevisionCycleLineage || !requireConvergenceLineage || !requireAcceptanceLineage ||
            !requireReleaseCandidateLineage || !requireManifestLineage) throw new ArgumentException("Policy 1.0 requires every lineage.");
        ArgumentNullException.ThrowIfNull(requiredArtifactTypes); ArgumentNullException.ThrowIfNull(requiredRelationshipTypes);
        if (!requiredArtifactTypes.SequenceEqual(DocumentaryProvenanceInventory.Artifacts)) throw new ArgumentException("Artifact inventory must be canonical.", nameof(requiredArtifactTypes));
        if (!requiredRelationshipTypes.SequenceEqual(DocumentaryProvenanceInventory.Relationships)) throw new ArgumentException("Relationship inventory must be canonical.", nameof(requiredRelationshipTypes));
        PolicySchemaVersion = policySchemaVersion == "1.0" ? policySchemaVersion : throw new ArgumentException("Policy schema must be 1.0.", nameof(policySchemaVersion));
        RequireCompleteProductionPackage=requireCompleteProductionPackage; RequireOriginalDraftLineage=requireOriginalDraftLineage;
        RequireValidationLineage=requireValidationLineage; RequireRevisionCycleLineage=requireRevisionCycleLineage;
        RequireConvergenceLineage=requireConvergenceLineage; RequireAcceptanceLineage=requireAcceptanceLineage;
        RequireReleaseCandidateLineage=requireReleaseCandidateLineage; RequireManifestLineage=requireManifestLineage;
        RequiredArtifactTypes=DocumentaryProvenanceInventory.Copy(requiredArtifactTypes,nameof(requiredArtifactTypes));
        RequiredRelationshipTypes=DocumentaryProvenanceInventory.Copy(requiredRelationshipTypes,nameof(requiredRelationshipTypes));
    }
    public bool RequireCompleteProductionPackage{get;} public bool RequireOriginalDraftLineage{get;} public bool RequireValidationLineage{get;}
    public bool RequireRevisionCycleLineage{get;} public bool RequireConvergenceLineage{get;} public bool RequireAcceptanceLineage{get;}
    public bool RequireReleaseCandidateLineage{get;} public bool RequireManifestLineage{get;}
    public IReadOnlyList<DocumentaryProvenanceArtifactType> RequiredArtifactTypes{get;}
    public IReadOnlyList<DocumentaryProvenanceRelationshipType> RequiredRelationshipTypes{get;} public string PolicySchemaVersion{get;}
}

public sealed class DocumentaryProvenanceMetadata
{
    public DocumentaryProvenanceMetadata(DateTimeOffset createdUtc,string createdBy,string provenanceSchemaVersion,string correlationId)
    { CreatedUtc=createdUtc!=default?createdUtc:throw new ArgumentException("A non-default timestamp is required.",nameof(createdUtc)); CreatedBy=Guard.Required(createdBy,nameof(createdBy)); ProvenanceSchemaVersion=provenanceSchemaVersion=="1.0"?provenanceSchemaVersion:throw new ArgumentException("Provenance schema must be 1.0.",nameof(provenanceSchemaVersion)); CorrelationId=Guard.Required(correlationId,nameof(correlationId)); }
    public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public string ProvenanceSchemaVersion{get;} public string CorrelationId{get;}
}
public sealed class DocumentaryProvenanceRequest
{
    public DocumentaryProvenanceRequest(DocumentaryProductionPackage productionPackage,DocumentaryProvenancePolicy policy,DocumentaryProvenanceMetadata metadata)
    { ProductionPackage=productionPackage??throw new ArgumentNullException(nameof(productionPackage));Policy=policy??throw new ArgumentNullException(nameof(policy));Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); }
    public DocumentaryProductionPackage ProductionPackage{get;} public DocumentaryProvenancePolicy Policy{get;} public DocumentaryProvenanceMetadata Metadata{get;}
}
public sealed class DocumentaryProvenanceArtifactNode
{
    public DocumentaryProvenanceArtifactNode(string nodeId,DocumentaryProvenanceArtifactType artifactType,string artifactIdentity,string artifactVersion,int sequence,string correlationId)
    { NodeId=Guard.Required(nodeId,nameof(nodeId));Guard.Enum(artifactType,nameof(artifactType));ArtifactIdentity=Guard.Required(artifactIdentity,nameof(artifactIdentity));ArtifactVersion=Guard.Required(artifactVersion,nameof(artifactVersion));if(sequence<0)throw new ArgumentOutOfRangeException(nameof(sequence));CorrelationId=Guard.Required(correlationId,nameof(correlationId));if(nodeId!=$"{artifactType}.{artifactIdentity}.{artifactVersion}")throw new ArgumentException("Node identity is inconsistent.",nameof(nodeId));ArtifactType=artifactType;Sequence=sequence; }
    public string NodeId{get;} public DocumentaryProvenanceArtifactType ArtifactType{get;} public string ArtifactIdentity{get;} public string ArtifactVersion{get;} public int Sequence{get;} public string CorrelationId{get;}
}
public sealed class DocumentaryProvenanceRelationshipEdge
{
    public DocumentaryProvenanceRelationshipEdge(string edgeId,DocumentaryProvenanceRelationshipType relationshipType,string sourceNodeId,string targetNodeId,int sequence,string correlationId)
    { EdgeId=Guard.Required(edgeId,nameof(edgeId));Guard.Enum(relationshipType,nameof(relationshipType));SourceNodeId=Guard.Required(sourceNodeId,nameof(sourceNodeId));TargetNodeId=Guard.Required(targetNodeId,nameof(targetNodeId));if(sourceNodeId==targetNodeId)throw new ArgumentException("Endpoints must differ.");if(sequence<0)throw new ArgumentOutOfRangeException(nameof(sequence));CorrelationId=Guard.Required(correlationId,nameof(correlationId));if(edgeId!=$"{relationshipType}.{sourceNodeId}.to.{targetNodeId}")throw new ArgumentException("Edge identity is inconsistent.",nameof(edgeId));RelationshipType=relationshipType;Sequence=sequence; }
    public string EdgeId{get;} public DocumentaryProvenanceRelationshipType RelationshipType{get;} public string SourceNodeId{get;} public string TargetNodeId{get;} public int Sequence{get;} public string CorrelationId{get;}
}

public sealed class DocumentaryProvenanceRecord
{
    public DocumentaryProvenanceRecord(string provenanceId,DocumentaryProductionPackage productionPackage,IReadOnlyList<DocumentaryProvenanceArtifactNode> artifactNodes,IReadOnlyList<DocumentaryProvenanceRelationshipEdge> relationshipEdges,DocumentaryProvenancePolicy policy,DocumentaryProvenanceMetadata metadata,string packageId,string manifestId,string releaseCandidateId,string convergenceId,string originalDraftId,string originalDraftVersion,string currentDraftId,string currentDraftVersion,int completedCycleCount,bool isComplete)
    { ProductionPackage=productionPackage??throw new ArgumentNullException(nameof(productionPackage));Policy=policy??throw new ArgumentNullException(nameof(policy));Metadata=metadata??throw new ArgumentNullException(nameof(metadata));ArtifactNodes=DocumentaryProvenanceInventory.Copy(artifactNodes,nameof(artifactNodes));RelationshipEdges=DocumentaryProvenanceInventory.Copy(relationshipEdges,nameof(relationshipEdges));ProvenanceId=Guard.Required(provenanceId,nameof(provenanceId));PackageId=Guard.Required(packageId,nameof(packageId));ManifestId=Guard.Required(manifestId,nameof(manifestId));ReleaseCandidateId=Guard.Required(releaseCandidateId,nameof(releaseCandidateId));ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId));OriginalDraftId=Guard.Required(originalDraftId,nameof(originalDraftId));OriginalDraftVersion=Guard.Required(originalDraftVersion,nameof(originalDraftVersion));CurrentDraftId=Guard.Required(currentDraftId,nameof(currentDraftId));CurrentDraftVersion=Guard.Required(currentDraftVersion,nameof(currentDraftVersion));CompletedCycleCount=completedCycleCount;if(!isComplete)throw new ArgumentException("Record must be complete.",nameof(isComplete));DocumentaryProvenanceValidator.ValidateRecord(this);IsComplete=true; }
    public string ProvenanceId{get;} public DocumentaryProductionPackage ProductionPackage{get;} public IReadOnlyList<DocumentaryProvenanceArtifactNode> ArtifactNodes{get;} public IReadOnlyList<DocumentaryProvenanceRelationshipEdge> RelationshipEdges{get;} public DocumentaryProvenancePolicy Policy{get;} public DocumentaryProvenanceMetadata Metadata{get;} public string PackageId{get;} public string ManifestId{get;} public string ReleaseCandidateId{get;} public string ConvergenceId{get;} public string OriginalDraftId{get;} public string OriginalDraftVersion{get;} public string CurrentDraftId{get;} public string CurrentDraftVersion{get;} public int CompletedCycleCount{get;} public int ArtifactNodeCount=>ArtifactNodes.Count; public int RelationshipEdgeCount=>RelationshipEdges.Count; public bool IsComplete{get;}
}

public sealed class DocumentaryProvenanceBuildResult
{
    public DocumentaryProvenanceBuildResult(DocumentaryProvenanceStatus status,IReadOnlyList<DocumentaryProvenanceRejectionReason> rejectionReasons,DocumentaryProvenanceRecord? provenanceRecord)
    { Guard.Enum(status,nameof(status));ArgumentNullException.ThrowIfNull(rejectionReasons);if(rejectionReasons.Any(x=>!Enum.IsDefined(x))||rejectionReasons.Distinct().Count()!=rejectionReasons.Count||!rejectionReasons.SequenceEqual(rejectionReasons.OrderBy(x=>(int)x)))throw new ArgumentException("Reasons must be defined, unique, and ordered.",nameof(rejectionReasons));if(status==DocumentaryProvenanceStatus.Complete?(rejectionReasons.Count!=0||provenanceRecord is null):(rejectionReasons.Count==0||provenanceRecord is not null))throw new ArgumentException("Build outcome is inconsistent.");Status=status;RejectionReasons=DocumentaryProvenanceInventory.Copy(rejectionReasons,nameof(rejectionReasons));ProvenanceRecord=provenanceRecord; }
    public DocumentaryProvenanceStatus Status{get;} public IReadOnlyList<DocumentaryProvenanceRejectionReason> RejectionReasons{get;} public DocumentaryProvenanceRecord? ProvenanceRecord{get;} public bool HasProvenanceRecord=>ProvenanceRecord is not null; public bool IsComplete=>Status==DocumentaryProvenanceStatus.Complete; public bool IsRejected=>Status==DocumentaryProvenanceStatus.Rejected;
}

public sealed class DocumentaryProvenanceSummary
{
    public DocumentaryProvenanceSummary(string provenanceId,string packageId,string manifestId,string releaseCandidateId,string convergenceId,string originalDraftId,string originalDraftVersion,string currentDraftId,string currentDraftVersion,int completedCycleCount,int artifactNodeCount,int relationshipEdgeCount,IReadOnlyList<DocumentaryProvenanceArtifactType> artifactTypes,IReadOnlyList<DocumentaryProvenanceRelationshipType> relationshipTypes,DateTimeOffset createdUtc,string createdBy,bool isComplete)
    { ProvenanceId=Guard.Required(provenanceId,nameof(provenanceId));PackageId=Guard.Required(packageId,nameof(packageId));ManifestId=Guard.Required(manifestId,nameof(manifestId));ReleaseCandidateId=Guard.Required(releaseCandidateId,nameof(releaseCandidateId));ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId));OriginalDraftId=Guard.Required(originalDraftId,nameof(originalDraftId));OriginalDraftVersion=Guard.Required(originalDraftVersion,nameof(originalDraftVersion));CurrentDraftId=Guard.Required(currentDraftId,nameof(currentDraftId));CurrentDraftVersion=Guard.Required(currentDraftVersion,nameof(currentDraftVersion));if(completedCycleCount<0||artifactNodeCount!=7+3*completedCycleCount||relationshipEdgeCount!=6+5*completedCycleCount)throw new ArgumentException("Summary counts are inconsistent.");ArtifactTypes=DocumentaryProvenanceInventory.Copy(artifactTypes,nameof(artifactTypes));RelationshipTypes=DocumentaryProvenanceInventory.Copy(relationshipTypes,nameof(relationshipTypes));if(ArtifactTypes.Distinct().Count()!=ArtifactTypes.Count||RelationshipTypes.Distinct().Count()!=RelationshipTypes.Count)throw new ArgumentException("Summary inventories must be distinct.");if(createdUtc==default)throw new ArgumentException("A non-default timestamp is required.",nameof(createdUtc));CreatedBy=Guard.Required(createdBy,nameof(createdBy));if(!isComplete)throw new ArgumentException("Summary must be complete.",nameof(isComplete));CompletedCycleCount=completedCycleCount;ArtifactNodeCount=artifactNodeCount;RelationshipEdgeCount=relationshipEdgeCount;CreatedUtc=createdUtc;IsComplete=true; }
    public string ProvenanceId{get;} public string PackageId{get;} public string ManifestId{get;} public string ReleaseCandidateId{get;} public string ConvergenceId{get;} public string OriginalDraftId{get;} public string OriginalDraftVersion{get;} public string CurrentDraftId{get;} public string CurrentDraftVersion{get;} public int CompletedCycleCount{get;} public int ArtifactNodeCount{get;} public int RelationshipEdgeCount{get;} public IReadOnlyList<DocumentaryProvenanceArtifactType> ArtifactTypes{get;} public IReadOnlyList<DocumentaryProvenanceRelationshipType> RelationshipTypes{get;} public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public bool IsComplete{get;}
}
