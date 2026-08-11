namespace Astronomy.MediaFactory.Core;

public enum PublishingPackageRole
{
    ShortVideo, LongVideo, ShortCaptionSrt, ShortCaptionAss, LongCaptionSrt, LongCaptionAss,
    ThumbnailLandscape, ThumbnailPortrait, ThumbnailSquare,
    HeroLandscape, HeroPortrait, HeroSquare, GalleryImage, Metadata
}

public enum PublishingDecisionStatus { Pending, Approved, Rejected }
public sealed record PublishingDecision(string DecisionId, PublishingDecisionStatus Status, string PolicyVersion,
    DateTimeOffset? DecisionUtc, string? ReviewerSubjectReference, string Source);

public sealed record PublishingManifestEntry(PublishingPackageRole Role, string Format, string Language,
    int SourcePhase, string SourceAuthorityChecksum, string SourceRelativePath, string? PackageRelativePath,
    string Sha256, long ByteLength, string ContentType, int? Sequence = null);

public static class Phase20ReasonCodes
{
    public const string UpstreamPhase19Invalid = "P20_UPSTREAM_PHASE19_INVALID";
    public const string SupportingAuthorityInvalid = "P20_SUPPORTING_AUTHORITY_INVALID";
    public const string GatePending = "P20_PUBLISH_GATE_PENDING";
    public const string GateRejected = "P20_PUBLISH_GATE_REJECTED";
    public const string ArtifactMissing = "P20_PACKAGE_ARTIFACT_MISSING";
    public const string ChecksumMismatch = "P20_PACKAGE_CHECKSUM_MISMATCH";
    public const string Accepted = "P20_PUBLISHING_AUTHORITY_ACCEPTED";
}
