namespace Astronomy.MediaFactory.Core;

using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using System.Text.Json;

public sealed record SceneAssetsV3Request(
    string? WorkingDirectoryRoot = null,
    bool GenerateShort = true,
    bool GenerateLong = true,
    bool OverwriteExisting = false,
    bool? EnableAccurateSkyGuideV2 = null,
    int LongTargetWidth = 1920,
    int LongTargetHeight = 1080,
    int ShortTargetWidth = 2160,
    int ShortTargetHeight = 3840,
    string ProviderRequestedSize = "1792x1024",
    Phase8AuthorityInput? AuthorityInput = null);

public sealed record SceneAssetsV3Response(
    string OutputRoot,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    string? ShortValidationPath,
    string? LongValidationPath);

public static class SceneAssetsV3SceneContract
{
    public const string ContractSource = nameof(SceneAssetsV3SceneContract);

    private static readonly string[] ShortSceneIds = ["001-hook", "002-cause", "003-accurate-sky-guide", "004-viewing-tip", "005-final-reminder"];
    private static readonly string[] LongSceneIds = ["001-hook", "002-what-is-it", "003-cause", "004-interesting-fact", "005-best-time", "006-accurate-sky-guide", "007-what-you-will-see", "008-viewing-tips", "009-final-reminder"];

    public static IReadOnlyList<string> GetExpectedSceneIds(string format)
        => string.Equals(format, "short", StringComparison.OrdinalIgnoreCase)
            ? ShortSceneIds
            : string.Equals(format, "long", StringComparison.OrdinalIgnoreCase)
                ? LongSceneIds
                : throw new ArgumentException($"Unsupported Scene Assets V3 format '{format}'.", nameof(format));
}

public sealed record SceneAssetsV3Timeline(string Version, string Format, IReadOnlyList<SceneAssetsV3Beat> Beats);

public sealed record SceneAssetsV3Beat(
    int BeatNo,
    string SceneId,
    string RenderMode,
    string NarrationBeat,
    string VisualIntent,
    string VisualSubjectCategory,
    string PrimaryVisualSubject,
    string CameraDistance,
    string OverlayDensity,
    string InformationDensity,
    string OverlayStyle,
    string PromptVariation,
    string CompositionType,
    string OverlayText,
    string? SupportingText,
    string VisualPrompt,
    int ExpectedDurationSec,
    string SceneGuideType = "GenericObjectPair",
    IReadOnlyList<string>? GuideElementsUsed = null,
    string NarrationBeatSource = "generated",
    string VisualPromptSource = "generated",
    string? BlueprintSceneId = null,
    string? StoryFrameId = null,
    string? Variant = null,
    int? SceneOrder = null)
{
    public string DeterministicOverlayText => OverlayText;
}

public sealed record Phase8SceneRequirement(
    string Variant, string SceneId, string BlueprintSceneId, string StoryFrameId, int SceneOrder,
    string SceneRole, string NarrativeStage, string ScenePurpose, string VisualDirection,
    string ObservationDirection, IReadOnlyList<string> RequiredAstronomyObjects,
    IReadOnlyList<string> KnowledgeReferenceIds, string AcceptedNarrationText,
    string AcceptedNarrationSceneId, string VisualOpportunityType, string AssetRole,
    string RenderingPreference, string? LocationContext = null, string? TimeContext = null,
    string? NarrationReleaseCandidateChecksum = null);

/// <summary>The frozen Phase 7 downstream artifact; deliberately contains no working-draft contract.</summary>
public sealed record Phase7AcceptedNarrationScene(string SceneId, int SceneNumber, string BlueprintSceneId,
    string StoryFrameId, IReadOnlyList<string> SelectedKnowledgeReferenceIds,
    IReadOnlyList<string> SelectedClaimIds, string NarrationText);
public sealed record Phase7AcceptedReleaseCandidate(string SchemaVersion, string AttemptId,
    DateTimeOffset GeneratedUtc, string ReleaseCandidateId, string ExecutionId, string PlanId,
    string EventId, string Language, string Variant, string SourceBlueprintAggregateId,
    string SourceBlueprintAggregateChecksum, string SourceVariantBlueprintId,
    string SourceVariantBlueprintChecksum, string SourceStoryFramesAuthorityId,
    string SourceStoryFramesAuthorityChecksum, int BlueprintSceneCount, int AcceptedSceneCount,
    IReadOnlyList<Phase7AcceptedNarrationScene> Scenes, JsonElement AcceptanceResult,
    string DeterministicChecksum);

public sealed record Phase8AuthorityInput(
    string PlanId, string ExecutionId, string EventId, string Language,
    DocumentaryBlueprintAggregate DocumentaryBlueprint,
    string DocumentaryBlueprintChecksum, StoryFramesAuthority StoryFrameAuthority,
    string StoryFrameManifestChecksum,
    Phase7AcceptedReleaseCandidate? LongNarrationReleaseCandidate,
    string? LongNarrationReleaseCandidateChecksum,
    Phase7AcceptedReleaseCandidate? ShortNarrationReleaseCandidate,
    string? ShortNarrationReleaseCandidateChecksum,
    IReadOnlyList<string> RequestedVariants,
    IReadOnlyList<Phase8SceneRequirement> LongScenes,
    IReadOnlyList<Phase8SceneRequirement> ShortScenes);

public static class Phase8AuthorityReasonCodes
{
    public const string Missing = "P8_AUTHORITY_MISSING";
    public const string NotCommitted = "P8_AUTHORITY_NOT_COMMITTED";
    public const string ChecksumMismatch = "P8_AUTHORITY_CHECKSUM_MISMATCH";
    public const string NarrationCandidatePhysicalChecksumMismatch = "P8_NARRATION_CANDIDATE_PHYSICAL_CHECKSUM_MISMATCH";
    public const string NarrationCandidateSemanticChecksumMismatch = "P8_NARRATION_CANDIDATE_SEMANTIC_CHECKSUM_MISMATCH";
    public const string NarrationManifestMismatch = "P8_NARRATION_MANIFEST_MISMATCH";
    public const string NarrationCertificationInvalid = "P8_NARRATION_CERTIFICATION_INVALID";
    public const string IdentityMismatch = "P8_AUTHORITY_IDENTITY_MISMATCH";
    public const string SceneLineageMismatch = "P8_SCENE_LINEAGE_MISMATCH";
    public const string NarrationSceneMappingFailed = "P8_NARRATION_SCENE_MAPPING_FAILED";
    public const string VariantAuthorityMissing = "P8_VARIANT_AUTHORITY_MISSING";
}

public sealed class Phase8AuthorityException(string reasonCode, IReadOnlyList<string> errors)
    : InvalidOperationException($"{reasonCode}: {string.Join("; ", errors)}")
{
    public string ReasonCode { get; } = reasonCode;
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class Phase8AuthorityLoadDiagnostics
{
    public bool Phase4AuthorityLoadStarted { get; set; }
    public bool Phase4AuthorityLoaded { get; set; }
    public bool Phase6AuthorityLoadStarted { get; set; }
    public bool Phase6AuthorityLoaded { get; set; }
    public bool ShortNarrationAuthorityLoadStarted { get; set; }
    public bool ShortNarrationAuthorityLoaded { get; set; }
    public bool LongNarrationAuthorityLoadStarted { get; set; }
    public bool LongNarrationAuthorityLoaded { get; set; }
    public bool AuthorityProjectionStarted { get; set; }
    public bool AuthorityProjectionCompleted { get; set; }
    public string? AuthorityFailureStage { get; set; }
    public string? AuthorityFailureType { get; set; }
    public string? AuthorityFailureMessage { get; set; }
    public string? NarrationChecksumDiagnostics { get; set; }
}
public sealed record Phase8AuthorityLoadRequest(string OutputRoot, string PlanId, string EventId,
    string Language, IReadOnlyList<string> RequestedVariants, Phase8AuthorityLoadDiagnostics? Diagnostics = null);
public interface IPhase8AuthorityLoader
{
    Task<Phase8AuthorityInput> LoadAsync(Phase8AuthorityLoadRequest request, CancellationToken cancellationToken);
}

public sealed record SceneAssetManifest(string SchemaVersion, string PlanId, string ExecutionId,
    string EventId, string Language, DateTimeOffset GeneratedAtUtc, string PublicationState,
    string DocumentaryBlueprintChecksum, string StoryFrameManifestChecksum,
    string? LongNarrationReleaseCandidateChecksum, string? ShortNarrationReleaseCandidateChecksum,
    IReadOnlyList<string> RequestedVariants, IReadOnlyList<SceneAssetManifestItem> Assets,
    string ValidationStatus, string DeterministicChecksum);
public sealed record SceneAssetManifestItem(string AssetId, string Variant, string SceneId,
    string BlueprintSceneId, string StoryFrameId, int SceneOrder, string AssetRole,
    string VisualOpportunityType, string ProviderType, string? ProviderResultId,
    string ProviderStatus, string SourceInstructionId, IReadOnlyList<string> SourceKnowledgeReferenceIds,
    string PhysicalPath, int Width, int Height, string AspectRatio, string Checksum,
    string SemanticIdentity, bool SharedAsset, string? SharedAssetOwner,
    IReadOnlyList<string> SharedAssetConsumers, bool Reused, bool ProviderCalledThisExecution,
    string ValidationStatus, IReadOnlyList<string> Warnings);
public sealed record Phase8ManifestValidationResult(bool IsValid, IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Errors);
public interface IPhase8SceneAssetManifestValidator
{
    Task<Phase8ManifestValidationResult> ValidateAsync(SceneAssetManifest manifest,
        Phase8AuthorityInput authority, string outputRoot, CancellationToken cancellationToken);
}

public sealed record SceneAssetsV3Manifest(
    string Version,
    string Format,
    int SceneCount,
    IReadOnlyList<SceneAssetsV3ManifestScene> Scenes);

public sealed record SceneAssetsV3ManifestScene(
    string SceneId,
    string RenderMode,
    string ImagePath,
    string NarrationBeat,
    string VisualIntent,
    string VisualSubjectCategory,
    string PrimaryVisualSubject,
    string CameraDistance,
    string OverlayDensity,
    string InformationDensity,
    string OverlayStyle,
    string CompositionType,
    string OverlayText,
    string? SupportingText,
    string SceneGuideType,
    IReadOnlyList<string> GuideElementsUsed,
    string Hash,
    bool ProviderCalled,
    bool ProviderSucceeded);

public sealed record SceneAssetsV3Review(
    int SceneCount,
    bool AccurateSkyGuidePresent,
    int CinematicSceneCount,
    int ExplainerSceneCount,
    int ViewingTipsSceneCount,
    bool DuplicateHashDetected,
    bool RepeatedBackgroundDetected,
    bool SameBackgroundDetected,
    bool SameCompositionDetected,
    bool SameCameraAngleDetected,
    bool AllScenesHaveNarrationBeat,
    IReadOnlyList<string> VisualIntentSequence,
    int PromptDiversityScore,
    bool RepeatedPromptDetected,
    IReadOnlyList<string> ForbiddenTermsDetected,
    int OverlayDensityScore,
    IReadOnlyList<string> RelativeDateWordsDetected,
    int DistinctCompositionTypeCount,
    string Status);

public sealed record SceneAssetsV3Validation(
    string Version,
    string Format,
    string Status,
    bool VisualTimelineExists,
    bool SceneManifestExists,
    bool ExpectedSceneCountPresent,
    bool AccurateSkyGuidePresent,
    bool DuplicateHashDetected,
    bool RepeatedGenericInfographicBackgroundDetected,
    bool SameBackgroundDetected,
    bool SameCompositionDetected,
    bool SameCameraAngleDetected,
    bool EverySceneHasNarrationBeat,
    bool EverySceneHasVisualIntent,
    int PromptDiversityScore,
    bool RepeatedPromptDetected,
    IReadOnlyList<string> ForbiddenTermsDetected,
    IReadOnlyList<string> RelativeDateWordsDetected,
    int DistinctCompositionTypeCount,
    IReadOnlyList<string> Errors,
    SceneAssetsV3FontDiagnostics? FontDiagnostics = null,
    IReadOnlyList<string>? ExpectedSceneIds = null,
    IReadOnlyList<string>? ActualSceneIds = null,
    IReadOnlyList<string>? MissingSceneIds = null,
    IReadOnlyList<string>? ExtraSceneIds = null,
    IReadOnlyList<string>? ExpectedSceneAssetPaths = null,
    IReadOnlyList<string>? ActualSceneAssetPaths = null,
    string SceneContractSource = SceneAssetsV3SceneContract.ContractSource);

public sealed record SceneTimelineMetadata(
    string SceneId,
    string RenderMode,
    string VisualIntent,
    string VisualSubjectCategory,
    string PrimaryVisualSubject,
    string CameraDistance,
    string OverlayDensity,
    string InformationDensity,
    string OverlayStyle,
    string PromptVariation,
    string CompositionType,
    string OverlayText,
    string? SupportingText,
    string NarrationBeat,
    int EstimatedDurationSec,
    string SceneGuideType,
    IReadOnlyList<string> GuideElementsUsed,
    string RecommendedTransition,
    string RecommendedMotion);

public sealed record SceneTimelineMetadataDocument(string Version, string Format, IReadOnlyList<SceneTimelineMetadata> Scenes);

public sealed record SceneAssetsV3FontDiagnostics(
    string RequestedFont,
    string ResolvedFont,
    bool FontFallbackUsed,
    IReadOnlyList<string> CheckedFontPaths);

public interface ISceneAssetsV3Service
{
    Task<SceneAssetsV3Response> GenerateAsync(SceneAssetsV3Request request, CancellationToken cancellationToken);
}
