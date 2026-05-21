using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumImageCaptureExecutor(IOptions<StellariumOptions> options, ILogger<StellariumImageCaptureExecutor> logger) : IStellariumImageCaptureExecutor
{
    private readonly StellariumOptions _options = options.Value;

    public Task<StellariumCaptureExecutionResponse> CaptureAsync(StellariumSceneCapturePlan scenePlan, StellariumCaptureExecutionRequest request, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var images = new List<StellariumCapturedImageResult>();
        var outputRoot = _options.CaptureDirectory;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            outputRoot = string.IsNullOrWhiteSpace(_options.OutputRoot) ? "outputs" : _options.OutputRoot;
            warnings.Add("Stellarium:CaptureDirectory is not configured; fallback output path used.");
        }

        var outputFolder = string.IsNullOrWhiteSpace(_options.CaptureDirectory)
            ? Path.Combine(outputRoot, request.ContentGenerationPlanId.ToString(), "stellarium-scenes")
            : Path.Combine(outputRoot, "content-plans", request.ContentGenerationPlanId.ToString(), "stellarium-scenes");

        if (!request.DryRun)
        {
            Directory.CreateDirectory(outputFolder);
        }

        if (request.DryRun)
        {
            warnings.Add("DryRun enabled. No images were captured.");
        }
        else if (!_options.Enabled)
        {
            warnings.Add("Stellarium capture is disabled in configuration.");
        }
        else if (_options.UseExistingCaptureUtility)
        {
            warnings.Add("Stellarium capture utility not wired yet.");
        }

        foreach (var scene in scenePlan.Scenes.OrderBy(x => x.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = $"{scene.SortOrder:D2}_{scene.SceneCode}_{scene.OutputImageRole}.png";
            var imagePath = Path.Combine(outputFolder, fileName);

            var success = request.DryRun || (!_options.Enabled) || !_options.UseExistingCaptureUtility;
            string? errorMessage = null;

            if (!request.DryRun && _options.Enabled && _options.UseExistingCaptureUtility)
            {
                success = false;
                errorMessage = "Stellarium capture utility not wired yet.";
            }

            logger.LogInformation(
                "Prepared Stellarium capture scene {SceneCode} at {CaptureTimeUtc} for {Latitude},{Longitude} target={TargetObjectCode} fov={FieldOfView} flags=({ConstellationLines},{ConstellationLabels},{PlanetLabels},{AzimuthGrid},{EquatorialGrid})",
                scene.SceneCode,
                scene.CaptureTimeUtc,
                scenePlan.Latitude,
                scenePlan.Longitude,
                scene.TargetObjectCode,
                scene.FieldOfViewDegrees,
                scene.ShowConstellationLines,
                scene.ShowConstellationLabels,
                scene.ShowPlanetLabels,
                scene.ShowAzimuthGrid,
                scene.ShowEquatorialGrid);

            images.Add(new StellariumCapturedImageResult(
                scene.SceneCode,
                scene.SceneType,
                scene.OutputImageRole,
                scene.TargetObjectCode,
                scene.CaptureTimeUtc,
                imagePath,
                success,
                errorMessage));
        }

        var capturedCount = images.Count(x => x.Success && !request.DryRun);
        var overallSuccess = request.DryRun || images.Any(x => x.Success);
        return Task.FromResult(new StellariumCaptureExecutionResponse(
            request.ContentGenerationPlanId,
            overallSuccess,
            images.Count,
            capturedCount,
            outputFolder,
            images,
            warnings,
            overallSuccess ? null : "All Stellarium scene captures failed."));
    }
}
