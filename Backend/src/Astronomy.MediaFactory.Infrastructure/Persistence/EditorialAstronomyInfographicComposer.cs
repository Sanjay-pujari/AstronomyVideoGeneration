using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EditorialAstronomyInfographicComposer(
    QuestionDrivenVisualComposer questionDrivenVisualComposer,
    IAstronomyInfographicDesignSystem designSystem,
    ILogger<EditorialAstronomyInfographicComposer> logger) : IEditorialAstronomyInfographicComposer
{
    public async Task<EditorialAstronomyInfographicGenerationResponse> GenerateEditorialAstronomyInfographicsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        logger.LogInformation("Composing golden-event editorial astronomy infographics with the professional design system. EventId={EventId}; RegionId={RegionId}; DryRun={DryRun}", request.EventId, request.RegionId, request.DryRun);

        var response = await questionDrivenVisualComposer.GenerateEditorialAstronomyInfographicsAsync(request, cancellationToken);
        var plannedScenes = response.PlannedScenes ?? [];

        // Materialize design-system templates during dry runs too, so invalid scene/question layouts fail before any image generation.
        var designSpecs = new List<AstronomyInfographicDesignTemplate>();
        foreach (var scene in plannedScenes)
        {
            designSpecs.Add(designSystem.CreateTemplate(new AstronomyInfographicDesignRequest(
                request.EventId,
                request.RegionId,
                request.Language,
                scene.SceneNumber,
                scene.QuestionType,
                scene.ScenePurpose,
                scene.ViewerQuestion,
                scene.ViewerTakeaway,
                scene.NarrationText,
                scene.CaptionText,
                Math.Max(4, scene.ValidationPreview.SrtReady ? 4 : 0),
                scene.VisualIntent,
                scene.ImagePromptIntent,
                scene.OverlayIntent,
                scene.AccessibilityIntent,
                scene.ProgrammaticOverlayPlan.LocalAssetObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase),
                scene.ProgrammaticOverlayPlan.LocalAssetObjects.Contains("Jupiter", StringComparer.OrdinalIgnoreCase))));
        }

        return new EditorialAstronomyInfographicGenerationResponse(
            response.EventId,
            response.SceneCount,
            response.PlannedInfographicCount,
            response.FinalImageCount,
            response.SrtCount,
            response.ApprovedSceneCount,
            response.FailedSceneCount,
            plannedScenes,
            response.GeneratedFiles,
            response.Warnings,
            designSpecs,
            response.CompositionMode,
            response.UsesSharedAstronomyVisualComposer,
            response.QuestionIsolationScore,
            response.CrossSceneLeakageDetected,
            response.SceneValidation,
            response.AstronomySceneEngineV1Status,
            response.SharedAstronomyVisualComposerStatus,
            response.HeroAssetRulesApplied,
            response.DuplicateObjectRenderingDetected,
            response.SceneVariantFinalImages,
            response.Diagnostics,
            response.ShortFormValidation);
    }
}
