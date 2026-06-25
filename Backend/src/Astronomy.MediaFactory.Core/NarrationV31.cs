namespace Astronomy.MediaFactory.Core;

public sealed record NarrationV31PreviewRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = true,
    ProductionPipelineExecutionContext? ProductionContext = null,
    string? OutputRoot = null,
    string? EventType = null,
    string? Title = null,
    string? LocalPeakTime = null,
    string? SkyDirectionHint = null,
    string? BestViewingWindowLocal = null);

public sealed record NarrationV31PreviewResponse(
    string EventId,
    string RegionId,
    string Language,
    bool IsValid,
    QuestionDrivenNarrationDto ShortNarration,
    QuestionDrivenNarrationDto LongNarration,
    NarrationV31QualityReport Quality,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record NarrationV31QualityReport(
    bool IsValid,
    bool HasRequiredSceneCounts,
    bool HasNoDuplicateNarration,
    bool HasNoAuthoringInstructions,
    bool HasLocalizedTimeFormatting,
    bool HindiTerminologyApplied,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public interface INarrationV31Composer
{
    Task<NarrationV31PreviewResponse> PreviewAsync(NarrationV31PreviewRequest request, CancellationToken cancellationToken);
    Task<NarrationV31PreviewResponse> WriteFinalSceneNarrationAsync(NarrationV31PreviewRequest request, CancellationToken cancellationToken);
}
