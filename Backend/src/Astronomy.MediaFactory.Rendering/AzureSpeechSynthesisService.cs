using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class AzureSpeechSynthesisService : ISpeechSynthesisService
{
    private readonly AzureSpeechOptions _options;
    private readonly IAzureSpeechClient _speechClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AzureSpeechSynthesisService> _logger;

    public AzureSpeechSynthesisService(
        IOptions<AzureSpeechOptions> options,
        IAzureSpeechClient speechClient,
        IFileSystem fileSystem,
        ILogger<AzureSpeechSynthesisService> logger)
    {
        _options = options.Value;
        _speechClient = speechClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
    {
        _fileSystem.CreateDirectory(outputDirectory);

        var textPath = Path.Combine(outputDirectory, "narration.txt");
        var audioPath = Path.Combine(outputDirectory, "narration.mp3");

        await _fileSystem.WriteAllTextAsync(textPath, script, cancellationToken);

        try
        {
            EnsureSpeechConfigurationIsUsable();

            var audioBytes = await SynthesizeWithFallbackAsync(script, cancellationToken);

            await _fileSystem.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
            return audioPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Speech synthesis failed. Narration audio was not created.");
            throw new InvalidOperationException("Narration audio generation failed.", ex);
        }
    }

    private async Task<byte[]> SynthesizeWithFallbackAsync(string script, CancellationToken cancellationToken)
    {
        try
        {
            return await _speechClient.SynthesizeMp3Async(script, _options, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ShouldFallbackToAlternateVoice(ex))
        {
            var fallbackVoice = _options.FallbackVoice.Trim();
            _logger.LogWarning(
                ex,
                "Azure Speech voice '{PrimaryVoice}' is unsupported. Retrying narration synthesis with fallback voice '{FallbackVoice}'.",
                _options.Voice,
                fallbackVoice);

            var fallbackOptions = CloneOptionsWithVoice(fallbackVoice);
            return await _speechClient.SynthesizeMp3Async(script, fallbackOptions, cancellationToken);
        }
    }

    private bool ShouldFallbackToAlternateVoice(InvalidOperationException ex)
    {
        if (string.IsNullOrWhiteSpace(_options.FallbackVoice)
            || string.Equals(_options.Voice, _options.FallbackVoice, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ex.Message.Contains("Unsupported voice", StringComparison.OrdinalIgnoreCase);
    }

    private AzureSpeechOptions CloneOptionsWithVoice(string voice) => new()
    {
        Key = _options.Key,
        Region = _options.Region,
        Endpoint = _options.Endpoint,
        UseManagedIdentity = _options.UseManagedIdentity,
        ResourceId = _options.ResourceId,
        ManagedIdentityClientId = _options.ManagedIdentityClientId,
        Voice = voice,
        FallbackVoice = _options.FallbackVoice
    };

    private void EnsureSpeechConfigurationIsUsable()
    {
        if (_options.UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(_options.Region) || string.IsNullOrWhiteSpace(_options.ResourceId))
                throw new InvalidOperationException("Azure Speech managed identity requires Region and ResourceId.");

            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Key))
            throw new InvalidOperationException("Azure Speech configuration is missing Key.");

        if (string.IsNullOrWhiteSpace(_options.Region) && string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException("Azure Speech configuration is missing Region and/or Endpoint.");
    }
}
