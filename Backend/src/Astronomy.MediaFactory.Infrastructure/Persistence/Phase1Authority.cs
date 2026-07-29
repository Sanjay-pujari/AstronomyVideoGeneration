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
public enum Phase1ExecutionKind { Generated, Reused, RegeneratedDueToMissingAuthority, RegeneratedDueToIncompleteAuthority, RegeneratedDueToCorruptAuthority, RegeneratedDueToChecksumMismatch, RegeneratedDueToRequestChange, RegeneratedDueToRuntimeIncompatibility, RegeneratedDueToManifestMismatch, RecoveredAndReused, RecoveredAndRegenerated, CompatibilityRepaired, Failed }
public sealed record Phase1ResumeEvaluation(bool CanReuse, string ReasonCode, string Reason, Phase1AuthorityValidationResult Validation, Phase1AuthoritySet? ExistingAuthority, IReadOnlyList<string> Warnings);
public sealed record Phase1RecoveryResult(string ActiveAuthorityState, bool Recovered, string? RestoredBackupPath, IReadOnlyList<string> RemovedStagingPaths, IReadOnlyList<string> RemovedBackupPaths, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public IReadOnlyList<string> IsolatedInvalidPaths { get; init; } = [];
    public bool CompatibilityRecovered { get; init; }
    public bool ManifestRepairRequired { get; init; }
}
public sealed record Phase1ExecutionOutcome(Phase1ExecutionKind Kind, string ReasonCode, string Reason, IReadOnlyList<string> OutputFiles, IReadOnlyList<string> Warnings, string? AuthorityChecksum, string? RequestIdentityChecksum, bool Reused, bool ReplacedExistingAuthority, bool DownstreamInvalidated, string CompatibilityProjectionStatus, Phase1RecoveryResult RecoveryStatus)
{
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string ManifestStatus { get; init; } = "Pending";
    public string ValidationStatus { get; init; } = "Pending";
}

public sealed record Phase1CompatibilityPublication(IReadOnlyDictionary<string,string> Payloads, IReadOnlyDictionary<string,string> Checksums);
public sealed record Phase1CompatibilityValidationResult(bool IsValid, bool IsMissing, IReadOnlyList<Phase1ValidationDiagnostic> Errors);
public interface IPhase1ResumeEvaluator { Phase1ResumeEvaluation Evaluate(Phase1AuthoritySet expected, Phase1AuthorityValidationResult existing, bool manifestCompatible, Phase1CompatibilityValidationResult compatibility, Phase1RecoveryResult recovery); }
public interface IPhase1CompatibilityPublisher
{
    Phase1CompatibilityPublication Project(ProductionPhaseContext context);
    Task<Phase1CompatibilityValidationResult> ValidateAsync(string workspaceRoot, Phase1CompatibilityPublication expected, CancellationToken token);
    Task<IReadOnlyList<string>> PublishAsync(string workspaceRoot, Phase1CompatibilityPublication publication, CancellationToken token);
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

public interface IPhase1FileSystem
{
    bool FileExists(string path); bool DirectoryExists(string path); void CreateDirectory(string path); void DeleteDirectory(string path, bool recursive); void MoveDirectory(string source, string destination);
    IEnumerable<string> EnumerateDirectories(string path, string pattern); IEnumerable<string> EnumerateFiles(string path, string pattern); Stream OpenRead(string path); Task WriteAllTextAsync(string path, string contents, CancellationToken token);
    string GetFullPath(string path); string GetFileName(string path); string? GetDirectoryName(string path); DateTimeOffset GetLastWriteTimeUtc(string path); FileAttributes GetAttributes(string path);
}
public sealed class Phase1FileSystem : IPhase1FileSystem
{
    public bool FileExists(string p)=>File.Exists(p); public bool DirectoryExists(string p)=>Directory.Exists(p); public void CreateDirectory(string p)=>Directory.CreateDirectory(p); public void DeleteDirectory(string p,bool r)=>Directory.Delete(p,r); public void MoveDirectory(string s,string d)=>Directory.Move(s,d);
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
public interface IPhase1AuthorityPersistence { Task<Phase1PersistenceResult> PersistAsync(string workspaceRoot, Phase1AuthoritySet authority, bool overwrite, CancellationToken cancellationToken); }
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
    public async Task<IReadOnlyList<string>> PublishAsync(string root,Phase1CompatibilityPublication publication,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var id=Guid.NewGuid().ToString("N");var active=Path.Combine(root,"plan-input");var staging=Path.Combine(root,$".plan-input.staging-{id}");var backup=Path.Combine(root,$".plan-input.backup-{id}");fileSystem.CreateDirectory(staging);
        try
        {
            foreach(var item in publication.Payloads)await fileSystem.WriteAllTextAsync(Path.Combine(staging,Path.GetFileName(item.Key)),item.Value,token);
            var staged=new Phase1CompatibilityPublication(publication.Payloads.ToDictionary(x=>Path.GetFileName(x.Key),x=>x.Value),publication.Checksums.ToDictionary(x=>Path.GetFileName(x.Key),x=>x.Value));
            foreach(var item in staged.Payloads){var path=Path.Combine(staging,item.Key);await using var s=fileSystem.OpenRead(path);using var reader=new StreamReader(s,Encoding.UTF8);var text=await reader.ReadToEndAsync(token);var sum=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();if(sum!=staged.Checksums[item.Key])throw new InvalidOperationException("P1_COMPATIBILITY_STAGED_VALIDATION_FAILED");}
            token.ThrowIfCancellationRequested();var had=fileSystem.DirectoryExists(active);if(had)fileSystem.MoveDirectory(active,backup);
            try{fileSystem.MoveDirectory(staging,active);var committed=await ValidateAsync(root,publication,new CancellationToken(false));if(!committed.IsValid)throw new InvalidOperationException("P1_COMPATIBILITY_COMMITTED_VALIDATION_FAILED");if(fileSystem.DirectoryExists(backup))fileSystem.DeleteDirectory(backup,true);}
            catch{if(fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(active,Path.Combine(root,$".plan-input.failed-{id}"));if(had&&fileSystem.DirectoryExists(backup))fileSystem.MoveDirectory(backup,active);throw;}
            return publication.Payloads.Keys.Select(x=>Path.Combine(root,x.Replace('/',Path.DirectorySeparatorChar))).ToArray();
        }
        finally{if(fileSystem.DirectoryExists(staging))fileSystem.DeleteDirectory(staging,true);}
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
public sealed class Phase1AuthorityPersistence(IPhase1AuthorityValidator validator,IPhase1FileSystem fileSystem):IPhase1AuthorityPersistence,IPhase1AuthorityReader
{
    public Task<Phase1AuthorityValidationResult> ReadAsync(string root,CancellationToken token)=>validator.ValidateAsync(root,Path.Combine(root,Phase1AuthorityContract.DirectoryName),false,token);
    public async Task<Phase1PersistenceResult> PersistAsync(string workspaceRoot,Phase1AuthoritySet authority,bool overwrite,CancellationToken token)
    {
        var root=fileSystem.GetFullPath(workspaceRoot);var warnings=new List<string>();await RecoverAsync(root,warnings,token);var active=Path.Combine(root,Phase1AuthorityContract.DirectoryName);var existing=await validator.ValidateAsync(root,active,false,token);
        if(!overwrite&&existing.IsReusable&&existing.IsDownstreamReady&&existing.RequestIdentityChecksum==authority.ExecutionContext.RequestIdentityChecksum)return new(true,Paths(active),existing,warnings);
        token.ThrowIfCancellationRequested();var id=Guid.NewGuid().ToString("N");var staging=Path.Combine(root,$".01-plan.staging-{id}");var backup=Path.Combine(root,$".01-plan.backup-{id}");fileSystem.CreateDirectory(staging);
        try{await Write(staging,"selected-plan.json",authority.SelectedPlan,token);await Write(staging,"production-request.json",authority.ProductionRequest,token);await Write(staging,"pipeline-state.json",authority.PipelineState,token);await Write(staging,"execution-context.json",authority.ExecutionContext,token);var staged=await validator.ValidateAsync(root,staging,true,token);if(!staged.IsValid||!staged.IsCompatible||!staged.IsDownstreamReady)throw new InvalidOperationException("P1_STAGED_VALIDATION_FAILED: "+string.Join(';',staged.Errors.Select(x=>x.Code)));token.ThrowIfCancellationRequested();
            var hadActive=fileSystem.DirectoryExists(active);if(hadActive)fileSystem.MoveDirectory(active,backup);try{fileSystem.MoveDirectory(staging,active);}catch{if(hadActive&&fileSystem.DirectoryExists(backup)&&!fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(backup,active);throw;}
            // Directory renames form a deliberately non-interruptible transaction. Cancellation resumes after a valid active set exists.
            var committed=await validator.ValidateAsync(root,active,false,new CancellationToken(canceled: false));if(!committed.IsValid||!committed.IsCompatible||!committed.IsDownstreamReady){var failed=Path.Combine(root,$".01-plan.failed-{id}");if(fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(active,failed);if(hadActive&&fileSystem.DirectoryExists(backup))fileSystem.MoveDirectory(backup,active);var restored=hadActive?await validator.ValidateAsync(root,active,false,new CancellationToken(canceled: false)):null;if(hadActive&&(restored is null||!restored.IsValid||!restored.IsDownstreamReady))throw new InvalidOperationException("P1_ROLLBACK_VALIDATION_FAILED");throw new InvalidOperationException("P1_COMMITTED_VALIDATION_FAILED_ROLLBACK_RESTORED");}
            if(fileSystem.DirectoryExists(backup))fileSystem.DeleteDirectory(backup,true);token.ThrowIfCancellationRequested();return new(false,Paths(active),committed,warnings);
        }finally{if(fileSystem.DirectoryExists(staging))try{fileSystem.DeleteDirectory(staging,true);}catch(IOException ex){warnings.Add("P1_STAGING_CLEANUP_WARNING: "+ex.Message);}}
    }
    private async Task RecoverAsync(string root,List<string>warnings,CancellationToken token){token.ThrowIfCancellationRequested();var active=Path.Combine(root,"01-plan");var activeValidation=await validator.ValidateAsync(root,active,false,token);var staging=fileSystem.EnumerateDirectories(root,".01-plan.staging-*").Where(x=>Phase1PathSecurity.IsApprovedTemporaryName(fileSystem.GetFileName(x))).ToArray();foreach(var s in staging){token.ThrowIfCancellationRequested();try{fileSystem.DeleteDirectory(s,true);}catch(IOException ex){warnings.Add("P1_RECOVERY_CLEANUP_WARNING: "+ex.Message);}}
        var backups=fileSystem.EnumerateDirectories(root,".01-plan.backup-*").Where(x=>Phase1PathSecurity.IsApprovedTemporaryName(fileSystem.GetFileName(x))).OrderByDescending(fileSystem.GetLastWriteTimeUtc).ThenByDescending(x=>x,StringComparer.Ordinal).ToArray();if(activeValidation.IsValid&&activeValidation.IsDownstreamReady){foreach(var b in backups)try{fileSystem.DeleteDirectory(b,true);}catch(IOException ex){warnings.Add("P1_RECOVERY_CLEANUP_WARNING: "+ex.Message);}return;}foreach(var b in backups){token.ThrowIfCancellationRequested();var v=await validator.ValidateAsync(root,b,true,token);if(!v.IsValid||!v.IsCompatible||!v.IsDownstreamReady)continue;if(fileSystem.DirectoryExists(active))fileSystem.MoveDirectory(active,Path.Combine(root,$".01-plan.failed-{Guid.NewGuid():N}"));fileSystem.MoveDirectory(b,active);var restored=await validator.ValidateAsync(root,active,false,token);if(!restored.IsValid||!restored.IsDownstreamReady)throw new InvalidOperationException("P1_RECOVERY_RESTORED_VALIDATION_FAILED");warnings.Add("P1_RECOVERY_BACKUP_RESTORED");break;}}
    private Task Write<T>(string root,string name,T value,CancellationToken token)=>fileSystem.WriteAllTextAsync(Path.Combine(root,name),Phase1CanonicalJson.Serialize(value),token);private static string[] Paths(string root)=>["execution-context.json","selected-plan.json","production-request.json","pipeline-state.json"].Select(x=>Path.Combine(root,x)).ToArray();
}
