using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegVideoRenderService : IVideoRenderService
{
    private readonly RenderingOptions _options;
    private readonly RenderManifestBuilder _manifestBuilder;
    private readonly FfmpegArgumentBuilder _argumentBuilder;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FfmpegVideoRenderService> _logger;

    public FfmpegVideoRenderService(
        IOptions<RenderingOptions> options,
        RenderManifestBuilder manifestBuilder,
        FfmpegArgumentBuilder argumentBuilder,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        ILogger<FfmpegVideoRenderService> logger)
    {
        _options = options.Value;
        _manifestBuilder = manifestBuilder;
        _argumentBuilder = argumentBuilder;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
    {
        var outputPath = manifest.OutputPath;
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? _options.WorkingDirectory;
        _fileSystem.CreateDirectory(outputDirectory);

        var plan = _manifestBuilder.Build(manifest);
        var manifestPath = Path.Combine(outputDirectory, "render-manifest.json");
        var concatPath = Path.Combine(outputDirectory, "ffmpeg-input.txt");
        var captionMetadataPath = Path.Combine(outputDirectory, "caption-metadata.json");
        var subtitleScaffoldPath = Path.Combine(outputDirectory, "subtitles.scaffold.srt");
        var commandPath = Path.Combine(outputDirectory, "ffmpeg-command.txt");
        var ffmpegLogPath = Path.Combine(outputDirectory, "ffmpeg.log");

        await _fileSystem.WriteAllTextAsync(manifestPath, plan.ManifestJson, cancellationToken);
        await _fileSystem.WriteAllTextAsync(concatPath, plan.ConcatInputContent, cancellationToken);
        await _fileSystem.WriteAllTextAsync(captionMetadataPath, plan.CaptionMetadataJson, cancellationToken);
        await _fileSystem.WriteAllTextAsync(subtitleScaffoldPath, plan.SubtitleScaffold, cancellationToken);

        if (!ValidateManifest(manifest, plan, out var validationError))
        {
            _logger.LogWarning("Skipping FFmpeg render because input validation failed: {Reason}", validationError);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, validationError, cancellationToken);
            await CreatePlaceholderOutputAsync(outputPath, commandPath, "", cancellationToken);
            return outputPath;
        }

        var arguments = _argumentBuilder.Build(_options, concatPath, manifest.AudioPath, outputPath);
        await _fileSystem.WriteAllTextAsync(commandPath, $"{_options.FfmpegPath} {arguments}", cancellationToken);

        try
        {
            var result = await _processRunner.ExecuteAsync(_options.FfmpegPath, arguments, cancellationToken);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, result.StandardError + Environment.NewLine + result.StandardOutput, cancellationToken);

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogWarning("FFmpeg exited with code {ExitCode}. Creating placeholder output.", result.ExitCode);
                await CreatePlaceholderOutputAsync(outputPath, commandPath, arguments, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg execution failed. Creating placeholder output.");
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, ex.ToString(), cancellationToken);
            await CreatePlaceholderOutputAsync(outputPath, commandPath, arguments, cancellationToken);
        }

        return outputPath;
    }

    private static bool ValidateManifest(RenderManifest manifest, RenderPlan plan, out string reason)
    {
        if (string.IsNullOrWhiteSpace(manifest.AudioPath) || !File.Exists(manifest.AudioPath))
        {
            reason = "Narration audio is missing.";
            return false;
        }

        if (plan.Scenes.Count == 0)
        {
            reason = "No scene visuals were provided.";
            return false;
        }

        var missingVisual = plan.Scenes.FirstOrDefault(scene => string.IsNullOrWhiteSpace(scene.VisualPath) || !File.Exists(scene.VisualPath));
        if (missingVisual is not null)
        {
            reason = $"Scene visual is missing: {missingVisual.VisualPath}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private async Task CreatePlaceholderOutputAsync(string outputPath, string commandPath, string arguments, CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? _options.WorkingDirectory;
        _fileSystem.CreateDirectory(outputDirectory);
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            await _fileSystem.WriteAllTextAsync(commandPath, $"{_options.FfmpegPath} {arguments}", cancellationToken);
        }

        await _fileSystem.WriteAllBytesAsync(outputPath, Array.Empty<byte>(), cancellationToken);
    }
}
