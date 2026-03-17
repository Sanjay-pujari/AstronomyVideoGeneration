using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.Rendering;
public sealed class AzureSpeechSynthesisService : ISpeechSynthesisService
{
    private readonly AzureSpeechOptions _options;
    public AzureSpeechSynthesisService(IOptions<AzureSpeechOptions> options) { _options = options.Value; }
    public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var textPath = Path.Combine(outputDirectory, "narration.txt");
        var audioPath = Path.Combine(outputDirectory, "narration.mp3");
        await File.WriteAllTextAsync(textPath, script, cancellationToken);
        await File.WriteAllBytesAsync(audioPath, Array.Empty<byte>(), cancellationToken);
        return audioPath;
    }
}
