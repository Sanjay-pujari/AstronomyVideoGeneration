using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed record RenderRecipeRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record RenderRecipeResult(
    int PlanCount,
    int SceneCount,
    int RecipeCount,
    int ReadyForExecutionCount,
    int NotReadyCount,
    IReadOnlyList<RenderRecipeDocument> Recipes,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record RenderRecipeDocument(
    string ContentGenerationPlanId,
    string RegionId,
    string ContentCategory,
    string PlannedFormat,
    int SceneNumber,
    string SceneName,
    string Renderer,
    double DurationSeconds,
    int FrameRate,
    RenderRecipeResolution Resolution,
    string OutputVideoPath,
    IReadOnlyList<RenderRecipeInput> Inputs,
    RenderRecipeMotion Motion,
    RenderRecipeCaptions Captions,
    RenderRecipeTransition Transition,
    IReadOnlyList<RenderRecipeFilter> Filters,
    RenderRecipeExecutionReadiness ExecutionReadiness,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record RenderRecipeResolution(int Width, int Height);

public sealed record RenderRecipeInput(
    string InputType,
    string AssetType,
    string AssetPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RenderMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ZIndex,
    string Role);

public sealed record RenderRecipeMotion(
    string Type,
    double StartScale,
    double EndScale,
    string Direction,
    string FilterHint);

public sealed record RenderRecipeCaptions(
    bool Enabled,
    string Source,
    string SafeZone,
    string Style);

public sealed record RenderRecipeTransition(
    string In,
    string Out,
    double DurationSeconds);

public sealed record RenderRecipeFilter(
    string Type,
    bool Enabled);

public sealed record RenderRecipeExecutionReadiness(
    bool ReadyForRenderExecution,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Warnings);

public interface IRenderRecipeGenerator
{
    Task<RenderRecipeResult> GenerateRenderRecipesAsync(RenderRecipeRequest request, CancellationToken cancellationToken);
}
