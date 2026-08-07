namespace Astronomy.MediaFactory.Core;

public static class Phase9ReasonCodes
{
    public const string LongNotRequested = "P9_LONG_NOT_REQUESTED";
    public const string Accepted = "P9_LONG_SCENE_IMAGE_AUTHORITY_ACCEPTED";
    public const string Phase8Missing = "P9_PHASE8_AUTHORITY_MISSING";
    public const string Phase8Invalid = "P9_PHASE8_AUTHORITY_INVALID";
    public const string Phase8ChecksumMismatch = "P9_PHASE8_CHECKSUM_MISMATCH";
    public const string Phase8NotCommitted = "P9_PHASE8_NOT_COMMITTED";
    public const string Phase8NotDownstreamReady = "P9_PHASE8_NOT_DOWNSTREAM_READY";
    public const string SourceMissing = "P9_SOURCE_ASSET_MISSING";
    public const string SourceChecksumMismatch = "P9_SOURCE_ASSET_CHECKSUM_MISMATCH";
    public const string SourceInvalid = "P9_SOURCE_ASSET_INVALID";
    public const string SourceDimensionMismatch = "P9_SOURCE_ASSET_DIMENSION_MISMATCH";
    public const string SourceLineageMismatch = "P9_SOURCE_ASSET_LINEAGE_MISMATCH";
    public const string ScientificEvidenceInvalid = "P9_SCIENTIFIC_EVIDENCE_INVALID";
    public const string SceneSetMismatch = "P9_SCENE_SET_MISMATCH";
}

public sealed record LongSceneImageManifest(string SchemaVersion, string PlanId, string ExecutionId,
    string EventId, string Language, string Variant, DateTimeOffset GeneratedAtUtc,
    string Phase8SceneAssetManifestChecksum, string Phase6StoryFrameManifestChecksum,
    int ExpectedSceneCount, int ActualSceneCount, IReadOnlyList<LongSceneImageManifestItem> Images,
    string ValidationStatus, string PublicationState, string DeterministicChecksum, bool DownstreamReady)
{
    // Both names are published deliberately: Phase8AuthorityChecksum is the downstream
    // lineage term, while the longer name identifies the concrete Phase 8 document.
    public string Phase8AuthorityChecksum => Phase8SceneAssetManifestChecksum;
}

public sealed record LongSceneImageManifestItem(string AssetId, string SceneId, string BlueprintSceneId,
    string StoryFrameId, int SceneOrder, string SourcePhase8AssetId, string SourcePhase8SemanticIdentity,
    string VisualStyle, string? BaseImageProvider, string? AstronomyGeometryProvider, string? FinalRenderer,
    string SourcePhase8PhysicalPath, string PhysicalPath, int Width, int Height, string AspectRatio,
    string PhysicalSha256, bool Materialized, bool Reused, bool Regenerated,
    bool RequiresScientificGeometry, bool ScientificGeometryCertified, string? ScientificEvidencePath,
    string ValidationStatus, IReadOnlyList<string> Warnings);

public sealed record LongSceneImageValidationResult(bool IsValid, IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Errors);
public interface ILongSceneImageManifestValidator
{
    Task<LongSceneImageValidationResult> ValidateAsync(LongSceneImageManifest manifest, SceneAssetManifest phase8,
        Phase8AuthorityInput phase6Authority, string packageRoot, CancellationToken cancellationToken);
}
public sealed record LongSceneImagePublicationRequest(string OutputRoot, string PlanId, string EventId,
    string Language, bool OverwriteExisting);
public sealed record LongSceneImagePublicationResult(string ReasonCode, string Reason,
    LongSceneImageManifest Manifest, IReadOnlyList<string> OutputFiles, bool Reused);
public interface ILongSceneImagePublicationService
{
    Task<LongSceneImagePublicationResult> PublishAsync(LongSceneImagePublicationRequest request,
        CancellationToken cancellationToken);
}
public interface IPhase9CommittedAuthorityReader
{
    Task<LongSceneImageManifest?> ReadAsync(string outputRoot, CancellationToken cancellationToken);
}
