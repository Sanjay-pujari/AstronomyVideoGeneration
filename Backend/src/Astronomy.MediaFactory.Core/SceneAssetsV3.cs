namespace Astronomy.MediaFactory.Core;

public sealed record SceneAssetsV3Request(
    string? WorkingDirectoryRoot = null,
    bool GenerateShort = true,
    bool GenerateLong = true,
    bool OverwriteExisting = false);

public sealed record SceneAssetsV3Response(
    string OutputRoot,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    string? ShortValidationPath,
    string? LongValidationPath);

public sealed record SceneAssetsV3Timeline(string Version, string Format, IReadOnlyList<SceneAssetsV3Beat> Beats);

public sealed record SceneAssetsV3Beat(
    int BeatNo,
    string SceneId,
    string RenderMode,
    string NarrationBeat,
    string VisualIntent,
    string VisualPrompt,
    int ExpectedDurationSec);

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
    bool AllScenesHaveNarrationBeat,
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
    bool EverySceneHasNarrationBeat,
    IReadOnlyList<string> Errors,
    SceneAssetsV3FontDiagnostics? FontDiagnostics = null);

public sealed record SceneAssetsV3FontDiagnostics(
    string RequestedFont,
    string ResolvedFont,
    bool FontFallbackUsed,
    IReadOnlyList<string> CheckedFontPaths);

public interface ISceneAssetsV3Service
{
    Task<SceneAssetsV3Response> GenerateAsync(SceneAssetsV3Request request, CancellationToken cancellationToken);
}
