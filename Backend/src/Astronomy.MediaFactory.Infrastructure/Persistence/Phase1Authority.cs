using System.Collections.Concurrent;
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
public sealed record Phase1ExecutionContext(string ContractVersion, string AuthorityType, string AuthorityVersion, string CgIdentifier, string OrchestrationVersion, string ProjectorIdentity, string CanonicalizationIdentity, Guid ExecutionId, Guid PlanId, Guid SelectedPlanId, Guid EventIntelligenceId, string CanonicalEventIdentity, string EventType, string RequestedLanguage, string ResolvedLanguage, IReadOnlyList<string> RequestedVariants, IReadOnlyList<string> RequestedOutputs, int RequestedStartPhaseNo, int RequestedEndPhaseNo, int EffectiveStartPhaseNo, int EffectiveEndPhaseNo, string ExecutionMode, bool DryRun, bool OverwriteExisting, bool RetryFailedOnly, string WorkspaceIdentity, string SelectedPlanChecksum, string ProductionRequestChecksum, string CompatibilityInputChecksum, string RequestIdentityChecksum, IReadOnlyDictionary<string, string> SupportingArtifactChecksums, DateTimeOffset GeneratedUtc, string AuthorityChecksum);
public sealed record Phase1AuthoritySet(Phase1ExecutionContext ExecutionContext, Phase1SelectedPlan SelectedPlan, Phase1ProductionRequest ProductionRequest, Phase1PipelineState PipelineState);
public sealed record Phase1ValidationDiagnostic(string Code, string Message, string? Path = null);
public sealed record Phase1AuthorityValidationResult(bool IsValid, bool IsCompatible, bool IsReusable, bool IsDownstreamReady, IReadOnlyList<Phase1ValidationDiagnostic> Errors, IReadOnlyList<Phase1ValidationDiagnostic> Warnings, string? ContractVersion, string? AuthorityChecksum, string? RequestIdentityChecksum, string RuntimeIdentity, Phase1AuthoritySet? AuthoritySet = null);
public sealed record Phase1PersistenceResult(bool Reused, IReadOnlyList<string> Files, Phase1AuthorityValidationResult Validation, IReadOnlyList<string> Warnings);

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
        var compatibility = Phase1CanonicalJson.Checksum(new { request.PlanId, language, variants, outputs, canonicalEvent });
        var authority = new Phase1ExecutionContext(Phase1AuthorityContract.ContractVersion, Phase1AuthorityContract.AuthorityType, Phase1AuthorityContract.AuthorityVersion, Phase1AuthorityContract.CgIdentifier, Phase1AuthorityContract.OrchestrationVersion, Phase1AuthorityContract.ProjectorIdentity, Phase1AuthorityContract.CanonicalizationIdentity, executionId, request.PlanId, request.PlanId, context.AstronomyEventIntelligenceId, canonicalEvent, request.EventType.Trim(), language, language, variants, outputs, requestedStart, requestedEnd, context.StartPhaseNo, context.EndPhaseNo, context.ExecutionMode.ToString(), false, context.OverwriteExisting, context.RetryFailedOnly, request.PlanId.ToString("D"), selected.SelectedPlanChecksum, production.RequestChecksum, compatibility, requestIdentity, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["pipeline-state.json"] = stateChecksum, ["production-request.json"] = production.RequestChecksum, ["selected-plan.json"] = selected.SelectedPlanChecksum }, generatedUtc, "");
        authority = authority with { AuthorityChecksum = Phase1CanonicalJson.Checksum(authority, nameof(Phase1ExecutionContext.GeneratedUtc), nameof(Phase1ExecutionContext.AuthorityChecksum)) };
        return new(authority, selected, production, state);
    }
    private static string[] Normalize(IEnumerable<string> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static string[] NormalizeVariants(IEnumerable<string> outputs) { var n = Normalize(outputs); var r = new List<string>(); if (n.Any(x => x.Contains("long", StringComparison.Ordinal))) r.Add("long"); if (n.Any(x => x.Contains("short", StringComparison.Ordinal))) r.Add("short"); return r.Count == 0 ? ["long", "short"] : r.ToArray(); }
}

public sealed class Phase1AuthorityValidator : IPhase1AuthorityValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public async Task<Phase1AuthorityValidationResult> ValidateAsync(string workspaceRoot, string authorityRoot, bool allowStaging, CancellationToken token)
    {
        var errors = new List<Phase1ValidationDiagnostic>(); var fullWorkspace = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var fullRoot = Path.GetFullPath(authorityRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullRoot.StartsWith(fullWorkspace, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) errors.Add(new("P1_PATH_OUTSIDE_WORKSPACE", "Authority path is outside its workspace.", authorityRoot));
        var name = Path.GetFileName(fullRoot);
        if (!(name == Phase1AuthorityContract.DirectoryName || allowStaging && name.StartsWith(".01-plan.staging-", StringComparison.Ordinal))) errors.Add(new("P1_PATH_UNEXPECTED_DIRECTORY", "Authority directory is not active or approved staging.", authorityRoot));
        if (name.Contains("backup", StringComparison.OrdinalIgnoreCase) || (!allowStaging && name.Contains("staging", StringComparison.OrdinalIgnoreCase))) errors.Add(new("P1_PATH_INACTIVE", "Staging or backup paths cannot be active authority.", authorityRoot));
        Phase1AuthoritySet? set = null;
        try
        {
            async Task<T?> Read<T>(string file) { var path = Path.Combine(fullRoot, file); if (!File.Exists(path)) { errors.Add(new("P1_ARTIFACT_MISSING", $"Required artifact '{file}' is missing.", path)); return default; } await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, Options, token); }
            var context = await Read<Phase1ExecutionContext>("execution-context.json"); var plan = await Read<Phase1SelectedPlan>("selected-plan.json"); var request = await Read<Phase1ProductionRequest>("production-request.json"); var state = await Read<Phase1PipelineState>("pipeline-state.json");
            if (context is not null && plan is not null && request is not null && state is not null) set = new(context, plan, request, state);
        }
        catch (JsonException ex) { errors.Add(new("P1_JSON_INVALID", ex.Message, authorityRoot)); }
        if (set is not null) ValidateSet(set, errors);
        var valid = errors.Count == 0;
        return new(valid, valid, valid, valid, errors, [], set?.ExecutionContext.ContractVersion, set?.ExecutionContext.AuthorityChecksum, set?.ExecutionContext.RequestIdentityChecksum, Phase1AuthorityContract.ProjectorIdentity, set);
    }
    private static void ValidateSet(Phase1AuthoritySet set, List<Phase1ValidationDiagnostic> errors)
    {
        var c = set.ExecutionContext; var p = set.SelectedPlan; var r = set.ProductionRequest; var s = set.PipelineState; void Require(bool ok, string code, string message) { if (!ok) errors.Add(new(code, message)); }
        Require(c.ContractVersion == Phase1AuthorityContract.ContractVersion, "P1_CONTRACT_UNSUPPORTED", "Unsupported authority contract version."); Require(c.AuthorityType == Phase1AuthorityContract.AuthorityType && c.AuthorityVersion == Phase1AuthorityContract.AuthorityVersion, "P1_AUTHORITY_IDENTITY_INVALID", "Authority identity is invalid."); Require(c.CgIdentifier == Phase1AuthorityContract.CgIdentifier, "P1_CG_INVALID", "CG identifier must be CG1."); Require(c.ProjectorIdentity == Phase1AuthorityContract.ProjectorIdentity && c.CanonicalizationIdentity == Phase1AuthorityContract.CanonicalizationIdentity, "P1_RUNTIME_INCOMPATIBLE", "Runtime identity is incompatible.");
        Require(c.ExecutionId != Guid.Empty && c.ExecutionId == r.ExecutionId && c.ExecutionId == s.ExecutionId, "P1_EXECUTION_ID_MISMATCH", "Execution IDs do not match."); Require(c.PlanId != Guid.Empty && c.PlanId == p.PlanId && c.PlanId == r.PlanId && c.PlanId == s.PlanId, "P1_PLAN_ID_MISMATCH", "Plan IDs do not match."); Require(!string.IsNullOrWhiteSpace(c.ResolvedLanguage) && c.ResolvedLanguage == p.RequestedLanguage && c.ResolvedLanguage == r.ResolvedLanguage, "P1_LANGUAGE_MISMATCH", "Languages do not match.");
        Require(c.RequestedVariants.Count == c.RequestedVariants.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "P1_VARIANT_DUPLICATE", "Duplicate variants are forbidden."); Require(c.RequestedVariants.SequenceEqual(p.RequestedVariants) && c.RequestedVariants.SequenceEqual(r.RequestedVariants), "P1_VARIANT_MISMATCH", "Variants do not match."); Require(c.RequestedOutputs.SequenceEqual(p.RequestedOutputs) && c.RequestedOutputs.SequenceEqual(r.RequestedOutputs), "P1_OUTPUT_MISMATCH", "Outputs do not match.");
        Require(c.EffectiveStartPhaseNo is >= 1 and <= 20 && c.EffectiveEndPhaseNo is >= 1 and <= 20 && c.EffectiveStartPhaseNo <= c.EffectiveEndPhaseNo, "P1_PHASE_RANGE_INVALID", "Effective phase range is invalid."); Require(c.RequestedStartPhaseNo == r.RequestedStartPhaseNo && c.RequestedEndPhaseNo == r.RequestedEndPhaseNo && c.EffectiveStartPhaseNo == r.EffectiveStartPhaseNo && c.EffectiveEndPhaseNo == r.EffectiveEndPhaseNo, "P1_PHASE_RANGE_MISMATCH", "Phase ranges do not match.");
        Require(Phase1CanonicalJson.Checksum(p, nameof(Phase1SelectedPlan.SelectedPlanChecksum)) == p.SelectedPlanChecksum && c.SelectedPlanChecksum == p.SelectedPlanChecksum, "P1_SELECTED_PLAN_CHECKSUM_INVALID", "Selected-plan checksum is invalid."); Require(Phase1CanonicalJson.Checksum(r, nameof(Phase1ProductionRequest.RequestChecksum)) == r.RequestChecksum && c.ProductionRequestChecksum == r.RequestChecksum, "P1_REQUEST_CHECKSUM_INVALID", "Production-request checksum is invalid.");
        var stateChecksum = Phase1CanonicalJson.Checksum(s, nameof(Phase1PipelineState.InitializedUtc)); Require(c.SupportingArtifactChecksums.GetValueOrDefault("pipeline-state.json") == stateChecksum && s.SelectedPlanChecksum == p.SelectedPlanChecksum && s.ProductionRequestChecksum == r.RequestChecksum, "P1_STATE_REFERENCE_INVALID", "Pipeline-state references are invalid."); Require(Phase1CanonicalJson.Checksum(new { p.SelectedPlanChecksum, r.RequestChecksum }) == c.RequestIdentityChecksum, "P1_REQUEST_IDENTITY_INVALID", "Request identity is invalid."); Require(Phase1CanonicalJson.Checksum(c, nameof(Phase1ExecutionContext.GeneratedUtc), nameof(Phase1ExecutionContext.AuthorityChecksum)) == c.AuthorityChecksum, "P1_AUTHORITY_CHECKSUM_INVALID", "Authority checksum is invalid.");
        Require(s.DownstreamPhaseStates.All(x => !string.Equals(x.Value, "Succeeded", StringComparison.OrdinalIgnoreCase)), "P1_FALSE_DOWNSTREAM_SUCCESS", "Initial state asserts downstream success."); var json = Phase1CanonicalJson.Serialize(set); Require(!new[] { "apikey", "connectionstring", "accesstoken", "refreshtoken", "authorization", "sastoken", "credential", "secret" }.Any(x => json.Contains(x, StringComparison.OrdinalIgnoreCase)), "P1_SECRET_PROPERTY", "A secret-bearing property is present.");
    }
}

public sealed class Phase1AuthorityPersistence(IPhase1AuthorityValidator validator) : IPhase1AuthorityPersistence, IPhase1AuthorityReader
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public Task<Phase1AuthorityValidationResult> ReadAsync(string workspaceRoot, CancellationToken token) => validator.ValidateAsync(workspaceRoot, Path.Combine(workspaceRoot, Phase1AuthorityContract.DirectoryName), false, token);
    public async Task<Phase1PersistenceResult> PersistAsync(string workspaceRoot, Phase1AuthoritySet authority, bool overwrite, CancellationToken token)
    {
        var root = Path.GetFullPath(workspaceRoot); var gate = Locks.GetOrAdd(root, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(token);
        try
        {
            var active = Path.Combine(root, Phase1AuthorityContract.DirectoryName);
            if (!overwrite && Directory.Exists(active)) { var existing = await validator.ValidateAsync(root, active, false, token); if (existing.IsReusable && existing.RequestIdentityChecksum == authority.ExecutionContext.RequestIdentityChecksum) return new(true, Paths(active), existing, []); }
            token.ThrowIfCancellationRequested(); var staging = Path.Combine(root, $".01-plan.staging-{Guid.NewGuid():N}"); var backup = Path.Combine(root, $".01-plan.backup-{Guid.NewGuid():N}"); Directory.CreateDirectory(staging);
            try
            {
                await WriteAsync(staging, "selected-plan.json", authority.SelectedPlan, token); await WriteAsync(staging, "production-request.json", authority.ProductionRequest, token); await WriteAsync(staging, "pipeline-state.json", authority.PipelineState, token); await WriteAsync(staging, "execution-context.json", authority.ExecutionContext, token);
                var staged = await validator.ValidateAsync(root, staging, true, token); if (!staged.IsValid) throw new InvalidOperationException("Phase 1 staged validation failed: " + string.Join("; ", staged.Errors.Select(x => x.Code))); token.ThrowIfCancellationRequested();
                if (Directory.Exists(active)) Directory.Move(active, backup); try { Directory.Move(staging, active); } catch { if (Directory.Exists(backup) && !Directory.Exists(active)) Directory.Move(backup, active); throw; } if (Directory.Exists(backup)) Directory.Delete(backup, true);
                var committed = await validator.ValidateAsync(root, active, false, token); return new(false, Paths(active), committed, []);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        finally { gate.Release(); }
    }
    private static Task WriteAsync<T>(string root, string name, T value, CancellationToken token) => File.WriteAllTextAsync(Path.Combine(root, name), Phase1CanonicalJson.Serialize(value), token);
    private static string[] Paths(string root) => ["execution-context.json", "selected-plan.json", "production-request.json", "pipeline-state.json"].Select(x => Path.Combine(root, x)).ToArray();
}
