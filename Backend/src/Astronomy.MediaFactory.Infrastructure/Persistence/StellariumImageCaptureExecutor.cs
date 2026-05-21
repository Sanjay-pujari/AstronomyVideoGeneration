using System.Diagnostics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumImageCaptureExecutor(
    IOptions<StellariumOptions> options,
    IStellariumScriptGenerator scriptGenerator,
    ILogger<StellariumImageCaptureExecutor> logger) : IStellariumImageCaptureExecutor
{
    private readonly StellariumOptions _options = options.Value;
    private const string DisabledMessage = "Stellarium capture is disabled in configuration.";

    public async Task<StellariumCaptureExecutionResponse> CaptureAsync(StellariumSceneCapturePlan scenePlan, StellariumCaptureExecutionRequest request, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var images = new List<StellariumCapturedImageResult>();
        var outputFolder = BuildCaptureFolder(request.ContentGenerationPlanId, warnings);
        var scriptsFolder = BuildScriptsFolder(request.ContentGenerationPlanId);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(scriptsFolder);
        }

        foreach (var scene in scenePlan.Scenes.OrderBy(x => x.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = $"{scene.SortOrder:D2}_{scene.SceneCode}_{scene.OutputImageRole}.png";
            var imagePath = Path.Combine(outputFolder, fileName);
            var scriptPath = Path.Combine(scriptsFolder, $"{scene.SortOrder:D2}_{scene.SceneCode}.ssc");
            string? commandLine = null;
            int? exitCode = null;
            string? stdOut = null;
            string? stdErr = null;
            string? error = null;

            if (request.DryRun)
            {
                await scriptGenerator.GenerateScriptAsync(scene, scenePlan, imagePath, scriptPath, cancellationToken);
            }
            else if (!_options.Enabled)
            {
                error = DisabledMessage;
                warnings.Add(DisabledMessage);
            }
            else if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath))
            {
                error = $"Stellarium executable was not found at '{_options.ExecutablePath}'.";
                warnings.Add(error);
            }
            else
            {
                await scriptGenerator.GenerateScriptAsync(scene, scenePlan, imagePath, scriptPath, cancellationToken);
                if (request.OverwriteExisting && File.Exists(imagePath)) File.Delete(imagePath);

                var psi = new ProcessStartInfo
                {
                    FileName = _options.ExecutablePath,
                    Arguments = $"--startup-script \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                commandLine = $"\"{psi.FileName}\" {psi.Arguments}";

                using var process = new Process { StartInfo = psi };
                process.Start();
                var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errTask = process.StandardError.ReadToEndAsync(cancellationToken);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, _options.CaptureTimeoutSeconds)));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await process.WaitForExitAsync(linked.Token);
                exitCode = process.ExitCode;
                stdOut = await outTask;
                stdErr = await errTask;

                await WaitForCaptureWriteAsync(imagePath, cancellationToken);
                if (!IsRealCapturedFile(imagePath))
                {
                    error = "Capture command completed but output file was not created.";
                }
            }

            var success = request.DryRun || (error is null && IsRealCapturedFile(imagePath));
            images.Add(new StellariumCapturedImageResult(scene.SceneCode, scene.SceneType, scene.OutputImageRole, scene.TargetObjectCode, scene.CaptureTimeUtc, imagePath, success, error, scriptPath, commandLine, exitCode, request.Diagnostics ? stdOut : null, request.Diagnostics ? stdErr : null));
        }

        var capturedCount = images.Count(x => x.Success && !request.DryRun);
        var requestedCount = images.Count;
        var overallSuccess = request.DryRun || capturedCount == requestedCount;
        return new StellariumCaptureExecutionResponse(request.ContentGenerationPlanId, overallSuccess, requestedCount, capturedCount, outputFolder, images, warnings.Distinct().ToList(), overallSuccess ? null : "One or more Stellarium scene captures failed.");
    }

    public Task<StellariumCaptureDiagnosticsResponse> GetDiagnosticsAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captureFolder = BuildCaptureFolder(contentGenerationPlanId, new List<string>());
        var canStart = _options.Enabled && !string.IsNullOrWhiteSpace(_options.ExecutablePath) && File.Exists(_options.ExecutablePath);
        return Task.FromResult(new StellariumCaptureDiagnosticsResponse(contentGenerationPlanId, _options.Enabled, _options.ExecutablePath, !string.IsNullOrWhiteSpace(_options.ExecutablePath) && File.Exists(_options.ExecutablePath), _options.ScriptsDirectory, !string.IsNullOrWhiteSpace(_options.ScriptsDirectory) && Directory.Exists(_options.ScriptsDirectory), _options.CaptureDirectory, !string.IsNullOrWhiteSpace(_options.CaptureDirectory) && Directory.Exists(_options.CaptureDirectory), _options.CaptureTimeoutSeconds, captureFolder, canStart));
    }

    private string BuildCaptureFolder(Guid planId, List<string> warnings)
    {
        var root = _options.CaptureDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = string.IsNullOrWhiteSpace(_options.OutputRoot) ? "outputs" : _options.OutputRoot;
            warnings.Add("Stellarium:CaptureDirectory is not configured; fallback output path used.");
            return Path.Combine(root, planId.ToString(), "stellarium-scenes");
        }

        return Path.Combine(root, "content-plans", planId.ToString(), "stellarium-scenes");
    }

    private string BuildScriptsFolder(Guid planId)
    {
        var root = string.IsNullOrWhiteSpace(_options.ScriptsDirectory)
            ? Path.Combine(string.IsNullOrWhiteSpace(_options.OutputRoot) ? "outputs" : _options.OutputRoot, "stellarium-scripts")
            : _options.ScriptsDirectory;
        return Path.Combine(root, "content-plans", planId.ToString());
    }

    private static bool IsRealCapturedFile(string imagePath) => File.Exists(imagePath) && new FileInfo(imagePath).Length > 0;

    private static async Task WaitForCaptureWriteAsync(string imagePath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (IsRealCapturedFile(imagePath)) return;
            await Task.Delay(250, cancellationToken);
        }
    }
}
