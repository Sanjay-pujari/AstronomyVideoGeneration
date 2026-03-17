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

        var missingAssets = FindMissingAssets(manifest, plan);
        if (missingAssets.Count > 0)
        {
            var validationError = BuildMissingAssetMessage(missingAssets);
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
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, BuildProcessDiagnostics(result), cancellationToken);

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

    private static List<string> FindMissingAssets(RenderManifest manifest, RenderPlan plan)
    {
        var missingAssets = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.AudioPath) || !File.Exists(manifest.AudioPath))
        {
            missingAssets.Add($"Narration audio missing: '{manifest.AudioPath}'");
        }

        if (plan.Scenes.Count == 0)
        {
            missingAssets.Add("No scene visuals were provided.");
        }

        foreach (var scene in plan.Scenes.Where(scene => string.IsNullOrWhiteSpace(scene.VisualPath) || !File.Exists(scene.VisualPath)))
        {
            missingAssets.Add($"{scene.Segment} visual missing: '{scene.VisualPath}' (caption: '{scene.Caption}')");
        }

        return missingAssets;
    }

    private static string BuildMissingAssetMessage(IReadOnlyCollection<string> missingAssets)
        => string.Join(Environment.NewLine, missingAssets);

    private static string BuildProcessDiagnostics(ProcessExecutionResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"Command: {result.FileName} {result.Arguments}".TrimEnd(),
            $"ExitCode: {result.ExitCode}",
            $"StartedUtc: {result.StartTimeUtc:O}",
            $"EndedUtc: {result.EndTimeUtc:O}",
            $"DurationMs: {result.Duration.TotalMilliseconds:F0}",
            string.IsNullOrWhiteSpace(result.ExceptionText) ? string.Empty : $"Exception: {result.ExceptionText}",
            "--- STDERR ---",
            result.StandardError,
            "--- STDOUT ---",
            result.StandardOutput
        }.Where(static line => !string.IsNullOrEmpty(line)));
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
