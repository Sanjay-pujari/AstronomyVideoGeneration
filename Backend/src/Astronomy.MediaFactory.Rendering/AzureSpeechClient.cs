using System.Security;
using Astronomy.MediaFactory.Contracts;
using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;

namespace Astronomy.MediaFactory.Rendering;

public interface IAzureSpeechClient
{
    Task<byte[]> SynthesizeMp3Async(
        string text,
        AzureSpeechOptions options,
        CancellationToken cancellationToken);
}

public sealed class AzureSpeechClient : IAzureSpeechClient
{
    private static readonly TokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);

    public async Task<byte[]> SynthesizeMp3Async(
        string text,
        AzureSpeechOptions options,
        CancellationToken cancellationToken)
    {
        var speechConfig = options.UseManagedIdentity
            ? await CreateManagedIdentityConfigAsync(options, cancellationToken)
            : CreateSubscriptionConfig(options);

        speechConfig.SpeechSynthesisVoiceName = options.Voice;
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);
        var ssml = BuildSsml(text, options.Voice);
        var result = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(cancellationToken);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            return result.AudioData;
        }

        var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
        throw new InvalidOperationException(
            $"Speech synthesis failed. Reason={result.Reason}, ErrorCode={cancellationDetails.ErrorCode}, Details={cancellationDetails.ErrorDetails}");
    }

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

    private static string BuildSsml(string text, string voice)
    {
        var escapedText = SecurityElement.Escape(text) ?? string.Empty;
        return $"""
                <speak version='1.0' xml:lang='en-US'>
                  <voice name='{voice}'>{escapedText}</voice>
                </speak>
                """;
    }
}
