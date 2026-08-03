namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class Phase7KnowledgeContract { public const string Version = "rc2-phase7-knowledge.v1"; }

public sealed record Phase7KnowledgeAuthority(
    string ContractVersion, string AuthorityId, string ExecutionId, string PlanId, string EventId,
    string EventFamily, string EventType, string Language, string ProfileId, string ProfileVersion,
    string SourcePhase6AuthorityId, string SourcePhase6AuthorityChecksum, string SourcePhase6IndexId,
    string SourcePhase6IndexChecksum, string SourcePhase4AggregateId, string SourcePhase4Checksum,
    string SourcePhase5PublicationId, string EventKnowledgePayloadId, string EventKnowledgeChecksum,
    string EventVerificationStatus, string EvergreenPayloadId, string EvergreenChecksum,
    string EvergreenReviewStatus, string EvergreenRelativePath, string SourceRegistryId,
    string SourceRegistryChecksum, IReadOnlyList<string> CanonicalDomains,
    IReadOnlyList<Phase7KnowledgeEntity> KnowledgeEntities, IReadOnlyList<CertifiedNarrationClaim> Claims,
    IReadOnlyList<CertifiedNarrationSource> Sources, IReadOnlyList<Phase7ClaimSupportEvidence> ClaimSupportEvidence,
    IReadOnlyList<Phase7KnowledgeAdapterDiagnostic> AdapterDiagnostics,
    IReadOnlyList<Phase7KnowledgeMergeDecision> MergeDecisions, Phase7SourceAuditSummary SourceAuditSummary,
    IReadOnlyList<string> UnknownSections, IReadOnlyList<string> UnknownProperties,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingIssues, string SemanticChecksum,
    IReadOnlyDictionary<string, string> RuntimeCompatibilityEvidence)
{
    public IReadOnlyList<string> MandatoryDomains { get; init; } = [];
    public IReadOnlyList<string> OptionalDomains { get; init; } = [];
}

public sealed record PublishedPhase7KnowledgeAuthority(
    Phase7KnowledgeAuthority KnowledgeAuthority, IReadOnlyList<string> ArtifactPaths,
    IReadOnlyDictionary<string,string> ArtifactSemanticChecksums,
    IReadOnlyDictionary<string,string> ArtifactPhysicalHashes, IReadOnlyDictionary<string,long> ArtifactSizes,
    IReadOnlyList<string> ValidationEvidence, IReadOnlyList<string> ManifestEvidence, string PublicationId,
    bool AlreadyPublished, bool PublicationCommitted, bool CommittedStateValidationPassed,
    IReadOnlyDictionary<string,string> ContractVersions,
    IReadOnlyDictionary<string,string> RuntimeCompatibilityEvidence);

public sealed record Phase7KnowledgeExecutionResult(bool IsValid, string Status, string ReasonCode,
    string OutputDirectory, string AuthorityId, bool AlreadyPublished, bool PublicationCommitted,
    bool CommittedStateValidationPassed, Phase7KnowledgeValidation? Validation,
    Phase7KnowledgeDiagnostics? Diagnostics, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record Phase7KnowledgeDiagnostics(
    string ContractVersion, string ExecutionId, string PlanId, string EventId, string AuthorityId,
    string EventFamily, string Language, string ProfileId, string ProfileVersion,
    bool EventPayloadLoaded, bool EvergreenPayloadLoaded, bool EventCertified, bool EvergreenCertified,
    bool SourceRegistryValid, bool SourceEligibilityValid, bool AdapterCoverageValid, bool ClaimIdentityValid,
    bool ClaimProvenanceValid, bool MandatoryDomainsSatisfied, bool MergeDecisionsValid,
    bool ContradictionFree, bool DiagnosticsReconciled, int KnowledgeEntityCount, int ExtractedCandidateCount,
    int AcceptedClaimCount, int DeferredClaimCount, int RejectedClaimCount, int RequiredClaimCount,
    int ExactClaimProvenanceCount, int ExactEntityProvenanceCount, int ExactFieldProvenanceCount,
    int UnsupportedClaimCount, int EquivalentMergeCount, int SpecializationMergeCount,
    int EventMorePreciseCount, int EvergreenMorePreciseCount, int ContradictionCount, int IncomparableCount,
    int AllSourceCount, int CertifiedSupportingSourceCount, int ReviewedNonCertifiedSourceCount,
    int RejectedSourceCount, int UnverifiedSourceCount, int UnknownSectionCount, int UnknownPropertyCount,
    int WarningCount, int BlockingIssueCount, IReadOnlyList<string> InputArtifactPaths,
    IReadOnlyList<string> OutputArtifactPaths, string DeterministicChecksum)
{
    public int AcceptedRequiredCount { get; init; }
    public int AcceptedOptionalCount { get; init; }
    public int HumanReviewClaimCount { get; init; }
    public int RequiredExactClaimCount { get; init; }
    public int RequiredExactEntityCount { get; init; }
    public int RequiredExactFieldCount { get; init; }
    public int OptionalAuthoritativeEvidenceCount { get; init; }
    public int OptionalReviewedEvidenceCount { get; init; }
    public int NoProvenanceClaimCount { get; init; }
    public int MandatoryAvailableDomainCount { get; init; }
    public int MandatoryHumanReviewDomainCount { get; init; }
    public int MandatoryDeferredDomainCount { get; init; }
    public int MandatoryMissingDomainCount { get; init; }
    public int OptionalAvailableDomainCount { get; init; }
    public int OptionalHumanReviewDomainCount { get; init; }
    public int OptionalDeferredDomainCount { get; init; }
    public int OptionalNotApplicableDomainCount { get; init; }
    public bool LocationTimeSafetyPassed { get; init; }
    public bool CulturalSafetyPassed { get; init; }
    public bool AstrologySeparationPassed { get; init; }
    public IReadOnlyList<string> ReconciliationDifferences { get; init; } = [];
}

public enum Phase7KnowledgeValidationMode { InMemoryCandidate, StagedPhysical, CommittedPhysical }
public sealed record Phase7KnowledgeValidationGate(string Name, bool Passed, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
public sealed record Phase7KnowledgeValidation(string ContractVersion, string ExecutionId, string PlanId,
    string EventId, string AuthorityId, bool IsValid, string ReasonCode, Phase7KnowledgeValidationMode Mode,
    IReadOnlyList<Phase7KnowledgeValidationGate> Gates, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, Phase7KnowledgeArtifactInventory? ArtifactInventory, string DeterministicChecksum);

public sealed record Phase7KnowledgeArtifactInventoryEntry(string RelativePath, string ContractType,
    string ContractVersion, string SemanticChecksum, string PhysicalSha256, long SizeBytes,
    string ExecutionId, string PlanId, string EventId, string AuthorityId, string AuthorityChecksum,
    string SourcePhase6AuthorityId, string SourcePhase6AuthorityChecksum, string LineageChecksum, bool Required);
public sealed record Phase7KnowledgeArtifactInventory(IReadOnlyList<Phase7KnowledgeArtifactInventoryEntry> Artifacts,
    string DeterministicChecksum);
public sealed record Phase7KnowledgeArtifactReadbackEvidence(string RelativePath, bool Exists, long SizeBytes,
    string PhysicalSha256, bool DeserializationSucceeded, string ContractType, string ContractVersion,
    bool IdentityMatched, bool SemanticChecksumMatched, bool LineageMatched, bool SafePath,
    IReadOnlyList<string> Errors);
public sealed record Phase7KnowledgeCompleteSetReadback(IReadOnlyList<Phase7KnowledgeArtifactReadbackEvidence> Artifacts,
    Phase7KnowledgeArtifactReadbackEvidence? ValidationReadback, bool ManifestEvidenceValid, bool IsValid,
    IReadOnlyList<string> Errors, Phase7KnowledgeArtifactInventory? ExpectedInventory);

public sealed record Phase7KnowledgeManifestEntry(int PhaseNo, string PhaseComponent, string Status,
    string ReasonCode, bool PublicationCommitted, bool CommittedStateValidationPassed, string AuthorityId,
    string AuthorityChecksum, string ValidationPhysicalSha256, string PublicationId, string ContractVersion,
    string DeterministicChecksum);

public sealed record Phase7KnowledgePublicationEvidence(string ContractVersion, string PublicationId,
    string ExecutionId, string PlanId, string EventId, string Language, string AuthorityId,
    string AuthorityChecksum, string ValidationPhysicalSha256, string ManifestEntryChecksum,
    bool PublicationCommitted, bool CommittedStateValidationPassed, DateTimeOffset CreatedUtc,
    string DeterministicChecksum);

public interface IPhase7KnowledgeAuthorityBuilder
{
    Phase7KnowledgeAuthority Build(Phase7CommittedInputAuthority input, CertifiedKnowledgePayload payload,
        ResolvedNarrationKnowledge knowledge, FamilyNarrationProfile profile,
        IReadOnlyDictionary<string,string> runtimeCompatibilityEvidence);
}
public interface IPhase7KnowledgeAuthorityValidator
{
    Phase7KnowledgeValidation Validate(Phase7KnowledgeAuthority authority, ResolvedNarrationKnowledge resolution,
        Phase7KnowledgeDiagnostics diagnostics, Phase7KnowledgeValidationMode mode = Phase7KnowledgeValidationMode.InMemoryCandidate,
        Phase7KnowledgeCompleteSetReadback? readback = null);
}
public interface IPhase7KnowledgeFileSystem
{
    bool FileExists(string path); bool DirectoryExists(string path); void CreateDirectory(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken token = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken token = default);
    Task WriteAllTextAsync(string path, string content, CancellationToken token = default);
    Task WriteAllBytesAsync(string path, byte[] content, CancellationToken token = default);
    void DeleteFile(string path); void DeleteDirectory(string path, bool recursive = true);
    void MoveDirectory(string source, string destination); void MoveFile(string source, string destination, bool overwrite = false);
    void CopyFile(string source, string destination, bool overwrite = false); IReadOnlyList<string> EnumerateOwnedPaths(string path);
}
public interface IPhase7KnowledgePhysicalReadback
{
    Task<Phase7KnowledgeArtifactInventory> CreateCandidateInventoryAsync(string knowledgeDirectory,
        Phase7KnowledgeAuthority authority, CancellationToken token = default);
    Task<Phase7KnowledgeCompleteSetReadback> ValidateCandidateCompleteSetAsync(string executionRoot,
        Phase7KnowledgeAuthority authority, Phase7KnowledgeArtifactInventory inventory, CancellationToken token = default);
    Task<Phase7KnowledgeCompleteSetReadback> ValidateCommittedCompleteSetAsync(string executionRoot,
        Phase7KnowledgeAuthority authority, Phase7KnowledgeArtifactInventory inventory,
        string expectedValidationHash, bool manifestEvidenceValid, CancellationToken token = default);
}
public interface IPhase7KnowledgeExecutionLock { Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken token = default); }

public sealed record Phase7KnowledgeTransactionPaths(string StableKnowledgeDirectory, string StableValidationPath,
    string StableManifestPath, string StagingKnowledgeDirectory, string CandidateValidationPath,
    string TransactionMarkerPath, string BackupKnowledgeDirectory, string BackupValidationPath,
    string BackupManifestPath, string StablePublicationEvidencePath, string CandidateManifestPath,
    string CandidatePublicationEvidencePath, string BackupPublicationEvidencePath)
{
    public static Phase7KnowledgeTransactionPaths Create(string executionRoot, string transactionId)
    {
        var root=Path.GetFullPath(executionRoot); var tx=$"phase-07-knowledge-{transactionId}";
        var staging=Path.Combine(root,$".{tx}-staging"); var backup=Path.Combine(root,$".{tx}-backup");
        return new(Path.Combine(root,"07-narration","knowledge"),Path.Combine(root,"validation","phase-07-knowledge-validation.json"),
            Path.Combine(root,"phase-manifest.json"),Path.Combine(staging,"07-narration","knowledge"),Path.Combine(staging,"validation","phase-07-knowledge-validation.json"),
            Path.Combine(root,$".{tx}-transaction.json"),Path.Combine(root,$".{tx}-backup","knowledge"),
            Path.Combine(backup,"validation","phase-07-knowledge-validation.json"),Path.Combine(backup,"manifest.json"),
            Path.Combine(root,".phase-07-knowledge-publication.json"),Path.Combine(staging,"manifest.json"),
            Path.Combine(staging,"publication.json"),Path.Combine(backup,"publication.json"));
    }
}
public enum Phase7KnowledgeTransactionState { Created, CandidateWritten, CandidateReadbackPassed, CandidateValidated,
    PreviousStateBackedUp, AuthoritySwapped, ValidationPublished, ManifestPublished, CommittedReadbackPassed,
    Completed, RollingBack, RollbackFailed }
public sealed record Phase7KnowledgeTransactionMarker(string ContractVersion, string TransactionId,
    string ExecutionId, string PlanId, string EventId, string Language, Phase7KnowledgeTransactionState State,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, string StagingKnowledgeDirectory,
    string StableKnowledgeDirectory, string BackupKnowledgeDirectory, string CandidateValidationPath,
    string StableValidationPath, string BackupValidationPath, string StableManifestPath, string BackupManifestPath,
    string OriginalError, IReadOnlyList<string> RollbackErrors, string CandidateAuthorityId,
    string PreviousAuthorityId, string DeterministicChecksum)
{
    public bool PreviousKnowledgeDirectoryExisted { get; init; }
    public bool PreviousValidationExisted { get; init; }
    public bool PreviousManifestExisted { get; init; }
    public bool PreviousPublicationEvidenceExisted { get; init; }
    public string StablePublicationEvidencePath { get; init; } = "";
    public string BackupPublicationEvidencePath { get; init; } = "";
    public string PreviousAuthorityChecksum { get; init; } = "";
    public string PreviousValidationPhysicalSha256 { get; init; } = "";
    public string PreviousManifestPhysicalSha256 { get; init; } = "";
    public string PreviousPublicationEvidencePhysicalSha256 { get; init; } = "";
}

public sealed record Phase7KnowledgeCommittedStateRequest(string ExecutionRoot, string ExecutionId,
    string PlanId, string EventId, string Language);
public sealed record Phase7KnowledgeCommittedStateEvaluation(bool IsValid,
    PublishedPhase7KnowledgeAuthority? Authority, string ReasonCode, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
public interface IPhase7KnowledgeCommittedStateEvaluator
{ Task<Phase7KnowledgeCommittedStateEvaluation> EvaluateAsync(Phase7KnowledgeCommittedStateRequest request, CancellationToken token = default); }
public interface IPhase7KnowledgeRecoveryService { Task<Phase7KnowledgeExecutionResult?> RecoverAsync(string executionRoot, CancellationToken token = default); }
public interface IPhase7KnowledgeTransactionCoordinator { Task<Phase7KnowledgeExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, bool overwriteExisting, CancellationToken token = default); }
public interface IPhase7KnowledgeService { Task<Phase7KnowledgeExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, bool overwriteExisting = false, CancellationToken token = default); }
