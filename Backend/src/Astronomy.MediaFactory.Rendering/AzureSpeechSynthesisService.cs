using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class AzureSpeechSynthesisService : ISpeechSynthesisService
{
    private readonly AzureSpeechOptions _options;
    private readonly IAzureSpeechClient _speechClient;
    private readonly ILogger<AzureSpeechSynthesisService> _logger;

    public AzureSpeechSynthesisService(
        IOptions<AzureSpeechOptions> options,
        IAzureSpeechClient speechClient,
        ILogger<AzureSpeechSynthesisService> logger)
    {
        _options = options.Value;
        _speechClient = speechClient;
        _logger = logger;
    }

    public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var textPath = Path.Combine(outputDirectory, "narration.txt");
        var audioPath = Path.Combine(outputDirectory, "narration.mp3");

        await File.WriteAllTextAsync(textPath, script, cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(_options.Key) || string.IsNullOrWhiteSpace(_options.Region))
            {
                throw new InvalidOperationException("Azure Speech configuration is missing Key and/or Region.");
            }

            var audioBytes = await _speechClient.SynthesizeMp3Async(
                script,
                _options.Key,
                _options.Region,
                _options.Voice,
                cancellationToken);

            await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
            return audioPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Speech synthesis failed. Falling back to placeholder narration audio.");
            await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>(), cancellationToken);
            return audioPath;
        }
    }
}
