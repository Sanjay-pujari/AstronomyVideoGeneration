using Astronomy.MediaFactory.Contracts;
using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Rendering;

public interface IAzureSpeechClient
{
    Task<byte[]> SynthesizeMp3Async(
        string text,
        AzureSpeechOptions options,
        CancellationToken cancellationToken);
}

public sealed class AzureSpeechClient(ILogger<AzureSpeechClient> logger, ISsmlBuilder ssmlBuilder) : IAzureSpeechClient
{
    private static readonly TokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);

    public async Task<byte[]> SynthesizeMp3Async(string text, AzureSpeechOptions options, CancellationToken cancellationToken)
    {
        var speechConfig = options.UseManagedIdentity
            ? await CreateManagedIdentityConfigAsync(options, cancellationToken)
            : CreateSubscriptionConfig(options);

        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

        var useSsml = options.UseSsml;
        logger.LogInformation("Azure Speech SSML mode is {SsmlMode}", useSsml ? "enabled" : "disabled");

        var voices = options.GetPreferredVoices();
        Exception? lastUnsupportedVoiceException = null;

        foreach (var voice in voices)
        {
            logger.LogInformation("Trying voice: {Voice}", voice);
            try
            {
                var audio = await SynthesizeWithVoiceAsync(text, speechConfig, voice, useSsml, options, cancellationToken);
                logger.LogInformation("Voice succeeded: {Voice}", voice);
                return audio;
            }
            catch (InvalidOperationException ex) when (IsUnsupportedVoice(ex))
            {
                lastUnsupportedVoiceException = ex;
                logger.LogWarning(ex, "Voice failed: {Voice}, trying fallback", voice);
            }
        }

        logger.LogError("All configured Azure Speech voices failed");
        throw new InvalidOperationException("All Azure Speech voices failed", lastUnsupportedVoiceException);
    }

    private async Task<byte[]> SynthesizeWithVoiceAsync(string text, SpeechConfig speechConfig, string voice, bool useSsml, AzureSpeechOptions options, CancellationToken cancellationToken)
    {
        speechConfig.SpeechSynthesisVoiceName = voice;
        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);

        SpeechSynthesisResult result;
        if (useSsml)
        {
            var ssml = ssmlBuilder.BuildSsml(text, voice, rateOverride: options.SsmlRate, pitchOverride: options.SsmlPitch);
            result = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(cancellationToken);
            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                logger.LogWarning("SSML synthesis failed for voice {Voice}. Falling back to plain text.", voice);
                result = await synthesizer.SpeakTextAsync(text).WaitAsync(cancellationToken);
            }
        }
        else
        {
            result = await synthesizer.SpeakTextAsync(text).WaitAsync(cancellationToken);
        }

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            return result.AudioData;
        }

        var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
        throw new InvalidOperationException(
            $"Speech synthesis failed. Reason={result.Reason}, ErrorCode={cancellationDetails.ErrorCode}, Details={cancellationDetails.ErrorDetails}");
    }

    private static bool IsUnsupportedVoice(Exception ex)
        => ex.Message.Contains("Unsupported voice", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("ErrorCode=BadRequest", StringComparison.OrdinalIgnoreCase);

    private static SpeechConfig CreateSubscriptionConfig(AzureSpeechOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
            throw new InvalidOperationException("Azure Speech configuration is missing Key.");

        if (!string.IsNullOrWhiteSpace(options.Region)
            && options.Region.Contains('-', StringComparison.Ordinal)
            && options.Region.Length == 5)
        {
            throw new InvalidOperationException(
                $"Azure Speech Region appears to be a locale ('{options.Region}'). Use an Azure region name such as 'eastus'.");
        }

        if (!string.IsNullOrWhiteSpace(options.Region))
            return SpeechConfig.FromSubscription(options.Key, options.Region);

        if (!string.IsNullOrWhiteSpace(options.Endpoint) && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpointUri))
            return SpeechConfig.FromEndpoint(endpointUri, options.Key);

        throw new InvalidOperationException("Azure Speech configuration is missing Region and/or Endpoint.");
    }

    private static async Task<SpeechConfig> CreateManagedIdentityConfigAsync(AzureSpeechOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.ResourceId))
            throw new InvalidOperationException("Azure Speech managed identity requires Region and ResourceId.");

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim()
        });
        var accessToken = await credential.GetTokenAsync(AzureCognitiveServicesScope, cancellationToken);
        var authorizationToken = $"aad#{options.ResourceId.Trim()}#{accessToken.Token}";
        return SpeechConfig.FromAuthorizationToken(authorizationToken, options.Region);
    }
}
