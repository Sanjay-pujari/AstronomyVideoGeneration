using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rc2PublishingTarget { YouTubeLong, YouTubeShort, FacebookLong, FacebookReel, InstagramReel, InstagramPost, InstagramCarousel, FacebookPost, FacebookCarousel }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rc2PublishingMediaType { Video, Hero, Gallery }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rc2PublicationState { NotPublished, Publishing, Published, Failed, AlreadyPublished, Blocked }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rc2PublishingApprovalStatus { NotAvailable, Pending, Approved, Rejected }

public sealed record Rc2CreatePublishingPackageRequest(Guid PlanId, bool OverwriteExisting);
public sealed record Rc2SetPublishingApprovalRequest(Guid PlanId, Rc2PublishingApprovalStatus Decision);

public sealed record Rc2PublishingPlan(Guid PlanId, string Title, string Language, string RegionId,
    string PlanOutputRoot, string Phase19Root, string Phase20Root, string Phase20ValidationPath);

public sealed record Rc2Phase19Status(bool Exists, bool TechnicalQaApproved, string? AuthorityChecksum, bool DownstreamReady);
public sealed record Rc2Phase20Status(bool Exists, string Status, string? PublishingPackageId, string? AuthorityChecksum,
    bool PublicationPackageReady, bool PublishGateChecked, bool PublishApproved, bool DownstreamReady,
    Rc2PublishingApprovalStatus ApprovalStatus, int ArtifactCount);
public sealed record Rc2TargetStatus(bool PackageAvailable, bool Configured, bool Enabled, string CredentialHealth, Rc2PublicationState PublicationState, string? BlockReason);
public sealed record Rc2PublishingStatusResponse(Guid PlanId, string Title, string Language, string RegionId,
    Rc2Phase19Status Phase19, Rc2Phase20Status Phase20, IReadOnlyDictionary<string, int> AvailableRoles,
    IReadOnlyList<Rc2PublishingTarget> AvailableTargets, IReadOnlyDictionary<Rc2PublishingTarget, Rc2TargetStatus> Targets);
public sealed record Rc2PublishingPackageResponse(Guid PlanId, string Language, string Phase20Status,
    string PublishingPackageId, string AuthorityChecksum, bool TechnicalQaApproved, bool PublicationPackageReady,
    bool PublishGateChecked, bool PublishApproved, bool DownstreamReady, Rc2PublishingApprovalStatus ApprovalStatus,
    int ArtifactCount, IReadOnlyList<Rc2PublishingTarget> AvailableTargets);
public sealed record Rc2PublishingApprovalResponse(Guid PlanId, string PublishingPackageId, string AuthorityChecksum,
    Rc2PublishingApprovalStatus ApprovalStatus, bool TechnicalQaApproved, bool PublicationPackageReady,
    bool PublishGateChecked, bool PublishApproved, bool DownstreamReady, DateTimeOffset? ApprovedUtc);

public interface IRc2PublishingControlService
{
    Task<Rc2PublishingPackageResponse> CreateOrRefreshPackageAsync(Guid planId, bool overwriteExisting, CancellationToken cancellationToken);
    Task<Rc2PublishingApprovalResponse> SetApprovalAsync(Guid planId, Rc2PublishingApprovalStatus decision, CancellationToken cancellationToken);
    Task<Rc2PublishingStatusResponse> GetStatusAsync(Guid planId, CancellationToken cancellationToken);
}

public sealed class Rc2PublishingControlException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class Rc2PublishingApproval
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public required string PublishingPackageId { get; set; }
    public required string Phase20AuthorityChecksum { get; set; }
    public Rc2PublishingApprovalStatus Decision { get; set; }
    public DateTimeOffset DecisionUtc { get; set; }
    public required string DecisionSource { get; set; }
}
