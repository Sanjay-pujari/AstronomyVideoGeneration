namespace Astronomy.MediaFactory.Core;

public sealed record NarrationPlanningRequest(
    string? RegionId = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    IReadOnlyList<Guid>? PlanIds = null,
    string Language = "en",
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record NarrationPlanningResult(
    int PlanCount,
    int GeneratedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<NarrationScriptDocument> NarrationScripts,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record NarrationScriptDocument(
    string ContentGenerationPlanId,
    string AstronomyEventIntelligenceId,
    string AstronomyContentOpportunityId,
    string ContentCategory,
    string? PlannedFormat,
    string Language,
    string RegionId,
    string LocationName,
    string Title,
    string NarrationStyle,
    int EstimatedDurationSeconds,
    IReadOnlyList<NarrationScriptSegment> Segments,
    IReadOnlyList<string> QualityChecklist,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record NarrationScriptSegment(
    int SceneNumber,
    string SceneName,
    string VoiceTone,
    string Script,
    int EstimatedDurationSeconds,
    string OnScreenTextHint,
    string AssetCue,
    string TransitionHint);

public interface INarrationPlanningService
{
    Task<NarrationPlanningResult> GenerateNarrationScriptsAsync(NarrationPlanningRequest request, CancellationToken cancellationToken);
}
