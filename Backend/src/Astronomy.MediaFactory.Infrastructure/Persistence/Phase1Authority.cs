using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public static class Phase1AuthorityContract
{
    public const string ContractVersion = "drashyam.phase1.v1";
    public const string AuthorityType = "CanonicalExecutionContext";
    public const string AuthorityVersion = "1.0";
    public const string CgIdentifier = "CG1";
    public const string OrchestrationVersion = "rc2.1.1";
    public const string ProjectorIdentity = "drashyam.phase1-projector/1.0";
    public const string CanonicalizationIdentity = "drashyam.canonical-json.sha256/1.0";
    public const string SelectedPlanContract = "drashyam.phase1-selected-plan/1.0";
    public const string ProductionRequestContract = "drashyam.phase1-production-request/1.0";
    public const string PipelineStateContract = "drashyam.phase1-pipeline-state/1.0";
    public const string DirectoryName = "01-plan";
}

public sealed record Phase1ArtifactDefinition(
    string RelativePath,
    string Role,
    string ContractVersion,
    string PublicationKind,
    bool Required,
    Func<Phase1ManifestStagingContext, string> StagingSourceResolver)
{
    public string ResolveFinalPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>The single inventory used to publish and validate Phase 1.</summary>
public static class Phase1ArtifactCatalog
{
    public static IReadOnlyList<Phase1ArtifactDefinition> Required { get; } =
    [
        new("01-plan/execution-context.json", "Authoritative", Phase1AuthorityContract.ContractVersion, "Canonical", true, x => Path.Combine(x.CanonicalStagingRoot, "execution-context.json")),
        new("01-plan/selected-plan.json", "Supporting", Phase1AuthorityContract.SelectedPlanContract, "Canonical", true, x => Path.Combine(x.CanonicalStagingRoot, "selected-plan.json")),
        new("01-plan/production-request.json", "Supporting", Phase1AuthorityContract.ProductionRequestContract, "Canonical", true, x => Path.Combine(x.CanonicalStagingRoot, "production-request.json")),
        new("01-plan/pipeline-state.json", "Supporting", Phase1AuthorityContract.PipelineStateContract, "Canonical", true, x => Path.Combine(x.CanonicalStagingRoot, "pipeline-state.json")),
        new("plan-input/content-plan-production-request.json", "Compatibility", "legacy", "Compatibility", true, x => Path.Combine(x.CompatibilityStagingRoot, "content-plan-production-request.json")),
        new("plan-input/production-event-intelligence.json", "Compatibility", "legacy", "Compatibility", true, x => Path.Combine(x.CompatibilityStagingRoot, "production-event-intelligence.json"))
    ];
}

public sealed record Phase1SelectedPlan(string ContractVersion, Guid PlanId, string SourcePlanIdentity, string Title, string ShortTitle, string EventType, string CanonicalEventIdentity, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, DateTimeOffset? ScheduledUtc, DateTimeOffset? ObservationStartUtc, DateTimeOffset? ObservationPeakUtc, DateTimeOffset? ObservationEndUtc, string RegionId, string RequestedLanguage, string Category, IReadOnlyList<string> RequestedVariants, IReadOnlyList<string> RequestedOutputs, string SourcePayloadChecksum, string SelectedPlanChecksum);
public sealed record Phase1ProductionRequest(string ContractVersion, Guid ExecutionId, Guid PlanId, string RequestedLanguage, string ResolvedLanguage, IReadOnlyList<string> RequestedVariants, IReadOnlyList<string> RequestedOutputs, int RequestedStartPhaseNo, int RequestedEndPhaseNo, int EffectiveStartPhaseNo, int EffectiveEndPhaseNo, bool DryRun, bool OverwriteExisting, bool RetryFailedOnly, string ExecutionMode, string RequestChecksum);
public sealed record Phase1PipelineState(string ContractVersion, Guid ExecutionId, Guid PlanId, DateTimeOffset InitializedUtc, int RequestedStartPhaseNo, int RequestedEndPhaseNo, int EffectiveStartPhaseNo, int EffectiveEndPhaseNo, string Phase1Status, IReadOnlyList<int> PlannedPhases, int InvalidationBoundary, bool DryRun, string ExecutionContextPath, string SelectedPlanChecksum, string ProductionRequestChecksum, IReadOnlyDictionary<int, string> DownstreamPhaseStates);
public sealed record Phase1ExecutionContext(string ContractVersion, string AuthorityType, string AuthorityVersion, string CgIdentifier, string OrchestrationVersion, string ProjectorIdentity, string CanonicalizationIdentity, Guid ExecutionId, Guid PlanId, Guid SelectedPlanId, Guid EventIntelligenceId, string CanonicalEventIdentity, string EventType, string RequestedLanguage, string ResolvedLanguage, IReadOnlyList<string> RequestedVariants, IReadOnlyList<string> RequestedOutputs, int RequestedStartPhaseNo, int RequestedEndPhaseNo, int EffectiveStartPhaseNo, int EffectiveEndPhaseNo, string ExecutionMode, bool DryRun, bool OverwriteExisting, bool RetryFailedOnly, string WorkspaceIdentity, string SelectedPlanChecksum, string ProductionRequestChecksum, string CompatibilityInputChecksum, string RequestIdentityChecksum, IReadOnlyDictionary<string, string> SupportingArtifactChecksums, DateTimeOffset GeneratedUtc, string AuthorityChecksum)
{
    public IReadOnlyDictionary<string,string> CompatibilityArtifactChecksums { get; init; } = new SortedDictionary<string,string>(StringComparer.Ordinal);
}
public sealed record Phase1AuthoritySet(Phase1ExecutionContext ExecutionContext, Phase1SelectedPlan SelectedPlan, Phase1ProductionRequest ProductionRequest, Phase1PipelineState PipelineState);
public sealed record Phase1ValidationDiagnostic(string Code, string Message, string? Path = null);
public sealed record Phase1AuthorityValidationResult(bool IsValid, bool IsCompatible, bool IsReusable, bool IsDownstreamReady, IReadOnlyList<Phase1ValidationDiagnostic> Errors, IReadOnlyList<Phase1ValidationDiagnostic> Warnings, string? ContractVersion, string? AuthorityChecksum, string? RequestIdentityChecksum, string RuntimeIdentity, Phase1AuthoritySet? AuthoritySet = null)
{
    public bool IsRequestCompatible { get; init; }
    public bool IsManifestCompatible { get; init; }
    public bool IsCompatibilityProjectionValid { get; init; }
}
public sealed record Phase1PersistenceResult(bool Reused, IReadOnlyList<string> Files, Phase1AuthorityValidationResult Validation, IReadOnlyList<string> Warnings);
public enum Phase1ExecutionKind { Generated, Reused, RegeneratedDueToMissingAuthority, RegeneratedDueToIncompleteAuthority, RegeneratedDueToCorruptAuthority, RegeneratedDueToChecksumMismatch, RegeneratedDueToRequestChange, RegeneratedDueToRuntimeIncompatibility, RegeneratedDueToManifestMismatch, RecoveredAndReused, RecoveredAndRegenerated, ManifestRepaired, CompatibilityRepaired, ValidationRepaired, Failed }
public sealed record Phase1ResumeEvaluation(bool CanReuse, string ReasonCode, string Reason, Phase1AuthorityValidationResult Validation, Phase1AuthoritySet? ExistingAuthority, IReadOnlyList<string> Warnings);
public sealed record Phase1RecoveryResult(string ActiveAuthorityState, bool Recovered, string? RestoredBackupPath, IReadOnlyList<string> RemovedStagingPaths, IReadOnlyList<string> RemovedBackupPaths, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public IReadOnlyList<string> IsolatedInvalidPaths { get; init; } = [];
    public bool CompatibilityRecovered { get; init; }
    public bool ManifestRepairRequired { get; init; }
    public string? CanonicalBackupPath { get; init; }
    public string? CompatibilityBackupPath { get; init; }
    public string? TransactionId { get; init; }
    public bool OriginalActiveRestoredOnRecoveryFailure { get; init; }
    public bool ManifestRecovered { get; init; }
    public bool ValidationRecovered { get; init; }
    public bool ValidationRepairRequired { get; init; }
    public string? ManifestBackupPath { get; init; }
    public string? ValidationBackupPath { get; init; }
    public IReadOnlyList<string> MetadataInvalidatedPaths { get; init; } = [];
}
public sealed record Phase1ExecutionOutcome(Phase1ExecutionKind Kind, string ReasonCode, string Reason, IReadOnlyList<string> OutputFiles, IReadOnlyList<string> Warnings, string? AuthorityChecksum, string? RequestIdentityChecksum, bool Reused, bool ReplacedExistingAuthority, bool DownstreamInvalidated, string CompatibilityProjectionStatus, Phase1RecoveryResult RecoveryStatus)
{
    public string? PublicationTransactionId { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string ManifestStatus { get; init; } = "Pending";
    public string ValidationStatus { get; init; } = "Pending";
    public bool RollbackPerformed { get; init; }
    public bool RollbackSucceeded { get; init; }
}

public sealed record Phase1CompatibilityPublication(IReadOnlyDictionary<string,string> Payloads, IReadOnlyDictionary<string,string> Checksums);
public sealed record Phase1CompatibilityValidationResult(bool IsValid, bool IsMissing, IReadOnlyList<Phase1ValidationDiagnostic> Errors);
public sealed record Phase1ManifestStagingContext(string WorkspaceRoot,string CanonicalStagingRoot,string CompatibilityStagingRoot,string ManifestStagingPath,string TransactionId,Phase1AuthoritySet ExpectedCanonicalAuthority,Phase1CompatibilityPublication ExpectedCompatibilityPublication);
public sealed record Phase1PreviousPublicationDescriptor(Phase1AuthoritySet? CanonicalAuthority,IReadOnlyDictionary<string,string> CompatibilityChecksums,string? ManifestChecksum,string? ValidationChecksum);
public sealed record Phase1ManifestValidationResult(bool IsPresent, bool IsValid, bool IsRepairable, IReadOnlyList<Phase1ValidationDiagnostic> Errors, IReadOnlyList<Phase1ValidationDiagnostic> Warnings, IReadOnlyList<string> ArtifactEntries);
public sealed record Phase1SuccessValidationResult(bool IsPresent,bool IsValid,bool IsRepairable,bool PublicationCommitted,string? Status,string? TransactionId,string? AuthorityChecksum,string? RequestIdentityChecksum,IReadOnlyList<Phase1ValidationDiagnostic> Errors,IReadOnlyList<Phase1ValidationDiagnostic> Warnings);
public sealed record Phase1PublicationValidationResult(Phase1AuthorityValidationResult CanonicalValidation, Phase1ManifestValidationResult ManifestValidation, Phase1CompatibilityValidationResult CompatibilityValidation, Phase1SuccessValidationResult SuccessValidation, bool IsRequestCompatible, bool IsRuntimeCompatible, bool IsManifestCompatible, bool IsCompatibilityProjectionValid, bool IsDownstreamReady, bool IsReusable, IReadOnlyList<Phase1ValidationDiagnostic> Errors, IReadOnlyList<Phase1ValidationDiagnostic> Warnings);
public interface IPhase1RecoveryService { Task<Phase1RecoveryResult> RecoverAsync(string outputRoot, Phase1AuthoritySet expectedAuthority, Phase1CompatibilityPublication expectedCompatibility, CancellationToken cancellationToken); }
public interface IPhase1ManifestValidator { Task<Phase1ManifestValidationResult> ValidateAsync(string workspaceRoot, Phase1AuthoritySet expectedAuthority, Phase1CompatibilityPublication expectedCompatibility, CancellationToken cancellationToken); }
public interface IPhase1SuccessValidationValidator { Task<Phase1SuccessValidationResult> ValidateAsync(string workspaceRoot,Phase1AuthoritySet authority,CancellationToken cancellationToken); }
public interface IPhase1ResumeEvaluator { Phase1ResumeEvaluation Evaluate(Phase1AuthoritySet expected, Phase1AuthorityValidationResult existing, bool manifestCompatible, Phase1CompatibilityValidationResult compatibility, Phase1RecoveryResult recovery); }
public interface IPhase1CompatibilityPublisher
{
    Phase1CompatibilityPublication Project(ProductionPhaseContext context);
    Task<Phase1CompatibilityValidationResult> ValidateAsync(string workspaceRoot, Phase1CompatibilityPublication expected, CancellationToken token);
    [Obsolete("Phase 1 publications must use IPhase1PublicationTransactionCoordinator.", true)]
    Task<IReadOnlyList<string>> PublishAsync(string workspaceRoot, Phase1CompatibilityPublication publication, CancellationToken token);
    Task StageAsync(string stagingRoot, Phase1CompatibilityPublication publication, CancellationToken token);
    Task<Phase1CompatibilityValidationResult> ValidateDirectoryAsync(string stagingRoot, Phase1CompatibilityPublication expected, CancellationToken token);
    Task<Phase1CompatibilityPublication> ReadDirectoryAsync(string directory, CancellationToken token);
}

public sealed record Phase1DownstreamPathMove(string OriginalPath,string QuarantinePath,bool IsDirectory);
public sealed record PhaseOutputTarget(
    int PhaseNo,
    string Path,
    bool IsDirectory,
    string RelativePath,
    string ArtifactKind,
    string Owner,
    bool IsAuthority,
    bool IsCompatibility,
    bool IsValidation,
    bool IsSharedManifest,
    bool CanDeleteOnOverwrite);
public interface IPhaseOutputTargetResolver { IReadOnlyList<PhaseOutputTarget> Resolve(ProductionPhaseContext context,int startPhaseNo,int endPhaseNo); }
public sealed class PhaseOutputTargetResolver:IPhaseOutputTargetResolver
{
    public IReadOnlyList<PhaseOutputTarget> Resolve(ProductionPhaseContext context,int start,int end)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.OutputRoot));var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;var targets=new List<PhaseOutputTarget>();
        void Add(int phase,string? path,bool directory=true,bool compatibility=false,bool validation=false)
        {
            if(phase<start||phase>end||string.IsNullOrWhiteSpace(path))return;
            var full=Path.GetFullPath(path);
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,comparison))throw new InvalidOperationException($"Phase {phase} output target is outside the workspace: {full}");
            targets.Add(new(phase,full,directory,Path.GetRelativePath(root,full).Replace('\\','/'),validation?"Validation":compatibility?"Compatibility":"Authority",$"Phase{phase}",!compatibility&&!validation,compatibility,validation,false,true));
        }
        Add(2,Path.Combine(root,"02-intelligence"));Add(3,Path.Combine(root,"03-questions"));Add(3,context.ExecutionContext.QuestionRoot);Add(4,Path.Combine(root,"04-blueprint"));Add(5,Path.Combine(root,"05-blueprint-certification"));Add(6,Path.Combine(root,"06-story-frames"));Add(7,context.ExecutionContext.NarrationRoot);Add(8,context.ExecutionContext.SceneRoot);Add(11,context.ExecutionContext.HeroRoot);Add(12,context.ExecutionContext.ThumbnailRoot);Add(13,Path.Combine(root,"gallery"));Add(14,Path.Combine(root,"sync"));Add(15,context.ExecutionContext.TtsRoot);Add(18,context.ExecutionContext.VideoAssemblyRoot);
        for(var phase=Math.Max(2,start);phase<=Math.Min(20,end);phase++)Add(phase,Path.Combine(context.ExecutionContext.ValidationRoot!,$"phase-{phase:00}-validation.json"),false,validation:true);
        var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        var deduplicated=targets.GroupBy(x=>x.Path,comparer).Select(g=>g.OrderBy(x=>x.PhaseNo).First()).ToArray();
        return deduplicated.Where(candidate=>!deduplicated.Any(parent=>parent.IsDirectory&&!comparer.Equals(parent.Path,candidate.Path)&&IsContainedByDirectory(candidate.Path,parent.Path,comparison)))
            .OrderBy(x=>x.PhaseNo).ThenBy(x=>x.Path,comparer).ToArray();
    }

    internal static bool IsContainedByDirectory(string candidatePath,string parentDirectory,StringComparison comparison)
    {
        var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
        var candidate=Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        if(candidate.Length<=parent.Length)return false;
        if(candidate.StartsWith(parent+Path.DirectorySeparatorChar,comparison))return true;
        return Path.AltDirectorySeparatorChar!=Path.DirectorySeparatorChar&&candidate.StartsWith(parent+Path.AltDirectorySeparatorChar,comparison);
    }
}
public static class UpstreamPhaseMutationGuard
{
    public static void AssertAllowed(int startPhaseNo,PhaseOutputTarget target,string operation)
    {
        if(target.PhaseNo>=startPhaseNo)return;
        throw new InvalidOperationException($"RC2_UPSTREAM_PHASE_MUTATION_ATTEMPT: startPhaseNo={startPhaseNo}; targetPhaseNo={target.PhaseNo}; targetPath={target.Path}; targetOwner={target.Owner}; operation={operation}");
    }
}
public sealed record Phase1DownstreamInvalidationState(string TransactionId,string WorkspaceRoot,string QuarantineRoot,IReadOnlyList<Phase1DownstreamPathMove> Moves,bool HasMutatedActiveState,bool IsFullyStaged);
public sealed class Phase1DownstreamStagingException(string message,Phase1DownstreamInvalidationState state,Exception? inner=null):IOException(message,inner){public Phase1DownstreamInvalidationState State{get;}=state;}
public interface IPhase1DownstreamInvalidationTransaction
{
    Task<Phase1DownstreamInvalidationState> StageAsync(ProductionPhaseContext context,string transactionId,CancellationToken cancellationToken);
    Task CommitAsync(Phase1DownstreamInvalidationState state,CancellationToken nonInterruptibleToken);
    Task RollbackAsync(Phase1DownstreamInvalidationState state,CancellationToken nonInterruptibleToken);
}

public sealed record Phase1PublicationTransactionRequest(
    string WorkspaceRoot,
    ProductionPhaseContext Context,
    Phase1AuthoritySet ExpectedCanonicalAuthority,
    Phase1CompatibilityPublication ExpectedCompatibilityPublication,
    bool ExistingCanonicalPublicationExists,
    bool DownstreamInvalidationRequired,
    Func<Phase1ValidationStagingContext,CancellationToken,Task> StageProvisionalValidationAsync,
    Func<Phase1ValidationStagingContext,CancellationToken,Task> StageFinalValidationAsync,
    Func<Phase1ManifestStagingContext,CancellationToken,Task> StageManifestAsync,
    IPhase1DownstreamInvalidationTransaction DownstreamTransaction);

public sealed record Phase1ValidationStagingContext(string WorkspaceRoot,string OutputPath,string TransactionId,bool DownstreamInvalidated,bool PublicationCommitted,string PublicationState);
public sealed record Phase1ManifestRepairRequest(string WorkspaceRoot,Phase1AuthoritySet ActiveCanonicalAuthority,Phase1CompatibilityPublication ActiveCompatibilityPublication,Func<Phase1ManifestStagingContext,CancellationToken,Task> StageManifestAsync,Func<Phase1ValidationStagingContext,CancellationToken,Task> StageFinalValidationAsync);
public sealed record Phase1CompatibilityRepairRequest(string WorkspaceRoot,Phase1AuthoritySet ActiveCanonicalAuthority,Phase1CompatibilityPublication ExpectedCompatibilityPublication,Func<Phase1ManifestStagingContext,CancellationToken,Task> StageManifestAsync,Func<Phase1ValidationStagingContext,CancellationToken,Task> StageFinalValidationAsync);
public sealed record Phase1ValidationRepairRequest(string WorkspaceRoot,Phase1AuthoritySet ActiveCanonicalAuthority,Phase1CompatibilityPublication ActiveCompatibilityPublication,Func<Phase1ValidationStagingContext,CancellationToken,Task> StageFinalValidationAsync);

public sealed record Phase1PublicationTransactionResult(bool Succeeded,string TransactionId,string ReasonCode,
    IReadOnlyList<string> Files,IReadOnlyList<string> Warnings,IReadOnlyList<string> Errors,bool RollbackPerformed,bool RollbackSucceeded,bool DownstreamInvalidated)
{
    public bool PreviousGenerationRestored { get; init; }
    public bool PreviousGenerationWasValid { get; init; }
    public string? FailureDiagnosticsPath { get; init; }
}

public interface IPhase1PublicationTransactionCoordinator
{
    Task<Phase1PublicationTransactionResult> PublishAsync(Phase1PublicationTransactionRequest request,CancellationToken cancellationToken);
    Task<Phase1PublicationTransactionResult> RepairManifestAsync(Phase1ManifestRepairRequest request,CancellationToken cancellationToken);
    Task<Phase1PublicationTransactionResult> RepairCompatibilityAsync(Phase1CompatibilityRepairRequest request,CancellationToken cancellationToken);
    Task<Phase1PublicationTransactionResult> RepairValidationAsync(Phase1ValidationRepairRequest request,CancellationToken cancellationToken);
}

public sealed class Phase1ResumeEvaluator : IPhase1ResumeEvaluator
{
    public Phase1ResumeEvaluation Evaluate(Phase1AuthoritySet expected, Phase1AuthorityValidationResult existing, bool manifestCompatible, Phase1CompatibilityValidationResult compatibility, Phase1RecoveryResult recovery)
    {
        string code;
        if (existing.AuthoritySet is null) code = existing.Errors.Any(x=>x.Code=="P1_RESUME_CORRUPT_JSON") ? "P1_RESUME_CORRUPT_JSON" : existing.Errors.Count(x=>x.Code=="P1_ARTIFACT_MISSING") is >0 and <4 ? "P1_RESUME_INCOMPLETE_SET" : "P1_RESUME_NO_AUTHORITY";
        else if (!existing.IsValid) code = existing.Errors.Any(x=>x.Code.Contains("CHECKSUM",StringComparison.Ordinal)) ? "P1_RESUME_CHECKSUM_MISMATCH" : existing.Errors.Any(x=>x.Code.Contains("PATH",StringComparison.Ordinal)) ? "P1_RESUME_PATH_INVALID" : "P1_RESUME_CORRUPT_JSON";
        else if (!existing.IsCompatible) code = existing.Errors.Any(x=>x.Code.Contains("RUNTIME",StringComparison.Ordinal)) ? "P1_RESUME_RUNTIME_INCOMPATIBLE" : "P1_RESUME_CONTRACT_UNSUPPORTED";
        else if (!string.Equals(existing.RequestIdentityChecksum, expected.ExecutionContext.RequestIdentityChecksum, StringComparison.Ordinal)) code = "P1_RESUME_REQUEST_CHANGED";
        else if (!existing.IsDownstreamReady) code = "P1_RESUME_VALIDATION_REPAIR_REQUIRED";
        else if (!manifestCompatible) code = "P1_RESUME_MANIFEST_INVALID";
        else if (!compatibility.IsValid) code = compatibility.IsMissing ? "P1_RESUME_COMPATIBILITY_MISSING" : "P1_RESUME_COMPATIBILITY_MISMATCH";
        else code = recovery.Recovered ? "P1_RESUME_RECOVERED_AUTHORITY" : "P1_RESUME_REUSABLE";
        var canReuse = code is "P1_RESUME_REUSABLE" or "P1_RESUME_RECOVERED_AUTHORITY";
        return new(canReuse,code,canReuse?"Complete Phase 1 publication is reusable.":"Phase 1 publication must be regenerated.",existing,existing.AuthoritySet,recovery.Warnings);
    }
}

public static class Phase1PublicationCancellation
{
    // Commit and rollback ignore external cancellation after the first active-to-backup rename.
    public static CancellationToken NonInterruptible => CancellationToken.None;
}

public interface IPhase1FileSystem
{
    bool FileExists(string path); bool DirectoryExists(string path); void CreateDirectory(string path); void DeleteDirectory(string path, bool recursive); void MoveDirectory(string source, string destination); void MoveFile(string source,string destination); void DeleteFile(string path);
    IEnumerable<string> EnumerateDirectories(string path, string pattern); IEnumerable<string> EnumerateFiles(string path, string pattern); Stream OpenRead(string path); Task WriteAllTextAsync(string path, string contents, CancellationToken token);
    string GetFullPath(string path); string GetFileName(string path); string? GetDirectoryName(string path); DateTimeOffset GetLastWriteTimeUtc(string path); FileAttributes GetAttributes(string path);
}
public sealed class Phase1FileSystem : IPhase1FileSystem
{
    public bool FileExists(string p)=>File.Exists(p); public bool DirectoryExists(string p)=>Directory.Exists(p); public void CreateDirectory(string p)=>Directory.CreateDirectory(p); public void DeleteDirectory(string p,bool r)=>Directory.Delete(p,r); public void MoveDirectory(string s,string d)=>Directory.Move(s,d); public void MoveFile(string s,string d)=>File.Move(s,d); public void DeleteFile(string p)=>File.Delete(p);
    public IEnumerable<string> EnumerateDirectories(string p,string pattern)=>Directory.Exists(p)?Directory.EnumerateDirectories(p,pattern,SearchOption.TopDirectoryOnly):[]; public IEnumerable<string> EnumerateFiles(string p,string pattern)=>Directory.Exists(p)?Directory.EnumerateFiles(p,pattern,SearchOption.TopDirectoryOnly):[]; public Stream OpenRead(string p)=>File.OpenRead(p); public Task WriteAllTextAsync(string p,string c,CancellationToken t)=>File.WriteAllTextAsync(p,c,t);
    public string GetFullPath(string p)=>Path.GetFullPath(p); public string GetFileName(string p)=>Path.GetFileName(p); public string? GetDirectoryName(string p)=>Path.GetDirectoryName(p); public DateTimeOffset GetLastWriteTimeUtc(string p)=>Directory.GetLastWriteTimeUtc(p); public FileAttributes GetAttributes(string p)=>File.GetAttributes(p);
}

public interface IPhase1ExecutionLock { ValueTask<IAsyncDisposable> AcquireAsync(string workspaceRoot, CancellationToken token); int EntryCount { get; } }
public sealed class InProcessPhase1ExecutionLock : IPhase1ExecutionLock
{
    private sealed class Entry { public readonly SemaphoreSlim Gate=new(1,1); public int References; }
    private readonly ConcurrentDictionary<string,Entry> entries=new(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
    public int EntryCount=>entries.Count;
    public async ValueTask<IAsyncDisposable> AcquireAsync(string root,CancellationToken token)
    {
        var key=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); Entry entry;
        while(true){ entry=entries.GetOrAdd(key,_=>new()); lock(entry){ if(entries.TryGetValue(key,out var current)&&ReferenceEquals(current,entry)){entry.References++;break;}}}
        try { await entry.Gate.WaitAsync(token); return new Lease(this,key,entry); } catch { ReleaseReference(key,entry,false); throw; }
    }
    private void ReleaseReference(string key,Entry entry,bool release){if(release)entry.Gate.Release();lock(entry){entry.References--;if(entry.References==0)entries.TryRemove(new KeyValuePair<string,Entry>(key,entry));}}
    private sealed class Lease(InProcessPhase1ExecutionLock owner,string key,Entry entry):IAsyncDisposable{private int disposed;public ValueTask DisposeAsync(){if(Interlocked.Exchange(ref disposed,1)==0)owner.ReleaseReference(key,entry,true);return ValueTask.CompletedTask;}}
}

public interface IPhase1AuthorityProjector { Phase1AuthoritySet Project(ProductionPhaseContext context, DateTimeOffset generatedUtc); }
public interface IPhase1AuthorityValidator { Task<Phase1AuthorityValidationResult> ValidateAsync(string workspaceRoot, string authorityRoot, bool allowStaging, CancellationToken cancellationToken); }
public interface IPhase1AuthorityPersistence { }
public interface IPhase1AuthorityReader { Task<Phase1AuthorityValidationResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken); }

public static class Phase1CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Checksum<T>(T value, params string[] excludedProperties)
    {
        var node = JsonSerializer.SerializeToNode(value, Options)!;
        Remove(node, excludedProperties.ToHashSet(StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(node)))).ToLowerInvariant();
    }
    private static void Remove(JsonNode? node, HashSet<string> excluded)
    {
        if (node is JsonObject obj) foreach (var property in obj.ToArray()) { if (excluded.Contains(property.Key)) obj.Remove(property.Key); else Remove(property.Value, excluded); }
        else if (node is JsonArray array) foreach (var item in array) Remove(item, excluded);
    }
    private static string Canonical(JsonNode? node) => node switch { null => "null", JsonObject obj => "{" + string.Join(',', obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Key) + ":" + Canonical(x.Value))) + "}", JsonArray array => "[" + string.Join(',', array.Select(Canonical)) + "]", _ => node.ToJsonString() };
}

public sealed class Phase1AuthorityProjector : IPhase1AuthorityProjector
{
    public Phase1AuthoritySet Project(ProductionPhaseContext context, DateTimeOffset generatedUtc)
    {
        var request = context.Request;
        var executionId = request.PlanId;
        var requestedStart = context.PipelineRequest.RequestedStartPhaseNo ?? context.PipelineRequest.StartPhaseNo ?? 1;
        var requestedEnd = context.PipelineRequest.RequestedEndPhaseNo ?? context.PipelineRequest.EndPhaseNo ?? 20;
        var variants = NormalizeVariants(request.RequestedOutputs); var outputs = Normalize(request.RequestedOutputs);
        var language = request.Language.Trim().ToLowerInvariant();
        var canonicalEvent = string.IsNullOrWhiteSpace(request.SourceExternalEventId) ? $"{request.EventType.Trim().ToLowerInvariant()}:{request.PlanId:D}" : request.SourceExternalEventId.Trim().ToLowerInvariant();
        var sourceChecksum = Phase1CanonicalJson.Checksum(new { request.PlanId, request.Title, request.ShortTitle, request.EventType, request.SourceExternalEventId, primaryObjects = Normalize(request.PrimaryObjects), secondaryObjects = Normalize(request.SecondaryObjects), request.StartUtc, request.PeakUtc, request.EndUtc, request.ScheduledUtc, request.RegionId, language, request.Category, variants, outputs });
        var selected = new Phase1SelectedPlan(Phase1AuthorityContract.SelectedPlanContract, request.PlanId, request.PlanId.ToString("D"), request.Title.Trim(), request.ShortTitle.Trim(), request.EventType.Trim(), canonicalEvent, Normalize(request.PrimaryObjects), Normalize(request.SecondaryObjects), request.ScheduledUtc, request.StartUtc, request.PeakUtc, request.EndUtc, request.RegionId.Trim(), language, request.Category.Trim(), variants, outputs, sourceChecksum, "");
        selected = selected with { SelectedPlanChecksum = Phase1CanonicalJson.Checksum(selected, nameof(Phase1SelectedPlan.SelectedPlanChecksum)) };
        var production = new Phase1ProductionRequest(Phase1AuthorityContract.ProductionRequestContract, executionId, request.PlanId, language, language, variants, outputs, requestedStart, requestedEnd, context.StartPhaseNo, context.EndPhaseNo, context.DryRun, context.OverwriteExisting, context.RetryFailedOnly, context.ExecutionMode.ToString(), "");
        production = production with { RequestChecksum = Phase1CanonicalJson.Checksum(production, nameof(Phase1ProductionRequest.RequestChecksum)) };
        var state = new Phase1PipelineState(Phase1AuthorityContract.PipelineStateContract, executionId, request.PlanId, generatedUtc, requestedStart, requestedEnd, context.StartPhaseNo, context.EndPhaseNo, "Initialized", Enumerable.Range(context.StartPhaseNo, context.EndPhaseNo - context.StartPhaseNo + 1).ToArray(), 2, false, "01-plan/execution-context.json", selected.SelectedPlanChecksum, production.RequestChecksum, Enumerable.Range(2, 19).ToDictionary(x => x, _ => "Pending"));
        var stateChecksum = Phase1CanonicalJson.Checksum(state, nameof(Phase1PipelineState.InitializedUtc));
        var requestIdentity = Phase1CanonicalJson.Checksum(new { selected.SelectedPlanChecksum, production.RequestChecksum });
        var compatibilityPublication = Phase1CompatibilityPublisher.ProjectPayloads(context);
        var compatibility = Phase1CanonicalJson.Checksum(compatibilityPublication.Checksums);
        var authority = new Phase1ExecutionContext(Phase1AuthorityContract.ContractVersion, Phase1AuthorityContract.AuthorityType, Phase1AuthorityContract.AuthorityVersion, Phase1AuthorityContract.CgIdentifier, Phase1AuthorityContract.OrchestrationVersion, Phase1AuthorityContract.ProjectorIdentity, Phase1AuthorityContract.CanonicalizationIdentity, executionId, request.PlanId, request.PlanId, context.AstronomyEventIntelligenceId, canonicalEvent, request.EventType.Trim(), language, language, variants, outputs, requestedStart, requestedEnd, context.StartPhaseNo, context.EndPhaseNo, context.ExecutionMode.ToString(), false, context.OverwriteExisting, context.RetryFailedOnly, request.PlanId.ToString("D"), selected.SelectedPlanChecksum, production.RequestChecksum, compatibility, requestIdentity, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["pipeline-state.json"] = stateChecksum, ["production-request.json"] = production.RequestChecksum, ["selected-plan.json"] = selected.SelectedPlanChecksum }, generatedUtc, "");
        authority = authority with { CompatibilityArtifactChecksums = compatibilityPublication.Checksums };
        authority = authority with { AuthorityChecksum = Phase1CanonicalJson.Checksum(authority, nameof(Phase1ExecutionContext.GeneratedUtc), nameof(Phase1ExecutionContext.AuthorityChecksum)) };
        return new(authority, selected, production, state);
    }
    private static string[] Normalize(IEnumerable<string> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static string[] NormalizeVariants(IEnumerable<string> outputs) { var n = Normalize(outputs); var r = new List<string>(); if (n.Any(x => x.Contains("long", StringComparison.Ordinal))) r.Add("long"); if (n.Any(x => x.Contains("short", StringComparison.Ordinal))) r.Add("short"); return r.Count == 0 ? ["long", "short"] : r.ToArray(); }
}

public sealed class Phase1CompatibilityPublisher(IPhase1FileSystem fileSystem) : IPhase1CompatibilityPublisher
{
    private const string RequestPath="plan-input/content-plan-production-request.json";
    private const string IntelligencePath="plan-input/production-event-intelligence.json";
    public Phase1CompatibilityPublication Project(ProductionPhaseContext context)=>ProjectPayloads(context);
    internal static Phase1CompatibilityPublication ProjectPayloads(ProductionPhaseContext context)
    {
        var payloads=new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            [RequestPath]=Phase1CanonicalJson.Serialize(context.Request),
            [IntelligencePath]=Phase1CanonicalJson.Serialize(context.ProductionEventIntelligence)
        };
        var sums=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var item in payloads)sums[item.Key]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Value))).ToLowerInvariant();
        return new(payloads,sums);
    }
    public async Task<Phase1CompatibilityValidationResult> ValidateAsync(string root,Phase1CompatibilityPublication expected,CancellationToken token)
    {
        var errors=new List<Phase1ValidationDiagnostic>();var missing=false;
        foreach(var item in expected.Payloads)
        {
            token.ThrowIfCancellationRequested();var path=Path.Combine(root,item.Key.Replace('/',Path.DirectorySeparatorChar));
            if(!fileSystem.FileExists(path)){missing=true;errors.Add(new("P1_RESUME_COMPATIBILITY_MISSING","Compatibility artifact is missing.",path));continue;}
            try{await using var stream=fileSystem.OpenRead(path);using var document=await JsonDocument.ParseAsync(stream,cancellationToken:token);_ = document.RootElement.ValueKind;stream.Position=0;using var reader=new StreamReader(stream,Encoding.UTF8);var actual=await reader.ReadToEndAsync(token);var checksum=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual))).ToLowerInvariant();if(!string.Equals(checksum,expected.Checksums[item.Key],StringComparison.Ordinal))errors.Add(new("P1_RESUME_COMPATIBILITY_MISMATCH","Compatibility checksum mismatch.",path));}
            catch(JsonException ex){errors.Add(new("P1_RESUME_COMPATIBILITY_MISMATCH",ex.Message,path));}
        }
        return new(errors.Count==0,missing,errors);
    }
    [Obsolete("Phase 1 publications must use IPhase1PublicationTransactionCoordinator.", true)]
    public Task<IReadOnlyList<string>> PublishAsync(string root,Phase1CompatibilityPublication publication,CancellationToken token)
        => throw new InvalidOperationException("P1_TRANSACTION_COORDINATOR_REQUIRED");

    public async Task StageAsync(string stagingRoot,Phase1CompatibilityPublication publication,CancellationToken token)
    {
        fileSystem.CreateDirectory(stagingRoot);
        foreach(var item in publication.Payloads)
            await fileSystem.WriteAllTextAsync(Path.Combine(stagingRoot,Path.GetFileName(item.Key)),item.Value,token);
    }
    public async Task<Phase1CompatibilityValidationResult> ValidateDirectoryAsync(string directory,Phase1CompatibilityPublication expected,CancellationToken token)
    {
        var errors=new List<Phase1ValidationDiagnostic>();var missing=false;
        foreach(var item in expected.Payloads){token.ThrowIfCancellationRequested();var path=Path.Combine(directory,Path.GetFileName(item.Key));if(!fileSystem.FileExists(path)){missing=true;errors.Add(new("P1_RESUME_COMPATIBILITY_MISSING","Compatibility artifact is missing.",path));continue;}await using var stream=fileSystem.OpenRead(path);using var reader=new StreamReader(stream,Encoding.UTF8);var actual=await reader.ReadToEndAsync(token);var checksum=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual))).ToLowerInvariant();if(!string.Equals(checksum,expected.Checksums[item.Key],StringComparison.Ordinal))errors.Add(new("P1_RESUME_COMPATIBILITY_MISMATCH","Compatibility checksum mismatch.",path));}
        return new(errors.Count==0,missing,errors);
    }
    public async Task<Phase1CompatibilityPublication> ReadDirectoryAsync(string directory,CancellationToken token)
    {
        var payloads=new SortedDictionary<string,string>(StringComparer.Ordinal);var checksums=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var relativePath in new[]{RequestPath,IntelligencePath})
        {
            token.ThrowIfCancellationRequested();var path=Path.Combine(directory,Path.GetFileName(relativePath));
            if(!fileSystem.FileExists(path))throw new InvalidDataException($"Restored compatibility artifact is missing: {path}");
            await using var stream=fileSystem.OpenRead(path);using var reader=new StreamReader(stream,Encoding.UTF8);var payload=await reader.ReadToEndAsync(token);
            using var document=JsonDocument.Parse(payload);_ = document.RootElement.ValueKind;
            payloads[relativePath]=payload;checksums[relativePath]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }
        return new(payloads,checksums);
    }
}

public sealed class Phase1DownstreamInvalidationTransaction(IPhase1FileSystem fileSystem,IPhaseOutputTargetResolver targetResolver):IPhase1DownstreamInvalidationTransaction
{
    public Task<Phase1DownstreamInvalidationState> StageAsync(ProductionPhaseContext context,string transactionId,CancellationToken token)
    {
        var root=fileSystem.GetFullPath(context.OutputRoot);var quarantine=Path.Combine(root,$".phase1-downstream-backup-{transactionId}");var moves=new List<Phase1DownstreamPathMove>();
        Phase1DownstreamInvalidationState State(bool complete)=>new(transactionId,root,quarantine,moves.ToArray(),moves.Count>0,complete);
        try
        {
            token.ThrowIfCancellationRequested();
            var resolved=targetResolver.Resolve(context,2,20);var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
            foreach(var target in resolved)UpstreamPhaseMutationGuard.AssertAllowed(2,target,"phase1-downstream-invalidation");
            var normalized=resolved.Select(x=>x with{Path=Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(x.Path))}).GroupBy(x=>x.Path,comparer).Select(g=>g.OrderBy(x=>x.PhaseNo).First()).ToArray();
            foreach(var target in normalized)if(!PhaseOutputTargetResolver.IsContainedByDirectory(target.Path,root,comparison))throw new IOException($"Downstream target is outside workspace: {target.Path}");
            var plan=normalized.Where(candidate=>!normalized.Any(parent=>parent.IsDirectory&&!comparer.Equals(parent.Path,candidate.Path)&&PhaseOutputTargetResolver.IsContainedByDirectory(candidate.Path,parent.Path,comparison)))
                .OrderBy(x=>x.IsDirectory?0:1).ThenBy(x=>x.Path,comparer).ToArray();
            var candidates=plan.Where(x=>x.IsDirectory?fileSystem.DirectoryExists(x.Path):fileSystem.FileExists(x.Path)).ToArray();
            foreach(var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(root,candidate.Path);var destination=Path.Combine(quarantine,relative);fileSystem.CreateDirectory(fileSystem.GetDirectoryName(destination)!);
                // Existence is checked again so concurrently removed targets remain an idempotent no-op.
                try
                {
                    if(candidate.IsDirectory){if(!fileSystem.DirectoryExists(candidate.Path))continue;fileSystem.MoveDirectory(candidate.Path,destination);}else{if(!fileSystem.FileExists(candidate.Path))continue;fileSystem.MoveFile(candidate.Path,destination);}
                }
                catch(Exception ex){ex.Data["targetPath"]=candidate.Path;ex.Data["targetPhaseNo"]=candidate.PhaseNo;ex.Data["targetType"]=candidate.IsDirectory?"directory":"file";throw;}
                moves.Add(new(candidate.Path,destination,candidate.IsDirectory));
            }
            if(candidates.Any(x=>x.IsDirectory?fileSystem.DirectoryExists(x.Path):fileSystem.FileExists(x.Path)))throw new IOException("A downstream path remained active after staging.");
            return Task.FromResult(State(true));
        }
        catch(Exception ex){throw new Phase1DownstreamStagingException($"P1_DOWNSTREAM_INVALIDATION_FAILED: transactionId={transactionId}; operation=stage; targetPath={ex.Data["targetPath"]??"unknown"}; targetPhaseNo={ex.Data["targetPhaseNo"]??"unknown"}; targetType={ex.Data["targetType"]??"unknown"}; parentTargetPath={ex.Data["parentTargetPath"]??"none"}",State(false),ex);}
    }
    public Task RollbackAsync(Phase1DownstreamInvalidationState state,CancellationToken token)
    {
        var errors=new List<string>();
        foreach(var move in state.Moves.Reverse())try
        {
            token.ThrowIfCancellationRequested();var sourceExists=move.IsDirectory?fileSystem.DirectoryExists(move.QuarantinePath):fileSystem.FileExists(move.QuarantinePath);var activeExists=move.IsDirectory?fileSystem.DirectoryExists(move.OriginalPath):fileSystem.FileExists(move.OriginalPath);
            if(activeExists&&sourceExists){errors.Add($"Conflicting active downstream path: {move.OriginalPath}");continue;}if(!sourceExists&&activeExists)continue;if(!sourceExists){errors.Add($"Missing quarantined path: {move.QuarantinePath}");continue;}
            fileSystem.CreateDirectory(fileSystem.GetDirectoryName(move.OriginalPath)!);if(move.IsDirectory)fileSystem.MoveDirectory(move.QuarantinePath,move.OriginalPath);else fileSystem.MoveFile(move.QuarantinePath,move.OriginalPath);
        }catch(Exception ex){errors.Add($"{move.OriginalPath}: {ex.Message}");}
        if(state.Moves.Any(m=>m.IsDirectory?!fileSystem.DirectoryExists(m.OriginalPath):!fileSystem.FileExists(m.OriginalPath)))errors.Add("Not every downstream path was restored.");
        if(errors.Count>0)throw new IOException(string.Join("; ",errors));
        if(fileSystem.DirectoryExists(state.QuarantineRoot))fileSystem.DeleteDirectory(state.QuarantineRoot,true);return Task.CompletedTask;
    }
    public Task CommitAsync(Phase1DownstreamInvalidationState state,CancellationToken token){token.ThrowIfCancellationRequested();if(fileSystem.DirectoryExists(state.QuarantineRoot))fileSystem.DeleteDirectory(state.QuarantineRoot,true);return Task.CompletedTask;}
}

/// <summary>Owns the single, lock-free Phase 1 publication transaction.  Its caller owns the lifecycle lease.</summary>
public sealed class Phase1PublicationTransactionCoordinator(IPhase1FileSystem fileSystem,IPhase1AuthorityValidator authorityValidator,IPhase1CompatibilityPublisher compatibilityPublisher,IPhase1ManifestValidator manifestValidator,IPhase1SuccessValidationValidator successValidationValidator):IPhase1PublicationTransactionCoordinator
{
    public async Task<Phase1PublicationTransactionResult> PublishAsync(Phase1PublicationTransactionRequest request,CancellationToken cancellationToken)
    {
        var id=Guid.NewGuid().ToString("N");var root=fileSystem.GetFullPath(request.WorkspaceRoot);var canonical=Path.Combine(root,"01-plan");var compatibility=Path.Combine(root,"plan-input");
        var canonicalStage=Path.Combine(root,$".01-plan.staging-{id}");var compatibilityStage=Path.Combine(root,$".plan-input.staging-{id}");var canonicalBackup=Path.Combine(root,$".01-plan.backup-{id}");var compatibilityBackup=Path.Combine(root,$".plan-input.backup-{id}");var canonicalFailed=Path.Combine(root,$".01-plan.failed-{id}");var compatibilityFailed=Path.Combine(root,$".plan-input.failed-{id}");
        var validation=Path.Combine(root,"validation","phase-01-validation.json");var validationStage=Path.Combine(root,"validation",$".phase-01-validation.staging-{id}.json");var finalValidationStage=Path.Combine(root,"validation",$".phase-01-validation.final-{id}.json");var validationBackup=Path.Combine(root,"validation",$".phase-01-validation.backup-{id}.json");var validationFailed=Path.Combine(root,"validation",$".phase-01-validation.failed-{id}.json");
        var manifest=Path.Combine(root,"phase-manifest.json");var manifestStage=Path.Combine(root,$".phase-manifest.staging-{id}.json");var manifestBackup=Path.Combine(root,$".phase-manifest.backup-{id}.json");var manifestFailed=Path.Combine(root,$".phase-manifest.failed-{id}.json");
        var warnings=new List<string>();var mutated=false;var coherent=false;var phase="P1_CANONICAL_COMMIT_FAILED";Phase1DownstreamInvalidationState? downstream=null;var previousCanonical=fileSystem.DirectoryExists(canonical)?await authorityValidator.ValidateAsync(root,canonical,false,cancellationToken):null;
        var previousValidationExisted=fileSystem.FileExists(validation);var previousValidationChecksum=previousValidationExisted?ChecksumFile(validation):null;
        var previousValidationValidity=previousValidationExisted&&previousCanonical?.IsValid==true&&previousCanonical.AuthoritySet is not null?await successValidationValidator.ValidateAsync(root,previousCanonical.AuthoritySet,cancellationToken):null;
        var previousValidationWasValid=previousValidationValidity?.IsValid==true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();fileSystem.CreateDirectory(canonicalStage);await Write(canonicalStage,"selected-plan.json",request.ExpectedCanonicalAuthority.SelectedPlan,cancellationToken);await Write(canonicalStage,"production-request.json",request.ExpectedCanonicalAuthority.ProductionRequest,cancellationToken);await Write(canonicalStage,"pipeline-state.json",request.ExpectedCanonicalAuthority.PipelineState,cancellationToken);await Write(canonicalStage,"execution-context.json",request.ExpectedCanonicalAuthority.ExecutionContext,cancellationToken);
            var staged=await authorityValidator.ValidateAsync(root,canonicalStage,true,cancellationToken);if(!staged.IsValid||!staged.IsCompatible||!staged.IsDownstreamReady)throw new InvalidOperationException("P1_CANONICAL_COMMIT_FAILED: staged canonical authority is invalid");
            await compatibilityPublisher.StageAsync(compatibilityStage,request.ExpectedCompatibilityPublication,cancellationToken);if(!(await compatibilityPublisher.ValidateDirectoryAsync(compatibilityStage,request.ExpectedCompatibilityPublication,cancellationToken)).IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMIT_FAILED: staged compatibility publication is invalid");
            await request.StageManifestAsync(new(root,canonicalStage,compatibilityStage,manifestStage,id,request.ExpectedCanonicalAuthority,request.ExpectedCompatibilityPublication),cancellationToken);await request.StageProvisionalValidationAsync(new(root,validationStage,id,false,false,"Publishing"),cancellationToken);await ValidateJsonAsync(manifestStage,cancellationToken,"P1_MANIFEST_STAGED_VALIDATION_FAILED");await ValidateJsonAsync(validationStage,cancellationToken,"P1_VALIDATION_STAGED_VALIDATION_FAILED");cancellationToken.ThrowIfCancellationRequested();var token=Phase1PublicationCancellation.NonInterruptible;
            mutated=true;if(fileSystem.DirectoryExists(canonical))fileSystem.MoveDirectory(canonical,canonicalBackup);fileSystem.MoveDirectory(canonicalStage,canonical);phase="P1_CANONICAL_COMMITTED_VALIDATION_FAILED";var committed=await authorityValidator.ValidateAsync(root,canonical,false,token);if(!committed.IsValid||!committed.IsCompatible||!committed.IsDownstreamReady)throw new InvalidOperationException(phase);
            phase="P1_COMPATIBILITY_COMMIT_FAILED";if(fileSystem.DirectoryExists(compatibility))fileSystem.MoveDirectory(compatibility,compatibilityBackup);fileSystem.MoveDirectory(compatibilityStage,compatibility);if(!(await compatibilityPublisher.ValidateAsync(root,request.ExpectedCompatibilityPublication,token)).IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMITTED_VALIDATION_FAILED");
            if(fileSystem.FileExists(manifest))fileSystem.MoveFile(manifest,manifestBackup);fileSystem.MoveFile(manifestStage,manifest);if(fileSystem.FileExists(validation))fileSystem.MoveFile(validation,validationBackup);fileSystem.MoveFile(validationStage,validation);if(!(await manifestValidator.ValidateAsync(root,request.ExpectedCanonicalAuthority,request.ExpectedCompatibilityPublication,token)).IsValid)throw new InvalidOperationException("P1_MANIFEST_COMMITTED_VALIDATION_FAILED");
            if(request.DownstreamInvalidationRequired)try{downstream=await request.DownstreamTransaction.StageAsync(request.Context,id,token);}catch(Phase1DownstreamStagingException ex){downstream=ex.State;throw;}
            await request.StageFinalValidationAsync(new(root,finalValidationStage,id,downstream?.HasMutatedActiveState==true,true,"Succeeded"),token);await ValidateJsonAsync(finalValidationStage,token,"P1_FINAL_VALIDATION_STAGED_VALIDATION_FAILED");fileSystem.MoveFile(validation,validationFailed);fileSystem.MoveFile(finalValidationStage,validation);var success=await successValidationValidator.ValidateAsync(root,request.ExpectedCanonicalAuthority,token);if(!success.IsValid)throw new InvalidOperationException("P1_FINAL_VALIDATION_COMMITTED_VALIDATION_FAILED");coherent=true;
            if(downstream is not null)try{await request.DownstreamTransaction.CommitAsync(downstream,token);}catch(Exception ex){warnings.Add("P1_DOWNSTREAM_QUARANTINE_CLEANUP_WARNING: "+ex.Message);}
            foreach(var d in new[]{canonicalBackup,compatibilityBackup})TryDeleteDirectoryAsWarning(d,warnings);foreach(var f in new[]{validationBackup,validationFailed,manifestBackup})TryDeleteFileAsWarning(f,warnings);return new(true,id,"P1_PUBLICATION_COMMITTED",Files(root),warnings,[],false,false,downstream?.HasMutatedActiveState==true);
        }
        catch(Exception ex) when(mutated&&!coherent)
        {
            var errors=new List<string>();var token=Phase1PublicationCancellation.NonInterruptible;if(downstream?.HasMutatedActiveState==true)try{await request.DownstreamTransaction.RollbackAsync(downstream,token);}catch(Exception e){errors.Add("downstream: "+e.Message);}RestoreFile(manifest,manifestBackup,manifestFailed,errors);RestoreFile(validation,validationBackup,validationFailed,errors);
            try{if(fileSystem.DirectoryExists(compatibility))fileSystem.MoveDirectory(compatibility,compatibilityFailed);if(fileSystem.DirectoryExists(compatibilityBackup))fileSystem.MoveDirectory(compatibilityBackup,compatibility);}catch(Exception e){errors.Add("compatibility: "+e.Message);}try{if(fileSystem.DirectoryExists(canonical))fileSystem.MoveDirectory(canonical,canonicalFailed);if(fileSystem.DirectoryExists(canonicalBackup))fileSystem.MoveDirectory(canonicalBackup,canonical);}catch(Exception e){errors.Add("canonical: "+e.Message);}
            var validationPhysicallyRestored=previousValidationExisted?fileSystem.FileExists(validation)&&ChecksumFile(validation)==previousValidationChecksum:!fileSystem.FileExists(validation);
            if(!validationPhysicallyRestored)errors.Add("restored validation does not match captured original state");
            try{if(request.ExistingCanonicalPublicationExists){var restored=await authorityValidator.ValidateAsync(root,canonical,false,token);if(!restored.IsValid||restored.AuthoritySet is null)errors.Add("restored canonical authority is incoherent");else{var lineage=await compatibilityPublisher.ReadDirectoryAsync(compatibility,token);if(!ChecksumsMatch(lineage.Checksums,restored.AuthoritySet.ExecutionContext.CompatibilityArtifactChecksums)||!(await compatibilityPublisher.ValidateDirectoryAsync(compatibility,lineage,token)).IsValid)errors.Add("restored compatibility lineage is incoherent");if(fileSystem.FileExists(manifest)&&!(await manifestValidator.ValidateAsync(root,restored.AuthoritySet,lineage,token)).IsValid)errors.Add("restored manifest is incoherent");if(previousValidationWasValid&&fileSystem.FileExists(validation)&&!(await successValidationValidator.ValidateAsync(root,restored.AuthoritySet,token)).IsValid)errors.Add("restored validation is incoherent");else if(previousValidationExisted&&!previousValidationWasValid&&validationPhysicallyRestored)warnings.Add("P1_ROLLBACK_RESTORED_PREEXISTING_INVALID_VALIDATION");}}}catch(Exception e){errors.Add("rollback validation: "+e.Message);}var ok=errors.Count==0;return new(false,id,ok?phase:"P1_ROLLBACK_FAILED",[],warnings,[ex.Message,..errors],true,ok,false){PreviousGenerationRestored=ok&&request.ExistingCanonicalPublicationExists,PreviousGenerationWasValid=previousValidationWasValid,FailureDiagnosticsPath=Path.Combine(root,"validation",$".phase-01-failed-attempt-{id}.json")};
        }
        catch(Exception ex){return new(false,id,phase,[],warnings,[ex.Message],false,false,false);}
        finally{foreach(var d in new[]{canonicalStage,compatibilityStage})if(fileSystem.DirectoryExists(d))try{fileSystem.DeleteDirectory(d,true);}catch(Exception e){warnings.Add("P1_STAGING_CLEANUP_WARNING: "+e.Message);}foreach(var f in new[]{validationStage,finalValidationStage,manifestStage})if(fileSystem.FileExists(f))try{fileSystem.DeleteFile(f);}catch(Exception e){warnings.Add("P1_STAGING_CLEANUP_WARNING: "+e.Message);}}
    }
    public async Task<Phase1PublicationTransactionResult> RepairManifestAsync(Phase1ManifestRepairRequest request,CancellationToken cancellationToken)
    {
        var id=Guid.NewGuid().ToString("N");var root=fileSystem.GetFullPath(request.WorkspaceRoot);var active=Path.Combine(root,"phase-manifest.json");var stage=Path.Combine(root,$".phase-manifest.staging-{id}.json");var backup=Path.Combine(root,$".phase-manifest.backup-{id}.json");var failed=Path.Combine(root,$".phase-manifest.failed-{id}.json");var validation=Path.Combine(root,"validation","phase-01-validation.json");var validationStage=Path.Combine(root,"validation",$".phase-01-validation.staging-{id}.json");var validationBackup=Path.Combine(root,"validation",$".phase-01-validation.backup-{id}.json");var validationFailed=Path.Combine(root,"validation",$".phase-01-validation.failed-{id}.json");var started=false;var coherent=false;var previousManifestExisted=fileSystem.FileExists(active);var previousValidationExisted=fileSystem.FileExists(validation);var previousValidationChecksum=previousValidationExisted?ChecksumFile(validation):null;
        try
        {
            await request.StageManifestAsync(new(root,Path.Combine(root,"01-plan"),Path.Combine(root,"plan-input"),stage,id,request.ActiveCanonicalAuthority,request.ActiveCompatibilityPublication),cancellationToken);
            await request.StageFinalValidationAsync(new(root,validationStage,id,false,true,"Succeeded"),cancellationToken);await ValidateJsonAsync(stage,cancellationToken,"P1_MANIFEST_STAGED_VALIDATION_FAILED");await ValidateJsonAsync(validationStage,cancellationToken,"P1_VALIDATION_STAGED_VALIDATION_FAILED");cancellationToken.ThrowIfCancellationRequested();started=true;
            if(fileSystem.FileExists(active))fileSystem.MoveFile(active,backup);fileSystem.MoveFile(stage,active);
            if(!(await manifestValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,request.ActiveCompatibilityPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_MANIFEST_COMMITTED_VALIDATION_FAILED");
            if(fileSystem.FileExists(validation))fileSystem.MoveFile(validation,validationBackup);fileSystem.MoveFile(validationStage,validation);if(!(await successValidationValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_VALIDATION_PUBLICATION_FAILED");var canonical=await authorityValidator.ValidateAsync(root,Path.Combine(root,"01-plan"),false,Phase1PublicationCancellation.NonInterruptible);if(!canonical.IsValid||!(await compatibilityPublisher.ValidateAsync(root,request.ActiveCompatibilityPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_MANIFEST_COMMITTED_VALIDATION_FAILED");coherent=true;var warnings=new List<string>();TryDeleteFileAsWarning(backup,warnings);TryDeleteFileAsWarning(validationBackup,warnings);
            return new(true,id,"P1_MANIFEST_REPAIRED",Files(root),warnings,[],false,false,false);
        }
        catch(Exception ex) when(started)
        {
            var errors=new List<string>();RestoreFile(validation,validationBackup,validationFailed,errors);RestoreFile(active,backup,failed,errors);var restored=previousManifestExisted?fileSystem.FileExists(active)&&!fileSystem.FileExists(backup):!fileSystem.FileExists(active)&&!fileSystem.FileExists(backup);if(restored&&previousManifestExisted&&!(await manifestValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,request.ActiveCompatibilityPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid){errors.Add("restored manifest is not coherent");restored=false;}if(previousValidationExisted?(!fileSystem.FileExists(validation)||ChecksumFile(validation)!=previousValidationChecksum):fileSystem.FileExists(validation)){errors.Add("restored validation does not match captured original state");restored=false;}
            return new(false,id,errors.Count==0&&restored?"P1_MANIFEST_PUBLICATION_FAILED":"P1_ROLLBACK_FAILED",[],[],[ex.Message,..errors],true,errors.Count==0&&restored,false);
        }
        finally{if(coherent){var cleanupWarnings=new List<string>();TryDeleteFileAsWarning(stage,cleanupWarnings);TryDeleteFileAsWarning(validationStage,cleanupWarnings);}else{if(fileSystem.FileExists(stage))try{fileSystem.DeleteFile(stage);}catch(IOException){}if(fileSystem.FileExists(validationStage))try{fileSystem.DeleteFile(validationStage);}catch(IOException){}}}
    }
    public async Task<Phase1PublicationTransactionResult> RepairCompatibilityAsync(Phase1CompatibilityRepairRequest request,CancellationToken cancellationToken)
    {
        var id=Guid.NewGuid().ToString("N");var root=fileSystem.GetFullPath(request.WorkspaceRoot);var active=Path.Combine(root,"plan-input");var stage=Path.Combine(root,$".plan-input.staging-{id}");var backup=Path.Combine(root,$".plan-input.backup-{id}");var failed=Path.Combine(root,$".plan-input.failed-{id}");var manifest=Path.Combine(root,"phase-manifest.json");var manifestStage=Path.Combine(root,$".phase-manifest.staging-{id}.json");var manifestBackup=Path.Combine(root,$".phase-manifest.backup-{id}.json");var manifestFailed=Path.Combine(root,$".phase-manifest.failed-{id}.json");var validation=Path.Combine(root,"validation","phase-01-validation.json");var validationStage=Path.Combine(root,"validation",$".phase-01-validation.staging-{id}.json");var validationBackup=Path.Combine(root,"validation",$".phase-01-validation.backup-{id}.json");var validationFailed=Path.Combine(root,"validation",$".phase-01-validation.failed-{id}.json");var started=false;var coherent=false;var previousValidationExisted=fileSystem.FileExists(validation);var previousValidationChecksum=previousValidationExisted?ChecksumFile(validation):null;
        try
        {
            await compatibilityPublisher.StageAsync(stage,request.ExpectedCompatibilityPublication,cancellationToken);if(!(await compatibilityPublisher.ValidateDirectoryAsync(stage,request.ExpectedCompatibilityPublication,cancellationToken)).IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMIT_FAILED");
            await request.StageManifestAsync(new(root,Path.Combine(root,"01-plan"),stage,manifestStage,id,request.ActiveCanonicalAuthority,request.ExpectedCompatibilityPublication),cancellationToken);await request.StageFinalValidationAsync(new(root,validationStage,id,false,true,"Succeeded"),cancellationToken);await ValidateJsonAsync(manifestStage,cancellationToken,"P1_MANIFEST_STAGED_VALIDATION_FAILED");await ValidateJsonAsync(validationStage,cancellationToken,"P1_VALIDATION_STAGED_VALIDATION_FAILED");cancellationToken.ThrowIfCancellationRequested();started=true;
            if(fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(active,backup);fileSystem.MoveDirectory(stage,active);if(!(await compatibilityPublisher.ValidateAsync(root,request.ExpectedCompatibilityPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMITTED_VALIDATION_FAILED");
            if(fileSystem.FileExists(manifest))fileSystem.MoveFile(manifest,manifestBackup);fileSystem.MoveFile(manifestStage,manifest);if(!(await manifestValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,request.ExpectedCompatibilityPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_MANIFEST_COMMITTED_VALIDATION_FAILED");
            if(fileSystem.FileExists(validation))fileSystem.MoveFile(validation,validationBackup);fileSystem.MoveFile(validationStage,validation);if(!(await successValidationValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_VALIDATION_PUBLICATION_FAILED");var canonical=await authorityValidator.ValidateAsync(root,Path.Combine(root,"01-plan"),false,Phase1PublicationCancellation.NonInterruptible);if(!canonical.IsValid)throw new InvalidOperationException("P1_CANONICAL_COMMIT_FAILED");coherent=true;var warnings=new List<string>();TryDeleteDirectoryAsWarning(backup,warnings);TryDeleteFileAsWarning(manifestBackup,warnings);TryDeleteFileAsWarning(validationBackup,warnings);return new(true,id,"P1_COMPATIBILITY_REPAIRED",Files(root),warnings,[],false,false,false);
        }
        catch(Exception ex) when(started)
        {
            var errors=new List<string>();RestoreFile(validation,validationBackup,validationFailed,errors);RestoreFile(manifest,manifestBackup,manifestFailed,errors);try{if(fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(active,failed);if(fileSystem.DirectoryExists(backup))fileSystem.MoveDirectory(backup,active);var restoredPublication=await compatibilityPublisher.ReadDirectoryAsync(active,Phase1PublicationCancellation.NonInterruptible);if(!ChecksumsMatch(restoredPublication.Checksums,request.ActiveCanonicalAuthority.ExecutionContext.CompatibilityArtifactChecksums)||!(await compatibilityPublisher.ValidateDirectoryAsync(active,restoredPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)errors.Add("restored compatibility is not coherent");var canonicalRestored=await authorityValidator.ValidateAsync(root,Path.Combine(root,"01-plan"),false,Phase1PublicationCancellation.NonInterruptible);if(!canonicalRestored.IsValid||canonicalRestored.AuthoritySet is null||canonicalRestored.AuthoritySet.ExecutionContext.AuthorityChecksum!=request.ActiveCanonicalAuthority.ExecutionContext.AuthorityChecksum)errors.Add("active canonical is not coherent");if(fileSystem.FileExists(manifest)&&!(await manifestValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,restoredPublication,Phase1PublicationCancellation.NonInterruptible)).IsValid)errors.Add("restored manifest is not coherent");if(previousValidationExisted?(!fileSystem.FileExists(validation)||ChecksumFile(validation)!=previousValidationChecksum):fileSystem.FileExists(validation))errors.Add("restored validation does not match captured original state");if(previousValidationExisted&&!(await successValidationValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,Phase1PublicationCancellation.NonInterruptible)).IsValid)errors.Add("restored validation is not coherent");}catch(Exception e){errors.Add(e.Message);}var ok=errors.Count==0;return new(false,id,ok?"P1_COMPATIBILITY_COMMIT_FAILED":"P1_ROLLBACK_FAILED",[],[],[ex.Message,..errors],true,ok,false);
        }
        finally{if(fileSystem.DirectoryExists(stage))try{fileSystem.DeleteDirectory(stage,true);}catch(IOException){}if(fileSystem.FileExists(manifestStage))try{fileSystem.DeleteFile(manifestStage);}catch(IOException){}if(fileSystem.FileExists(validationStage))try{fileSystem.DeleteFile(validationStage);}catch(IOException){}}
    }
    public async Task<Phase1PublicationTransactionResult> RepairValidationAsync(Phase1ValidationRepairRequest request,CancellationToken cancellationToken)
    {
        var id=Guid.NewGuid().ToString("N");var root=fileSystem.GetFullPath(request.WorkspaceRoot);var active=Path.Combine(root,"validation","phase-01-validation.json");var stage=Path.Combine(root,"validation",$".phase-01-validation.staging-{id}.json");var backup=Path.Combine(root,"validation",$".phase-01-validation.backup-{id}.json");var failed=Path.Combine(root,"validation",$".phase-01-validation.failed-{id}.json");var started=false;var existed=fileSystem.FileExists(active);var originalChecksum=existed?ChecksumFile(active):null;
        try
        {
            var canonical=await authorityValidator.ValidateAsync(root,Path.Combine(root,"01-plan"),false,cancellationToken);if(!canonical.IsValid)throw new InvalidOperationException("P1_CANONICAL_COMMIT_FAILED");
            if(!(await compatibilityPublisher.ValidateAsync(root,request.ActiveCompatibilityPublication,cancellationToken)).IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMIT_FAILED");
            if(!(await manifestValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,request.ActiveCompatibilityPublication,cancellationToken)).IsValid)throw new InvalidOperationException("P1_MANIFEST_PUBLICATION_FAILED");
            await request.StageFinalValidationAsync(new(root,stage,id,false,true,"Succeeded"),cancellationToken);await ValidateJsonAsync(stage,cancellationToken,"P1_VALIDATION_STAGED_VALIDATION_FAILED");cancellationToken.ThrowIfCancellationRequested();started=true;
            if(existed)fileSystem.MoveFile(active,backup);fileSystem.MoveFile(stage,active);if(!(await successValidationValidator.ValidateAsync(root,request.ActiveCanonicalAuthority,Phase1PublicationCancellation.NonInterruptible)).IsValid)throw new InvalidOperationException("P1_VALIDATION_PUBLICATION_FAILED");
            var warnings=new List<string>();TryDeleteFileAsWarning(backup,warnings);return new(true,id,"P1_VALIDATION_REPAIRED",Files(root),warnings,[],false,false,false);
        }
        catch(Exception ex) when(started){var errors=new List<string>();RestoreFile(active,backup,failed,errors);var restored=existed?fileSystem.FileExists(active)&&ChecksumFile(active)==originalChecksum:!fileSystem.FileExists(active);return new(false,id,errors.Count==0&&restored?"P1_VALIDATION_PUBLICATION_FAILED":"P1_ROLLBACK_FAILED",[],[],[ex.Message,..errors],true,errors.Count==0&&restored,false);}
        catch(Exception ex){return new(false,id,"P1_VALIDATION_PUBLICATION_FAILED",[],[],[ex.Message],false,false,false);}
        finally{if(fileSystem.FileExists(stage))try{fileSystem.DeleteFile(stage);}catch(IOException){}}
    }
    private void TryDeleteDirectoryAsWarning(string path,List<string> warnings){if(fileSystem.DirectoryExists(path))try{fileSystem.DeleteDirectory(path,true);}catch(Exception ex){warnings.Add($"P1_BACKUP_CLEANUP_WARNING: {path}: {ex.Message}");}}
    private void TryDeleteFileAsWarning(string path,List<string> warnings){if(fileSystem.FileExists(path))try{fileSystem.DeleteFile(path);}catch(Exception ex){warnings.Add($"P1_BACKUP_CLEANUP_WARNING: {path}: {ex.Message}");}}
    private void RestoreFile(string active,string backup,string failed,List<string> errors){try{if(fileSystem.FileExists(active)){if(fileSystem.FileExists(failed))fileSystem.DeleteFile(failed);fileSystem.MoveFile(active,failed);}if(fileSystem.FileExists(backup))fileSystem.MoveFile(backup,active);}catch(Exception e){errors.Add(e.Message);}}
    private string ChecksumFile(string path){using var stream=fileSystem.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    private static bool ChecksumsMatch(IReadOnlyDictionary<string,string> actual,IReadOnlyDictionary<string,string> expected)=>actual.Count==expected.Count&&actual.All(item=>expected.TryGetValue(item.Key,out var checksum)&&string.Equals(item.Value,checksum,StringComparison.Ordinal));
    private async Task ValidateJsonAsync(string path,CancellationToken token,string code){if(!fileSystem.FileExists(path))throw new InvalidOperationException(code);await using var s=fileSystem.OpenRead(path);try{using var d=await JsonDocument.ParseAsync(s,cancellationToken:token);_ = d.RootElement.ValueKind;}catch(JsonException e){throw new InvalidOperationException(code,e);}}
    private Task Write<T>(string root,string name,T value,CancellationToken token)=>fileSystem.WriteAllTextAsync(Path.Combine(root,name),Phase1CanonicalJson.Serialize(value),token);
    private void Retain(string root,string pattern,List<string> warnings){foreach(var path in fileSystem.EnumerateDirectories(root,pattern).OrderByDescending(fileSystem.GetLastWriteTimeUtc).ThenBy(x=>x,StringComparer.Ordinal).Skip(3))try{fileSystem.DeleteDirectory(path,true);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warnings.Add("P1_FAILED_EVIDENCE_CLEANUP_WARNING: "+ex.Message);}}
    private static string[] Files(string root)=>new[]{"01-plan/execution-context.json","01-plan/selected-plan.json","01-plan/production-request.json","01-plan/pipeline-state.json","plan-input/content-plan-production-request.json","plan-input/production-event-intelligence.json"}.Select(x=>Path.Combine(root,x.Replace('/',Path.DirectorySeparatorChar))).ToArray();
}

public sealed class Phase1SuccessValidationValidator(IPhase1FileSystem fileSystem):IPhase1SuccessValidationValidator
{
    public async Task<Phase1SuccessValidationResult> ValidateAsync(string workspaceRoot,Phase1AuthoritySet authority,CancellationToken token)
    {
        var path=Path.Combine(fileSystem.GetFullPath(workspaceRoot),"validation","phase-01-validation.json");
        if(!fileSystem.FileExists(path))return new(false,false,true,false,null,null,null,null,[new("P1_SUCCESS_VALIDATION_MISSING","Committed Phase 1 validation is missing.",path)],[]);
        var errors=new List<Phase1ValidationDiagnostic>();string? status=null,transaction=null,authorityChecksum=null,requestChecksum=null;var committed=false;
        try
        {
            await using var stream=fileSystem.OpenRead(path);using var document=await JsonDocument.ParseAsync(stream,cancellationToken:token);var root=document.RootElement;
            string? String(string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
            status=String("status");transaction=String("transactionId");authorityChecksum=String("authorityChecksum");requestChecksum=String("requestIdentityChecksum");committed=root.TryGetProperty("publicationCommitted",out var c)&&c.ValueKind==JsonValueKind.True;
            if(!root.TryGetProperty("phaseNo",out var phase)||phase.GetInt32()!=1)errors.Add(new("P1_SUCCESS_VALIDATION_PHASE_INVALID","Validation phaseNo must equal 1.",path));
            if(status!="Succeeded" && status!="Skipped")errors.Add(new("P1_SUCCESS_VALIDATION_STATUS_INVALID","Only a succeeded or recognized reuse validation is reusable.",path));
            if(!committed)errors.Add(new("P1_SUCCESS_VALIDATION_NOT_COMMITTED","Validation publication is not committed.",path));
            if(String("validationStatus")!="Valid")errors.Add(new("P1_SUCCESS_VALIDATION_INVALID","validationStatus must be Valid.",path));
            if(String("manifestValidationStatus") is not ("Valid" or "Repaired"))errors.Add(new("P1_SUCCESS_VALIDATION_MANIFEST_INVALID","manifestValidationStatus must be Valid or Repaired.",path));
            if(!string.Equals(authorityChecksum,authority.ExecutionContext.AuthorityChecksum,StringComparison.Ordinal))errors.Add(new("P1_SUCCESS_VALIDATION_AUTHORITY_STALE","Validation authority checksum is stale.",path));
            if(!string.Equals(requestChecksum,authority.ExecutionContext.RequestIdentityChecksum,StringComparison.Ordinal))errors.Add(new("P1_SUCCESS_VALIDATION_REQUEST_STALE","Validation request identity is stale.",path));
            if(string.IsNullOrWhiteSpace(transaction))errors.Add(new("P1_SUCCESS_VALIDATION_TRANSACTION_MISSING","Validation transactionId is required.",path));
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException){errors.Add(new("P1_SUCCESS_VALIDATION_CORRUPT",ex.Message,path));}
        return new(true,errors.Count==0,true,committed,status,transaction,authorityChecksum,requestChecksum,errors,[]);
    }
}

public sealed class Phase1ManifestValidator(IPhase1FileSystem fileSystem) : IPhase1ManifestValidator
{
    public async Task<Phase1ManifestValidationResult> ValidateAsync(string root,Phase1AuthoritySet authority,Phase1CompatibilityPublication compatibility,CancellationToken token)
    {
        var manifestPath=Path.Combine(root,"phase-manifest.json");
        if(!fileSystem.FileExists(manifestPath))return new(false,false,true,[new("P1_MANIFEST_MISSING","Phase manifest is missing.",manifestPath)],[],[]);
        var errors=new List<Phase1ValidationDiagnostic>();var paths=new List<string>();
        try
        {
            await using var stream=fileSystem.OpenRead(manifestPath);using var document=await JsonDocument.ParseAsync(stream,cancellationToken:token);var manifest=document.RootElement;
            if(!manifest.TryGetProperty("planId",out var plan)||!Guid.TryParse(plan.ToString(),out var id)||id!=authority.ExecutionContext.PlanId)errors.Add(new("P1_MANIFEST_PLAN_ID_MISMATCH","Manifest plan ID does not match authority.",manifestPath));
            if(!manifest.TryGetProperty("phase1Artifacts",out var artifacts)||artifacts.ValueKind!=JsonValueKind.Array)return new(true,false,false,[..errors,new("P1_MANIFEST_ENTRIES_MISSING","Phase 1 entries are missing.",manifestPath)],[],[]);
            var items=artifacts.EnumerateArray().ToArray();
            if(items.Length!=Phase1ArtifactCatalog.Required.Count)errors.Add(new("P1_MANIFEST_ENTRY_COUNT","Exactly six Phase 1 entries are required.",manifestPath));
            var workspace=Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(root));var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            var resolved=new List<(JsonElement Item,string Path)>();
            foreach(var item in items)
            {
                if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("path",out var p)||p.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(p.GetString())){errors.Add(new("P1_MANIFEST_PATH_MALFORMED","Every Phase 1 entry requires a string path.",manifestPath));continue;}
                try{var full=fileSystem.GetFullPath(p.GetString()!);paths.Add(full);if(!full.StartsWith(workspace+Path.DirectorySeparatorChar,comparison)||new[]{".staging-",".backup-",".failed-","quarantine","transaction",".phase1-downstream-backup-"}.Any(marker=>full.Contains(marker,comparison)))errors.Add(new("P1_MANIFEST_PATH_UNSAFE","Only contained active publication paths are allowed.",full));resolved.Add((item,full));}
                catch(Exception ex)when(ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException){errors.Add(new("P1_MANIFEST_PATH_MALFORMED",ex.Message,manifestPath));}
            }
            if(paths.Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).Count()!=paths.Count)errors.Add(new("P1_MANIFEST_DUPLICATE_PATH","Duplicate Phase 1 paths are forbidden.",manifestPath));
            foreach(var expected in Phase1ArtifactCatalog.Required)
            {
                var full=fileSystem.GetFullPath(expected.ResolveFinalPath(root));var matches=resolved.Where(i=>string.Equals(i.Path,full,comparison)).Select(i=>i.Item).ToArray();
                if(matches.Length!=1){errors.Add(new("P1_MANIFEST_REQUIRED_ENTRY_MISSING","An exact required Phase 1 path is absent or duplicated.",full));continue;}
                var item=matches[0];if(!full.StartsWith(workspace+Path.DirectorySeparatorChar,comparison)||full.Contains(".staging-",comparison)||full.Contains(".backup-",comparison)||full.Contains(".failed-",comparison))errors.Add(new("P1_MANIFEST_PATH_UNSAFE","Only contained active publication paths are allowed.",full));
                if(!item.TryGetProperty("role",out var role)||role.GetString()!=expected.Role)errors.Add(new("P1_MANIFEST_ROLE_INVALID","Artifact role is invalid.",full));
                if(!item.TryGetProperty("contractVersion",out var contract)||contract.GetString()!=expected.ContractVersion)errors.Add(new("P1_MANIFEST_CONTRACT_INVALID","Artifact contract is invalid.",full));
                if(!item.TryGetProperty("phaseNo",out var phaseNo)||phaseNo.ValueKind!=JsonValueKind.Number||!phaseNo.TryGetInt32(out var number)||number!=1)errors.Add(new("P1_MANIFEST_PHASE_INVALID","Artifact phase is invalid.",full));
                if(!item.TryGetProperty("executionId",out var executionId)||executionId.GetString()!=authority.ExecutionContext.ExecutionId.ToString("D"))errors.Add(new("P1_MANIFEST_EXECUTION_ID_MISMATCH","Artifact execution ID is stale or foreign.",full));
                if(!item.TryGetProperty("planId",out var artifactPlan)||artifactPlan.GetString()!=authority.ExecutionContext.PlanId.ToString("D"))errors.Add(new("P1_MANIFEST_PLAN_ID_MISMATCH","Artifact plan ID is stale.",full));
                if(!item.TryGetProperty("authorityChecksum",out var authorityChecksum)||authorityChecksum.GetString()!=authority.ExecutionContext.AuthorityChecksum)errors.Add(new("P1_MANIFEST_AUTHORITY_CHECKSUM_MISMATCH","Artifact authority checksum is stale.",full));
                if(!item.TryGetProperty("requestIdentityChecksum",out var requestChecksum)||requestChecksum.GetString()!=authority.ExecutionContext.RequestIdentityChecksum)errors.Add(new("P1_MANIFEST_REQUEST_IDENTITY_MISMATCH","Artifact request identity is stale.",full));
                if(!item.TryGetProperty("publicationState",out var publication)||publication.GetString()!="Committed"||!item.TryGetProperty("validationStatus",out var validation)||validation.GetString()!="Valid")errors.Add(new("P1_MANIFEST_PUBLICATION_STATE_INVALID","Artifact is not a validated committed publication.",full));
                if(!fileSystem.FileExists(full))errors.Add(new("P1_MANIFEST_ARTIFACT_MISSING","Declared artifact is missing.",full));
                else {await using var artifactStream=fileSystem.OpenRead(full);if(artifactStream.Length==0)errors.Add(new("P1_MANIFEST_ARTIFACT_EMPTY","Declared artifact is empty.",full));var actual=Convert.ToHexString(await SHA256.HashDataAsync(artifactStream,token)).ToLowerInvariant();if(!item.TryGetProperty("checksum",out var checksum)||checksum.GetString()!=actual)errors.Add(new("P1_MANIFEST_CHECKSUM_MISMATCH","Artifact checksum differs from the committed file.",full));}
            }
            var expectedPaths=Phase1ArtifactCatalog.Required.Select(x=>fileSystem.GetFullPath(x.ResolveFinalPath(root))).ToHashSet(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
            foreach(var unexpected in resolved.Where(x=>!expectedPaths.Contains(x.Path)))errors.Add(new("P1_MANIFEST_UNEXPECTED_ENTRY","Unexpected Phase 1 artifact entry.",unexpected.Path));
            string? Role(JsonElement i)=>i.ValueKind==JsonValueKind.Object&&i.TryGetProperty("role",out var role)&&role.ValueKind==JsonValueKind.String?role.GetString():null;
            if(items.Count(i=>Role(i)=="Authoritative")!=1||items.Count(i=>Role(i)=="Supporting")!=3||items.Count(i=>Role(i)=="Compatibility")!=2)errors.Add(new("P1_MANIFEST_ROLE_CARDINALITY","Role cardinality must be 1/3/2.",manifestPath));
        }
        catch(Exception ex)when(ex is JsonException or IOException or ArgumentException or InvalidOperationException){errors.Add(new("P1_MANIFEST_INVALID",ex.Message,manifestPath));}
        return new(true,errors.Count==0,false,errors,[],paths);
    }
}

public static class Phase1PathSecurity
{
    private static readonly Regex TemporaryName = new(@"^\.01-plan\.(staging|backup)-[0-9a-f]{32}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    public static bool TryValidateRoot(IPhase1FileSystem fs, string workspaceRoot, string authorityRoot, bool allowStaging, out string code)
    {
        code="";
        try
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot)||string.IsNullOrWhiteSpace(authorityRoot)||authorityRoot.Contains('\0')) { code="P1_RESUME_PATH_INVALID"; return false; }
            if (authorityRoot.StartsWith(@"\\",StringComparison.Ordinal)||authorityRoot.StartsWith("//",StringComparison.Ordinal)||authorityRoot.StartsWith(@"\\?\",StringComparison.Ordinal)||HasAlternateDataStream(authorityRoot)){code="P1_PATH_UNSAFE";return false;}
            var workspace=Path.TrimEndingDirectorySeparator(fs.GetFullPath(workspaceRoot)); var candidate=Path.TrimEndingDirectorySeparator(fs.GetFullPath(authorityRoot));
            var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            if (!candidate.StartsWith(workspace+Path.DirectorySeparatorChar,comparison)||!string.Equals(fs.GetDirectoryName(candidate),workspace,comparison)){code="P1_PATH_OUTSIDE_WORKSPACE";return false;}
            var name=fs.GetFileName(candidate); if(name!=Phase1AuthorityContract.DirectoryName && !(allowStaging&&TemporaryName.IsMatch(name))){code="P1_PATH_UNEXPECTED_DIRECTORY";return false;}
            if (fs.DirectoryExists(workspace) && (fs.GetAttributes(workspace)&FileAttributes.ReparsePoint)!=0 || fs.DirectoryExists(candidate)&&(fs.GetAttributes(candidate)&FileAttributes.ReparsePoint)!=0){code="P1_PATH_REPARSE_POINT";return false;}
            return true;
        } catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException){code="P1_PATH_INVALID";return false;}
    }
    public static bool IsApprovedTemporaryName(string name)=>TemporaryName.IsMatch(name);
    private static bool HasAlternateDataStream(string path){var colon=path.IndexOf(':');if(colon<0)return false;return !(OperatingSystem.IsWindows()&&colon==1&&char.IsLetter(path[0]))||path.IndexOf(':',colon+1)>=0;}
}

public sealed class Phase1AuthorityValidator(IPhase1FileSystem fileSystem) : IPhase1AuthorityValidator
{
    public Phase1AuthorityValidator():this(new Phase1FileSystem()){}
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    public async Task<Phase1AuthorityValidationResult> ValidateAsync(string workspaceRoot,string authorityRoot,bool allowStaging,CancellationToken token)
    {
        var structural=new List<Phase1ValidationDiagnostic>();var compatibility=new List<Phase1ValidationDiagnostic>();var readiness=new List<Phase1ValidationDiagnostic>();
        if(!Phase1PathSecurity.TryValidateRoot(fileSystem,workspaceRoot,authorityRoot,allowStaging,out var pathCode))structural.Add(new(pathCode,"Authority path failed canonical security policy.",authorityRoot));
        Phase1AuthoritySet? set=null;
        try{async Task<T?> Read<T>(string name){token.ThrowIfCancellationRequested();var path=Path.Combine(authorityRoot,name);if(!fileSystem.FileExists(path)){structural.Add(new("P1_ARTIFACT_MISSING",$"Required artifact '{name}' is missing.",path));return default;}await using var stream=fileSystem.OpenRead(path);return await JsonSerializer.DeserializeAsync<T>(stream,Options,token);}
            var c=await Read<Phase1ExecutionContext>("execution-context.json");var p=await Read<Phase1SelectedPlan>("selected-plan.json");var r=await Read<Phase1ProductionRequest>("production-request.json");var st=await Read<Phase1PipelineState>("pipeline-state.json");if(c is not null&&p is not null&&r is not null&&st is not null)set=new(c,p,r,st);
        }catch(JsonException ex){structural.Add(new("P1_RESUME_CORRUPT_JSON",ex.Message,authorityRoot));}
        if(set is not null)ValidateSet(set,structural,compatibility,readiness);
        var valid=structural.Count==0;var compatible=compatibility.Count==0;var downstreamReady=valid&&compatible&&readiness.Count==0;
        return new(valid,compatible,valid&&compatible&&downstreamReady,downstreamReady,structural.Concat(compatibility).Concat(readiness).ToArray(),[],set?.ExecutionContext.ContractVersion,set?.ExecutionContext.AuthorityChecksum,set?.ExecutionContext.RequestIdentityChecksum,Phase1AuthorityContract.ProjectorIdentity,set)
        { IsRequestCompatible=valid, IsManifestCompatible=false, IsCompatibilityProjectionValid=false };
    }
    private static void ValidateSet(Phase1AuthoritySet set,List<Phase1ValidationDiagnostic> e,List<Phase1ValidationDiagnostic> c,List<Phase1ValidationDiagnostic> ready)
    {
        var x=set.ExecutionContext;var p=set.SelectedPlan;var r=set.ProductionRequest;var s=set.PipelineState;void R(bool ok,string code,string message,List<Phase1ValidationDiagnostic>? target=null){if(!ok)(target??e).Add(new(code,message));}
        R(x.ContractVersion==Phase1AuthorityContract.ContractVersion,"P1_AUTHORITY_CONTRACT_UNSUPPORTED","Unsupported authority contract.",c);R(p.ContractVersion==Phase1AuthorityContract.SelectedPlanContract,"P1_SELECTED_PLAN_CONTRACT_UNSUPPORTED","Unsupported selected-plan contract.",c);R(r.ContractVersion==Phase1AuthorityContract.ProductionRequestContract,"P1_PRODUCTION_REQUEST_CONTRACT_UNSUPPORTED","Unsupported production-request contract.",c);R(s.ContractVersion==Phase1AuthorityContract.PipelineStateContract,"P1_PIPELINE_STATE_CONTRACT_UNSUPPORTED","Unsupported pipeline-state contract.",c);
        R(x.AuthorityType==Phase1AuthorityContract.AuthorityType&&x.AuthorityVersion==Phase1AuthorityContract.AuthorityVersion,"P1_AUTHORITY_IDENTITY_INVALID","Authority identity invalid.",c);R(x.CgIdentifier==Phase1AuthorityContract.CgIdentifier,"P1_CG_INVALID","CG identifier must be CG1.");R(x.OrchestrationVersion==Phase1AuthorityContract.OrchestrationVersion&&x.ProjectorIdentity==Phase1AuthorityContract.ProjectorIdentity&&x.CanonicalizationIdentity==Phase1AuthorityContract.CanonicalizationIdentity,"P1_RESUME_RUNTIME_INCOMPATIBLE","Runtime identity incompatible.",c);
        R(x.ExecutionId!=Guid.Empty&&x.ExecutionId==r.ExecutionId&&x.ExecutionId==s.ExecutionId,"P1_EXECUTION_ID_MISMATCH","Execution IDs mismatch.");R(x.PlanId!=Guid.Empty&&x.PlanId==x.SelectedPlanId&&x.PlanId==p.PlanId&&x.PlanId==r.PlanId&&x.PlanId==s.PlanId,"P1_PLAN_ID_MISMATCH","Plan IDs mismatch.");R(x.EventIntelligenceId!=Guid.Empty,"P1_EVENT_ID_INVALID","Event intelligence ID missing.");R(x.CanonicalEventIdentity==p.CanonicalEventIdentity,"P1_EVENT_IDENTITY_MISMATCH","Canonical event identity mismatch.");R(x.ResolvedLanguage==p.RequestedLanguage&&x.ResolvedLanguage==r.ResolvedLanguage&&!string.IsNullOrWhiteSpace(x.ResolvedLanguage),"P1_LANGUAGE_MISMATCH","Language mismatch.");
        R(x.RequestedVariants.SequenceEqual(p.RequestedVariants)&&x.RequestedVariants.SequenceEqual(r.RequestedVariants),"P1_VARIANT_MISMATCH","Variants mismatch.");R(x.RequestedOutputs.SequenceEqual(p.RequestedOutputs)&&x.RequestedOutputs.SequenceEqual(r.RequestedOutputs),"P1_OUTPUT_MISMATCH","Outputs mismatch.");
        bool Range(int a,int b)=>a is>=1 and<=20&&b is>=1 and<=20&&a<=b;R(Range(x.RequestedStartPhaseNo,x.RequestedEndPhaseNo),"P1_REQUESTED_PHASE_RANGE_INVALID","Requested range invalid.");R(Range(x.EffectiveStartPhaseNo,x.EffectiveEndPhaseNo),"P1_EFFECTIVE_PHASE_RANGE_INVALID","Effective range invalid.");R(x.RequestedStartPhaseNo==r.RequestedStartPhaseNo&&x.RequestedEndPhaseNo==r.RequestedEndPhaseNo&&x.EffectiveStartPhaseNo==r.EffectiveStartPhaseNo&&x.EffectiveEndPhaseNo==r.EffectiveEndPhaseNo,"P1_PHASE_RANGE_MISMATCH","Ranges mismatch.");IEnumerable<int> planned=Range(x.EffectiveStartPhaseNo,x.EffectiveEndPhaseNo)?Enumerable.Range(x.EffectiveStartPhaseNo,x.EffectiveEndPhaseNo-x.EffectiveStartPhaseNo+1):Array.Empty<int>();R(s.PlannedPhases.SequenceEqual(planned),"P1_PLANNED_PHASES_MISMATCH","Planned phases mismatch.",ready);R(s.ExecutionContextPath=="01-plan/execution-context.json","P1_AUTHORITY_PATH_INVALID","Authority reference invalid.");R(s.InvalidationBoundary==2,"P1_INVALIDATION_BOUNDARY_INVALID","Invalidation boundary invalid.");R(s.Phase1Status=="Initialized","P1_PHASE1_STATE_INVALID","Phase 1 state invalid.",ready);R(!s.DownstreamPhaseStates.Any(v=>v.Value.Equals("Succeeded",StringComparison.OrdinalIgnoreCase)),"P1_FALSE_DOWNSTREAM_SUCCESS","Downstream success asserted.",ready);
        var ps=Phase1CanonicalJson.Checksum(p,nameof(Phase1SelectedPlan.SelectedPlanChecksum));var rs=Phase1CanonicalJson.Checksum(r,nameof(Phase1ProductionRequest.RequestChecksum));var ss=Phase1CanonicalJson.Checksum(s,nameof(Phase1PipelineState.InitializedUtc));R(ps==p.SelectedPlanChecksum&&x.SelectedPlanChecksum==ps,"P1_SELECTED_PLAN_CHECKSUM_INVALID","Selected-plan checksum invalid.");R(rs==r.RequestChecksum&&x.ProductionRequestChecksum==rs,"P1_REQUEST_CHECKSUM_INVALID","Request checksum invalid.");var expected=new Dictionary<string,string>{{"selected-plan.json",ps},{"production-request.json",rs},{"pipeline-state.json",ss}};R(x.SupportingArtifactChecksums.Count==3&&expected.All(k=>x.SupportingArtifactChecksums.TryGetValue(k.Key,out var v)&&v==k.Value),"P1_SUPPORTING_CHECKSUM_INVALID","Supporting checksum map invalid.");var compatibilityPaths=new[]{"plan-input/content-plan-production-request.json","plan-input/production-event-intelligence.json"};R(x.CompatibilityArtifactChecksums.Count==2&&compatibilityPaths.All(path=>x.CompatibilityArtifactChecksums.TryGetValue(path,out var sum)&&sum.Length==64),"P1_COMPATIBILITY_CHECKSUM_LINEAGE_INVALID","Compatibility checksum map must contain exactly the two deterministic projections.");R(Phase1CanonicalJson.Checksum(x.CompatibilityArtifactChecksums)==x.CompatibilityInputChecksum,"P1_COMPATIBILITY_INPUT_CHECKSUM_INVALID","Compatibility checksum lineage does not match.");R(Phase1CanonicalJson.Checksum(new{p.SelectedPlanChecksum,r.RequestChecksum})==x.RequestIdentityChecksum,"P1_REQUEST_IDENTITY_INVALID","Request identity invalid.");R(Phase1CanonicalJson.Checksum(x,nameof(Phase1ExecutionContext.GeneratedUtc),nameof(Phase1ExecutionContext.AuthorityChecksum))==x.AuthorityChecksum,"P1_AUTHORITY_CHECKSUM_INVALID","Authority checksum invalid.");
        var json=Phase1CanonicalJson.Serialize(set);R(!new[]{"apikey","connectionstring","accesstoken","refreshtoken","authorization","sastoken","credential","secret","password"}.Any(term=>json.Contains(term,StringComparison.OrdinalIgnoreCase)),"P1_SECRET_CONTENT","Secret-bearing content detected.");R(!Path.IsPathRooted(x.WorkspaceIdentity),"P1_WORKSPACE_IDENTITY_UNSAFE","Workspace identity must not be absolute.");
    }
}

// Lock-free transactional component. The production lifecycle owns the one workspace lease.
public sealed class Phase1AuthorityPersistence(IPhase1AuthorityValidator validator,IPhase1FileSystem fileSystem,IPhase1ManifestValidator manifestValidator,IPhase1SuccessValidationValidator successValidationValidator):IPhase1AuthorityPersistence,IPhase1AuthorityReader,IPhase1RecoveryService
{
    public Task<Phase1AuthorityValidationResult> ReadAsync(string root,CancellationToken token)=>validator.ValidateAsync(root,Path.Combine(root,Phase1AuthorityContract.DirectoryName),false,token);
    [Obsolete("Phase 1 publications must use IPhase1PublicationTransactionCoordinator.", true)]
    public Task<Phase1PersistenceResult> PersistAsync(string workspaceRoot,Phase1AuthoritySet authority,bool overwrite,CancellationToken token)
        => throw new InvalidOperationException("P1_TRANSACTION_COORDINATOR_REQUIRED");

    public async Task<Phase1RecoveryResult> RecoverAsync(string outputRoot,Phase1AuthoritySet expectedAuthority,Phase1CompatibilityPublication expectedCompatibility,CancellationToken token)
    {
        var root=fileSystem.GetFullPath(outputRoot);var warnings=new List<string>();var removedStaging=new List<string>();var removedBackups=new List<string>();var isolated=new List<string>();
        var activeCanonical=Path.Combine(root,"01-plan");var activeCompatibility=Path.Combine(root,"plan-input");var activeManifest=Path.Combine(root,"phase-manifest.json");var activeValidation=Path.Combine(root,"validation","phase-01-validation.json");var compatibilityPublisher=new Phase1CompatibilityPublisher(fileSystem);
        token.ThrowIfCancellationRequested();var canonicalValidation=await validator.ValidateAsync(root,activeCanonical,false,token);var activeLineage=canonicalValidation.AuthoritySet is null?expectedCompatibility:new Phase1CompatibilityPublication(expectedCompatibility.Payloads,canonicalValidation.AuthoritySet.ExecutionContext.CompatibilityArtifactChecksums);var compatibilityValidation=await compatibilityPublisher.ValidateAsync(root,activeLineage,token);
        foreach(var pattern in new[]{".01-plan.staging-*",".plan-input.staging-*"})foreach(var staging in fileSystem.EnumerateDirectories(root,pattern))try{fileSystem.DeleteDirectory(staging,true);removedStaging.Add(staging);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warnings.Add($"P1_RECOVERY_CLEANUP_WARNING: {staging}: {ex.Message}");}
        if(canonicalValidation.IsValid&&canonicalValidation.IsCompatible&&canonicalValidation.IsDownstreamReady&&compatibilityValidation.IsValid)return Finalize(new("Valid",false,null,removedStaging,removedBackups,warnings,[]));
        static string? Id(string path,string marker){var name=Path.GetFileName(path);var at=name.IndexOf(marker,StringComparison.Ordinal);return at<0?null:name[(at+marker.Length)..].Replace(".json",string.Empty,StringComparison.OrdinalIgnoreCase);}
        var canonicalBackups=fileSystem.EnumerateDirectories(root,".01-plan.backup-*").Select(p=>(Path:p,Id:Id(p,".01-plan.backup-"))).Where(x=>x.Id is not null).ToArray();
        var compatibilityBackups=fileSystem.EnumerateDirectories(root,".plan-input.backup-*").Select(p=>(Path:p,Id:Id(p,".plan-input.backup-"))).Where(x=>x.Id is not null).ToDictionary(x=>x.Id!,x=>x.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in canonicalBackups.Where(x=>compatibilityBackups.ContainsKey(x.Id!)).OrderByDescending(x=>fileSystem.GetLastWriteTimeUtc(x.Path)).ThenByDescending(x=>x.Path,StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();var id=pair.Id!;var compatibilityBackup=compatibilityBackups[id];var cv=await validator.ValidateAsync(root,pair.Path,true,token);if(cv.AuthoritySet is null)continue;var candidateCompatibility=new Phase1CompatibilityPublication(expectedCompatibility.Payloads,cv.AuthoritySet.ExecutionContext.CompatibilityArtifactChecksums);var pv=await compatibilityPublisher.ValidateDirectoryAsync(compatibilityBackup,candidateCompatibility,token);if(!cv.IsValid||!cv.IsCompatible||!cv.IsDownstreamReady||!pv.IsValid)continue;
            var manifestCandidate=Path.Combine(root,$".phase-manifest.backup-{id}.json");var validationCandidate=Path.Combine(root,"validation",$".phase-01-validation.backup-{id}.json");
            var originalCanonical=Path.Combine(root,$".01-plan.original-recovery-{id}");var originalCompatibility=Path.Combine(root,$".plan-input.original-recovery-{id}");var originalManifest=Path.Combine(root,$".phase-manifest.original-recovery-{id}.json");var originalValidation=Path.Combine(root,"validation",$".phase-01-validation.original-recovery-{id}.json");
            var failedCanonical=Path.Combine(root,$".01-plan.failed-recovery-{id}");var failedCompatibility=Path.Combine(root,$".plan-input.failed-recovery-{id}");var failedManifest=Path.Combine(root,$".phase-manifest.failed-recovery-{id}.json");var failedValidation=Path.Combine(root,"validation",$".phase-01-validation.failed-recovery-{id}.json");
            var canonicalExisted=fileSystem.DirectoryExists(activeCanonical);var compatibilityExisted=fileSystem.DirectoryExists(activeCompatibility);var manifestExisted=fileSystem.FileExists(activeManifest);var validationExisted=fileSystem.FileExists(activeValidation);var mutated=false;
            try
            {
                EnsureDestinationAbsent(originalCanonical,true);EnsureDestinationAbsent(originalCompatibility,true);EnsureDestinationAbsent(originalManifest,false);EnsureDestinationAbsent(originalValidation,false);
                if(canonicalExisted){fileSystem.MoveDirectory(activeCanonical,originalCanonical);isolated.Add(originalCanonical);}if(compatibilityExisted){fileSystem.MoveDirectory(activeCompatibility,originalCompatibility);isolated.Add(originalCompatibility);}if(manifestExisted){fileSystem.MoveFile(activeManifest,originalManifest);isolated.Add(originalManifest);}if(validationExisted){fileSystem.MoveFile(activeValidation,originalValidation);isolated.Add(originalValidation);}mutated=true;
                fileSystem.MoveDirectory(pair.Path,activeCanonical);fileSystem.MoveDirectory(compatibilityBackup,activeCompatibility);
                if(fileSystem.FileExists(manifestCandidate))fileSystem.MoveFile(manifestCandidate,activeManifest);if(fileSystem.FileExists(validationCandidate))fileSystem.MoveFile(validationCandidate,activeValidation);
                var restoredCanonical=await validator.ValidateAsync(root,activeCanonical,false,Phase1PublicationCancellation.NonInterruptible);if(restoredCanonical.AuthoritySet is null||!restoredCanonical.IsValid||!restoredCanonical.IsCompatible||!restoredCanonical.IsDownstreamReady)throw new InvalidOperationException("canonical: restored authority failed semantic validation");
                var restoredLineage=new Phase1CompatibilityPublication(expectedCompatibility.Payloads,restoredCanonical.AuthoritySet.ExecutionContext.CompatibilityArtifactChecksums);var restoredCompatibility=await compatibilityPublisher.ValidateAsync(root,restoredLineage,Phase1PublicationCancellation.NonInterruptible);if(!restoredCompatibility.IsValid)throw new InvalidOperationException("compatibility: restored lineage failed semantic validation");
                var manifestRecovered=false;var validationRecovered=false;var metadataInvalid=new List<string>();
                if(fileSystem.FileExists(activeManifest))
                {
                    var transactionMatches=await TransactionMatchesAsync(activeManifest,id,required:false,Phase1PublicationCancellation.NonInterruptible);var semantic=transactionMatches&& (await manifestValidator.ValidateAsync(root,restoredCanonical.AuthoritySet,restoredLineage,Phase1PublicationCancellation.NonInterruptible)).IsValid;
                    if(semantic)manifestRecovered=true;else{MoveEvidence(activeManifest,failedManifest,metadataInvalid);}
                }
                if(fileSystem.FileExists(activeValidation))
                {
                    var transactionMatches=await TransactionMatchesAsync(activeValidation,id,required:true,Phase1PublicationCancellation.NonInterruptible);var semantic=transactionMatches&&(await successValidationValidator.ValidateAsync(root,restoredCanonical.AuthoritySet,Phase1PublicationCancellation.NonInterruptible)).IsValid;
                    if(semantic)validationRecovered=true;else{MoveEvidence(activeValidation,failedValidation,metadataInvalid);}
                }
                isolated.RemoveAll(p=>p==originalCanonical||p==originalCompatibility||p==originalManifest||p==originalValidation);warnings.Add("P1_RECOVERY_BACKUP_PAIR_RESTORED");
                return Finalize(new("Valid",true,pair.Path,removedStaging,removedBackups,warnings,[]){CompatibilityRecovered=true,CanonicalBackupPath=pair.Path,CompatibilityBackupPath=compatibilityBackup,TransactionId=id,IsolatedInvalidPaths=isolated.Concat(metadataInvalid).ToArray(),MetadataInvalidatedPaths=metadataInvalid,ManifestRecovered=manifestRecovered,ValidationRecovered=validationRecovered,ManifestRepairRequired=!manifestRecovered,ValidationRepairRequired=!validationRecovered,ManifestBackupPath=manifestCandidate,ValidationBackupPath=validationCandidate});
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                if(!mutated)continue;var restoreErrors=new List<string>();
                RestoreDirectoryState("canonical",activeCanonical,pair.Path,failedCanonical,originalCanonical,canonicalExisted,restoreErrors);RestoreDirectoryState("compatibility",activeCompatibility,compatibilityBackup,failedCompatibility,originalCompatibility,compatibilityExisted,restoreErrors);RestoreFileState("manifest",activeManifest,manifestCandidate,failedManifest,originalManifest,manifestExisted,restoreErrors);RestoreFileState("validation",activeValidation,validationCandidate,failedValidation,originalValidation,validationExisted,restoreErrors);
                var restored=restoreErrors.Count==0&&Exists(activeCanonical,true)==canonicalExisted&&Exists(activeCompatibility,true)==compatibilityExisted&&Exists(activeManifest,false)==manifestExisted&&Exists(activeValidation,false)==validationExisted;
                return Finalize(new("Invalid",false,null,removedStaging,removedBackups,warnings,["P1_RECOVERY_FAILED: "+ex.Message,..restoreErrors]){CanonicalBackupPath=pair.Path,CompatibilityBackupPath=compatibilityBackup,TransactionId=id,IsolatedInvalidPaths=isolated.Where(p=>Exists(p,DirectoryLike(p))).ToArray(),OriginalActiveRestoredOnRecoveryFailure=restored});
            }
        }
        if(canonicalBackups.Length>0||compatibilityBackups.Count>0)warnings.Add("P1_RECOVERY_NO_MATCHING_BACKUP_PAIR");
        return Finalize(new(canonicalValidation.AuthoritySet is null?"Missing":"Invalid",false,null,removedStaging,removedBackups,warnings,[]){IsolatedInvalidPaths=isolated});

        bool DirectoryLike(string p)=>!p.EndsWith(".json",StringComparison.OrdinalIgnoreCase);bool Exists(string p,bool directory)=>directory?fileSystem.DirectoryExists(p):fileSystem.FileExists(p);
        void EnsureDestinationAbsent(string path,bool directory){if(Exists(path,directory))throw new IOException($"recovery evidence destination already exists: {path}");}
        void MoveEvidence(string active,string failed,List<string> evidence){EnsureDestinationAbsent(failed,false);fileSystem.MoveFile(active,failed);evidence.Add(failed);}
        void RestoreDirectoryState(string component,string active,string candidateBackup,string failed,string original,bool existed,List<string> errors){try{if(fileSystem.DirectoryExists(active)){EnsureDestinationAbsent(failed,true);fileSystem.MoveDirectory(active,failed);}if(existed){if(!fileSystem.DirectoryExists(original))throw new IOException($"isolated original is missing: {original}");fileSystem.MoveDirectory(original,active);}else if(fileSystem.DirectoryExists(active))throw new IOException("active path must remain absent");}catch(Exception e){errors.Add($"P1_RECOVERY_ORIGINAL_RESTORE_FAILED: {component}: {e.Message}");}}
        void RestoreFileState(string component,string active,string candidateBackup,string failed,string original,bool existed,List<string> errors){try{if(fileSystem.FileExists(active)){EnsureDestinationAbsent(failed,false);fileSystem.MoveFile(active,failed);}if(existed){if(!fileSystem.FileExists(original))throw new IOException($"isolated original is missing: {original}");fileSystem.MoveFile(original,active);}else if(fileSystem.FileExists(active))throw new IOException("active path must remain absent");}catch(Exception e){errors.Add($"P1_RECOVERY_ORIGINAL_RESTORE_FAILED: {component}: {e.Message}");}}
        async Task<bool> TransactionMatchesAsync(string path,string expected,bool required,CancellationToken ct){try{await using var stream=fileSystem.OpenRead(path);using var document=await JsonDocument.ParseAsync(stream,cancellationToken:ct);if(!document.RootElement.TryGetProperty("transactionId",out var value)||value.ValueKind!=JsonValueKind.String)return !required;return string.Equals(value.GetString(),expected,StringComparison.OrdinalIgnoreCase);}catch(JsonException){return false;}}
        Phase1RecoveryResult Finalize(Phase1RecoveryResult result){var contradictions=new List<string>();if(result.ManifestRecovered&&result.ManifestRepairRequired)contradictions.Add("manifest recovered and repair-required flags conflict");if(result.ValidationRecovered&&result.ValidationRepairRequired)contradictions.Add("validation recovered and repair-required flags conflict");if(result.Recovered&&(!result.CompatibilityRecovered||result.ActiveAuthorityState!="Valid"))contradictions.Add("recovered authority pair is not semantically coherent");if(result.OriginalActiveRestoredOnRecoveryFailure&&result.IsolatedInvalidPaths.Any(p=>p.Contains("original-recovery-",StringComparison.Ordinal)))contradictions.Add("original active component remains isolated");return contradictions.Count==0?result:result with{ActiveAuthorityState="Invalid",Recovered=false,CompatibilityRecovered=false,Errors=[..result.Errors,..contradictions.Select(x=>"P1_RECOVERY_FAILED: invariant: "+x)]};}
    }

    private Task Write<T>(string root,string name,T value,CancellationToken token)=>fileSystem.WriteAllTextAsync(Path.Combine(root,name),Phase1CanonicalJson.Serialize(value),token);private static string[] Paths(string root)=>new[] { "execution-context.json", "selected-plan.json", "production-request.json", "pipeline-state.json" }.Select(x=>Path.Combine(root,x)).ToArray();
}
