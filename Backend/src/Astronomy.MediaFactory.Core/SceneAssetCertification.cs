namespace Astronomy.MediaFactory.Core;

public static class Phase10ReasonCodes
{
    public const string Accepted = "P10_SCENE_ASSET_CERTIFICATION_ACCEPTED";
    public const string Phase8Invalid = "P10_PHASE8_AUTHORITY_INVALID";
    public const string Phase9Invalid = "P10_PHASE9_AUTHORITY_INVALID";
    public const string ShortSetMismatch = "P10_SHORT_SCENE_SET_MISMATCH";
    public const string LongSetMismatch = "P10_LONG_SCENE_SET_MISMATCH";
    public const string LongEquivalenceMismatch = "P10_LONG_PHASE8_PHASE9_MISMATCH";
}

public sealed record SceneVariantCertification(bool Requested, int ExpectedSceneCount,
    int ActualSceneCount, int CertifiedSceneCount, IReadOnlyList<string> SceneIds,
    IReadOnlyList<string> MissingSceneIds, IReadOnlyList<string> ExtraSceneIds,
    bool DimensionValidationPassed, bool PhysicalChecksumValidationPassed,
    bool LineageValidationPassed, bool ScientificEvidenceValidationPassed,
    string ValidationStatus, int? Phase8SceneCount = null, int? Phase9SceneCount = null,
    bool? Phase8Phase9EquivalencePassed = null);

public sealed record SceneAssetCertification(string SchemaVersion, string PlanId, string ExecutionId,
    string EventId, string Language, DateTimeOffset GeneratedAtUtc, IReadOnlyList<string> RequestedVariants,
    string Phase6StoryFrameAuthorityChecksum, string Phase8SceneAssetAuthorityChecksum,
    string? Phase9LongSceneAuthorityChecksum, SceneVariantCertification ShortCertification,
    SceneVariantCertification LongCertification, int TotalExpectedSceneCount, int TotalCertifiedSceneCount,
    bool CrossVariantValidation, string ValidationStatus, string PublicationState,
    string DeterministicChecksum, bool DownstreamReady);

public sealed record Phase10CertificationRequest(string OutputRoot, string PlanId, string EventId,
    string Language, bool RequestedShort, bool RequestedLong);
public sealed record Phase10CertificationResult(string ReasonCode, string Reason,
    SceneAssetCertification Certification, IReadOnlyList<string> InputFiles, IReadOnlyList<string> OutputFiles);
public interface ISceneAssetCertificationService
{
    Task<Phase10CertificationResult> CertifyAsync(Phase10CertificationRequest request, CancellationToken cancellationToken);
}
