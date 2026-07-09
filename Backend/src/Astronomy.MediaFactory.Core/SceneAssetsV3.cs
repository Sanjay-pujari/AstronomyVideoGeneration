namespace Astronomy.MediaFactory.Core;

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
    string ProviderRequestedSize = "1792x1024");

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
    string VisualPromptSource = "generated")
{
    public string DeterministicOverlayText => OverlayText;
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
