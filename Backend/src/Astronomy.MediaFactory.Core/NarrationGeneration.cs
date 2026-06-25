using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed record NarrationPreviewRequest(
    string? PlanId,
    string EventType,
    string EventName,
    string? ShortTitle,
    string Language,
    string RegionId,
    string? Format,
    JsonElement? EventMetadata,
    bool ReturnScenes = true);

public sealed record NarrationPreviewResponse(
    string? PlanId,
    string EventType,
    string EventName,
    string Language,
    string RegionId,
    string? Format,
    IReadOnlyList<NarrationPreviewScene> Scenes,
    NarrationValidationResult OverallValidation,
    NarrationFormattingDiagnostics FormattingDiagnostics);

public sealed record NarrationPreviewScene(
    string SceneId,
    string ScenePurpose,
    string Narration,
    NarrationValidationResult Validation);

public sealed record NarrationValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record NarrationFormattingDiagnostics(
    string EventDate,
    string PeakTime,
    string ViewingWindow,
    string Direction,
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> Warnings);

public interface INarrationGenerationService
{
    Task<NarrationPreviewResponse> GeneratePreviewAsync(NarrationPreviewRequest request, CancellationToken cancellationToken);
    Task<NarrationPreviewResponse> GenerateProductionNarrationAsync(NarrationPreviewRequest request, CancellationToken cancellationToken);
}
