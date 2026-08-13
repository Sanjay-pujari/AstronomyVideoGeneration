using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2PublishingExecutionService(
    IRc2PublishingPlanResolver resolver, IPhase20PublishingAuthorityReader authorityReader,
    MediaFactoryDbContext db, ITokenHealthService tokenHealth, IYouTubePublishService youTube,
    IYouTubeAuthService youTubeAuth, IYouTubeApiClient youTubeApi,
    IFacebookVideoPublishService facebookVideo, IFacebookReelPublishService facebookReel,
    IInstagramReelPublishService instagramReel, IOptions<PublishingTargetsOptions> targetOptions,
    IOptions<PublishingOptions> publishingOptions, IOptions<YouTubeOptions> youTubeOptions,
    IOptions<MetaPublishingOptions> metaOptions, IOptions<PlatformPublishingOptions> platformOptions,
    IOptions<PublicMediaStorageOptions> publicMediaStorageOptions, IConfiguration configuration,
    ILogger<Rc2PublishingExecutionService> logger) : IRc2PublishingExecutionService
{
    private const string PolicyVersion = "rc2-phase20-publish-v1";
    internal const int MaximumStoredProviderDiagnosticLength = 16_384;
    internal const int MaximumApiFailureMessageLength = 512;
    internal const string FailureDiagnosticFallback = "Provider operation failed; detailed diagnostic could not be persisted.";
    private static readonly HashSet<Rc2PublishingTarget> VideoTargets =
        [Rc2PublishingTarget.YouTubeLong, Rc2PublishingTarget.YouTubeShort, Rc2PublishingTarget.FacebookLong,
         Rc2PublishingTarget.FacebookReel, Rc2PublishingTarget.InstagramReel];
    private static readonly HashSet<Rc2PublishingTarget> MediaTargets =
        [Rc2PublishingTarget.InstagramPost, Rc2PublishingTarget.InstagramCarousel,
         Rc2PublishingTarget.FacebookPost, Rc2PublishingTarget.FacebookCarousel];

    public Task<Rc2PublishingExecutionResponse> PublishVideoAsync(Rc2PublishVideoRequest request, CancellationToken ct)
    {
        ValidateTargets(request.Platforms, VideoTargets);
        if (request.PublishMode != Rc2PublishMode.Now)
            throw new ArgumentException("Scheduled publishing is not supported (RC2_PUBLISH_MODE_NOT_SUPPORTED).");
        logger.LogInformation("RC2_PUBLISH_VIDEO_REQUESTED PlanId={PlanId} DryRun={DryRun}", request.PlanId, request.DryRun);
        return ExecuteAsync(request.PlanId, request.Platforms, "Video", request.DryRun, ct);
    }

    public Task<Rc2PublishingExecutionResponse> PublishMediaAsync(Rc2PublishMediaRequest request, CancellationToken ct)
    {
        ValidateTargets(request.Targets, MediaTargets);
        if (request.MediaTypes is null || request.MediaTypes.Count == 0 || request.MediaTypes.Contains(Rc2PublishingMediaType.Video) ||
            request.MediaTypes.Distinct().Count() != request.MediaTypes.Count)
            throw new ArgumentException("mediaTypes must contain unique Hero and/or Gallery values.");
        foreach (var target in request.Targets)
        {
            var required = target is Rc2PublishingTarget.InstagramCarousel or Rc2PublishingTarget.FacebookCarousel
                ? Rc2PublishingMediaType.Gallery : Rc2PublishingMediaType.Hero;
            if (!request.MediaTypes.Contains(required))
                throw new ArgumentException($"{target} requires mediaTypes to include {required} (RC2_PUBLISH_MEDIA_TYPE_MISMATCH).");
        }
        logger.LogInformation("RC2_PUBLISH_MEDIA_REQUESTED PlanId={PlanId} DryRun={DryRun}", request.PlanId, request.DryRun);
        return ExecuteAsync(request.PlanId, request.Targets, "Media", request.DryRun, ct);
    }

    private async Task<Rc2PublishingExecutionResponse> ExecuteAsync(Guid planId, IReadOnlyList<Rc2PublishingTarget> targets,
        string requestType, bool dryRun, CancellationToken ct)
    {
        var plan = await resolver.ResolveAsync(planId, ct);
        var authority = await authorityReader.ReadAsync(plan, ct)
            ?? throw new Rc2PublishingControlException("RC2_PUBLISH_PACKAGE_NOT_AVAILABLE", "A committed Phase 20 package is required.");
        var approved = await db.Rc2PublishingApprovals.AsNoTracking().AnyAsync(x => x.PlanId == planId &&
            x.PublishingPackageId == authority.PublishingPackageId && x.Phase20AuthorityChecksum == authority.AuthorityChecksum &&
            x.Decision == Rc2PublishingApprovalStatus.Approved, ct);
        if (!approved)
            throw new Rc2PublishingControlException("RC2_PUBLISH_APPROVAL_REQUIRED", "The current Phase 20 authority has not been approved.");

        var results = new List<Rc2PublicationResult>();
        foreach (var target in targets)
            results.Add(await ExecuteTargetAsync(plan, authority, target, requestType, dryRun, ct));
        var successful = results.Count(x => x.PublicationState is Rc2PublicationState.Published or Rc2PublicationState.AlreadyPublished || x.DryRunPassed);
        var overall = successful == results.Count ? "Succeeded" : successful > 0 ? "PartialSuccess" :
            results.All(x => x.PublicationState == Rc2PublicationState.Blocked) ? "Blocked" : "Failed";
        return new(planId, authority.PublishingPackageId, authority.AuthorityChecksum, requestType, overall, results);
    }

    private async Task<Rc2PublicationResult> ExecuteTargetAsync(Rc2PublishingPlan plan,
        Phase20PublishingAuthoritySnapshot authority, Rc2PublishingTarget target, string requestType, bool dryRun, CancellationToken ct)
    {
        IReadOnlyList<Phase20PublishingArtifact> artifacts;
        try { artifacts = ResolveArtifacts(authority, target); }
        catch (Rc2PublishingControlException ex) { return Blocked(target, ex.Code, ex.Message, 0); }
        var identity = string.Join("|", artifacts.Select(x => $"{x.Role}:{x.Sha256}"));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{plan.PlanId:D}|{authority.AuthorityChecksum}|{authority.PublishingPackageId}|{target}|{identity}|{PolicyVersion}"))).ToLowerInvariant();
        logger.LogInformation("RC2_PUBLISH_{RequestType}_TARGET_STARTED PlanId={PlanId} PackageId={PackageId} AuthorityChecksum={Checksum} Target={Target} IdempotencyKey={IdempotencyKey}",
            requestType.ToUpperInvariant(), plan.PlanId, authority.PublishingPackageId, authority.AuthorityChecksum, target, key);

        var existing = await db.Rc2PublishingPublications.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing?.Status == Rc2PublicationState.Published)
            return Result(existing, Rc2PublicationState.AlreadyPublished, true);
        var now = DateTimeOffset.UtcNow;
        var leaseCutoff = now.AddMinutes(-publishingOptions.Value.InProgressLeaseMinutes);
        var stalePublishing = existing is not null && IsPublishingLeaseStale(existing, leaseCutoff);
        if (existing?.Status == Rc2PublicationState.Publishing && !stalePublishing)
            return Result(existing, Rc2PublicationState.Blocked, false, "RC2_PUBLISH_ALREADY_IN_PROGRESS", "An identical publication is already in progress.");

        try { await VerifyArtifactsAsync(plan, artifacts, ct); }
        catch (Rc2PublishingControlException ex) { return Blocked(target, ex.Code, ex.Message, artifacts.Count); }
        if (target == Rc2PublishingTarget.YouTubeLong)
        {
            try { ValidateYouTubeMetadata(plan.Title, ResolveYouTubePrivacy(), youTubeOptions.Value.CategoryId); }
            catch (Rc2PublishingControlException ex) { return Blocked(target, ex.Code, ex.Message, artifacts.Count); }
        }
        LogEnablementConfiguration(plan.PlanId, target);
        if (!IsTargetEffectivelyEnabled(target, publishingOptions.Value, youTubeOptions.Value, targetOptions.Value,
                metaOptions.Value, platformOptions.Value))
            return Blocked(target, "RC2_PUBLISH_TARGET_DISABLED", $"{target} is not explicitly enabled.", artifacts.Count);
        var health = target is Rc2PublishingTarget.YouTubeLong or Rc2PublishingTarget.YouTubeShort
            ? await tokenHealth.CheckYouTubeAsync(ct) : await tokenHealth.CheckMetaAsync(ct);
        if (!health.IsConfigured || !health.IsValid)
            return string.Equals(health.Status, "ReauthorizationRequired", StringComparison.Ordinal)
                ? Blocked(target, "RC2_PUBLISH_REAUTHORIZATION_REQUIRED",
                    $"Provider={health.Platform}; OAuthStartPath={health.OAuthStartPath}", artifacts.Count)
                : Blocked(target, "RC2_PUBLISH_CREDENTIALS_INVALID", NonSecretHealthMessage(health), artifacts.Count);
        if (dryRun) return new(target, Rc2PublicationState.NotPublished, existing?.RemotePublicationId, existing?.RemoteUrl, false,
            existing?.AttemptCount ?? 0, stalePublishing ? "RC2_PUBLISH_RECOVERABLE_STALE_PUBLICATION" : null,
            stalePublishing ? "The abandoned publication is ready for governed recovery; dry-run did not acquire it." : null,
            IsCarousel(target) ? artifacts.Count : null, true);

        if (existing?.FailureCode == "RC2_PUBLISH_REMOTE_OUTCOME_UNKNOWN" && !existing.VideoUploadCompleted)
            return Result(existing, Rc2PublicationState.Blocked, false, existing.FailureCode,
                "The remote create outcome requires reconciliation before another upload can be attempted.");

        var row = existing is null ? new Rc2PublishingPublication
        {
            Id = Guid.NewGuid(), PlanId = plan.PlanId, PublishingPackageId = authority.PublishingPackageId,
            Phase20AuthorityChecksum = authority.AuthorityChecksum, Target = target, RoleOrMediaType = identity,
            IdempotencyKey = key, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow
        } : null;
        try
        {
            if (existing is null)
            {
                row!.Status = Rc2PublicationState.Publishing; row.AttemptCount = 1; row.LastAttemptUtc = row.UpdatedUtc = now;
                db.Rc2PublishingPublications.Add(row);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Database compare-and-swap: only one worker observing this exact stale/terminal
                // version can acquire the next execution lease.
                if (!await TryAcquireExistingLeaseAsync(existing, now, ct))
                {
                    var owner = await db.Rc2PublishingPublications.AsNoTracking().SingleAsync(x => x.Id == existing.Id, ct);
                    return Result(owner, owner.Status == Rc2PublicationState.Published ? Rc2PublicationState.AlreadyPublished : Rc2PublicationState.Blocked,
                        owner.Status == Rc2PublicationState.Published, "RC2_PUBLISH_ALREADY_IN_PROGRESS", "An identical publication claimed execution concurrently.");
                }
                db.ChangeTracker.Clear();
                row = await db.Rc2PublishingPublications.SingleAsync(x => x.Id == existing.Id, ct);
            }
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Rc2PublishingPublications.AsNoTracking().SingleAsync(x => x.IdempotencyKey == key, ct);
            return Result(winner, winner.Status == Rc2PublicationState.Published ? Rc2PublicationState.AlreadyPublished : Rc2PublicationState.Blocked,
                winner.Status == Rc2PublicationState.Published, "RC2_PUBLISH_ALREADY_IN_PROGRESS", "An identical publication claimed execution concurrently.");
        }

        try
        {
            if (target == Rc2PublishingTarget.YouTubeLong)
                await PublishYouTubeLongAsync(row, plan, artifacts, ct);
            else
            {
                var provider = await PublishProviderAsync(plan, target, artifacts, ct);
                row.Status = provider.Success ? Rc2PublicationState.Published : Rc2PublicationState.Failed;
                row.RemotePublicationId = provider.Id; row.RemoteUrl = provider.Url;
                row.FailureCode = provider.Success ? null : "RC2_PUBLISH_PROVIDER_FAILED";
                row.FailureMessage = provider.Success ? null : NormalizeProviderFailure(provider.Error);
            }
        }
        catch (YouTubeCreateOutcomeUnknownException ex)
        {
            row.Status = Rc2PublicationState.Failed; row.FailureCode = "RC2_PUBLISH_REMOTE_OUTCOME_UNKNOWN";
            row.FailureMessage = NormalizeProviderFailure(ex.Message);
        }
        catch (Exception ex)
        {
            row.Status = Rc2PublicationState.Failed; row.FailureCode = "RC2_PUBLISH_PROVIDER_FAILED";
            row.FailureMessage = NormalizeProviderFailure(ex.Message);
        }
        finally
        {
            // Never depend on graceful shutdown to release a claim. A hard crash is recovered by
            // lease expiry; every observable exit is moved to a durable terminal state here.
            if (row.Status == Rc2PublicationState.Publishing)
            {
                row.Status = Rc2PublicationState.Failed;
                row.FailureCode ??= "RC2_PUBLISH_EXECUTION_INTERRUPTED";
                row.FailureMessage ??= "Publishing execution ended before a terminal state was persisted.";
            }
            row.UpdatedUtc = DateTimeOffset.UtcNow;
            if (row.Status == Rc2PublicationState.Failed)
                await PersistFailureAsync(row, CancellationToken.None);
            else
                await db.SaveChangesAsync(CancellationToken.None);
        }
        logger.LogInformation("RC2_PUBLISH_{RequestType}_TARGET_COMPLETED PlanId={PlanId} Target={Target} IdempotencyKey={IdempotencyKey} State={State}",
            requestType.ToUpperInvariant(), plan.PlanId, target, key, row.Status);
        return Result(row, row.Status, false, row.FailureCode, ConciseApiFailureMessage(row.FailureMessage), IsCarousel(target) ? artifacts.Count : null);
    }

    internal static bool IsPublishingLeaseStale(Rc2PublishingPublication row, DateTimeOffset leaseCutoff) =>
        row.Status == Rc2PublicationState.Publishing && (row.LastAttemptUtc is null || row.LastAttemptUtc <= leaseCutoff);

    internal async Task<bool> TryAcquireExistingLeaseAsync(Rc2PublishingPublication observed, DateTimeOffset now,
        CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-publishingOptions.Value.InProgressLeaseMinutes);
        if (observed.Status == Rc2PublicationState.Publishing && !IsPublishingLeaseStale(observed, cutoff))
            return false;
        var claimed = await db.Rc2PublishingPublications
            .Where(x => x.Id == observed.Id && x.Status == observed.Status && x.UpdatedUtc == observed.UpdatedUtc &&
                (x.Status != Rc2PublicationState.Publishing || x.LastAttemptUtc == null || x.LastAttemptUtc <= cutoff))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, Rc2PublicationState.Publishing)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.LastAttemptUtc, now)
                .SetProperty(x => x.UpdatedUtc, now), ct);
        return claimed == 1;
    }

    internal async Task PersistFailureAsync(Rc2PublishingPublication row, CancellationToken ct)
    {
        row.FailureMessage = NormalizeProviderFailure(row.FailureMessage);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception detailedFailure) when (detailedFailure is not OperationCanceledException)
        {
            logger.LogError(detailedFailure,
                "RC2 provider diagnostic persistence failed; saving compact failure. PublicationId={PublicationId}", row.Id);
            // Do not clear/reload the entity: the durable remote identity and checkpoint values on
            // this tracked publication are safety-critical and must survive the fallback update.
            row.Status = Rc2PublicationState.Failed;
            row.FailureMessage = FailureDiagnosticFallback;
            row.UpdatedUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    internal async Task PublishYouTubeLongAsync(Rc2PublishingPublication row, Rc2PublishingPlan plan,
        IReadOnlyList<Phase20PublishingArtifact> artifacts, CancellationToken ct)
    {
        string PathFor(string role) => ResolvePath(plan, artifacts.Single(x => x.Role == role).Path);
        var privacy = ResolveYouTubePrivacy();
        var request = new PublishRequest { PipelineRunId = plan.PlanId, VideoPath = PathFor("LongVideo"),
            ThumbnailPath = PathFor("ThumbnailLandscape"), PlatformThumbnailPath = PathFor("ThumbnailLandscape"),
            Title = plan.Title.Trim(), Description = plan.Title.Trim(), AssetType = "LongVideo", PrivacyStatus = privacy,
            IsShort = false, UploadThumbnail = true };

        // Scope preflight is deliberately before channel lookup or any provider side effect. This target
        // always carries LongCaptionSrt, so caption management permission is mandatory even on resume.
        await youTubeAuth.EnsurePublishingScopesAsync(captionsRequired: true, cancellationToken: ct);
        // Refresh before the non-idempotent create call; an upload exception is deliberately never replayed.
        var accessToken = await youTubeAuth.GetAccessTokenAsync(true, ct);
        var channel = await youTubeApi.GetAuthenticatedChannelAsync(accessToken, ct);
        if (!string.IsNullOrWhiteSpace(youTubeOptions.Value.ExpectedChannelId) &&
            !string.Equals(channel.ChannelId, youTubeOptions.Value.ExpectedChannelId, StringComparison.Ordinal))
            throw new Rc2PublishingControlException("RC2_PUBLISH_REMOTE_CHANNEL_MISMATCH", "Authenticated YouTube channel does not match configuration.");

        if (!row.VideoUploadCompleted)
        {
            if (!string.IsNullOrWhiteSpace(row.RemotePublicationId))
                throw new Rc2PublishingControlException("RC2_PUBLISH_CHECKPOINT_INVALID", "Remote video checkpoint is inconsistent.");
            string videoId;
            try { videoId = await youTubeApi.UploadVideoAsync(request, accessToken, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new YouTubeCreateOutcomeUnknownException(ex.Message, ex); }
            if (string.IsNullOrWhiteSpace(videoId))
                throw new YouTubeCreateOutcomeUnknownException("YouTube create completed without a confirmed video ID.");

            // Critical durability boundary: no supplementary remote call occurs before this commit succeeds.
            row.RemotePublicationId = videoId;
            row.RemoteUrl = $"https://www.youtube.com/watch?v={videoId}";
            row.VideoCreatedUtc = row.UpdatedUtc = DateTimeOffset.UtcNow;
            row.VideoUploadCompleted = true;
            row.LastCompletedStep = Rc2PublicationStep.VideoCreated;
            await db.SaveChangesAsync(ct);
        }

        if (!row.ThumbnailCompleted)
        {
            await youTubeApi.UploadThumbnailAsync(row.RemotePublicationId!, PathFor("ThumbnailLandscape"), accessToken, ct);
            row.ThumbnailCompleted = true; row.LastCompletedStep = Rc2PublicationStep.ThumbnailCompleted;
            row.UpdatedUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        if (!row.CaptionCompleted)
        {
            var (language, name) = ResolveCaptionLanguage(plan.Language);
            var captionPath = PathFor("LongCaptionSrt");
            await ValidateSrtAsync(captionPath, ct);
            await youTubeApi.UploadCaptionAsync(row.RemotePublicationId!, captionPath, language, name, accessToken, ct);
            row.CaptionCompleted = true; row.LastCompletedStep = Rc2PublicationStep.CaptionCompleted;
            row.UpdatedUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        if (!row.RemoteVerificationCompleted)
        {
            var remote = await youTubeApi.GetVideoPostUploadStatusAsync(row.RemotePublicationId!, accessToken, ct);
            if (remote is null || remote.VideoId != row.RemotePublicationId || remote.ChannelId != channel.ChannelId ||
                remote.ChannelId != youTubeOptions.Value.ExpectedChannelId || !string.Equals(remote.PrivacyStatus, privacy, StringComparison.OrdinalIgnoreCase))
                throw new Rc2PublishingControlException("RC2_PUBLISH_REMOTE_VERIFICATION_FAILED", "YouTube remote video verification failed closed.");
            row.RemoteVerificationCompleted = true; row.LastCompletedStep = Rc2PublicationStep.RemoteVerified;
            row.UpdatedUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        if (!row.VideoUploadCompleted || !row.ThumbnailCompleted || !row.CaptionCompleted || !row.RemoteVerificationCompleted)
            throw new Rc2PublishingControlException("RC2_PUBLISH_CHECKPOINT_INCOMPLETE", "Mandatory YouTube publication steps are incomplete.");
        row.Status = Rc2PublicationState.Published; row.FailureCode = row.FailureMessage = null;
    }

    private string ResolveYouTubePrivacy() => publishingOptions.Value.Mode.Equals("Public", StringComparison.OrdinalIgnoreCase)
        ? "public" : publishingOptions.Value.Mode.Equals("Private", StringComparison.OrdinalIgnoreCase) ? "private"
        : publishingOptions.Value.DefaultPrivacyStatus.ToLowerInvariant();

    internal static (string Code, string Name) ResolveCaptionLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "en" or "eng" or "english" => ("en", "English"),
        "es" or "spa" or "spanish" => ("es", "Spanish"),
        "fr" or "fra" or "french" => ("fr", "French"),
        _ => throw new Rc2PublishingControlException("RC2_PUBLISH_CAPTION_LANGUAGE_INVALID", "Plan language is not mapped to a YouTube caption language.")
    };

    internal static async Task ValidateSrtAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0 || info.Length == 0)
            throw new Rc2PublishingControlException("RC2_PUBLISH_CAPTION_INVALID", "The governed SRT must be a non-empty regular file.");

        string text;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new Rc2PublishingControlException("RC2_PUBLISH_CAPTION_ENCODING_INVALID", "The governed SRT must be valid UTF-8.");
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (blocks.Length == 0 || blocks.Any(block =>
        {
            var lines = block.Split('\n');
            return lines.Length < 3 || !int.TryParse(lines[0].TrimStart('\uFEFF'), out _) ||
                !System.Text.RegularExpressions.Regex.IsMatch(lines[1], @"^\d{2}:\d{2}:\d{2},\d{3} --> \d{2}:\d{2}:\d{2},\d{3}$");
        }))
            throw new Rc2PublishingControlException("RC2_PUBLISH_CAPTION_STRUCTURE_INVALID", "The governed caption does not have valid SRT blocks.");
    }

    internal static void ValidateYouTubeMetadata(string title, string privacy, string category)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 100)
            throw new Rc2PublishingControlException("RC2_PUBLISH_TITLE_INVALID", "YouTube title must contain 1 to 100 characters.");
        if (title.Length > 5000) // deterministic title fallback is also the governed description source
            throw new Rc2PublishingControlException("RC2_PUBLISH_DESCRIPTION_INVALID", "YouTube description exceeds 5000 characters.");
        if (privacy is not ("private" or "public" or "unlisted"))
            throw new Rc2PublishingControlException("RC2_PUBLISH_PRIVACY_INVALID", "YouTube privacy is invalid.");
        if (!int.TryParse(category, out var categoryId) || categoryId <= 0)
            throw new Rc2PublishingControlException("RC2_PUBLISH_CATEGORY_INVALID", "YouTube category is invalid.");
    }

    private sealed class YouTubeCreateOutcomeUnknownException(string message, Exception? inner = null) : Exception(message, inner);

    private async Task<(bool Success, string? Id, string? Url, string? Error)> PublishProviderAsync(
        Rc2PublishingPlan plan, Rc2PublishingTarget target, IReadOnlyList<Phase20PublishingArtifact> artifacts, CancellationToken ct)
    {
        string PathFor(string role) => ResolvePath(plan, artifacts.First(x => x.Role == role).Path);
        var videoRole = target is Rc2PublishingTarget.YouTubeLong or Rc2PublishingTarget.FacebookLong ? "LongVideo" : "ShortVideo";
        var thumbRole = target is Rc2PublishingTarget.YouTubeLong or Rc2PublishingTarget.FacebookLong ? "ThumbnailLandscape" : "ThumbnailPortrait";
        if (target is Rc2PublishingTarget.YouTubeLong or Rc2PublishingTarget.YouTubeShort)
        {
            var result = await youTube.PublishAsync(new PublishRequest { PipelineRunId = plan.PlanId, VideoPath = PathFor(videoRole),
                ThumbnailPath = PathFor(thumbRole), PlatformThumbnailPath = PathFor(thumbRole), Title = plan.Title,
                Description = plan.Title, AssetType = videoRole, IsShort = videoRole == "ShortVideo", UploadThumbnail = true }, ct);
            return (result.Success, result.VideoId, result.VideoUrl ?? result.Url, result.Error);
        }
        if (target is Rc2PublishingTarget.InstagramPost or Rc2PublishingTarget.InstagramCarousel or Rc2PublishingTarget.FacebookPost or Rc2PublishingTarget.FacebookCarousel)
            return (false, null, null, "RC2_PUBLISH_CAPABILITY_NOT_SUPPORTED: the existing Meta provider contract does not support governed image posts/carousels.");
        var request = new MetaPublishRequest { PipelineRunId = plan.PlanId, Platform = target == Rc2PublishingTarget.InstagramReel ? "Instagram" : "Facebook",
            VideoPath = PathFor(videoRole), PlatformThumbnailPath = PathFor(thumbRole), Caption = plan.Title,
            ShortTitle = plan.Title, IsReel = target != Rc2PublishingTarget.FacebookLong };
        MetaPublishResult meta = target switch { Rc2PublishingTarget.FacebookLong => await facebookVideo.PublishVideoAsync(request, ct),
            Rc2PublishingTarget.FacebookReel => await facebookReel.PublishReelAsync(request, ct),
            _ => await instagramReel.PublishReelAsync(request, ct) };
        return (meta.Success, meta.PostId ?? meta.VideoId, meta.Url, meta.Error);
    }

    internal static IReadOnlyList<Phase20PublishingArtifact> ResolveArtifacts(Phase20PublishingAuthoritySnapshot authority, Rc2PublishingTarget target)
    {
        string[] roles = target switch {
            Rc2PublishingTarget.YouTubeLong => ["LongVideo", "ThumbnailLandscape", "LongCaptionSrt"],
            Rc2PublishingTarget.YouTubeShort => ["ShortVideo", "ThumbnailPortrait", "ShortCaptionSrt"],
            Rc2PublishingTarget.FacebookLong => ["LongVideo", "ThumbnailLandscape"],
            Rc2PublishingTarget.FacebookReel or Rc2PublishingTarget.InstagramReel => ["ShortVideo", "ThumbnailPortrait"],
            Rc2PublishingTarget.InstagramPost => [authority.Roles.ContainsKey("HeroPortrait") ? "HeroPortrait" : "HeroSquare"],
            Rc2PublishingTarget.FacebookPost => [authority.Roles.ContainsKey("HeroLandscape") ? "HeroLandscape" : "HeroSquare"],
            _ => ["GalleryImage"] };
        var resolved = roles.SelectMany(role => authority.Artifacts.Where(x => x.Role == role)
            .OrderBy(x => x.Sequence ?? int.MaxValue).ThenBy(x => x.Path, StringComparer.Ordinal)).ToArray();
        if (roles.Any(role => resolved.All(x => x.Role != role)))
            throw new Rc2PublishingControlException("RC2_PUBLISH_REQUIRED_ROLE_MISSING", $"{target} is missing a required governed Phase 20 role.");
        return resolved;
    }

    private static async Task VerifyArtifactsAsync(Rc2PublishingPlan plan, IReadOnlyList<Phase20PublishingArtifact> artifacts, CancellationToken ct)
    {
        foreach (var artifact in artifacts)
        {
            var path = ResolvePath(plan, artifact.Path);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new Rc2PublishingControlException("RC2_PUBLISH_ARTIFACT_INVALID", $"Governed role {artifact.Role} is not a regular file.");
            var info = new FileInfo(path);
            if (artifact.ByteLength < 0 || info.Length != artifact.ByteLength)
                throw new Rc2PublishingControlException("RC2_PUBLISH_ARTIFACT_LENGTH_MISMATCH", $"Governed role {artifact.Role} byte length changed.");
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(artifact.Sha256) || !hash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Rc2PublishingControlException("RC2_PUBLISH_ARTIFACT_HASH_MISMATCH", $"Governed role {artifact.Role} checksum changed.");
        }
    }

    private static string ResolvePath(Rc2PublishingPlan plan, string value)
    {
        var path = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(plan.PlanOutputRoot, value));
        var root = Path.GetFullPath(plan.PlanOutputRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal)) throw new Rc2PublishingControlException("RC2_PUBLISH_ARTIFACT_INVALID", "Artifact path escapes the governed output root.");
        return path;
    }
    internal static bool IsTargetEnabled(Rc2PublishingTarget target, PublishingTargetsOptions targets) => target switch
    {
        Rc2PublishingTarget.YouTubeLong => targets.YouTubeLong,
        Rc2PublishingTarget.YouTubeShort => targets.YouTubeShort,
        Rc2PublishingTarget.FacebookLong => targets.FacebookLong,
        Rc2PublishingTarget.FacebookReel => targets.FacebookReel,
        Rc2PublishingTarget.InstagramReel => targets.InstagramReel,
        Rc2PublishingTarget.InstagramPost => targets.InstagramPost,
        Rc2PublishingTarget.InstagramCarousel => targets.InstagramCarousel,
        Rc2PublishingTarget.FacebookPost => targets.FacebookPost,
        Rc2PublishingTarget.FacebookCarousel => targets.FacebookCarousel,
        _ => false
    };

    internal static bool IsTargetEffectivelyEnabled(Rc2PublishingTarget target, PublishingOptions publishing,
        YouTubeOptions youTube, PublishingTargetsOptions targets, MetaPublishingOptions meta,
        PlatformPublishingOptions platform) => publishing.Enabled && IsTargetEnabled(target, targets) && (target switch {
        Rc2PublishingTarget.YouTubeLong => youTube.PublishingEnabled,
        Rc2PublishingTarget.YouTubeShort => youTube.PublishingEnabled && platform.YouTubeShortsEnabled,
        Rc2PublishingTarget.FacebookLong => meta.Enabled && meta.PublishFacebookLong &&
            meta.PublishFacebookFullVideo && platform.FacebookEnabled,
        Rc2PublishingTarget.FacebookReel => meta.Enabled && meta.PublishFacebookReel && platform.FacebookEnabled,
        Rc2PublishingTarget.InstagramReel => meta.Enabled && meta.PublishInstagramReel && platform.InstagramReelsEnabled,
        Rc2PublishingTarget.InstagramPost or Rc2PublishingTarget.InstagramCarousel or
        Rc2PublishingTarget.FacebookPost or Rc2PublishingTarget.FacebookCarousel => meta.Enabled,
        _ => false });

    private void LogEnablementConfiguration(Guid planId, Rc2PublishingTarget target)
    {
        var publishing = publishingOptions.Value;
        var youtube = youTubeOptions.Value;
        var targets = targetOptions.Value;
        var meta = metaOptions.Value;
        var storage = publicMediaStorageOptions.Value;
        var platform = platformOptions.Value;
        logger.LogInformation("RC2_PUBLISH_TARGET_CONFIGURATION PlanId={PlanId} Target={Target} PublishingEnabled={PublishingEnabled} YouTubePublishingEnabled={YouTubePublishingEnabled} YouTubeLong={YouTubeLong} YouTubeShort={YouTubeShort} FacebookLong={FacebookLong} FacebookReel={FacebookReel} InstagramReel={InstagramReel} InstagramPost={InstagramPost} InstagramCarousel={InstagramCarousel} FacebookPost={FacebookPost} FacebookCarousel={FacebookCarousel} MetaPublishingEnabled={MetaPublishingEnabled} PublicMediaStorageEnabled={PublicMediaStorageEnabled} YouTubeShortsEnabled={YouTubeShortsEnabled} InstagramReelsEnabled={InstagramReelsEnabled} FacebookEnabled={FacebookEnabled} YouTubeLongSource={YouTubeLongSource} ConfigurationProviders={ConfigurationProviders}",
            planId, target, publishing.Enabled, youtube.PublishingEnabled, targets.YouTubeLong, targets.YouTubeShort,
            targets.FacebookLong, targets.FacebookReel, targets.InstagramReel, targets.InstagramPost,
            targets.InstagramCarousel, targets.FacebookPost, targets.FacebookCarousel, meta.Enabled, storage.Enabled,
            platform.YouTubeShortsEnabled, platform.InstagramReelsEnabled, platform.FacebookEnabled,
            EffectiveSource("PublishingTargets:YouTubeLong"), ProviderNames());
    }

    private string EffectiveSource(string key)
    {
        if (configuration is not IConfigurationRoot root) return "Unavailable";
        return root.Providers.Reverse().FirstOrDefault(provider => provider.TryGet(key, out _))?.ToString() ?? "Default";
    }

    private string ProviderNames() => configuration is IConfigurationRoot root
        ? string.Join(" -> ", root.Providers.Select(provider => provider.ToString())) : "Unavailable";
    private static bool IsCarousel(Rc2PublishingTarget target) => target is Rc2PublishingTarget.InstagramCarousel or Rc2PublishingTarget.FacebookCarousel;
    private static void ValidateTargets(IReadOnlyList<Rc2PublishingTarget>? targets, HashSet<Rc2PublishingTarget> allowed)
    {
        if (targets is null || targets.Count == 0) throw new ArgumentException("At least one explicit target is required.");
        if (targets.Distinct().Count() != targets.Count) throw new ArgumentException("Duplicate targets are not allowed.");
        if (targets.Any(x => !Enum.IsDefined(x) || !allowed.Contains(x))) throw new ArgumentException("RC2_PUBLISH_INVALID_TARGET: request contains a target for the wrong endpoint.");
    }
    private static Rc2PublicationResult Blocked(Rc2PublishingTarget target, string code, string message, int count) =>
        new(target, Rc2PublicationState.Blocked, null, null, false, 0, code, message, IsCarousel(target) ? count : null);
    private static Rc2PublicationResult Result(Rc2PublishingPublication row, Rc2PublicationState state, bool reused,
        string? code = null, string? message = null, int? count = null) => new(row.Target, state, row.RemotePublicationId,
            row.RemoteUrl, reused, row.AttemptCount, code ?? row.FailureCode, message ?? row.FailureMessage, count);
    internal static string NonSecretHealthMessage(TokenHealthResult health) => !health.IsConfigured
        ? $"{health.Platform} credentials are not configured; use the existing OAuth setup flow."
        : $"{health.Platform} credentials are unhealthy or cannot be refreshed; use the existing OAuth setup flow.";
    internal static string NormalizeProviderFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Provider operation failed.";

        var safe = message.Replace('\0', ' ').Trim();
        safe = Regex.Replace(safe, @"(?i)\b(authorization)\s*:\s*(?:bearer\s+)?[^\s,;\r\n]+", "$1: [REDACTED]");
        safe = Regex.Replace(safe,
            "(?i)([\\\"']?(?:access[_-]?token|refresh[_-]?token|client[_-]?secret)[\\\"']?\\s*[:=]\\s*[\\\"']?)[^\\s,;\\\"'&}]+",
            "$1[REDACTED]");
        safe = Regex.Replace(safe, @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+", "Bearer [REDACTED]");
        return safe.Length <= MaximumStoredProviderDiagnosticLength
            ? safe
            : safe[..MaximumStoredProviderDiagnosticLength] + " [diagnostic truncated]";
    }

    internal static string? ConciseApiFailureMessage(string? message)
    {
        if (message is null) return null;
        var safe = NormalizeProviderFailure(message);
        return safe.Length <= MaximumApiFailureMessageLength
            ? safe
            : safe[..MaximumApiFailureMessageLength] + " [details truncated]";
    }
}
