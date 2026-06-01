using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;
using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class WeeklySkyForecastAzureTtsSynthesizer(IOptions<AzureSpeechOptions> options) : IWeeklySkyForecastTtsSynthesizer
{
    private static readonly TokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);
    private readonly AzureSpeechOptions _options = options.Value;

    public async Task SynthesizeSsmlToFileAsync(string ssml, string outputPath, string voiceName, string audioFormat, CancellationToken cancellationToken)
    {
        if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported WeeklySkyForecast audio format '{audioFormat}'. Phase 6.3 supports mp3 only.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var speechConfig = _options.UseManagedIdentity
            ? await CreateManagedIdentityConfigAsync(_options, cancellationToken)
            : CreateSubscriptionConfig(_options);
        speechConfig.SpeechSynthesisVoiceName = voiceName;
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3);

        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);
        using var result = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(cancellationToken);
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            throw new InvalidOperationException($"WeeklySkyForecast Azure Speech synthesis failed. Reason={result.Reason}, ErrorCode={cancellation.ErrorCode}, Details={cancellation.ErrorDetails}");
        }

        await File.WriteAllBytesAsync(outputPath, result.AudioData, cancellationToken);
    }

    private static SpeechConfig CreateSubscriptionConfig(AzureSpeechOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key)) throw new InvalidOperationException("Azure Speech configuration is missing Key.");
        if (!string.IsNullOrWhiteSpace(options.Region)) return SpeechConfig.FromSubscription(options.Key, options.Region);
        if (!string.IsNullOrWhiteSpace(options.Endpoint) && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpointUri)) return SpeechConfig.FromEndpoint(endpointUri, options.Key);
        throw new InvalidOperationException("Azure Speech configuration is missing Region and/or Endpoint.");
    }

    private static async Task<SpeechConfig> CreateManagedIdentityConfigAsync(AzureSpeechOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.ResourceId))
        {
            throw new InvalidOperationException("Azure Speech managed identity requires Region and ResourceId.");
        }

        var credential = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ManagedIdentityClientId });
        var token = await credential.GetTokenAsync(AzureCognitiveServicesScope, cancellationToken);
        var speechConfig = SpeechConfig.FromAuthorizationToken($"aad#{options.ResourceId}#{token.Token}", options.Region);
        speechConfig.SetProperty(PropertyId.SpeechServiceAuthorization_Token, $"aad#{options.ResourceId}#{token.Token}");
        return speechConfig;
    }
}
