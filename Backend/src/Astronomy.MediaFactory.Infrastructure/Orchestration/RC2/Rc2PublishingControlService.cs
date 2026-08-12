using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public interface IRc2PublishingPlanResolver
{
    Task<Rc2PublishingPlan> ResolveAsync(Guid planId, CancellationToken cancellationToken);
}

public sealed class Rc2PublishingPlanResolver(MediaFactoryDbContext db) : IRc2PublishingPlanResolver
{
    public async Task<Rc2PublishingPlan> ResolveAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId, cancellationToken)
            ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PLAN_NOT_FOUND", $"Content generation plan '{planId:D}' was not found.");
        var root = await db.ContentPipelineExecutions.AsNoTracking().Where(x => x.ContentGenerationPlanId == planId && x.OutputFolder != null)
            .OrderByDescending(x => x.StartedUtc).Select(x => x.OutputFolder).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root))
            throw new Rc2PublishingControlException("RC2_PUBLISH_OUTPUT_ROOT_NOT_AVAILABLE", "The plan has no governed production output root.");
        var language = plan.Language.Trim().ToLowerInvariant();
        root = Path.GetFullPath(root);
        return new(plan.Id, plan.Title ?? string.Empty, language, plan.RegionId, root,
            Path.Combine(root, "19-video-qa", language), Path.Combine(root, "20-publishing", language),
            Path.Combine(root, "validation", "phase-20-validation.json"));
    }
}

public sealed record Phase20PublishingAuthoritySnapshot(string PublishingPackageId, string AuthorityChecksum, string Status,
    bool TechnicalQaApproved, bool PublicationPackageReady, int ArtifactCount,
    IReadOnlyDictionary<string, int> Roles, IReadOnlyList<Rc2PublishingTarget> Targets);

public interface IPhase20PublishingAuthorityReader
{
    Task<Phase20PublishingAuthoritySnapshot?> ReadAsync(Rc2PublishingPlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// The certified execution boundary used by the publishing command.  Unlike the
/// content-plan orchestrator this boundary does not recover a run or expand its
/// historical prerequisites: Phase 20 owns validation of its committed inputs.
/// </summary>
public interface IRc2Phase20ExecutionService
{
    Task<ProductionPipelineExecutionResult> ExecuteAsync(Rc2PublishingPlan publishingPlan, bool overwriteExisting,
        CancellationToken cancellationToken);
}

public sealed class Rc2Phase20ExecutionService(MediaFactoryDbContext db, IContentPlanProductionRequestMapper mapper,
    IProductionPipelineExecutionService pipeline) : IRc2Phase20ExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductionPipelineExecutionResult> ExecuteAsync(Rc2PublishingPlan publishingPlan,
        bool overwriteExisting, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans
            .Include(x => x.AstronomyEventIntelligence)!.ThenInclude(x => x!.Objects)
            .SingleAsync(x => x.Id == publishingPlan.PlanId, cancellationToken);
        var intelligence = plan.AstronomyEventIntelligence
            ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PLAN_NOT_FOUND", "The plan has no production intelligence.");
        var request = mapper.Map(plan, intelligence);
        var requestedOutputs = await ResolveGovernedRequestedOutputsAsync(
            publishingPlan.PlanOutputRoot, publishingPlan.Phase20Root, publishingPlan.PlanId,
            request.RequestedOutputs, cancellationToken);
        request = request with { RequestedOutputs = requestedOutputs };

        return await pipeline.ExecuteAsync(new ProductionPipelineRequest(request, intelligence.Id,
            publishingPlan.PlanOutputRoot, DryRun: false, OverwriteExisting: overwriteExisting,
            StartPhaseNo: 20, EndPhaseNo: 20,
            ExecutionMode: overwriteExisting ? ContentPlanExecutionMode.RerunPhase : ContentPlanExecutionMode.Normal,
            RequestedStartPhaseNo: 20, RequestedEndPhaseNo: 20,
            DependencyExpansionMode: DependencyExpansionMode.None), cancellationToken);
    }

    internal static async Task<IReadOnlyList<string>> ResolveGovernedRequestedOutputsAsync(string outputRoot,
        string phase20Root, Guid planId, IReadOnlyList<string> planRequestedOutputs, CancellationToken cancellationToken)
    {
        // Phase 1 is the committed request identity for the production execution. In particular,
        // it preserves a manual output override that need not alter the semantic plan itself.
        var productionRequestPath = Path.Combine(outputRoot, "01-plan", "production-request.json");
        if (File.Exists(productionRequestPath))
        {
            await using var stream = File.OpenRead(productionRequestPath);
            var productionRequest = await JsonSerializer.DeserializeAsync<Phase1ProductionRequest>(stream, JsonOptions,
                cancellationToken) ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PRODUCTION_REQUEST_INVALID",
                "The committed Phase 1 production request could not be read.");
            if (productionRequest.PlanId != planId)
                throw new Rc2PublishingControlException("RC2_PUBLISH_PRODUCTION_REQUEST_INVALID",
                    "The committed Phase 1 production request does not belong to the requested plan.");
            if (Phase1CanonicalJson.Checksum(productionRequest, nameof(Phase1ProductionRequest.RequestChecksum)) !=
                productionRequest.RequestChecksum)
                throw new Rc2PublishingControlException("RC2_PUBLISH_PRODUCTION_REQUEST_INVALID",
                    "The committed Phase 1 production request checksum is invalid.");
            if (productionRequest.RequestedOutputs.Count > 0) return productionRequest.RequestedOutputs;
            throw new Rc2PublishingControlException("RC2_PUBLISH_PRODUCTION_REQUEST_INVALID",
                "The committed Phase 1 production request has no requested outputs.");
        }

        if (planRequestedOutputs.Count > 0) return planRequestedOutputs;

        // Compatibility fallback for an older run whose Phase 1 request predates requestedOutputs.
        var manifestPath = Path.Combine(phase20Root, "publishing-manifest.json");
        if (File.Exists(manifestPath))
        {
            await using var stream = File.OpenRead(manifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("requestedOutputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
            {
                var resolved = outputs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray();
                if (resolved.Length > 0) return resolved;
            }
        }
        throw new Rc2PublishingControlException("RC2_PUBLISH_REQUESTED_OUTPUTS_NOT_AVAILABLE",
            "No governed requested-output identity is available for the production run.");
    }
}

public sealed class Phase20PublishingAuthorityReader : IPhase20PublishingAuthorityReader
{
    public async Task<Phase20PublishingAuthoritySnapshot?> ReadAsync(Rc2PublishingPlan plan, CancellationToken cancellationToken)
    {
        var paths = new[] { "publishing-manifest.json", "publishing-package.json", "phase20-authority-diagnostics.json", "phase20-publication-report.json" }
            .Select(x => Path.Combine(plan.Phase20Root, x)).Append(plan.Phase20ValidationPath).ToArray();
        if (paths.All(path => !File.Exists(path))) return null;
        if (paths.Any(x => !File.Exists(x))) throw Invalid("Canonical Phase 20 evidence is incomplete.");
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var path in paths)
            {
                await using var stream = File.OpenRead(path);
                documents.Add(await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken));
            }
            var roots = documents.Select(x => x.RootElement).ToArray();
            var packageId = Text(roots[0], "publishingPackageId");
            var checksum = Text(roots[0], "authorityChecksum");
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(checksum) ||
                roots.Any(x => Text(x, "publishingPackageId") != packageId || Text(x, "authorityChecksum") != checksum))
                throw Invalid("Phase 20 authority identity does not agree across canonical evidence.");
            var report = roots[3]; var validation = roots[4]; var diagnostics = roots[2]; var package = roots[1];
            if (!Bool(report, "publicationCommitted") || !Bool(report, "committedReadbackPassed") || !Bool(report, "committedStateValidationPassed") ||
                Text(report, "validationStatus") != "Valid" || Text(report, "manifestValidationStatus") != "Valid" ||
                !Bool(diagnostics, "semanticValidationPassed") || !Bool(diagnostics, "checksumValidationPassed") || !Bool(diagnostics, "manifestValidationPassed") ||
                !Bool(validation, "publicationCommitted") || Text(validation, "validationStatus") != "Valid" ||
                !Bool(package, "technicalQaApproved") || !Bool(package, "publicationPackageReady")) throw Invalid("Committed Phase 20 governance is invalid.");
            var artifacts = roots[0].GetProperty("artifacts").EnumerateArray().ToArray();
            var roles = artifacts.GroupBy(RoleName).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
            var targets = PackageableTargets(roles);
            return new(packageId, checksum, Text(report, "status"), true, true, artifacts.Length, roles, targets.Distinct().ToArray());
        }
        catch (Rc2PublishingControlException) { throw; }
        catch (Exception ex) { throw new Rc2PublishingControlException("RC2_PUBLISH_PHASE20_INVALID", ex.Message); }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    private static Rc2PublishingControlException Invalid(string message) => new("RC2_PUBLISH_PHASE20_INVALID", message);
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static bool Bool(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True;
    private static string RoleName(JsonElement artifact)
    {
        if (!artifact.TryGetProperty("role", out var role)) return "Unknown";
        if (role.ValueKind == JsonValueKind.String) return role.GetString() ?? "Unknown";
        return role.TryGetInt32(out var number) && Enum.IsDefined(typeof(PublishingPackageRole), number)
            ? ((PublishingPackageRole)number).ToString() : "Unknown";
    }

    internal static IReadOnlyList<Rc2PublishingTarget> PackageableTargets(IReadOnlyDictionary<string, int> roles)
    {
        bool Has(string role) => roles.TryGetValue(role, out var count) && count > 0;
        return Enum.GetValues<Rc2PublishingTarget>().Where(target => target switch
        {
            Rc2PublishingTarget.YouTubeLong or Rc2PublishingTarget.FacebookLong => Has("LongVideo"),
            Rc2PublishingTarget.YouTubeShort or Rc2PublishingTarget.FacebookReel or Rc2PublishingTarget.InstagramReel => Has("ShortVideo"),
            Rc2PublishingTarget.InstagramPost => Has("HeroPortrait") || Has("HeroSquare"),
            Rc2PublishingTarget.FacebookPost => Has("HeroLandscape") || Has("HeroSquare"),
            Rc2PublishingTarget.InstagramCarousel or Rc2PublishingTarget.FacebookCarousel => Has("GalleryImage"),
            _ => false
        }).ToArray();
    }
}

public sealed class Rc2PublishingControlService(IRc2PublishingPlanResolver resolver, IPhase20PublishingAuthorityReader reader,
    IRc2Phase20ExecutionService phase20, MediaFactoryDbContext db, ILogger<Rc2PublishingControlService> logger) : IRc2PublishingControlService
{
    public async Task<Rc2PublishingPackageResponse> CreateOrRefreshPackageAsync(Guid planId, bool overwriteExisting, CancellationToken ct)
    {
        var plan = await resolver.ResolveAsync(planId, ct);
        logger.LogInformation("RC2_PUBLISH_PACKAGE_REQUESTED PlanId={PlanId} Language={Language}", planId, plan.Language);
        var result = await phase20.ExecuteAsync(plan, overwriteExisting, ct);
        if (!result.Success) throw new Rc2PublishingControlException("RC2_PUBLISH_PACKAGE_GOVERNANCE_FAILED", string.Join("; ", result.Errors));
        plan = await resolver.ResolveAsync(planId, ct);
        var authority = await reader.ReadAsync(plan, ct) ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PHASE20_INVALID", "Phase 20 did not commit a package.");
        logger.LogInformation("RC2_PUBLISH_PACKAGE_COMPLETED PlanId={PlanId} PublishingPackageId={PublishingPackageId} Phase20AuthorityChecksum={Checksum}", planId, authority.PublishingPackageId, authority.AuthorityChecksum);
        var state = await ApprovalAsync(planId, authority, ct);
        return new(planId, plan.Language, authority.Status, authority.PublishingPackageId, authority.AuthorityChecksum, true, true, true,
            state == Rc2PublishingApprovalStatus.Approved, state == Rc2PublishingApprovalStatus.Approved, state, authority.ArtifactCount, authority.Targets);
    }

    public async Task<Rc2PublishingApprovalResponse> SetApprovalAsync(Guid planId, Rc2PublishingApprovalStatus decision, CancellationToken ct)
    {
        if (!Enum.IsDefined(decision) || decision == Rc2PublishingApprovalStatus.NotAvailable)
            throw new ArgumentException("Decision must be Pending, Approved, or Rejected.", nameof(decision));
        var plan = await resolver.ResolveAsync(planId, ct);
        var observed = await reader.ReadAsync(plan, ct) ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PACKAGE_NOT_AVAILABLE", "A valid Phase 20 package is required for approval.");
        logger.LogInformation("RC2_PUBLISH_APPROVAL_REQUESTED PlanId={PlanId} Language={Language} Decision={Decision} PublishingPackageId={PackageId} Phase20AuthorityChecksum={Checksum}", planId, plan.Language, decision, observed.PublishingPackageId, observed.AuthorityChecksum);
        var row = await db.Rc2PublishingApprovals.SingleOrDefaultAsync(x => x.PlanId == planId && x.Phase20AuthorityChecksum == observed.AuthorityChecksum && x.PublishingPackageId == observed.PublishingPackageId, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null) db.Rc2PublishingApprovals.Add(row = new Rc2PublishingApproval { Id = Guid.NewGuid(), PlanId = planId, PublishingPackageId = observed.PublishingPackageId, Phase20AuthorityChecksum = observed.AuthorityChecksum, Decision = decision, DecisionUtc = now, DecisionSource = "Rc2PublishingControlApi", CreatedUtc = now, UpdatedUtc = now });
        else if (row.Decision != decision) { row.Decision = decision; row.DecisionUtc = now; row.DecisionSource = "Rc2PublishingControlApi"; row.UpdatedUtc = now; }
        var current = await reader.ReadAsync(plan, ct);
        if (current is null || current.AuthorityChecksum != observed.AuthorityChecksum || current.PublishingPackageId != observed.PublishingPackageId)
            throw new Rc2PublishingControlException("RC2_PUBLISH_AUTHORITY_CHANGED", "Phase 20 authority changed while approval was being recorded.");
        await db.SaveChangesAsync(ct);
        logger.LogInformation("RC2_PUBLISH_APPROVAL_CHANGED PlanId={PlanId} Decision={Decision} Phase20AuthorityChecksum={Checksum}", planId, decision, observed.AuthorityChecksum);
        var approved = decision == Rc2PublishingApprovalStatus.Approved;
        return new(planId, observed.PublishingPackageId, observed.AuthorityChecksum, decision, true, true, true, approved, approved, approved ? row.DecisionUtc : null);
    }

    public async Task<Rc2PublishingStatusResponse> GetStatusAsync(Guid planId, CancellationToken ct)
    {
        var plan = await resolver.ResolveAsync(planId, ct);
        var authority = await reader.ReadAsync(plan, ct);
        var approval = authority is null ? Rc2PublishingApprovalStatus.NotAvailable : await ApprovalAsync(planId, authority, ct);
        var approved = approval == Rc2PublishingApprovalStatus.Approved;
        var phase19 = await ReadPhase19(plan, ct);
        var availableTargets = authority?.Targets.ToHashSet() ?? [];
        var targets = Enum.GetValues<Rc2PublishingTarget>().ToDictionary(x => x, x =>
        {
            var packageAvailable = availableTargets.Contains(x);
            var blockReason = !packageAvailable ? authority is null ? "BlockedPackageMissing" : "BlockedRequiredRoleMissing"
                : approved ? null : "BlockedApprovalRequired";
            return new Rc2TargetStatus(packageAvailable, false, false, "NotChecked",
                packageAvailable && approved ? Rc2PublicationState.NotPublished : Rc2PublicationState.Blocked, blockReason);
        });
        logger.LogInformation("RC2_PUBLISH_STATUS_READ PlanId={PlanId} Language={Language} ApprovalStatus={ApprovalStatus}", planId, plan.Language, approval);
        return new(planId, plan.Title, plan.Language, plan.RegionId, phase19,
            new(authority is not null, authority?.Status ?? "NotAvailable", authority?.PublishingPackageId, authority?.AuthorityChecksum,
                authority?.PublicationPackageReady ?? false, authority is not null, approved, approved, approval, authority?.ArtifactCount ?? 0),
            authority?.Roles ?? new Dictionary<string, int>(), authority?.Targets ?? [], targets);
    }

    private async Task<Rc2PublishingApprovalStatus> ApprovalAsync(Guid planId, Phase20PublishingAuthoritySnapshot authority, CancellationToken ct)
        => await db.Rc2PublishingApprovals.AsNoTracking().Where(x => x.PlanId == planId && x.Phase20AuthorityChecksum == authority.AuthorityChecksum && x.PublishingPackageId == authority.PublishingPackageId)
            .Select(x => (Rc2PublishingApprovalStatus?)x.Decision).SingleOrDefaultAsync(ct) ?? Rc2PublishingApprovalStatus.Pending;

    private static async Task<Rc2Phase19Status> ReadPhase19(Rc2PublishingPlan plan, CancellationToken ct)
    {
        var path = Path.Combine(plan.Phase19Root, "phase19-manifest.json");
        if (!File.Exists(path)) return new(false, false, null, false);
        await using var stream = File.OpenRead(path); using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        return new(true, root.TryGetProperty("technicalQaApproved", out var qa) && qa.GetBoolean(), root.TryGetProperty("authorityChecksum", out var checksum) ? checksum.GetString() : null, root.TryGetProperty("downstreamReady", out var ready) && ready.GetBoolean());
    }
}
