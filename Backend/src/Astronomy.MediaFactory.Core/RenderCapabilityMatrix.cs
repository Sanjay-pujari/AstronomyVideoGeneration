using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed record RenderCapabilityMatrixRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record RenderCapabilityMatrixResult(
    int PlanCount,
    int SceneCount,
    int CapabilityCount,
    int CanExecuteCount,
    int BlockedCount,
    IReadOnlyList<RenderCapabilityDocument> Capabilities,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record RenderCapabilityDocument(
    string ContentGenerationPlanId,
    string RegionId,
    int SceneNumber,
    string SceneName,
    string RecipePath,
    string OutputVideoPath,
    string Renderer,
    IReadOnlyList<RenderCapabilityHandler> RequiredHandlers,
    RenderCapabilityAudioHandler AudioHandler,
    RenderCapabilityMotionHandler MotionHandler,
    RenderCapabilityCaptionHandler CaptionHandler,
    RenderCapabilityTransitionHandler TransitionHandler,
    RenderCapabilityExecutionPlan ExecutionPlan,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record RenderCapabilityHandler(
    string RenderMode,
    string Handler,
    bool Required,
    bool Available,
    string Notes);

public sealed record RenderCapabilityAudioHandler(
    string Handler,
    bool Required,
    bool Available);

public sealed record RenderCapabilityMotionHandler(
    string MotionType,
    string FilterHint,
    string Handler,
    bool Available);

public sealed record RenderCapabilityCaptionHandler(
    bool Enabled,
    string Handler,
    bool Available);

public sealed record RenderCapabilityTransitionHandler(
    [property: JsonPropertyName("in")] string In,
    [property: JsonPropertyName("out")] string Out,
    string Handler,
    bool Available);

public sealed record RenderCapabilityExecutionPlan(
    bool CanExecute,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Fallbacks);

public interface IRenderCapabilityMatrixService
{
    Task<RenderCapabilityMatrixResult> GenerateRenderCapabilitiesAsync(RenderCapabilityMatrixRequest request, CancellationToken cancellationToken);
}
