namespace Astronomy.MediaFactory.Core;

public sealed record TtsAudioGenerationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    int? MaxPlans = 1,
    bool DryRun = true,
    bool OverwriteExisting = false,
    bool CombineSegments = true);

public sealed record TtsAudioGenerationResult(
    int PlanCount,
    int SegmentAudioCount,
    int CombinedAudioCount,
    int CompletedCount,
    int FailedCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record TtsAudioManifest(
    string ContentGenerationPlanId,
    string RegionId,
    string VoiceName,
    string Provider,
    IReadOnlyList<TtsAudioManifestSegment> Segments,
    string CombinedAudioPath,
    double TotalDurationSeconds,
    DateTimeOffset GeneratedUtc);

public sealed record TtsAudioManifestSegment(
    int SceneNumber,
    string AudioPath,
    double DurationSeconds,
    long FileSizeBytes,
    string Status);

public interface ITtsAudioGenerationService
{
    Task<TtsAudioGenerationResult> GenerateTtsAudioAsync(TtsAudioGenerationRequest request, CancellationToken cancellationToken);
}
