using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumImageCaptureExecutor(IOptions<StellariumOptions> options, ILogger<StellariumImageCaptureExecutor> logger) : IStellariumImageCaptureExecutor
{
    private readonly StellariumOptions _options = options.Value;
    private const string DisabledMessage = "Stellarium capture is disabled in configuration.";
    private const string UtilityNotWiredMessage = "Stellarium capture utility is not wired yet.";

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

        if (request.DryRun)
        {
            warnings.Add("DryRun enabled. No images were captured.");
        }
        else if (!_options.Enabled)
        {
            warnings.Add(DisabledMessage);
        }
        else if (!_options.UseExistingCaptureUtility)
        {
            warnings.Add(UtilityNotWiredMessage);
        }
        else
        {
            Directory.CreateDirectory(outputFolder);
        }

        foreach (var scene in scenePlan.Scenes.OrderBy(x => x.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = $"{scene.SortOrder:D2}_{scene.SceneCode}_{scene.OutputImageRole}.png";
            var imagePath = Path.Combine(outputFolder, fileName);

            var success = false;
            string? errorMessage = null;

            if (request.DryRun)
            {
                success = true;
            }
            else if (!_options.Enabled)
            {
                errorMessage = DisabledMessage;
            }
            else if (!_options.UseExistingCaptureUtility)
            {
                errorMessage = UtilityNotWiredMessage;
            }
            else
            {
                // Capture command execution is not yet implemented. Only mark success when output file physically exists.
                success = IsRealCapturedFile(imagePath);
                if (!success)
                {
                    errorMessage = "Capture command completed but output file was not created.";
                }
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

        var capturedCount = images.Count(x => !request.DryRun && IsRealCapturedFile(x.ImagePath));
        var requestedCount = images.Count;
        var overallSuccess = request.DryRun || capturedCount == requestedCount;
        if (!request.DryRun && capturedCount < requestedCount)
        {
            warnings.Add($"Missing Stellarium output files: expected {requestedCount}, found {capturedCount}.");
        }

        return Task.FromResult(new StellariumCaptureExecutionResponse(
            request.ContentGenerationPlanId,
            overallSuccess,
            requestedCount,
            capturedCount,
            outputFolder,
            images,
            warnings,
            overallSuccess ? null : "One or more Stellarium scene captures failed."));
    }

    private static bool IsRealCapturedFile(string imagePath)
    {
        return File.Exists(imagePath) && new FileInfo(imagePath).Length > 0;
    }
}
